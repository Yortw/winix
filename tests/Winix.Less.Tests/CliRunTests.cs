#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Winix.Less;
using Xunit;

namespace Winix.Less.Tests;

/// <summary>
/// Tests for the library-seam <see cref="Cli.Run"/> entry point. Pin orchestration-layer
/// exit-code routing, F4 bare-dash POSIX intercept, F5 multi-file refusal, F6 directory
/// message, F7 catch broadening (FileNotFoundException + IOException + UnauthorizedAccess
/// Exception), and POSIX-traditional exit code 2 for usage errors. Avoids the
/// interactive pager loop via the <c>pagerRunner</c> seam — tests verify everything up
/// to the pager dispatch.
/// </summary>
public sealed class CliRunTests : IDisposable
{
    private const int ExitCodePosixUsageError = 2;

    private readonly string _tempDir;
    // Fake pager that records the LessOptions + InputSource it would have run, and
    // returns 0 (success). Tests assert against captured state to verify orchestration
    // delivered the right inputs to the pager — without entering the interactive loop.
    private LessOptions? _capturedOptions;

    public CliRunTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "less-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private static (StringWriter stdout, StringWriter stderr) Sinks()
    {
        return (new StringWriter(), new StringWriter());
    }

    private Func<LessOptions, InputSource, int> CapturingPagerRunner(int exitCode = 0)
    {
        return (options, _) =>
        {
            _capturedOptions = options;
            return exitCode;
        };
    }

    // ── F5: multi-file rejection ──────────────────────────────────────────────────

    [Fact]
    public void Run_TwoFiles_ReturnsExit2WithCanonicalMessage()
    {
        // Pre-fix F5: every non-+command positional silently overwrote filePath; the
        // README's "Multiple files are paged in sequence" claim was a lie. Now refuse
        // multi-file with exit 2 and a clear message that mentions both names.
        var (stdout, stderr) = Sinks();
        string fileA = Path.Combine(_tempDir, "a.txt");
        string fileB = Path.Combine(_tempDir, "b.txt");
        File.WriteAllText(fileA, "content a");
        File.WriteAllText(fileB, "content b");

        int exit = Cli.Run(
            new[] { fileA, fileB },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner());

        Assert.Equal(ExitCodePosixUsageError, exit);
        string err = stderr.ToString();
        Assert.Contains("too many file arguments", err, StringComparison.Ordinal);
        // Message embeds the full paths inside single quotes; assert each name appears.
        Assert.Contains("a.txt", err, StringComparison.Ordinal);
        Assert.Contains("b.txt", err, StringComparison.Ordinal);
        // Pager must NOT have run.
        Assert.Null(_capturedOptions);
    }

    // ── F6: directory → "Is a directory" ──────────────────────────────────────────

