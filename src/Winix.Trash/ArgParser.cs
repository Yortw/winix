#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Trash;

/// <summary>The operation mode selected by the user.</summary>
public enum TrashMode
{
    /// <summary>Move one or more paths to the recycle bin / Trash.</summary>
    Trash,
    /// <summary>List the current contents of the recycle bin / Trash.</summary>
    List,
    /// <summary>Permanently empty the recycle bin / Trash.</summary>
    Empty,
}

/// <summary>Parses argv into a strongly-typed <see cref="Result"/>. Dispatches on
/// <c>--list</c>/<c>--empty</c> flags; bare positionals select <see cref="TrashMode.Trash"/>.</summary>
public static class ArgParser
{
    /// <summary>Exit code returned when <c>--empty</c> is not performed because it was not confirmed:
    /// the user declined the interactive prompt, or it was refused without <c>--yes</c> when stdin is
    /// not a TTY. Distinct from 0 (success), 1 (a real failure), and the 125–127 tool-error band, so a
    /// script can tell "nothing was emptied because you didn't confirm" from "emptied" or "errored".</summary>
    public const int CancelledExitCode = 2;

    /// <summary>Parse outcome: <see cref="Success"/> when everything is valid; <see cref="Error"/>
    /// is non-null on usage error; <see cref="IsHandled"/> when ShellKit already emitted
    /// help / version / describe output.</summary>
    /// <param name="Mode">The selected operation mode.</param>
    /// <param name="Paths">Normalised full paths for Trash mode; empty for List/Empty.</param>
    /// <param name="Yes">True when <c>--yes</c> / <c>-y</c> was passed (skips --empty confirmation).</param>
    /// <param name="Json">True when <c>--json</c> was passed.</param>
    /// <param name="Error">Usage error message, or null on success.</param>
    /// <param name="IsHandled">True when ShellKit already handled the invocation (help/version/describe).</param>
    /// <param name="ExitCode">Exit code appropriate for the handled/error state; 0 on success.</param>
    /// <param name="UseColor">Whether coloured output should be emitted.</param>
    public sealed record Result(
        TrashMode Mode,
        IReadOnlyList<string> Paths,
        bool Yes,
        bool Json,
        string? Error,
        bool IsHandled,
        int ExitCode,
        bool UseColor)
    {
        /// <summary>True when options parsed cleanly with no errors and no early-exit handling.</summary>
        public bool Success => Error is null && !IsHandled;
    }

    /// <summary>Parse argv (without the executable name).</summary>
    /// <param name="argv">The raw argument vector.</param>
    /// <returns>A <see cref="Result"/> describing the parse outcome.</returns>
    public static Result Parse(IReadOnlyList<string> argv)
    {
        string[] slice = new string[argv.Count];
        for (int i = 0; i < slice.Length; i++) { slice[i] = argv[i]; }

        // Build a fresh parser per call: ShellKit's CommandLineParser.Parse mutates instance
        // state and is not reentrant. A shared static instance races when two callers parse
        // concurrently (observed as a usage-error misparse under xUnit's parallel collections).
        // Matches the per-call build used by mksecret and the rest of the suite.
        ParseResult parsed = BuildParser().Parse(slice);
        bool useColor = parsed.ResolveColor(checkStdErr: true);

        if (parsed.IsHandled) { return new Result(TrashMode.Trash, Array.Empty<string>(), false, false, null, true, parsed.ExitCode, useColor); }
        if (parsed.HasErrors) { return Fail(parsed.Errors[0], useColor); }

        bool hasList = parsed.Has("--list");
        bool hasEmpty = parsed.Has("--empty");

        // Mutual exclusion
        if (hasList && hasEmpty)
        {
            return Fail("--list and --empty are mutually exclusive", useColor);
        }

        // --list / --empty take no paths
        if ((hasList || hasEmpty) && parsed.Positionals.Length > 0)
        {
            return Fail("--list/--empty take no paths", useColor);
        }

        TrashMode mode;
        if (hasList)
        {
            mode = TrashMode.List;
        }
        else if (hasEmpty)
        {
            mode = TrashMode.Empty;
        }
        else
        {
            mode = TrashMode.Trash;
        }

        // Trash mode requires at least one path
        if (mode == TrashMode.Trash && parsed.Positionals.Length == 0)
        {
            return Fail("no paths given; see --help", useColor);
        }

        // Path hygiene (F5): Trash mode only
        IReadOnlyList<string> paths;
        if (mode == TrashMode.Trash)
        {
            // Reject empty/whitespace-only positionals
            foreach (string raw in parsed.Positionals)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return Fail("empty path argument", useColor);
                }
            }

