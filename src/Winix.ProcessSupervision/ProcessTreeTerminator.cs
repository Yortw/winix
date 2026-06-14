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
}
