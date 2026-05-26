namespace Winix.Clip;

/// <summary>
/// Chooses the right <see cref="IClipboardBackend"/> for the current platform.
/// Returns <c>null</c> when no backend is possible (e.g. Linux with no helper installed);
/// the console app is expected to surface that as an exit-127 error with the
/// returned hint message.
/// </summary>
public static class ClipboardBackendFactory
{
    /// <summary>
    /// Creates a backend. On failure, returns <c>null</c> and writes a human-readable
    /// reason to <paramref name="error"/>. A non-null return always has a null error.
    /// Optionally inject a custom <see cref="IProcessRunner"/> for testing; when null,
    /// a <see cref="DefaultProcessRunner"/> is used.
    /// </summary>
    public static IClipboardBackend? Create(IPlatformProbe probe, bool primary, out string? error, IProcessRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(probe);

        runner ??= new DefaultProcessRunner();
        error = null;

        switch (probe.Os)
        {
            case ClipPlatform.Windows:
                // --primary has no meaning on Windows; flag is silently ignored.
                return new WindowsClipboardBackend();

            case ClipPlatform.MacOS:
                // pbcopy/pbpaste are always present on macOS; no probing needed.
                return new ShellOutClipboardBackend(
                    primary ? HelperSets.Pb.WithPrimary() : HelperSets.Pb,
                    runner);

            case ClipPlatform.Linux:
                return CreateLinux(probe, primary, runner, out error);

            default:
                error = "clip: unsupported platform.";
                return null;
        }
    }

    private static IClipboardBackend? CreateLinux(IPlatformProbe probe, bool primary, IProcessRunner runner, out string? error)
    {
        error = null;

        bool wayland = !string.IsNullOrEmpty(probe.GetEnv("WAYLAND_DISPLAY"));

        if (wayland && probe.HasBinary("wl-copy"))
        {
            return new ShellOutClipboardBackend(
                primary ? HelperSets.WlClipboard.WithPrimary() : HelperSets.WlClipboard,
                runner);
        }

        if (probe.HasBinary("xclip"))
        {
            return new ShellOutClipboardBackend(
                primary ? HelperSets.XClip.WithPrimary() : HelperSets.XClip,
                runner);
        }

        if (probe.HasBinary("xsel"))
        {
            return new ShellOutClipboardBackend(
                primary ? HelperSets.XSel.WithPrimary() : HelperSets.XSel,
                runner);
        }

        error = "clip: no clipboard helper found — install wl-clipboard, xclip, or xsel.";
        return null;
    }
}
