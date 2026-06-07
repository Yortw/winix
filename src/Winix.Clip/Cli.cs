#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Text;
using Yort.ShellKit;

namespace Winix.Clip;

/// <summary>
/// The result of resolving a clipboard backend for a given <c>--primary</c> setting.
/// Returned by the optional backend factory passed into <see cref="Cli.Run"/>.
/// </summary>
/// <param name="Backend">The resolved backend, or <see langword="null"/> if none was available.</param>
/// <param name="Error">Human-readable explanation when <paramref name="Backend"/> is <see langword="null"/>.</param>
public readonly record struct BackendResolution(IClipboardBackend? Backend, string? Error);

/// <summary>
/// Library-level entry point for the clip CLI. <c>Program.cs</c> is a thin shim around
/// <see cref="Run"/> that wires up <c>Console.*</c> and forwards exit codes; all
/// orchestration lives here so it can be exercised by unit tests.
/// </summary>
/// <remarks>
/// Round-1 tier-2 review extraction (TestAnalyzer C1, I1, I2). The seam exposes:
/// <list type="bullet">
///   <item>The strict-UTF-8 decode (F1 regression scenarios).</item>
///   <item>The buffer-and-decide stdin flow (F2 byte-level pin).</item>
///   <item>The <see cref="ClipboardException"/> → exit 126 mapping.</item>
/// </list>
/// to deterministic byte-level testing without spawning a process.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the clip pipeline: parse args, resolve mode + backend, dispatch to copy/paste/clear,
    /// return exit code. All side effects are routed through the supplied parameters — no
    /// references to <c>Console.*</c> inside this method.
    /// </summary>
    /// <param name="args">Command-line arguments (without the executable name).</param>
    /// <param name="payloadStdin">Raw byte <see cref="Stream"/> for stdin payload; tests inject a <see cref="MemoryStream"/>.</param>
    /// <param name="isStdinRedirected">
    /// Whether stdin is redirected (<c>Console.IsInputRedirected</c> in production).
    /// Tests pass an explicit value to exercise the buffer-and-decide branches deterministically.
    /// </param>
    /// <param name="stdout">Output writer for paste content. Must not append a newline on Write.</param>
    /// <param name="stderr">Error writer for usage errors and clipboard failures.</param>
    /// <param name="backendFactory">
    /// Optional factory that resolves the clipboard backend for a given <c>--primary</c> setting.
    /// Defaults to <see cref="ClipboardBackendFactory.Create"/> with a real
    /// <see cref="DefaultPlatformProbe"/>; tests pass a fake-backend factory.
    /// </param>
    /// <returns>Process exit code (0 success, 125 usage / invalid UTF-8, 126 clipboard busy / helper failure, 127 no helper found).</returns>
    public static int Run(
        string[] args,
        Stream payloadStdin,
        bool isStdinRedirected,
        TextWriter stdout,
        TextWriter stderr,
        Func<bool, BackendResolution>? backendFactory = null)
    {
        string version = GetVersion();
        var parser = ConfigureParser(version);
        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        ClipOptions options;
        try
        {
            options = new ClipOptions(
                forceCopy: result.Has("--copy"),
                forcePaste: result.Has("--paste"),
                clear: result.Has("--clear"),
                raw: result.Has("--raw"),
                primary: result.Has("--primary"));
        }
        catch (ArgumentException ex)
        {
            return result.WriteError(ex.Message, stderr);
        }

        // Buffer stdin only when its bytes will affect the decision: explicit --copy
        // (always read), or autodetect with redirected stdin (need bytes to decide
        // copy-vs-paste). --paste / --clear / interactive-no-flag skip the read so we
        // don't block on a TTY we don't intend to consume. The byte-level inspection
        // is the F2 fix — see notes on ModeResolver.Resolve.
        byte[] bufferedStdin = Array.Empty<byte>();
        bool needsStdinRead = options.ForceCopy
            || (!options.ForcePaste && !options.Clear && isStdinRedirected);
        if (needsStdinRead)
        {
            try
            {
                using var ms = new MemoryStream();
                payloadStdin.CopyTo(ms);
                bufferedStdin = ms.ToArray();
            }
            catch (IOException ex)
            {
                // Producer process died mid-stream, broken pipe, or other transient
                // stdin failure. Surface a friendly diagnostic rather than letting the
                // runtime print a raw stack trace. Don't pipe ex.Message under
                // InvariantGlobalization — emit the exception type as a stable
                // English discriminator. Exit code matches the ClipboardException
                // path (NotExecutable / 126) since this is a runtime failure
                // preventing the operation, not a usage error.
                stderr.WriteLine($"clip: failed to read stdin ({ex.GetType().Name})");
                return ExitCode.NotExecutable;
            }
        }

        ClipMode mode = ModeResolver.Resolve(bufferedStdin.Length > 0, options);

        backendFactory ??= DefaultBackendFactory;
        BackendResolution backendRes = backendFactory(options.Primary);
        if (backendRes.Backend is null)
        {
            stderr.WriteLine(backendRes.Error ?? "clip: unable to initialise clipboard.");
            return ExitCode.NotFound;
        }

        try
        {
            return mode switch
            {
                ClipMode.Copy => DoCopy(backendRes.Backend, bufferedStdin, stderr),
                ClipMode.Paste => DoPaste(backendRes.Backend, options.Raw, stdout),
                ClipMode.Clear => DoClear(backendRes.Backend),
                _ => result.WriteError($"internal error: unknown mode {mode}", stderr),
            };
        }
        catch (ClipboardException ex)
        {
            stderr.WriteLine($"clip: {ex.Message}");
            return ExitCode.NotExecutable;
        }
    }

    /// <summary>
    /// Configures the ShellKit parser surface for clip. Centralised here so
    /// <c>Program.cs</c> stays a thin Console-wiring shim and tests exercise the
    /// same parser the production binary uses.
    /// </summary>
    private static CommandLineParser ConfigureParser(string version)
    {
        return new CommandLineParser("clip", version)
            .Description("Cross-platform clipboard bridge — copy from stdin, paste to stdout, clear.")
            .Maturity(ToolMaturity.Core)
            .StandardFlags()
            .Flag("--copy", "-c", "Force copy mode (read stdin to clipboard), overriding autodetect")
            .Flag("--paste", "-p", "Force paste mode (read clipboard to stdout), overriding autodetect")
            .Flag("--clear", null, "Empty the clipboard, overriding autodetect")
            .Flag("--raw", "-r", "Do not strip trailing newline on paste")
            .Flag("--primary", null, "Target X11/Wayland PRIMARY selection (Linux only; ignored elsewhere)")
            .ExitCodes(
                (0, "Success (including empty-clipboard paste)"),
                (ExitCode.UsageError, "Invalid flags / conflicting modes / invalid UTF-8 input"),
                (ExitCode.NotExecutable, "Clipboard busy or helper failure"),
                (ExitCode.NotFound, "No clipboard helper found (Linux only)"))
            .Platform("cross-platform",
                replaces: new[] { "clip.exe", "pbcopy", "pbpaste", "xclip", "xsel", "wl-copy", "wl-paste" },
                valueOnWindows: "Windows clip.exe is write-only — this adds paste and clear",
                valueOnUnix: "Normalises xclip/xsel/wl-copy/pbcopy behaviour behind one command")
            .StdinDescription("Content to copy when stdin is redirected with content, or when -c is passed")
            .StdoutDescription("Clipboard contents on paste; nothing on copy or clear")
            .StderrDescription("Error messages only")
            .Example("clip", "Paste clipboard contents to stdout")
            .Example("echo hello | clip", "Copy 'hello' to the clipboard (trailing newline preserved in copy)")
            .Example("clip --clear", "Empty the clipboard")
            .Example("clip -r > out.txt", "Paste without stripping trailing newline")
            .Example("clip --primary", "Paste from the Linux PRIMARY selection (middle-click buffer)")
            .Example("echo hi | clip --primary", "Copy to the Linux PRIMARY selection (applies to copy and clear too)")
            .ComposesWith("ids", "ids | clip", "Generate an ID and copy it to the clipboard")
            .ComposesWith("digest", "digest report.txt | clip", "Copy a file hash to the clipboard (sha256 default)")
            .ComposesWith("qr", "qr \"https://example.com\" --format svg | clip", "Copy an SVG-encoded QR payload to the clipboard");
    }

    private static int DoCopy(IClipboardBackend backend, byte[] stdinBytes, TextWriter stderr)
    {
        if (!StrictUtf8Decoder.TryDecode(stdinBytes, out string content))
        {
            stderr.WriteLine("clip: invalid UTF-8 in input.");
            return ExitCode.UsageError;
        }

        backend.CopyText(content);
        return 0;
    }

    private static int DoPaste(IClipboardBackend backend, bool raw, TextWriter stdout)
    {
        string content = backend.PasteText();
        if (!raw)
        {
            content = NewlineStripping.StripTrailingNewline(content) ?? string.Empty;
        }

        stdout.Write(content);
        stdout.Flush();
        return 0;
    }

    private static int DoClear(IClipboardBackend backend)
    {
        backend.Clear();
        return 0;
    }

    private static BackendResolution DefaultBackendFactory(bool primary)
    {
        IClipboardBackend? backend = ClipboardBackendFactory.Create(
            new DefaultPlatformProbe(),
            primary,
            out string? error);
        return new BackendResolution(backend, error);
    }

    private static string GetVersion()
    {
        AssemblyInformationalVersionAttribute? attr = typeof(Cli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string raw = attr?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }
}
