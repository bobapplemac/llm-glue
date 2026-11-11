using System.Text;
using System.Text.Json;
using LlmGlue.Core.Client;
using LlmGlue.Core.Generation;

namespace LlmGlue.Tests;

public sealed class ConfigurationTargetTests
{
    [Fact]
    public async Task OpenCodePreservesUnownedSettingsAndReconcilesOwnedProviders()
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
            Assert.False(providers.TryGetProperty("llm-glue-stale", out _));
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
}
