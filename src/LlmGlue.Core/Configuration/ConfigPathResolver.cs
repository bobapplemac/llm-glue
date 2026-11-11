namespace LlmGlue.Core.Configuration;

public static class ConfigPathResolver
{
    public static string Resolve(
        string executablePath,
        string? explicitPath,
        bool isLinux,
        Func<string, bool>? exists = null,
        string? canonicalDirectory = null)
    {
        exists ??= File.Exists;
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))
            ?? throw new ConfigurationException("Cannot resolve the executable directory.");
        var adjacentConf = Path.Combine(executableDirectory, "llm-glue.conf");
        var adjacentToml = Path.Combine(executableDirectory, "llm-glue.toml");
        if (exists(adjacentConf)) return adjacentConf;
        if (exists(adjacentToml)) return adjacentToml;

        canonicalDirectory ??= isLinux
            ? "/etc/llm-glue"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LLMGlue");
        var canonicalConf = Path.Combine(canonicalDirectory, "llm-glue.conf");
        var canonicalToml = Path.Combine(canonicalDirectory, "llm-glue.toml");
        if (exists(canonicalConf)) return canonicalConf;
        if (exists(canonicalToml)) return canonicalToml;

        if (isLinux)
        {
            const string legacyConf = "/etc/llm-glue.conf";
            const string legacyToml = "/etc/llm-glue.toml";
            if (exists(legacyConf)) return legacyConf;
            if (exists(legacyToml)) return legacyToml;
        }

        return adjacentConf;
    }
}
