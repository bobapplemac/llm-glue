using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LlmGlue.Core.Domain;
using LlmGlue.Core.Configuration;
using LlmGlue.Core.Providers;

namespace LlmGlue.Tests;

public sealed class LlamaCppDiscoveryAdapterTests
{
    [Fact]
    public async Task DirectServerSeparatesNativeAndEffectiveContext()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/models" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/v1/models" => TestHttpHandler.Json("{\"data\":[{\"id\":\"qwen.gguf\",\"meta\":{\"n_ctx_train\":262144}}]}"),
            "/props" => TestHttpHandler.Json("{\"default_generation_settings\":{\"n_ctx\":65536}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "local").DiscoverAsync(TestContext.Current.CancellationToken));
        Assert.Equal(262144, model.NativeContext!.Value);
        Assert.Equal(65536, model.EffectiveContext!.Value);
        Assert.Equal("direct", model.Runtime.Properties["mode"]);
    }

    [Fact]
    public async Task DirectServerModelsCompatibilityAliasUsesPropsAndBuildInfo()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/models" => TestHttpHandler.Json("""
                {"models":[{"name":"smollm-live","model":"smollm-live"}],"object":"list","data":[{"id":"smollm-live","meta":{"n_ctx_train":8192}}]}
                """),
            "/props" => TestHttpHandler.Json("""
                {"default_generation_settings":{"n_ctx":2048,"params":{"reasoning_format":"none","speculative.type":"draft-mtp"}},"build_info":"b8681-Debian"}
                """),
            "/v1/models" => throw new InvalidOperationException("The compatibility alias should not trigger a second model request."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "local").DiscoverAsync(TestContext.Current.CancellationToken));

        Assert.Equal(8192, model.NativeContext!.Value);
        Assert.Equal(2048, model.EffectiveContext!.Value);
        Assert.True(model.Runtime.Loaded);
        Assert.Equal("b8681-Debian", model.Runtime.Version);
        Assert.Equal("direct", model.Runtime.Properties["mode"]);
        Assert.Equal("true", model.Runtime.Properties["speculative.enabled"]);
        Assert.Equal("draft-mtp", model.Runtime.Properties["speculative.types"]);
        Assert.Equal(ReasoningModeKind.Unknown, model.Capabilities.Reasoning.Kind);
        Assert.Empty(model.Capabilities.Reasoning.Modes);
        Assert.Null(model.Capabilities.Reasoning.Evidence);
    }

    [Fact]
    public async Task DirectServerDoesNotInferReasoningFromResponseParsingFormat()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/models" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/v1/models" => TestHttpHandler.Json("{\"data\":[{\"id\":\"qwen.gguf\"}]}"),
            "/props" => TestHttpHandler.Json("{\"default_generation_settings\":{\"params\":{\"reasoning_format\":\"deepseek\"}}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "local").DiscoverAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ReasoningModeKind.Unknown, model.Capabilities.Reasoning.Kind);
        Assert.Null(model.Capabilities.Reasoning.Evidence);
    }

    [Fact]
    public async Task DirectServerAcceptsPluralSpeculativeShape()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/models" => TestHttpHandler.Json("{\"object\":\"list\",\"data\":[{\"id\":\"model.gguf\"}]}"),
            "/props" => TestHttpHandler.Json("{\"default_generation_settings\":{\"speculative\":false,\"params\":{\"speculative.types\":\"none\"}}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "local")
            .DiscoverAsync(TestContext.Current.CancellationToken));

        Assert.Equal("false", model.Runtime.Properties["speculative.enabled"]);
        Assert.Equal("none", model.Runtime.Properties["speculative.types"]);
    }

    [Fact]
    public async Task DirectServerEnrichesAliasedRouteFromProviderConfirmedPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"llm-glue-llama-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var modelPath = Path.Combine(directory, "actual-model.gguf");
        try
        {
            WriteGguf(modelPath, "qwen3", 262144, splitCount: 2);
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(modelPath)));
            var props = JsonSerializer.Serialize(new
            {
                model_path = modelPath,
                default_generation_settings = new { n_ctx = 65536 }
            });
            var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
            {
                "/models" => TestHttpHandler.Json("{\"object\":\"list\",\"data\":[{\"id\":\"friendly-alias\"}]}"),
                "/props" => TestHttpHandler.Json(props),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

            var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "local", [directory], hashModelFiles: true)
                .DiscoverAsync(TestContext.Current.CancellationToken));

            Assert.Equal("friendly-alias", model.Identity.ModelId);
            Assert.Equal("qwen3", model.Identity.Family);
            Assert.Equal(expectedHash, model.Identity.FileHash);
            Assert.Equal(262144, model.NativeContext!.Value);
            Assert.Equal(65536, model.EffectiveContext!.Value);
            Assert.Equal("2", model.Runtime.Properties["gguf.split_count"]);
            Assert.Contains(model.Warnings, warning => warning.Contains("provider-reported path", StringComparison.Ordinal));
            Assert.DoesNotContain(modelPath, JsonSerializer.Serialize(model), StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DirectServerRejectsProviderPathOutsideAllowlistWithoutLeakingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"llm-glue-llama-path-{Guid.NewGuid():N}");
        var allowed = Path.Combine(root, "allowed");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(outside);
        var outsidePath = Path.Combine(outside, "actual-model.gguf");
        try
        {
            WriteGguf(outsidePath, "qwen3", 262144, splitCount: 1);
            var props = JsonSerializer.Serialize(new { model_path = outsidePath });
            var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
            {
                "/models" => TestHttpHandler.Json("{\"object\":\"list\",\"data\":[{\"id\":\"friendly-alias\"}]}"),
                "/props" => TestHttpHandler.Json(props),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

            var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "local", [allowed])
                .DiscoverAsync(TestContext.Current.CancellationToken));

            Assert.Null(model.Identity.Family);
            Assert.Null(model.NativeContext);
            Assert.Contains(model.Warnings, warning => warning.Contains(nameof(UnauthorizedAccessException), StringComparison.Ordinal));
            Assert.DoesNotContain(outsidePath, JsonSerializer.Serialize(model), StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RouterDoesNotTreatUnloadedModelAsLoaded()
    {
        var requestedReload = false;
        var handler = new TestHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/models")
            {
                requestedReload = request.RequestUri.Query == "?reload=1";
                return TestHttpHandler.Json("[{\"id\":\"gemma.gguf\",\"status\":\"unloaded\",\"meta\":{\"n_ctx_train\":131072}}]");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "router").DiscoverAsync(TestContext.Current.CancellationToken));
        Assert.True(requestedReload);
        Assert.False(model.Runtime.Loaded);
        Assert.Equal("router", model.Runtime.Properties["mode"]);
        Assert.Equal(SupportState.Unknown, model.Capabilities.TextInput.State);
        Assert.Equal(SupportState.Unknown, model.Capabilities.TextOutput.State);
    }

    [Fact]
    public async Task CurrentRouterDataShapePreservesStatusContextsRuntimeAndConfirmedPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"llm-glue-current-router-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var modelPath = Path.Combine(directory, "Qwen3.6-35B-A3B-Q8_0.gguf");
        try
        {
            WriteGguf(modelPath, "qwen35", 262144, splitCount: 1, mtpLayers: 1);
            var payload = JsonSerializer.Serialize(new
            {
                data = new object[]
                {
                    new
                    {
                        id = "bench-01-01",
                        status = new
                        {
                            value = "loaded",
                            args = new[] { "llama-server", "--model", modelPath, "--ctx-size", "131072", "--cache-type-k", "q8_0", "--spec-type", "draft-mtp", "--spec-draft-n-max", "2", "--n-gpu-layers", "all" },
                            memory = new { effective = new { n_ctx = 65536 } }
                        },
                        context_length = 262144,
                        meta = new { n_ctx = 65536, n_ctx_train = 262144 },
                        architecture = new { input_modalities = new[] { "text" }, output_modalities = new[] { "text" } },
                        source = "preset"
                    },
                    new
                    {
                        id = "dormant",
                        status = new { value = "unloaded", args = new[] { "llama-server", "--ctx-size", "0" } },
                        context_length = 131072,
                        architecture = new { input_modalities = new[] { "text", "image" }, output_modalities = new[] { "text" } },
                        source = "preset"
                    }
                }
            });
            var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
            {
                "/models" => TestHttpHandler.Json(payload),
                "/props" => throw new InvalidOperationException("A router catalog must not be parsed as a direct server."),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

            var models = await new LlamaCppDiscoveryAdapter(client, "router", [directory])
                .DiscoverAsync(TestContext.Current.CancellationToken);

            var loaded = Assert.Single(models, model => model.Identity.ModelId == "bench-01-01");
            Assert.True(loaded.Runtime.Loaded);
            Assert.Equal(262144, loaded.NativeContext!.Value);
            Assert.Equal(65536, loaded.EffectiveContext!.Value);
            Assert.Equal("131072", loaded.Runtime.Properties["context.configured"]);
            Assert.Equal("q8_0", loaded.Runtime.Properties["cache.type.k"]);
            Assert.Equal("draft-mtp", loaded.Runtime.Properties["speculative.type"]);
            Assert.Equal("2", loaded.Runtime.Properties["speculative.draft_n_max"]);
            Assert.Equal("all", loaded.Runtime.Properties["gpu.layers"]);
            Assert.Equal("qwen35", loaded.Identity.Family);
            Assert.Equal("present", loaded.Runtime.Properties["gguf.mtp"]);
            Assert.Equal(SupportState.Supported, loaded.Capabilities.TextInput.State);
            Assert.Equal(SupportState.Unsupported, loaded.Capabilities.ImageInput.State);
            Assert.Equal(SupportState.Supported, loaded.Capabilities.TextOutput.State);
            Assert.DoesNotContain(modelPath, JsonSerializer.Serialize(loaded), StringComparison.OrdinalIgnoreCase);

            var dormant = Assert.Single(models, model => model.Identity.ModelId == "dormant");
            Assert.False(dormant.Runtime.Loaded);
            Assert.Equal(131072, dormant.NativeContext!.Value);
            Assert.Null(dormant.EffectiveContext);
            Assert.Equal(SupportState.Supported, dormant.Capabilities.TextInput.State);
            Assert.Equal(SupportState.Supported, dormant.Capabilities.ImageInput.State);
            Assert.Equal(SupportState.Supported, dormant.Capabilities.TextOutput.State);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RouterMapsContainerModelPathIntoExplicitHostAllowlist()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"llm-glue-router-map-{Guid.NewGuid():N}");
        var nested = Path.Combine(directory, "qwen");
        Directory.CreateDirectory(nested);
        var modelPath = Path.Combine(nested, "model.gguf");
        try
        {
            WriteGguf(modelPath, "qwen3", 262144, splitCount: 1);
            var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
            {
                "/models" => TestHttpHandler.Json("{\"data\":[{\"id\":\"mapped\",\"source\":\"preset\",\"status\":{\"value\":\"loaded\",\"args\":[\"llama-server\",\"--model\",\"/models/qwen/model.gguf\"]}}]}"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

            var model = Assert.Single(await new LlamaCppDiscoveryAdapter(
                client, "router", [directory], [new ModelPathMapping("/models", directory)])
                .DiscoverAsync(TestContext.Current.CancellationToken));

            Assert.Equal("qwen3", model.Identity.Family);
            Assert.Equal(262144, model.NativeContext!.Value);
            Assert.DoesNotContain(modelPath, JsonSerializer.Serialize(model), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/models/", JsonSerializer.Serialize(model), StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task EnrichesOnlyProviderConfirmedExactGgufRouteFromAllowlist()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"llm-glue-llama-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var routeFile = Path.Combine(directory, "qwen.gguf");
        var dormantFile = Path.Combine(directory, "dormant.gguf");
        try
        {
            WriteGguf(routeFile, "qwen3", 262144, splitCount: 3);
            WriteGguf(dormantFile, "gemma", 131072, splitCount: 1);
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(routeFile)));
            var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
            {
                "/models" => TestHttpHandler.Json("[{\"id\":\"qwen.gguf\",\"status\":\"loaded\"}]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

            var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "router", [directory], hashModelFiles: true)
                .DiscoverAsync(TestContext.Current.CancellationToken));

            Assert.Equal("qwen3", model.Identity.Family);
            Assert.Equal(expectedHash, model.Identity.FileHash);
            Assert.Equal(262144, model.NativeContext!.Value);
            Assert.Equal("3", model.Runtime.Properties["gguf.split_count"]);
            Assert.Equal("sha256-observed", model.Runtime.Properties["gguf.file_hash"]);
            Assert.DoesNotContain(model.Warnings, x => x.Contains(dormantFile, StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ReportsMtpMetadataAsRuntimeTraitWithoutInventingClientModes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"llm-glue-llama-mtp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var routeFile = Path.Combine(directory, "qwen-mtp.gguf");
        try
        {
            WriteGguf(routeFile, "qwen35", 262144, splitCount: 1, mtpLayers: 1);
            var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
            {
                "/models" => TestHttpHandler.Json("[{\"id\":\"qwen-mtp.gguf\",\"status\":\"loaded\"}]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

            var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "router", [directory])
                .DiscoverAsync(TestContext.Current.CancellationToken));

            Assert.Equal("present", model.Runtime.Properties["gguf.mtp"]);
            Assert.Equal("1", model.Runtime.Properties["gguf.mtp_layers"]);
            Assert.Equal(ReasoningModeKind.Unknown, model.Capabilities.Reasoning.Kind);
            Assert.Empty(model.Capabilities.Reasoning.Modes);
            Assert.Contains(model.Warnings, warning => warning.Contains("runtime artifact trait", StringComparison.Ordinal));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void WriteGguf(string path, string architecture, ulong context, uint splitCount, uint? mtpLayers = null)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8, false);
        writer.Write("GGUF"u8);
        writer.Write((uint)3);
        writer.Write((ulong)0);
        writer.Write((ulong)(mtpLayers is null ? 4 : 5));
        WriteString(writer, "general.architecture"); writer.Write((uint)8); WriteString(writer, architecture);
        WriteString(writer, $"{architecture}.context_length"); writer.Write((uint)10); writer.Write(context);
        WriteString(writer, "general.file_type"); writer.Write((uint)4); writer.Write((uint)15);
        WriteString(writer, "split.count"); writer.Write((uint)4); writer.Write(splitCount);
        if (mtpLayers is not null)
        {
            WriteString(writer, $"{architecture}.nextn_predict_layers"); writer.Write((uint)4); writer.Write(mtpLayers.Value);
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }
}
