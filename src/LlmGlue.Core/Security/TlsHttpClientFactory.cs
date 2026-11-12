using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LlmGlue.Core.Security;

public static class TlsHttpClientFactory
{
    public static HttpClient Create(
        Uri? baseAddress,
        TimeSpan timeout,
        bool allowInvalidCertificate,
        string? trustedPublicKeySha256)
    {
        HttpMessageHandler handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                ValidateServerCertificate(certificate, errors, allowInvalidCertificate, trustedPublicKeySha256)
        };
        return new HttpClient(handler)
        {
            BaseAddress = baseAddress,
            Timeout = timeout
        };
    }

    public static bool ValidateServerCertificate(
        X509Certificate2? certificate,
        SslPolicyErrors errors,
        bool allowInvalidCertificate,
        string? trustedPublicKeySha256)
    {
        if (trustedPublicKeySha256 is not null)
        {
            if (certificate is null) return false;
            var actual = PublicKeySha256(certificate);
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(trustedPublicKeySha256));
        }
        return errors == SslPolicyErrors.None || allowInvalidCertificate;
    }

    public static string PublicKeySha256(X509Certificate2 certificate) =>
        Convert.ToHexStringLower(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
}
