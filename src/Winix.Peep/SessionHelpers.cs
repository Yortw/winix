using System.Text.RegularExpressions;

namespace Winix.Peep;

/// <summary>
/// Pure decision helpers extracted from <see cref="InteractiveSession"/> and
/// <c>Program.RunOnceAsync</c> so each contract is testable without driving a
/// full event loop. R4 TA I3/I4/I6/I7 — per the project's "test-infeasible
/// branches → extract or seam, don't skip" rule, body of any inline lambda or
/// branch arm gets a name + a callable surface so tests can target the
/// regression-prone behaviour directly.
/// </summary>
internal static class SessionHelpers
{
    /// <summary>
    /// Emits a one-shot stderr warning for a regex that timed out under
    /// <c>--exit-on-match</c>. Subsequent calls with the same <paramref name="regex"/>
    /// instance are silent. The diagnostic write is strictly weaker than the watch
    /// loop: a failing <paramref name="writer"/> must not propagate.
    ///
    /// R4 TA I7 extraction. Without this, the <c>RegexMatchTimeoutException</c>
    /// catch arm in <c>CheckAutoExit</c> was test-infeasible (the warning policy
    /// hid behind a private HashSet). Now the policy is regression-pinnable.
    /// </summary>
    internal static void WarnOnceForRegexTimeout(Regex regex, HashSet<Regex> warned, TextWriter writer)
    {
        if (!warned.Add(regex))
        {
            return;
        }
        try
        {
            writer.WriteLine(
                $"[peep] warning: --exit-on-match pattern '{regex}' timed out; " +
                "treating as non-match for this and future runs of this pattern.");
        }
        catch
        {
            // best effort — see XML doc.
        }
    }

