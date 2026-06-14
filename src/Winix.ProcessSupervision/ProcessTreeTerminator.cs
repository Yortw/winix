using System;
using System.ComponentModel;
using System.Diagnostics;

namespace Winix.ProcessSupervision;

/// <summary>
/// Cross-platform process-tree termination for the process-supervision family. <see cref="KillTree"/>
/// is the immediate (SIGKILL-equivalent) kill used by <c>lock</c>/<c>soak</c>/<c>attempt</c> on cancel;
/// <c>TerminateGracefully</c> (added in a later task) adds the Unix SIGTERM→grace→SIGKILL escalation
/// <c>runfor</c> uses.
/// </summary>
public static class ProcessTreeTerminator
{
    /// <summary>
    /// Immediately kills <paramref name="process"/> and its entire child tree. Best-effort: every
    /// failure mode (already-exited, disposed, access-denied, platform-can't-kill) is swallowed, so a
    /// kill that races process teardown never throws into the caller (a throwing kill called from a
    /// <see cref="System.Threading.CancellationToken"/> callback would propagate out of
    /// <c>Cancel()</c> into the supervising tool).
    /// </summary>
    public static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        // ObjectDisposedException FIRST — it derives from InvalidOperationException.
        catch (ObjectDisposedException) { /* disposed before kill fired */ }
        catch (InvalidOperationException) { /* already exited — benign */ }
        catch (Win32Exception) { /* access denied / signal-delivery error — best-effort */ }
        catch (NotSupportedException) { /* platform cannot kill the tree — best-effort */ }
    }

    /// <summary>
    /// Terminates <paramref name="process"/> with a graceful escalation.
    /// <para>Unix: sends <paramref name="signal"/> (typically SIGTERM) to the <b>direct child</b> via
    /// libc <c>kill(2)</c>, waits up to <paramref name="grace"/> for it to exit on its own, then — if
    /// it is still alive — kills the entire tree (SIGKILL backstop via <see cref="KillTree"/>).</para>
    /// <para>Windows: there is no portable graceful-termination signal, so <paramref name="signal"/>
    /// and <paramref name="grace"/> are ignored and the tree is killed immediately. This platform
    /// difference is intentional and documented (family ADR D7).</para>
    /// </summary>
    /// <param name="process">The child process to terminate.</param>
    /// <param name="signal">The Unix signal number to send first (ignored on Windows). See
    /// <see cref="NativeProcess"/> for the portable constants.</param>
    /// <param name="grace">How long to wait for a graceful exit before the SIGKILL backstop
    /// (ignored on Windows). Zero or negative ⇒ no grace: signal then immediately SIGKILL-tree if not
    /// already dead.</param>
    /// <returns>
    /// <c>true</c> if the process is confirmed exited when this method returns; <c>false</c> if it may
    /// still be alive (e.g. the signal AND the SIGKILL backstop both failed — typically EPERM, a child
    /// owned by another user). A caller (<c>runfor</c>) should surface <c>false</c> as "could not kill
    /// child (may still be running)" rather than silently reporting a clean timeout.
    /// </returns>
    /// <remarks>
    /// SCOPE (v1, accepted): the graceful <paramref name="signal"/> is sent to the DIRECT CHILD ONLY,
    /// not the child's process group. A child that handles the signal and exits ITSELF within
    /// <paramref name="grace"/> may therefore ORPHAN any grandchildren it spawned (they are not
    /// signalled, and the SIGKILL tree backstop does not fire because the parent exited in time). The
    /// backstop's <see cref="KillTree"/> reaps the whole tree ONLY when the parent ignores the signal
    /// and the grace elapses. True process-group signalling (<c>kill(-pgid, …)</c>) needs the child to
    /// be a session/group leader (<c>setsid</c>/<c>setpgid</c> pre-exec), which the BCL cannot arrange
    /// and macOS lacks the <c>setsid</c> CLI for — deferred (ADR D10).
    /// </remarks>
    public static bool TerminateGracefully(Process process, int signal, TimeSpan grace)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            // Windows / unsupported: no graceful signal model — kill immediately.
            KillTree(process);
            return ConfirmExited(process);
        }

        // Re-check HasExited immediately before signalling to NARROW (not eliminate) the PID-reuse
        // window: signalling by raw PID could otherwise hit an unrelated process that recycled the
        // PID after the child exited. See NativeProcess.SendSignal remarks.
        if (HasExitedSafe(process)) { return true; }

        int pid;
        // ObjectDisposedException derives from InvalidOperationException, so the broader catch covers
        // both (already-exited and disposed) — both mean "nothing left to signal".
        try { pid = process.Id; }
        catch (InvalidOperationException) { return true; }

        NativeProcess.SendSignal(pid, signal); // errno not surfaced here — the backstop + the bool
                                               // return below report the net outcome.

        // Give the child the grace window. WaitForExit(0) (grace<=0) does NOT block — it returns the
        // current exited-state immediately.
        int graceMs = grace <= TimeSpan.Zero ? 0 : (int)Math.Min(grace.TotalMilliseconds, int.MaxValue);
        bool exited;
        try { exited = process.WaitForExit(graceMs); }
        catch (InvalidOperationException) { return true; } // exited; handle state odd — nothing to kill
        catch (SystemException) { exited = false; }        // treat odd handle state as "still alive"

        if (!exited)
        {
            // Grace elapsed and the child ignored the signal — force-kill the whole tree (handle-based,
            // reuse-safe). This is where grandchildren get reaped.
            KillTree(process);
        }

        return ConfirmExited(process);
    }

    // Bounded confirm window: Process.Kill is asynchronous (especially on Windows), so a kill that
    // WILL succeed may not have taken effect the instant we check. Wait up to this long for the
    // process to actually die before declaring the kill failed. Returns as soon as it exits, so a
    // successful kill confirms in milliseconds — the cap only bites when the kill genuinely failed.
    private const int ConfirmExitMs = 5_000;

    // Best-effort "is it gone?" used for the bool return. A throwing/odd handle state is treated as
    // "not confirmed exited" (false) so the caller errs toward surfacing a possible survivor.
    private static bool ConfirmExited(Process process)
    {
        try { return process.WaitForExit(ConfirmExitMs); }
        catch (InvalidOperationException) { return true; } // no process associated ⇒ it's gone
        catch (SystemException) { return false; }
    }

    private static bool HasExitedSafe(Process process)
    {
        // ObjectDisposedException derives from InvalidOperationException — the broader catch covers both.
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }
}
