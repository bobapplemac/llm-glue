using LlmGlue.Core.Configuration;
using LlmGlue.Operations;

namespace LlmGlue.Tests;

public sealed class SetupConfigurationTests
{
    [Fact]
    public void RendersEveryRoleWithMultipleIntegrations()
    {
        var text = SetupConfiguration.Render(new(
            new(true, true, true),
            [new("ollama", "local-ollama", new("http://127.0.0.1:11434")), new("llama.cpp", "local-llama", new("http://127.0.0.1:8081"), "/models")],
            [new("opencode", "/etc/opencode/opencode.jsonc"), new("litellm", "/var/lib/llm-glue/litellm/models.yaml", "/opt/litellm/config.yaml", "litellm.service", new("http://127.0.0.1:4000/health/liveliness"))],
            null, null, null, false, null,
            new("https://0.0.0.0:8765"), ["localhost", "catalog.example"], "read", "write",
            new("http://litellm.example:4000"), "inference", "/var/lib/llm-glue", "/var/log/llm-glue/llm-glue.log"));

        Assert.Contains("agent = true", text);
        Assert.Equal(2, Count(text, "[[agent.providers]]"));
        Assert.Equal(2, Count(text, "[[client.targets]]"));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, text);
            var options = TomlConfigLoader.Load(path);
            Assert.True(options.Roles.Agent && options.Roles.Server && options.Roles.Client);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RendersRemoteAgentOnly()
    {
        var text = SetupConfiguration.Render(new(
            new(true, false, false), [new("ollama", "local-ollama", new("http://127.0.0.1:11434"))], [],
            new("https://catalog.example:8765"), null, "write", true, null,
            new("https://127.0.0.1:8765"), [], null, null, null, null, "state", "logs/llm-glue.log"));
        Assert.Contains("submit_to = \"https://catalog.example:8765/\"", text);
        Assert.Contains("allow_invalid_server_certificate = true", text);
    }

    [Fact]
    public void GeneratesIndependentFortyEightCharacterTokens()
    {
        var first = SetupConfiguration.RandomToken();
        var second = SetupConfiguration.RandomToken();
        Assert.Matches("^[0-9a-f]{48}$", first);
        Assert.NotEqual(first, second);
    }

    private static int Count(string text, string value) => text.Split(value, StringSplitOptions.None).Length - 1;
}