            // Normalise to full paths and de-duplicate (keep first occurrence).
            // Use case-insensitive comparison only on case-insensitive filesystems (Windows/macOS).
            // OrdinalIgnoreCase on Linux would merge genuinely distinct paths (e.g. a.txt vs A.txt).
            StringComparer pathComparer = (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var seen = new HashSet<string>(pathComparer);
            var normalised = new List<string>(parsed.Positionals.Length);
            foreach (string raw in parsed.Positionals)
            {
                string full = Path.GetFullPath(raw);
                if (seen.Add(full))
                {
                    normalised.Add(full);
                }
            }
            paths = normalised;
        }
        else
        {
            paths = Array.Empty<string>();
        }

        bool yes = parsed.Has("--yes");
        bool json = parsed.Has("--json");

        return new Result(mode, paths, yes, json, null, false, 0, useColor);
    }

    private static CommandLineParser BuildParser()
    {
        string version = ResolveVersion();
        return new CommandLineParser("trash", version)
            .Description("Move files and directories to the recycle bin / Trash. Also lists and empties the trash.")
            .Maturity(ToolMaturity.Fresh)
            .PreferDefaultWhen("permanent unrecoverable delete — use rm or shred")
            .StandardFlags()
            .ExpandGlobPositionals()
            // --json is already registered by StandardFlags() above; do NOT re-add it.
            // The tool reads it via parsed.Has("--json").
            .Platform("cross-platform",
                replaces: new[] { "rm -i", "trash-cli", "macos-trash", "PowerShell Remove-Item" },
                valueOnWindows: "Windows has no built-in recycle-bin CLI; Remove-Item deletes permanently.",
                valueOnUnix: "One binary matching trash-cli/macos-trash semantics, no Python/Node runtime required.")
            .ExitCodes(
                (0, "Success (a closed downstream pipe, e.g. | head -1, also exits 0 — not an error)"),
                (CancelledExitCode, "--empty cancelled: declined at the prompt, or refused without --yes when not interactive"),
                (ExitCode.UsageError, "Usage error: unknown flag, --list/--empty misuse, empty/duplicate path, no paths"),
                (1, "One or more paths failed (missing path, permission denied), or some items could not be emptied — like rm"),
                (ExitCode.NotExecutable, "Backend failure: OS recycle-bin API error"))
            .Flag("--list", "Show what is currently in the recycle bin / Trash.")
            .Flag("--empty", "Permanently empty the recycle bin / Trash (prompts unless --yes or non-interactive).")
            .Flag("--yes", "-y", "Skip confirmation when emptying the trash.")
            .Positional("paths...")
            .StdinDescription("Not used.")
            .StdoutDescription("Plain mode: nothing on success (summary to stderr); --list: the listing; --json: a JSON envelope.")
            .StderrDescription("Per-path/operation summary and errors.")
            .Example("trash file.txt", "Send a file to the recycle bin / Trash")
            .Example("trash old.log build/", "Trash multiple paths (a file and a directory)")
            .Example("trash --list", "Show what's in the trash")
            .Example("trash --empty", "Permanently empty the trash");
    }

    // Read AssemblyInformationalVersion (injected via /p:Version by the release pipeline) and strip
    // the "+gitsha" SourceLink suffix. Falls back to AssemblyVersion for dev builds.
    // Matches mksecret/qr/digest/ids/notify.
    private static string ResolveVersion()
    {
        string? info = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            int plus = info.IndexOf('+');
            return plus >= 0 ? info.Substring(0, plus) : info;
        }
        return typeof(ArgParser).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    // ExitCode carries UsageError (not 0) so the field is honest in the error state — a usage error is
    // a 125, and Cli surfaces it as such.
    private static Result Fail(string msg, bool useColor)
        => new(TrashMode.Trash, Array.Empty<string>(), false, false, msg, false, ExitCode.UsageError, useColor);
}
