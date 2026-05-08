#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Less;

/// <summary>
/// Library-level entry point for the less CLI. <c>Program.cs</c> is a thin shim around
/// <see cref="Run"/> that wires up <c>Console.*</c> and forwards the exit code; all
/// orchestration lives here so it can be exercised by unit tests.
/// </summary>
/// <remarks>
/// Round-1 tier-2 review extraction (test-analyzer C2 + code-reviewer I1). The seam
/// exposes:
/// <list type="bullet">
///   <item>The F4 bare-<c>-</c> POSIX stdin-marker pre-parser intercept.</item>
///   <item>The F5 too-many-files exit-2 routing with the canonical user-facing message.</item>
///   <item>The F6 directory → "Is a directory" message routing.</item>
///   <item>The F7 catch broadening (FileNotFoundException → 1, IOException → 1,
///         UnauthorizedAccessException → 1 with synthesised English message).</item>
///   <item>The POSIX-traditional exit code 2 for usage errors (deliberate suite divergence
///         since less replaces a POSIX tool — see <c>feedback_match_established_tool_conventions.md</c>).</item>
///   <item>The F2 colour resolution → <c>LessOptions.StripAnsi</c> wiring.</item>
/// </list>
/// to deterministic byte-level testing without spawning a process or entering the
/// interactive pager loop.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the less pipeline: parse args, resolve options, load input, dispatch to the
    /// pager, return exit code. All side effects route through the supplied parameters
    /// — no <c>Console.*</c> references inside this method.
    /// </summary>
    /// <param name="args">Command-line arguments (without the executable name).</param>
    /// <param name="stdout">Output writer (paged content goes here when isStdoutRedirected).</param>
    /// <param name="stderr">Error writer (errors, missing-filename, multi-file rejection).</param>
    /// <param name="isStdoutRedirected">
    /// Whether stdout is redirected (<c>Console.IsOutputRedirected</c> in production).
    /// Drives F1 dump-strategy selection (redirected stdout → direct dump, no pager loop).
    /// </param>
    /// <param name="isStdinRedirected">
    /// Whether stdin is redirected (<c>Console.IsInputRedirected</c> in production). Drives
    /// the "no-file-no-stdin" usage error and the implicit-stdin source.
    /// </param>
    /// <param name="pagerRunner">
    /// Optional seam for the pager invocation. Tests substitute a fake to avoid
    /// entering the interactive Pager.Run loop. Default: <see cref="DefaultPagerRunner"/>
    /// which constructs a real <see cref="Pager"/> and runs it.
    /// </param>
    /// <param name="lessEnvVar">
    /// Optional override for the <c>LESS</c> environment variable lookup. Tests pass an
    /// explicit value; production reads the actual env var.
    /// </param>
    /// <returns>Process exit code: 0 success, 1 file/runtime error, 2 usage error
    /// (POSIX-traditional, NOT the suite's 125 — deliberate divergence).</returns>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        bool isStdoutRedirected,
        bool isStdinRedirected,
        Func<LessOptions, InputSource, int>? pagerRunner = null,
        string? lessEnvVar = null)
    {
        _ = stdout; // paged content writes via Console.* through Pager today; reserved
        pagerRunner ??= DefaultPagerRunner;
        // Use sentinel "" to distinguish "explicit empty env var" from "tests didn't pass one";
        // null means "fetch from environment" (production path).
        if (lessEnvVar is null)
        {
            lessEnvVar = Environment.GetEnvironmentVariable("LESS");
        }

        // Tier-2 baseline 2026-05-07 finding F4: POSIX convention treats a bare "-" argument
        // as an explicit stdin marker (e.g. `less -`, `cat file -`). ShellKit's CommandLineParser
        // would consume "-" as an unknown short option and fail with exit 125. Strip it from
        // args[] up front and remember it as an explicit-stdin signal.
        bool useStdinFromDash = false;
        if (args.Length > 0)
        {
            var filteredArgs = new List<string>(args.Length);
            foreach (string a in args)
            {
                if (a == "-")
                {
                    useStdinFromDash = true;
                }
                else
                {
                    filteredArgs.Add(a);
                }
            }
            args = filteredArgs.ToArray();
        }

        string version = GetVersion();
        var parser = ConfigureParser(version);

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors)
        {
            // ShellKit returns its own usage-error code (suite-wide 125), but less documents
            // the POSIX-traditional 2 for usage errors. Emit messages, override exit code.
            result.WriteErrors(stderr);
            return 2;
        }

        // Collect less-specific CLI flags for LessOptions
        var lessFlags = new List<string>();
        if (result.Has("-N")) { lessFlags.Add("-N"); }
        if (result.Has("-S")) { lessFlags.Add("-S"); }
        if (result.Has("-F")) { lessFlags.Add("-F"); }
        if (result.Has("-R")) { lessFlags.Add("-R"); }
        if (result.Has("-X")) { lessFlags.Add("-X"); }
        if (result.Has("-i")) { lessFlags.Add("-i"); }
        if (result.Has("-I")) { lessFlags.Add("-I"); }

        // Positionals may contain +F, +G, +/pattern, or file path.
        // Tier-2 baseline 2026-05-07 finding F5: refuse multi-file with exit 2.
        string? filePath = null;
        foreach (string pos in result.Positionals)
        {
            if (pos == "+F")
            {
                lessFlags.Add("+F");
            }
            else if (pos == "+G")
            {
                lessFlags.Add("+G");
            }
            else if (pos.StartsWith("+/") && pos.Length > 2)
            {
                lessFlags.Add(pos);
            }
            else if (filePath is null)
            {
                filePath = pos;
            }
            else
            {
                stderr.WriteLine($"less: too many file arguments (expected at most one, got '{filePath}' and '{pos}'). Multi-file paging is not yet implemented; pipe through 'cat' for concatenated input.");
                return 2;
            }
        }

        // F2: NO_COLOR / --color / --no-color resolution → StripAnsi.
        bool useColor = result.ResolveColor();

        // Resolve options: CLI flags > LESS env > defaults.
        // F8 contract: null lessEnvVar = "use defaults"; "" = "all defaults disabled".
        var options = LessOptions.Resolve(lessFlags.ToArray(), lessEnvVar, stripAnsi: !useColor);

        // F7 catch broadening: FileNotFoundException, IOException, UnauthorizedAccessException.
        InputSource source;
        try
        {
            if (useStdinFromDash)
            {
                source = InputSource.FromStdin();
            }
            else if (filePath is not null)
            {
                source = InputSource.FromFile(filePath);
            }
            else if (isStdinRedirected)
            {
                source = InputSource.FromStdin();
            }
            else
            {
                stderr.WriteLine("less: missing filename (use \"less --help\" for help)");
                return 2;
            }
        }
        catch (FileNotFoundException ex)
        {
            // ex.Message here is our project-controlled "File not found: ..." string.
            stderr.WriteLine($"less: {ex.Message}");
            return 1;
        }
        catch (IOException ex)
        {
            // Covers our "Is a directory" (from F6) plus genuine read errors.
            stderr.WriteLine($"less: {ex.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            // F7 + InvariantGlobalization: don't pipe framework ex.Message; synthesise English.
            stderr.WriteLine($"less: cannot read {filePath ?? "(stdin)"} (access denied)");
            return 1;
        }

        return pagerRunner(options, source);
    }

    /// <summary>
    /// Default pager invocation: reattaches console input if needed, constructs a real
    /// <see cref="Pager"/>, and runs it. Tests substitute a fake via the
    /// <c>pagerRunner</c> parameter to avoid entering the interactive loop.
    /// </summary>
    private static int DefaultPagerRunner(LessOptions options, InputSource source)
    {
        // After reading piped content, reattach console input so ReadKey works.
        ConsoleInput.ReattachIfRedirected();

        var pager = new Pager(options);
        return pager.Run(source);
    }

    /// <summary>
    /// Builds the ShellKit <see cref="CommandLineParser"/> for less. Extracted so the
    /// CLI shape lives in one place; <see cref="Program"/> and tests share it.
    /// </summary>
    internal static CommandLineParser ConfigureParser(string version)
    {
        return new CommandLineParser("less", version)
            .Description("Display file contents one screen at a time with scrolling, search, and ANSI colour passthrough.")
            .StandardFlags()
            .Flag("-N", "Show line numbers")
            .Flag("-S", "Chop (truncate) long lines instead of wrapping")
            .Flag("-F", "Quit if content fits on one screen (default: on)")
            .Flag("-R", "Raw ANSI passthrough (default: on)")
            .Flag("-X", "Don't clear screen on exit (default: on)")
            .Flag("-i", "Case-insensitive search (smart case: sensitive if pattern has uppercase)")
            .Flag("-I", "Force case-insensitive search")
            .Positional("[file]")
            .Platform("cross-platform",
                new[] { "less", "more" },
                "Windows has no native pager — more.com is forward-only and destroys ANSI",
                "Native ANSI passthrough, search, follow mode, modern defaults")
            .StdinDescription("Content to page (when no file argument)")
            .StdoutDescription("Paged content (with -F for short content, or when piped)")
            .StderrDescription("Errors")
            .Example("git diff | less", "Page coloured diff output")
            .Example("less logfile.txt", "View a file with scrolling and search")
            .Example("less -N logfile.txt", "View with line numbers")
            .Example("less -S wide.csv", "View wide file with horizontal scrolling")
            .Example("less +F logfile.txt", "Follow file for new content (like tail -f)")
            .Example("less +/error logfile.txt", "Open file and jump to first 'error'")
            .ExitCodes(
                (0, "Success"),
                (1, "Error (file not found, read error, permission denied)"),
                (2, "Usage error (POSIX-traditional; deliberate suite divergence since less replaces a POSIX tool)"));
    }

    /// <summary>
    /// Returns the informational version from the Winix.Less library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(Pager).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
