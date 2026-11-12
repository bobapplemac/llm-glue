using System.Text;

namespace LlmGlue.Core.Documentation;

public sealed record ConfigurationSetting(
    string Scope,
    string Key,
    string Path,
    string Type,
    string Default,
    string Description);

public sealed record CliParameter(string Syntax, string Description);

public sealed record CliCommandReference(
    string Syntax,
    string Summary,
    IReadOnlyList<CliParameter> Parameters,
    bool Internal = false);

public static class ReferenceDocumentation
{
    public static IReadOnlyList<ConfigurationSetting> ConfigurationSettings { get; } =
    [
        S("root", "roles", "[roles]", "table", "required", "Enables the composable Agent, Server, and Client roles."),
        S("root", "agent", "[agent]", "table", "empty", "Agent discovery, export, and remote-submission policy."),
        S("root", "server", "[server]", "table", "empty", "Server listener, publication, authentication, naming, and persistence settings."),
        S("root", "client", "[client]", "table", "empty", "Artifact source, polling, authentication, and target settings."),
        S("root", "logging", "[logging]", "table", "empty", "Console and rolling-file logging settings."),

        S("roles", "agent", "roles.agent", "boolean", "false", "Run provider discovery and optional remote inventory submission."),
        S("roles", "server", "roles.server", "boolean", "false", "Run the catalog API, reconciliation, and artifact generation."),
        S("roles", "client", "roles.client", "boolean", "false", "Poll and apply generated target artifacts."),

        S("agent", "providers", "[[agent.providers]]", "array of tables", "[]", "Provider discovery endpoints. Required when the Agent role is enabled."),
        S("agent", "exclude_models", "agent.exclude_models", "array of strings", "[]", "Case-insensitive exact or * / ? model patterns removed before probing, publication, or submission."),
        S("agent", "submit_to", "agent.submit_to", "HTTP(S) URI", "none", "Remote Server root for Agent-only inventory submission."),
        S("agent", "write_token_environment", "agent.write_token_environment", "string", "none", "Environment variable containing the remote inventory write token."),
        S("agent", "write_token", "agent.write_token", "secret string", "none", "Literal remote inventory write token; mutually exclusive with write_token_environment."),
        S("agent", "allow_invalid_server_certificate", "agent.allow_invalid_server_certificate", "boolean", "false", "Accept any TLS certificate for agent.submit_to; encrypts traffic without authenticating the Server."),
        S("agent", "trusted_server_public_key_sha256", "agent.trusted_server_public_key_sha256", "64-character hexadecimal SHA-256", "none", "Require the Server certificate SubjectPublicKeyInfo hash to match, regardless of platform trust."),
        S("agent", "discovery_interval_seconds", "agent.discovery_interval_seconds", "integer 1..3600", "60", "Delay after each completed Agent discovery cycle."),
        S("agent", "removal_grace_seconds", "agent.removal_grace_seconds", "integer 1..3600", "600", "Time an absent deployment remains degraded before removal."),

        S("agent.providers", "type", "agent.providers[].type", "enum", "required", "Provider adapter: ollama or llama.cpp."),
        S("agent.providers", "name", "agent.providers[].name", "string", "required", "Stable provider instance ID used by diagnostics, exclusions, and namespace overrides."),
        S("agent.providers", "url", "agent.providers[].url", "HTTP(S) URI", "required", "Provider API base URL."),
        S("agent.providers", "model_directories", "agent.providers[].model_directories", "array of paths", "[]", "Allowlisted host GGUF directories for llama.cpp inspection."),
        S("agent.providers", "model_path_mappings", "agent.providers[].model_path_mappings", "array of strings", "[]", "Container-to-host path mappings written as provider-prefix=local-directory."),
        S("agent.providers", "inspect_gguf", "agent.providers[].inspect_gguf", "boolean", "true", "Inspect allowlisted GGUF metadata for llama.cpp deployments."),
        S("agent.providers", "hash_model_files", "agent.providers[].hash_model_files", "boolean", "false", "Compute full model-file hashes; requires inspect_gguf."),
        S("agent.providers", "probe_mode", "agent.providers[].probe_mode", "enum", "off", "Inference probing policy: off, loaded-only, or allow-load."),
        S("agent.providers", "probe_timeout_seconds", "agent.providers[].probe_timeout_seconds", "integer 1..3600", "15", "Per-inference-probe timeout."),
        S("agent.providers", "discovery_timeout_seconds", "agent.providers[].discovery_timeout_seconds", "integer 1..3600", "300", "Provider inventory and router metadata-refresh timeout."),

        S("server", "listen", "server.listen", "HTTP(S) URI", "https://127.0.0.1:8765", "Kestrel listener; HTTPS uses file-based TLS settings below."),
        S("server", "opencode_inference_url", "server.opencode_inference_url", "HTTP(S) URI", "none", "Client-reachable LiteLLM base URL used by central OpenCode artifacts."),
        S("server", "embed_opencode_inference_api_key", "server.embed_opencode_inference_api_key", "boolean", "true", "Embed the LiteLLM client key in authenticated OpenCode artifacts."),
        S("server", "opencode_inference_api_key_environment", "server.opencode_inference_api_key_environment", "string", "none", "Environment variable containing the embedded LiteLLM client key."),
        S("server", "opencode_inference_api_key", "server.opencode_inference_api_key", "secret string", "none", "Literal embedded LiteLLM client key; mutually exclusive with its environment setting."),
        S("server", "opencode_namespace", "server.opencode_namespace", "namespace", "llm-glue", "Default concise OpenCode provider namespace."),
        S("server", "opencode_namespace_overrides", "[[server.opencode_namespace_overrides]]", "array of tables", "[]", "Ordered exact-match rules that split selected sources into other OpenCode namespaces."),
        S("server", "litellm_namespace", "server.litellm_namespace", "namespace", "normalized hostname", "Server-scoped prefix for generated LiteLLM aliases."),
        S("server", "read_token_environment", "server.read_token_environment", "string", "none", "Environment variable containing the artifact/API read token."),
        S("server", "write_token_environment", "server.write_token_environment", "string", "none", "Environment variable containing the Agent inventory write token."),
        S("server", "read_token", "server.read_token", "secret string", "none", "Literal artifact/API read token."),
        S("server", "write_token", "server.write_token", "secret string", "none", "Literal Agent inventory write token."),
        S("server", "acknowledge_insecure_remote_http", "server.acknowledge_insecure_remote_http", "boolean", "false", "Required acknowledgement for a non-loopback plaintext HTTP listener."),
        S("server", "tls_auto_generate", "server.tls_auto_generate", "boolean", "true for HTTPS", "Create a 30-year self-signed PEM certificate/key when the configured files are absent."),
        S("server", "tls_certificate_path", "server.tls_certificate_path", "path", "state/tls/server.crt when auto-generated", "PFX, PEM, or CRT server certificate file."),
        S("server", "tls_private_key_path", "server.tls_private_key_path", "path", "state/tls/server.key when auto-generated", "PEM private-key file; required for PEM/CRT certificates and forbidden for PFX."),
        S("server", "tls_certificate_password_environment", "server.tls_certificate_password_environment", "string", "none", "Environment variable containing a PFX password."),
        S("server", "tls_certificate_password", "server.tls_certificate_password", "secret string", "none", "Literal PFX password; mutually exclusive with the environment setting."),
        S("server", "tls_server_names", "server.tls_server_names", "array of DNS patterns", "[]", "Exact, *.suffix, or * SNI names; unmatched TLS handshakes are refused, localhost remains available for local management, and names become auto-certificate SANs."),
        S("server", "exclusions", "[[server.exclusions]]", "array of tables", "[]", "Publication-only exact/wildcard exclusion rules."),
        S("server", "state_directory", "server.state_directory", "path", "state", "Durable catalog and artifact history directory."),
        S("server", "artifact_retention", "server.artifact_retention", "integer 1..3600", "5", "Number of validated catalog/artifact states retained."),

        S("server.opencode_namespace_overrides", "namespace", "server.opencode_namespace_overrides[].namespace", "namespace", "required", "OpenCode provider namespace assigned by this rule."),
        S("server.opencode_namespace_overrides", "provider_id", "server.opencode_namespace_overrides[].provider_id", "string", "none", "Exact provider ID selector."),
        S("server.opencode_namespace_overrides", "instance_id", "server.opencode_namespace_overrides[].instance_id", "string", "none", "Exact provider instance selector; at least one selector is required."),

        S("server.exclusions", "provider_id", "server.exclusions[].provider_id", "string", "none", "Exact provider ID selector."),
        S("server.exclusions", "instance_id", "server.exclusions[].instance_id", "string", "none", "Exact provider instance selector."),
        S("server.exclusions", "model_id", "server.exclusions[].model_id", "string", "none", "Exact model ID selector."),
        S("server.exclusions", "display_name", "server.exclusions[].display_name", "string", "none", "Exact display-name selector."),
        S("server.exclusions", "provider_model", "server.exclusions[].provider_model", "string", "none", "Exact provider/instance/model selector."),
        S("server.exclusions", "model_pattern", "server.exclusions[].model_pattern", "wildcard string", "none", "Case-insensitive * / ? pattern matched across model identities."),

        S("client", "source", "client.source", "HTTP(S) URI", "in-process Server listener", "Server root from which artifacts and status are fetched."),
        S("client", "token_environment", "client.token_environment", "string", "none", "Environment variable containing the Server read token."),
        S("client", "token", "client.token", "secret string", "none", "Literal Server read token; mutually exclusive with token_environment."),
        S("client", "allow_invalid_server_certificate", "client.allow_invalid_server_certificate", "boolean", "false", "Accept any TLS certificate for client.source; encrypts traffic without authenticating the Server."),
        S("client", "trusted_server_public_key_sha256", "client.trusted_server_public_key_sha256", "64-character hexadecimal SHA-256", "none", "Require the Server certificate SubjectPublicKeyInfo hash to match, regardless of platform trust."),
        S("client", "poll_interval_seconds", "client.poll_interval_seconds", "integer 1..3600", "60", "Delay between conditional artifact synchronization cycles."),
        S("client", "targets", "[[client.targets]]", "array of tables", "[]", "Explicit local OpenCode or LiteLLM destinations."),

        S("client.targets", "type", "client.targets[].type", "enum", "required", "Target adapter: opencode or litellm."),
        S("client.targets", "path", "client.targets[].path", "path", "required", "Managed target file path."),
        S("client.targets", "primary_config_path", "client.targets[].primary_config_path", "path", "required for litellm", "Operator-owned LiteLLM primary YAML containing the generated include."),
        S("client.targets", "service_name", "client.targets[].service_name", "string", "required for litellm", "Allowlisted service identity restarted after LiteLLM changes."),
        S("client.targets", "health_url", "client.targets[].health_url", "HTTP(S) URI", "none", "Post-activation health endpoint."),
        S("client.targets", "deny_builtin_opencode_provider", "client.targets[].deny_builtin_opencode_provider", "boolean", "true", "Add an owned policy hiding OpenCode's built-in catalog."),

        S("logging", "level", "logging.level", "enum", "Information", "Trace, Debug, Information, Warning, Error, or Critical."),
        S("logging", "file", "logging.file", "path", "logs/llm-glue.log", "Rolling log file path."),
        S("logging", "archive_size_mb", "logging.archive_size_mb", "integer 1..3600", "10", "Maximum active log size before rotation."),
        S("logging", "retention", "logging.retention", "integer 1..3600", "7", "Number of archived log files retained.")
    ];

