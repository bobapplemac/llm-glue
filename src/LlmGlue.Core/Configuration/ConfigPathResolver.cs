namespace LlmGlue.Core.Configuration;

public static class ConfigPathResolver
{
    public static string Resolve(string executablePath, string? explicitPath, bool isLinux, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))
            ?? throw new ConfigurationException("Cannot resolve the executable directory.");
        var sidecar = Path.Combine(executableDirectory, "llm-glue.conf");
        if (exists(sidecar) || !isLinux)
            return sidecar;

        const string linuxSystemPath = "/etc/llm-glue.conf";
        return exists(linuxSystemPath) ? linuxSystemPath : sidecar;
    }
}
