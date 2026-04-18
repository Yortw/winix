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
            .StdinDescription("Content to copy when stdin is redirected or -c is passed")
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

        ClipMode mode = ModeResolver.Resolve(Console.IsInputRedirected, options);

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
                ClipMode.Copy => DoCopy(backend),
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

    private static int DoCopy(IClipboardBackend backend)
    {
        string content;
        try
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, throwOnInvalidBytes: true));
            content = reader.ReadToEnd();
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
