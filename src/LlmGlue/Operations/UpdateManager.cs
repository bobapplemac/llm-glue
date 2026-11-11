using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LlmGlue.Operations;

public sealed record UpdateInfo(int Revision, string Tag, string AssetName, Uri AssetUrl, Uri ChecksumsUrl);
public sealed record UpdateApplyOptions(bool AllowDowngrade = false, bool ServiceMode = false, Uri? HealthUrl = null);

public static partial class UpdateManager
{
    private const string Repository = "bobapplemac/llm-glue";
    public static int CurrentRevision => ProductInfo.RevisionNumber;

    public static async Task<UpdateInfo?> CheckAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd(ProductInfo.UserAgent);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<Release>(cancellationToken: cancellationToken) ?? throw new InvalidDataException("GitHub returned an empty release.");
        if (release.Draft || release.Prerelease || !RevisionPattern().Match(release.TagName).Success) return null;
        var revision = int.Parse(release.TagName.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
        if (revision <= CurrentRevision) return null;
        var rid = OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsLinux() ? "linux-x64" : throw new PlatformNotSupportedException();
        var suffix = OperatingSystem.IsWindows() ? ".zip" : ".tar.gz";
        var name = $"llm-glue-{release.TagName}-{rid}{suffix}";
        var asset = release.Assets.SingleOrDefault(x => x.Name == name) ?? throw new InvalidDataException($"Release is missing {name}.");
        var sums = release.Assets.SingleOrDefault(x => x.Name == "SHA256SUMS") ?? throw new InvalidDataException("Release is missing SHA256SUMS.");
        return new(revision, release.TagName, name, asset.BrowserDownloadUrl, sums.BrowserDownloadUrl);
    }

