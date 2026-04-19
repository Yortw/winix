#nullable enable
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Sends a desktop notification on Linux by shelling out to <c>notify-send</c> (libnotify CLI).
/// notify-send is part of <c>libnotify-bin</c> on Debian/Ubuntu and <c>libnotify</c> on Fedora.
/// If the binary isn't on PATH, returns a failure with an install hint per common distro.
/// </summary>
public sealed class LinuxNotifySendBackend : IBackend
{
    /// <inheritdoc />
    public string Name => "linux-notify-send";

    /// <inheritdoc />
    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "notify-send",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        // ArgumentList per project convention — never string Arguments (avoids quoting bugs).
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(UrgencyArg(message.Urgency));
        if (message.IconPath is not null)
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(message.IconPath);
        }
        psi.ArgumentList.Add(message.Title);
        if (message.Body is not null)
        {
            psi.ArgumentList.Add(message.Body);
        }

        try
        {
            using var process = Process.Start(psi)!;
            // Drain stdout concurrently — child blocks on a full stdout pipe otherwise.
            var stdoutDrain = process.StandardOutput.ReadToEndAsync(ct);
            var stderrDrain = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            await stdoutDrain.ConfigureAwait(false);
            string stderr = await stderrDrain.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return new BackendResult(Name, false,
                    $"notify-send exited {process.ExitCode}: {stderr.Trim()}", null);
            }
            return new BackendResult(Name, true, null, null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ENOENT / "file not found" surfaces here when Process.Start can't locate the binary.
            return new BackendResult(Name, false,
                "notify-send not found — install libnotify-bin (Debian/Ubuntu) or libnotify (Fedora)",
                null);
        }
        catch (Exception ex)
        {
            // IBackend contract: never throw — convert to BackendResult.
            return new BackendResult(Name, false, $"notify-send: {ex.GetType().Name}: {ex.Message}", null);
        }
    }

    private static string UrgencyArg(Urgency u) => u switch
    {
        Urgency.Low => "low",
        Urgency.Critical => "critical",
        _ => "normal",
    };
}
