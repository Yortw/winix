using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Yort.ShellKit;

namespace Winix.Retry;

/// <summary>
/// Library entry point for the retry tool: parses arguments, builds <see cref="RetryOptions"/>,
/// runs the retry loop with a real process spawner, and routes summary output through the
/// supplied writers. <c>Program.Main</c> is a thin shell that owns Ctrl+C handling (process-global
/// <c>Console.CancelKeyPress</c> state) and passes a <see cref="CancellationToken"/> in.
/// </summary>
/// <remarks>
/// Seam limit (by design — do not "fix"): the child process inherits the REAL console handles
/// (<c>RedirectStandardOutput/Error/Input = false</c>) so its output passes through unmodified.
/// Tests through this seam therefore cannot observe child passthrough; that contract is covered
/// by ProgramMainTests (process-spawn) and the native smoke fixtures.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the retry CLI.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="stdout">Receives the summary only when <c>--stdout</c> is set; child stdout
    /// passes through the real console handles, never through this writer.</param>
    /// <param name="stderr">Receives progress lines, the final summary (default), and all errors.
    /// Errors always go here even under <c>--stdout</c> — pipe consumers expect stdout to be
    /// clean on failure.</param>
    /// <param name="cancellationToken">Cancellation signal (Ctrl+C in production, owned by Main).
    /// Cancels the wait AND kills the running child (entire tree).</param>
    /// <returns>Child exit code passed through, or 125/126/127 for retry's own errors.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        string version = GetVersion();

        var parser = new CommandLineParser("retry", version)
            .Description("Run a command with automatic retries on failure.")
            .StandardFlags()
            .Option("--times", "-n", "N", "Max retry attempts, not counting initial run (default: 3)")
            .Option("--delay", "-d", "DURATION", "Delay before retries, e.g. 500ms, 2s, 1m (default: 1s)")
            .Option("--backoff", "-b", "STRATEGY", "Backoff strategy: fixed, linear, exp (default: fixed)")
            .Flag("--jitter", null, "Add random jitter to delay (50-100% of calculated value)")
            .Option("--on", null, "CODES", "Retry only on these exit codes (comma-separated)")
            .Option("--until", null, "CODES", "Stop when exit code matches (comma-separated)")
            .Flag("--stdout", null, "Write summary to stdout instead of stderr (errors still go to stderr)")
            .CommandMode()
            .ExitCodes(
                (0, "Success (child returned 0) or child exit code pass-through (1–124)"),
                (ExitCode.UsageError, "Usage error: no command, bad retry arguments, invalid --on/--until list"),
                (ExitCode.NotExecutable, "Command not executable (permission denied, bad EXE format)"),
                (ExitCode.NotFound, "Command not found on PATH"))
            .Platform("cross-platform",
                replaces: new[] { "retry" },
                valueOnWindows: "No native retry loop; requires scripting boilerplate in PowerShell or batch",
                valueOnUnix: "Simpler than shell retry loops; richer output with exit-code filtering and backoff")
            .StdinDescription("Not used (child process inherits stdin)")
            .StdoutDescription("Child process stdout passes through unmodified")
            .StderrDescription("Progress lines and final JSON summary. Child stderr also passes through.")
            .Example("retry dotnet build", "Retry a build up to 3 times on failure")
            .Example("retry --times 5 --delay 2s curl https://example.com/api", "Retry HTTP call with delay")
            .Example("retry --backoff exp --delay 1s --times 6 dotnet test", "Exponential backoff")
            .Example("retry --on 1,2 --times 3 my-script.sh", "Retry only on specific exit codes")
            .Example("retry --until 42 --times 10 poll-command", "Stop when exit code matches target")
            .ComposesWith("timeit", "timeit retry --times 3 dotnet build", "Time a build with retries")
            .ComposesWith("peep", "peep -- retry dotnet test", "Watch tests with auto-retry on file change")
            .ComposesWith("nc", "retry --until 0 --times 30 --delay 2s nc -z localhost 5432", "Wait until a service port accepts connections (wait-for-it replacement)")
            .JsonField("tool", "string", "Tool name (\"retry\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (retry's own return value — equal to child's on pass-through, or 125/126/127 on tool errors)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason: succeeded, retries_exhausted, not_retryable, launch_failed, cancelled")
            .JsonField("child_exit_code", "int|null", "Final child process exit code, or null if the child never ran (e.g. launch_failed)")
            .JsonField("attempts", "int", "Total attempts made (initial run + retries)")
            .JsonField("max_attempts", "int", "Maximum attempts allowed (--times + 1)")
            .JsonField("total_seconds", "float", "Total wall time including delays in seconds")
            .JsonField("delays_seconds", "float[]", "Actual delay durations between attempts in seconds");

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        // Errors from the parser go to stderr even when --stdout is requested. --stdout only
        // applies to the SUCCESS summary; error output must never be redirected — pipe consumers
        // expect stdout to be clean on failure.
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        bool jsonOutput = result.Has("--json");
        bool useStdout = result.Has("--stdout");
        bool useColor = result.ResolveColor(checkStdErr: !useStdout);
        // summaryWriter carries retry's own summary output (per-attempt progress lines in plain
        // mode, the final JSON envelope in --json mode). The pre-R1-review code reused this
        // writer for error output too — silently routing usage errors to stdout under --stdout.
        // Errors now always go to Console.Error regardless of --stdout.
        TextWriter summaryWriter = useStdout ? stdout : stderr;

        if (result.Command.Length == 0)
        {
            return result.WriteError("no command specified. Run 'retry --help' for usage.", stderr);
        }

        // --- Parse --times ---
        int maxRetries = 3;
        if (result.Has("--times"))
        {
            string timesStr = result.GetString("--times");
            if (!int.TryParse(timesStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxRetries) || maxRetries < 0)
            {
                return result.WriteError($"invalid --times value: '{timesStr}' (must be a non-negative integer)", stderr);
            }
        }

        // --- Parse --delay ---
        TimeSpan delay = TimeSpan.FromSeconds(1);
        if (result.Has("--delay"))
        {
            string delayStr = result.GetString("--delay");
            if (!DurationParser.TryParse(delayStr, out delay))
            {
                return result.WriteError($"invalid --delay value: '{delayStr}' (e.g. 500ms, 2s, 1m)", stderr);
            }
        }

        // --- Parse --backoff ---
        BackoffStrategy backoff = BackoffStrategy.Fixed;
        if (result.Has("--backoff"))
        {
            string backoffStr = result.GetString("--backoff");
            if (backoffStr.Equals("fixed", StringComparison.OrdinalIgnoreCase))
            {
                backoff = BackoffStrategy.Fixed;
            }
            else if (backoffStr.Equals("linear", StringComparison.OrdinalIgnoreCase))
            {
                backoff = BackoffStrategy.Linear;
            }
            else if (backoffStr.Equals("exp", StringComparison.OrdinalIgnoreCase)
                     || backoffStr.Equals("exponential", StringComparison.OrdinalIgnoreCase))
            {
                backoff = BackoffStrategy.Exponential;
            }
            else
            {
                return result.WriteError($"invalid --backoff value: '{backoffStr}' (must be fixed, linear, or exp)", stderr);
            }
        }

        bool jitter = result.Has("--jitter");

        // --- Parse --on and --until ---
        HashSet<int>? retryCodes = ParseCodeList(result.Has("--on") ? result.GetString("--on") : null, out string? onInvalid);
        if (onInvalid != null)
        {
            return result.WriteError($"invalid --on value: '{onInvalid}' is not a valid exit-code list.", stderr);
        }

        HashSet<int>? stopCodes = ParseCodeList(result.Has("--until") ? result.GetString("--until") : null, out string? untilInvalid);
        if (untilInvalid != null)
        {
            return result.WriteError($"invalid --until value: '{untilInvalid}' is not a valid exit-code list.", stderr);
        }

        if (retryCodes != null && stopCodes != null)
        {
            return result.WriteError("--on and --until cannot be combined — they are contradictory.", stderr);
        }

        // --- Build options ---
        var options = new RetryOptions(maxRetries, delay, backoff, jitter, retryCodes, stopCodes);

        string command = result.Command[0];
        string[] commandArgs = result.Command.Skip(1).ToArray();

        try
        {
            return RunWithRetry(command, commandArgs, options, version, jsonOutput, useColor,
                summaryWriter, stderr, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Final safety net: round-2's narrowing of IsLaunchFailure means any Win32Exception
            // from WaitForExit/ExitCode — or any other unexpected exception bubbling out of
            // RunWithRetry — now escapes the library. Without this catch, the CLR's unhandled-
            // exception handler kicks in and (with <StackTraceSupport>false</StackTraceSupport>)
            // prints a stack-traceless "Unhandled exception" message that's nearly un-diagnosable.
            // Unwrap TypeInitializationException so the user sees the actionable inner cause
            // (e.g. "Unable to load libX.so") rather than the useless wrapper text. Matches
            // envvault's Cli.UnwrapTypeInit pattern.
            Exception surface = UnwrapTypeInit(ex);
            string msg = string.IsNullOrEmpty(surface.Message)
                ? $"retry: unexpected error: {surface.GetType().Name}"
                : $"retry: unexpected error: {surface.GetType().Name}: {surface.Message}";
            SafeWriteLine(stderr, msg);
            return ExitCode.NotExecutable;
        }
    }

    private static int RunWithRetry(
        string command, string[] commandArgs, RetryOptions options, string version,
        bool jsonOutput, bool useColor, TextWriter summaryWriter, TextWriter stderr, CancellationToken cancellationToken)
    {
        // Build the real process spawner. The delegate signature includes CancellationToken so
        // a long-running child can be killed on Ctrl+C — without this, the retry loop's token
        // only gates BETWEEN attempts, leaving the current WaitForExit blocking indefinitely
        // if the child ignores SIGINT (common for daemons, some editors, sudo, ping -t).
        // Reference pattern: src/Winix.Peep/CommandExecutor.cs:82-95.
        RetryRunner.RunProcessDelegate runProcess = (cmd, cmdArgs, attemptToken) =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cmd,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                RedirectStandardInput = false,
            };

            foreach (string arg in cmdArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new CommandNotFoundException(cmd);
            }
            catch (Win32Exception ex)
            {
                // Win32Exception is thrown on all .NET platforms (not just Windows).
                // .NET maps POSIX errors to Win32 error codes on Linux/macOS.
                // ERROR_ACCESS_DENIED (5) on Windows, EACCES (13) on Linux/macOS → not executable.
                if (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 13)
                {
                    throw new CommandNotExecutableException(cmd);
                }

                // ERROR_FILE_NOT_FOUND (2), ERROR_PATH_NOT_FOUND (3), ENOENT (2) → not found.
                if (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3)
                {
                    throw new CommandNotFoundException(cmd);
                }

                // Other errors (ERROR_BAD_EXE_FORMAT, etc.) — wrap as CommandNotExecutableException
                // so RetryRunner's narrow IsLaunchFailure catch (typed only) recognises it. This
                // prevents mid-run Win32Exceptions from `WaitForExit`/`ExitCode` from being
                // misclassified as launch failures — those now escape loudly as real bugs.
                //
                // Use the (message, innerException) constructor: the single-arg ctor prepends
                // "permission denied: " unconditionally, which is actively misleading for
                // non-permission errors like ERROR_BAD_EXE_FORMAT (193). The 2-arg ctor uses
                // the supplied message verbatim and preserves the underlying Win32Exception
                // for diagnostics. Consumers needing the command name can parse it from the
                // prefixed message (Program.cs only uses .Message for display).
                throw new CommandNotExecutableException($"{cmd}: {ex.Message}", ex);
            }

            // Wrap the registration + WaitForExit in a single try so the registration disposes
            // BEFORE `process.Dispose()` runs (finally). Without this nesting, `using killReg`
            // extends past the inner try/finally and a Ctrl+C arriving between process.Dispose
            // and killReg.Dispose would hit a disposed Process in the Register callback. The
            // inner try/finally ensures: Register scope ends → killReg.Dispose (unregister) →
            // THEN process.Dispose. Reference: review round-2 Critical C2.
            try
            {
                using CancellationTokenRegistration killReg = attemptToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    // ObjectDisposedException must come FIRST — it derives from InvalidOperationException,
                    // so the reverse order is compile-error-level unreachable. This catches the race where
                    // process.Dispose ran before killReg.Dispose (defence-in-depth; the disposal-order
                    // fix above makes this unreachable in practice, but the catch protects future changes).
                    catch (ObjectDisposedException) { /* process disposed before kill fired */ }
                    catch (InvalidOperationException) { /* already exited — benign */ }
                    catch (Win32Exception ex)
                    {
                        // Real kill failure (access-denied on elevated child, signal-delivery error on
                        // Linux that surfaces as Win32Exception). Surface the diagnostic so the user
                        // knows why retry appears to be lingering. SafeWriteLine for consistency with
                        // the rest of Program.cs — the bare-catch previously used here would have
                        // also swallowed OOM/StackOverflow, which is overly broad.
                        SafeWriteLine(stderr, $"retry: warning: failed to kill child: {ex.Message}");
                    }
                    catch (NotSupportedException ex)
                    {
                        // NotSupportedException here is a framework exception; under UseSystemResourceKeys
                        // its .Message would be a bare CoreLib resource key. Route via SafeError for readable text.
                        SafeWriteLine(stderr, $"retry: warning: cannot kill child: {SafeError.Describe(ex)}");
                    }
                });

                // Cancellation-aware wait: Process.WaitForExitAsync honours the token by completing
                // early with OperationCanceledException when the token is signalled, regardless of
                // whether the token fired BEFORE or DURING the wait. The prior code only checked
                // `attemptToken.IsCancellationRequested` once at the top, so a cancel arriving
                // mid-wait with a failed kill would wedge the synchronous WaitForExit forever.
                // .GetAwaiter().GetResult() unwraps async-task exceptions as the underlying type
                // without the AggregateException wrapper that .Wait() would produce.
                try
                {
                    process.WaitForExitAsync(attemptToken).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    // Unregister the kill callback FIRST: the grace block runs its own
                    // synchronous Kill, and leaving the callback armed means a second Ctrl+C
                    // during the grace would race two concurrent Kill calls on the same Process.
                    // CancellationTokenRegistration.Dispose waits for any in-flight callback to
                    // complete, so after this line the callback will not fire again.
                    killReg.Dispose();

                    // Token cancelled; kill was fired via the Register callback above. Give the
                    // child a grace window to honour the signal. If it still hasn't exited, warn
                    // about the orphan risk (on Windows, process.Dispose does NOT terminate an
                    // unmanaged process — the child survives retry's exit).
                    const int CancelGraceMs = 5_000;

                    // WaitForExit(int) can throw on a disposed/invalid-handle Process; treat
                    // any such escape as "did not exit" so the orphan-warning path still fires
                    // rather than bubbling through the outer safety-net as a generic "unexpected
                    // error" (which would lose the specific cancel-diagnostic framing).
                    bool exited;
                    try { exited = process.WaitForExit(CancelGraceMs); }
                    catch (InvalidOperationException) { exited = false; }
                    catch (SystemException) { exited = false; }

                    if (!exited)
                    {
                        // Best-effort second kill — the first one (from the Register callback) may
                        // have hit a transient error. Silently swallow: if this also fails, the
                        // warning below still fires and the user has enough info to intervene.
                        try { process.Kill(entireProcessTree: true); }
                        catch (ObjectDisposedException) { }
                        catch (InvalidOperationException) { }
                        catch (Win32Exception) { }
                        catch (NotSupportedException) { }

                        // Process.Id throws InvalidOperationException when no process is
                        // associated; ObjectDisposedException only reachable in a future refactor
                        // (the outer finally disposes AFTER this block) but catch it for
                        // defence-in-depth so a disposed handle can't crash the diagnostic.
                        int pid;
                        try { pid = process.Id; }
                        catch (ObjectDisposedException) { pid = -1; }
                        catch (InvalidOperationException) { pid = -1; }
                        SafeWriteLine(stderr,
                            $"retry: warning: child (PID {pid}) did not exit within {CancelGraceMs}ms of cancel — retry is exiting; child may still be running");
                        // Conventional SIGKILL-ish exit code; explicit "we abandoned the child".
                        return 137;
                    }
                }
                return process.ExitCode;
            }
            finally
            {
                process.Dispose();
            }
        };

        var runner = new RetryRunner(runProcess);

        // --- Progress callback (only in non-JSON mode) ---
        // Wrap the writer in a broken-pipe-tolerant shim: a closed stderr/stdout (downstream
        // consumer exited early) must NOT convert a retry run into a CLR crash. Envvault
        // round 5 had the same class of bug.
        Action<AttemptInfo>? onAttempt = null;
        if (!jsonOutput)
        {
            onAttempt = (info) => SafeWriteLine(summaryWriter, Formatting.FormatAttempt(info, useColor));
        }

        // --- Run with retries ---
        RetryResult retryResult = runner.Run(command, commandArgs, options, onAttempt,
            cancellationToken: cancellationToken);

        // --- Handle launch failure (child never ran, or partial run then launch failed) ---
        if (retryResult.Outcome == RetryOutcome.LaunchFailed)
        {
            if (jsonOutput)
            {
                // FormatJson correctly emits child_exit_code=null for LaunchFailed.
                SafeWriteLine(summaryWriter, Formatting.FormatJson(retryResult, "retry", version));
            }
            else
            {
                // Plain-text error always on stderr. Use the original exception's message so
                // the user sees "retry: command not found: X" rather than a generic label.
                SafeWriteLine(stderr,
                    $"retry: {retryResult.LaunchError?.Message ?? "launch failed"}");
            }
            return retryResult.ChildExitCode;
        }

        // --- JSON summary on success / RetriesExhausted / NotRetryable paths ---
        if (jsonOutput)
        {
            SafeWriteLine(summaryWriter, Formatting.FormatJson(retryResult, "retry", version));
        }

        return retryResult.ChildExitCode;
    }

    /// <summary>
    /// Best-effort write to <paramref name="writer"/>. Broken pipe, closed stream, or disposed
    /// writer must not convert a clean exit code into a CLR crash. Matches the suite-wide
    /// SafeWriteLine convention (see <c>src/Winix.EnvVault/Cli.cs</c> for the precedent).
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
    /// the user. The InnerException carries the real cause (e.g. "Unable to load libsecret-1.so.0").
    /// Depth cap protects against a pathological self-referencing exception chain. Same pattern
    /// as envvault's Cli.UnwrapTypeInit — worth lifting to ShellKit at some point.
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
        // SDK appends a SourceLink "+gitsha" suffix to AssemblyInformationalVersion
        // by default; strip it so users see plain "X.Y.Z" — matches the convention
        // adopted across clip / digest / ids / schedule / etc.
        string raw = typeof(RetryResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }

    /// <summary>
    /// Parses a comma-separated exit-code list. Distinguishes three cases:
    /// <para>- <paramref name="value"/> is null (flag absent) → returns null, no error.</para>
    /// <para>- <paramref name="value"/> is empty / all-empty-entries (e.g. <c>""</c> or <c>",,,"</c>)
    ///   → sets <paramref name="invalidEntry"/> to <c>"(empty list)"</c> and returns null. This is
    ///   the fix for the "empty-list silently disables constraint" defect: silently treating
    ///   <c>--on ""</c> as "no filter" flipped retry's conservatism upside down (user intended
    ///   "retry only on these codes", actual effect: "retry on any non-zero"). A CI config bug
    ///   could turn a focused retry into a retry storm with no warning.</para>
    /// <para>- <paramref name="value"/> contains non-integer entries → sets
    ///   <paramref name="invalidEntry"/> to the first offending part and returns null.</para>
    /// </summary>
    private static HashSet<int>? ParseCodeList(string? value, out string? invalidEntry)
    {
        invalidEntry = null;
        if (value == null) { return null; }  // flag absent — legal null
        var codes = new HashSet<int>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                codes.Add(code);
            }
            else
            {
                invalidEntry = part;
                return null;
            }
        }
        if (codes.Count == 0)
        {
            invalidEntry = "(empty list)";
            return null;
        }
        return codes;
    }
}