    public static IReadOnlyList<CliParameter> GlobalParameters { get; } =
    [
        new("--config PATH", "Use an explicit TOML sidecar instead of automatic config discovery."),
        new("--help, -h, /?", "Show help without loading configuration."),
        new("--version", "Print the product revision without loading configuration.")
    ];

    public static IReadOnlyList<CliCommandReference> CliCommands { get; } =
    [
        C("run", "Run enabled roles in the foreground; this is also the default when no command is supplied."),
        C("help", "Show help without loading configuration."),
        C("status", "Fetch and print Server catalog status."),
        C("discover", "Run one discovery pass and print the normalized snapshot without publishing it."),
        C("doctor", "Check configured secret sources, providers, and target paths."),
        C("tls validate", "Load and validate the configured certificate, private key, validity, Server Authentication usage, and chain without changing the running Server."),
        C("tls reload", "Ask the local running Server to validate and adopt the certificate files for new TLS connections without restarting."),
        C("tls renew", "Issue and immediately adopt a fresh 30-year self-signed certificate using the existing RSA private key, preserving the public-key pin."),
        C("tls regenerate", "Ask the local running Server to transactionally replace its current certificate with a new 30-year self-signed certificate and adopt it immediately.",
            new CliParameter("--force", "Allow replacement when the current certificate is not self-signed.")),
        C("config validate", "Parse and validate the selected TOML configuration."),
        C("config preview TARGET", "Discover once and print a generated artifact.", new CliParameter("TARGET", "opencode or litellm; defaults to opencode when omitted by the current parser.")),
        C("sync --once", "Perform one Client artifact synchronization cycle.", new CliParameter("--once", "Required sync action selector.")),
        C("service ACTION", "Manage the operating-system service.",
            new CliParameter("ACTION", "install, repair, start, stop, status, or uninstall; defaults to status."),
            new CliParameter("--dry-run", "Print service installation intent without changing service state.")),
        C("update check", "Check GitHub Releases for a newer stable revision."),
        C("update apply", "Download, verify, stage, and activate a stable update.",
            new CliParameter("--service", "Restart and health-check the registered service during activation."),
            new CliParameter("--allow-downgrade", "Allow applying an older stable revision.")),
        C("update rollback", "Restore the retained rollback executable.",
            new CliParameter("--service", "Restart and health-check the registered service after rollback.")),
        C("docs generate", "Generate the CLI and TOML reference documents without loading configuration.",
            new CliParameter("--output DIRECTORY", "Destination directory; defaults to docs under the current directory."),
            new CliParameter("--check", "Do not write; return nonzero if committed references differ.")),
        new("update activate-staged", "Internal Linux post-exit update activator.",
        [
            new("--wait-process-id PID", "Initiating process to wait for."),
            new("--source PATH", "Staged replacement executable."),
            new("--destination PATH", "Installed executable destination."),
            new("--fallback PATH", "Rollback executable."),
            new("--service", "Restart and verify the service."),
            new("--health-url URI", "Optional authenticated Server health URL."),
            new("--result PATH", "Activation result file.")
        ], Internal: true)
    ];