    /// <summary>
    /// Pure auto-exit predicate. Inspects <paramref name="lastResult"/> against the
    /// configured exit-on-X conditions and returns true with the matching reason.
    /// Returns false (with <paramref name="exitReason"/> = "manual") when no condition
    /// fires.
    ///
    /// R4 TA I3 extraction. Pre-extraction this lived inside
    /// <c>InteractiveSession.CheckAutoExit</c> and mutated <c>_exitReason</c> directly,
    /// so the round-1/round-2 contract changes ("exit_on_match" added to the
    /// success-class, regex-timeout one-shot warning) had no test pin. The session
    /// wrapper now reads <paramref name="exitReason"/> from the out-param.
    /// </summary>
    /// <param name="config">Immutable session configuration.</param>
    /// <param name="lastResult">The most recent run result, or null if no run happened.</param>
    /// <param name="prevOutput">Previous run output for exit-on-change comparison; null skips that branch.</param>
    /// <param name="regexTimeoutWarned">HashSet tracking which regex instances have already produced a one-shot timeout warning.</param>
    /// <param name="warnWriter">Where regex-timeout warnings are written (test seam).</param>
    /// <param name="exitReason">On true: the matching reason. On false: "manual" (caller may overwrite).</param>
    internal static bool TryGetAutoExit(
        SessionConfig config,
        PeepResult? lastResult,
        string? prevOutput,
        HashSet<Regex> regexTimeoutWarned,
        TextWriter warnWriter,
        out string exitReason)
    {
        exitReason = "manual";
        if (lastResult is null)
        {
            return false;
        }

        if (config.ExitOnSuccess && lastResult.ExitCode == 0)
        {
            exitReason = "exit_on_success";
            return true;
        }

        if (config.ExitOnError && lastResult.ExitCode != 0)
        {
            exitReason = "exit_on_error";
            return true;
        }

        if (config.ExitOnChange && prevOutput is not null
            && !string.Equals(lastResult.Output, prevOutput, StringComparison.Ordinal))
        {
            exitReason = "exit_on_change";
            return true;
        }

        if (config.ExitOnMatchRegexes.Length > 0 && lastResult.Output is not null)
        {
            string stripped = Formatting.StripAnsi(lastResult.Output);
            foreach (Regex regex in config.ExitOnMatchRegexes)
            {
                try
                {
                    if (regex.IsMatch(stripped))
                    {
                        exitReason = "exit_on_match";
                        return true;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    // R3 SFH I3 — one-shot warning per pattern, then treat as non-match.
                    WarnOnceForRegexTimeout(regex, regexTimeoutWarned, warnWriter);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Maps a session exit reason to the final peep exit code. Auto-exit conditions
    /// representing user-requested success conditions (exit_on_change, exit_on_success,
    /// exit_on_match) override the child's exit code with 0 — the user asked "exit when
    /// X", reaching X is a success signal regardless of the child's last exit code.
    ///
    /// R4 TA I3 extraction (the round-2 CR I2 fix). Pre-extraction this was an inline
    /// branch in <c>HandleExit</c> with no regression pin: a refactor that dropped any of
    /// the three exit reasons from the override list would silently regress the
    /// "0 = Auto-exit condition met" README contract.
    /// </summary>
    /// <param name="exitReason">The session's exit reason (e.g. "manual", "exit_on_match").</param>
    /// <param name="lastChildExit">The most recent child process exit code, or null if no run happened.</param>
    /// <param name="failedFallback">The fallback exit code when no child run was captured (e.g. 127 for command-not-found on initial run).</param>
    internal static int ResolveExitCode(string exitReason, int? lastChildExit, int failedFallback)
    {
        int exitCode = lastChildExit ?? failedFallback;

        if (exitReason == "exit_on_change"
            || exitReason == "exit_on_success"
            || exitReason == "exit_on_match")
        {
            exitCode = 0;
        }

        return exitCode;
    }

    /// <summary>
    /// Body of the <c>Console.CancelKeyPress</c> handler shared by both the interactive
    /// session and once-mode. Marks the event as handled and requests cancellation,
    /// silently swallowing <see cref="ObjectDisposedException"/> if the CTS has already
    /// been disposed (in-flight handler racing with shutdown unregister).
    ///
    /// R4 TA I6 extraction. <c>Console.CancelKeyPress</c> is a static event that
    /// doesn't compose with xunit, but the body itself is a pure function of its
    /// arguments and is now directly testable.
    /// </summary>
    /// <param name="e">The cancel event args (Cancel will be set to true).</param>
    /// <param name="cts">The CTS to cancel; may have already been disposed.</param>
    internal static void RequestCancellationSilently(ConsoleCancelEventArgs e, CancellationTokenSource cts)
    {
        e.Cancel = true;
        try { cts.Cancel(); } catch (ObjectDisposedException) { /* in-flight race with shutdown; safe to swallow */ }
    }

    /// <summary>
    /// Decides whether the main event loop should dispatch a run on this iteration,
    /// and if so, what triggered it. Consumes the file-change flag (atomic clear-if-set)
    /// only when <paramref name="running"/> is false — a long-running child must not
    /// silently swallow file-change triggers.
    ///
    /// R4 TA I4 extraction. Pre-extraction this logic was inline in the main loop and
    /// the round-2 fix (CR R2-C1: "check _running BEFORE consuming the file-change
    /// flag") had no regression pin. A future refactor that flipped the order back —
    /// or that converted <paramref name="running"/> to a non-volatile read subject to
    /// compiler reordering — would silently drop file-change triggers during long runs.
    /// </summary>
    /// <param name="running">True if a child process is currently in flight.</param>
    /// <param name="useInterval">True if interval-based triggering is enabled.</param>
    /// <param name="fileChangeFlag">Atomic flag (0/1). Consumed (set to 0) only when this method dispatches a FileChange.</param>
    /// <param name="now">Current wall clock for interval comparison (injected for determinism).</param>
    /// <param name="nextRunTime">When the next interval-driven run is due.</param>
    /// <param name="trigger">On true: the trigger source. Meaningless on false.</param>
    /// <returns>True if a run should be dispatched.</returns>
    internal static bool ShouldDispatch(
        bool running,
        bool useInterval,
        ref int fileChangeFlag,
        DateTime now,
        DateTime nextRunTime,
        out TriggerSource trigger)
    {
        // Default to Interval as the "no dispatch" sentinel; callers must check the bool.
        trigger = TriggerSource.Interval;

        if (running)
        {
            // CR R2-C1: do NOT consume the file-change flag while a run is in flight.
            // The flag stays set so the next idle iteration picks it up.
            return false;
        }

        if (Interlocked.CompareExchange(ref fileChangeFlag, 0, 1) == 1)
        {
            trigger = TriggerSource.FileChange;
            return true;
        }

        if (useInterval && now >= nextRunTime)
        {
            trigger = TriggerSource.Interval;
            return true;
        }

        return false;
    }
}
