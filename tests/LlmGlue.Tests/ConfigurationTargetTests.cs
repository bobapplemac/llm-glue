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
            using var json = await ParseJsoncAsync(path);
            Assert.Equal("keep-me", json.RootElement.GetProperty("username").GetString());
            Assert.True(json.RootElement.TryGetProperty("mcp", out _));
            var providers = json.RootElement.GetProperty("providers");
            Assert.True(providers.TryGetProperty("manual", out _));
            Assert.True(providers.TryGetProperty("llm-glue-llama-cpp-lab", out _));
            Assert.True(providers.TryGetProperty("llm-glue-stale", out _));

            var empty = await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []), TestContext.Current.CancellationToken);
            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator()).ApplyAsync(empty, TestContext.Current.CancellationToken);
            using var reconciled = await ParseJsoncAsync(path);
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
            await File.WriteAllTextAsync(path, """
                {
                  "disabled_providers": [
                    // The operator disabled this provider.
                    "manual"
                  ],
                  "providers": {}
                }
                """, TestContext.Current.CancellationToken);
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
            var appliedText = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("// The operator disabled this provider.", appliedText);
            Assert.Contains("// Managed by LLM Glue.", appliedText);
            using (var applied = await ParseJsoncAsync(path))
                Assert.Equal(["manual", "ollama"], applied.RootElement.GetProperty("disabled_providers").EnumerateArray().Select(x => x.GetString()!).ToArray());
            var ownershipPath = path + ".llm-glue-ownership.json";
            File.SetLastWriteTimeUtc(ownershipPath, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            var ownershipWriteTime = File.GetLastWriteTimeUtc(ownershipPath);
            await target.ApplyAsync(generated, TestContext.Current.CancellationToken);
            Assert.Equal(ownershipWriteTime, File.GetLastWriteTimeUtc(ownershipPath));

            var empty = await new OpenCodeGenerator().GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch, []), TestContext.Current.CancellationToken);
            await target.ApplyAsync(empty, TestContext.Current.CancellationToken);
            Assert.Contains("// The operator disabled this provider.", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            using var reconciled = await ParseJsoncAsync(path);
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
            using var reconciled = await ParseJsoncAsync(path);
            Assert.Equal("ollama", Assert.Single(reconciled.RootElement.GetProperty("disabled_providers").EnumerateArray()).GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodeAddsAndReversesOnlyItsOwnedBuiltInProviderPolicy()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.jsonc");
            await File.WriteAllTextAsync(path, """
                {
                  "policies": [
                    // Operator policy remains.
                    { "action": "provider.use", "resource": "openai", "effect": "deny" }
                  ],
                  "providers": {}
                }
                """, TestContext.Current.CancellationToken);
            var artifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            var enabled = new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:test", true);
            await enabled.ApplyAsync(artifact, TestContext.Current.CancellationToken);

            var appliedText = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("// Operator policy remains.", appliedText);
            Assert.Contains("// Managed by LLM Glue.", appliedText);
            using (var applied = await ParseJsoncAsync(path))
            {
                var policies = applied.RootElement.GetProperty("policies").EnumerateArray().ToArray();
                Assert.Equal(2, policies.Length);
                Assert.Contains(policies, IsBuiltInOpenCodeDenyPolicy);
            }
            using (var ownership = JsonDocument.Parse(await File.ReadAllBytesAsync(path + ".llm-glue-ownership.json", TestContext.Current.CancellationToken)))
            {
                Assert.Equal(3, ownership.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal("provider.use:opencode:deny", Assert.Single(ownership.RootElement.GetProperty("controllers").GetProperty("source:test").GetProperty("managedPolicies").EnumerateArray()).GetString());
            }

            var disabled = new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:test", false);
            await disabled.ApplyAsync(artifact, TestContext.Current.CancellationToken);
            var reconciledText = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("// Operator policy remains.", reconciledText);
            using var reconciled = await ParseJsoncAsync(path);
            var remaining = Assert.Single(reconciled.RootElement.GetProperty("policies").EnumerateArray());
            Assert.Equal("openai", remaining.GetProperty("resource").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodeDoesNotClaimOrRemoveAnIdenticalOperatorPolicy()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.jsonc");
            await File.WriteAllTextAsync(path, """
                {
                  "policies": [
                    // Operator owns this exact policy.
                    { "effect": "deny", "resource": "opencode", "action": "provider.use" }
                  ],
                  "providers": {}
                }
                """, TestContext.Current.CancellationToken);
            var artifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:test", true).ApplyAsync(artifact, TestContext.Current.CancellationToken);
            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:test", false).ApplyAsync(artifact, TestContext.Current.CancellationToken);

            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("// Operator owns this exact policy.", text);
            using var result = await ParseJsoncAsync(path);
            Assert.True(IsBuiltInOpenCodeDenyPolicy(Assert.Single(result.RootElement.GetProperty("policies").EnumerateArray())));
            using var ownership = JsonDocument.Parse(await File.ReadAllBytesAsync(path + ".llm-glue-ownership.json", TestContext.Current.CancellationToken));
            Assert.Empty(ownership.RootElement.GetProperty("controllers").GetProperty("source:test").GetProperty("managedPolicies").EnumerateArray());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodeOwnedDenyWinsAfterALaterOperatorAllowAndReversesCleanly()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.jsonc");
            const string original = """
                {
                  "policies": [
                    { "action": "provider.use", "resource": "opencode", "effect": "deny" },
                    { "action": "provider.use", "resource": "*", "effect": "allow" }
                  ],
                  "providers": {}
                }
                """;
            await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
            var artifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:test", true).ApplyAsync(artifact, TestContext.Current.CancellationToken);

            using (var applied = await ParseJsoncAsync(path))
            {
                var policies = applied.RootElement.GetProperty("policies").EnumerateArray().ToArray();
                Assert.Equal(3, policies.Length);
                Assert.True(IsBuiltInOpenCodeDenyPolicy(policies[^1]));
            }

            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:test", false).ApplyAsync(artifact, TestContext.Current.CancellationToken);
            using var reconciled = await ParseJsoncAsync(path);
            var remaining = reconciled.RootElement.GetProperty("policies").EnumerateArray().ToArray();
            Assert.Equal(2, remaining.Length);
            Assert.Equal("deny", remaining[0].GetProperty("effect").GetString());
            Assert.Equal("allow", remaining[1].GetProperty("effect").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodeDisablePreservesAnOperatorDuplicateAddedAfterManagedPolicy()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.jsonc");
            var artifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:test", true).ApplyAsync(artifact, TestContext.Current.CancellationToken);
            var providers = (await ParseJsoncAsync(path)).RootElement.GetProperty("providers").GetRawText();
            await File.WriteAllTextAsync(path, $$"""
                {
                  "policies": [
                    // Managed by LLM Glue. Do not edit this section; ownership is tracked in the adjacent sidecar.
                    { "action": "provider.use", "resource": "opencode", "effect": "deny" },
                    // Operator duplicate remains.
                    { "effect": "deny", "resource": "opencode", "action": "provider.use" }
                  ],
                  "providers": {{providers}}
                }
                """, TestContext.Current.CancellationToken);

            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:test", false).ApplyAsync(artifact, TestContext.Current.CancellationToken);

            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("// Operator duplicate remains.", text);
            Assert.DoesNotContain("// Managed by LLM Glue. Do not edit this section; ownership is tracked in the adjacent sidecar.\n    { \"action\": \"provider.use\"", text);
            using var result = await ParseJsoncAsync(path);
            Assert.True(IsBuiltInOpenCodeDenyPolicy(Assert.Single(result.RootElement.GetProperty("policies").EnumerateArray())));
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
            using var restored = await ParseJsoncAsync(path);
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
            using (var applied = await ParseJsoncAsync(path))
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
            using var reconciled = await ParseJsoncAsync(path);
            var finalProviders = reconciled.RootElement.GetProperty("providers");
            Assert.True(finalProviders.TryGetProperty("llm-glue-ollama-local", out _));
            Assert.False(finalProviders.TryGetProperty("llm-glue-llama-cpp-lab", out _));
            Assert.Equal("ollama", Assert.Single(reconciled.RootElement.GetProperty("disabled_providers").EnumerateArray()).GetString());
            using var ownership = JsonDocument.Parse(await File.ReadAllBytesAsync(path + ".llm-glue-ownership.json", TestContext.Current.CancellationToken));
            Assert.Equal(3, ownership.RootElement.GetProperty("schemaVersion").GetInt32());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpenCodeMigratesV2OwnershipBeforeAddingManagedPolicy()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.jsonc");
            var artifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(path, """
                {
                  "providers": {
                    "llm-glue-llama-cpp-lab": { "name": "old managed value", "models": {} }
                  }
                }
                """, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(path + ".llm-glue-ownership.json", """
                {
                  "schemaVersion": 2,
                  "controllers": {
                    "source:test": {
                      "providers": ["llm-glue-llama-cpp-lab"],
                      "disabledProviders": []
                    }
                  }
                }
                """, TestContext.Current.CancellationToken);

            await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:test", true)
                .ApplyAsync(artifact, TestContext.Current.CancellationToken);

            using var ownership = JsonDocument.Parse(await File.ReadAllBytesAsync(path + ".llm-glue-ownership.json", TestContext.Current.CancellationToken));
            Assert.Equal(3, ownership.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("provider.use:opencode:deny", Assert.Single(ownership.RootElement.GetProperty("controllers").GetProperty("source:test").GetProperty("managedPolicies").EnumerateArray()).GetString());
            using var applied = await ParseJsoncAsync(path);
            Assert.Contains(applied.RootElement.GetProperty("policies").EnumerateArray(), IsBuiltInOpenCodeDenyPolicy);
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
    public async Task OpenCodePreservesOperatorCommentsAndMarksManagedSections()
    {
        var directory = AtomicFileInstallerTests.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "opencode.jsonc");
            await File.WriteAllTextAsync(path, """
                // Operator header stays here.
                {
                  // This explains the user setting.
                  "username": "keep-me", // Inline operator comment.
                  "temperature": 1/* Inline block comment. */,
                  "providers": {
                    /* This provider is maintained manually. */
                    "manual": { "name": "Manual", "models": {} },
                  }
                }
                """, TestContext.Current.CancellationToken);
            var target = new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator());
            var artifact = await new OpenCodeGenerator().GenerateAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken);

            await target.ApplyAsync(artifact, TestContext.Current.CancellationToken);
            var first = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            await target.ApplyAsync(artifact, TestContext.Current.CancellationToken);
            var second = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

            Assert.Contains("// Operator header stays here.", first);
            Assert.Contains("// This explains the user setting.", first);
            Assert.Contains("// Inline operator comment.", first);
            Assert.Contains("/* Inline block comment. */", first);
            Assert.Contains("/* This provider is maintained manually. */", first);
            Assert.Contains("// Managed by LLM Glue.", first);
            Assert.Equal(first, second);
            using var parsed = await ParseJsoncAsync(path);
            Assert.True(parsed.RootElement.GetProperty("providers").TryGetProperty("manual", out _));
            Assert.True(parsed.RootElement.GetProperty("providers").TryGetProperty("llm-glue-llama-cpp-lab", out _));
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

    private static bool IsBuiltInOpenCodeDenyPolicy(JsonElement policy) => policy.GetProperty("action").GetString() == "provider.use"
        && policy.GetProperty("resource").GetString() == "opencode"
        && policy.GetProperty("effect").GetString() == "deny";

    private static async Task<JsonDocument> ParseJsoncAsync(string path) => JsonDocument.Parse(
        await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken),
        new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
}
