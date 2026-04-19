#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Sends a Windows toast notification by shelling out to PowerShell with an inline
/// toast XML payload. Uses Windows.UI.Notifications via PowerShell's WinRT bridge,
/// which avoids the modern .NET limitation that <c>[ComImport, InterfaceIsIInspectable]</c>
/// is not directly marshalable from C# without the official WinRT projection.
/// </summary>
/// <remarks>
/// PowerShell startup adds ~300-500ms cold-start latency vs direct COM, but for
/// fire-and-forget notifications this is unobservable in practice. A future v2 can
/// migrate to <c>Microsoft.Windows.SDK.NET.Ref</c> with a TFM-split (net10.0-windows10.0.19041.0
/// for the console app) if anyone needs sub-100ms latency.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsToastBackend : IBackend
{
    /// <inheritdoc />
    public string Name => "windows-toast";

    /// <inheritdoc />
    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        try
        {
            AumidShortcut.EnsureExists();
            string xml = BuildToastXml(message);
            string script = BuildPowerShellScript(AumidShortcut.Aumid, xml);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

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
                    $"Windows toast: PowerShell exited {process.ExitCode}: {stderr.Trim()}", null);
            }
            return new BackendResult(Name, true, null, null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ENOENT — powershell.exe not found.
            return new BackendResult(Name, false, "Windows toast: powershell.exe not found on PATH", null);
        }
        catch (Exception ex)
        {
            return new BackendResult(Name, false,
                $"Windows toast: {ex.GetType().Name}: {ex.Message}", null);
        }
    }

    // Internal for testing the XML composition without hitting PowerShell.
    internal static string BuildToastXml(NotifyMessage message)
    {
        // Toast XML schema: ToastGeneric template — title + body line + optional image + audio.
        var sb = new StringBuilder();
        sb.Append("<toast");
        if (message.Urgency == Urgency.Critical)
        {
            // Win11 honours scenario; harmless on Win10.
            sb.Append(" scenario=\"urgent\"");
        }
        sb.Append("><visual><binding template=\"ToastGeneric\">");
        sb.Append("<text>").Append(EscapeXml(message.Title)).Append("</text>");
        if (message.Body is not null)
        {
            sb.Append("<text>").Append(EscapeXml(message.Body)).Append("</text>");
        }
        if (message.IconPath is not null)
        {
            sb.Append("<image placement=\"appLogoOverride\" src=\"")
                .Append(EscapeXml(message.IconPath)).Append("\"/>");
        }
        sb.Append("</binding></visual>");
        if (message.Urgency == Urgency.Low)
        {
            sb.Append("<audio silent=\"true\"/>");
        }
        sb.Append("</toast>");
        return sb.ToString();
    }

    private static string EscapeXml(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    // Internal for testing — builds the inline PowerShell that loads WinRT and shows the toast.
    internal static string BuildPowerShellScript(string aumid, string toastXml)
    {
        // The XML is double-quoted in PS, so escape any " in the XML as `" (PS escape).
        // Since our XML escapes " as &quot; in EscapeXml above, raw " is impossible inside
        // attribute values. The only " in the XML are the structural ones from our builder.
        // Single-quote the PS string and escape inner single quotes — safer.
        string psXml = toastXml.Replace("'", "''");
        string psAumid = aumid.Replace("'", "''");

        // Load the WinRT types via [Windows.X.Y, Windows.X.Y, ContentType=WindowsRuntime]
        // pattern — PowerShell 5.1+ ships with the WinRT bridge that handles IInspectable
        // marshalling for us. Errors propagate via ThrowOnError = $true and PowerShell exits non-zero.
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("[void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]");
        sb.AppendLine("[void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime]");
        sb.AppendLine("$x = New-Object Windows.Data.Xml.Dom.XmlDocument");
        sb.Append("$x.LoadXml('").Append(psXml).AppendLine("')");
        sb.Append("$t = New-Object Windows.UI.Notifications.ToastNotification $x").AppendLine();
        sb.Append("[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('").Append(psAumid).AppendLine("').Show($t)");
        return sb.ToString();
    }
}
