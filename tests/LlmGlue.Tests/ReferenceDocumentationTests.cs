using LlmGlue.Core.Documentation;
using LlmGlue.Operations;

namespace LlmGlue.Tests;

public sealed class ReferenceDocumentationTests
{
    [Fact]
    public void ConfigurationCatalogHasUniqueKeysAndDocumentedScopes()
    {
        var duplicates = ReferenceDocumentation.ConfigurationSettings
            .GroupBy(x => (x.Scope, x.Key))
            .Where(x => x.Count() > 1)
            .Select(x => $"{x.Key.Scope}.{x.Key.Key}")
            .ToArray();

        Assert.Empty(duplicates);
        Assert.All(ReferenceDocumentation.ConfigurationSettings, setting =>
        {
            Assert.False(string.IsNullOrWhiteSpace(setting.Path));
            Assert.False(string.IsNullOrWhiteSpace(setting.Type));
            Assert.False(string.IsNullOrWhiteSpace(setting.Default));
            Assert.False(string.IsNullOrWhiteSpace(setting.Description));
        });
    }

    [Fact]
    public void CliCatalogHasUniqueCommandsAndParameters()
    {
        Assert.DoesNotContain(
            ReferenceDocumentation.CliCommands.GroupBy(x => x.Syntax, StringComparer.Ordinal),
            group => group.Count() > 1);

        Assert.All(ReferenceDocumentation.CliCommands, command =>
            Assert.DoesNotContain(
                command.Parameters.GroupBy(x => x.Syntax, StringComparer.Ordinal),
                group => group.Count() > 1));
    }

    [Fact]
    public void WriterGeneratesDeterministicReferencesAndDetectsDrift()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"llm-glue-reference-{Guid.NewGuid():N}");
        try
        {
            Assert.Equal(0, ReferenceDocumentWriter.Generate(directory, check: false));
            Assert.Equal(0, ReferenceDocumentWriter.Generate(directory, check: true));
            Assert.Equal(
                ReferenceDocumentation.CliMarkdown().ReplaceLineEndings("\n"),
                File.ReadAllText(Path.Combine(directory, "cli-reference.md")).ReplaceLineEndings("\n"));
            Assert.Equal(
                ReferenceDocumentation.ConfigurationMarkdown().ReplaceLineEndings("\n"),
                File.ReadAllText(Path.Combine(directory, "configuration-reference.md")).ReplaceLineEndings("\n"));

            File.AppendAllText(Path.Combine(directory, "cli-reference.md"), "stale");
            Assert.Equal(1, ReferenceDocumentWriter.Generate(directory, check: true));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
