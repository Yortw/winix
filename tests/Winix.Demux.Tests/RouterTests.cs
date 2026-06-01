#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class RouterTests
{
    private static RouteSpec Route(string pat, TargetKind kind = TargetKind.File)
        => new(new Regex(pat), pat, kind, "target");

    private static DemuxOptions Opts(bool all = false, int? field = null, string delim = "")
        => new(new List<RouteSpec>(), null, field, delim, all, false, false, false, false);

    // --- Base 5 ---

    [Fact]
    public void FirstMatch_RoutesToFirstMatchingSinkOnly()
    {
        var s1 = new FakeSink("ERROR");
        var s2 = new FakeSink("R");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("ERROR"), s1), (Route("R"), s2) };

        new Router().Run(new StringReader("ERROR here\nplain\n"), Opts(), routes, null, stdout);

        Assert.Equal(new[] { "ERROR here" }, s1.Lines.ToArray());
        Assert.Empty(s2.Lines);
        Assert.Equal(new[] { "plain" }, stdout.Lines.ToArray());
    }

    [Fact]
    public void All_BroadcastsToEveryMatchingSink()
    {
        var s1 = new FakeSink("ERROR");
        var s2 = new FakeSink("R");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("ERROR"), s1), (Route("R"), s2) };

        new Router().Run(new StringReader("ERROR here\n"), Opts(all: true), routes, null, stdout);

        Assert.Equal(new[] { "ERROR here" }, s1.Lines.ToArray());
        Assert.Equal(new[] { "ERROR here" }, s2.Lines.ToArray());
        Assert.Empty(stdout.Lines);
    }

    [Fact]
    public void Unmatched_GoesToDefaultSinkWhenPresent()
    {
        var s1 = new FakeSink("ERROR");
        var def = new FakeSink("(default)");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("ERROR"), s1) };

        new Router().Run(new StringReader("plain\n"), Opts(), routes, def, stdout);

        Assert.Equal(new[] { "plain" }, def.Lines.ToArray());
        Assert.Empty(stdout.Lines);
    }

    [Fact]
    public void Field_TestsChosenColumnOneBased()
    {
        var s1 = new FakeSink("5xx");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("^5"), s1) };

        new Router().Run(new StringReader("GET 503\nGET 200\n"), Opts(field: 2), routes, null, stdout);

        Assert.Equal(new[] { "GET 503" }, s1.Lines.ToArray());
        Assert.Equal(new[] { "GET 200" }, stdout.Lines.ToArray());
    }

    [Fact]
    public void Field_OutOfRange_IsUnmatched()
    {
        var s1 = new FakeSink("x");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route(".*"), s1) };

        new Router().Run(new StringReader("oneword\n"), Opts(field: 5), routes, null, stdout);

        Assert.Empty(s1.Lines);
        Assert.Equal(new[] { "oneword" }, stdout.Lines.ToArray());
    }

    // --- Adversarial-review 5 ---

    /// <summary>Under --all, a dead sink (dieAfter:0) must not starve its live sibling.</summary>
    [Fact]
    public void All_DeadSibling_StillDeliversToLiveSibling()
    {
        var sinkA = new FakeSink("A", dieAfter: 0); // dies on first write attempt
        var sinkB = new FakeSink("B");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)>
        {
            (Route("match"), sinkA),
            (Route("match"), sinkB),
        };

        new Router().Run(new StringReader("match\n"), Opts(all: true), routes, null, stdout);

        Assert.Equal(new[] { "match" }, sinkB.Lines.ToArray());
        Assert.Empty(sinkA.Lines);
        Assert.Equal(1L, sinkA.UndeliveredCount);
        Assert.True(sinkA.IsDead);
    }

    /// <summary>An interior empty field (comma-split) matches the empty-string regex ^$.</summary>
    [Fact]
    public void Field_EmptyInteriorField_MatchesEmptyRegex()
    {
        var s1 = new FakeSink("empty");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("^$"), s1) };

        // "a,,c" split on "," → ["a", "", "c"]; field 2 is ""; ^$ matches ""
        new Router().Run(new StringReader("a,,c\n"), Opts(field: 2, delim: ","), routes, null, stdout);

        Assert.Equal(new[] { "a,,c" }, s1.Lines.ToArray()); // full original line delivered
        Assert.Empty(stdout.Lines);
    }

    /// <summary>Out-of-range field returns null (not ""), so ^$ never fires — proving null≠"" distinction.</summary>
    [Fact]
    public void Field_OutOfRange_DoesNotMatchEmptyRegex()
    {
        var s1 = new FakeSink("empty");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("^$"), s1) };

        // "a,b" split on "," → ["a", "b"]; field 9 is out of range → Subject returns null → unmatched
        new Router().Run(new StringReader("a,b\n"), Opts(field: 9, delim: ","), routes, null, stdout);

        Assert.Empty(s1.Lines);
        Assert.Equal(new[] { "a,b" }, stdout.Lines.ToArray());
    }

    /// <summary>Empty input produces no writes to any sink.</summary>
    [Fact]
    public void EmptyInput_WritesNothing()
    {
        var s1 = new FakeSink("x");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("x"), s1) };

        new Router().Run(new StringReader(""), Opts(), routes, null, stdout);

        Assert.Empty(s1.Lines);
        Assert.Empty(stdout.Lines);
    }

    /// <summary>A final line with no trailing newline is still routed (TextReader.ReadLine returns it).</summary>
    [Fact]
    public void LastLineNoTrailingNewline_StillRouted()
    {
        var s1 = new FakeSink("ERROR");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("ERROR"), s1) };

        new Router().Run(new StringReader("ERROR x"), Opts(), routes, null, stdout);

        Assert.Equal(new[] { "ERROR x" }, s1.Lines.ToArray());
    }
}