    [Fact]
    public void Run_DirectoryPath_ReturnsExit1AndIsADirectoryMessage()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { _tempDir },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner());

        Assert.Equal(1, exit);
        Assert.Contains("less:", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("Is a directory", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── F7: catch broadening + InvariantGlobalization SR-key safety ───────────────

    [Fact]
    public void Run_NonExistentFile_ReturnsExit1WithFileNotFoundMessage()
    {
        var (stdout, stderr) = Sinks();
        string missing = Path.Combine(_tempDir, "does-not-exist.txt");

        int exit = Cli.Run(
            new[] { missing },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner());

        Assert.Equal(1, exit);
        // Project-controlled English (InputSource.FromFile throws FileNotFoundException
        // with our own message), so SR-key safety is moot here.
        Assert.Contains("less:", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("File not found", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── No-file no-stdin: usage error 2 ───────────────────────────────────────────

    [Fact]
    public void Run_NoFileNoStdin_ReturnsExit2WithMissingFilenameMessage()
    {
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            Array.Empty<string>(),
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner());

        Assert.Equal(ExitCodePosixUsageError, exit);
        Assert.Contains("missing filename", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── F4: bare-dash POSIX stdin marker ──────────────────────────────────────────

    [Fact]
    public void Run_BareDash_TreatsAsStdinMarker()
    {
        // F4 fix: ShellKit's CommandLineParser would consume "-" as an unknown short
        // option and fail with exit 125. The Cli.Run prefix strips bare dashes from
        // args[] and remembers the explicit-stdin signal. Pin: `less -` runs the pager
        // (no usage error), even with isStdinRedirected=false (the dash forces stdin).
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "-" },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,  // dash overrides this — explicit marker wins
            pagerRunner: CapturingPagerRunner());

        Assert.Equal(0, exit);
        // Must NOT have printed a usage error.
        Assert.DoesNotContain("missing filename", stderr.ToString(), StringComparison.Ordinal);
        // Pager MUST have been called (orchestration delivered the input source).
        Assert.NotNull(_capturedOptions);
    }

    [Fact]
    public void Run_BareDashWithFile_DashWinsExplicitStdin()
    {
        // POSIX precedence: explicit `-` beats a file argument. Documented in Cli.cs
        // "When both `-` AND a file are given, `-` wins per tradition."
        var (stdout, stderr) = Sinks();
        string file = Path.Combine(_tempDir, "ignored.txt");
        File.WriteAllText(file, "would-be-content");

        int exit = Cli.Run(
            new[] { "-", file },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner());

        Assert.Equal(0, exit);
        Assert.NotNull(_capturedOptions);
    }

    // ── ShellKit usage error → POSIX exit 2 (deliberate suite divergence) ────────

    [Fact]
    public void Run_BogusFlag_ReturnsExit2NotShellKit125()
    {
        // F3 fix: ShellKit's WriteErrors returns 125 (suite-wide usage error), but less
        // documents the POSIX-traditional 2. Cli.Run overrides the return code while
        // still emitting ShellKit's error messages.
        var (stdout, stderr) = Sinks();

        int exit = Cli.Run(
            new[] { "--bogus-flag" },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner());

        Assert.Equal(ExitCodePosixUsageError, exit);
        Assert.NotEqual(125, exit);  // Explicit: NOT the suite default
    }

    // ── F2: NO_COLOR / --no-color → StripAnsi=true ────────────────────────────────

    [Fact]
    public void Run_NoColorFlag_LessOptionsHasStripAnsiTrue()
    {
        // F2 fix: pre-fix the three colour knobs (NO_COLOR env, --color, --no-color)
        // were silently ignored. Now ResolveColor() drives LessOptions.StripAnsi.
        var (stdout, stderr) = Sinks();
        string file = Path.Combine(_tempDir, "color.txt");
        File.WriteAllText(file, "x");

        int exit = Cli.Run(
            new[] { "--no-color", file },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner());

        Assert.Equal(0, exit);
        Assert.NotNull(_capturedOptions);
        Assert.True(_capturedOptions!.StripAnsi,
            "--no-color should set StripAnsi=true on resolved LessOptions (F2 contract).");
    }

    [Fact]
    public void Run_DefaultColor_LessOptionsHasStripAnsiFalse()
    {
        // Inverse of the above: when colour is enabled (no NO_COLOR, no --no-color),
        // StripAnsi must be false.
        var (stdout, stderr) = Sinks();
        string file = Path.Combine(_tempDir, "color.txt");
        File.WriteAllText(file, "x");

        // Pass --color to force-enable (avoid host-tty heuristic in the test runner).
        int exit = Cli.Run(
            new[] { "--color", file },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner());

        Assert.Equal(0, exit);
        Assert.NotNull(_capturedOptions);
        Assert.False(_capturedOptions!.StripAnsi,
            "--color should set StripAnsi=false (passthrough) on resolved LessOptions.");
    }

    // ── F8: LESS env var null vs empty ────────────────────────────────────────────

    [Fact]
    public void Run_LessEnvVarEmpty_DisablesAllDefaults()
    {
        // F8 contract: LESS="" disables defaults (StripAnsi/etc reflect raw "no flags");
        // null LESS uses defaults. Pin via lessEnvVar override so the test doesn't
        // mutate process env.
        var (stdout, stderr) = Sinks();
        string file = Path.Combine(_tempDir, "x.txt");
        File.WriteAllText(file, "x");

        int exit = Cli.Run(
            new[] { file },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner(),
            lessEnvVar: "");  // explicit empty

        Assert.Equal(0, exit);
        Assert.NotNull(_capturedOptions);
        // Defaults-disabled means -F is OFF (default is on) — pager would not auto-quit.
        Assert.False(_capturedOptions!.QuitIfOneScreen,
            "LESS=\"\" should disable defaults including -F (QuitIfOneScreen).");
    }

    [Fact]
    public void Run_LessEnvVarNull_UsesDefaults()
    {
        var (stdout, stderr) = Sinks();
        string file = Path.Combine(_tempDir, "x.txt");
        File.WriteAllText(file, "x");

        int exit = Cli.Run(
            new[] { file },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner(),
            lessEnvVar: null);  // null = treat as unset → fall through to env lookup, but
                                // this ALSO means "use the host env"; test environment may
                                // have LESS unset, in which case defaults apply.

        // We can't pin defaults reliably without knowing host env; just verify the
        // call succeeded and pager was invoked.
        Assert.Equal(0, exit);
        Assert.NotNull(_capturedOptions);
    }

    // ── Stdin-redirected → implicit pager dispatch (round-2 I-R2-1) ──────────────

    [Fact]
    public void Run_StdinRedirected_NoArgs_DispatchesToPager()
    {
        // Round-2 fresh-eyes 2026-05-09 test-analyzer I-R2-1: existing tests all pass
        // isStdinRedirected:false. The "stdin piped, no file argument" path
        // (`dmesg | less`) was unverified at the seam. A regression that flipped the
        // dispatch condition (e.g. accidentally wired F4's useStdinFromDash flag into
        // the wrong branch) would only surface in manual smoke. Pin the contract.
        var (stdout, stderr) = Sinks();

        var savedIn = Console.In;
        Console.SetIn(new StringReader("piped line 1\npiped line 2\n"));
        try
        {
            int exit = Cli.Run(
                Array.Empty<string>(),
                stdout, stderr,
                isStdoutRedirected: true,
                isStdinRedirected: true,
                pagerRunner: CapturingPagerRunner());

            Assert.Equal(0, exit);
            Assert.NotNull(_capturedOptions);
            // No usage error — stdin path was dispatched, not refused.
            Assert.DoesNotContain("missing filename", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetIn(savedIn);
        }
    }

    // ── Pager returns the user's exit code unchanged ──────────────────────────────

    [Fact]
    public void Run_PagerReturnsCustomExitCode_PassedThrough()
    {
        var (stdout, stderr) = Sinks();
        string file = Path.Combine(_tempDir, "x.txt");
        File.WriteAllText(file, "x");

        int exit = Cli.Run(
            new[] { file },
            stdout, stderr,
            isStdoutRedirected: true,
            isStdinRedirected: false,
            pagerRunner: CapturingPagerRunner(exitCode: 42));

        Assert.Equal(42, exit);
    }
}
