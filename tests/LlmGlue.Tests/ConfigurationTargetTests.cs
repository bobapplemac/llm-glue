using System.Text;
using System.Text.Json;
using LlmGlue.Core.Client;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;

namespace LlmGlue.Tests;

public sealed class ConfigurationTargetTests
{
    [Fact]
    public async Task OpenCodePreservesUnownedSettingsAndOnlyReconcilesTrackedProviders()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.json");
            await File.WriteAllTextAsync(path, """
                { "username": "keep-me", "mcp": { "servers": {} }, "providers": {
                  "manual": { "name": "Manual", "models": {} },
                  "llm-glue-stale": { "name": "Stale", "models": {} }
                } }
                """, TestContext.Current.CancellationToken);
            var artifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator()).ApplyAsync(artifact, TestContext.Current.CancellationToken);
            using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal("keep-me", json.RootElement.GetProperty("username").GetString());
            Assert.True(json.RootElement.TryGetProperty("mcp", out _));
            var providers = json.RootElement.GetProperty("providers");
            Assert.True(providers.TryGetProperty("manual", out _));
            Assert.True(providers.TryGetProperty("llm-glue-llama-cpp-lab", out _));
            Assert.True(providers.TryGetProperty("llm-glue-stale", out _));

            var empty = await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []), TestContext.Current.CancellationToken);
            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator()).ApplyAsync(empty, TestContext.Current.CancellationToken);
            using var reconciled = JsonDocument.Parse(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.True(reconciled.RootElement.GetProperty("providers").TryGetProperty("llm-glue-stale", out _));
            Assert.False(reconciled.RootElement.GetProperty("providers").TryGetProperty("llm-glue-llama-cpp-lab", out _));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodeReconcilesOnlyOwnedNativeProviderDisablement()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.json");
            await File.WriteAllTextAsync(path, """{ "disabled_providers": ["manual"], "providers": {} }""", TestContext.Current.CancellationToken);
            var ollama = GenerationTests.Qualified() with
            {
                Identity = GenerationTests.Qualified().Identity with
                {
                    ProviderId = "ollama",
                    InstanceId = "windows",
                    SourceEndpoint = "http://127.0.0.1:11434"
                }
            };
            var target = new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator());
            var generated = await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [ollama]), TestContext.Current.CancellationToken);
            await target.ApplyAsync(generated, TestContext.Current.CancellationToken);
            using (var applied = JsonDocument.Parse(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)))
                Assert.Equal(["manual", "ollama"], applied.RootElement.GetProperty("disabled_providers").EnumerateArray().Select(x => x.GetString()!).ToArray());
            var ownershipPath = path + ".llm-glue-ownership.json";
            File.SetLastWriteTimeUtc(ownershipPath, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            var ownershipWriteTime = File.GetLastWriteTimeUtc(ownershipPath);
            await target.ApplyAsync(generated, TestContext.Current.CancellationToken);
            Assert.Equal(ownershipWriteTime, File.GetLastWriteTimeUtc(ownershipPath));

            var empty = await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []), TestContext.Current.CancellationToken);
            await target.ApplyAsync(empty, TestContext.Current.CancellationToken);
            using var reconciled = JsonDocument.Parse(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal("manual", Assert.Single(reconciled.RootElement.GetProperty("disabled_providers").EnumerateArray()).GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodePreservesPreexistingNativeProviderDisablement()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.json");
            await File.WriteAllTextAsync(path, """{ "disabled_providers": ["ollama"], "providers": {} }""", TestContext.Current.CancellationToken);
            var ollama = GenerationTests.Qualified() with
            {
                Identity = GenerationTests.Qualified().Identity with
                {
                    ProviderId = "ollama",
                    InstanceId = "windows",
                    SourceEndpoint = "http://127.0.0.1:11434"
                }
            };
            var target = new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator());
            await target.ApplyAsync(await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [ollama]), TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
            await target.ApplyAsync(await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []), TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
            using var reconciled = JsonDocument.Parse(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal("ollama", Assert.Single(reconciled.RootElement.GetProperty("disabled_providers").EnumerateArray()).GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodeRestoresOwnershipStateWhenActivationRollsBack()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.json");
            await File.WriteAllTextAsync(path, """{ "providers": {} }""", TestContext.Current.CancellationToken);
            var ollama = GenerationTests.Qualified() with
            {
                Identity = GenerationTests.Qualified().Identity with
                {
                    ProviderId = "ollama",
                    InstanceId = "windows",
                    SourceEndpoint = "http://127.0.0.1:11434"
                }
            };
            var artifact = await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [ollama]), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenCodeTarget(path, new AtomicFileInstaller(), new FailOnceActivator()).ApplyAsync(artifact, TestContext.Current.CancellationToken));
            using var ownership = JsonDocument.Parse(await File.ReadAllBytesAsync(path + ".llm-glue-ownership.json", TestContext.Current.CancellationToken));
            Assert.Empty(ownership.RootElement.GetProperty("controllers").EnumerateObject());
            using var restored = JsonDocument.Parse(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.False(restored.RootElement.TryGetProperty("disabled_providers", out _));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodeMigratesV1OwnershipAndKeepsIndependentArtifactSources()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.json");
            await File.WriteAllTextAsync(path, """
                { "disabled_providers": ["ollama"], "providers": {
                  "llm-glue-ollama-local": { "name": "Local Ollama", "models": {} }
                } }
                """, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(path + ".llm-glue-ownership.json", """
                { "schemaVersion": 1, "disabledProviders": ["ollama"] }
                """, TestContext.Current.CancellationToken);

            var centralArtifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            var central = new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:http://central:8765");
            await central.ApplyAsync(centralArtifact, TestContext.Current.CancellationToken);
            using (var applied = JsonDocument.Parse(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)))
            {
                var providers = applied.RootElement.GetProperty("providers");
                Assert.True(providers.TryGetProperty("llm-glue-ollama-local", out _));
                Assert.True(providers.TryGetProperty("llm-glue-llama-cpp-lab", out _));
                Assert.Equal("ollama", Assert.Single(applied.RootElement.GetProperty("disabled_providers").EnumerateArray()).GetString());
            }

            var ollama = GenerationTests.Qualified() with
            {
                Identity = GenerationTests.Qualified().Identity with
                {
                    ProviderId = "ollama",
                    InstanceId = "local",
                    SourceEndpoint = "http://127.0.0.1:11434"
                }
            };
            var localArtifact = await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, [ollama]), TestContext.Current.CancellationToken);
            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:http://127.0.0.1:8765")
                .ApplyAsync(localArtifact, TestContext.Current.CancellationToken);

            var emptyCentral = await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []), TestContext.Current.CancellationToken);
            await central.ApplyAsync(emptyCentral, TestContext.Current.CancellationToken);
            using var reconciled = JsonDocument.Parse(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            var finalProviders = reconciled.RootElement.GetProperty("providers");
            Assert.True(finalProviders.TryGetProperty("llm-glue-ollama-local", out _));
            Assert.False(finalProviders.TryGetProperty("llm-glue-llama-cpp-lab", out _));
            Assert.Equal("ollama", Assert.Single(reconciled.RootElement.GetProperty("disabled_providers").EnumerateArray()).GetString());
            using var ownership = JsonDocument.Parse(await File.ReadAllBytesAsync(path + ".llm-glue-ownership.json", TestContext.Current.CancellationToken));
            Assert.Equal(2, ownership.RootElement.GetProperty("schemaVersion").GetInt32());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodeRejectsCollisionWithUnownedGeneratedProviderKey()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.json");
            const string original = "{ \"providers\": { \"llm-glue-llama-cpp-lab\": { \"name\": \"operator-owned\", \"models\": {} } } }";
            await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
            var artifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:http://server")
                    .ApplyAsync(artifact, TestContext.Current.CancellationToken));

            Assert.Contains("without ownership", error.Message);
            Assert.Equal(original, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(path + ".llm-glue-ownership.json"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("{ \"providers\": [] }")]
    [InlineData("{ \"providers\": {}, \"disabled_providers\": \"ollama\" }")]
    public async Task OpenCodeRejectsInvalidExistingOwnershipShapesWithoutWriting(string original)
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.json");
            await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
            var artifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator()).ApplyAsync(artifact, TestContext.Current.CancellationToken));
            Assert.Equal(original, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(path + ".llm-glue-ownership.json"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LiteLlmRequiresIncludeAndPreservesPrimaryFile()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var primary = Path.Combine(directory, "config.yaml");
            var generated = Path.Combine(directory, "llm-glue-models.yaml");
            const string primaryText = "include:\n  - llm-glue-models.yaml\ngeneral_settings:\n  master_key: os.environ/MASTER_KEY\nmodel_list:\n  - model_name: manual\n    litellm_params:\n      model: openai/manual\n";
            await File.WriteAllTextAsync(primary, primaryText, TestContext.Current.CancellationToken);
            var artifact = await new LiteLlmGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            await new LiteLlmTarget(generated, primary, new AtomicFileInstaller(), new NoOpTargetActivator()).ApplyAsync(artifact, TestContext.Current.CancellationToken);
            Assert.Equal(primaryText, await File.ReadAllTextAsync(primary, TestContext.Current.CancellationToken));
            Assert.Contains("llama-cpp-lab-qwen-model", await File.ReadAllTextAsync(generated, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LiteLlmRejectsMissingIncludeAndManualAliasCollision()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var primary = Path.Combine(directory, "config.yaml");
            var generated = Path.Combine(directory, "llm-glue-models.yaml");
            var artifact = await new LiteLlmGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(primary, "model_list: []\n", TestContext.Current.CancellationToken);
            var target = new LiteLlmTarget(generated, primary, new AtomicFileInstaller(), new NoOpTargetActivator());
            Assert.Contains("does not include", (await Assert.ThrowsAsync<InvalidOperationException>(() => target.ApplyAsync(artifact, TestContext.Current.CancellationToken))).Message);
            await File.WriteAllTextAsync(primary, "include: [llm-glue-models.yaml]\nmodel_list:\n  - model_name: llama-cpp-lab-qwen-model\n", TestContext.Current.CancellationToken);
            Assert.Contains("alias collision", (await Assert.ThrowsAsync<InvalidOperationException>(() => target.ApplyAsync(artifact, TestContext.Current.CancellationToken))).Message);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class FailOnceActivator : ITargetActivator
    {
        private int calls;
        public Task ActivateAndVerifyAsync(string managedPath, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("test activation failure");
            return Task.CompletedTask;
        }
    }
}
