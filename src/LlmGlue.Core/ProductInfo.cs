using System.Reflection;
using System.Text.RegularExpressions;

namespace LlmGlue.Core;

public static partial class ProductInfo
{
    public static string Revision { get; } = ResolveRevision();
    public static int RevisionNumber { get; } = int.Parse(Revision.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
    public static string UserAgent => $"llm-glue-{Revision}";

    private static string ResolveRevision()
    {
        var value = typeof(ProductInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var revision = value?.Split('+', 2)[0];
        return revision is not null && RevisionPattern().IsMatch(revision) ? revision : "r0";
    }

    [GeneratedRegex("^r[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();
}
