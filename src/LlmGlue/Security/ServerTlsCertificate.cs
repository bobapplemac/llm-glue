using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Security;

namespace LlmGlue.Security;

public sealed record ServerTlsCertificate(
    X509Certificate2 Certificate,
    X509Certificate2Collection Chain,
    SslStreamCertificateContext CertificateContext,
    string CertificatePath,
    string? PrivateKeyPath) : IDisposable
{
    public string PublicKeySha256 => TlsHttpClientFactory.PublicKeySha256(Certificate);
    public string CertificateSha256 => Convert.ToHexStringLower(SHA256.HashData(Certificate.RawData));
    public void Dispose()
    {
        Certificate.Dispose();
        foreach (var item in Chain) item.Dispose();
    }
}

public sealed record ServerTlsReloadResult(
    bool Changed,
    string PreviousCertificateSha256,
    string CertificateSha256,
    string PreviousPublicKeySha256,
    string PublicKeySha256,
    DateTime NotBefore,
    DateTime NotAfter);

public sealed class ReloadableServerTlsCertificate : IDisposable
{
    private readonly object sync = new();
    private readonly GlueOptions options;
    private readonly List<ServerTlsCertificate> retired = [];
    private ServerTlsCertificate current;
    private bool disposed;

    public ReloadableServerTlsCertificate(GlueOptions options)
    {
        this.options = options;
        current = ServerTlsCertificateManager.LoadOrCreate(options);
    }

    public ServerTlsCertificate Current => Volatile.Read(ref current);

    public ServerTlsCertificate? Select(string? serverName) =>
        serverName?.Equals("localhost", StringComparison.OrdinalIgnoreCase) == true
            || ServerTlsCertificateManager.MatchesServerName(options.Server.TlsServerNames, serverName)
            ? Current
            : null;

    public ServerTlsReloadResult Reload() => Swap(ServerTlsCertificateManager.LoadExisting(options));

    public ServerTlsReloadResult Regenerate(bool force) => Swap(ServerTlsCertificateManager.RegenerateSelfSigned(options, force));

    public ServerTlsReloadResult Renew() => Swap(ServerTlsCertificateManager.RenewSelfSigned(options));

    private ServerTlsReloadResult Swap(ServerTlsCertificate replacement)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var previous = current;
            var changed = !CryptographicOperations.FixedTimeEquals(previous.Certificate.RawData, replacement.Certificate.RawData);
            if (changed)
            {
                retired.Add(previous);
                Volatile.Write(ref current, replacement);
            }
            else replacement.Dispose();
            var active = current;
            return new ServerTlsReloadResult(
                changed,
                previous.CertificateSha256,
                active.CertificateSha256,
                previous.PublicKeySha256,
                active.PublicKeySha256,
                active.Certificate.NotBefore,
                active.Certificate.NotAfter);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            current.Dispose();
            foreach (var certificate in retired) certificate.Dispose();
            retired.Clear();
        }
    }
}

public static class ServerTlsCertificateManager
{
    private sealed record MaterialPaths(string Certificate, string? PrivateKey, bool Pfx, string? Password);

    public static ServerTlsCertificate LoadOrCreate(GlueOptions options)
    {
        var paths = ResolveMaterial(options);
        var certificateExists = File.Exists(paths.Certificate);
        var keyExists = paths.PrivateKey is null || File.Exists(paths.PrivateKey);
        if (!certificateExists || !keyExists)
        {
            if (!options.Server.TlsAutoGenerate)
                throw new ConfigurationException($"TLS certificate material is missing: {paths.Certificate}{(paths.PrivateKey is null ? string.Empty : $" and {paths.PrivateKey}")}.");
            if (certificateExists || keyExists && paths.PrivateKey is not null)
                throw new ConfigurationException("Refusing to replace partial TLS certificate material; remove or repair the mismatched certificate/key files explicitly.");
            Generate(options, paths.Certificate, paths.PrivateKey, paths.Password);
        }
        return LoadMaterial(paths);
    }

    public static ServerTlsCertificate LoadExisting(GlueOptions options)
    {
        var paths = ResolveMaterial(options);
        if (!File.Exists(paths.Certificate) || paths.PrivateKey is not null && !File.Exists(paths.PrivateKey))
            throw new ConfigurationException($"TLS certificate material is missing: {paths.Certificate}{(paths.PrivateKey is null ? string.Empty : $" and {paths.PrivateKey}")}.");
        return LoadMaterial(paths);
    }

