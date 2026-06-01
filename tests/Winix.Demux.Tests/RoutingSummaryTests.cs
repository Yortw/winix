#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class RoutingSummaryTests
{
    // ---- helpers ----

    private static RoutingSummary Make(IReadOnlyList<ISink> sinks, bool exitOnChildError = false)
        => new(sinks, exitOnChildError);

    // ---- ExitCode tests ----

    [Fact]
    public void ExitCode_AllDelivered_IsZero()
    {
        var sink = new FakeSink("a");
        sink.Write("line1");
        var summary = Make(new[] { sink });

        Assert.Equal(0, summary.ExitCode);
    }

    [Fact]
    public void ExitCode_Undelivered_IsOne()
    {
        // dieAfter:0 — dies immediately on first write attempt
        var sink = new FakeSink("a", dieAfter: 0);
        sink.Write("x"); // triggers death → UndeliveredCount = 1
        var summary = Make(new[] { sink }, exitOnChildError: false);

        Assert.Equal(1, summary.ExitCode);
    }

    [Fact]
    public void ExitCode_ChildNonZeroUnderStrict_IsTwo()
    {
        var sink = new FakeSink("a") { ChildExitCode = 3 };
        sink.Write("x"); // delivered — no undelivered records
        var summary = Make(new[] { sink }, exitOnChildError: true);

        Assert.Equal(2, summary.ExitCode);
    }

    [Fact]
    public void ExitCode_ChildNonZeroWithoutStrict_IsZero()
    {
        var sink = new FakeSink("a") { ChildExitCode = 3 };
        sink.Write("x"); // delivered
        var summary = Make(new[] { sink }, exitOnChildError: false);

        Assert.Equal(0, summary.ExitCode);
    }

    [Fact]
    public void ExitCode_UndeliveredAndChildError_PrecedenceIsOne()
    {
        // dieAfter:0 — undelivered AND non-zero child; exit code 1 must win
        var sink = new FakeSink("a", dieAfter: 0) { ChildExitCode = 5 };
        sink.Write("x"); // triggers death → UndeliveredCount = 1
        var summary = Make(new[] { sink }, exitOnChildError: true);

        Assert.Equal(1, summary.ExitCode);
    }

    [Fact]
    public void ExitCode_KilledChildSentinelUnderStrict_IsTwo()
    {
        // The -1 sentinel means "killed after timeout" — must count as non-zero
        var sink = new FakeSink("a") { ChildExitCode = -1 };
        sink.Write("x"); // delivered — no undelivered records
        var summary = Make(new[] { sink }, exitOnChildError: true);

        Assert.Equal(2, summary.ExitCode);
    }

    // ---- FormatHuman tests ----

    [Fact]
    public void FormatHuman_KilledChild_RendersKilledAfterTimeout()
    {
        var sink = new FakeSink("cmd-sink") { ChildExitCode = -1 };
        sink.Write("x"); // delivered
        var summary = Make(new[] { sink });

        string result = summary.FormatHuman(useColor: false);

        Assert.Contains("killed after timeout", result, StringComparison.Ordinal);
        Assert.DoesNotContain("exit -1", result, StringComparison.Ordinal);
    }

    // ---- FormatJson tests ----

    [Fact]
    public void FormatJson_IncludesExitCodeAndRoutes()
    {
        var sink = new FakeSink("my-route");
        sink.Write("hello");
        var summary = Make(new[] { sink });

        string json = summary.FormatJson("demux", "1.2.3");

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.Equal("demux", root.GetProperty("tool").GetString());
        Assert.Equal(0, root.GetProperty("exit_code").GetInt32());

        JsonElement routes = root.GetProperty("routes");
        Assert.Equal(1, routes.GetArrayLength());
        Assert.Equal("my-route", routes[0].GetProperty("label").GetString());
        Assert.Equal(1, routes[0].GetProperty("delivered").GetInt64());
        Assert.Equal(0, routes[0].GetProperty("undelivered").GetInt64());
        Assert.False(routes[0].GetProperty("dead").GetBoolean());
    }
}
