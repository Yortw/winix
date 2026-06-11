#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class InternetCheckTests
{
    private static readonly IReadOnlyList<string> TwoEndpoints =
        new[] { "https://a.example/generate_204", "https://b.example/generate_204" };

    // Identity ordering — deterministic test path, no Random.
    private static IReadOnlyList<string> Identity(IReadOnlyList<string> e) => e;

    [Fact]
    public async Task Route_down_short_circuits_with_zero_dns_and_http()
    {
        int dnsCalls = 0, httpCalls = 0;
        var check = new InternetCheck(
            TwoEndpoints,
            routeAvailable: () => false,
            dnsProbe: (_, _) => { dnsCalls++; return Task.FromResult(true); },
            httpProbe: (_, _) => { httpCalls++; return Task.FromResult(new HttpProbeResult(true, 204)); },
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.False(r.Ok);
        Assert.Equal(0, dnsCalls);   // invariant: no traffic when offline
        Assert.Equal(0, httpCalls);
    }

    [Fact]
    public async Task Dns_failure_skips_http_for_that_endpoint()
    {
        int httpCalls = 0;
        var check = new InternetCheck(
            new[] { "https://a.example/generate_204" },
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(false),
            httpProbe: (_, _) => { httpCalls++; return Task.FromResult(new HttpProbeResult(true, 204)); },
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.False(r.Ok);
        Assert.Equal(0, httpCalls);  // invariant: no HTTP when DNS fails
    }

    [Fact]
    public async Task Dns_failure_continues_to_next_endpoint()
    {
        int dnsCalls = 0, httpCalls = 0;
        var check = new InternetCheck(
            TwoEndpoints,
            routeAvailable: () => true,
            dnsProbe: (_, _) => { dnsCalls++; return Task.FromResult(dnsCalls != 1); },  // 1st host fails DNS, 2nd resolves
            httpProbe: (_, _) => { httpCalls++; return Task.FromResult(new HttpProbeResult(true, 204)); },
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.True(r.Ok);            // requirement: a DNS failure on endpoint 1 must NOT stop iteration
        Assert.Equal(2, dnsCalls);    // both endpoints' DNS attempted
        Assert.Equal(1, httpCalls);   // HTTP skipped for the DNS-failed endpoint, fired once for the 2nd
    }

    [Theory]
    [InlineData(200)]   // captive portal login page
    [InlineData(302)]   // captive portal redirect
    [InlineData(403)]
    public async Task Non_204_status_is_not_online(int status)
    {
        var check = new InternetCheck(
            new[] { "https://a.example/generate_204" },
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(true),
            httpProbe: (_, _) => Task.FromResult(new HttpProbeResult(true, status)),
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.False(r.Ok);   // 204 status is the portal discriminator; anything else ⇒ not online
    }

    [Fact]
    public async Task Status_204_is_online()
    {
        var check = new InternetCheck(
            new[] { "https://a.example/generate_204" },
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(true),
            httpProbe: (_, _) => Task.FromResult(new HttpProbeResult(true, 204)),
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Equal("internet", r.Kind);
        Assert.Equal("204 via https://a.example/generate_204", r.Detail);
    }

    [Fact]
    public async Task First_success_short_circuits_remaining_endpoints()
    {
        int httpCalls = 0;
        var check = new InternetCheck(
            TwoEndpoints,
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(true),
            httpProbe: (_, _) => { httpCalls++; return Task.FromResult(new HttpProbeResult(true, 204)); },
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Equal(1, httpCalls);  // invariant: first 204 stops; second endpoint not probed
    }

    [Fact]
    public async Task Falls_through_to_second_endpoint_when_first_fails()
    {
        int httpCalls = 0;
        var check = new InternetCheck(
            TwoEndpoints,
            routeAvailable: () => true,
            dnsProbe: (_, _) => Task.FromResult(true),
            httpProbe: (_, _) =>
            {
                httpCalls++;
                // First endpoint connect-fails; second returns 204.
                return Task.FromResult(httpCalls == 1 ? HttpProbeResult.Unreachable : new HttpProbeResult(true, 204));
            },
            order: Identity);

        CheckResult r = await check.RunAsync(CancellationToken.None);

        Assert.True(r.Ok);
        Assert.Equal(2, httpCalls);
    }
}
