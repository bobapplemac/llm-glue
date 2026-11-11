using LlmGlue.Operations;

namespace LlmGlue.Tests;

public sealed class UpdateManagerTests
{
    [Fact]
    public async Task SelectsOnlyPinnedStableOfficialAsset()
    {
        var handler = new TestHttpHandler(_ => TestHttpHandler.Json("""
            {
              "tag_name":"r2", "draft":false, "prerelease":false,
              "assets":[
                {"name":"llm-glue-r2-win-x64.zip","browser_download_url":"https://example.invalid/windows"},
                {"name":"llm-glue-r2-linux-x64.tar.gz","browser_download_url":"https://example.invalid/linux"},
                {"name":"SHA256SUMS","browser_download_url":"https://example.invalid/sums"}
              ]
            }
            """));
        using var http = new HttpClient(handler);
        var update = await UpdateManager.CheckAsync(http, TestContext.Current.CancellationToken);
        Assert.NotNull(update);
        Assert.Equal(2, update.Revision);
        Assert.StartsWith("llm-glue-r2-", update.AssetName, StringComparison.Ordinal);
        Assert.Equal("example.invalid", update.ChecksumsUrl.Host);
    }

    [Theory]
    [InlineData("r0", false, false)]
    [InlineData("v1.2.3", false, false)]
    [InlineData("r1", true, false)]
    [InlineData("r1", false, true)]
    public async Task IgnoresNonOfficialOrUnstableReleases(string tag, bool draft, bool prerelease)
    {
        var handler = new TestHttpHandler(_ => TestHttpHandler.Json($$"""{"tag_name":"{{tag}}","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":{{prerelease.ToString().ToLowerInvariant()}},"assets":[]}"""));
        using var http = new HttpClient(handler);
        Assert.Null(await UpdateManager.CheckAsync(http, TestContext.Current.CancellationToken));
    }
}
