using System.Security.Cryptography;

namespace LlmGlue.Core.Client;

public sealed record FileApplyResult(bool Changed, string ContentHash, string? BackupPath);

public sealed class AtomicFileInstaller(int backupRetention = 5)
{
    public async Task<FileApplyResult> ApplyAsync(
        string destination,
        ReadOnlyMemory<byte> content,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> validate,
        Func<string, CancellationToken, Task> activateAndVerify,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Destination has no parent directory.");
        Directory.CreateDirectory(directory);
        await validate(content, cancellationToken);
        var hash = Convert.ToHexStringLower(SHA256.HashData(content.Span));
        var lockPath = path + ".llm-glue.lock";
        await using var fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);

        if (File.Exists(path))
        {
            var existing = await File.ReadAllBytesAsync(path, cancellationToken);
            if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing), SHA256.HashData(content.Span))) return new(false, hash, null);
        }

        var stagePath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var backupDirectory = Path.Combine(directory, ".llm-glue-backups");
        string? backupPath = null;
        try
        {
            await using (var stage = new FileStream(stagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                await stage.WriteAsync(content, cancellationToken);
                await stage.FlushAsync(cancellationToken);
                stage.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                Directory.CreateDirectory(backupDirectory);
                backupPath = Path.Combine(backupDirectory, $"{Path.GetFileName(path)}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.bak");
                File.Replace(stagePath, path, backupPath, ignoreMetadataErrors: true);
            }
            else File.Move(stagePath, path);

            try { await activateAndVerify(path, cancellationToken); }
            catch (Exception activationError)
            {
                if (backupPath is not null && File.Exists(backupPath)) File.Replace(backupPath, path, null, ignoreMetadataErrors: true);
                else if (File.Exists(path)) File.Delete(path);
                try { await activateAndVerify(path, CancellationToken.None); }
                catch (Exception rollbackError) { throw new ConfigurationRollbackException("Activation failed; the file was restored but reactivation of the prior state also failed.", activationError, rollbackError); }
                throw;
            }
            TrimBackups(backupDirectory, Path.GetFileName(path), backupRetention);
            return new(true, hash, backupPath);
        }
        finally
        {
            if (File.Exists(stagePath)) File.Delete(stagePath);
        }
    }

    private static void TrimBackups(string directory, string fileName, int retention)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, fileName + ".*.bak").OrderByDescending(File.GetCreationTimeUtc).Skip(retention)) File.Delete(file);
    }
}

public sealed class ConfigurationRollbackException(string message, Exception activationError, Exception rollbackError)
    : InvalidOperationException(message, new AggregateException(activationError, rollbackError));
