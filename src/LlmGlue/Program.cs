using LlmGlue.Api;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Generation;
using LlmGlue.Core.Client;
using LlmGlue.Services;
using LlmGlue.Core.Providers;
using LlmGlue.Core.Reconciliation;
using LlmGlue.Operations;
using LlmGlue.Observability;
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

if (args.FirstOrDefault() == "help" || args.Any(argument => argument is "--help" or "/?" or "-h"))
{
    Console.WriteLine(CliCommands.HelpText);
    return;
}

if (args.Take(2).SequenceEqual(["update", "activate-staged"]))
{
    if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The staged update activator is Linux-only.");
    Environment.ExitCode = await UpdateManager.ActivateStagedAsync(
        int.Parse(RequiredOption(args, "--wait-process-id"), System.Globalization.CultureInfo.InvariantCulture),
        RequiredOption(args, "--source"),
        RequiredOption(args, "--destination"),
        RequiredOption(args, "--fallback"),
        args.Contains("--service", StringComparer.Ordinal),
        OptionalUri(args, "--health-url"),
        RequiredOption(args, "--result"),
        CancellationToken.None);
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
    await UpdateManager.ApplyAsync(http, executablePath, new UpdateApplyOptions(
        args.Contains("--allow-downgrade", StringComparer.Ordinal),
        args.Contains("--service", StringComparer.Ordinal),
        ServiceHealthUrl(options), ServiceHealthToken(options)), CancellationToken.None);
    return;
}
if (args.Take(2).SequenceEqual(["update", "rollback"]))
{
    await UpdateManager.RollbackAsync(executablePath, args.Contains("--service", StringComparer.Ordinal), ServiceHealthUrl(options), ServiceHealthToken(options), CancellationToken.None);
    return;
}

if (options.Roles.Server)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = OperationalDirectory(options) });
    ConfigureLogging(builder.Logging, options);
    builder.WebHost.UseUrls(options.Server.Listen.ToString());
    builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 4 * 1024 * 1024);
    AddServer(builder.Services, options);
    builder.Services.AddWindowsService(service => service.ServiceName = "LLMGlue");
    builder.Services.AddSystemd();
    if (options.Roles.Agent) AddAgent(builder.Services, options);
    if (options.Roles.Client) AddClient(builder.Services, options);
    var app = builder.Build();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ApiAuthenticationMiddleware>();
    app.MapGlueApi();
    app.Logger.LogInformation("Starting LLM Glue {Revision}", ProductInfo.Revision);
    await app.RunAsync();
}
else
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = OperationalDirectory(options) });
    ConfigureLogging(builder.Logging, options);
    builder.Services.AddWindowsService(service => service.ServiceName = "LLMGlue");
    builder.Services.AddSystemd();
    if (options.Roles.Agent) AddRemoteAgent(builder.Services, options);
    if (options.Roles.Client) AddClient(builder.Services, options);
    var host = builder.Build();
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LlmGlue").LogInformation("Starting LLM Glue {Revision}", ProductInfo.Revision);
    await host.RunAsync();
}

static void AddServer(IServiceCollection services, GlueOptions options)
{
    services.AddSingleton<IConfigurationGenerator>(_ => CreateOpenCodeGenerator(options));
    services.AddSingleton<IConfigurationGenerator>(_ => new LiteLlmGenerator(liteLlmNamespace: options.Server.LiteLlmNamespace));
    services.AddSingleton(new ArtifactStore(options.Server.ArtifactRetention));
    services.AddSingleton(new CatalogStateRepository(ResolveConfigPath(options, options.Server.StateDirectory), options.Server.ArtifactRetention));
    services.AddSingleton(provider => new ApiState(
        provider.GetServices<IConfigurationGenerator>(),
        provider.GetRequiredService<ArtifactStore>(),
        options.Server.Exclusions,
        provider.GetRequiredService<CatalogStateRepository>()));
    services.AddSingleton(new ApiAuthenticationOptions(
        ResolveSecret(options.Server.ReadToken, options.Server.ReadTokenEnvironment, "Server read token"),
        ResolveSecret(options.Server.WriteToken, options.Server.WriteTokenEnvironment, "Server write token"),
        options.Server.Listen.IsLoopback));
}

static OpenCodeGenerator CreateOpenCodeGenerator(GlueOptions options) => new(
    options.Server.OpenCodeInferenceUrl,
    options.Server.EmbedOpenCodeInferenceApiKey
        ? ResolveSecret(options.Server.OpenCodeInferenceApiKey, options.Server.OpenCodeInferenceApiKeyEnvironment, "OpenCode inference API key")
        : null,
    options.Server.EmbedOpenCodeInferenceApiKey,
    options.Server.OpenCodeNamespace,
    options.Server.OpenCodeNamespaceOverrides,
    options.Server.LiteLlmNamespace);

static void AddAgent(IServiceCollection services, GlueOptions options)
{
    AddDiscoveryAdapters(services, options);
    services.AddSingleton<IHostedService>(provider => new AgentDiscoveryService(
        provider.GetServices<IModelDiscoveryAdapter>(), provider.GetRequiredService<ApiState>(),
        provider.GetRequiredService<ILogger<AgentDiscoveryService>>(),
        TimeSpan.FromSeconds(options.Agent.DiscoveryIntervalSeconds),
        TimeSpan.FromSeconds(options.Agent.RemovalGraceSeconds),
        options.Agent.ExcludeModels));
}