    public static ServerTlsCertificate RegenerateSelfSigned(GlueOptions options, bool force)
    {
        var paths = ResolveMaterial(options);
        using (var existing = LoadExisting(options))
        {
            if (!force && !existing.Certificate.SubjectName.RawData.AsSpan().SequenceEqual(existing.Certificate.IssuerName.RawData))
                throw new ConfigurationException("Refusing to replace a certificate that is not self-signed; use --force to confirm replacement.");
        }

        var id = Guid.NewGuid().ToString("N");
        var candidateCertificate = CandidatePath(paths.Certificate, id);
        var candidateKey = paths.PrivateKey is null ? null : CandidatePath(paths.PrivateKey, id);
        try
        {
            Generate(options, candidateCertificate, candidateKey, paths.Password);
            using (LoadMaterial(new MaterialPaths(candidateCertificate, candidateKey, paths.Pfx, paths.Password))) { }
            var replacements = new List<(string Candidate, string Destination)> { (candidateCertificate, paths.Certificate) };
            if (candidateKey is not null) replacements.Add((candidateKey, paths.PrivateKey!));
            ReplaceMaterial(replacements, id);
            return LoadMaterial(paths);
        }
        finally
        {
            if (File.Exists(candidateCertificate)) File.Delete(candidateCertificate);
            if (candidateKey is not null && File.Exists(candidateKey)) File.Delete(candidateKey);
        }
    }

    public static ServerTlsCertificate RenewSelfSigned(GlueOptions options)
    {
        var paths = ResolveMaterial(options);
        var id = Guid.NewGuid().ToString("N");
        var candidateCertificate = CandidatePath(paths.Certificate, id);
        try
        {
            using var existing = LoadExisting(options);
            if (!existing.Certificate.SubjectName.RawData.AsSpan().SequenceEqual(existing.Certificate.IssuerName.RawData))
                throw new ConfigurationException("TLS renewal applies only to a self-signed certificate; install a renewed public certificate and use tls reload instead.");
            using var key = existing.Certificate.GetRSAPrivateKey()
                ?? throw new ConfigurationException("TLS renewal currently requires an RSA self-signed certificate.");
            using var certificate = CreateSelfSigned(options, key, existing.Certificate.SubjectName);
            if (paths.Pfx) WriteAtomic(candidateCertificate, certificate.Export(X509ContentType.Pkcs12, paths.Password));
            else WriteAtomic(candidateCertificate, System.Text.Encoding.ASCII.GetBytes(certificate.ExportCertificatePem()));
            Restrict(candidateCertificate);
            using (LoadMaterial(new MaterialPaths(candidateCertificate, paths.PrivateKey, paths.Pfx, paths.Password))) { }
            ReplaceMaterial([(candidateCertificate, paths.Certificate)], id);
            return LoadMaterial(paths);
        }
        finally
        {
            if (File.Exists(candidateCertificate)) File.Delete(candidateCertificate);
        }
    }

