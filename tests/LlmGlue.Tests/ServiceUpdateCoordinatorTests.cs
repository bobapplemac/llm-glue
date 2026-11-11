using LlmGlue.Operations;

namespace LlmGlue.Tests;

public sealed class ServiceUpdateCoordinatorTests
{
    [Fact]
    public async Task ReplacesRestartsAndVerifiesService()
    {
        var files = Files();
        try
        {
            var restarts = 0;
            await ServiceUpdateCoordinator.ReplaceAndVerifyAsync(
                files.Executable, files.Replacement, files.Fallback,
                _ => { restarts++; return Task.CompletedTask; },
                _ => Task.FromResult(true),
                preserveUnixMode: false,
                TestContext.Current.CancellationToken);

            Assert.Equal("new", File.ReadAllText(files.Executable));
            Assert.Equal(1, restarts);
        }
        finally { Directory.Delete(files.Directory, true); }
    }

    [Fact]
    public async Task FailedHealthRestoresAndReverifiesPreviousExecutable()
    {
        var files = Files();
        try
        {
            var restarts = 0;
            var healthChecks = 0;
            var error = await Assert.ThrowsAsync<UpdateRollbackException>(() => ServiceUpdateCoordinator.ReplaceAndVerifyAsync(
                files.Executable, files.Replacement, files.Fallback,
                _ => { restarts++; return Task.CompletedTask; },
                _ => Task.FromResult(++healthChecks > 1),
                preserveUnixMode: false,
                TestContext.Current.CancellationToken));

            Assert.Contains("restored and verified", error.Message, StringComparison.Ordinal);
            Assert.Equal("old", File.ReadAllText(files.Executable));
            Assert.Equal(2, restarts);
            Assert.Equal(2, healthChecks);
        }
        finally { Directory.Delete(files.Directory, true); }
    }

    [Fact]
    public void WindowsServiceHelperContainsStopHealthAndAutomaticRollbackPath()
    {
        var script = UpdateManager.WindowsServiceHelperScript;
        Assert.Contains("sc.exe", script, StringComparison.Ordinal);
        Assert.Contains("Wait-Healthy", script, StringComparison.Ordinal);
        Assert.Contains("$Rollback", script, StringComparison.Ordinal);
        Assert.Contains("failed and rolled back", script, StringComparison.Ordinal);
        Assert.Contains("LLM_GLUE_UPDATE_HEALTH_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("Authorization", script, StringComparison.Ordinal);
        Assert.Contains("-Encoding UTF8", script, StringComparison.Ordinal);
        Assert.DoesNotContain("utf8NoBOM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-Expression", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InterruptedActivationStillRestoresPreviousExecutable()
    {
        var files = Files();
        try
        {
            var restarts = 0;
            await Assert.ThrowsAsync<UpdateRollbackException>(() => ServiceUpdateCoordinator.ReplaceAndVerifyAsync(
                files.Executable, files.Replacement, files.Fallback,
                _ => ++restarts == 1 ? Task.FromException(new OperationCanceledException("interrupted")) : Task.CompletedTask,
                _ => Task.FromResult(true),
                preserveUnixMode: false,
                TestContext.Current.CancellationToken));
            Assert.Equal("old", File.ReadAllText(files.Executable));
            Assert.Equal(2, restarts);
        }
        finally { Directory.Delete(files.Directory, true); }
    }

    private static (string Directory, string Executable, string Replacement, string Fallback) Files()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"llm-glue-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "llm-glue");
        var replacement = Path.Combine(directory, "replacement");
        var fallback = Path.Combine(directory, "fallback");
        File.WriteAllText(executable, "old");
        File.WriteAllText(replacement, "new");
        File.WriteAllText(fallback, "old");
        return (directory, executable, replacement, fallback);
    }
}
