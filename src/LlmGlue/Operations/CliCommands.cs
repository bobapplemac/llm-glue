using System.Text;
using System.Text.Json;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Client;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;
using LlmGlue.Core.Providers;
using LlmGlue.Core.Reconciliation;

namespace LlmGlue.Operations;

public static class CliCommands
{
    public const string HelpText = """
        LLM Glue - discover local LLM deployments and synchronize client configuration

        Usage:
          llm-glue [run] [--config PATH]
          llm-glue <command> [options] [--config PATH]

        Commands:
          help, --help, /?, -h          Show this help without loading configuration
          --version                     Show the product revision
          status                       Show server catalog status
          discover                     Print the normalized discovery snapshot
          doctor                       Check configuration, secrets, providers, and targets
          config validate              Validate the selected TOML configuration
          config preview opencode      Preview generated OpenCode JSON
          config preview litellm       Preview generated LiteLLM YAML
          sync --once                  Synchronize configured client targets once
          service <action>             install|repair|start|stop|status|uninstall
          update check                 Check for a newer stable release
          update apply [--service]     Verify, stage, and activate an update
          update rollback [--service]  Restore the rollback executable

        Configuration search order:
          1. Explicit --config PATH
          2. llm-glue.conf, then llm-glue.toml, beside the executable
          3. /etc/llm-glue or %ProgramData%\LLMGlue (same filename precedence)
          4. Legacy /etc/llm-glue.conf or /etc/llm-glue.toml on Linux
        """;

    public static async Task<int> DoctorAsync(GlueOptions options, CancellationToken cancellationToken)
        => await DoctorAsync(options, cancellationToken, provider => new HttpClient
        {
            BaseAddress = provider.Url,
            Timeout = TimeSpan.FromSeconds(5)
        });

