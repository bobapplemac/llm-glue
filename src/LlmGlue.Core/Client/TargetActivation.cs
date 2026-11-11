namespace LlmGlue.Core.Client;

public interface ITargetActivator
{
    Task ActivateAndVerifyAsync(string managedPath, CancellationToken cancellationToken);
}

public sealed class NoOpTargetActivator : ITargetActivator
{
    public Task ActivateAndVerifyAsync(string managedPath, CancellationToken cancellationToken) => Task.CompletedTask;
}
