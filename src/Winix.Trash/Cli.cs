#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using Yort.ShellKit;

namespace Winix.Trash;

/// <summary>Library entry point. <c>Program.cs</c> is a thin shim around <see cref="Run"/> so the
/// full orchestration (arg dispatch, backend calls, --empty safety gating) is unit-testable with a
/// <see cref="FakeTrashBackend"/>. The injectable overrides mirror the mksecret / qr seam pattern.</summary>
public static class Cli
{
    /// <summary>Runs the full trash pipeline: parse args, dispatch to backend, format output, return exit code.</summary>
    /// <param name="args">The raw argv (without the executable name).</param>
    /// <param name="stdout">Destination for primary output (list table, JSON envelopes).</param>
    /// <param name="stderr">Destination for summary lines, per-path errors, and prompts.</param>
    /// <param name="backendOverride">Inject a test double; production passes null to use
    /// <see cref="TrashBackendFactory.Create"/>.</param>
    /// <param name="isInteractiveOverride">Overrides the stdin-redirected check for --empty gating;
    /// production passes null to use <c>!Console.IsInputRedirected</c>.</param>
    /// <param name="readLineOverride">Overrides console input for the --empty confirmation prompt;
    /// production passes null to use <c>Console.In.ReadLine</c>.</param>
    /// <returns>Exit code per POSIX convention: 0 success, 1 partial failure, 125 usage error, 126 backend error.</returns>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        ITrashBackend? backendOverride = null,
        Func<bool>? isInteractiveOverride = null,
        Func<string?>? readLineOverride = null)
    {
        ArgParser.Result r = ArgParser.Parse(args);

        if (r.IsHandled) { return r.ExitCode; }
        if (!r.Success)
        {
            stderr.WriteLine($"trash: {r.Error}");
            stderr.WriteLine("Run 'trash --help' for usage.");
            return ExitCode.UsageError;
        }

        ITrashBackend backend = backendOverride ?? TrashBackendFactory.Create();

        try
        {
            switch (r.Mode)
            {
                case TrashMode.Trash:
                    return RunTrash(r, backend, stdout, stderr);

                case TrashMode.List:
                    return RunList(r, backend, stdout);

                case TrashMode.Empty:
                    return RunEmpty(r, backend, stdout, stderr, isInteractiveOverride, readLineOverride);

                default:
                    // Unreachable: ArgParser only produces the three modes above.
                    stderr.WriteLine($"trash: error: unexpected mode {r.Mode}");
                    return ExitCode.NotExecutable;
            }
        }
        catch (Exception ex)
        {
            // Backend threw a catastrophic OS failure (TrashException, Win32Exception, IOException, etc.).
            // There is NO narrow IOException-success swallow here — that would mask real errors.
            // A closed downstream pipe does NOT throw on .NET (the runtime absorbs it silently at
            // the Console.Out layer on both Windows and Linux), so a pipe-close always stays exit 0
            // without needing a special catch arm. Mirrors src/Winix.MkSecret/Cli.cs.
            //
            // SAFE classes print .Message verbatim: TrashException (project-authored errno/HRESULT
            // diagnostics), Win32Exception (native-OS text), and PlatformNotSupportedException (factory
            // English). Everything else routes through SafeError.Describe, because under
            // UseSystemResourceKeys a framework .Message is a bare CoreLib resource key (CR-2: the
            // SHEmptyRecycleBin HRESULT was previously flattened to "InvalidOperationException").
            string detail = ex is TrashException or Win32Exception or PlatformNotSupportedException
                ? ex.Message
                : SafeError.Describe(ex);
            stderr.WriteLine($"trash: error: {detail}");
            return ExitCode.NotExecutable;
        }
    }

    // ── Mode handlers ─────────────────────────────────────────────────────────

    private static int RunTrash(
        ArgParser.Result r,
        ITrashBackend backend,
        TextWriter stdout,
        TextWriter stderr)
    {
        TrashResult result = backend.Trash(r.Paths);

        if (r.Json)
        {
            stdout.WriteLine(Formatting.TrashJson(result));
        }
        else
        {
            stderr.WriteLine(Formatting.TrashSummary(result.SuccessCount, r.UseColor));
            foreach (PathOutcome outcome in result.Outcomes)
            {
                if (!outcome.Succeeded)
                {
                    string red   = Yort.ShellKit.AnsiColor.Red(r.UseColor);
                    string reset = Yort.ShellKit.AnsiColor.Reset(r.UseColor);
                    stderr.WriteLine($"trash: {red}{outcome.Path}: {outcome.Error}{reset}");
                }
            }
        }

        return result.AnyFailed ? 1 : ExitCode.Success;
    }

    private static int RunList(
        ArgParser.Result r,
        ITrashBackend backend,
        TextWriter stdout)
    {
        System.Collections.Generic.IReadOnlyList<TrashedItem> items = backend.List();

        if (r.Json)
        {
            stdout.WriteLine(Formatting.ListJson(items));
        }
        else
        {
            string table = Formatting.ListTable(items, r.UseColor);
            if (!string.IsNullOrEmpty(table))
            {
                stdout.Write(table);
            }
        }

        return ExitCode.Success;
    }

    private static int RunEmpty(
        ArgParser.Result r,
        ITrashBackend backend,
        TextWriter stdout,
        TextWriter stderr,
        Func<bool>? isInteractiveOverride,
        Func<string?>? readLineOverride)
    {
        bool interactive = (isInteractiveOverride ?? (() => !Console.IsInputRedirected))();

        if (!r.Yes)
        {
            if (interactive)
            {
                // Prompt to stderr so it is visible even when stdout is redirected.
                stderr.Write("Permanently delete the trash? [y/N] ");

                string? line = (readLineOverride ?? Console.In.ReadLine)();
                string trimmed = line?.Trim() ?? string.Empty;

                if (!string.Equals(trimmed, "y", StringComparison.OrdinalIgnoreCase))
                {
                    // Declined: exit 2 (cancelled), not 0 — the requested empty did not happen, so a
                    // caller can distinguish "you cancelled" from "emptied".
                    stderr.WriteLine("trash: cancelled");
                    return ArgParser.CancelledExitCode;
                }
            }
            else
            {
                // Non-interactive without --yes: refuse safely rather than permanently destroying data.
                // Also exit 2 (cancelled) — the empty was not performed.
                stderr.WriteLine("trash: refusing to empty without --yes when not interactive");
                return ArgParser.CancelledExitCode;
            }
        }

        // Proceed with empty. The backend call is inside the outer try/catch(Exception) in Run(),
        // so any backend exception (including IOException) surfaces as exit 126, not 0.
        EmptyResult e = backend.Empty();

        if (r.Json)
        {
            stdout.WriteLine(Formatting.EmptyJson(e.ItemsRemoved, e.FailedCount));
        }
        else
        {
            stderr.WriteLine($"trash: emptied {e.ItemsRemoved} item(s)");
            if (e.FailedCount > 0)
            {
                stderr.WriteLine($"trash: {e.FailedCount} item(s) could not be removed");
            }
        }

        // An item that could not be permanently removed (data still present) is a real failure — exit 1
        // so a script does not assume the trash is empty.
        return e.FailedCount > 0 ? 1 : ExitCode.Success;
    }
}