    public static IReadOnlyCollection<string> AllowedKeys(string scope) => ConfigurationSettings
        .Where(x => x.Scope == scope)
        .Select(x => x.Key)
        .ToHashSet(StringComparer.Ordinal);

    public static string CliHelpText()
    {
        var builder = new StringBuilder("LLM Glue - discover local LLM deployments and synchronize client configuration\n\nUsage:\n  llm-glue [run] [--config PATH]\n  llm-glue <command> [options] [--config PATH]\n\nCommands:\n");
        foreach (var command in CliCommands.Where(x => !x.Internal))
            builder.Append("  ").Append(command.Syntax.PadRight(30)).Append(command.Summary).Append('\n');
        builder.Append("\nHelp aliases: help, --help, /?, -h\n")
            .Append("Example: llm-glue config preview opencode [--config PATH]\n")
            .Append("Config search: llm-glue.conf then llm-glue.toml beside the executable, then the platform configuration folder.\n")
            .Append("Use docs/cli-reference.md for every command and parameter.\n");
        return builder.ToString();
    }

    public static string CliMarkdown()
    {
        var builder = Header("CLI reference", "This file is generated from the executable's typed CLI reference catalog. Do not edit it manually.");
        builder.Append("## Global parameters\n\n| Parameter | Description |\n|---|---|\n");
        foreach (var item in GlobalParameters) Row(builder, Code(item.Syntax), item.Description);
        foreach (var command in CliCommands)
        {
            builder.Append("\n## `").Append(command.Syntax).Append("`\n\n").Append(command.Summary).Append("\n");
            if (command.Internal) builder.Append("\n> Internal command. It is documented for completeness and is not an operator workflow.\n");
            if (command.Parameters.Count == 0) builder.Append("\nNo command-specific parameters.\n");
            else
            {
                builder.Append("\n| Parameter | Description |\n|---|---|\n");
                foreach (var item in command.Parameters) Row(builder, Code(item.Syntax), item.Description);
            }
        }
        return builder.ToString();
    }

