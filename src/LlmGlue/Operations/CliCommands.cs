using System.Text;
using System.Text.Json;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Client;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;
using LlmGlue.Core.Providers;

namespace LlmGlue.Operations;

public static class CliCommands
{
    public static async Task<int> DoctorAsync(GlueOptions options, CancellationToken cancellationToken)
    {
        Console.WriteLine($"LLM Glue {ProductInfo.Revision} doctor");
        Console.WriteLine($"Config: {options.ConfigPath}");
        Console.WriteLine($"Roles: agent={options.Roles.Agent}, server={options.Roles.Server}, client={options.Roles.Client}");
        Console.WriteLine($"Server: {options.Server.Listen}");
        Console.WriteLine($"OpenCode inference route: {(options.Server.OpenCodeInferenceUrl is null ? "direct provider endpoints" : options.Server.OpenCodeInferenceUrl)}");
        Console.WriteLine($"Server read token: {SecretStatus(options.Server.ReadToken, options.Server.ReadTokenEnvironment)}");
        Console.WriteLine($"Server write token: {SecretStatus(options.Server.WriteToken, options.Server.WriteTokenEnvironment)}");
        Console.WriteLine($"Server state: {ResolvePath(options, options.Server.StateDirectory)} (retention {options.Server.ArtifactRetention})");
        if (options.Agent.SubmitTo is not null) Console.WriteLine($"Agent submission: {options.Agent.SubmitTo}");
        if (options.Agent.WriteTokenEnvironment is not null || options.Agent.WriteToken is not null) Console.WriteLine($"Agent write token: {SecretStatus(options.Agent.WriteToken, options.Agent.WriteTokenEnvironment)}");
        Console.WriteLine($"Server exclusions: {options.Server.Exclusions.Count}");
        if (options.Client.Source is not null) Console.WriteLine($"Client source: {options.Client.Source}");
        if (options.Client.TokenEnvironment is not null || options.Client.Token is not null) Console.WriteLine($"Client token: {SecretStatus(options.Client.Token, options.Client.TokenEnvironment)}");
        foreach (var provider in options.Providers)
        {
            Console.WriteLine($"Provider {provider.Name} policy: probe={provider.ProbeMode} timeout={provider.ProbeTimeoutSeconds}s, gguf={provider.InspectGguf}, full-hash={provider.HashModelFiles}");
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, provider.Type == "ollama" ? "api/version" : "v1/models");
                using var client = new HttpClient { BaseAddress = provider.Url, Timeout = TimeSpan.FromSeconds(5) };
                using var response = await client.SendAsync(request, cancellationToken);
                Console.WriteLine($"Provider {provider.Name} ({provider.Type}, {provider.Url}): HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Console.WriteLine($"Provider {provider.Name}: unavailable ({ex.Message})"); }
        }
        foreach (var target in options.Client.Targets) Console.WriteLine($"Target {target.Type}: {ResolvePath(options, target.Path)} ({(File.Exists(ResolvePath(options, target.Path)) ? "exists" : "will be created")})");
        return 0;
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
            if (target.Type == "opencode") targets.Add(new OpenCodeTarget(path, new AtomicFileInstaller(), new NoOpTargetActivator()));
            else targets.Add(new LiteLlmTarget(path, ResolvePath(options, target.PrimaryConfigPath!), new AtomicFileInstaller(), new ServiceRestartActivator(target.ServiceName!, new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, target.HealthUrl)));
        }
        return targets;
    }

    private static async Task<DiscoverySnapshot> DiscoverSnapshotAsync(GlueOptions options, CancellationToken cancellationToken)
    {
        var models = new List<ModelDeployment>();
        foreach (var provider in options.Providers)
        {
            using var client = new HttpClient { BaseAddress = provider.Url, Timeout = TimeSpan.FromSeconds(15) };
            using var probeClient = provider.ProbeMode == CapabilityProbeMode.Off ? null : new HttpClient { BaseAddress = provider.Url, Timeout = Timeout.InfiniteTimeSpan };
            IModelDiscoveryAdapter adapter = provider.Type == "ollama"
                ? new OllamaDiscoveryAdapter(client, provider.Name)
                : new LlamaCppDiscoveryAdapter(
                    client,
                    provider.Name,
                    provider.ModelDirectories.Select(path => ResolvePath(options, path)).ToArray(),
                    provider.InspectGguf,
                    provider.HashModelFiles);
            if (provider.ProbeMode != CapabilityProbeMode.Off)
            {
                adapter = new ProbingDiscoveryAdapter(adapter, new InferenceRouteProbe(
                    probeClient!, provider.Type, provider.ProbeMode, TimeSpan.FromSeconds(provider.ProbeTimeoutSeconds)));
            }
            models.AddRange(await adapter.DiscoverAsync(cancellationToken));
        }
        return DiscoverySnapshot.Create(DateTimeOffset.UtcNow, models);
    }

    private static string ResolvePath(GlueOptions options, string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, options.BaseDirectory);
    internal static string SecretStatus(string? literal, string? environmentName) => literal is not null
        ? "protected sidecar literal (set)"
        : environmentName is not null
            ? $"environment {environmentName} ({(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(environmentName)) ? "missing" : "set")})"
            : "not configured";
}
