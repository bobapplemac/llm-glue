using System.Text;
using LlmGlue.Core.Documentation;

namespace LlmGlue.Operations;

public static class ReferenceDocumentWriter
{
    private static readonly IReadOnlyDictionary<string, Func<string>> Documents = new Dictionary<string, Func<string>>(StringComparer.Ordinal)
    {
        ["cli-reference.md"] = ReferenceDocumentation.CliMarkdown,
        ["configuration-reference.md"] = ReferenceDocumentation.ConfigurationMarkdown
    };

    public static int Generate(string outputDirectory, bool check)
    {
        var root = Path.GetFullPath(outputDirectory);
        var stale = new List<string>();
        if (!check) Directory.CreateDirectory(root);
        foreach (var document in Documents)
        {
            var path = Path.Combine(root, document.Key);
            var expected = document.Value().ReplaceLineEndings("\n");
            var actual = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
            if (string.Equals(actual, expected, StringComparison.Ordinal)) continue;
            stale.Add(path);
            if (!check) File.WriteAllText(path, expected, new UTF8Encoding(false));
        }
        if (stale.Count == 0)
        {
            Console.WriteLine(check ? "Generated reference documentation is current." : $"Reference documentation is current in {root}.");
            return 0;
        }
        if (check)
        {
            foreach (var path in stale) Console.Error.WriteLine($"Generated reference is stale: {path}");
            return 1;
        }
        foreach (var path in stale) Console.WriteLine($"Generated {path}");
        return 0;
    }
}
