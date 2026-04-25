using System.Reflection;
using Winix.Wargs;
using Yort.ShellKit;

namespace Wargs;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        string version = GetVersion();

        try
        {
            return await RunAsync(args, version).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User-initiated Ctrl+C — POSIX convention is exit 128+SIGINT(2)=130. Treating
            // this as "unexpected error" with exit 126 (NotExecutable) was misleading both in
            // diagnostic ("unexpected" implies a wargs bug) and exit code (126 implies the
            // CHILD wasn't executable). The OCE arm must come BEFORE the broad catch since
            // OperationCanceledException is an Exception.
            return 130;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Final safety net. Without this, an unexpected exception (e.g. IOException on
            // stdin read, an uncaught InvalidOperationException) escapes Main with the CLR's
            // unhandled-exception handler firing. Combined with <StackTraceSupport>false</...>
            // in wargs.csproj that produces a near-blank "Unhandled exception" message —
            // unactionable. Mirrors the pattern in retry/Program.cs.
            Exception surface = UnwrapTypeInit(ex);
            string msg = string.IsNullOrEmpty(surface.Message)
                ? $"wargs: unexpected error: {surface.GetType().Name}"
                : $"wargs: unexpected error: {surface.GetType().Name}: {surface.Message}";
            SafeWriteLine(Console.Error, msg);
            return ExitCode.NotExecutable;
        }
    }

    private static async Task<int> RunAsync(string[] args, string version)
    {

        var parser = new CommandLineParser("wargs", version)
            .Description("Read items from stdin and execute a command for each one.")
            .StandardFlags()
            .Flag("--ndjson", "Streaming NDJSON per job to stderr")
            .IntOption("--parallel", "-P", "N", "Max concurrent jobs (default 1, 0 = unlimited)",
                n => n < 0 ? "must be >= 0" : null)
            .IntOption("--batch", "-n", "N", "Items per invocation (default 1)",
                n => n < 1 ? "must be >= 1" : null)
            .Flag("--null", "-0", "Null-delimited input")
            .Option("--delimiter", "-d", "CHAR", "Custom input delimiter")
            .Flag("--compat", "POSIX whitespace splitting with quote handling")
            .Flag("--fail-fast", "Stop spawning after first failure")
            .Flag("--keep-order", "-k", "Print output in input order")
            .Flag("--line-buffered", "Children inherit stdio directly")
            .Flag("--confirm", "-p", "Prompt before each job")
            .Flag("--dry-run", "Print commands without executing")
            .Flag("--verbose", "-v", "Print each command to stderr before running")
            .Flag("--no-shell-fallback", "Disable shell fallback for builtins (cmd /c, sh -c)")
            .CommandMode()
            .ExitCodes(
                (0, "All jobs succeeded"),
                (WargsExitCode.ChildFailed, "One or more child processes failed"),
                (WargsExitCode.FailFastAbort, "Aborted due to --fail-fast"),
                (ExitCode.UsageError, "Usage error"),
                (ExitCode.NotExecutable, "Command not executable"),
                (ExitCode.NotFound, "Command not found"))
            .Platform("cross-platform",
                replaces: new[] { "xargs" },
                valueOnWindows: "No native xargs; Git Bash xargs has path-mangling issues with Windows paths",
                valueOnUnix: "Sane line-delimited default instead of whitespace splitting")
            .StdinDescription("Items to process, one per line (default). Null-delimited with -0. Whitespace with --compat.")
            .StdoutDescription("Child process stdout (buffered per job by default)")
            .StderrDescription("Failure summary. JSON with --json. NDJSON per job with --ndjson.")
            .Example("files . --ext log | wargs rm", "Delete all log files")
            .Example("git diff --name-only | wargs dotnet format", "Format changed files")
            .Example("files . --ext cs | wargs -P4 dotnet format", "Parallel format")
            .Example("echo 'one\\ntwo\\nthree' | wargs echo", "Basic usage")
            .ComposesWith("files", "files ... | wargs <command>", "Find then execute (find | xargs pattern)")
            .ComposesWith("squeeze", "files . --ext csv | wargs squeeze --zstd", "Batch compress")
            .JsonField("tool", "string", "Tool name (\"wargs\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (0 = success)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("total_jobs", "int", "Total number of jobs")
            .JsonField("succeeded", "int", "Jobs that exited 0")
            .JsonField("failed", "int", "Jobs that exited non-zero")
            .JsonField("skipped", "int", "Jobs skipped (fail-fast)")
            .JsonField("wall_seconds", "float", "Total wall time in seconds");

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // --- Resolve options ---
        int parallelism = result.Has("--parallel") ? result.GetInt("--parallel") : 1;
        int batchSize = result.Has("--batch") ? result.GetInt("--batch") : 1;
        bool jsonOutput = result.Has("--json");
        bool ndjsonOutput = result.Has("--ndjson");
        bool verbose = result.Has("--verbose");
        bool dryRun = result.Has("--dry-run");
        bool failFast = result.Has("--fail-fast");
        bool confirm = result.Has("--confirm");
        bool keepOrder = result.Has("--keep-order");
        bool lineBuffered = result.Has("--line-buffered");
        bool noShellFallback = result.Has("--no-shell-fallback");

        // --- Resolve delimiter mode ---
        bool hasNull = result.Has("--null");
        bool hasDelimiter = result.Has("--delimiter");
        bool hasCompat = result.Has("--compat");

        int delimiterCount = (hasNull ? 1 : 0) + (hasDelimiter ? 1 : 0) + (hasCompat ? 1 : 0);
        if (delimiterCount > 1)
        {
            return UsageError(result, "--null, --delimiter, and --compat are mutually exclusive", jsonOutput, version);
        }

        DelimiterMode delimMode = DelimiterMode.Line;
        char customDelimiter = '\0';
        if (hasNull)
        {
            delimMode = DelimiterMode.Null;
        }
        else if (hasCompat)
        {
            delimMode = DelimiterMode.Whitespace;
        }
        else if (hasDelimiter)
        {
            string delimStr = result.GetString("--delimiter");
            if (delimStr.Length != 1)
            {
                return UsageError(result, "--delimiter requires a single character", jsonOutput, version);
            }
            delimMode = DelimiterMode.Custom;
            customDelimiter = delimStr[0];
        }

        // --- Validate flag combinations ---
        if (confirm && parallelism != 1)
        {
            return UsageError(result, "--confirm cannot be used with parallel execution", jsonOutput, version);
        }

        if (lineBuffered && keepOrder)
        {
            return UsageError(result, "--line-buffered and --keep-order cannot be combined", jsonOutput, version);
        }

        if (lineBuffered && parallelism != 1)
        {
            return UsageError(result, "--line-buffered cannot be used with parallel execution (output would interleave)", jsonOutput, version);
        }

        if (ndjsonOutput && lineBuffered)
        {
            return UsageError(result, "--ndjson and --line-buffered cannot be combined", jsonOutput, version);
        }

        // --- Resolve buffer strategy ---
        BufferStrategy strategy = BufferStrategy.JobBuffered;
        if (lineBuffered)
        {
            strategy = BufferStrategy.LineBuffered;
        }
        else if (keepOrder)
        {
            strategy = BufferStrategy.KeepOrder;
        }

        // --- Build pipeline ---
        var inputReader = new InputReader(Console.In, delimMode, customDelimiter);
        var commandBuilder = new CommandBuilder(result.Command, batchSize);
        var runnerOptions = new JobRunnerOptions(
            Parallelism: parallelism,
            Strategy: strategy,
            FailFast: failFast,
            DryRun: dryRun,
            Verbose: verbose,
            Confirm: confirm,
            ShellFallback: !noShellFallback);
        var jobRunner = new JobRunner(runnerOptions);

        // JobRunner.RunAsync takes IReadOnlyList<CommandInvocation>, so materialise the pipeline.
        // Materialisation does the actual stdin reads (ReadItems is a streaming enumerable).
        // A broken stdin pipe or encoding fault here would otherwise escape to the top-level
        // catch as "unexpected error" — which bypasses the --json/--ndjson contract that
        // promises an envelope on every exit path. The catch matches Main's outer-catch
        // breadth (excluding OOM, SOE, and OCE which have their own handling) so any
        // realistic input-side failure is classified as input_read_failed.
        List<CommandInvocation> invocations;
        try
        {
            IEnumerable<string> items = inputReader.ReadItems();
            invocations = commandBuilder.Build(items).ToList();
        }
        catch (Exception ex) when (
            ex is not OutOfMemoryException
            and not StackOverflowException
            and not OperationCanceledException)
        {
            if (jsonOutput || ndjsonOutput)
            {
                SafeWriteLine(Console.Error,
                    Formatting.FormatJsonError(ExitCode.NotExecutable, "input_read_failed", "wargs", version));
            }
            SafeWriteLine(Console.Error, $"wargs: failed to read input: {ex.GetType().Name}: {ex.Message}");
            return ExitCode.NotExecutable;
        }

        // Empty-input diagnostic: previously `findfiles | wargs rm` with zero matches produced
        // a silent exit 0, indistinguishable from "all jobs succeeded". Each output mode gets
        // a positive signal — round 1 covered --json + human; --ndjson previously emitted
        // nothing (silent-failure class L). Now NDJSON gets a single no_input envelope line,
        // matching its "one JSON object per outcome" contract.
        if (invocations.Count == 0 && !dryRun)
        {
            if (jsonOutput)
            {
                SafeWriteLine(Console.Error, Formatting.FormatJson(
                    new WargsResult(0, 0, 0, 0, TimeSpan.Zero, new List<JobResult>()),
                    exitCode: 0,
                    exitReason: "no_input",
                    "wargs",
                    version));
            }
            else if (ndjsonOutput)
            {
                SafeWriteLine(Console.Error,
                    Formatting.FormatJsonError(0, "no_input", "wargs", version));
            }
            else
            {
                SafeWriteLine(Console.Error, "wargs: no input items, nothing to do");
            }
            return 0;
        }

        // --- Execute ---
        // Wire Ctrl+C to a CancellationToken so the kill-on-cancel registrations in
        // JobRunner activate and cleanly terminate child process trees.
        // Named delegate so we can unregister before the CTS is disposed.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;  // Prevent immediate process termination — let RunAsync clean up
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* raced with shutdown — safe to drop */ }
        };
        Console.CancelKeyPress += cancelHandler;

        WargsResult wargsResult;
        try
        {
            wargsResult = await jobRunner.RunAsync(
                invocations, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            // Unregister before 'using' disposes cts, so a late Ctrl+C
            // doesn't call Cancel() on a disposed CancellationTokenSource.
            Console.CancelKeyPress -= cancelHandler;
        }

        // --- Determine exit code ---
        if (dryRun)
        {
            return 0;
        }

        int exitCode = 0;
        string exitReason = "success";

        if (wargsResult.Failed > 0)
        {
            exitCode = WargsExitCode.ChildFailed;
            exitReason = "child_failed";

            if (failFast && wargsResult.Skipped > 0)
            {
                exitCode = WargsExitCode.FailFastAbort;
                exitReason = "fail_fast_abort";
            }
        }

        // --- NDJSON: emit per-job lines to stderr ---
        // Skipped jobs are intentionally omitted from streaming output to match the
        // "one line per job actually run" contract; the JSON summary's `skipped` field is the
        // single source of truth for skip count. Callers correlating job count with NDJSON
        // line count must add `skipped` to the line count.
        if (ndjsonOutput)
        {
            foreach (JobResult job in wargsResult.Jobs)
            {
                if (!job.Skipped)
                {
                    int jobExitCode = job.ChildExitCode == 0 ? 0 : WargsExitCode.ChildFailed;
                    string jobExitReason = job.ChildExitCode == 0 ? "success" : "child_failed";
                    SafeWriteLine(Console.Error,
                        Formatting.FormatNdjsonLine(job, jobExitCode, jobExitReason, "wargs", version));
                }
            }
        }

        // --- JSON: summary to stderr ---
        if (jsonOutput)
        {
            SafeWriteLine(Console.Error,
                Formatting.FormatJson(wargsResult, exitCode, exitReason, "wargs", version));
        }

        // --- Human: surface fault diagnostics + failure summary ---
        if (!jsonOutput && !ndjsonOutput)
        {
            // Each fault that wasn't a normal child non-zero exit (FaultMessage was set)
            // gets a stderr line. Without this, a spawn-failure or task-body fault becomes
            // a bare "wargs: N/M jobs failed" with no clue why — silent-failure class B.
            foreach (JobResult job in wargsResult.Jobs)
            {
                if (job.FaultMessage is not null)
                {
                    SafeWriteLine(Console.Error, $"wargs: job {job.JobIndex}: {job.FaultMessage}");
                }
            }

            string? summary = Formatting.FormatHumanSummary(wargsResult);
            if (summary is not null)
            {
                SafeWriteLine(Console.Error, summary);
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Routes a usage error to either stderr (text) or a JSON envelope, depending on
    /// whether <c>--json</c> was set. Replaces the call sites that previously emitted
    /// plain text only, leaving JSON-mode tooling without a parseable error.
    /// </summary>
    private static int UsageError(ParseResult result, string message, bool jsonOutput, string version)
    {
        if (jsonOutput)
        {
            SafeWriteLine(Console.Error,
                Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "wargs", version));
            // Still emit the plain-text reason so a human reading mixed output sees the cause.
            SafeWriteLine(Console.Error, $"wargs: {message}");
            return ExitCode.UsageError;
        }
        return result.WriteError(message, Console.Error);
    }

    /// <summary>
    /// Best-effort write to <paramref name="writer"/>. Broken pipe, closed stream, or disposed
    /// writer must not convert a clean exit code into a CLR crash. Matches the suite-wide
    /// SafeWriteLine convention (see retry/Program.cs and envvault/Cli.cs for precedents).
    /// </summary>
    private static void SafeWriteLine(TextWriter writer, string message)
    {
        try { writer.WriteLine(message); }
        catch (IOException) { /* downstream pipe closed */ }
        catch (ObjectDisposedException) { /* writer already disposed */ }
    }

    /// <summary>
    /// Peels TypeInitializationException wrappers to reveal the actionable inner exception.
    /// The wrapper's Message is "The type initializer for X threw an exception." — useless to
    /// the user. Same pattern as retry/Program.cs and envvault's Cli.UnwrapTypeInit.
    /// </summary>
    private static Exception UnwrapTypeInit(Exception ex)
    {
        Exception current = ex;
        for (int depth = 0; depth < 32 && current is TypeInitializationException tie && tie.InnerException != null; depth++)
        {
            current = tie.InnerException;
        }
        return current;
    }

    private static string GetVersion()
    {
        return typeof(WargsExitCode).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
