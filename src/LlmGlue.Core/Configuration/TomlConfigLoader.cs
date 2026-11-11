using Tomlyn;
using Tomlyn.Model;

namespace LlmGlue.Core.Configuration;

public static class TomlConfigLoader
{
    public static GlueOptions Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        string text;
        try { text = File.ReadAllText(fullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new ConfigurationException($"Could not read configuration '{fullPath}': {ex.Message}"); }

        TomlTable root;
        try { root = TomlSerializer.Deserialize<TomlTable>(text) ?? throw new ConfigurationException("Configuration is empty."); }
        catch (Exception ex) when (ex is not ConfigurationException)
        { throw new ConfigurationException($"Invalid TOML in '{fullPath}': {ex.Message}"); }
        CheckKeys(root, "root", "roles", "agent", "server", "client");
        var roles = RequiredTable(root, "roles");
        CheckKeys(roles, "roles", "agent", "server", "client");
        var roleOptions = new RoleOptions(Bool(roles, "agent", false), Bool(roles, "server", false), Bool(roles, "client", false));

        var agent = OptionalTable(root, "agent");
        CheckKeys(agent, "agent", "providers");
        var providers = TableArray(agent, "providers").Select(ReadProvider).ToArray();

        var server = OptionalTable(root, "server");
        CheckKeys(server, "server", "listen", "read_token_environment", "write_token_environment", "acknowledge_insecure_remote_http");
        var serverOptions = new ServerOptions(
            AbsoluteUri(String(server, "listen", "http://127.0.0.1:8765"), "server.listen"),
            OptionalString(server, "read_token_environment"), OptionalString(server, "write_token_environment"),
            Bool(server, "acknowledge_insecure_remote_http", false));
        ValidateListen(serverOptions);

        var client = OptionalTable(root, "client");
        CheckKeys(client, "client", "source", "token_environment", "poll_interval_seconds", "targets");
        var source = OptionalString(client, "source");
        var tokenEnvironment = OptionalString(client, "token_environment");
        var targets = TableArray(client, "targets").Select(ReadTarget).ToArray();

        if (!roleOptions.Agent && !roleOptions.Server && !roleOptions.Client)
            throw new ConfigurationException("At least one role must be enabled.");
        if (roleOptions.Agent && providers.Length == 0)
            throw new ConfigurationException("Agent role requires at least one [[agent.providers]] entry.");
        if (roleOptions.Client && source is null && !roleOptions.Server)
            throw new ConfigurationException("Client role requires client.source unless Server is enabled in-process.");
        if (roleOptions.Client && source is not null && !AbsoluteUri(source, "client.source").IsLoopback && tokenEnvironment is null)
            throw new ConfigurationException("Remote client.source requires client.token_environment.");

        var baseDirectory = Path.GetDirectoryName(fullPath)!;
        return new GlueOptions(roleOptions, providers, serverOptions,
            new ClientOptions(source is null ? null : AbsoluteUri(source, "client.source"), tokenEnvironment, PositiveInt(client, "poll_interval_seconds", 30), targets), fullPath, baseDirectory);
    }

    private static ProviderOptions ReadProvider(TomlTable table)
    {
        CheckKeys(table, "agent.providers", "type", "name", "url", "model_directories");
        var type = RequiredString(table, "type");
        if (type is not ("ollama" or "llama.cpp")) throw new ConfigurationException($"Unsupported provider type '{type}'.");
        return new ProviderOptions(type, RequiredString(table, "name"), AbsoluteUri(RequiredString(table, "url"), "provider.url"), StringArray(table, "model_directories"));
    }

    private static TargetOptions ReadTarget(TomlTable table)
    {
        CheckKeys(table, "client.targets", "type", "path", "primary_config_path", "service_name", "health_url");
        var type = RequiredString(table, "type");
        if (type is not ("opencode" or "litellm")) throw new ConfigurationException($"Unsupported target type '{type}'.");
        var primary = OptionalString(table, "primary_config_path");
        var service = OptionalString(table, "service_name");
        var health = OptionalString(table, "health_url");
        if (type == "litellm" && primary is null) throw new ConfigurationException("LiteLLM target requires primary_config_path.");
        if (type == "litellm" && service is null) throw new ConfigurationException("LiteLLM target requires service_name for controlled activation.");
        return new TargetOptions(type, RequiredString(table, "path"), primary, service, health is null ? null : AbsoluteUri(health, "target.health_url"));
    }

    private static void ValidateListen(ServerOptions server)
    {
        if (server.Listen.Scheme != Uri.UriSchemeHttp) throw new ConfigurationException("server.listen must use http; TLS termination is external.");
        var loopback = server.Listen.IsLoopback;
        if (!loopback && (server.ReadTokenEnvironment is null || server.WriteTokenEnvironment is null))
            throw new ConfigurationException("Non-loopback server.listen requires both read and write token environment references.");
        if (!loopback && !server.AcknowledgeInsecureRemoteHttp)
            throw new ConfigurationException("Non-loopback plain HTTP requires acknowledge_insecure_remote_http = true.");
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
    private static void CheckKeys(TomlTable table, string scope, params string[] allowed)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        var unknown = table.Keys.Where(x => !set.Contains(x)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0) throw new ConfigurationException($"Unknown setting(s) in {scope}: {string.Join(", ", unknown)}.");
    }
}
