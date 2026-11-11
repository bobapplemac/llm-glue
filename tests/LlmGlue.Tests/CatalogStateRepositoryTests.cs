using LlmGlue.Api;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;

namespace LlmGlue.Tests;

public sealed class CatalogStateRepositoryTests
{
    [Fact]
    public async Task RoundTripsValidatedSnapshotAndArtifactsAndRestoresApiState()
    {
        var directory = TempDirectory();
        try
        {
            var repository = new CatalogStateRepository(directory, 3);
            var snapshot = GenerationTests.Snapshot();
            var artifact = await new OpenCodeGenerator().GenerateAsync(snapshot, TestContext.Current.CancellationToken);
            await repository.SaveAsync(snapshot, [new("opencode", artifact)], DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

            var loaded = Assert.IsType<CatalogState>(repository.Load());
            Assert.Equal(snapshot.ContentHash, loaded.Snapshot.ContentHash);
            Assert.Equal(artifact.ContentHash, Assert.Single(loaded.Artifacts).Artifact.ContentHash);

            var store = new ArtifactStore();
            var state = new ApiState([new OpenCodeGenerator()], store, repository: repository);
            Assert.Equal(snapshot.ContentHash, state.Snapshot.ContentHash);
            Assert.Equal(artifact.ContentHash, store.Latest("opencode")!.Artifact.ContentHash);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RejectsTamperedPersistedArtifact()
    {
        var directory = TempDirectory();
        try
        {
            var repository = new CatalogStateRepository(directory);
            var snapshot = GenerationTests.Snapshot();
            var artifact = await new OpenCodeGenerator().GenerateAsync(snapshot, TestContext.Current.CancellationToken);
            await repository.SaveAsync(snapshot, [new("opencode", artifact)], DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
            var revision = File.ReadAllText(Path.Combine(directory, "current")).Trim();
            await File.AppendAllTextAsync(Path.Combine(directory, "revisions", revision, "opencode.artifact"), "tampered", TestContext.Current.CancellationToken);

            Assert.Throws<InvalidDataException>(() => repository.Load());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RetentionDeletesOnlyOwnedRevisionDirectories()
    {
        var directory = TempDirectory();
        try
        {
            var repository = new CatalogStateRepository(directory, 2);
            Directory.CreateDirectory(Path.Combine(directory, "revisions", "operator-data"));
            for (var index = 0; index < 3; index++)
            {
                var deployment = GenerationTests.Qualified() with
                {
                    Identity = GenerationTests.Qualified().Identity with { ModelId = $"model-{index}", Route = $"model-{index}" }
                };
                var snapshot = DiscoverySnapshot.Create(DateTimeOffset.UnixEpoch.AddMinutes(index), [deployment]);
                var artifact = await new OpenCodeGenerator().GenerateAsync(snapshot, TestContext.Current.CancellationToken);
                await repository.SaveAsync(snapshot, [new("opencode", artifact)], DateTimeOffset.UnixEpoch.AddMinutes(index), TestContext.Current.CancellationToken);
            }

            var revisions = Directory.GetDirectories(Path.Combine(directory, "revisions"));
            Assert.Equal(3, revisions.Length);
            Assert.Contains(revisions, x => Path.GetFileName(x) == "operator-data");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task PersistenceFailureDoesNotExposeInventoryOrArtifacts()
    {
        var directory = TempDirectory();
        try
        {
            var invalidRoot = Path.Combine(directory, "not-a-directory");
            await File.WriteAllTextAsync(invalidRoot, "occupied", TestContext.Current.CancellationToken);
            var store = new ArtifactStore();
            var state = new ApiState([new OpenCodeGenerator()], store, repository: new CatalogStateRepository(invalidRoot));

            await Assert.ThrowsAnyAsync<IOException>(() => state.PublishAsync(GenerationTests.Snapshot(), TestContext.Current.CancellationToken));
            Assert.Empty(state.Snapshot.Deployments);
            Assert.Null(store.Latest("opencode"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"llm-glue-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
