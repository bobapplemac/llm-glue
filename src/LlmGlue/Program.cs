using LlmGlue.Api;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Generation;

if (args is ["--version"])
{
    Console.WriteLine("LLM Glue r0");
    return;
}

var explicitIndex = Array.IndexOf(args, "--config");
var configArgument = explicitIndex < 0 ? null : args.ElementAtOrDefault(explicitIndex + 1);
var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine executable path.");
var configPath = ConfigPathResolver.Resolve(executablePath, configArgument, OperatingSystem.IsLinux());

GlueOptions options;
try { options = TomlConfigLoader.Load(configPath); }
catch (ConfigurationException ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 2;
    return;
}

if (args.Take(2).SequenceEqual(["config", "validate"]))
{
    Console.WriteLine($"Configuration valid: {options.ConfigPath}");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(options.Server.Listen.ToString());
builder.Services.AddSingleton<IConfigurationGenerator, OpenCodeGenerator>();
builder.Services.AddSingleton<IConfigurationGenerator>(_ => new LiteLlmGenerator());
builder.Services.AddSingleton(new ArtifactStore());
builder.Services.AddSingleton<ApiState>();
builder.Services.AddSingleton(new ApiAuthenticationOptions(
    options.Server.ReadTokenEnvironment is null ? null : Environment.GetEnvironmentVariable(options.Server.ReadTokenEnvironment),
    options.Server.WriteTokenEnvironment is null ? null : Environment.GetEnvironmentVariable(options.Server.WriteTokenEnvironment)));

var app = builder.Build();
app.UseMiddleware<ApiAuthenticationMiddleware>();
app.MapGlueApi();
await app.RunAsync();

public partial class Program;
