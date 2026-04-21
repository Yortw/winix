using System;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;
using Winix.Clip;

namespace Winix.Clip.Tests;

public class WindowsClipboardBackendTests
{
    private readonly ITestOutputHelper _output;
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public WindowsClipboardBackendTests(ITestOutputHelper output) { _output = output; }

    // The Windows clipboard is a single-owner resource. If another process (clipboard manager,
    // remote-desktop redirection, browser extension, IDE tooling) holds it continuously, no
    // amount of retry will succeed — the production code retries for ~1s before giving up.
    // That is an environmental constraint, not a code regression, so we treat it as a skip.
    private void RunOrSkipIfBusy(Action body)
    {
        try { body(); }
        catch (ClipboardException ex) when (ex.Message.Contains("busy", StringComparison.Ordinal))
        {
            _output.WriteLine($"[skip] Clipboard held by external process throughout retry budget: {ex.Message}");
        }
    }

    [Fact]
    public void CopyThenPaste_RoundTripsText()
    {
        if (!IsWindows) { return; }

        RunOrSkipIfBusy(() =>
        {
            var backend = new WindowsClipboardBackend();
            backend.CopyText("hello clipboard");

            string result = backend.PasteText();
            Assert.Equal("hello clipboard", result);
        });
    }

    [Fact]
    public void CopyThenPaste_PreservesUnicode()
    {
        if (!IsWindows) { return; }

        RunOrSkipIfBusy(() =>
        {
            var backend = new WindowsClipboardBackend();
            backend.CopyText("こんにちは 🌏 naïve café");

            string result = backend.PasteText();
            Assert.Equal("こんにちは 🌏 naïve café", result);
        });
    }

    [Fact]
    public void Clear_ThenPaste_ReturnsEmpty()
    {
        if (!IsWindows) { return; }

        RunOrSkipIfBusy(() =>
        {
            var backend = new WindowsClipboardBackend();
            backend.CopyText("to be cleared");
            backend.Clear();

            string result = backend.PasteText();
            Assert.Equal(string.Empty, result);
        });
    }

    [Fact]
    public void CopyEmpty_ThenPaste_ReturnsEmpty()
    {
        if (!IsWindows) { return; }

        RunOrSkipIfBusy(() =>
        {
            var backend = new WindowsClipboardBackend();
            backend.CopyText(string.Empty);

            string result = backend.PasteText();
            Assert.Equal(string.Empty, result);
        });
    }
}
