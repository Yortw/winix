using System;
using System.Runtime.InteropServices;

namespace Winix.ProcessSupervision;

/// <summary>
/// Thin libc <c>kill(2)</c> P/Invoke for sending a Unix signal to a process. Windows has no signal
/// model; <see cref="SendSignal"/> throws <see cref="PlatformNotSupportedException"/> there.
/// Mirrors the platform-split P/Invoke pattern of <c>Winix.TimeIt.NativeMetrics</c>.
/// </summary>
internal static partial class NativeProcess
{
    /// <summary>SIGHUP (1) — hangup. Identical on Linux and macOS.</summary>
    public const int SigHup = 1;

    /// <summary>SIGINT (2) — interrupt (Ctrl+C). Identical on Linux and macOS.</summary>
    public const int SigInt = 2;

    /// <summary>SIGQUIT (3) — quit. Identical on Linux and macOS.</summary>
    public const int SigQuit = 3;

    /// <summary>SIGKILL (9) — force kill, uncatchable. Identical on Linux and macOS.</summary>
    public const int SigKill = 9;

    /// <summary>SIGTERM (15) — polite termination request (default). Identical on Linux and macOS.</summary>
    public const int SigTerm = 15;

    /// <summary>ESRCH (3) — no such process. Benign: the target already exited.</summary>
    public const int ESRCH = 3;

    /// <summary>EPERM (1) — operation not permitted. The signal could NOT be delivered. A real failure.</summary>
    public const int EPERM = 1;

    /// <summary>
    /// Sends <paramref name="signal"/> to the process with id <paramref name="pid"/> via libc
    /// <c>kill(2)</c>. Linux uses <c>libc</c>, macOS uses <c>libSystem</c> (both expose <c>kill</c>).
    /// </summary>
    /// <returns>
    /// 0 on success, otherwise the errno from the failed <c>kill</c> (<see cref="ESRCH"/> = target
    /// already gone — benign; <see cref="EPERM"/> = not permitted — a real failure). Does NOT swallow
    /// the result, so a genuinely-failed kill (EPERM) is distinguishable from a benign one (ESRCH)
    /// and from success.
    /// </returns>
    /// <remarks>
    /// SIGNALS BY RAW PID — RESIDUAL REUSE RACE: signalling by PID (the only mechanism the BCL exposes
    /// on Unix) has a narrow window where the target could exit and its PID be recycled between the
    /// caller reading the PID and this call. Callers MUST re-check <c>Process.HasExited</c> immediately
    /// before calling to narrow the window; it cannot be eliminated. The handle-based SIGKILL backstop
    /// (<see cref="ProcessTreeTerminator.KillTree"/>) is reuse-safe.
    /// </remarks>
    /// <exception cref="PlatformNotSupportedException">Called on a non-Unix platform.</exception>
    public static int SendSignal(int pid, int signal)
    {
        int rc;
        if (OperatingSystem.IsLinux())
        {
            rc = KillLinux(pid, signal);
        }
        else if (OperatingSystem.IsMacOS())
        {
            rc = KillMacOS(pid, signal);
        }
        else
        {
            throw new PlatformNotSupportedException("Unix signals are not available on this platform.");
        }

        // kill returns 0 on success, -1 on failure with errno set. Surface the errno so callers can
        // distinguish ESRCH (benign) from EPERM (real failure) rather than treating all as success.
        return rc == 0 ? 0 : Marshal.GetLastPInvokeError();
    }
}
