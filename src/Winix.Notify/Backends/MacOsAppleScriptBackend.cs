#nullable enable
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Sends a macOS notification by shelling out to <c>osascript</c> with an inline AppleScript snippet:
/// <c>display notification "BODY" with title "TITLE" sound name "Submarine"</c>.
/// The proper notification APIs (UNUserNotificationCenter) require a signed app bundle which a loose CLI binary
/// can't provide — osascript is the only viable path. <c>--icon</c> is silently ignored on macOS for the same
/// bundle-requires-icon-asset reason.
/// </summary>
public sealed class MacOsAppleScriptBackend : IBackend
{
    /// <inheritdoc />
    public string Name => "macos-osascript";

    /// <inheritdoc />
    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        string script = BuildScript(message);

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return new BackendResult(Name, false, "osascript not found (unexpected on macOS)", null);
            }
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                return new BackendResult(Name, false,
                    $"osascript exited {process.ExitCode}: {stderr.Trim()}", null);
            }
            return new BackendResult(Name, true, null, null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new BackendResult(Name, false, "osascript not found (unexpected on macOS)", null);
        }
    }

    // Internal for testing — escape order matters: backslash first, then double-quote.
    internal static string EscapeForApplescript(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string BuildScript(NotifyMessage message)
    {
        var sb = new StringBuilder();
        sb.Append("display notification \"");
        sb.Append(EscapeForApplescript(message.Body ?? ""));
        sb.Append("\" with title \"");
        sb.Append(EscapeForApplescript(message.Title));
        sb.Append('"');
        // Critical urgency adds an alert sound; low/normal stay silent.
        if (message.Urgency == Urgency.Critical)
        {
            sb.Append(" sound name \"Submarine\"");
        }
        return sb.ToString();
    }
}