    public static bool MatchesServerName(IReadOnlyList<string> patterns, string? serverName)
    {
        if (patterns.Count == 0) return true;
        foreach (var pattern in patterns)
        {
            if (pattern == "*") return true;
            if (serverName is null) continue;
            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = pattern[1..];
                if (serverName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && serverName.Length > suffix.Length) return true;
            }
            else if (string.Equals(pattern, serverName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static MaterialPaths ResolveMaterial(GlueOptions options)
    {
        if (options.Server.Listen.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A TLS certificate is only available for an HTTPS listener.");
        var certificatePath = Resolve(options, options.Server.TlsCertificatePath ?? Path.Combine(options.Server.StateDirectory, "tls", "server.crt"));
        var pfx = IsPfx(certificatePath);
        var keyPath = pfx ? null : Resolve(options, options.Server.TlsPrivateKeyPath ?? Path.ChangeExtension(certificatePath, ".key"));
        var password = SecretResolver.Resolve(options.Server.TlsCertificatePassword, options.Server.TlsCertificatePasswordEnvironment);
        if (options.Server.TlsCertificatePasswordEnvironment is not null && password is null)
            throw new ConfigurationException($"TLS certificate password environment variable '{options.Server.TlsCertificatePasswordEnvironment}' is not set.");
        return new MaterialPaths(certificatePath, keyPath, pfx, password);
    }

    private static ServerTlsCertificate LoadMaterial(MaterialPaths paths)
    {
        X509Certificate2? certificate = null;
        var chain = new X509Certificate2Collection();
        try
        {
            if (paths.Pfx)
            {
                var collection = X509CertificateLoader.LoadPkcs12CollectionFromFile(paths.Certificate, paths.Password, X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
                certificate = collection.Cast<X509Certificate2>().FirstOrDefault(item => item.HasPrivateKey)
                    ?? throw new CryptographicException("The PFX/P12 file does not contain a certificate with a private key.");
                collection.Remove(certificate);
                chain = collection;
            }
            else
            {
                using var pem = X509Certificate2.CreateFromPemFile(paths.Certificate, paths.PrivateKey!);
                certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable);
                var collection = new X509Certificate2Collection();
                collection.ImportFromPemFile(paths.Certificate);
                foreach (var item in collection)
                {
                    if (item.RawData.AsSpan().SequenceEqual(certificate.RawData)) item.Dispose();
                    else chain.Add(item);
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            certificate?.Dispose();
            foreach (var item in chain) item.Dispose();
            throw new ConfigurationException($"Could not load TLS certificate '{paths.Certificate}': {ex.Message}");
        }
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            foreach (var item in chain) item.Dispose();
            throw new ConfigurationException($"TLS certificate '{paths.Certificate}' has no private key.");
        }
        var now = DateTime.UtcNow;
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
        {
            certificate.Dispose();
            foreach (var item in chain) item.Dispose();
            throw new ConfigurationException($"TLS certificate '{paths.Certificate}' is not currently valid.");
        }
        var enhancedKeyUsage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (enhancedKeyUsage is not null && !enhancedKeyUsage.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.1"))
        {
            certificate.Dispose();
            foreach (var item in chain) item.Dispose();
            throw new ConfigurationException($"TLS certificate '{paths.Certificate}' does not permit Server Authentication.");
        }
        try
        {
            var context = SslStreamCertificateContext.Create(certificate, chain, offline: true);
            return new ServerTlsCertificate(certificate, chain, context, paths.Certificate, paths.PrivateKey);
        }
        catch
        {
            certificate.Dispose();
            foreach (var item in chain) item.Dispose();
            throw;
        }
    }

    private static void Generate(GlueOptions options, string certificatePath, string? keyPath, string? password)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
        if (keyPath is not null) Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        using var key = RSA.Create(3072);
        var names = SubjectNames(options).ToArray();
        var commonName = names.FirstOrDefault(name => name != "*" && !name.StartsWith("*.", StringComparison.Ordinal)) ?? "localhost";
        using var certificate = CreateSelfSigned(options, key, new X500DistinguishedName($"CN={commonName}"));

        if (IsPfx(certificatePath)) WriteAtomic(certificatePath, certificate.Export(X509ContentType.Pkcs12, password));
        else
        {
            WriteAtomic(certificatePath, System.Text.Encoding.ASCII.GetBytes(certificate.ExportCertificatePem()));
            WriteAtomic(keyPath!, System.Text.Encoding.ASCII.GetBytes(key.ExportPkcs8PrivateKeyPem()));
        }
        Restrict(certificatePath);
        if (keyPath is not null) Restrict(keyPath);
    }

    private static X509Certificate2 CreateSelfSigned(GlueOptions options, RSA key, X500DistinguishedName subject)
    {
        var names = SubjectNames(options).ToArray();
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));
        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in names.Where(name => name != "*"))
        {
            if (IPAddress.TryParse(name, out var address)) san.AddIpAddress(address);
            else san.AddDnsName(name);
        }
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(options.Server.TlsSelfSignedValidityDays));
    }

    private static void ReplaceMaterial(IReadOnlyList<(string Candidate, string Destination)> files, string id)
    {
        var backups = new List<(string Backup, string Destination)>();
        var installed = new List<string>();
        try
        {
            foreach (var (_, destination) in files)
            {
                var backup = destination + $".{id}.bak";
                File.Move(destination, backup, overwrite: false);
                backups.Add((backup, destination));
            }
            foreach (var (candidate, destination) in files)
            {
                File.Move(candidate, destination, overwrite: false);
                installed.Add(destination);
            }
        }
        catch
        {
            foreach (var installedPath in installed) if (File.Exists(installedPath)) File.Delete(installedPath);
            foreach (var (backup, destination) in backups.AsEnumerable().Reverse())
                if (File.Exists(backup)) File.Move(backup, destination, overwrite: false);
            throw;
        }
        foreach (var (backup, _) in backups)
        {
            try { File.Delete(backup); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<string> SubjectNames(GlueOptions options)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", Environment.MachineName };
        foreach (var name in options.Server.TlsServerNames) names.Add(name);
        if (options.Server.Listen.Host is not ("0.0.0.0" or "::" or "[::]" or "*" or "+")) names.Add(options.Server.Listen.Host);
        return names;
    }

    private static string CandidatePath(string path, string id)
    {
        var directory = Path.GetDirectoryName(path)!;
        return Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(path)}.{id}{Path.GetExtension(path)}");
    }

    private static void WriteAtomic(string path, byte[] content)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static bool IsPfx(string path) => Path.GetExtension(path) is var extension
        && (extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase));
    private static string Resolve(GlueOptions options, string path) => Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(path, options.BaseDirectory);
}
