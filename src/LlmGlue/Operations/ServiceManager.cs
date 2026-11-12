using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using LlmGlue.Core.Configuration;

namespace LlmGlue.Operations;

public static class ServiceManager
{
    private const string WindowsName = "LLMGlue";
    private const string LinuxName = "llm-glue.service";
    private const string LinuxUser = "llm-glue";

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
                Console.WriteLine($"Dry run: would grant LocalService read/execute access to the executable, read access to {options.ConfigPath}, modify access only to configured state/log/target directories, and create Windows service {WindowsName} with binPath {command}");
                return 0;
            }
            await PrepareWindowsPermissionsAsync(executable, options, cancellationToken);
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
            var writable = options.Client.Targets.Select(x => Path.GetDirectoryName(Resolve(options, x.Path)))
                .Where(x => x is not null).Cast<string>().ToList();
            if (options.Roles.Server)
            {
                writable.Add(Resolve(options, options.Server.StateDirectory));
                writable.AddRange(TlsWritableDirectories(options));
            }
            writable.Add(Path.GetDirectoryName(Resolve(options, options.Logging.File))!);
            var unit = new StringBuilder("[Unit]\nDescription=LLM Glue model discovery and configuration synchronization\nAfter=network-online.target\nWants=network-online.target\n\n[Service]\nType=notify\n")
                .Append("ExecStart=\"").Append(EscapeSystemd(executable)).Append("\" run --config \"").Append(EscapeSystemd(options.ConfigPath)).Append("\"\n")
                .Append("Restart=on-failure\nRestartSec=5s\nUser=llm-glue\nGroup=llm-glue\nNoNewPrivileges=yes\nPrivateTmp=yes\nProtectSystem=strict\nProtectHome=read-only\nStateDirectory=llm-glue\nStateDirectoryMode=0750\nLogsDirectory=llm-glue\nLogsDirectoryMode=0750\n");
            foreach (var path in writable.Distinct(StringComparer.Ordinal)) unit.Append("ReadWritePaths=\"").Append(EscapeSystemd(path)).Append("\"\n");
            unit.Append("\n[Install]\nWantedBy=multi-user.target\n");
            if (dryRun)
            {
                Console.WriteLine($"Dry run: would ensure fixed system account {LinuxUser}, grant it read/execute access to the executable, group-read access to {options.ConfigPath}, and write access only to newly created or already-authorized configured state/log/target directories; then write {unitPath}:\n{unit}");
                return 0;
            }
            await PrepareLinuxPermissionsAsync(executable, options, writable.Distinct(StringComparer.Ordinal).ToArray(), cancellationToken);
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
            Console.WriteLine("Removed /etc/systemd/system/llm-glue.service. Configuration, data, permissions, and the fixed llm-glue account were preserved.");
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

    private static async Task PrepareWindowsPermissionsAsync(string executable, GlueOptions options, CancellationToken cancellationToken)
    {
        var executablePath = Path.GetFullPath(executable);
        await RunAsync("icacls.exe", [executablePath, "/grant", "*S-1-5-19:(RX)"], cancellationToken);
        await RunAsync("icacls.exe", [options.ConfigPath, "/grant", "*S-1-5-19:(R)"], cancellationToken);
        foreach (var tlsFile in TlsFiles(options).Where(File.Exists))
            await RunAsync("icacls.exe", [tlsFile, "/grant", "*S-1-5-19:(R)"], cancellationToken);
        var directories = options.Client.Targets.Select(x => Path.GetDirectoryName(Resolve(options, x.Path)))
            .Where(x => x is not null).Cast<string>().ToList();
        if (options.Roles.Server)
        {
            directories.Add(Resolve(options, options.Server.StateDirectory));
            directories.AddRange(TlsWritableDirectories(options));
        }
        directories.Add(Path.GetDirectoryName(Resolve(options, options.Logging.File))!);
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            Directory.CreateDirectory(directory);

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            await RunAsync("icacls.exe", [directory, "/grant", "*S-1-5-19:(M)"], cancellationToken);
    }

    private static async Task PrepareLinuxPermissionsAsync(string executable, GlueOptions options, IReadOnlyList<string> writableDirectories, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var exists = await RunAsync("id", ["-u", LinuxUser], cancellationToken, [0, 1]) == 0;
        if (!exists) await RunAsync("useradd", ["--system", "--home-dir", "/nonexistent", "--shell", "/usr/sbin/nologin", LinuxUser], cancellationToken);
        await RunAsync("chgrp", [LinuxUser, options.ConfigPath], cancellationToken);
        var mode = File.GetUnixFileMode(options.ConfigPath);
        mode |= UnixFileMode.GroupRead;
        mode &= ~(UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        File.SetUnixFileMode(options.ConfigPath, mode);
        await RunAsync("runuser", ["-u", LinuxUser, "--", "test", "-x", Path.GetFullPath(executable)], cancellationToken);
        await RunAsync("runuser", ["-u", LinuxUser, "--", "test", "-r", options.ConfigPath], cancellationToken);
        foreach (var tlsFile in TlsFiles(options).Where(File.Exists))
        {
            await RunAsync("chgrp", [LinuxUser, tlsFile], cancellationToken);
            var tlsMode = File.GetUnixFileMode(tlsFile) | UnixFileMode.GroupRead;
            tlsMode &= ~(UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(tlsFile, tlsMode);
        }
        foreach (var directory in writableDirectories)
        {
            var created = !Directory.Exists(directory);
            if (created)
            {
                Directory.CreateDirectory(directory);
                await RunAsync("chgrp", [LinuxUser, directory], cancellationToken);
                var directoryMode = File.GetUnixFileMode(directory) | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute;
                directoryMode &= ~(UnixFileMode.OtherWrite);
                File.SetUnixFileMode(directory, directoryMode);
            }
            await RunAsync("runuser", ["-u", LinuxUser, "--", "test", "-w", directory], cancellationToken);
        }
    }

    private static IEnumerable<string> TlsFiles(GlueOptions options)
    {
        if (options.Server.TlsCertificatePath is not null) yield return Resolve(options, options.Server.TlsCertificatePath);
        if (options.Server.TlsPrivateKeyPath is not null) yield return Resolve(options, options.Server.TlsPrivateKeyPath);
    }

    private static IEnumerable<string> TlsWritableDirectories(GlueOptions options)
    {
        if (!options.Server.TlsAutoGenerate) yield break;
        foreach (var file in TlsFiles(options)) yield return Path.GetDirectoryName(file)!;
    }
}