    internal static async Task<int> DoctorAsync(
        GlueOptions options,
        CancellationToken cancellationToken,
        Func<ProviderOptions, HttpClient> createProviderClient)
    {
        var failures = 0;
        Console.WriteLine($"LLM Glue {ProductInfo.Revision} doctor");
        Console.WriteLine($"Config: {options.ConfigPath}");
        Console.WriteLine($"Roles: agent={options.Roles.Agent}, server={options.Roles.Server}, client={options.Roles.Client}");
        Console.WriteLine($"Server: {options.Server.Listen}");
        Console.WriteLine($"OpenCode inference route: {(options.Server.OpenCodeInferenceUrl is null ? "direct provider endpoints" : options.Server.OpenCodeInferenceUrl)}");
        Console.WriteLine($"Server read token: {SecretStatus(options.Server.ReadToken, options.Server.ReadTokenEnvironment)}");
        Console.WriteLine($"Server write token: {SecretStatus(options.Server.WriteToken, options.Server.WriteTokenEnvironment)}");
        failures += MissingSecret(options.Server.ReadToken, options.Server.ReadTokenEnvironment);
        failures += MissingSecret(options.Server.WriteToken, options.Server.WriteTokenEnvironment);
        Console.WriteLine($"Server state: {ResolvePath(options, options.Server.StateDirectory)} (retention {options.Server.ArtifactRetention})");
        if (options.Agent.SubmitTo is not null) Console.WriteLine($"Agent submission: {options.Agent.SubmitTo}");
        if (options.Agent.WriteTokenEnvironment is not null || options.Agent.WriteToken is not null)
        {
            Console.WriteLine($"Agent write token: {SecretStatus(options.Agent.WriteToken, options.Agent.WriteTokenEnvironment)}");
            failures += MissingSecret(options.Agent.WriteToken, options.Agent.WriteTokenEnvironment);
        }
        Console.WriteLine($"Server exclusions: {options.Server.Exclusions.Count}");
        Console.WriteLine($"Agent model exclusions: {options.Agent.ExcludeModels.Count}");
        if (options.Client.Source is not null) Console.WriteLine($"Client source: {options.Client.Source}");
        if (options.Client.TokenEnvironment is not null || options.Client.Token is not null)
        {
            Console.WriteLine($"Client token: {SecretStatus(options.Client.Token, options.Client.TokenEnvironment)}");
            failures += MissingSecret(options.Client.Token, options.Client.TokenEnvironment);
        }
        foreach (var provider in options.Providers)
        {
            Console.WriteLine($"Provider {provider.Name} policy: probe={provider.ProbeMode} probe-timeout={provider.ProbeTimeoutSeconds}s, discovery-timeout={provider.DiscoveryTimeoutSeconds}s, gguf={provider.InspectGguf}, full-hash={provider.HashModelFiles}");
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, provider.Type == "ollama" ? "api/version" : "v1/models");
                using var client = createProviderClient(provider);
                using var response = await client.SendAsync(request, cancellationToken);
                Console.WriteLine($"Provider {provider.Name} ({provider.Type}, {provider.Url}): HTTP {(int)response.StatusCode}");
                if (!response.IsSuccessStatusCode) failures++;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Provider {provider.Name}: unavailable ({ex.Message})");
                failures++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Provider {provider.Name}: unavailable ({ex.Message})");
                failures++;
            }
        }
        foreach (var target in options.Client.Targets) Console.WriteLine($"Target {target.Type}: {ResolvePath(options, target.Path)} ({(File.Exists(ResolvePath(options, target.Path)) ? "exists" : "will be created")})");
        Console.WriteLine(failures == 0 ? "Doctor result: healthy" : $"Doctor result: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    public static async Task<int> DiscoverAsync(GlueOptions options, CancellationToken cancellationToken)
    {
        var snapshot = await DiscoverSnapshotAsync(options, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    public static async Task<int> PreviewAsync(GlueOptions options, string target, CancellationToken cancellationToken)
    {
        IConfigurationGenerator generator = target switch
        {
            "opencode" => new OpenCodeGenerator(options.Server.OpenCodeInferenceUrl),
            "litellm" => new LiteLlmGenerator(),
            _ => throw new ConfigurationException("config preview target must be opencode or litellm.")
        };
        var artifact = await generator.GenerateAsync(await DiscoverSnapshotAsync(options, cancellationToken), cancellationToken);
        Console.Write(Encoding.UTF8.GetString(artifact.Content));
        return 0;
    }

    public static async Task<int> SyncOnceAsync(GlueOptions options, CancellationToken cancellationToken)
    {
        var source = options.Client.Source ?? options.Server.Listen;
        using var http = new HttpClient { BaseAddress = source, Timeout = TimeSpan.FromSeconds(30) };
        var token = SecretResolver.Resolve(options.Client.Token, options.Client.TokenEnvironment);
        if ((options.Client.Token is not null || options.Client.TokenEnvironment is not null) && string.IsNullOrEmpty(token)) throw new ConfigurationException("Client token source is configured but unavailable.");
        var results = await new ArtifactSyncClient(http, CreateTargets(options), token).SyncOnceAsync(cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return results.Any(x => x.Error is not null) ? 1 : 0;
    }

    public static async Task<int> StatusAsync(GlueOptions options, CancellationToken cancellationToken)
    {
        var source = options.Client.Source ?? options.Server.Listen;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(source, "api/v1/status"));
        if (options.Client.TokenEnvironment is not null || options.Client.Token is not null)
        {
            var token = SecretResolver.Resolve(options.Client.Token, options.Client.TokenEnvironment);
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await http.SendAsync(request, cancellationToken);
        Console.WriteLine(await response.Content.ReadAsStringAsync(cancellationToken));
        return response.IsSuccessStatusCode ? 0 : 1;
    }

    public static IReadOnlyList<IConfigurationTarget> CreateTargets(GlueOptions options)
    {
        var targets = new List<IConfigurationTarget>();
        foreach (var target in options.Client.Targets)
        {
            var path = ResolvePath(options, target.Path);
            if (target.Type == "opencode")
            {
                var source = options.Client.Source ?? options.Server.Listen;
                targets.Add(new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator(), $"source:{source.AbsoluteUri.TrimEnd('/')}", target.DenyBuiltInOpenCodeProvider));
            }
            else targets.Add(new LiteLlmTarget(path, ResolvePath(options, target.PrimaryConfigPath!), new AtomicFileInstaller(), new ServiceRestartActivator(target.ServiceName!, new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, target.HealthUrl)));
        }
        return targets;
    }

    private static async Task<DiscoverySnapshot> DiscoverSnapshotAsync(GlueOptions options, CancellationToken cancellationToken)
    {
        var models = new List<ModelDeployment>();
        foreach (var provider in options.Providers)
        {
            using var client = new HttpClient { BaseAddress = provider.Url, Timeout = TimeSpan.FromSeconds(provider.DiscoveryTimeoutSeconds) };
            using var probeClient = provider.ProbeMode == CapabilityProbeMode.Off ? null : new HttpClient { BaseAddress = provider.Url, Timeout = Timeout.InfiniteTimeSpan };
            IModelDiscoveryAdapter adapter = provider.Type == "ollama"
                ? new OllamaDiscoveryAdapter(client, provider.Name)
                : new LlamaCppDiscoveryAdapter(
                    client,
                    provider.Name,
                    provider.ModelDirectories.Select(path => ResolvePath(options, path)).ToArray(),
                    (provider.ModelPathMappings ?? []).Select(mapping => mapping with { LocalDirectory = ResolvePath(options, mapping.LocalDirectory) }).ToArray(),
                    provider.InspectGguf,
                    provider.HashModelFiles);
            if (options.Agent.ExcludeModels.Count != 0)
                adapter = new AgentExclusionDiscoveryAdapter(adapter, options.Agent.ExcludeModels);
            if (provider.ProbeMode != CapabilityProbeMode.Off)
            {
                adapter = new ProbingDiscoveryAdapter(adapter, new InferenceRouteProbe(
                    probeClient!, provider.Type, provider.ProbeMode, TimeSpan.FromSeconds(provider.ProbeTimeoutSeconds)));
            }
            models.AddRange(await adapter.DiscoverAsync(cancellationToken));
        }
        return DiscoverySnapshot.Create(DateTimeOffset.UtcNow, ModelExclusionPatterns.Apply(models, options.Agent.ExcludeModels));
    }

    private static string ResolvePath(GlueOptions options, string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, options.BaseDirectory);
    internal static string SecretStatus(string? literal, string? environmentName) => literal is not null
        ? "protected sidecar literal (set)"
        : environmentName is not null
            ? $"environment {environmentName} ({(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(environmentName)) ? "missing" : "set")})"
            : "not configured";
    private static int MissingSecret(string? literal, string? environmentName) => literal is null
        && environmentName is not null
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(environmentName)) ? 1 : 0;
}
