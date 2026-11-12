using System.Text;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Client;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Generation;
using LlmGlue.Operations;

namespace LlmGlue.Tests;

public sealed class UninstallManagerTests
{
    [Fact]
    public async Task ReconcilesOwnedOpenCodeAndEmptiesLiteLlm()
    {
        var root = Path.Combine(Path.GetTempPath(), $"llm-glue-uninstall-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var openCode = Path.Combine(root, "opencode.jsonc");
            var primary = Path.Combine(root, "litellm.yaml");
            var generated = Path.Combine(root, "generated.yaml");
            await File.WriteAllTextAsync(openCode, "{\"theme\":\"dark\"}", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(primary, "include:\n  - generated.yaml\n", TestContext.Current.CancellationToken);
            var config = Path.Combine(root, "llm-glue.toml");
            await File.WriteAllTextAsync(config, $$"""
                [roles]
                client = true
                [client]
                source = "https://catalog.example:8765"
                token = "read"
                [[client.targets]]
                type = "opencode"
                path = "{{openCode.Replace("\\", "\\\\")}}"
                [[client.targets]]
                type = "litellm"
                path = "{{generated.Replace("\\", "\\\\")}}"
                primary_config_path = "{{primary.Replace("\\", "\\\\")}}"
                service_name = "test"
                """, TestContext.Current.CancellationToken);
            var options = TomlConfigLoader.Load(config);
            var artifact = new OpenCodeGenerator(new("http://inference.example:4000"), "key").GenerateAsync(
                LlmGlue.Core.Domain.DiscoverySnapshot.Create(DateTimeOffset.UtcNow, []), TestContext.Current.CancellationToken);
            await new OpenCodeTarget(openCode, new AtomicFileInstaller(), new NoOpTargetActivator(), "source:https://catalog.example:8765", true)
                .ApplyAsync(await artifact, TestContext.Current.CancellationToken);

            await UninstallManager.CleanupTargetsAsync(options, false, _ => new NoOpTargetActivator(), TestContext.Current.CancellationToken);

            var json = await File.ReadAllTextAsync(openCode, TestContext.Current.CancellationToken);
            Assert.Contains("\"theme\":\"dark\"", json);
            Assert.DoesNotContain("provider.use", json);
            var yaml = await File.ReadAllTextAsync(generated, TestContext.Current.CancellationToken);
            Assert.Contains("model_list:", yaml);
            Assert.DoesNotContain("model_name:", yaml);
        }
        finally { Directory.Delete(root, true); }
    }
}
