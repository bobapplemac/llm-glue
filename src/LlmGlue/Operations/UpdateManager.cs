using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LlmGlue.Operations;

public sealed record UpdateInfo(int Revision, string Tag, string AssetName, Uri AssetUrl, Uri ChecksumsUrl);

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

    public static async Task ApplyAsync(HttpClient httpClient, string executablePath, bool allowDowngrade, CancellationToken cancellationToken)
    {
        var update = await CheckAsync(httpClient, cancellationToken) ?? throw new InvalidOperationException("No newer stable release is available.");
        if (!allowDowngrade && update.Revision < CurrentRevision) throw new InvalidOperationException("Downgrades require explicit authorization.");
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
            var script = "param([int]$ProcessId,[string]$Source,[string]$Destination)\n$ErrorActionPreference='Stop'\nWait-Process -Id $ProcessId -ErrorAction SilentlyContinue\nCopy-Item -LiteralPath $Source -Destination $Destination -Force\n";
            await File.WriteAllTextAsync(helper, script, cancellationToken);
            Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, ArgumentList = { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-File", helper, "-ProcessId", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "-Source", replacement, "-Destination", executable } });
            Console.WriteLine($"Verified {update.Tag}; replacement is staged and will apply after this process exits. Rollback: {rollback}");
        }
        else
        {
            File.Move(replacement, executable, true);
            File.SetUnixFileMode(executable, File.GetUnixFileMode(rollback));
            Console.WriteLine($"Updated to {update.Tag}. Rollback: {rollback}");
        }
    }

    public static void Rollback(string executablePath)
    {
        var executable = Path.GetFullPath(executablePath);
        var rollback = executable + ".rollback";
        if (!File.Exists(rollback)) throw new FileNotFoundException("No rollback executable exists.", rollback);
        if (OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows rollback cannot overwrite the running executable; stop the service and copy the .rollback file, or run the bootstrap installer for the prior release.");
        File.Copy(rollback, executable, true);
        Console.WriteLine("Rollback executable restored. Restart and run doctor.");
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

    [GeneratedRegex("^r[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();

    private sealed record Release(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string TagName,
        bool Draft,
        bool Prerelease,
        ReleaseAsset[] Assets);
    private sealed record ReleaseAsset(string Name, [property: System.Text.Json.Serialization.JsonPropertyName("browser_download_url")] Uri BrowserDownloadUrl);
}
