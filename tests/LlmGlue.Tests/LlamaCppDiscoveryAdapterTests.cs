using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    public async Task RouterDoesNotTreatUnloadedModelAsLoaded()
    {
        var handler = new TestHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/models" => TestHttpHandler.Json("[{\"id\":\"gemma.gguf\",\"status\":\"unloaded\",\"meta\":{\"n_ctx_train\":131072}}]"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var model = Assert.Single(await new LlamaCppDiscoveryAdapter(client, "router").DiscoverAsync(TestContext.Current.CancellationToken));
        Assert.False(model.Runtime.Loaded);
        Assert.Equal("router", model.Runtime.Properties["mode"]);
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

    private static void WriteGguf(string path, string architecture, ulong context, uint splitCount)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8, false);
        writer.Write("GGUF"u8);
        writer.Write((uint)3);
        writer.Write((ulong)0);
        writer.Write((ulong)4);
        WriteString(writer, "general.architecture"); writer.Write((uint)8); WriteString(writer, architecture);
        WriteString(writer, $"{architecture}.context_length"); writer.Write((uint)10); writer.Write(context);
        WriteString(writer, "general.file_type"); writer.Write((uint)4); writer.Write((uint)15);
        WriteString(writer, "split.count"); writer.Write((uint)4); writer.Write(splitCount);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }
}
