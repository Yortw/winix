using System.Reflection;
using System.Text;
using Winix.Clip;
using Yort.ShellKit;

namespace Clip;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        string version = GetVersion();

        var parser = new CommandLineParser("clip", version)
            .Description("Cross-platform clipboard bridge — copy from stdin, paste to stdout, clear.")
            .StandardFlags()
            .Flag("--copy", "-c", "Force copy mode (read stdin to clipboard), overriding autodetect")
            .Flag("--paste", "-p", "Force paste mode (read clipboard to stdout), overriding autodetect")
            .Flag("--clear", null, "Empty the clipboard")
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
            .Example("clip --primary", "Paste the Linux PRIMARY selection (middle-click)")
            .ComposesWith("ids", "ids | clip", "Generate an ID and copy it to the clipboard")
            .ComposesWith("digest", "digest sha256 file | clip", "Copy a file hash to the clipboard");

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

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
            return result.WriteError(ex.Message, Console.Error);
        }

        // Tier-2 re-verification 2026-05-06 finding F2: previously this passed
        // Console.IsInputRedirected directly to ModeResolver. Under Git Bash / MSYS,
        // IsInputRedirected always reports true (MSYS pipes stdin even for interactive
        // terminals), so bare `clip` auto-detected as copy mode, read empty stdin, and
        // silently emptied the user's clipboard. Buffering stdin first turns the
        // auto-detection predicate into "did the user actually send any bytes?" — accurate
        // across native Windows shells, Git Bash, Linux, and macOS uniformly.
        //
        // Read stdin in two cases:
        //  - explicit -c (always honour the user's request, even on a terminal — they get
        //    the "type-then-Ctrl+D" interactive copy model);
        //  - auto-detect with redirected stdin (need bytes to decide mode).
        // ForcePaste / Clear / interactive-no-flag skip the read so we don't block on a
        // terminal we don't intend to consume.
        byte[] bufferedStdin = System.Array.Empty<byte>();
        bool needsStdinRead = options.ForceCopy
            || (!options.ForcePaste && !options.Clear && Console.IsInputRedirected);
        if (needsStdinRead)
        {
            using var ms = new MemoryStream();
            Console.OpenStandardInput().CopyTo(ms);
            bufferedStdin = ms.ToArray();
        }

        ClipMode mode = ModeResolver.Resolve(bufferedStdin.Length > 0, options);

        IClipboardBackend? backend = ClipboardBackendFactory.Create(
            new DefaultPlatformProbe(), options.Primary, out string? factoryError);
        if (backend is null)
        {
            Console.Error.WriteLine(factoryError ?? "clip: unable to initialise clipboard.");
            return ExitCode.NotFound;
        }

        try
        {
            return mode switch
            {
                ClipMode.Copy => DoCopy(backend, bufferedStdin),
                ClipMode.Paste => DoPaste(backend, options.Raw),
                ClipMode.Clear => DoClear(backend),
                _ => result.WriteError($"internal error: unknown mode {mode}", Console.Error),
            };
        }
        catch (ClipboardException ex)
        {
            Console.Error.WriteLine($"clip: {ex.Message}");
            return ExitCode.NotExecutable;
        }
    }

    private static int DoCopy(IClipboardBackend backend, byte[] stdinBytes)
    {
        // Strict UTF-8 decode of the buffered stdin bytes. We avoid StreamReader because
        // its default `detectEncodingFromByteOrderMarks: true` would silently switch
        // decoders on UTF-16 LE/BE / UTF-8 BOM and bypass the throwOnInvalidBytes guard
        // (tier-2 re-verification 2026-05-06 finding F1 — superseded by this F2 refactor
        // which removes the StreamReader entirely). GetString on a strict UTF8Encoding
        // throws DecoderFallbackException on any non-UTF-8 byte sequence, exactly the
        // contract README and --help advertise.
        string content;
        try
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            content = encoding.GetString(stdinBytes);
        }
        catch (DecoderFallbackException)
        {
            Console.Error.WriteLine("clip: invalid UTF-8 in input.");
            return ExitCode.UsageError;
        }

        backend.CopyText(content);
        return 0;
    }

    private static int DoPaste(IClipboardBackend backend, bool raw)
    {
        string content = backend.PasteText();
        if (!raw)
        {
            content = NewlineStripping.StripTrailingNewline(content) ?? string.Empty;
        }

        using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        writer.Write(content);
        return 0;
    }

    private static int DoClear(IClipboardBackend backend)
    {
        backend.Clear();
        return 0;
    }

    private static string GetVersion()
    {
        var attr = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string raw = attr?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }

}
