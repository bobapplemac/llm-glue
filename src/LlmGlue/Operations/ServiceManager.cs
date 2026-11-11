using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using LlmGlue.Core.Configuration;

namespace LlmGlue.Operations;

public static class ServiceManager
{
    private const string WindowsName = "LLMGlue";
    private const string LinuxName = "llm-glue.service";

    public static async Task<int> ExecuteAsync(string action, string executable, GlueOptions options, CancellationToken cancellationToken, bool dryRun = false)
    {
        return action switch
        {
            "install" or "repair" => await InstallAsync(executable, options, cancellationToken, dryRun),
            "uninstall" => await UninstallAsync(cancellationToken),
            "start" => await ControlAsync("start", cancellationToken),
            "stop" => await ControlAsync("stop", cancellationToken),
            "status" => await StatusAsync(cancellationToken),
            _ => throw new ConfigurationException("service command must be install, repair, uninstall, start, stop, or status.")
        };
    }

    private static async Task<int> InstallAsync(string executable, GlueOptions options, CancellationToken cancellationToken, bool dryRun)
    {
        if (Path.GetExtension(executable).Equals(".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Service installation requires the published self-contained executable, not dotnet-hosted development output.");
        if (OperatingSystem.IsWindows())
        {
            var command = $"\"{Path.GetFullPath(executable)}\" run --config \"{options.ConfigPath}\"";
            if (dryRun)
            {
                Console.WriteLine($"Dry run: would create Windows service {WindowsName} as LocalService with binPath {command}");
                return 0;
            }
            var exists = await RunAsync("sc.exe", ["query", WindowsName], cancellationToken, allowedExitCodes: [0, 1060]) == 0;
            if (exists)
            {
                await RunAsync("sc.exe", ["stop", WindowsName], cancellationToken, allowedExitCodes: [0, 1062]);
                await RunAsync("sc.exe", ["config", WindowsName, "binPath=", command, "start=", "auto", "obj=", "NT AUTHORITY\\LocalService"], cancellationToken);
            }
            else
            {
                await RunAsync("sc.exe", ["create", WindowsName, "binPath=", command, "start=", "auto", "obj=", "NT AUTHORITY\\LocalService"], cancellationToken);
            }
            await RunAsync("sc.exe", ["description", WindowsName, "LLM Glue model discovery and configuration synchronization"], cancellationToken);
            Console.WriteLine($"Installed Windows service {WindowsName} for {executable} with config {options.ConfigPath}.");
            return 0;
        }
        if (OperatingSystem.IsLinux())
        {
            var unitPath = "/etc/systemd/system/llm-glue.service";
            var writable = options.Client.Targets.Select(x => Path.GetDirectoryName(Resolve(options, x.Path))).Where(x => x is not null).Distinct(StringComparer.Ordinal).ToArray();
            var unit = new StringBuilder("[Unit]\nDescription=LLM Glue model discovery and configuration synchronization\nAfter=network-online.target\nWants=network-online.target\n\n[Service]\nType=notify\n")
                .Append("ExecStart=\"").Append(EscapeSystemd(executable)).Append("\" run --config \"").Append(EscapeSystemd(options.ConfigPath)).Append("\"\n")
                .Append("Restart=on-failure\nRestartSec=5s\nDynamicUser=yes\nNoNewPrivileges=yes\nPrivateTmp=yes\nProtectSystem=strict\nProtectHome=read-only\nStateDirectory=llm-glue\nLogsDirectory=llm-glue\n");
            foreach (var path in writable) unit.Append("ReadWritePaths=").Append(EscapeSystemd(path!)).Append('\n');
            unit.Append("\n[Install]\nWantedBy=multi-user.target\n");
            if (dryRun)
            {
                Console.WriteLine($"Dry run: would write {unitPath}:\n{unit}");
                return 0;
            }
            await File.WriteAllTextAsync(unitPath, unit.ToString(), new UTF8Encoding(false), cancellationToken);
            await RunAsync("systemctl", ["daemon-reload"], cancellationToken);
            await RunAsync("systemctl", ["enable", LinuxName], cancellationToken);
            Console.WriteLine($"Installed systemd unit {unitPath} for {executable} with config {options.ConfigPath}.");
            return 0;
        }
        throw new PlatformNotSupportedException("Service management supports Windows and Linux.");
    }

    private static async Task<int> UninstallAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await RunAsync("sc.exe", ["stop", WindowsName], cancellationToken, [0, 1060, 1062]);
            await RunAsync("sc.exe", ["delete", WindowsName], cancellationToken, [0, 1060]);
            Console.WriteLine($"Removed Windows service {WindowsName}. Configuration and data were preserved.");
            return 0;
        }
        if (OperatingSystem.IsLinux())
        {
            await RunAsync("systemctl", ["disable", "--now", LinuxName], cancellationToken, [0, 1, 5]);
            if (File.Exists("/etc/systemd/system/llm-glue.service")) File.Delete("/etc/systemd/system/llm-glue.service");
            await RunAsync("systemctl", ["daemon-reload"], cancellationToken);
            Console.WriteLine("Removed /etc/systemd/system/llm-glue.service. Configuration and data were preserved.");
            return 0;
        }
        throw new PlatformNotSupportedException();
    }

    private static async Task<int> ControlAsync(string action, CancellationToken cancellationToken)
    {
        await RunAsync(OperatingSystem.IsWindows() ? "sc.exe" : "systemctl", OperatingSystem.IsWindows() ? [action, WindowsName] : [action, LinuxName], cancellationToken);
        return 0;
    }

    private static async Task<int> StatusAsync(CancellationToken cancellationToken)
    {
        var exit = await RunAsync(OperatingSystem.IsWindows() ? "sc.exe" : "systemctl", OperatingSystem.IsWindows() ? ["query", WindowsName] : ["status", "--no-pager", LinuxName], cancellationToken, [0, 3, 4, 1060]);
        return exit == 0 ? 0 : 3;
    }

    private static async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken, int[]? allowedExitCodes = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout;
        var error = await stderr;
        if (output.Length != 0) Console.Write(output);
        allowedExitCodes ??= [0];
        if (!allowedExitCodes.Contains(process.ExitCode)) throw new InvalidOperationException($"{executable} exited {process.ExitCode}: {error.Trim()}");
        return process.ExitCode;
    }

    private static string Resolve(GlueOptions options, string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, options.BaseDirectory);
    private static string EscapeSystemd(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
