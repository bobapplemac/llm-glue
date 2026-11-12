using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Runtime.Versioning;
using LlmGlue.Core.Configuration;

namespace LlmGlue.Operations;

public static class SetupManager
{
    public static async Task<int> ExecuteAsync(string[] args, string sourceExecutable, CancellationToken cancellationToken)
    {
        var nonInteractive = args.Contains("--non-interactive", StringComparer.Ordinal);
        var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
        var detectedProviders = await DetectProvidersAsync(cancellationToken);
        var detectedTargets = DetectTargets();
        var defaultRoles = detectedProviders.Count != 0 && detectedTargets.Count != 0 ? "agent,server,client" : detectedProviders.Count != 0 ? "agent" : "client";
        var roles = Csv(Option(args, "--roles") ?? Prompt("Enabled roles, comma separated [agent/server/client]", defaultRoles, nonInteractive));
        ValidateValues(roles, ["agent", "server", "client"], "role");
        var agent = roles.Contains("agent", StringComparer.Ordinal);
        var server = roles.Contains("server", StringComparer.Ordinal);
        var client = roles.Contains("client", StringComparer.Ordinal);

        var providerNames = agent ? Csv(Option(args, "--providers") ?? Prompt("Agent providers, comma separated [ollama/llama.cpp]", detectedProviders.Count == 0 ? "ollama" : string.Join(',', detectedProviders), nonInteractive)) : [];
        if (agent) ValidateValues(providerNames, ["ollama", "llama.cpp"], "provider");
        var targetNames = client ? Csv(Option(args, "--targets") ?? Prompt("Client targets, comma separated [opencode/litellm]", detectedTargets.Count == 0 ? "opencode" : string.Join(',', detectedTargets), nonInteractive)) : [];
        if (client) ValidateValues(targetNames, ["opencode", "litellm"], "target");

        var remote = !server && (agent || client) ? UriValue(args, "--server-url", Prompt("Remote LLM Glue Server URL", "https://replace-with-your-server:8765", nonInteractive)) : null;
        var remoteWrite = !server && agent ? Secret(args, "--write-token", "Remote Server write token", nonInteractive) : null;
        var remoteRead = !server && client ? Secret(args, "--read-token", "Remote Server read token", nonInteractive) : null;
        var pin = !server && (agent || client) ? Option(args, "--server-public-key-sha256") : null;

        var providers = new List<SetupProvider>();
        if (providerNames.Contains("ollama", StringComparer.Ordinal)) providers.Add(new("ollama", "local-ollama", UriValue(args, "--ollama-url", Prompt("Ollama URL", "http://127.0.0.1:11434", nonInteractive))!));
        if (providerNames.Contains("llama.cpp", StringComparer.Ordinal)) providers.Add(new("llama.cpp", "local-llama", UriValue(args, "--llama-url", Prompt("llama.cpp URL", "http://127.0.0.1:8081", nonInteractive))!, Option(args, "--model-directory") ?? Prompt("GGUF model directory", OperatingSystem.IsWindows() ? @"D:\Models\GGUF" : "/mnt/models/gguf", nonInteractive)));

        var readToken = server ? Option(args, "--read-token") ?? SetupConfiguration.RandomToken() : null;
        var writeToken = server ? Option(args, "--write-token") ?? SetupConfiguration.RandomToken() : null;
        var listen = server ? UriValue(args, "--listen", Prompt("LLM Glue listen URL", "https://0.0.0.0:8765", nonInteractive))! : new Uri("https://127.0.0.1:8765");
        var names = server ? Csv(Option(args, "--tls-server-names") ?? Prompt("TLS/SNI server names", $"localhost,{Environment.MachineName.ToLowerInvariant()}", nonInteractive)) : [];
        var inferenceUrlText = server ? Option(args, "--litellm-url") ?? Prompt("Externally reachable LiteLLM URL for generated OpenCode routes", "http://replace-with-your-litellm:4000", nonInteractive) : null;
        var inferenceUrl = inferenceUrlText is null ? null : new Uri(inferenceUrlText, UriKind.Absolute);
        var inferenceKey = server ? Secret(args, "--inference-api-key", "LiteLLM API key embedded in authenticated OpenCode artifacts", nonInteractive) : null;

        var paths = CanonicalPaths();
        var targets = new List<SetupTarget>();
        if (targetNames.Contains("opencode", StringComparer.Ordinal)) targets.Add(new("opencode", Option(args, "--opencode-path") ?? paths.OpenCodePath));
        if (targetNames.Contains("litellm", StringComparer.Ordinal)) targets.Add(new("litellm",
            Option(args, "--litellm-generated-path") ?? (OperatingSystem.IsWindows() ? @"C:\ProgramData\LiteLLM\llm-glue-models.yaml" : "/var/lib/llm-glue/litellm/llm-glue-models.yaml"),
            Option(args, "--litellm-primary-path") ?? (OperatingSystem.IsWindows() ? @"C:\ProgramData\LiteLLM\config.yaml" : "/opt/litellm/config.yaml"),
            Option(args, "--litellm-service") ?? "litellm.service",
            UriValue(args, "--litellm-health-url", "http://127.0.0.1:4000/health/liveliness")));

        var configuration = SetupConfiguration.Render(new(
            new(agent, server, client), providers, targets, remote, remoteRead, remoteWrite,
            pin is null, pin, listen, names, readToken, writeToken, inferenceUrl, inferenceKey,
            paths.StateDirectory, paths.LogFile));

        if (dryRun)
        {
            Console.WriteLine($"Dry run: would install {sourceExecutable} to {paths.Executable}, write protected configuration {paths.Config}, register the system service, and start it.");
            return 0;
        }
        RequireAdministrator();
        if (File.Exists(paths.Config) && !args.Contains("--replace-config", StringComparer.Ordinal))
            throw new ConfigurationException($"Configuration already exists at {paths.Config}. Use setup --replace-config to replace it explicitly.");

        Directory.CreateDirectory(Path.GetDirectoryName(paths.Executable)!);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Config)!);
        if (!Path.GetFullPath(sourceExecutable).Equals(Path.GetFullPath(paths.Executable), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            File.Copy(sourceExecutable, paths.Executable, true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(paths.Executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        await File.WriteAllTextAsync(paths.Config, configuration, new UTF8Encoding(false), cancellationToken);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(paths.Config, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _ = TomlConfigLoader.Load(paths.Config);
        if (OperatingSystem.IsWindows()) await ProtectWindowsConfigAsync(paths.Config, cancellationToken);
        if (OperatingSystem.IsWindows()) EnsureWindowsMachinePath(Path.GetDirectoryName(paths.Executable)!);
        var options = TomlConfigLoader.Load(paths.Config);
        await ServiceManager.ExecuteAsync("install", paths.Executable, options, cancellationToken);
        await ServiceManager.ExecuteAsync("start", paths.Executable, options, cancellationToken);
        Console.WriteLine($"Installed LLM Glue to {paths.Executable} with configuration {paths.Config}.");
        return 0;
    }

    public static (string Executable, string Config, string StateDirectory, string LogFile, string OpenCodePath) CanonicalPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LLMGlue");
            return (Path.Combine(data, "bin", "llm-glue.exe"), Path.Combine(data, "llm-glue.toml"), Path.Combine(data, "state"), Path.Combine(data, "logs", "llm-glue.log"), @"C:\ProgramData\opencode\opencode.jsonc");
        }
        if (OperatingSystem.IsLinux()) return ("/usr/local/bin/llm-glue", "/etc/llm-glue/llm-glue.toml", "/var/lib/llm-glue", "/var/log/llm-glue/llm-glue.log", "/etc/opencode/opencode.jsonc");
        throw new PlatformNotSupportedException("Setup supports Windows and Linux.");
    }

    private static async Task<IReadOnlyList<string>> DetectProvidersAsync(CancellationToken cancellationToken)
    {
        var found = new List<string>();
        if (CommandExists(OperatingSystem.IsWindows() ? "ollama.exe" : "ollama") || await ReachableAsync("http://127.0.0.1:11434/api/version", cancellationToken)) found.Add("ollama");
        if ((!OperatingSystem.IsWindows() && File.Exists("/opt/llama-cpp/router/models.ini")) || await ReachableAsync("http://127.0.0.1:8081/health", cancellationToken)) found.Add("llama.cpp");
        return found;
    }

    private static IReadOnlyList<string> DetectTargets()
    {
        var found = new List<string>();
        if (CommandExists(OperatingSystem.IsWindows() ? "opencode.exe" : "opencode") || Directory.Exists(OperatingSystem.IsWindows() ? @"C:\ProgramData\opencode" : "/etc/opencode")) found.Add("opencode");
        if (File.Exists(OperatingSystem.IsWindows() ? @"C:\ProgramData\LiteLLM\config.yaml" : "/opt/litellm/config.yaml")) found.Add("litellm");
        return found;
    }

    private static async Task<bool> ReachableAsync(string url, CancellationToken cancellationToken)
    {
        try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) }; using var response = await client.GetAsync(url, cancellationToken); return response.IsSuccessStatusCode; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    private static bool CommandExists(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(directory => File.Exists(Path.Combine(directory, name)));
    }

    [SupportedOSPlatform("windows")]
    private static async Task ProtectWindowsConfigAsync(string path, CancellationToken cancellationToken)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Cannot determine current Windows identity.");
        await RunAsync("icacls.exe", [path, "/inheritance:r", "/grant:r", $"*{sid}:(F)", "*S-1-5-18:(F)", "*S-1-5-32-544:(F)"], cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    internal static void EnsureWindowsMachinePath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var values = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (values.Any(x => Path.GetFullPath(x).Equals(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))) return;
        values.Add(directory);
        Environment.SetEnvironmentVariable("Path", string.Join(';', values), EnvironmentVariableTarget.Machine);
        Console.WriteLine($"Added {directory} to the machine PATH; new terminals will inherit it.");
    }

    [SupportedOSPlatform("windows")]
    internal static void RemoveWindowsMachinePath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var values = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !Path.GetFullPath(x).Equals(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase)).ToArray();
        Environment.SetEnvironmentVariable("Path", string.Join(';', values), EnvironmentVariableTarget.Machine);
    }

    private static void RequireAdministrator()
    {
        if (OperatingSystem.IsWindows())
        {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("Setup requires an elevated Administrator terminal.");
        }
        else if (OperatingSystem.IsLinux() && !string.Equals(Environment.UserName, "root", StringComparison.Ordinal)) throw new InvalidOperationException("Setup requires root privileges.");
    }

    private static string Prompt(string label, string defaultValue, bool nonInteractive)
    {
        if (nonInteractive) return defaultValue;
        Console.Write($"{label} ({defaultValue}): ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static string Secret(string[] args, string name, string label, bool nonInteractive)
    {
        var supplied = Option(args, name);
        if (supplied is not null) return supplied;
        if (nonInteractive) throw new ConfigurationException($"{name} is required.");
        Console.Write($"{label}: ");
        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (builder.Length != 0) builder.Length--; }
            else if (!char.IsControl(key.KeyChar)) builder.Append(key.KeyChar);
        }
        Console.WriteLine();
        return builder.Length == 0 ? throw new ConfigurationException($"{label} cannot be empty.") : builder.ToString();
    }

    private static string[] Csv(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
    private static void ValidateValues(IReadOnlyList<string> values, IReadOnlyCollection<string> allowed, string label)
    {
        if (values.Count == 0) throw new ConfigurationException($"At least one {label} is required.");
        var invalid = values.Where(x => !allowed.Contains(x)).ToArray();
        if (invalid.Length != 0) throw new ConfigurationException($"Invalid {label}: {string.Join(", ", invalid)}.");
    }
    private static string? Option(string[] args, string name) { var index = Array.IndexOf(args, name); return index >= 0 ? args.ElementAtOrDefault(index + 1) : null; }
    private static Uri? UriValue(string[] args, string name, string? fallback) { var value = Option(args, name) ?? fallback; return value is null ? null : new Uri(value, UriKind.Absolute); }
    private static async Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} exited with code {process.ExitCode}.");
    }
}
