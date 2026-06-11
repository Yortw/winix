#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class UrlCheckTests
{
    private static HttpProbe Probe(HttpProbeResult result)
        => (_, _) => Task.FromResult(result);

    [Fact]
    public async Task Status_2xx_is_ready()
    {
        var check = new UrlCheck("https://api/health", StatusSpec.Default, Probe(new HttpProbeResult(true, 200)));
        CheckResult r = await check.RunAsync(CancellationToken.None);
        Assert.True(r.Ok);
        Assert.Equal("url", r.Kind);
        Assert.Equal("https://api/health", r.Target);
    }

    [Theory]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(404)]
    [InlineData(301)]   // NOTE: this asserts UrlCheck's status MATCHING given a 301 result. That the
                        // real handler does not transparently FOLLOW the redirect to a 2xx is proven
                        // separately by the AllowAutoRedirect integration test (Task 11, F2).
    public async Task Non_matching_status_is_not_ready(int status)
    {
        var check = new UrlCheck("https://api/health", StatusSpec.Default, Probe(new HttpProbeResult(true, status)));
        CheckResult r = await check.RunAsync(CancellationToken.None);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Connection_failure_is_not_ready()
    {
        var check = new UrlCheck("https://api/health", StatusSpec.Default, Probe(HttpProbeResult.Unreachable));
        CheckResult r = await check.RunAsync(CancellationToken.None);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Custom_status_matches_exact_set()
    {
        Assert.True(StatusSpec.TryParse("200,204", out StatusSpec spec, out _));
        var ready = new UrlCheck("https://x", spec, Probe(new HttpProbeResult(true, 204)));
        var notReady = new UrlCheck("https://x", spec, Probe(new HttpProbeResult(true, 201)));
        Assert.True((await ready.RunAsync(CancellationToken.None)).Ok);
        Assert.False((await notReady.RunAsync(CancellationToken.None)).Ok);
    }
}