    public static async Task ApplyAsync(HttpClient httpClient, string executablePath, UpdateApplyOptions options, CancellationToken cancellationToken)
    {
        var update = await CheckAsync(httpClient, cancellationToken) ?? throw new InvalidOperationException("No newer stable release is available.");
        if (!options.AllowDowngrade && update.Revision < CurrentRevision) throw new InvalidOperationException("Downgrades require explicit authorization.");
        var executable = Path.GetFullPath(executablePath);
        if (Path.GetExtension(executable).Equals(".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Self-update requires a published self-contained executable.");
        var directory = Path.GetDirectoryName(executable)!;
        var stage = Path.Combine(directory, ".llm-glue-update", update.Tag);
        Directory.CreateDirectory(stage);
        var archive = Path.Combine(stage, update.AssetName);
        var sums = Path.Combine(stage, "SHA256SUMS");
        await DownloadAsync(httpClient, update.AssetUrl, archive, cancellationToken);
        await DownloadAsync(httpClient, update.ChecksumsUrl, sums, cancellationToken);
        var expected = (await File.ReadAllLinesAsync(sums, cancellationToken)).Select(x => x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).SingleOrDefault(x => x.Length >= 2 && x[^1] == update.AssetName)?[0]
            ?? throw new InvalidDataException("SHA256SUMS does not name the selected asset.");
        await using (var input = File.OpenRead(archive))
        {
            var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum verification failed.");
        }
        var extracted = Path.Combine(stage, "extracted");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
        Directory.CreateDirectory(extracted);
        if (OperatingSystem.IsWindows()) ZipFile.ExtractToDirectory(archive, extracted);
        else await ExtractTarGzAsync(archive, extracted, cancellationToken);
        var replacement = Path.Combine(extracted, OperatingSystem.IsWindows() ? "llm-glue.exe" : "llm-glue");
        if (!File.Exists(replacement)) throw new InvalidDataException("Update archive does not contain the expected executable.");
        var rollback = executable + ".rollback";
        File.Copy(executable, rollback, true);
        if (OperatingSystem.IsWindows())
        {
            var helper = Path.Combine(stage, "apply-update.ps1");
            var script = options.ServiceMode ? WindowsServiceHelperScript : WindowsStandaloneHelperScript;
            await File.WriteAllTextAsync(helper, script, cancellationToken);
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-File", helper, "-ProcessId", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "-Source", replacement, "-Destination", executable, "-Rollback", rollback, "-ServiceMode", options.ServiceMode.ToString(), "-HealthUrl", options.HealthUrl?.AbsoluteUri ?? string.Empty, "-ResultPath", Path.Combine(stage, "update-result.log") }) start.ArgumentList.Add(argument);
            Process.Start(start);
            Console.WriteLine($"Verified {update.Tag}; replacement is staged and will apply after this process exits. ServiceMode={options.ServiceMode}. Result: {Path.Combine(stage, "update-result.log")}. Rollback: {rollback}");
        }
        else
        {
            if (options.ServiceMode)
            {
                await ServiceUpdateCoordinator.ReplaceAndVerifyAsync(
                    executable, replacement, rollback,
                    token => RunProcessAsync("systemctl", ["restart", "llm-glue.service"], token),
                    token => VerifyLinuxServiceAsync(options.HealthUrl, token),
                    preserveUnixMode: true,
                    cancellationToken);
            }
            else
            {
                File.Move(replacement, executable, true);
                File.SetUnixFileMode(executable, File.GetUnixFileMode(rollback));
            }
            Console.WriteLine($"Updated to {update.Tag}. Rollback: {rollback}");
        }
    }

    public static async Task RollbackAsync(string executablePath, bool serviceMode, Uri? healthUrl, CancellationToken cancellationToken)
    {
        var executable = Path.GetFullPath(executablePath);
        var rollback = executable + ".rollback";
        if (!File.Exists(rollback)) throw new FileNotFoundException("No rollback executable exists.", rollback);
        var fallback = executable + ".pre-rollback";
        File.Copy(executable, fallback, true);
        if (OperatingSystem.IsWindows())
        {
            var stage = Path.Combine(Path.GetDirectoryName(executable)!, ".llm-glue-update", "rollback");
            Directory.CreateDirectory(stage);
            var helper = Path.Combine(stage, "apply-rollback.ps1");
            await File.WriteAllTextAsync(helper, serviceMode ? WindowsServiceHelperScript : WindowsStandaloneHelperScript, cancellationToken);
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-File", helper, "-ProcessId", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "-Source", rollback, "-Destination", executable, "-Rollback", fallback, "-ServiceMode", serviceMode.ToString(), "-HealthUrl", healthUrl?.AbsoluteUri ?? string.Empty, "-ResultPath", Path.Combine(stage, "rollback-result.log") }) start.ArgumentList.Add(argument);
            Process.Start(start);
            Console.WriteLine($"Rollback is staged and will apply after this process exits. Result: {Path.Combine(stage, "rollback-result.log")}");
            return;
        }
        if (serviceMode)
        {
            await ServiceUpdateCoordinator.ReplaceAndVerifyAsync(
                executable, rollback, fallback,
                token => RunProcessAsync("systemctl", ["restart", "llm-glue.service"], token),
                token => VerifyLinuxServiceAsync(healthUrl, token),
                preserveUnixMode: true,
                cancellationToken);
        }
        else
        {
            File.Copy(rollback, executable, true);
            File.SetUnixFileMode(executable, File.GetUnixFileMode(rollback));
        }
        Console.WriteLine("Rollback executable restored and verified.");
    }

    private static async Task DownloadAsync(HttpClient client, Uri url, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(ProductInfo.UserAgent);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        output.Flush(true);
    }

    private static async Task ExtractTarGzAsync(string archive, string destination, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archive);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await System.Formats.Tar.TarFile.ExtractToDirectoryAsync(gzip, destination, overwriteFiles: false, cancellationToken);
    }

