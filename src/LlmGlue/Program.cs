using LlmGlue.Api;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Generation;
using LlmGlue.Core.Client;
using LlmGlue.Services;
using LlmGlue.Core.Providers;
using LlmGlue.Operations;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;

if (args is ["--version"])
{
    Console.WriteLine($"LLM Glue {ProductInfo.Revision}");
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

if (args.FirstOrDefault() == "service")
{
    var action = args.ElementAtOrDefault(1) ?? "status";
    Environment.ExitCode = await ServiceManager.ExecuteAsync(action, executablePath, options, CancellationToken.None, args.Contains("--dry-run", StringComparer.Ordinal));
    return;
}

if (args.FirstOrDefault() == "doctor") { Environment.ExitCode = await CliCommands.DoctorAsync(options, CancellationToken.None); return; }
if (args.FirstOrDefault() == "discover") { Environment.ExitCode = await CliCommands.DiscoverAsync(options, CancellationToken.None); return; }
if (args.Take(2).SequenceEqual(["config", "preview"])) { Environment.ExitCode = await CliCommands.PreviewAsync(options, args.ElementAtOrDefault(2) ?? "opencode", CancellationToken.None); return; }
if (args.Take(2).SequenceEqual(["sync", "--once"])) { Environment.ExitCode = await CliCommands.SyncOnceAsync(options, CancellationToken.None); return; }
if (args.FirstOrDefault() == "status") { Environment.ExitCode = await CliCommands.StatusAsync(options, CancellationToken.None); return; }
if (args.Take(2).SequenceEqual(["update", "check"]))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var update = await UpdateManager.CheckAsync(http, CancellationToken.None);
    Console.WriteLine(update is null ? "No newer stable release is available." : $"Update available: {update.Tag}");
    return;
}
if (args.Take(2).SequenceEqual(["update", "apply"]))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    await UpdateManager.ApplyAsync(http, executablePath, args.Contains("--allow-downgrade", StringComparer.Ordinal), CancellationToken.None);
    return;
}
if (args.Take(2).SequenceEqual(["update", "rollback"])) { UpdateManager.Rollback(executablePath); return; }

if (options.Roles.Server)
{
    var builder = WebApplication.CreateBuilder(args);
    ConfigureLogging(builder.Logging, options);
    builder.WebHost.UseUrls(options.Server.Listen.ToString());
    AddServer(builder.Services, options);
    builder.Services.AddWindowsService(service => service.ServiceName = "LLMGlue");
    builder.Services.AddSystemd();
    if (options.Roles.Agent) AddAgent(builder.Services, options);
    if (options.Roles.Client) AddClient(builder.Services, options);
    var app = builder.Build();
    app.UseMiddleware<ApiAuthenticationMiddleware>();
    app.MapGlueApi();
    await app.RunAsync();
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    ConfigureLogging(builder.Logging, options);
    builder.Services.AddWindowsService(service => service.ServiceName = "LLMGlue");
    builder.Services.AddSystemd();
    if (options.Roles.Client) AddClient(builder.Services, options);
    await builder.Build().RunAsync();
}

static void AddServer(IServiceCollection services, GlueOptions options)
{
    services.AddSingleton<IConfigurationGenerator, OpenCodeGenerator>();
    services.AddSingleton<IConfigurationGenerator>(_ => new LiteLlmGenerator());
    services.AddSingleton(new ArtifactStore());
    services.AddSingleton<ApiState>();
    services.AddSingleton(new ApiAuthenticationOptions(
        options.Server.ReadTokenEnvironment is null ? null : Environment.GetEnvironmentVariable(options.Server.ReadTokenEnvironment),
        options.Server.WriteTokenEnvironment is null ? null : Environment.GetEnvironmentVariable(options.Server.WriteTokenEnvironment)));
}

static void AddAgent(IServiceCollection services, GlueOptions options)
{
    foreach (var provider in options.Providers)
    {
        var client = new HttpClient { BaseAddress = provider.Url, Timeout = TimeSpan.FromSeconds(15) };
        IModelDiscoveryAdapter adapter = provider.Type == "ollama"
            ? new OllamaDiscoveryAdapter(client, provider.Name)
            : new LlamaCppDiscoveryAdapter(client, provider.Name);
        services.AddSingleton<IModelDiscoveryAdapter>(adapter);
    }
    services.AddSingleton<IHostedService>(provider => new AgentDiscoveryService(
        provider.GetServices<IModelDiscoveryAdapter>(), provider.GetRequiredService<ApiState>(),
        provider.GetRequiredService<ILogger<AgentDiscoveryService>>(), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10)));
}

static void AddClient(IServiceCollection services, GlueOptions options)
{
    var source = options.Client.Source ?? options.Server.Listen;
    var sourceClient = new HttpClient { BaseAddress = source, Timeout = TimeSpan.FromSeconds(30) };
    var targets = CliCommands.CreateTargets(options);
    var token = options.Client.TokenEnvironment is null ? null : Environment.GetEnvironmentVariable(options.Client.TokenEnvironment);
    if (options.Client.TokenEnvironment is not null && string.IsNullOrEmpty(token)) throw new ConfigurationException($"Client token environment variable '{options.Client.TokenEnvironment}' is not set.");
    services.AddSingleton(new ArtifactSyncClient(sourceClient, targets, token));
    services.AddSingleton<IHostedService>(provider => new ClientPollingService(
        provider.GetRequiredService<ArtifactSyncClient>(), TimeSpan.FromSeconds(options.Client.PollIntervalSeconds), provider.GetRequiredService<ILogger<ClientPollingService>>()));
}

static void ConfigureLogging(ILoggingBuilder logging, GlueOptions options)
{
    logging.ClearProviders();
    var configuration = new LoggingConfiguration();
    var layout = "${longdate}|${level:uppercase=true}|${logger}|${message:withexception=true}";
    var console = new ConsoleTarget("console") { Layout = layout };
    var filePath = Path.IsPathRooted(options.Logging.File) ? options.Logging.File : Path.GetFullPath(options.Logging.File, options.BaseDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    var file = new FileTarget("file")
    {
        FileName = filePath,
        Layout = layout,
        ArchiveAboveSize = options.Logging.ArchiveSizeMb * 1024L * 1024L,
        MaxArchiveFiles = options.Logging.Retention,
        KeepFileOpen = false
    };
    var minimum = NLog.LogLevel.FromString(options.Logging.Level == "Information" ? "Info" : options.Logging.Level == "Warning" ? "Warn" : options.Logging.Level == "Critical" ? "Fatal" : options.Logging.Level);
    configuration.AddRule(minimum, NLog.LogLevel.Fatal, console);
    configuration.AddRule(minimum, NLog.LogLevel.Fatal, file);
    logging.AddNLog(configuration);
}

public partial class Program;
