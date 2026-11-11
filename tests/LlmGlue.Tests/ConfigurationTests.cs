using LlmGlue.Core.Configuration;

namespace LlmGlue.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void LoadsSpecificationShapeAndResolvesRoles()
    {
        var path = WriteTemp("""
            [roles]
            agent = true
            server = true
            client = true

            [[agent.providers]]
            type = "llama.cpp"
            name = "local-llama"
            url = "http://127.0.0.1:8081"
            model_directories = ["models"]

            [server]
            listen = "http://127.0.0.1:8765"

            [client]
            source = "http://127.0.0.1:8765"

            [[client.targets]]
            type = "litellm"
            path = "generated-models.yaml"
            primary_config_path = "config.yaml"
            service_name = "litellm.service"
            """);
        try
        {
            var options = TomlConfigLoader.Load(path);
            Assert.True(options.Roles.Agent);
            Assert.Single(options.Providers);
            Assert.Equal("local-llama", options.Providers[0].Name);
            Assert.Equal("litellm", Assert.Single(options.Client.Targets).Type);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsUnknownSettings()
    {
        var path = WriteTemp("""
            [roles]
            agent = false
            server = true
            client = false
            surprise = true
            """);
        try { Assert.Contains("Unknown setting", Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)).Message); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsRemoteHttpWithoutTokensAndAcknowledgement()
    {
        var path = WriteTemp("""
            [roles]
            agent = false
            server = true
            client = false
            [server]
            listen = "http://0.0.0.0:8765"
            """);
        try { Assert.Contains("requires both read and write", Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)).Message); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExplicitConfigPathHasHighestPrecedence()
    {
        var resolved = ConfigPathResolver.Resolve("C:/apps/llm-glue.exe", "./custom.conf", false, _ => false);
        Assert.EndsWith("custom.conf", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllShippedExamplesValidate()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "examples");
        var files = Directory.GetFiles(directory, "*.conf");
        Assert.Equal(3, files.Length);
        foreach (var file in files) _ = TomlConfigLoader.Load(file);
    }

    private static string WriteTemp(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"llm-glue-{Guid.NewGuid():N}.conf");
        File.WriteAllText(path, contents);
        return path;
    }
}
