#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    // Drives the real Cli.Run path with a stubbed backend that returns one listed item,
    // so the test exercises production wiring (ArgParser.ResolveColor → r.UseColor → ListTable),
    // not a side formatter.
    private static (string outText, string errText) RunList(string colorFlag)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        Cli.Run(new[] { "--list", colorFlag }, so, se, backendOverride: new OneItemBackend());
        return (so.ToString(), se.ToString());
    }

    [Fact]
    public void List_WithColor_EmitsAnsi()
    {
        var (outText, _) = RunList("--color=always");
        Assert.Contains(Esc, outText, StringComparison.Ordinal);
    }

    [Fact]
    public void List_NoColor_IsPlain()
    {
        var (outText, _) = RunList("--no-color");
        Assert.DoesNotContain(Esc, outText, StringComparison.Ordinal);
        Assert.Contains("Name", outText, StringComparison.Ordinal); // header still present
    }

    [Fact]
    public void TrashSummary_ColorTogglesAnsi()
    {
        Assert.Contains(Esc, Formatting.TrashSummary(3, useColor: true), StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, Formatting.TrashSummary(3, useColor: false), StringComparison.Ordinal);
    }

    [Fact]
    public void TrashSummary_Plain_ExactString()
    {
        // A3: pin the exact plain string so no stray bytes leak when colour is off.
        Assert.Equal("trash: moved 3 item(s) to trash", Formatting.TrashSummary(3, useColor: false));
    }

    // P2-B: drives RunTrash's red per-path failure line via a backend that fails one path.
    [Fact]
    public void TrashFailureLine_ColorTogglesAnsi()
    {
        var seColor = new StringWriter();
        Cli.Run(new[] { "x.txt", "--color=always" }, new StringWriter(), seColor, backendOverride: new OneFailBackend());
        Assert.Contains(Esc, seColor.ToString(), StringComparison.Ordinal);

        var sePlain = new StringWriter();
        Cli.Run(new[] { "x.txt", "--no-color" }, new StringWriter(), sePlain, backendOverride: new OneFailBackend());
        Assert.DoesNotContain(Esc, sePlain.ToString(), StringComparison.Ordinal);
        Assert.Contains("x.txt", sePlain.ToString(), StringComparison.Ordinal); // path still present
    }

    // Minimal in-memory backend returning one trashed item for the --list path.
    private sealed class OneItemBackend : ITrashBackend
    {
        public TrashResult Trash(IReadOnlyList<string> paths)
            => throw new NotImplementedException();

        public IReadOnlyList<TrashedItem> List()
            => new[] { new TrashedItem("a.txt", "/x/a.txt", DateTime.UtcNow, 12, "home") };

        public EmptyResult Empty()
            => throw new NotImplementedException();
    }

    // Backend whose Trash returns one FAILED outcome (drives the red per-path failure line).
    private sealed class OneFailBackend : ITrashBackend
    {
        public TrashResult Trash(IReadOnlyList<string> paths)
            => new TrashResult
            {
                Outcomes = new[] { PathOutcome.Failed("x.txt", "no such file") }
            };

        public IReadOnlyList<TrashedItem> List()
            => Array.Empty<TrashedItem>();

        public EmptyResult Empty()
            => throw new NotImplementedException();
    }
}
