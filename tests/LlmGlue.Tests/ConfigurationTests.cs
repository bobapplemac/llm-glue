using LlmGlue.Core.Configuration;
using LlmGlue.Operations;

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
            opencode_inference_url = "https://litellm.example.test"

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
            Assert.True(options.Providers[0].InspectGguf);
            Assert.False(options.Providers[0].HashModelFiles);
            Assert.Equal(CapabilityProbeMode.Off, options.Providers[0].ProbeMode);
            Assert.Equal("litellm", Assert.Single(options.Client.Targets).Type);
            Assert.Equal("state", options.Server.StateDirectory);
            Assert.Equal(5, options.Server.ArtifactRetention);
            Assert.Equal(new Uri("https://litellm.example.test"), options.Server.OpenCodeInferenceUrl);
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
    public void RejectsCentralLiteLlmTargetWithoutClientReachableInferenceUrl()
    {
        var path = WriteTemp("""
            [roles]
            agent = false
            server = true
            client = true
            [server]
            listen = "http://127.0.0.1:8765"
            [client]
            source = "http://127.0.0.1:8765"
            [[client.targets]]
            type = "litellm"
            path = "generated.yaml"
            primary_config_path = "config.yaml"
            service_name = "litellm.service"
            """);
        try { Assert.Contains("opencode_inference_url", Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)).Message); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadsMultipleExplicitOpenCodeTargets()
    {
        var path = WriteTemp("""
            [roles]
            agent = false
            server = false
            client = true
            [client]
            source = "http://127.0.0.1:8765"
            [[client.targets]]
            type = "opencode"
            path = "first/opencode.json"
            [[client.targets]]
            type = "opencode"
            path = "second/opencode.json"
            """);
        try
        {
            var targets = TomlConfigLoader.Load(path).Client.Targets;
            Assert.Equal(2, targets.Count);
            Assert.All(targets, target => Assert.True(target.DenyBuiltInOpenCodeProvider));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadsExplicitBuiltInOpenCodeProviderPolicyOptOut()
    {
        var path = WriteTemp("""
            [roles]
            agent = false
            server = false
            client = true
            [client]
            source = "http://127.0.0.1:8765"
            [[client.targets]]
            type = "opencode"
            path = "opencode.jsonc"
            deny_builtin_opencode_provider = false
            """);
        try { Assert.False(Assert.Single(TomlConfigLoader.Load(path).Client.Targets).DenyBuiltInOpenCodeProvider); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsBuiltInOpenCodeProviderPolicyOnLiteLlmTarget()
    {
        var path = WriteTemp("""
            [roles]
            agent = false
            server = true
            client = true
            [server]
            listen = "http://127.0.0.1:8765"
            opencode_inference_url = "http://127.0.0.1:4000"
            [client]
            source = "http://127.0.0.1:8765"
            [[client.targets]]
            type = "litellm"
            path = "generated.yaml"
            primary_config_path = "config.yaml"
            service_name = "litellm.service"
            deny_builtin_opencode_provider = false
            """);
        try { Assert.Contains("only to OpenCode", Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)).Message); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsDuplicateTargetPaths()
    {
        var path = WriteTemp("""
            [roles]
            agent = false
            server = false
            client = true
            [client]
            source = "http://127.0.0.1:8765"
            [[client.targets]]
            type = "opencode"
            path = "same/opencode.json"
            [[client.targets]]
            type = "opencode"
            path = "same/../same/opencode.json"
            """);
        try { Assert.Contains("configured more than once", Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)).Message); }
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

    [Theory]
    [InlineData("file:///tmp/provider")]
    [InlineData("http://user:secret@127.0.0.1:11434")]
    public void RejectsNonHttpOrCredentialBearingProviderUrls(string url)
    {
        var path = WriteTemp($$"""
            [roles]
            agent = true
            server = true
            client = false
            [[agent.providers]]
            type = "ollama"
            name = "local"
            url = "{{url}}"
            """);
        try { Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExplicitConfigPathHasHighestPrecedence()
    {
        var resolved = ConfigPathResolver.Resolve("C:/apps/llm-glue.exe", "./custom.conf", false, _ => false);
        Assert.EndsWith("custom.conf", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadsRemoteAgentAndExactServerExclusions()
    {
        var agentPath = WriteTemp("""
            [roles]
            agent = true
            server = false
            client = false

            [agent]
            submit_to = "https://glue.example.test"
            write_token_environment = "LLM_GLUE_WRITE_TOKEN"
            discovery_interval_seconds = 15
            removal_grace_seconds = 120

            [[agent.providers]]
            type = "ollama"
            name = "workstation"
            url = "http://127.0.0.1:11434"
            """);
        var serverPath = WriteTemp("""
            [roles]
            agent = false
            server = true
            client = false

            [[server.exclusions]]
            provider_id = "ollama"
            instance_id = "workstation"
            model_id = "private-model"
            """);
        try
        {
            var agent = TomlConfigLoader.Load(agentPath);
            Assert.Equal(new Uri("https://glue.example.test"), agent.Agent.SubmitTo);
            Assert.Equal(15, agent.Agent.DiscoveryIntervalSeconds);
            var exclusion = Assert.Single(TomlConfigLoader.Load(serverPath).Server.Exclusions);
            Assert.Equal("private-model", exclusion.ModelId);
        }
        finally
        {
            File.Delete(agentPath);
            File.Delete(serverPath);
        }
    }

    [Fact]
    public void RejectsAgentOnlyWithoutSubmissionDestination()
    {
        var path = WriteTemp("""
            [roles]
            agent = true
            server = false
            client = false
            [[agent.providers]]
            type = "ollama"
            name = "local"
            url = "http://127.0.0.1:11434"
            """);
        try { Assert.Contains("agent.submit_to", Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)).Message); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsHashingWhenGgufInspectionIsDisabled()
    {
        var path = WriteTemp("""
            [roles]
            agent = true
            server = true
            client = false
            [[agent.providers]]
            type = "llama.cpp"
            name = "router"
            url = "http://127.0.0.1:8081"
            model_directories = ["models"]
            inspect_gguf = false
            hash_model_files = true
            """);
        try { Assert.Contains("requires inspect_gguf", Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)).Message); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadsExplicitBoundedProbePolicy()
    {
        var path = WriteTemp("""
            [roles]
            agent = true
            server = true
            client = false
            [[agent.providers]]
            type = "llama.cpp"
            name = "router"
            url = "http://127.0.0.1:8081"
            probe_mode = "loaded-only"
            probe_timeout_seconds = 20
            """);
        try
        {
            var provider = Assert.Single(TomlConfigLoader.Load(path).Providers);
            Assert.Equal(CapabilityProbeMode.LoadedOnly, provider.ProbeMode);
            Assert.Equal(20, provider.ProbeTimeoutSeconds);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadsExplicitContainerToHostModelPathMapping()
    {
        var path = WriteTemp("""
            [roles]
            agent = true
            server = true
            client = false
            [[agent.providers]]
            type = "llama.cpp"
            name = "router"
            url = "http://127.0.0.1:8081"
            model_directories = ["/srv/models"]
            model_path_mappings = ["/models=/srv/models"]
            """);
        try
        {
            var mapping = Assert.Single(Assert.Single(TomlConfigLoader.Load(path).Providers).ModelPathMappings!);
            Assert.Equal("/models", mapping.ProviderPrefix);
            Assert.Equal("/srv/models", mapping.LocalDirectory);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("bad-mapping")]
    [InlineData("/models=")]
    public void RejectsInvalidModelPathMapping(string mapping)
    {
        var path = WriteTemp($$"""
            [roles]
            agent = true
            server = true
            client = false
            [[agent.providers]]
            type = "llama.cpp"
            name = "router"
            url = "http://127.0.0.1:8081"
            model_directories = ["/srv/models"]
            model_path_mappings = ["{{mapping}}"]
            """);
        try { Assert.Contains("model_path_mappings", Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)).Message); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AllowsProtectedSidecarTokensForHeadlessServices()
    {
        var serverPath = WriteTemp("""
            [roles]
            agent = false
            server = true
            client = false
            [server]
            listen = "http://0.0.0.0:8765"
            read_token = "read-secret"
            write_token = "write-secret"
            acknowledge_insecure_remote_http = true
            """);
        var clientPath = WriteTemp("""
            [roles]
            agent = false
            server = false
            client = true
            [client]
            source = "https://glue.example.test"
            token = "read-secret"
            """);
        try
        {
            var server = TomlConfigLoader.Load(serverPath);
            Assert.Equal("read-secret", server.Server.ReadToken);
            Assert.Equal("write-secret", server.Server.WriteToken);
            Assert.Equal("read-secret", TomlConfigLoader.Load(clientPath).Client.Token);
        }
        finally
        {
            File.Delete(serverPath);
            File.Delete(clientPath);
        }
    }

    [Fact]
    public void RejectsAmbiguousLiteralAndEnvironmentSecretSources()
    {
        var path = WriteTemp("""
            [roles]
            agent = false
            server = false
            client = true
            [client]
            source = "https://glue.example.test"
            token = "literal"
            token_environment = "LLM_GLUE_READ_TOKEN"
            """);
        try { Assert.Contains("either an environment reference or a literal", Assert.Throws<ConfigurationException>(() => TomlConfigLoader.Load(path)).Message); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DiagnosticsDescribeLiteralSecretWithoutPrintingIt()
    {
        var status = CliCommands.SecretStatus("super-secret-value", null);
        Assert.Equal("protected sidecar literal (set)", status);
        Assert.DoesNotContain("super-secret-value", status, StringComparison.Ordinal);
    }

    [Fact]
    public void AllShippedExamplesValidate()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "examples");
        var files = Directory.GetFiles(directory, "*.conf");
        Assert.Equal(4, files.Length);
        foreach (var file in files) _ = TomlConfigLoader.Load(file);
    }

    private static string WriteTemp(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"llm-glue-{Guid.NewGuid():N}.conf");
        File.WriteAllText(path, contents);
        return path;
    }
}
