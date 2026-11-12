using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Client;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;

namespace LlmGlue.Operations;

public static class UninstallManager
{
    public static async Task<int> ExecuteAsync(GlueOptions options, string executable, bool clean, bool dryRun, CancellationToken cancellationToken)
    {
        var canonical = SetupManager.CanonicalPaths();
        if (dryRun)
        {
            Console.WriteLine($"Dry run: would stop/remove the system service, reconcile {options.Client.Targets.Count} managed target(s), and remove {canonical.Executable}." + (clean ? $" Clean mode would also remove {canonical.Config}, {canonical.StateDirectory}, and {Path.GetDirectoryName(canonical.LogFile)}." : " Configuration, state, and logs would remain."));
            return 0;
        }
        RequireAdministrator();
        if (clean && options.Client.Targets.Any(x => x.Type == "litellm"))
            throw new ConfigurationException("Clean uninstall cannot yet prove ownership of the operator LiteLLM include/mount. Run regular uninstall or remove that exact reference before retrying clean uninstall; no service or files were changed.");

        await ServiceManager.ExecuteAsync("uninstall", executable, options, cancellationToken);
        await CleanupTargetsAsync(options, clean, target => target.Type == "litellm"
            ? new ServiceRestartActivator(target.ServiceName!, new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, target.HealthUrl)
            : new NoOpTargetActivator(), cancellationToken);

        var delete = new List<string> { canonical.Executable, canonical.Executable + ".rollback", Path.Combine(Path.GetDirectoryName(canonical.Executable)!, ".llm-glue-update") };
        if (clean)
        {
            delete.Add(canonical.Config);
            delete.Add(canonical.StateDirectory);
            delete.Add(Path.GetDirectoryName(canonical.LogFile)!);
        }
        if (OperatingSystem.IsWindows()) StartWindowsFinalizer(executable, delete, clean);
        else
        {
            DeletePaths(delete);
            if (clean && OperatingSystem.IsLinux())
            {
                await RunAsync("userdel", ["llm-glue"], cancellationToken, [0, 6]);
                await RunAsync("groupdel", ["llm-glue"], cancellationToken, [0, 6, 8]);
            }
        }
        Console.WriteLine(clean ? "Clean uninstall initiated; all owned system files are being removed." : "Uninstalled service and binary; configuration, state, and logs were preserved.");
        return 0;
    }

    public static async Task<int> FinalizeWindowsAsync(int waitProcessId, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        try { using var process = Process.GetProcessById(waitProcessId); await process.WaitForExitAsync(cancellationToken); }
        catch (ArgumentException) { }
        DeletePaths(paths);
        return 0;
    }

    internal static async Task CleanupTargetsAsync(GlueOptions options, bool clean, Func<TargetOptions, ITargetActivator> activatorFactory, CancellationToken cancellationToken)
    {
        foreach (var target in options.Client.Targets)
        {
            var path = Resolve(options, target.Path);
            if (target.Type == "opencode")
            {
                var source = EffectiveClientSource(options);
                var content = Encoding.UTF8.GetBytes("{\"providers\":{}}\n");
                var empty = new GeneratedArtifact("application/json", content, Convert.ToHexStringLower(SHA256.HashData(content)), 1);
                await new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), $"source:{source.AbsoluteUri.TrimEnd('/')}", false).ApplyAsync(empty, cancellationToken);
            }
            else
            {
                var empty = await new LiteLlmGenerator(liteLlmNamespace: options.Server.LiteLlmNamespace)
                    .GenerateAsync(DiscoverySnapshot.Create(DateTimeOffset.UtcNow, []), cancellationToken);
                await new LiteLlmTarget(path, Resolve(options, target.PrimaryConfigPath!), new AtomicFileInstaller(), activatorFactory(target)).ApplyAsync(empty, cancellationToken);
                if (clean) Console.WriteLine($"Retained empty LiteLLM include {path} because its reference is operator-owned; remove both together after verifying the primary configuration.");
            }
        }
    }

    private static void StartWindowsFinalizer(string executable, IReadOnlyList<string> paths, bool clean)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"llm-glue-uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(executable, temporary, true);
        var start = new ProcessStartInfo(temporary) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("uninstall");
        start.ArgumentList.Add("finalize-windows");
        start.ArgumentList.Add("--wait-process-id");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var path in paths) { start.ArgumentList.Add("--delete"); start.ArgumentList.Add(path); }
        _ = Process.Start(start) ?? throw new InvalidOperationException("Could not start uninstall finalizer.");
        _ = clean;
    }

    private static void DeletePaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private static Uri EffectiveClientSource(GlueOptions options)
    {
        if (options.Client.Source is not null) return options.Client.Source;
        var builder = new UriBuilder(options.Server.Listen);
        if (builder.Scheme == Uri.UriSchemeHttps) builder.Host = "localhost";
        else if (builder.Host is "0.0.0.0" or "::" or "[::]" or "*" or "+") builder.Host = "127.0.0.1";
        return builder.Uri;
    }

    private static string Resolve(GlueOptions options, string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, options.BaseDirectory);
    private static void RequireAdministrator()
    {
        if (OperatingSystem.IsWindows())
        {
            var principal = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
            if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("Uninstall requires an elevated Administrator terminal.");
        }
        else if (OperatingSystem.IsLinux() && Environment.UserName != "root") throw new InvalidOperationException("Uninstall requires root privileges.");
    }

    private static async Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken, int[] allowed)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (!allowed.Contains(process.ExitCode)) throw new InvalidOperationException($"{executable} exited {process.ExitCode}: {(await error).Trim()}");
    }
}