static void AddRemoteAgent(IServiceCollection services, GlueOptions options)
{
    AddDiscoveryAdapters(services, options);
    var destination = options.Agent.SubmitTo ?? throw new ConfigurationException("Agent-only role requires agent.submit_to.");
    var token = ResolveSecret(options.Agent.WriteToken, options.Agent.WriteTokenEnvironment, "Agent write token");
    services.AddSingleton(new RemoteInventoryPublisher(new HttpClient { BaseAddress = EnsureTrailingSlash(destination), Timeout = TimeSpan.FromSeconds(30) }, token));
    services.AddSingleton(TimeProvider.System);
    services.AddSingleton<IHostedService>(provider => new RemoteAgentDiscoveryService(
        provider.GetServices<IModelDiscoveryAdapter>(),
        provider.GetRequiredService<RemoteInventoryPublisher>(),
        provider.GetRequiredService<ILogger<RemoteAgentDiscoveryService>>(),
        provider.GetRequiredService<TimeProvider>(),
        TimeSpan.FromSeconds(options.Agent.DiscoveryIntervalSeconds),
        TimeSpan.FromSeconds(options.Agent.RemovalGraceSeconds),
        options.Agent.ExcludeModels));
}

static void AddDiscoveryAdapters(IServiceCollection services, GlueOptions options)
{
    foreach (var provider in options.Providers)
    {
        var client = new HttpClient { BaseAddress = provider.Url, Timeout = TimeSpan.FromSeconds(provider.DiscoveryTimeoutSeconds) };
        IModelDiscoveryAdapter adapter = provider.Type == "ollama"
            ? new OllamaDiscoveryAdapter(client, provider.Name)
            : new LlamaCppDiscoveryAdapter(
                client,
                provider.Name,
                provider.ModelDirectories.Select(path => ResolveConfigPath(options, path)).ToArray(),
                (provider.ModelPathMappings ?? []).Select(mapping => mapping with { LocalDirectory = ResolveConfigPath(options, mapping.LocalDirectory) }).ToArray(),
                provider.InspectGguf,
                provider.HashModelFiles);
        if (options.Agent.ExcludeModels.Count != 0)
            adapter = new AgentExclusionDiscoveryAdapter(adapter, options.Agent.ExcludeModels);
        if (provider.ProbeMode != CapabilityProbeMode.Off)
        {
            var probeClient = new HttpClient { BaseAddress = provider.Url, Timeout = Timeout.InfiniteTimeSpan };
            adapter = new ProbingDiscoveryAdapter(adapter, new InferenceRouteProbe(
                probeClient, provider.Type, provider.ProbeMode, TimeSpan.FromSeconds(provider.ProbeTimeoutSeconds)));
        }
        services.AddSingleton<IModelDiscoveryAdapter>(adapter);
    }
}

static void AddClient(IServiceCollection services, GlueOptions options)
{
    var source = options.Client.Source ?? options.Server.Listen;
    var sourceClient = new HttpClient { BaseAddress = source, Timeout = TimeSpan.FromSeconds(30) };
    var targets = CliCommands.CreateTargets(options);
    var token = ResolveSecret(options.Client.Token, options.Client.TokenEnvironment, "Client token");
    services.AddSingleton(new ArtifactSyncClient(sourceClient, targets, token));
    services.AddSingleton<IHostedService>(provider => new ClientPollingService(
        provider.GetRequiredService<ArtifactSyncClient>(), TimeSpan.FromSeconds(options.Client.PollIntervalSeconds), provider.GetRequiredService<ILogger<ClientPollingService>>()));
}

static Uri EnsureTrailingSlash(Uri value) => value.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? value : new Uri(value.AbsoluteUri + "/", UriKind.Absolute);
static string ResolveConfigPath(GlueOptions options, string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, options.BaseDirectory);
static Uri? ServiceHealthUrl(GlueOptions options)
{
    if (!options.Roles.Server) return null;
    var builder = new UriBuilder(options.Server.Listen) { Path = "/api/v1/health" };
    if (builder.Host is "0.0.0.0" or "::" or "[::]") builder.Host = "127.0.0.1";
    return builder.Uri;
}
static string? ServiceHealthToken(GlueOptions options) => options.Roles.Server
    ? ResolveSecret(options.Server.ReadToken, options.Server.ReadTokenEnvironment, "Server read token")
    : null;
static string RequiredOption(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    return index >= 0 && index + 1 < values.Length
        ? values[index + 1]
        : throw new ConfigurationException($"Missing internal update option {name}.");
}
static Uri? OptionalUri(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    return index >= 0 && index + 1 < values.Length && !string.IsNullOrWhiteSpace(values[index + 1])
        ? new Uri(values[index + 1], UriKind.Absolute)
        : null;
}
static string? ResolveSecret(string? literal, string? environmentName, string label)
{
    var value = SecretResolver.Resolve(literal, environmentName);
    if ((literal is not null || environmentName is not null) && string.IsNullOrEmpty(value))
        throw new ConfigurationException($"{label} source is configured but unavailable.");
    return value;
}

static string OperationalDirectory(GlueOptions options)
{
    var directory = Path.GetDirectoryName(ResolveConfigPath(options, options.Logging.File))!;
    Directory.CreateDirectory(directory);
    return directory;
}

static void ConfigureLogging(ILoggingBuilder logging, GlueOptions options)
{
    logging.ClearProviders();
    var configuration = new LoggingConfiguration();
    var layout = "${longdate}|${level:uppercase=true}|correlation=${scopeproperty:item=CorrelationId}|${logger}|${message:withexception=true}";
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
