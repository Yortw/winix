using System.Runtime.InteropServices;
using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

public class WindowsClipboardBackendTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [Fact]
    public void CopyThenPaste_RoundTripsText()
    {
        if (!IsWindows) { return; }

        var backend = new WindowsClipboardBackend();
        backend.CopyText("hello clipboard");

        string result = backend.PasteText();
        Assert.Equal("hello clipboard", result);
    }

    [Fact]
    public void CopyThenPaste_PreservesUnicode()
    {
        if (!IsWindows) { return; }

        var backend = new WindowsClipboardBackend();
        backend.CopyText("こんにちは 🌏 naïve café");

        string result = backend.PasteText();
        Assert.Equal("こんにちは 🌏 naïve café", result);
    }

    [Fact]
    public void Clear_ThenPaste_ReturnsEmpty()
    {
        if (!IsWindows) { return; }

        var backend = new WindowsClipboardBackend();
        backend.CopyText("to be cleared");
        backend.Clear();

        string result = backend.PasteText();
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CopyEmpty_ThenPaste_ReturnsEmpty()
    {
        if (!IsWindows) { return; }

        var backend = new WindowsClipboardBackend();
        backend.CopyText(string.Empty);

        string result = backend.PasteText();
        Assert.Equal(string.Empty, result);
    }
}
