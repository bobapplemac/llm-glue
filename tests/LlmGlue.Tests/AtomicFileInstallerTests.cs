using System.Text;
using LlmGlue.Core.Client;

namespace LlmGlue.Tests;

public sealed class AtomicFileInstallerTests
{
    [Fact]
    public async Task NoOpsWhenContentIsUnchanged()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "config.json");
            var activations = 0;
            var installer = new AtomicFileInstaller();
            var first = await installer.ApplyAsync(path, "one"u8.ToArray(), Valid, (_, _) => { activations++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
            var second = await installer.ApplyAsync(path, "one"u8.ToArray(), Valid, (_, _) => { activations++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
            Assert.True(first.Changed);
            Assert.False(second.Changed);
            Assert.Equal(1, activations);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RestoresLastKnownGoodWhenActivationFails()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
            var installer = new AtomicFileInstaller();
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => installer.ApplyAsync(path, "new"u8.ToArray(), Valid, (_, _) => throw new InvalidOperationException("reload failed"), TestContext.Current.CancellationToken));
            Assert.Equal("old", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ValidationFailureNeverTouchesDestination()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
            var installer = new AtomicFileInstaller();
            await Assert.ThrowsAsync<InvalidDataException>(() => installer.ApplyAsync(path, "bad"u8.ToArray(), (_, _) => throw new InvalidDataException("invalid"), (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken));
            Assert.Equal("old", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Task Valid(ReadOnlyMemory<byte> _, CancellationToken cancellationToken) => Task.CompletedTask;
    internal static string TempDirectory() { var path = Path.Combine(Path.GetTempPath(), $"llm-glue-tests-{Guid.NewGuid():N}"); Directory.CreateDirectory(path); return path; }
}
