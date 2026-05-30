#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Winix.Trash;
using Xunit;
using Yort.ShellKit;

namespace Winix.Trash.Tests;

/// <summary>Unit tests for <see cref="Cli.Run"/> orchestration, all exercised through a
/// <see cref="FakeTrashBackend"/>. No real OS operations are performed.</summary>
public class CliTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (int code, string outText, string errText) Run(
        string[] args,
        FakeTrashBackend? backend = null,
        Func<bool>? isInteractive = null,
        Func<string?>? readLine = null)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = Cli.Run(args, so, se, backend, isInteractive, readLine);
        return (code, so.ToString(), se.ToString());
    }

    private static FakeTrashBackend AllSuccess(string path = "/fake/file.txt")
        => new FakeTrashBackend(trashResult: new TrashResult
        {
            Outcomes = new[] { new PathOutcome(path, null) }
        });

    // ── Trash mode ───────────────────────────────────────────────────────────

    [Fact]
    public void Trash_success_writes_summary_to_stderr_and_returns_exit0()
    {
        var backend = AllSuccess();
        var (code, outText, errText) = Run(new[] { "/fake/file.txt" }, backend);

        Assert.Equal(ExitCode.Success, code);
        Assert.Equal(string.Empty, outText);
        Assert.Contains("moved 1 item(s) to trash", errText);
    }

    [Fact]
    public void Trash_json_writes_envelope_to_stdout_and_returns_exit0()
    {
        var backend = AllSuccess();
        var (code, outText, errText) = Run(new[] { "/fake/file.txt", "--json" }, backend);

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("\"trashed\"", outText);
        Assert.Contains("\"failed\"", outText);
        // summary must NOT appear on stderr when --json is used
        Assert.DoesNotContain("moved", errText);
    }

    [Fact]
    public void Trash_one_failed_path_returns_exit1_and_emits_error_to_stderr()
    {
        var backend = new FakeTrashBackend(trashResult: new TrashResult
        {
            Outcomes = new[]
            {
                new PathOutcome("/fake/file.txt", "Permission denied")
            }
        });
        var (code, _, errText) = Run(new[] { "/fake/file.txt" }, backend);

        Assert.Equal(1, code);
        // F7: per-path error must be surfaced on stderr
        Assert.Contains("Permission denied", errText);
        Assert.Contains("/fake/file.txt", errText);
    }

    [Fact]
    public void Trash_all_failed_returns_exit1()
    {
        var backend = new FakeTrashBackend(trashResult: new TrashResult
        {
            Outcomes = new[]
            {
                new PathOutcome("/a", "not found"),
                new PathOutcome("/b", "not found")
            }
        });
        var (code, _, _) = Run(new[] { "/a", "/b" }, backend);

        Assert.Equal(1, code);
    }

    // ── List mode ─────────────────────────────────────────────────────────────

    [Fact]
    public void List_writes_table_to_stdout_and_returns_exit0()
    {
        var items = new List<TrashedItem>
        {
            new TrashedItem("note.txt", "/home/u/note.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100L, "home")
        };
        var backend = new FakeTrashBackend(listItems: items);
        var (code, outText, errText) = Run(new[] { "--list" }, backend);

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("note.txt", outText);
        Assert.Equal(string.Empty, errText);
    }

    [Fact]
    public void List_json_writes_items_to_stdout_and_nothing_to_stderr()
    {
        var items = new List<TrashedItem>
        {
            new TrashedItem("a.txt", "/home/u/a.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 12L, "home")
        };
        var backend = new FakeTrashBackend(listItems: items);
        var (code, outText, errText) = Run(new[] { "--list", "--json" }, backend);

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("\"items\"", outText);
        Assert.Contains("a.txt", outText);
        Assert.Equal(string.Empty, errText);
    }

    // ── Empty mode ───────────────────────────────────────────────────────────

    [Fact]
    public void Empty_with_yes_calls_Empty_and_returns_exit0()
    {
        var backend = new FakeTrashBackend(emptyResult: new EmptyResult(3));
        var (code, _, errText) = Run(new[] { "--empty", "--yes" }, backend);

        Assert.Equal(ExitCode.Success, code);
        Assert.True(backend.EmptyCalled, "Empty() should have been called");
        Assert.Contains("3 item(s)", errText);
    }

    [Fact]
    public void Empty_with_failures_reports_them_and_returns_exit1()
    {
        // 1 removed, 2 could not be removed (data still present) → exit 1 + a surfaced failure line.
        var backend = new FakeTrashBackend(emptyResult: new EmptyResult(1, 2));
        var (code, _, errText) = Run(new[] { "--empty", "--yes" }, backend);

        Assert.Equal(1, code);
        Assert.True(backend.EmptyCalled);
        Assert.Contains("emptied 1 item(s)", errText);
        Assert.Contains("2 item(s) could not be removed", errText);
    }

    [Fact]
    public void Empty_with_failures_json_reports_failed_count()
    {
        var backend = new FakeTrashBackend(emptyResult: new EmptyResult(1, 2));
        var (code, outText, _) = Run(new[] { "--empty", "--yes", "--json" }, backend);

        Assert.Equal(1, code);
        Assert.Contains("\"emptied\":1", outText);
        Assert.Contains("\"failed\":2", outText);
    }

    [Fact]
    public void Empty_non_interactive_without_yes_does_not_call_Empty_and_returns_cancelled()
    {
        // non-interactive = console input is redirected
        var backend = new FakeTrashBackend(emptyResult: new EmptyResult(5));
        var (code, _, errText) = Run(
            new[] { "--empty" },
            backend,
            isInteractive: () => false);

        Assert.Equal(ArgParser.CancelledExitCode, code);
        Assert.False(backend.EmptyCalled, "Empty() must NOT be called in non-interactive mode without --yes");
        Assert.Contains("refusing to empty", errText);
    }

    [Fact]
    public void Empty_interactive_reads_n_does_not_call_Empty()
    {
        var backend = new FakeTrashBackend(emptyResult: new EmptyResult(2));
        var (code, _, errText) = Run(
            new[] { "--empty" },
            backend,
            isInteractive: () => true,
            readLine: () => "n");

        Assert.Equal(ArgParser.CancelledExitCode, code);
        Assert.False(backend.EmptyCalled, "Empty() must NOT be called when user enters 'n'");
        Assert.Contains("cancelled", errText);
    }

    [Fact]
    public void Empty_interactive_reads_empty_does_not_call_Empty()
    {
        var backend = new FakeTrashBackend(emptyResult: new EmptyResult(2));
        var (code, _, _) = Run(
            new[] { "--empty" },
            backend,
            isInteractive: () => true,
            readLine: () => string.Empty);

        Assert.Equal(ArgParser.CancelledExitCode, code);
        Assert.False(backend.EmptyCalled, "Empty() must NOT be called when user enters empty string");
    }

    [Fact]
    public void Empty_interactive_reads_null_eof_does_not_call_Empty()
    {
        var backend = new FakeTrashBackend(emptyResult: new EmptyResult(2));
        var (code, _, _) = Run(
            new[] { "--empty" },
            backend,
            isInteractive: () => true,
            readLine: () => null);

        Assert.Equal(ArgParser.CancelledExitCode, code);
        Assert.False(backend.EmptyCalled, "Empty() must NOT be called on EOF");
    }

    [Fact]
    public void Empty_interactive_reads_y_calls_Empty_and_returns_exit0()
    {
        // F14: the isInteractiveOverride + readLineOverride seam makes this a unit test, not manual.
        var backend = new FakeTrashBackend(emptyResult: new EmptyResult(4));
        var (code, _, errText) = Run(
            new[] { "--empty" },
            backend,
            isInteractive: () => true,
            readLine: () => "y");

        Assert.Equal(ExitCode.Success, code);
        Assert.True(backend.EmptyCalled, "Empty() must be called when user enters 'y'");
        Assert.Contains("4 item(s)", errText);
    }

    [Fact]
    public void Empty_interactive_reads_Y_uppercase_calls_Empty()
    {
        var backend = new FakeTrashBackend(emptyResult: new EmptyResult(1));
        var (code, _, _) = Run(
            new[] { "--empty" },
            backend,
            isInteractive: () => true,
            readLine: () => "Y");

        Assert.Equal(ExitCode.Success, code);
        Assert.True(backend.EmptyCalled, "Empty() must be called when user enters 'Y'");
    }

    // ── Error handling ───────────────────────────────────────────────────────

    [Fact]
    public void Backend_throws_generic_exception_returns_126_with_error_prefix_and_nothing_on_stdout()
    {
        var backend = new FakeTrashBackend(
            trashException: new InvalidOperationException("OS API failed"));
        var (code, outText, errText) = Run(new[] { "/fake/file.txt" }, backend);

        Assert.Equal(ExitCode.NotExecutable, code);
        Assert.Contains("trash: error:", errText);
        Assert.Contains("OS API failed", errText);
        Assert.Equal(string.Empty, outText);
    }

    [Fact]
    public void Backend_throws_IOException_returns_126_not_0()
    {
        // F10: This test locks the mksecret IOException lesson.
        // A backend IOException is a catastrophic OS failure → exit 126.
        // The ONLY thing that maps to 0 is a closed downstream pipe (which .NET's runtime absorbs
        // silently at Console.Out and never throws). A catch(IOException)=>0 swallow would be wrong.
        var backend = new FakeTrashBackend(
            trashException: new IOException("disk full or OS API failed"));
        var (code, outText, errText) = Run(new[] { "/fake/file.txt" }, backend);

        Assert.Equal(ExitCode.NotExecutable, code);
        Assert.Contains("trash: error:", errText);
        Assert.Equal(string.Empty, outText);
    }

    [Fact]
    public void Usage_error_returns_exit125()
    {
        // No paths given with no mode flag → usage error from ArgParser.
        var (code, _, errText) = Run(Array.Empty<string>());

        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("trash:", errText);
    }
}
