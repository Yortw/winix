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

        // Pre-scan args for the structured-output flags so Main's outer catches can honour
        // the "envelope on every exit path" contract. The full parse happens later in
        // RunAsync; this is a cheap forward-look for an unambiguous flag name (no value,
        // no short alias). False-positive risk: a user passing literal "--json" as a child
        // command argument after a `--` separator. Acceptable — the resulting envelope
        // emission is harmless on stderr.
        bool jsonOutput = ContainsFlag(args, "--json");
        bool ndjsonOutput = ContainsFlag(args, "--ndjson");

        // Register Console.CancelKeyPress AT MAIN SCOPE, BEFORE RunAsync — including before
        // input materialisation. Round-4 wrongly assumed an in-RunAsync handler caught
        // Ctrl+C-during-stdin-read; in fact .NET's default Ctrl+C handling tears the
        // process down before any handler installed later in the call stack can fire.
        // With this early registration, e.Cancel=true keeps the process alive long enough
        // for InputReader to observe the cancellation (Console.In.ReadLine returns null
        // after Ctrl+C with e.Cancel=true) and for Main's OCE catch to emit the cancelled
        // envelope.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* raced with shutdown — safe to drop */ }
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            return await RunAsync(args, version, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User-initiated Ctrl+C — POSIX convention is exit 128+SIGINT(2)=130. Under
            // structured-output modes, emit a cancelled envelope before exiting so consumers
            // parsing stderr always see a JSON object on cancel. Without this, Ctrl+C on
            // `wargs --json ...` exited 130 with no stderr payload, indistinguishable from
            // a SIGKILL or runtime crash for tooling.
            if (jsonOutput || ndjsonOutput)
            {
                SafeWriteLine(Console.Error,
                    Formatting.FormatJsonError(130, "cancelled", "wargs", version));
            }
            return 130;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Final safety net. Without this, an unexpected exception (e.g. IOException on
            // stdin read, an uncaught InvalidOperationException) escapes Main with the CLR's
            // unhandled-exception handler firing. Combined with <StackTraceSupport>false</...>
            // in wargs.csproj that produces a near-blank "Unhandled exception" message —
            // unactionable. Mirrors the pattern in retry/Program.cs.
            //
            // Mode-discriminated: under structured-output modes emit ONLY the envelope so the
            // stream stays parseable as one JSON object per line. Mixing the plaintext
            // diagnostic alongside the envelope (the round-4 implementation) broke strict
            // NDJSON parsers — same defect class as the round-4 input-pipeline catch fix that
            // was missed here.
            Exception surface = UnwrapTypeInit(ex);
            if (jsonOutput || ndjsonOutput)
            {
                SafeWriteLine(Console.Error,
                    Formatting.FormatJsonError(ExitCode.NotExecutable, "unexpected_error", "wargs", version));
            }
            else
            {
                string msg = string.IsNullOrEmpty(surface.Message)
                    ? $"wargs: unexpected error: {surface.GetType().Name}"
                    : $"wargs: unexpected error: {surface.GetType().Name}: {surface.Message}";
                SafeWriteLine(Console.Error, msg);
            }
            return ExitCode.NotExecutable;
        }
        finally
        {
            // Unregister before 'using' disposes cts, so a late Ctrl+C
            // doesn't call Cancel() on a disposed CancellationTokenSource.
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// Pre-scans <paramref name="args"/> for an exact flag match, ignoring `--` separator
    /// boundaries (we deliberately scan the whole array because `--` parsing happens later).
    /// Used by Main to detect structured-output modes before parser construction so the
    /// outer catches can emit JSON envelopes on cancellation / unexpected errors.
    /// </summary>
    private static bool ContainsFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == flag) { return true; }
        }
        return false;
    }

    private static async Task<int> RunAsync(string[] args, string version, CancellationToken cancellationToken)
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
        if (result.HasErrors)
        {
            // Mode discrimination on the parser-error path:
            //  - --ndjson: emit ONLY wargs's envelope (strict NDJSON line discipline). ShellKit's
            //    WriteErrors emits a multi-line envelope which would break parsers; suppress it.
            //  - --json: defer to ShellKit's WriteErrors. ShellKit's --json mode emits a richer
            //    envelope including the `errors[]` array — strictly more useful than wargs's
            //    bare usage_error envelope. Emitting our own AS WELL (round 5) produced a
            //    double envelope on stderr (round-6 CR/SFH I1).
            //  - Human mode: ShellKit's plaintext error formatting.
            bool ndjsonModeHere = ContainsFlag(args, "--ndjson");
            if (ndjsonModeHere)
            {
                SafeWriteLine(Console.Error,
                    Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "wargs", version));
                return ExitCode.UsageError;
            }
            return result.WriteErrors(Console.Error);
        }

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
            return UsageError(result, "--null, --delimiter, and --compat are mutually exclusive", jsonOutput, ndjsonOutput, version);
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
                return UsageError(result, "--delimiter requires a single character", jsonOutput, ndjsonOutput, version);
            }
            delimMode = DelimiterMode.Custom;
            customDelimiter = delimStr[0];
        }

        // --- Validate flag combinations ---
        if (confirm && parallelism != 1)
        {
            return UsageError(result, "--confirm cannot be used with parallel execution", jsonOutput, ndjsonOutput, version);
        }

        if (lineBuffered && keepOrder)
        {
            return UsageError(result, "--line-buffered and --keep-order cannot be combined", jsonOutput, ndjsonOutput, version);
        }

        if (lineBuffered && parallelism != 1)
        {
            return UsageError(result, "--line-buffered cannot be used with parallel execution (output would interleave)", jsonOutput, ndjsonOutput, version);
        }

        if (ndjsonOutput && lineBuffered)
        {
            return UsageError(result, "--ndjson and --line-buffered cannot be combined", jsonOutput, ndjsonOutput, version);
        }

        if ((jsonOutput || ndjsonOutput) && verbose)
        {
            // Verbose writes raw "wargs: <command>" lines to stderr per invocation;
            // structured-output modes also use stderr for envelopes/streaming JSON. Mixing
            // the two breaks line-discipline parsers (the user piping `wargs --ndjson -v ... | jq`
            // hit interleaved plaintext among NDJSON lines). Same root reason as the
            // --ndjson/--line-buffered rejection above.
            return UsageError(result, "--verbose cannot be combined with --json or --ndjson (would interleave plaintext with structured output)", jsonOutput, ndjsonOutput, version);
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
            // Mode discrimination: under --json/--ndjson, emit ONLY the envelope so the
            // stream stays parseable as one JSON object per line. Mixing the plaintext
            // diagnostic on the same stream broke strict NDJSON parsers (round-4 SFH).
            if (jsonOutput || ndjsonOutput)
            {
                SafeWriteLine(Console.Error,
                    Formatting.FormatJsonError(ExitCode.NotExecutable, "input_read_failed", "wargs", version));
            }
            else
            {
                SafeWriteLine(Console.Error, $"wargs: failed to read input: {ex.GetType().Name}: {ex.Message}");
            }
            return ExitCode.NotExecutable;
        }

        // Round-6 SFH I2: if Ctrl+C fired during stdin materialisation, .NET's CancelKeyPress
        // handler set e.Cancel=true and Console.In.ReadLine() returned null (EOF). The read
        // loop yield-broke and we landed here with invocations.Count==0 AND the cancel token
        // signalled. Without this check the empty-input branch below would classify the run
        // as exit 0 / no_input — silently misclassifying a user-initiated cancel as a
        // benign empty-input case. Throwing OCE here lets Main's OCE catch produce the
        // documented exit 130 / cancelled envelope.
        cancellationToken.ThrowIfCancellationRequested();

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
        // The CancellationTokenSource and Console.CancelKeyPress handler are owned by Main
        // (registered before input materialisation, see Main for rationale). RunAsync just
        // observes the token. Kill-on-cancel registrations inside JobRunner cleanly terminate
        // child process trees when the user hits Ctrl+C.
        WargsResult wargsResult = await jobRunner.RunAsync(
            invocations, Console.Out, Console.Error, cancellationToken).ConfigureAwait(false);

        // --- Determine exit code ---
        if (dryRun)
        {
            // Under structured-output modes, emit a dry_run envelope so consumers always
            // see a JSON object even on the dry-run preview path. Without this, --json or
            // --ndjson with --dry-run produced zero stderr — indistinguishable from a crash
            // for tooling. The envelope reports the would-be invocation count via total_jobs
            // so callers can tell whether anything would have run.
            if (jsonOutput)
            {
                SafeWriteLine(Console.Error, Formatting.FormatJson(
                    new WargsResult(invocations.Count, 0, 0, 0, TimeSpan.Zero, new List<JobResult>()),
                    exitCode: 0,
                    exitReason: "dry_run",
                    "wargs",
                    version));
            }
            else if (ndjsonOutput)
            {
                SafeWriteLine(Console.Error,
                    Formatting.FormatJsonError(0, "dry_run", "wargs", version));
            }
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
    /// Routes a usage error to either plaintext stderr or a JSON envelope, depending on
    /// the active output mode. Mode discrimination:
    /// <para>- <c>--ndjson</c>: ONLY the envelope (strict NDJSON contract — no trailing plaintext).</para>
    /// <para>- <c>--json</c>: envelope first, then a plaintext reason (mixed output is acceptable
    /// for --json since it's a single envelope, not a stream — humans reading stderr still see
    /// the cause).</para>
    /// <para>- Neither: ShellKit's standard plaintext error format.</para>
    /// </summary>
    private static int UsageError(ParseResult result, string message, bool jsonOutput, bool ndjsonOutput, string version)
    {
        if (ndjsonOutput)
        {
            // Strict NDJSON: envelope only, no trailing plaintext that would break parsers.
            SafeWriteLine(Console.Error,
                Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "wargs", version));
            return ExitCode.UsageError;
        }
        if (jsonOutput)
        {
            SafeWriteLine(Console.Error,
                Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "wargs", version));
            // Plain-text reason after the single envelope so a human reading mixed output sees the cause.
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
