using Tomlyn;
using Tomlyn.Model;

namespace LlmGlue.Core.Configuration;

public static class TomlConfigLoader
{
    public static GlueOptions Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var baseDirectory = Path.GetDirectoryName(fullPath)!;
        string text;
        try { text = File.ReadAllText(fullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new ConfigurationException($"Could not read configuration '{fullPath}': {ex.Message}"); }

        TomlTable root;
        try { root = TomlSerializer.Deserialize<TomlTable>(text) ?? throw new ConfigurationException("Configuration is empty."); }
        catch (Exception ex) when (ex is not ConfigurationException)
        { throw new ConfigurationException($"Invalid TOML in '{fullPath}': {ex.Message}"); }
        CheckKeys(root, "root", "roles", "agent", "server", "client", "logging");
        var roles = RequiredTable(root, "roles");
        CheckKeys(roles, "roles", "agent", "server", "client");
        var roleOptions = new RoleOptions(Bool(roles, "agent", false), Bool(roles, "server", false), Bool(roles, "client", false));

        var agent = OptionalTable(root, "agent");
        CheckKeys(agent, "agent", "providers", "exclude_models", "submit_to", "write_token_environment", "write_token", "discovery_interval_seconds", "removal_grace_seconds");
        var providers = TableArray(agent, "providers").Select(ReadProvider).ToArray();
        var excludeModels = StringArray(agent, "exclude_models").ToArray();
        if (excludeModels.Any(string.IsNullOrWhiteSpace)) throw new ConfigurationException("agent.exclude_models cannot contain empty patterns.");
        if (excludeModels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != excludeModels.Length)
            throw new ConfigurationException("agent.exclude_models patterns must be unique (case-insensitive).");
        var submitToText = OptionalString(agent, "submit_to");
        var agentOptions = new AgentOptions(
            providers,
            excludeModels,
            submitToText is null ? null : HttpUri(submitToText, "agent.submit_to"),
            OptionalString(agent, "write_token_environment"),
            OptionalString(agent, "write_token"),
            PositiveInt(agent, "discovery_interval_seconds", 60),
            PositiveInt(agent, "removal_grace_seconds", 600));

        var server = OptionalTable(root, "server");
        CheckKeys(server, "server", "listen", "opencode_inference_url", "embed_opencode_inference_api_key", "opencode_inference_api_key_environment", "opencode_inference_api_key", "read_token_environment", "write_token_environment", "read_token", "write_token", "acknowledge_insecure_remote_http", "exclusions", "state_directory", "artifact_retention");
        var openCodeInferenceUrl = OptionalString(server, "opencode_inference_url");
        var serverOptions = new ServerOptions(
            AbsoluteUri(String(server, "listen", "http://127.0.0.1:8765"), "server.listen"),
            openCodeInferenceUrl is null ? null : HttpUri(openCodeInferenceUrl, "server.opencode_inference_url"),
            Bool(server, "embed_opencode_inference_api_key", true),
            OptionalString(server, "opencode_inference_api_key_environment"),
            OptionalString(server, "opencode_inference_api_key"),
            OptionalString(server, "read_token_environment"), OptionalString(server, "write_token_environment"),
            OptionalString(server, "read_token"), OptionalString(server, "write_token"),
            Bool(server, "acknowledge_insecure_remote_http", false),
            TableArray(server, "exclusions").Select(ReadExclusion).ToArray(),
            String(server, "state_directory", "state"),
            PositiveInt(server, "artifact_retention", 5));
        ValidateListen(serverOptions);

        var client = OptionalTable(root, "client");
        CheckKeys(client, "client", "source", "token_environment", "token", "poll_interval_seconds", "targets");
        var source = OptionalString(client, "source");
        var tokenEnvironment = OptionalString(client, "token_environment");
        var token = OptionalString(client, "token");
        var targets = TableArray(client, "targets").Select(ReadTarget).ToArray();
        ValidateUniqueTargetPaths(targets, baseDirectory);
        if (roleOptions.Server && targets.Any(x => x.Type == "litellm") && serverOptions.OpenCodeInferenceUrl is null)
            throw new ConfigurationException("A Server with a LiteLLM target requires server.opencode_inference_url so remote OpenCode artifacts route through LiteLLM instead of private provider endpoints.");

        var logging = OptionalTable(root, "logging");
        CheckKeys(logging, "logging", "level", "file", "archive_size_mb", "retention");
        var level = String(logging, "level", "Information");
        if (level is not ("Trace" or "Debug" or "Information" or "Warning" or "Error" or "Critical")) throw new ConfigurationException("logging.level is invalid.");
        var loggingOptions = new LoggingOptions(level, String(logging, "file", "logs/llm-glue.log"), PositiveInt(logging, "archive_size_mb", 10), PositiveInt(logging, "retention", 7));

        if (!roleOptions.Agent && !roleOptions.Server && !roleOptions.Client)
            throw new ConfigurationException("At least one role must be enabled.");
        if (roleOptions.Agent && providers.Length == 0)
            throw new ConfigurationException("Agent role requires at least one [[agent.providers]] entry.");
        if (roleOptions.Agent && !roleOptions.Server && agentOptions.SubmitTo is null)
            throw new ConfigurationException("Agent-only role requires agent.submit_to.");
        ValidateSecretChoice("agent write token", agentOptions.WriteTokenEnvironment, agentOptions.WriteToken);
        ValidateSecretChoice("OpenCode inference API key", serverOptions.OpenCodeInferenceApiKeyEnvironment, serverOptions.OpenCodeInferenceApiKey);
        ValidateSecretChoice("server read token", serverOptions.ReadTokenEnvironment, serverOptions.ReadToken);
        ValidateSecretChoice("server write token", serverOptions.WriteTokenEnvironment, serverOptions.WriteToken);
        ValidateSecretChoice("client token", tokenEnvironment, token);
        ValidateOpenCodeInferenceSecret(serverOptions);
        if (agentOptions.SubmitTo is not null && !agentOptions.SubmitTo.IsLoopback && agentOptions.WriteTokenEnvironment is null && agentOptions.WriteToken is null)
            throw new ConfigurationException("Remote agent.submit_to requires agent.write_token_environment or a protected agent.write_token.");
        if (roleOptions.Server && agentOptions.SubmitTo is not null)
            throw new ConfigurationException("agent.submit_to is only valid when the Server role is disabled; combined Agent+Server uses the in-process publication boundary.");
        var sourceUri = source is null ? null : HttpUri(source, "client.source");
        if (roleOptions.Client && sourceUri is null && !roleOptions.Server)
            throw new ConfigurationException("Client role requires client.source unless Server is enabled in-process.");
        if (roleOptions.Client && sourceUri is not null && !sourceUri.IsLoopback && tokenEnvironment is null && token is null)
            throw new ConfigurationException("Remote client.source requires client.token_environment or a protected client.token.");

        return new GlueOptions(roleOptions, agentOptions, serverOptions,
            new ClientOptions(sourceUri, tokenEnvironment, token, PositiveInt(client, "poll_interval_seconds", 30), targets), loggingOptions, fullPath, baseDirectory);
    }

