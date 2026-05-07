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

    // The Windows clipboard is a single-owner resource. Brief contention from clipboard
    // managers (Windows Clipboard History, Ditto), CI VM cold-start resource spikes, or
    // other monitoring tooling is normal and transient. Persistent contention is
    // environmental and the test should fail loudly to surface it — silent Skip would
    // hide the fact that 'clip' itself can't reliably run in this environment.
    //
    // Round-5 fresh-eyes SFH I1 + Round-6 follow-up: rather than Skip-on-busy (which
    // hides flakes silently) or Fail-on-first-busy (which over-flakes on cold CI VMs
    // with transient spikes), retry the full operation up to maxAttempts times. Each
    // attempt gets a fresh production retry budget (20×50ms = 1s). Every attempt is
    // logged loudly to ITestOutputHelper so CI maintainers see flake patterns even when
    // tests eventually pass. Final failure is XunitException (proper test failure) with
    // a message that explicitly attributes the cause to environment, so investigation
    // starts with the runner not the code.
    private void RunOrFailAfterRetries(Action body, int maxAttempts = 3)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                body();
                if (attempt > 1)
                {
                    _output.WriteLine($"[succeeded on attempt {attempt}/{maxAttempts}]");
                }
                return;
            }
            catch (ClipboardException ex) when (ex.Message.Contains("busy", StringComparison.Ordinal))
            {
                _output.WriteLine($"[attempt {attempt}/{maxAttempts}] clipboard busy: {ex.Message}");
                if (attempt == maxAttempts)
                {
                    throw new Xunit.Sdk.XunitException(
                        $"Clipboard remained busy across {maxAttempts} attempts " +
                        $"(production retry budget {ClipboardRetryPolicy.OpenAttempts}×{ClipboardRetryPolicy.OpenRetryDelayMs}ms each, " +
                        $"~{maxAttempts * ClipboardRetryPolicy.OpenAttempts * ClipboardRetryPolicy.OpenRetryDelayMs / 1000}s total). " +
                        $"Likely environmental: a clipboard manager (Ditto, Windows Clipboard History), " +
                        $"remote-desktop redirection, or another tool is holding the resource. Last error: {ex.Message}");
                }
            }
        }
    }

    [SkippableFact]
    public void CopyThenPaste_RoundTripsText()
    {
        Skip.IfNot(IsWindows, "Windows-only — exercises Windows clipboard backend.");
        if (!IsWindows) { return; } // redundant, satisfies CA1416 analyzer

        RunOrFailAfterRetries(() =>
        {
            var backend = new WindowsClipboardBackend();
            backend.CopyText("hello clipboard");

            string result = backend.PasteText();
            Assert.Equal("hello clipboard", result);
        });
    }

    [SkippableFact]
    public void CopyThenPaste_PreservesUnicode()
    {
        Skip.IfNot(IsWindows, "Windows-only — exercises Windows clipboard backend.");
        if (!IsWindows) { return; } // redundant, satisfies CA1416 analyzer

        RunOrFailAfterRetries(() =>
        {
            var backend = new WindowsClipboardBackend();
            backend.CopyText("こんにちは 🌏 naïve café");

            string result = backend.PasteText();
            Assert.Equal("こんにちは 🌏 naïve café", result);
        });
    }

    [SkippableFact]
    public void Clear_ThenPaste_ReturnsEmpty()
    {
        Skip.IfNot(IsWindows, "Windows-only — exercises Windows clipboard backend.");
        if (!IsWindows) { return; } // redundant, satisfies CA1416 analyzer

        RunOrFailAfterRetries(() =>
        {
            var backend = new WindowsClipboardBackend();
            backend.CopyText("to be cleared");
            backend.Clear();

            string result = backend.PasteText();
            Assert.Equal(string.Empty, result);
        });
    }

    [SkippableFact]
    public void CopyEmpty_ThenPaste_ReturnsEmpty()
    {
        Skip.IfNot(IsWindows, "Windows-only — exercises Windows clipboard backend.");
        if (!IsWindows) { return; } // redundant, satisfies CA1416 analyzer

        RunOrFailAfterRetries(() =>
        {
            var backend = new WindowsClipboardBackend();
            backend.CopyText(string.Empty);

            string result = backend.PasteText();
            Assert.Equal(string.Empty, result);
        });
    }
}
