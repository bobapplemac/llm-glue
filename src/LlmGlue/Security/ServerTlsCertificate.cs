using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Security;

namespace LlmGlue.Security;

public sealed record ServerTlsCertificate(X509Certificate2 Certificate, string CertificatePath, string? PrivateKeyPath) : IDisposable
{
    public string PublicKeySha256 => TlsHttpClientFactory.PublicKeySha256(Certificate);
    public void Dispose() => Certificate.Dispose();
}

public static class ServerTlsCertificateManager
{
    public static ServerTlsCertificate LoadOrCreate(GlueOptions options)
    {
        if (options.Server.Listen.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A TLS certificate is only available for an HTTPS listener.");

        var configuredCertificate = options.Server.TlsCertificatePath;
        var certificatePath = Resolve(options, configuredCertificate ?? Path.Combine(options.Server.StateDirectory, "tls", "server.crt"));
        var pfx = IsPfx(certificatePath);
        var keyPath = pfx ? null : Resolve(options, options.Server.TlsPrivateKeyPath ?? Path.ChangeExtension(certificatePath, ".key"));
        var password = SecretResolver.Resolve(options.Server.TlsCertificatePassword, options.Server.TlsCertificatePasswordEnvironment);
        if (options.Server.TlsCertificatePasswordEnvironment is not null && password is null)
            throw new ConfigurationException($"TLS certificate password environment variable '{options.Server.TlsCertificatePasswordEnvironment}' is not set.");
        var certificateExists = File.Exists(certificatePath);
        var keyExists = keyPath is null || File.Exists(keyPath);
        if (!certificateExists || !keyExists)
        {
            if (!options.Server.TlsAutoGenerate)
                throw new ConfigurationException($"TLS certificate material is missing: {certificatePath}{(keyPath is null ? string.Empty : $" and {keyPath}")}.");
            if (certificateExists || keyExists && keyPath is not null)
                throw new ConfigurationException("Refusing to replace partial TLS certificate material; remove or repair the mismatched certificate/key files explicitly.");
            Generate(options, certificatePath, keyPath, password);
        }

        X509Certificate2 certificate;
        try
        {
            if (pfx) certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password, X509KeyStorageFlags.DefaultKeySet);
            else
            {
                using var pem = X509Certificate2.CreateFromPemFile(certificatePath, keyPath!);
                certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.DefaultKeySet);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"Could not load TLS certificate '{certificatePath}': {ex.Message}");
        }
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new ConfigurationException($"TLS certificate '{certificatePath}' has no private key.");
        }
        return new ServerTlsCertificate(certificate, certificatePath, keyPath);
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

    private static void Generate(GlueOptions options, string certificatePath, string? keyPath, string? password)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
        if (keyPath is not null) Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        using var key = RSA.Create(3072);
        var names = SubjectNames(options).ToArray();
        var commonName = names.FirstOrDefault(name => name != "*" && !name.StartsWith("*.", StringComparison.Ordinal)) ?? "localhost";
        var request = new CertificateRequest(new X500DistinguishedName($"CN={commonName}"), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(30));

        if (IsPfx(certificatePath))
        {
            WriteAtomic(certificatePath, certificate.Export(X509ContentType.Pkcs12, password));
        }
        else
        {
            WriteAtomic(certificatePath, System.Text.Encoding.ASCII.GetBytes(certificate.ExportCertificatePem()));
            WriteAtomic(keyPath!, System.Text.Encoding.ASCII.GetBytes(key.ExportPkcs8PrivateKeyPem()));
        }
        Restrict(certificatePath);
        if (keyPath is not null) Restrict(keyPath);
    }

    private static IEnumerable<string> SubjectNames(GlueOptions options)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", Environment.MachineName };
        foreach (var name in options.Server.TlsServerNames) names.Add(name);
        if (options.Server.Listen.Host is not ("0.0.0.0" or "::" or "[::]" or "*" or "+")) names.Add(options.Server.Listen.Host);
        return names;
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