    private static void ValidateOpenCodeInferenceSecret(ServerOptions server)
    {
        var hasSecretSource = server.OpenCodeInferenceApiKeyEnvironment is not null || server.OpenCodeInferenceApiKey is not null;
        if (server.OpenCodeInferenceUrl is null && hasSecretSource)
            throw new ConfigurationException("OpenCode inference API key settings require server.opencode_inference_url.");
        if (server.OpenCodeInferenceUrl is not null && server.EmbedOpenCodeInferenceApiKey && !hasSecretSource)
            throw new ConfigurationException("Default embedded OpenCode credentials require server.opencode_inference_api_key or server.opencode_inference_api_key_environment. Set embed_opencode_inference_api_key = false only when every OpenCode process receives LLM_GLUE_INFERENCE_API_KEY separately.");
        if (!server.EmbedOpenCodeInferenceApiKey && hasSecretSource)
            throw new ConfigurationException("OpenCode inference API key settings cannot be combined with embed_opencode_inference_api_key = false; clients must provide LLM_GLUE_INFERENCE_API_KEY in that mode.");
    }

    private static void ValidateUniqueTargetPaths(IReadOnlyList<TargetOptions> targets, string baseDirectory)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new HashSet<string>(comparer);
        foreach (var target in targets)
        {
            string path;
            try { path = Path.GetFullPath(target.Path, baseDirectory); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { throw new ConfigurationException($"Invalid client target path '{target.Path}': {ex.Message}"); }
            if (!paths.Add(path)) throw new ConfigurationException($"Client target path '{path}' is configured more than once.");
        }
    }

