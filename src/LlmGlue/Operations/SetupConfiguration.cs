using System.Security.Cryptography;
using System.Text;
using LlmGlue.Core.Configuration;

namespace LlmGlue.Operations;

public sealed record SetupProvider(string Type, string Name, Uri Url, string? ModelDirectory = null);
public sealed record SetupTarget(string Type, string Path, string? PrimaryConfigPath = null, string? ServiceName = null, Uri? HealthUrl = null);
public sealed record SetupConfigurationOptions(
    RoleOptions Roles,
    IReadOnlyList<SetupProvider> Providers,
    IReadOnlyList<SetupTarget> Targets,
    Uri? RemoteServer,
    string? RemoteReadToken,
    string? RemoteWriteToken,
    bool AllowInvalidRemoteCertificate,
    string? TrustedRemotePublicKeySha256,
    Uri Listen,
    IReadOnlyList<string> TlsServerNames,
    string? ServerReadToken,
    string? ServerWriteToken,
    Uri? OpenCodeInferenceUrl,
    string? OpenCodeInferenceApiKey,
    string StateDirectory,
    string LogFile);

public static class SetupConfiguration
{
    public static string Render(SetupConfigurationOptions value)
    {
        if (!value.Roles.Agent && !value.Roles.Server && !value.Roles.Client) throw new ConfigurationException("At least one role must be enabled.");
        if (value.Roles.Agent && value.Providers.Count == 0) throw new ConfigurationException("The Agent role requires at least one provider.");
        if (value.Roles.Client && value.Targets.Count == 0) throw new ConfigurationException("The Client role requires at least one target.");
        if (!value.Roles.Server && (value.Roles.Agent || value.Roles.Client) && value.RemoteServer is null) throw new ConfigurationException("Remote Agent/Client roles require a Server URL.");
        if (!value.Roles.Server && value.Roles.Agent && string.IsNullOrEmpty(value.RemoteWriteToken)) throw new ConfigurationException("A remote Agent requires a write token.");
        if (!value.Roles.Server && value.Roles.Client && string.IsNullOrEmpty(value.RemoteReadToken)) throw new ConfigurationException("A remote Client requires a read token.");
        if (value.Roles.Server && (string.IsNullOrEmpty(value.ServerReadToken) || string.IsNullOrEmpty(value.ServerWriteToken))) throw new ConfigurationException("The Server role requires read and write tokens.");

        var lines = new List<string>
        {
            "[roles]",
            $"agent = {Bool(value.Roles.Agent)}",
            $"server = {Bool(value.Roles.Server)}",
            $"client = {Bool(value.Roles.Client)}"
        };
        if (value.Roles.Agent)
        {
            if (!value.Roles.Server)
            {
                lines.AddRange(["", "[agent]", $"submit_to = {Toml(value.RemoteServer!.ToString())}", $"write_token = {Toml(value.RemoteWriteToken!)}"]);
                AddTrust(lines, value);
            }
            foreach (var provider in value.Providers)
            {
                lines.AddRange(["", "[[agent.providers]]", $"name = {Toml(provider.Name)}", $"type = {Toml(provider.Type)}", $"url = {Toml(provider.Url.ToString())}"]);
                if (provider.Type == "llama.cpp")
                {
                    if (string.IsNullOrWhiteSpace(provider.ModelDirectory)) throw new ConfigurationException("llama.cpp setup requires a model directory.");
                    lines.AddRange([$"model_directories = [{Toml(provider.ModelDirectory)}]", "inspect_gguf = true", "hash_model_files = false", "probe_mode = \"loaded-only\""]);
                }
            }
        }
        if (value.Roles.Server)
        {
            lines.AddRange(["", "[server]", $"listen = {Toml(value.Listen.ToString())}", "tls_auto_generate = true", $"tls_server_names = [{string.Join(", ", value.TlsServerNames.Select(Toml))}]", $"read_token = {Toml(value.ServerReadToken!)}", $"write_token = {Toml(value.ServerWriteToken!)}", $"state_directory = {Toml(value.StateDirectory)}"]);
            if (value.OpenCodeInferenceUrl is not null)
            {
                if (string.IsNullOrEmpty(value.OpenCodeInferenceApiKey)) throw new ConfigurationException("A LiteLLM OpenCode route requires an inference API key.");
                lines.AddRange([$"opencode_inference_url = {Toml(value.OpenCodeInferenceUrl.ToString())}", "embed_opencode_inference_api_key = true", $"opencode_inference_api_key = {Toml(value.OpenCodeInferenceApiKey)}"]);
            }
        }
        if (value.Roles.Client)
        {
            lines.AddRange(["", "[client]"]);
            if (value.Roles.Server) lines.Add($"token = {Toml(value.ServerReadToken!)}");
            else
            {
                lines.AddRange([$"source = {Toml(value.RemoteServer!.ToString())}", $"token = {Toml(value.RemoteReadToken!)}"]);
                AddTrust(lines, value);
            }
            foreach (var target in value.Targets)
            {
                lines.AddRange(["", "[[client.targets]]", $"type = {Toml(target.Type)}", $"path = {Toml(target.Path)}"]);
                if (target.Type == "litellm")
                {
                    if (target.PrimaryConfigPath is null || target.ServiceName is null) throw new ConfigurationException("LiteLLM setup requires primary config and service paths.");
                    lines.AddRange([$"primary_config_path = {Toml(target.PrimaryConfigPath)}", $"service_name = {Toml(target.ServiceName)}"]);
                    if (target.HealthUrl is not null) lines.Add($"health_url = {Toml(target.HealthUrl.ToString())}");
                }
            }
        }
        lines.AddRange(["", "[logging]", $"file = {Toml(value.LogFile)}", ""]);
        return string.Join('\n', lines);
    }

    public static string RandomToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    private static void AddTrust(List<string> lines, SetupConfigurationOptions value)
    {
        if (value.TrustedRemotePublicKeySha256 is not null) lines.Add($"trusted_server_public_key_sha256 = {Toml(value.TrustedRemotePublicKeySha256)}");
        else lines.Add($"allow_invalid_server_certificate = {Bool(value.AllowInvalidRemoteCertificate)}");
    }

    private static string Bool(bool value) => value ? "true" : "false";
    private static string Toml(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
