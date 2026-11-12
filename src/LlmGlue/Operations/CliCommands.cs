using System.Text;
using System.Text.Json;
using LlmGlue.Core.Abstractions;
using LlmGlue.Core.Client;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Generation;
using LlmGlue.Core.Documentation;
using LlmGlue.Core.Providers;
using LlmGlue.Core.Reconciliation;
using LlmGlue.Core.Security;
using LlmGlue.Security;

namespace LlmGlue.Operations;

public static class CliCommands
{
    public static string HelpText => ReferenceDocumentation.CliHelpText();

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
        if (options.Server.Listen.Scheme == Uri.UriSchemeHttps)
        {
            using var tls = ServerTlsCertificateManager.LoadOrCreate(options);
            Console.WriteLine($"Server TLS certificate: {tls.CertificatePath}");
            Console.WriteLine($"Server TLS public-key SHA-256: {tls.PublicKeySha256}");
            Console.WriteLine($"Server TLS SNI names: {(options.Server.TlsServerNames.Count == 0 ? "any" : string.Join(", ", options.Server.TlsServerNames))}");
        }
        Console.WriteLine($"OpenCode inference route: {(options.Server.OpenCodeInferenceUrl is null ? "direct provider endpoints" : options.Server.OpenCodeInferenceUrl)}");
        Console.WriteLine($"OpenCode namespace: {options.Server.OpenCodeNamespace} ({options.Server.OpenCodeNamespaceOverrides.Count} override(s))");
        Console.WriteLine($"LiteLLM namespace: {options.Server.LiteLlmNamespace}");
        if (options.Server.OpenCodeInferenceUrl is not null)
        {
            Console.WriteLine($"OpenCode inference credential delivery: {(options.Server.EmbedOpenCodeInferenceApiKey ? "embedded in authenticated artifact" : $"client environment ({OpenCodeGenerator.InferenceApiKeyEnvironment})")}");
            if (options.Server.EmbedOpenCodeInferenceApiKey)
            {
                Console.WriteLine($"OpenCode inference API key: {SecretStatus(options.Server.OpenCodeInferenceApiKey, options.Server.OpenCodeInferenceApiKeyEnvironment)}");
                failures += MissingSecret(options.Server.OpenCodeInferenceApiKey, options.Server.OpenCodeInferenceApiKeyEnvironment);
            }
        }
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

    public static int ValidateTls(GlueOptions options)
    {
        RequireHttpsServer(options);
        using var tls = ServerTlsCertificateManager.LoadExisting(options);
        PrintTlsCertificate("TLS certificate is valid", tls);
        return 0;
    }

    public static Task<int> ReloadTlsAsync(GlueOptions options, CancellationToken cancellationToken) =>
        InvokeTlsOperationAsync(options, "api/v1/tls/reload", cancellationToken);

    public static Task<int> RegenerateTlsAsync(GlueOptions options, bool force, CancellationToken cancellationToken) =>
        InvokeTlsOperationAsync(options, $"api/v1/tls/regenerate?force={force.ToString().ToLowerInvariant()}", cancellationToken);