    private static async Task RunProcessAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} exited {process.ExitCode}: {(await stderr).Trim()} {(await stdout).Trim()}".Trim());
    }

    private static async Task<bool> VerifyLinuxServiceAsync(Uri? healthUrl, CancellationToken cancellationToken)
    {
        using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                if (healthUrl is not null)
                {
                    using var response = await healthClient.GetAsync(healthUrl, cancellationToken);
                    if (response.IsSuccessStatusCode) return true;
                }
                else
                {
                    await RunProcessAsync("systemctl", ["is-active", "--quiet", "llm-glue.service"], cancellationToken);
                    return true;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) { }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        return false;
    }

    internal const string WindowsStandaloneHelperScript = """
param([int]$ProcessId,[string]$Source,[string]$Destination,[string]$Rollback,[bool]$ServiceMode,[string]$HealthUrl,[string]$ResultPath)
$ErrorActionPreference='Stop'
try {
  Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
  Copy-Item -LiteralPath $Source -Destination $Destination -Force
  Set-Content -LiteralPath $ResultPath -Value 'success' -Encoding utf8NoBOM
} catch {
  Set-Content -LiteralPath $ResultPath -Value "failed: $($_.Exception.Message)" -Encoding utf8NoBOM
  exit 1
}
""";

    internal const string WindowsServiceHelperScript = """
param([int]$ProcessId,[string]$Source,[string]$Destination,[string]$Rollback,[bool]$ServiceMode,[string]$HealthUrl,[string]$ResultPath)
$ErrorActionPreference='Stop'
$service='LLMGlue'
function Invoke-Sc([string[]]$Arguments,[int[]]$Allowed=@(0)) {
  & sc.exe @Arguments *> $null
  if ($LASTEXITCODE -notin $Allowed) { throw "sc.exe $($Arguments -join ' ') failed with $LASTEXITCODE" }
}
function Wait-State([string]$State) {
  for ($i=0; $i -lt 30; $i++) {
    $text = (& sc.exe query $service 2>&1 | Out-String)
    if ($LASTEXITCODE -eq 0 -and $text -match "STATE\s*:\s*\d+\s+$State") { return }
    Start-Sleep -Seconds 1
  }
  throw "Service did not reach $State"
}
function Wait-Healthy {
  for ($i=0; $i -lt 30; $i++) {
    if ($HealthUrl) {
      try { $response=Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 2; if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) { return } } catch {}
    } else {
      $text = (& sc.exe query $service 2>&1 | Out-String)
      if ($LASTEXITCODE -eq 0 -and $text -match 'STATE\s*:\s*\d+\s+RUNNING') { return }
    }
    Start-Sleep -Seconds 1
  }
  throw 'Updated service did not become healthy'
}
Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
try {
  Invoke-Sc @('stop',$service) @(0,1062)
  Wait-State 'STOPPED'
  Copy-Item -LiteralPath $Source -Destination $Destination -Force
  Invoke-Sc @('start',$service)
  Wait-Healthy
  Set-Content -LiteralPath $ResultPath -Value 'success' -Encoding utf8NoBOM
} catch {
  $activation=$_.Exception.Message
  try {
    Invoke-Sc @('stop',$service) @(0,1062)
    Wait-State 'STOPPED'
    Copy-Item -LiteralPath $Rollback -Destination $Destination -Force
    Invoke-Sc @('start',$service)
    Wait-Healthy
    Set-Content -LiteralPath $ResultPath -Value "failed and rolled back: $activation" -Encoding utf8NoBOM
  } catch {
    Set-Content -LiteralPath $ResultPath -Value "failed; rollback unhealthy: $activation; $($_.Exception.Message)" -Encoding utf8NoBOM
  }
  exit 1
}
""";

    [GeneratedRegex("^r[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();

    private sealed record Release(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string TagName,
        bool Draft,
        bool Prerelease,
        ReleaseAsset[] Assets);
    private sealed record ReleaseAsset(string Name, [property: System.Text.Json.Serialization.JsonPropertyName("browser_download_url")] Uri BrowserDownloadUrl);
}
