using LlmGlue.Api;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Generation;
using LlmGlue.Core.Client;
using LlmGlue.Services;
using LlmGlue.Core.Providers;

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

if (options.Roles.Server)
{
    var builder = WebApplication.CreateBuilder(args);
    ConfigureLogging(builder.Logging);
    builder.WebHost.UseUrls(options.Server.Listen.ToString());
    AddServer(builder.Services, options);
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
    ConfigureLogging(builder.Logging);
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
    var targets = new List<IConfigurationTarget>();
    foreach (var target in options.Client.Targets)
    {
        var path = ResolvePath(options.BaseDirectory, target.Path);
        if (target.Type == "opencode") targets.Add(new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator()));
        else
        {
            var primary = ResolvePath(options.BaseDirectory, target.PrimaryConfigPath!);
            var activator = new ServiceRestartActivator(target.ServiceName!, new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, target.HealthUrl);
            targets.Add(new LiteLlmTarget(path, primary, new AtomicFileInstaller(), activator));
        }
    }
    var token = options.Client.TokenEnvironment is null ? null : Environment.GetEnvironmentVariable(options.Client.TokenEnvironment);
    if (options.Client.TokenEnvironment is not null && string.IsNullOrEmpty(token)) throw new ConfigurationException($"Client token environment variable '{options.Client.TokenEnvironment}' is not set.");
    services.AddSingleton(new ArtifactSyncClient(sourceClient, targets, token));
    services.AddSingleton<IHostedService>(provider => new ClientPollingService(
        provider.GetRequiredService<ArtifactSyncClient>(), TimeSpan.FromSeconds(options.Client.PollIntervalSeconds), provider.GetRequiredService<ILogger<ClientPollingService>>()));
}

static string ResolvePath(string baseDirectory, string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, baseDirectory);

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging.ClearProviders();
    logging.AddSimpleConsole(options => { options.SingleLine = true; options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz "; });
}

public partial class Program;