    private static ExclusionRuleOptions ReadExclusion(TomlTable table)
    {
        CheckKeys(table, "server.exclusions", "provider_id", "instance_id", "model_id", "display_name", "provider_model", "model_pattern");
        var rule = new ExclusionRuleOptions(
            OptionalString(table, "provider_id"),
            OptionalString(table, "instance_id"),
            OptionalString(table, "model_id"),
            OptionalString(table, "display_name"),
            OptionalString(table, "provider_model"),
            OptionalString(table, "model_pattern"));
        if (rule is { ProviderId: null, InstanceId: null, ModelId: null, DisplayName: null, ProviderModel: null, ModelPattern: null })
            throw new ConfigurationException("Each [[server.exclusions]] rule must specify at least one match field.");
        return rule;
    }

    private static ProviderOptions ReadProvider(TomlTable table)
    {
        CheckKeys(table, "agent.providers", "type", "name", "url", "model_directories", "model_path_mappings", "inspect_gguf", "hash_model_files", "probe_mode", "probe_timeout_seconds", "discovery_timeout_seconds");
        var type = RequiredString(table, "type");
        if (type is not ("ollama" or "llama.cpp")) throw new ConfigurationException($"Unsupported provider type '{type}'.");
        var directories = StringArray(table, "model_directories");
        var mappings = StringArray(table, "model_path_mappings").Select(ParseModelPathMapping).ToArray();
        if (mappings.Select(x => x.ProviderPrefix).Distinct(StringComparer.Ordinal).Count() != mappings.Length)
            throw new ConfigurationException("model_path_mappings provider prefixes must be unique.");
        var inspect = Bool(table, "inspect_gguf", true);
        var hash = Bool(table, "hash_model_files", false);
        var probeMode = String(table, "probe_mode", "off") switch
        {
            "off" => CapabilityProbeMode.Off,
            "loaded-only" => CapabilityProbeMode.LoadedOnly,
            "allow-load" => CapabilityProbeMode.AllowLoad,
            _ => throw new ConfigurationException("probe_mode must be 'off', 'loaded-only', or 'allow-load'.")
        };
        if (type != "llama.cpp" && (directories.Count != 0 || mappings.Length != 0 || !inspect || hash))
            throw new ConfigurationException("model_directories, model_path_mappings, inspect_gguf, and hash_model_files apply only to llama.cpp providers.");
        if (hash && !inspect) throw new ConfigurationException("hash_model_files requires inspect_gguf = true.");
        if (mappings.Length != 0 && directories.Count == 0) throw new ConfigurationException("model_path_mappings requires at least one model_directories allowlist entry.");
        return new ProviderOptions(type, RequiredString(table, "name"), HttpUri(RequiredString(table, "url"), "provider.url"), directories, inspect, hash, probeMode, PositiveInt(table, "probe_timeout_seconds", 15), PositiveInt(table, "discovery_timeout_seconds", 300), mappings);
    }

    private static ModelPathMapping ParseModelPathMapping(string value)
    {
        var separator = value.IndexOf('=');
        if (separator <= 0 || separator == value.Length - 1)
            throw new ConfigurationException("Each model_path_mappings entry must use 'provider-prefix=local-directory'.");
        var provider = value[..separator].Trim().TrimEnd('/', '\\');
        var local = value[(separator + 1)..].Trim().TrimEnd('/', '\\');
        if (provider.Length == 0 || local.Length == 0)
            throw new ConfigurationException("Each model_path_mappings entry must use non-empty provider and local paths.");
        return new ModelPathMapping(provider, local);
    }

