#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class IntegrationTests
{
    // Opt-in: set WINIX_ONLINE_INTEGRATION=1 to run (requires real, non-captive internet).
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WINIX_ONLINE_INTEGRATION") == "1";

    [SkippableFact]
    public async Task Internet_once_is_ready_on_real_network()
    {
        Skip.IfNot(Enabled, "Set WINIX_ONLINE_INTEGRATION=1 to run network integration tests.");
        int code = await Cli.RunAsync(new[] { "--once" }, TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(0, code);
    }

    [SkippableFact]
    public async Task Url_against_known_204_endpoint_is_ready()
    {
        Skip.IfNot(Enabled, "Set WINIX_ONLINE_INTEGRATION=1 to run network integration tests.");
        int code = await Cli.RunAsync(
            new[] { "--url", "https://www.gstatic.com/generate_204", "--status", "204", "--once" },
            TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(0, code);
    }

    [SkippableFact]
    public async Task Url_against_unreachable_host_times_out_124()
    {
        Skip.IfNot(Enabled, "Set WINIX_ONLINE_INTEGRATION=1 to run network integration tests.");
        // RFC 5737 TEST-NET-1 — guaranteed non-routable; short budget so the test is quick.
        int code = await Cli.RunAsync(
            new[] { "--url", "https://192.0.2.1/health", "--timeout", "3s", "--interval", "1s", "--probe-timeout", "1s" },
            TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(124, code);
    }

    // F2 — proves AllowAutoRedirect=off is actually wired on the production handler. A unit test
    // cannot prove this (it injects a fake probe result); only the real handler reveals whether a
    // 3xx is transparently followed to a 2xx. http://google.com returns a 301 to https; with the
    // default 2xx status and redirects OFF, the 301 never matches → --once → exit 1. If redirects
    // were ON, the followed 200 would match → exit 0. Asserting exit 1 proves redirects are off.
    // VERIFY-AT-IMPLEMENTATION: confirm the chosen URL still issues a single 3xx (not a 200) — pick
    // another stable permanent-redirector if google's behaviour changes.
    [SkippableFact]
    public async Task Redirect_is_not_followed_for_url_check()
    {
        Skip.IfNot(Enabled, "Set WINIX_ONLINE_INTEGRATION=1 to run network integration tests.");
        int code = await Cli.RunAsync(
            new[] { "--url", "http://google.com", "--once", "--probe-timeout", "5s" },
            TextWriter.Null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(1, code);   // 301 not followed → not 2xx → --once miss
    }
}
