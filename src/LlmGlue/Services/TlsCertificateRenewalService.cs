using System.Security.Cryptography.X509Certificates;
using LlmGlue.Core.Configuration;
using LlmGlue.Security;

namespace LlmGlue.Services;

public sealed class TlsCertificateRenewalService(
    ReloadableServerTlsCertificate certificate,
    GlueOptions options,
    TimeProvider timeProvider,
    ILogger<TlsCertificateRenewalService> logger) : BackgroundService
{
    internal static bool IsRenewalDue(X509Certificate2 current, int renewBeforeDays, DateTimeOffset now) =>
        current.SubjectName.RawData.AsSpan().SequenceEqual(current.IssuerName.RawData)
        && current.NotAfter.ToUniversalTime() <= now.UtcDateTime.AddDays(renewBeforeDays);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TryRenew();
            await Task.Delay(TimeSpan.FromDays(1), timeProvider, stoppingToken);
        }
    }

    internal bool TryRenew()
    {
        var active = certificate.Current;
        if (!IsRenewalDue(active.Certificate, options.Server.TlsRenewBeforeDays, timeProvider.GetUtcNow())) return false;
        try
        {
            var result = certificate.Renew();
            logger.LogInformation(
                "Automatically renewed self-signed TLS certificate; certificate SHA-256 {PreviousCertificateSha256} -> {CertificateSha256}; public-key SHA-256 remains {PublicKeySha256}; valid through {NotAfter}",
                result.PreviousCertificateSha256, result.CertificateSha256, result.PublicKeySha256, result.NotAfter);
            return true;
        }
        catch (Exception ex) when (ex is ConfigurationException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            logger.LogError(ex, "Automatic self-signed TLS certificate renewal failed; the active certificate remains unchanged");
            return false;
        }
    }
}
