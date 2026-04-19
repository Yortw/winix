#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Sends a Windows toast notification via direct WinRT COM activation. Requires the AUMID
/// from <see cref="AumidShortcut"/> to be registered (via a Start Menu shortcut) — handled
/// idempotently on every send.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsToastBackend : IBackend
{
    /// <inheritdoc />
    public string Name => "windows-toast";

    /// <inheritdoc />
    public Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        // Synchronous body — Windows.UI.Notifications.ToastNotifier.Show is fire-and-forget.
        // Wrap to satisfy the async interface.
        try
        {
            AumidShortcut.EnsureExists();
            string xml = BuildToastXml(message);
            ShowToastViaWinRT(AumidShortcut.Aumid, xml);
            return Task.FromResult(new BackendResult(Name, true, null, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new BackendResult(Name, false,
                $"Windows toast: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    // Internal for testing the XML composition without hitting WinRT.
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

    // --- Direct COM activation of WinRT ToastNotificationManager ---
    //
    // We avoid the .NET WinRT projection (Microsoft.Windows.SDK.NET.Ref) so this library
    // can target plain net10.0 without TFM gymnastics. The COM contracts below are stable
    // from Windows 8 onwards.

    private static void ShowToastViaWinRT(string aumid, string xml)
    {
        // 1. Get IToastNotificationManagerStatics (activation factory for ToastNotificationManager).
        // 2. Create a ToastNotifier bound to our AUMID.
        // 3. Build a ToastNotification from a Windows.Data.Xml.Dom.XmlDocument that has loaded our XML.
        // 4. Call notifier.Show(toast).

        const string runtimeClassToastManager = "Windows.UI.Notifications.ToastNotificationManager";
        const string runtimeClassXmlDocument = "Windows.Data.Xml.Dom.XmlDocument";
        const string runtimeClassToastNotification = "Windows.UI.Notifications.ToastNotification";

        IntPtr managerStatics = IntPtr.Zero;
        IntPtr xmlDocument = IntPtr.Zero;
        IntPtr toastFactory = IntPtr.Zero;

        try
        {
            IntPtr managerActivatableId = WindowsCreateString(runtimeClassToastManager);
            try
            {
                Guid iidManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");
                int hr = RoGetActivationFactory(managerActivatableId, ref iidManagerStatics, out managerStatics);
                Marshal.ThrowExceptionForHR(hr);
            }
            finally { WindowsDeleteString(managerActivatableId); }

            IntPtr xmlActivatableId = WindowsCreateString(runtimeClassXmlDocument);
            try
            {
                int hr = RoActivateInstance(xmlActivatableId, out xmlDocument);
                Marshal.ThrowExceptionForHR(hr);
            }
            finally { WindowsDeleteString(xmlActivatableId); }

            // QI for IXmlDocumentIO and load the toast XML.
            IXmlDocumentIO xmlIo = (IXmlDocumentIO)Marshal.GetObjectForIUnknown(xmlDocument);
            IntPtr hxml = WindowsCreateString(xml);
            try
            {
                xmlIo.LoadXml(hxml);
            }
            finally { WindowsDeleteString(hxml); }

            // Create the toast notifier with our AUMID.
            IToastNotificationManagerStatics statics = (IToastNotificationManagerStatics)Marshal.GetObjectForIUnknown(managerStatics);
            IntPtr aumidStr = WindowsCreateString(aumid);
            IToastNotifier notifier;
            try
            {
                notifier = statics.CreateToastNotifierWithId(aumidStr);
            }
            finally { WindowsDeleteString(aumidStr); }

            // Build a ToastNotification from the XmlDocument.
            IntPtr toastClassId = WindowsCreateString(runtimeClassToastNotification);
            try
            {
                Guid iidToastFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");
                int hr = RoGetActivationFactory(toastClassId, ref iidToastFactory, out toastFactory);
                Marshal.ThrowExceptionForHR(hr);
            }
            finally { WindowsDeleteString(toastClassId); }

            IToastNotificationFactory tnFactory = (IToastNotificationFactory)Marshal.GetObjectForIUnknown(toastFactory);
            object toast = tnFactory.CreateToastNotification(xmlIo);

            notifier.Show(toast);
        }
        finally
        {
            if (managerStatics != IntPtr.Zero) Marshal.Release(managerStatics);
            if (xmlDocument != IntPtr.Zero) Marshal.Release(xmlDocument);
            if (toastFactory != IntPtr.Zero) Marshal.Release(toastFactory);
        }
    }

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        [In] ref Guid iid,
        out IntPtr factory);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = true)]
    private static extern int RoActivateInstance(
        IntPtr activatableClassId,
        out IntPtr instance);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern IntPtr WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    private static IntPtr WindowsCreateString(string s)
    {
        WindowsCreateString(s, (uint)s.Length, out IntPtr hstring);
        return hstring;
    }

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIInspectable), Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")]
    private interface IXmlDocumentIO
    {
        void LoadXml(IntPtr xml);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIInspectable), Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
    private interface IToastNotificationManagerStatics
    {
        IToastNotifier CreateToastNotifier();
        IToastNotifier CreateToastNotifierWithId(IntPtr aumid);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIInspectable), Guid("75927B93-03F3-41EC-91D3-6E5BAC1B38E7")]
    private interface IToastNotifier
    {
        void Show([MarshalAs(UnmanagedType.IUnknown)] object notification);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIInspectable), Guid("04124B20-82C6-4229-B109-FD9ED4662B53")]
    private interface IToastNotificationFactory
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object CreateToastNotification([MarshalAs(UnmanagedType.IUnknown)] object xml);
    }
}
