using LlmGlue.Core.Configuration;

if (args is ["--version"])
{
    Console.WriteLine("LLM Glue r0");
    return;
}

var explicitConfig = args.Select((value, index) => (value, index)).FirstOrDefault(x => x.value == "--config");
var configArgument = explicitConfig.value is null ? null : args.ElementAtOrDefault(explicitConfig.index + 1);
var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine executable path.");
var configPath = ConfigPathResolver.Resolve(executablePath, configArgument, OperatingSystem.IsLinux());

try
{
    var options = TomlConfigLoader.Load(configPath);
    Console.WriteLine($"LLM Glue r0 configuration valid: {options.ConfigPath}");
}
catch (ConfigurationException ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 2;
}
