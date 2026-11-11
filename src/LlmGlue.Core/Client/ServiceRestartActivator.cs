using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LlmGlue.Core.Client;

public sealed partial class ServiceRestartActivator(string serviceName, HttpClient? healthClient = null, Uri? healthUrl = null) : ITargetActivator
{
    public async Task ActivateAndVerifyAsync(string managedPath, CancellationToken cancellationToken)
    {
        if (!ServiceNamePattern().IsMatch(serviceName)) throw new InvalidOperationException("Service name contains unsupported characters.");
        if (OperatingSystem.IsWindows())
        {
            await RunAsync("sc.exe", ["stop", serviceName], allowAlreadyStopped: true, cancellationToken);
            await RunAsync("sc.exe", ["start", serviceName], allowAlreadyStopped: false, cancellationToken);
        }
        else if (OperatingSystem.IsLinux()) await RunAsync("systemctl", ["restart", serviceName], allowAlreadyStopped: false, cancellationToken);
        else throw new PlatformNotSupportedException("Controlled service activation supports Windows and Linux.");

        if (healthClient is not null && healthUrl is not null)
            await WaitForHealthAsync(healthClient, healthUrl, 60, TimeSpan.FromSeconds(2), cancellationToken);
    }

    internal static async Task WaitForHealthAsync(HttpClient healthClient, Uri healthUrl, int maximumAttempts, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        if (maximumAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        if (retryDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryDelay));

        Exception? last = null;
        // Model routers can legitimately need close to a minute to load their
        // configuration and initialize database-backed middleware after a
        // container recreation. Keep individual requests bounded while allowing
        // a two-minute cold-start qualification window in the production caller.
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            try
            {
                using var response = await healthClient.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode) return;
                last = new HttpRequestException($"Health endpoint returned {(int)response.StatusCode}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { last = ex; }
            if (attempt + 1 < maximumAttempts) await Task.Delay(retryDelay, cancellationToken);
        }
        throw new InvalidOperationException("Service restarted but failed its health check.", last);
    }

    private static async Task RunAsync(string executable, IReadOnlyList<string> arguments, bool allowAlreadyStopped, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 && !(allowAlreadyStopped && OperatingSystem.IsWindows()))
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Service activation failed with exit code {process.ExitCode}: {error.Trim()}");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.@-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNamePattern();
}
