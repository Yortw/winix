using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Yort.ShellKit;

namespace Winix.Wargs;

/// <summary>
/// Library entry point for the wargs tool: parses arguments, validates mode combinations,
/// reads items from the supplied reader, runs the job pipeline, and routes all output —
/// including the envelope-on-every-exit-path cancellation/error catches — through the
/// supplied writers. <c>Program.Main</c> is a thin shell owning console setup, Ctrl+C
/// registration, and the real-console stdin-unblock-on-cancel hook.
/// </summary>
/// <remarks>
/// Seam limit (by design — do not "fix"): the <c>Console.In.Close()</c>-on-cancel
/// registration lives in Main because it mutates the REAL console's stdin to unblock a
/// pending read (round-7 Linux fix; round-8 Windows caveat). The seam's <paramref
/// name="stdin"/> is an injected reader in tests; the library never touches Console.In.
/// That path stays covered by the Linux SkippableFact binary test and the smoke fixture.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the wargs CLI.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="stdin">Source of input items (one per line by default; <c>-0</c>,
    /// <c>--delimiter</c>, <c>--compat</c> alter splitting). Production passes
    /// <c>Console.In</c>; tests pass a <see cref="StringReader"/>.</param>
    /// <param name="stdout">Receives child process stdout (buffered per job by default).</param>
    /// <param name="stderr">Receives the failure summary, JSON envelope (<c>--json</c>),
    /// or streaming NDJSON lines (<c>--ndjson</c>) — every exit path emits an envelope
    /// under structured-output modes, including cancellation and unexpected errors.</param>
    /// <param name="cancellationToken">Cancellation signal (Ctrl+C in production, owned by
    /// Main). Observed between/during jobs (kill-on-cancel) and during input materialisation.</param>
    /// <returns>0 success; 123 child_failed; 124 fail_fast_abort; 125 usage; 126
    /// input_read_failed/unexpected; 130 cancelled.</returns>
    public static async Task<int> RunAsync(string[] args, TextReader stdin,
        TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        string version = GetVersion();

        // Pre-scan args for the structured-output flags so the outer catches can honour
        // the "envelope on every exit path" contract. The full parse happens later in
        // RunCoreAsync; this is a cheap forward-look for an unambiguous flag name (no value,
        // no short alias). False-positive risk: a user passing literal "--json" as a child
        // command argument after a `--` separator. Acceptable — the resulting envelope
        // emission is harmless on stderr.
        bool jsonOutput = ContainsFlag(args, "--json");
        bool ndjsonOutput = ContainsFlag(args, "--ndjson");

        try
        {
            return await RunCoreAsync(args, version, stdin, stdout, stderr, cancellationToken).ConfigureAwait(false);
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
                SafeWriteLine(stderr,
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
            // unactionable. Mirrors the pattern in Winix.Retry/Cli.cs.
            //
            // Mode-discriminated: under structured-output modes emit ONLY the envelope so the
            // stream stays parseable as one JSON object per line. Mixing the plaintext
            // diagnostic alongside the envelope (the round-4 implementation) broke strict
            // NDJSON parsers — same defect class as the round-4 input-pipeline catch fix that
            // was missed here.
            (Exception surface, bool depthCapped) = ExceptionUnwrap.UnwrapTypeInit(ex);
            if (jsonOutput || ndjsonOutput)
            {
                SafeWriteLine(stderr,
                    Formatting.FormatJsonError(ExitCode.NotExecutable, "unexpected_error", "wargs", version));
            }
            else
            {
                string msg = string.IsNullOrEmpty(surface.Message)
                    ? $"wargs: unexpected error: {surface.GetType().Name}"
                    : $"wargs: unexpected error: {surface.GetType().Name}: {surface.Message}";
                if (depthCapped)
                {
                    msg += " (unwrap depth limit reached — root cause may be deeper)";
                }
                SafeWriteLine(stderr, msg);
            }
            return ExitCode.NotExecutable;
        }
    }

    /// <summary>
    /// Pre-scans <paramref name="args"/> for an exact flag match, ignoring `--` separator
    /// boundaries (we deliberately scan the whole array because `--` parsing happens later).
    /// Used by <see cref="RunAsync"/> to detect structured-output modes before parser
    /// construction so the outer catches can emit JSON envelopes on cancellation /
    /// unexpected errors.
    /// </summary>
    private static bool ContainsFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == flag) { return true; }
        }
        return false;
    }

    private static async Task<int> RunCoreAsync(string[] args, string version, TextReader stdin,
        TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {

        var parser = new CommandLineParser("wargs", version)
            .Description("Read items from stdin and execute a command for each one.")
            .Maturity(ToolMaturity.Core)
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
                (WargsExitCode.ChildFailed, "One or more child processes failed (or could not be spawned). Per-job spawn failures surface in fault_message rather than as a separate exit code — wargs intentionally collapses spawn failures into 123 + per-job diagnostic."),
                (WargsExitCode.FailFastAbort, "Aborted due to --fail-fast"),
                (ExitCode.UsageError, "Usage error"),
                (ExitCode.NotExecutable, "Internal/IO failure: stdin read failed, unexpected exception escaped to safety net"),
                (130, "Cancelled by signal (Ctrl+C / SIGINT)"))
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
            .JsonField("tool", "string", "Tool name (\"wargs\"). Emitted in both --json summary envelope and --ndjson per-job lines.")
            .JsonField("version", "string", "Tool version. Emitted in both --json summary envelope and --ndjson per-job lines.")
            .JsonField("exit_code", "int", "(--json summary) Tool exit code: 0 = success; 123 = child_failed (any child non-zero exit OR spawn failure — spawn failures surface via per-job fault_message rather than a separate exit code); 124 = fail_fast_abort; 125 = usage_error; 126 = unexpected_error / input_read_failed; 130 = cancelled. (--ndjson per-job) Restricted to 0 (job succeeded) or 123 (job's child failed or could not be spawned).")
            .JsonField("exit_reason", "string", "(--json summary) One of: success, child_failed, fail_fast_abort, no_input, input_read_failed, usage_error, dry_run, cancelled, unexpected_error. (--ndjson per-job) Restricted to: success or child_failed.")
            .JsonField("total_jobs", "int", "(--json summary) Total number of jobs. Reflects --dry-run plan count when emitted under exit_reason=dry_run.")
            .JsonField("succeeded", "int", "(--json summary) Jobs that exited 0")
            .JsonField("failed", "int", "(--json summary) Jobs that exited non-zero")
            .JsonField("skipped", "int", "(--json summary) Jobs skipped (fail-fast or confirm declined)")
            .JsonField("wall_seconds", "float", "(--json summary) Wall-clock duration of the wargs run in seconds — NOT the sum of per-job durations (under -P parallel jobs overlap, so wall_seconds is closer to max(per-job durations) plus dispatch overhead). (--ndjson per-job) Per-job wall-clock duration in seconds.")
            .JsonField("faults", "object[]|null", "(--json summary) Per-job fault diagnostics; present only when at least one job carries a FaultMessage. Each entry is {job: int, message: string}.")
            .JsonField("job", "int", "(--ndjson per-job) 1-based job index of this line")
            .JsonField("child_exit_code", "int", "(--ndjson per-job) Child process exit code. -1 only on a TRUE spawn failure (--no-shell-fallback, or the fallback shell itself failing to start) — under the default shell fallback a not-found command is retried via sh -c / cmd /c, so the shell's exit code (127 / 9009) appears here instead, with no fault_message.")
            .JsonField("input", "string|string[]", "(--ndjson per-job) Source item(s) — string when --batch=1, array when batched")
            .JsonField("fault_message", "string|null", "(--ndjson per-job) Spawn/task fault diagnostic; omitted on normal paths");

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
                SafeWriteLine(stderr,
                    Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "wargs", version));
                return ExitCode.UsageError;
            }
            return result.WriteErrors(stderr);
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
            return UsageError(result, "--null, --delimiter, and --compat are mutually exclusive", jsonOutput, ndjsonOutput, version, stderr);
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
                return UsageError(result, "--delimiter requires a single character", jsonOutput, ndjsonOutput, version, stderr);
            }
            delimMode = DelimiterMode.Custom;
            customDelimiter = delimStr[0];
        }

        // --- Validate flag combinations ---
        if (confirm && parallelism != 1)
        {
            return UsageError(result, "--confirm cannot be used with parallel execution", jsonOutput, ndjsonOutput, version, stderr);
        }

        if (lineBuffered && keepOrder)
        {
            return UsageError(result, "--line-buffered and --keep-order cannot be combined", jsonOutput, ndjsonOutput, version, stderr);
        }

        if (lineBuffered && parallelism != 1)
        {
            return UsageError(result, "--line-buffered cannot be used with parallel execution (output would interleave)", jsonOutput, ndjsonOutput, version, stderr);
        }

        if (ndjsonOutput && lineBuffered)
        {
            return UsageError(result, "--ndjson and --line-buffered cannot be combined", jsonOutput, ndjsonOutput, version, stderr);
        }

        if ((jsonOutput || ndjsonOutput) && confirm)
        {
            // Confirm prompts to stderr ("wargs: run 'X'? [y/N] ") and the
            // "no terminal available; declining" diagnostic both write plaintext
            // to stderr — the same channel as structured envelopes/streaming JSON.
            // Mixing breaks line-discipline parsers. Same root reason as the
            // --verbose rejection below and the --ndjson + --line-buffered rejection above.
            return UsageError(result, "--confirm cannot be combined with --json or --ndjson (prompt would interleave plaintext with structured output)", jsonOutput, ndjsonOutput, version, stderr);
        }

        if ((jsonOutput || ndjsonOutput) && verbose)
        {
            // Verbose writes raw "wargs: <command>" lines to stderr per invocation;
            // structured-output modes also use stderr for envelopes/streaming JSON. Mixing
            // the two breaks line-discipline parsers (the user piping `wargs --ndjson -v ... | jq`
            // hit interleaved plaintext among NDJSON lines). Same root reason as the
            // --ndjson/--line-buffered rejection above.
            return UsageError(result, "--verbose cannot be combined with --json or --ndjson (would interleave plaintext with structured output)", jsonOutput, ndjsonOutput, version, stderr);
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
        var inputReader = new InputReader(stdin, delimMode, customDelimiter);
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
            IEnumerable<string> items = inputReader.ReadItems(cancellationToken);
            invocations = commandBuilder.Build(items).ToList();
        }
        catch (Exception ex) when (
            ex is not OutOfMemoryException
            and not StackOverflowException
            and not OperationCanceledException)
        {
            // Round-7 SFH: re-check the cancellation token before classifying as
            // input_read_failed. On Linux a SIGINT may translate to an IOException
            // (EINTR-restart failure) on a blocked stdin read — the cancellation token
            // is already signalled at that point. Without this re-check the user sees
            // input_read_failed (exit 126) instead of the documented cancelled (exit
            // 130) for a Ctrl+C-during-stdin path. Throwing OCE here lets the OCE
            // catch produce the cancelled envelope.
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            // Mode discrimination: under --json/--ndjson, emit ONLY the envelope so the
            // stream stays parseable as one JSON object per line. Mixing the plaintext
            // diagnostic on the same stream broke strict NDJSON parsers (round-4 SFH).
            if (jsonOutput || ndjsonOutput)
            {
                SafeWriteLine(stderr,
                    Formatting.FormatJsonError(ExitCode.NotExecutable, "input_read_failed", "wargs", version));
            }
            else
            {
                SafeWriteLine(stderr, $"wargs: failed to read input: {ex.GetType().Name}: {ex.Message}");
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
                SafeWriteLine(stderr, Formatting.FormatJson(
                    new WargsResult(0, 0, 0, 0, TimeSpan.Zero, new List<JobResult>()),
                    exitCode: 0,
                    exitReason: "no_input",
                    "wargs",
                    version));
            }
            else if (ndjsonOutput)
            {
                SafeWriteLine(stderr,
                    Formatting.FormatJsonError(0, "no_input", "wargs", version));
            }
            else
            {
                SafeWriteLine(stderr, "wargs: no input items, nothing to do");
            }
            return 0;
        }

        // --- Execute ---
        // The CancellationTokenSource and Console.CancelKeyPress handler are owned by Main
        // (registered before input materialisation, see Main for rationale). RunCoreAsync just
        // observes the token. Kill-on-cancel registrations inside JobRunner cleanly terminate
        // child process trees when the user hits Ctrl+C.
        //
        // Round-12: NDJSON streaming wired via OnJobCompleted callback (was emitted in a
        // batched post-run loop in earlier rounds — that violated the documented "as it
        // completes" contract). Round-12.5: callback contract changed to fire for EVERY
        // job (including skipped) so reorder-buffer subscribers below can advance their
        // next-expected pointer past skipped slots. The default callback shape filters
        // skipped jobs in the SUBSCRIBER per the per-job-stream contract: "one line per
        // job actually run". The --keep-order callback uses the skipped notifications to
        // advance its next-expected pointer past skipped slots without emitting. Both
        // callbacks are invoked from the parallel task body (any thread) or the sequential
        // dispatch loop (caller thread), so we lock stderr writes for thread safety.
        var ndjsonStderrLock = new object();
        if (ndjsonOutput && !dryRun)
        {
            // Reconstruct JobRunnerOptions with the streaming callback wired in. The earlier
            // construction at runnerOptions creation didn't have it because we hadn't yet
            // resolved the output mode.
            //
            // Two callback shapes:
            //   1. Default --ndjson: emit-on-receive in completion order. Skipped jobs are
            //      omitted from the stream (per-job-stream contract).
            //   2. --ndjson --keep-order: reorder-buffer in input order. Out-of-order
            //      completions are held back until all earlier-indexed jobs have been
            //      received (skipped jobs advance the next-expected pointer without
            //      emitting). Original wargs design (2026-03-31 design doc line 219:
            //      "With --keep-order, NDJSON lines emitted in order") explicitly required
            //      this — round-12's first streaming attempt missed it because the agents
            //      framed the bug as one-way "implementation doesn't match docs" without
            //      checking whether the docs had a second clause that batched-emission
            //      satisfied trivially.
            if (keepOrder)
            {
                var ndjsonBuffer = new Dictionary<int, JobResult>();
                int ndjsonNextExpected = 1;  // 1-based job index
                runnerOptions = runnerOptions with { OnJobCompleted = job =>
                {
                    lock (ndjsonStderrLock)
                    {
                        ndjsonBuffer[job.JobIndex] = job;
                        // Drain consecutive completed jobs starting from next-expected.
                        // Skipped jobs advance the pointer without emitting.
                        while (ndjsonBuffer.TryGetValue(ndjsonNextExpected, out JobResult? head))
                        {
                            ndjsonBuffer.Remove(ndjsonNextExpected);
                            ndjsonNextExpected++;
                            if (head.Skipped) { continue; }
                            int jobExitCode = head.ChildExitCode == 0 ? 0 : WargsExitCode.ChildFailed;
                            string jobExitReason = head.ChildExitCode == 0 ? "success" : "child_failed";
                            string line = Formatting.FormatNdjsonLine(head, jobExitCode, jobExitReason, "wargs", version);
                            SafeWriteLine(stderr, line);
                        }
                    }
                }};
            }
            else
            {
                runnerOptions = runnerOptions with { OnJobCompleted = job =>
                {
                    if (job.Skipped) { return; }  // omit skipped from default stream
                    int jobExitCode = job.ChildExitCode == 0 ? 0 : WargsExitCode.ChildFailed;
                    string jobExitReason = job.ChildExitCode == 0 ? "success" : "child_failed";
                    string line = Formatting.FormatNdjsonLine(job, jobExitCode, jobExitReason, "wargs", version);
                    lock (ndjsonStderrLock)
                    {
                        SafeWriteLine(stderr, line);
                    }
                }};
            }
            jobRunner = new JobRunner(runnerOptions);
        }

        WargsResult wargsResult = await jobRunner.RunAsync(
            invocations, stdout, stderr, cancellationToken).ConfigureAwait(false);

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
                SafeWriteLine(stderr, Formatting.FormatJson(
                    new WargsResult(invocations.Count, 0, 0, 0, TimeSpan.Zero, new List<JobResult>()),
                    exitCode: 0,
                    exitReason: "dry_run",
                    "wargs",
                    version));
            }
            else if (ndjsonOutput)
            {
                SafeWriteLine(stderr,
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

            // Round-12 SFH+TA I4: fail_fast_abort must fire ONLY when at least one job was
            // skipped specifically because of fail-fast. The prior `Skipped > 0` test mis-
            // classified runs where a confirm-declined skip happened to coexist with an
            // unrelated child failure (or external cancel + child failure). Now we check
            // SkipReason on each Skipped JobResult to filter for actual fail-fast skips.
            bool actualFailFastTriggered = false;
            foreach (JobResult j in wargsResult.Jobs)
            {
                if (j.Skipped && j.SkipReason == SkipReason.FailFastAbort)
                {
                    actualFailFastTriggered = true;
                    break;
                }
            }
            if (failFast && actualFailFastTriggered)
            {
                exitCode = WargsExitCode.FailFastAbort;
                exitReason = "fail_fast_abort";
            }
        }

        // --- NDJSON per-job lines were already streamed via OnJobCompleted (see above) ---
        // Skipped jobs are intentionally omitted from the stream to match the "one line
        // per job actually run" contract; the JSON summary's `skipped` field is the single
        // source of truth for skip count. Callers correlating job count with NDJSON line
        // count must add `skipped` to the line count.

        // --- JSON: summary to stderr ---
        if (jsonOutput)
        {
            SafeWriteLine(stderr,
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
                    SafeWriteLine(stderr, $"wargs: job {job.JobIndex}: {job.FaultMessage}");
                }
            }

            HumanSummary.Emit(result, wargsResult, stderr);
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
    private static int UsageError(ParseResult result, string message, bool jsonOutput, bool ndjsonOutput, string version, TextWriter stderr)
    {
        if (ndjsonOutput)
        {
            // Strict NDJSON: envelope only, no trailing plaintext that would break parsers.
            SafeWriteLine(stderr,
                Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "wargs", version));
            return ExitCode.UsageError;
        }
        if (jsonOutput)
        {
            SafeWriteLine(stderr,
                Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "wargs", version));
            // Plain-text reason after the single envelope so a human reading mixed output sees the cause.
            SafeWriteLine(stderr, $"wargs: {message}");
            return ExitCode.UsageError;
        }
        return result.WriteError(message, stderr);
    }

    /// <summary>
    /// Best-effort write to <paramref name="writer"/>. Broken pipe, closed stream, disposed
    /// writer, encoder fallback, or any other write-time fault must not convert a clean exit
    /// code into a CLR crash. The catch is intentionally broad (excluding only OOM/SOE which
    /// would already terminate the process) — diagnostic writes must be strictly weaker than
    /// the production paths they diagnose. Matches the suite-wide convention.
    /// </summary>
    private static void SafeWriteLine(TextWriter writer, string message)
    {
        try { writer.WriteLine(message); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* IOException, ObjectDisposedException, EncoderFallbackException, NotSupportedException — all best-effort-swallowed */ }
    }

    private static string GetVersion()
    {
        // SDK appends a SourceLink "+gitsha" suffix to AssemblyInformationalVersion
        // by default; strip it so users see plain "X.Y.Z" — matches the convention
        // adopted across clip / digest / ids / schedule / etc.
        string raw = typeof(WargsExitCode).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