    private static async Task<int> InvokeTlsOperationAsync(GlueOptions options, string path, CancellationToken cancellationToken)
    {
        RequireHttpsServer(options);
        var source = LocalServerSource(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(source, path));
        var token = SecretResolver.Resolve(options.Server.WriteToken, options.Server.WriteTokenEnvironment);
        if (options.Server.WriteTokenEnvironment is not null && string.IsNullOrEmpty(token))
            throw new ConfigurationException("Server write token environment source is configured but unavailable.");
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var http = TlsHttpClientFactory.Create(null, TimeSpan.FromSeconds(30), allowInvalidCertificate: true, trustedPublicKeySha256: null);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"TLS operation failed: HTTP {(int)response.StatusCode} {body}");
            return 1;
        }
        var result = JsonSerializer.Deserialize<ServerTlsReloadResult>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Server returned an empty TLS operation result.");
        Console.WriteLine(result.Changed ? "TLS certificate reloaded." : "TLS certificate was already current.");
        Console.WriteLine($"Certificate SHA-256: {result.CertificateSha256}");
        Console.WriteLine($"Public-key SHA-256: {result.PublicKeySha256}");
        if (!string.Equals(result.PreviousPublicKeySha256, result.PublicKeySha256, StringComparison.Ordinal))
            Console.WriteLine($"Previous public-key SHA-256: {result.PreviousPublicKeySha256}");
        Console.WriteLine($"Validity: {result.NotBefore:O} through {result.NotAfter:O}");
        return 0;
    }

    private static void RequireHttpsServer(GlueOptions options)
    {
        if (!options.Roles.Server) throw new ConfigurationException("TLS certificate commands require the Server role.");
        if (options.Server.Listen.Scheme != Uri.UriSchemeHttps) throw new ConfigurationException("TLS certificate commands require an HTTPS server.listen URI.");
    }

    private static void PrintTlsCertificate(string heading, ServerTlsCertificate tls)
    {
        Console.WriteLine($"{heading}: {tls.CertificatePath}");
        Console.WriteLine($"Certificate SHA-256: {tls.CertificateSha256}");
        Console.WriteLine($"Public-key SHA-256: {tls.PublicKeySha256}");
        Console.WriteLine($"Subject: {tls.Certificate.Subject}");
        Console.WriteLine($"Validity: {tls.Certificate.NotBefore:O} through {tls.Certificate.NotAfter:O}");
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
            "opencode" => CreateOpenCodeGenerator(options),
            "litellm" => new LiteLlmGenerator(liteLlmNamespace: options.Server.LiteLlmNamespace),
            _ => throw new ConfigurationException("config preview target must be opencode or litellm.")
        };
        var artifact = await generator.GenerateAsync(await DiscoverSnapshotAsync(options, cancellationToken), cancellationToken);
        Console.Write(Encoding.UTF8.GetString(artifact.Content));
        return 0;
    }

    private static OpenCodeGenerator CreateOpenCodeGenerator(GlueOptions options)
    {
        var key = options.Server.EmbedOpenCodeInferenceApiKey
            ? SecretResolver.Resolve(options.Server.OpenCodeInferenceApiKey, options.Server.OpenCodeInferenceApiKeyEnvironment)
            : null;
        if (options.Server.EmbedOpenCodeInferenceApiKey && options.Server.OpenCodeInferenceUrl is not null && string.IsNullOrEmpty(key))
            throw new ConfigurationException("OpenCode inference API key source is configured but unavailable.");
        return new OpenCodeGenerator(options.Server.OpenCodeInferenceUrl, key, options.Server.EmbedOpenCodeInferenceApiKey,
            options.Server.OpenCodeNamespace, options.Server.OpenCodeNamespaceOverrides, options.Server.LiteLlmNamespace);
    }

    public static async Task<int> SyncOnceAsync(GlueOptions options, CancellationToken cancellationToken)
    {
        var source = EffectiveClientSource(options);
        using var http = CreateServerClient(options, source, TimeSpan.FromSeconds(30));
        var token = SecretResolver.Resolve(options.Client.Token, options.Client.TokenEnvironment);
        if ((options.Client.Token is not null || options.Client.TokenEnvironment is not null) && string.IsNullOrEmpty(token)) throw new ConfigurationException("Client token source is configured but unavailable.");
        var results = await new ArtifactSyncClient(http, CreateTargets(options), token).SyncOnceAsync(cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return results.Any(x => x.Error is not null) ? 1 : 0;
    }

    public static async Task<int> StatusAsync(GlueOptions options, CancellationToken cancellationToken)
    {
        var source = EffectiveClientSource(options);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(source, "api/v1/status"));
        if (options.Client.TokenEnvironment is not null || options.Client.Token is not null)
        {
            var token = SecretResolver.Resolve(options.Client.Token, options.Client.TokenEnvironment);
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        using var http = CreateServerClient(options, null, TimeSpan.FromSeconds(10));
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
                var source = EffectiveClientSource(options);
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
    private static Uri EffectiveClientSource(GlueOptions options)
    {
        if (options.Client.Source is not null) return options.Client.Source;
        var builder = new UriBuilder(options.Server.Listen);
        if (builder.Scheme == Uri.UriSchemeHttps) builder.Host = "localhost";
        else if (builder.Host is "0.0.0.0" or "::" or "[::]" or "*" or "+") builder.Host = "127.0.0.1";
        return builder.Uri;
    }
    private static Uri LocalServerSource(GlueOptions options)
    {
        var builder = new UriBuilder(options.Server.Listen) { Host = "localhost" };
        return builder.Uri;
    }
    private static HttpClient CreateServerClient(GlueOptions options, Uri? baseAddress, TimeSpan timeout)
    {
        var pin = options.Client.TrustedServerPublicKeySha256;
        if (pin is null && options.Client.Source is null && options.Server.Listen.Scheme == Uri.UriSchemeHttps)
        {
            using var certificate = ServerTlsCertificateManager.LoadOrCreate(options);
            pin = certificate.PublicKeySha256;
        }
        return TlsHttpClientFactory.Create(baseAddress, timeout, options.Client.AllowInvalidServerCertificate, pin);
    }
    internal static string SecretStatus(string? literal, string? environmentName) => literal is not null
        ? "protected sidecar literal (set)"
        : environmentName is not null
            ? $"environment {environmentName} ({(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(environmentName)) ? "missing" : "set")})"
            : "not configured";
    private static int MissingSecret(string? literal, string? environmentName) => literal is null
        && environmentName is not null
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(environmentName)) ? 1 : 0;
}