    private static TargetOptions ReadTarget(TomlTable table)
    {
        CheckKeys(table, "client.targets", "type", "path", "primary_config_path", "service_name", "health_url", "deny_builtin_opencode_provider");
        var type = RequiredString(table, "type");
        if (type is not ("opencode" or "litellm")) throw new ConfigurationException($"Unsupported target type '{type}'.");
        var primary = OptionalString(table, "primary_config_path");
        var service = OptionalString(table, "service_name");
        var health = OptionalString(table, "health_url");
        var denyBuiltInOpenCodeProvider = Bool(table, "deny_builtin_opencode_provider", true);
        if (type == "litellm" && primary is null) throw new ConfigurationException("LiteLLM target requires primary_config_path.");
        if (type == "litellm" && service is null) throw new ConfigurationException("LiteLLM target requires service_name for controlled activation.");
        if (type != "opencode" && table.ContainsKey("deny_builtin_opencode_provider")) throw new ConfigurationException("deny_builtin_opencode_provider applies only to OpenCode targets.");
        return new TargetOptions(type, RequiredString(table, "path"), primary, service, health is null ? null : HttpUri(health, "target.health_url"), denyBuiltInOpenCodeProvider);
    }

    private static void ValidateListen(ServerOptions server)
    {
        if (server.Listen.Scheme != Uri.UriSchemeHttp) throw new ConfigurationException("server.listen must use http; TLS termination is external.");
        if (!string.IsNullOrEmpty(server.Listen.UserInfo)) throw new ConfigurationException("server.listen cannot contain user information.");
        var loopback = server.Listen.IsLoopback;
        if (!loopback && (server.ReadTokenEnvironment is null && server.ReadToken is null || server.WriteTokenEnvironment is null && server.WriteToken is null))
            throw new ConfigurationException("Non-loopback server.listen requires both read and write token sources.");
        if (!loopback && !server.AcknowledgeInsecureRemoteHttp)
            throw new ConfigurationException("Non-loopback plain HTTP requires acknowledge_insecure_remote_http = true.");
    }

    private static void ValidateSecretChoice(string name, string? environment, string? literal)
    {
        if (environment is not null && literal is not null) throw new ConfigurationException($"{name} must use either an environment reference or a literal, not both.");
    }

    private static TomlTable RequiredTable(TomlTable table, string key) => table.TryGetValue(key, out var value) && value is TomlTable result ? result : throw new ConfigurationException($"Missing table [{key}].");
    private static TomlTable OptionalTable(TomlTable table, string key) => table.TryGetValue(key, out var value) && value is TomlTable result ? result : new TomlTable();
    private static IEnumerable<TomlTable> TableArray(TomlTable table, string key) => table.TryGetValue(key, out var value) && value is TomlTableArray array ? array.Cast<TomlTable>() : [];
    private static string RequiredString(TomlTable table, string key) => OptionalString(table, key) ?? throw new ConfigurationException($"Missing string '{key}'.");
    private static string String(TomlTable table, string key, string defaultValue) => OptionalString(table, key) ?? defaultValue;
    private static string? OptionalString(TomlTable table, string key) => !table.TryGetValue(key, out var value) ? null : value is string text && !string.IsNullOrWhiteSpace(text) ? text : throw new ConfigurationException($"'{key}' must be a non-empty string.");
    private static bool Bool(TomlTable table, string key, bool defaultValue) => !table.TryGetValue(key, out var value) ? defaultValue : value is bool result ? result : throw new ConfigurationException($"'{key}' must be a boolean.");
    private static int PositiveInt(TomlTable table, string key, int defaultValue) => !table.TryGetValue(key, out var value) ? defaultValue : value is long result && result is > 0 and <= 3600 ? (int)result : throw new ConfigurationException($"'{key}' must be an integer from 1 through 3600.");
    private static IReadOnlyList<string> StringArray(TomlTable table, string key) => !table.TryGetValue(key, out var value) ? [] : value is TomlArray array && array.All(x => x is string) ? array.Cast<string>().ToArray() : throw new ConfigurationException($"'{key}' must be an array of strings.");
    private static Uri AbsoluteUri(string value, string field) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : throw new ConfigurationException($"'{field}' must be an absolute URI.");
    private static Uri HttpUri(string value, string field)
    {
        var uri = AbsoluteUri(value, field);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) throw new ConfigurationException($"'{field}' must use http or https.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new ConfigurationException($"'{field}' cannot contain user information; configure credentials through the supported secret source.");
        return uri;
    }
    private static void CheckKeys(TomlTable table, string scope, params string[] allowed)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        var unknown = table.Keys.Where(x => !set.Contains(x)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0) throw new ConfigurationException($"Unknown setting(s) in {scope}: {string.Join(", ", unknown)}.");
    }
}