    public static string ConfigurationMarkdown()
    {
        var builder = Header("TOML configuration reference", "This file is generated from the same setting catalog used to reject unknown TOML keys. Do not edit it manually.");
        foreach (var group in ConfigurationSettings.GroupBy(x => x.Scope, StringComparer.Ordinal))
        {
            builder.Append("## `").Append(group.Key).Append("`\n\n| Setting | Type | Default / requirement | Description |\n|---|---|---|---|\n");
            foreach (var item in group) Row(builder, Code(item.Path), Code(item.Type), Code(item.Default), item.Description);
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static ConfigurationSetting S(string scope, string key, string path, string type, string defaultValue, string description) => new(scope, key, path, type, defaultValue, description);
    private static CliCommandReference C(string syntax, string summary, params CliParameter[] parameters) => new(syntax, summary, parameters);
    private static StringBuilder Header(string title, string notice) => new StringBuilder("# ").Append(title).Append("\n\n").Append(notice).Append("\n\n");
    private static void Row(StringBuilder builder, params string[] values) => builder.Append('|').Append(string.Join('|', values.Select(Escape))).Append("|\n");
    private static string Escape(string value) => $" {value.Replace("|", "\\|", StringComparison.Ordinal)} ";
    private static string Code(string value) => $"`{value}`";
}
