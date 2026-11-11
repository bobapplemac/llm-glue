namespace LlmGlue.Operations;

public sealed class UpdateRollbackException(string message, Exception activationError, Exception? rollbackError = null)
    : InvalidOperationException(message, rollbackError is null ? activationError : new AggregateException(activationError, rollbackError));

public static class ServiceUpdateCoordinator
{
    public static async Task ReplaceAndVerifyAsync(
        string executable,
        string replacement,
        string fallback,
        Func<CancellationToken, Task> restart,
        Func<CancellationToken, Task<bool>> healthCheck,
        bool preserveUnixMode,
        CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(executable);
        var stage = destination + $".stage-{Guid.NewGuid():N}";
        try
        {
            File.Copy(replacement, stage, overwrite: false);
            if (preserveUnixMode && !OperatingSystem.IsWindows()) File.SetUnixFileMode(stage, File.GetUnixFileMode(replacement));
            File.Move(stage, destination, overwrite: true);
            try
            {
                await restart(cancellationToken);
                if (!await healthCheck(cancellationToken)) throw new InvalidOperationException("Updated service did not become healthy.");
            }
            catch (Exception activationError)
            {
                Exception? rollbackError = null;
                try
                {
                    File.Copy(fallback, stage, overwrite: true);
                    if (preserveUnixMode && !OperatingSystem.IsWindows()) File.SetUnixFileMode(stage, File.GetUnixFileMode(fallback));
                    File.Move(stage, destination, overwrite: true);
                    await restart(CancellationToken.None);
                    if (!await healthCheck(CancellationToken.None)) throw new InvalidOperationException("Restored service did not become healthy.");
                }
                catch (Exception ex) { rollbackError = ex; }
                throw new UpdateRollbackException(
                    rollbackError is null ? "Service activation failed; the previous executable was restored and verified." : "Service activation failed and rollback did not become healthy.",
                    activationError,
                    rollbackError);
            }
        }
        finally
        {
            if (File.Exists(stage)) File.Delete(stage);
        }
    }
}
