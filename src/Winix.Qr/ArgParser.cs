#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Winix.QrCode;
using Yort.ShellKit;

namespace Winix.Qr;

/// <summary>
/// Parses argv into <see cref="QrOptions"/>. Dispatches on subcommand (wifi/sms/mailto/geo/tel) or treats
/// the first positional as a text payload (default case). Uses one <see cref="CommandLineParser"/> per
/// subcommand so that <c>qr wifi --help</c> / <c>qr wifi --describe</c> emit wifi-specific output.
/// </summary>
public static class ArgParser
{
    /// <summary>
    /// Parse outcome. Either <see cref="Options"/> is set (success), <see cref="Error"/> is set (usage error),
    /// or <see cref="IsHandled"/> is true (ShellKit already printed help/version/describe and the caller
    /// should exit with <see cref="ExitCode"/>). <see cref="ReadStdin"/> is a text-mode-only flag meaning
    /// "no positional payload was supplied" — Program.cs decides whether stdin is actually readable.
    /// </summary>
    public sealed record Result(
        QrOptions? Options,
        string? Error,
        bool ReadStdin,
        bool IsHandled,
        int ExitCode,
        bool UseColor);

    private static readonly HashSet<string> SubcommandKeywords = new(StringComparer.Ordinal)
    {
        "wifi", "sms", "mailto", "geo", "tel",
    };

    /// <summary>Parse argv.</summary>
    public static Result Parse(IReadOnlyList<string> argv)
    {
        // Subcommand selection: first token wins if it matches a known keyword. Everything else falls through
        // to text mode, where the first non-flag positional is the payload.
        SubCommand sc = SubCommand.Text;
        int startIdx = 0;
        if (argv.Count > 0 && SubcommandKeywords.Contains(argv[0]))
        {
            sc = argv[0] switch
            {
                "wifi"   => SubCommand.Wifi,
                "sms"    => SubCommand.Sms,
                "mailto" => SubCommand.Mailto,
                "geo"    => SubCommand.Geo,
                "tel"    => SubCommand.Tel,
                _ => SubCommand.Text,
            };
            startIdx = 1;
        }

        // In text mode, a bare "-" positional means "read stdin". CommandLineParser treats anything starting
        // with '-' as a flag, so strip and remember locally — same pattern as digest.
        (string[] argsForParser, bool sawStdinDash) = ExtractBareDashForTextMode(argv, startIdx, sc);

        CommandLineParser parser = BuildParser(sc);
        ParseResult parsed = parser.Parse(argsForParser);

        bool useColor = parsed.ResolveColor(checkStdErr: false);

        if (parsed.IsHandled)
        {
            return new Result(null, null, false, true, parsed.ExitCode, useColor);
        }
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0], useColor);
        }

        // Resolve render options (apply to every subcommand).
        OutputFormat format = OutputFormat.Auto;
        if (parsed.Has("--format"))
        {
            string raw = parsed.GetString("--format");
            if (!TryParseFormat(raw, out format))
            {
                return Fail($"unknown --format value: {raw}", useColor);
            }
        }

        EccLevel ecc = EccLevel.M;
        if (parsed.Has("--error-correction"))
        {
            string raw = parsed.GetString("--error-correction");
            if (!TryParseEcc(raw, out ecc))
            {
                return Fail($"unknown --error-correction value: {raw}", useColor);
            }
        }

        int pixels = parsed.Has("--size") ? parsed.GetInt("--size") : 10;
        bool noMargin = parsed.Has("--no-margin");
        string? outputPath = parsed.Has("--output") ? parsed.GetString("--output") : null;
        bool forceBinary = parsed.Has("--force-binary");
        bool forceOverwrite = parsed.Has("--force");

        // Subcommand-specific fields. Each BuildParser_* registers only the flags valid for that subcommand,
        // so GetString / Has will only succeed here for the right subcommand — no risk of reading a wifi flag
        // in sms mode because wifi flags aren't registered on the sms parser.
        string? textPayload = null;
        bool readStdin = false;
        string? wifiSsid = null, wifiPassword = null, wifiSecurity = null;
        bool wifiHidden = false;
        string? smsNumber = null, smsMessage = null;
        string? mailtoTo = null, mailtoSubject = null, mailtoBody = null, mailtoCc = null, mailtoBcc = null;
        double? geoLat = null, geoLon = null;
        string? geoQuery = null;
        string? telNumber = null;

        switch (sc)
        {
            case SubCommand.Text:
                if (parsed.Positionals.Length > 1)
                {
                    return Fail($"unexpected positional argument: {parsed.Positionals[1]}", useColor);
                }
                if (parsed.Positionals.Length == 1)
                {
                    textPayload = parsed.Positionals[0];
                }
                else
                {
                    // Either "-" was given (sawStdinDash), or no positional at all. Both signal "read stdin" —
                    // Program.cs short-circuits if stdin is a TTY.
                    readStdin = true;
                }
                break;
            case SubCommand.Wifi:
                if (parsed.Positionals.Length > 0)
                {
                    return Fail("wifi does not accept positional arguments", useColor);
                }
                wifiSsid = parsed.Has("--ssid") ? parsed.GetString("--ssid") : null;
                wifiPassword = parsed.Has("--password") ? parsed.GetString("--password") : null;
                wifiSecurity = parsed.Has("--security") ? parsed.GetString("--security") : null;
                wifiHidden = parsed.Has("--hidden");
                if (string.IsNullOrEmpty(wifiSsid))
                {
                    return Fail("wifi: missing required --ssid", useColor);
                }
                break;
            case SubCommand.Sms:
                if (parsed.Positionals.Length > 0)
                {
                    return Fail("sms does not accept positional arguments", useColor);
                }
                smsNumber = parsed.Has("--number") ? parsed.GetString("--number") : null;
                smsMessage = parsed.Has("--message") ? parsed.GetString("--message") : null;
                if (string.IsNullOrEmpty(smsNumber))
                {
                    return Fail("sms: missing required --number", useColor);
                }
                break;
            case SubCommand.Mailto:
                if (parsed.Positionals.Length > 0)
                {
                    return Fail("mailto does not accept positional arguments", useColor);
                }
                mailtoTo = parsed.Has("--to") ? parsed.GetString("--to") : null;
                mailtoSubject = parsed.Has("--subject") ? parsed.GetString("--subject") : null;
                mailtoBody = parsed.Has("--body") ? parsed.GetString("--body") : null;
                mailtoCc = parsed.Has("--cc") ? parsed.GetString("--cc") : null;
                mailtoBcc = parsed.Has("--bcc") ? parsed.GetString("--bcc") : null;
                if (string.IsNullOrEmpty(mailtoTo))
                {
                    return Fail("mailto: missing required --to", useColor);
                }
                break;
            case SubCommand.Geo:
                if (parsed.Positionals.Length > 0)
                {
                    return Fail("geo does not accept positional arguments", useColor);
                }
                if (parsed.Has("--lat")) { geoLat = parsed.GetDouble("--lat"); }
                if (parsed.Has("--lon")) { geoLon = parsed.GetDouble("--lon"); }
                geoQuery = parsed.Has("--query") ? parsed.GetString("--query") : null;
                if (geoLat is null || geoLon is null)
                {
                    return Fail("geo: missing required --lat and/or --lon", useColor);
                }
                break;
            case SubCommand.Tel:
                if (parsed.Positionals.Length > 0)
                {
                    return Fail("tel does not accept positional arguments", useColor);
                }
                telNumber = parsed.Has("--number") ? parsed.GetString("--number") : null;
                if (string.IsNullOrEmpty(telNumber))
                {
                    return Fail("tel: missing required --number", useColor);
                }
                break;
        }

        QrOptions opts = new(
            SubCommand: sc,
            TextPayload: textPayload,
            Format: format,
            PixelsPerModule: pixels,
            Ecc: ecc,
            NoMargin: noMargin,
            OutputPath: outputPath,
            ForceBinary: forceBinary,
            ForceOverwrite: forceOverwrite,
            WifiSsid: wifiSsid, WifiPassword: wifiPassword, WifiSecurity: wifiSecurity, WifiHidden: wifiHidden,
            SmsNumber: smsNumber, SmsMessage: smsMessage,
            MailtoTo: mailtoTo, MailtoSubject: mailtoSubject, MailtoBody: mailtoBody, MailtoCc: mailtoCc, MailtoBcc: mailtoBcc,
            GeoLat: geoLat, GeoLon: geoLon, GeoQuery: geoQuery,
            TelNumber: telNumber);

        return new Result(opts, null, readStdin, false, 0, useColor);
    }

    private static CommandLineParser BuildParser(SubCommand sc)
    {
        string version = ResolveVersion();

        CommandLineParser p = sc switch
        {
            SubCommand.Wifi   => BuildWifiParser(version),
            SubCommand.Sms    => BuildSmsParser(version),
            SubCommand.Mailto => BuildMailtoParser(version),
            SubCommand.Geo    => BuildGeoParser(version),
            SubCommand.Tel    => BuildTelParser(version),
            _                 => BuildTextParser(version),
        };
        return p;
    }

    private static CommandLineParser BuildTextParser(string version)
    {
        return new CommandLineParser("qr", version)
            .Description("Cross-platform QR code generator with helpers for Wi-Fi, SMS, mailto, geo, and tel. Default mode encodes a text payload from a positional argument or stdin.")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "qrencode", "python qrcode", "online QR generators" },
                valueOnWindows: "Native gap-fill — Windows has no built-in QR generator; single-binary AOT avoids requiring Python or a Cygwin-built qrencode.",
                valueOnUnix: "One tool covering unicode terminal output, ascii for no-UTF8 terminals, SVG and PNG, plus structured helpers (Wi-Fi / SMS / mailto / geo / tel) missing from qrencode.")
            .ExitCodes(
                (0, "Success"),
                (ExitCode.UsageError, "Usage error: bad flags, missing required field for a helper subcommand, helper grammar violation (e.g. tel/sms with letters), --format vs --output extension contradiction, refusing to overwrite an existing --output file without --force, PNG to TTY without --force-binary"),
                (ExitCode.NotExecutable, "Runtime error: payload exceeds QR capacity (try --error-correction l), stdin not valid UTF-8, output write failed (parent missing, permission denied, etc.)"))
            .StdinDescription("Text payload to encode when no positional is given, or when '-' is passed.")
            .StdoutDescription("Rendered QR code (unicode/ascii/svg by default; bytes for PNG unless --output).")
            .StderrDescription("Errors, capacity-overflow hints, and diagnostic messages.")
            .Positional("PAYLOAD")
            .Option("--format", null, "FMT", "Output format: auto, unicode, ascii, svg, png. Default auto (unicode on TTY, svg when stdout redirected).")
            .IntOption("--size", "-s", "PIXELS", "Pixels per module (PNG/SVG only). Default 10.",
                v => v > 0 ? null : "must be a positive integer")
            .Option("--error-correction", "-e", "LEVEL", "Error-correction level: l (~7%), m (~15%, default), q (~25%), h (~30%).")
            .Flag("--no-margin", "Strip the 4-module quiet zone.")
            .Option("--output", "-o", "PATH", "Write to file instead of stdout.")
            .Flag("--force-binary", "Allow PNG output to a TTY.")
            .Flag("--force", "Overwrite an existing --output file (refused by default).")
            .Example("qr 'Hello, world'", "Encode text, render unicode QR to the terminal")
            .Example("echo payload | qr", "Read payload from stdin (no positional)")
            .Example("qr 'https://example.com' --format svg -o code.svg", "SVG file output")
            .Example("qr 'https://example.com' --format png --size 16 -o code.png", "PNG with 16 pixels/module")
            .Example("qr wifi --ssid HomeNet --password s3cr3t --security wpa2", "Wi-Fi helper subcommand")
            .Example("qr sms --number +15551234 --message 'Hi'", "SMS helper subcommand")
            .Example("qr mailto --to a@b.com --subject 'Hello'", "Mailto helper subcommand")
            .Example("qr geo --lat -41.2924 --lon 174.7787 --query Wellington", "Geo helper subcommand")
            .Example("qr tel --number +15551234", "Tel helper subcommand")
            .ComposesWith("pipe", "echo 'secret' | qr --format png -o secret.png", "Pipe any text producer into qr for encoding")
            .ComposesWith("clip", "clip --paste | qr", "Encode the clipboard contents as a QR code")
            .Section("Subcommands",
                "qr wifi   [OPTIONS] --ssid S [--password P] [--security wpa2|wpa|wep|nopass] [--hidden]\n" +
                "qr sms    [OPTIONS] --number N [--message M]\n" +
                "qr mailto [OPTIONS] --to A [--subject S] [--body B] [--cc A] [--bcc A]\n" +
                "qr geo    [OPTIONS] --lat L --lon L [--query Q]\n" +
                "qr tel    [OPTIONS] --number N\n\n" +
                "Run 'qr SUBCOMMAND --help' for subcommand-specific flags.");
    }

    private static CommandLineParser BuildWifiParser(string version)
    {
        CommandLineParser p = BuildHelperBase("qr wifi", version,
            "Encode a Wi-Fi network configuration as a QR code (WIFI: URI scheme). Scanning joins the network on compatible phones.");
        return p
            .Option("--ssid", null, "SSID", "Wi-Fi network name (required).")
            .Option("--password", null, "PW", "Network password (optional for open networks).")
            .Option("--security", null, "TYPE", "Security mode: wpa2 (default), wpa, wep, nopass.")
            .Flag("--hidden", "Mark the network as hidden.")
            .Example("qr wifi --ssid HomeNet --password s3cr3t --security wpa2", "WPA2-protected network")
            .Example("qr wifi --ssid Guest --security nopass", "Open network (no password)")
            .Example("qr wifi --ssid Secret --password pw --hidden", "Hidden SSID")
            .Example("qr wifi --ssid HomeNet --password pw --format svg -o wifi.svg", "Write to SVG file");
    }

    private static CommandLineParser BuildSmsParser(string version)
    {
        CommandLineParser p = BuildHelperBase("qr sms", version,
            "Encode an SMS message intent as a QR code (SMSTO: URI scheme). Scanning opens the SMS composer pre-filled with the number and message.");
        return p
            .Option("--number", null, "PHONE", "Destination phone number (E.164 recommended, required).")
            .Option("--message", null, "TEXT", "Pre-filled message body.")
            .Example("qr sms --number +15551234 --message 'Hi'", "SMS with pre-filled message")
            .Example("qr sms --number +6421555123", "SMS number only, blank message");
    }

    private static CommandLineParser BuildMailtoParser(string version)
    {
        CommandLineParser p = BuildHelperBase("qr mailto", version,
            "Encode a mailto URI as a QR code. Scanning opens the email composer pre-filled with addressee, subject, and body.");
        return p
            .Option("--to", null, "EMAIL", "Addressee (required).")
            .Option("--subject", null, "SUBJECT", "Pre-filled subject line.")
            .Option("--body", null, "BODY", "Pre-filled message body.")
            .Option("--cc", null, "EMAIL", "Carbon-copy addressee.")
            .Option("--bcc", null, "EMAIL", "Blind carbon-copy addressee.")
            .Example("qr mailto --to a@b.com --subject 'Hi' --body 'hello'", "Pre-filled email")
            .Example("qr mailto --to a@b.com --cc c@d.com --bcc e@f.com", "With cc/bcc");
    }

    private static CommandLineParser BuildGeoParser(string version)
    {
        CommandLineParser p = BuildHelperBase("qr geo", version,
            "Encode a geographic coordinate as a QR code (geo: URI scheme). Scanning opens the maps app at the given coordinate.");
        return p
            .DoubleOption("--lat", null, "DEGREES", "Latitude in decimal degrees (required, -90 to 90).",
                v => v >= -90 && v <= 90 ? null : "must be between -90 and 90")
            .DoubleOption("--lon", null, "DEGREES", "Longitude in decimal degrees (required, -180 to 180).",
                v => v >= -180 && v <= 180 ? null : "must be between -180 and 180")
            .Option("--query", null, "NAME", "Optional placemark label shown above the coordinate in some maps apps.")
            .Example("qr geo --lat -41.2924 --lon 174.7787 --query Wellington", "Coordinate with a named placemark")
            .Example("qr geo --lat 48.8566 --lon 2.3522", "Plain coordinate");
    }

    private static CommandLineParser BuildTelParser(string version)
    {
        CommandLineParser p = BuildHelperBase("qr tel", version,
            "Encode a telephone number as a QR code (tel: URI scheme). Scanning opens the dialer pre-filled.");
        return p
            .Option("--number", null, "PHONE", "Phone number (E.164 recommended, required).")
            .Example("qr tel --number +15551234", "Phone number (scanning opens the dialer)");
    }

    // Shared shape for every helper subcommand: description, standard flags, shared render options, exit codes,
    // io descriptions, and platform metadata. Each helper parser then chains its own flags and examples.
    private static CommandLineParser BuildHelperBase(string toolName, string version, string description)
    {
        return new CommandLineParser(toolName, version)
            .Description(description)
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "qrencode (lacks structured helpers)", "python qrcode", "manual URI-scheme encoding" },
                valueOnWindows: "Native gap-fill — Windows has no built-in QR generator and no structured URI helpers.",
                valueOnUnix: "Structured helpers for Wi-Fi / SMS / mailto / geo / tel are missing from qrencode and most alternatives.")
            .ExitCodes(
                (0, "Success"),
                (ExitCode.UsageError, "Usage error: missing required field, bad flag value, helper grammar violation (e.g. tel/sms with letters or out-of-range geo coordinates), --format vs --output extension contradiction, refusing to overwrite an existing --output file without --force"),
                (ExitCode.NotExecutable, "Runtime error: payload exceeds QR capacity (try --error-correction l), output write failed (parent missing, permission denied, etc.)"))
            .StdinDescription("Not used by helper subcommands.")
            .StdoutDescription("Rendered QR code (unicode/ascii/svg by default; bytes for PNG unless --output).")
            .StderrDescription("Errors and diagnostic messages.")
            .Option("--format", null, "FMT", "Output format: auto, unicode, ascii, svg, png. Default auto (unicode on TTY, svg when stdout redirected).")
            .IntOption("--size", "-s", "PIXELS", "Pixels per module (PNG/SVG only). Default 10.",
                v => v > 0 ? null : "must be a positive integer")
            .Option("--error-correction", "-e", "LEVEL", "Error-correction level: l (~7%), m (~15%, default), q (~25%), h (~30%).")
            .Flag("--no-margin", "Strip the 4-module quiet zone.")
            .Option("--output", "-o", "PATH", "Write to file instead of stdout.")
            .Flag("--force-binary", "Allow PNG output to a TTY.")
            .Flag("--force", "Overwrite an existing --output file (refused by default).");
    }

    private static (string[] Args, bool SawDash) ExtractBareDashForTextMode(IReadOnlyList<string> argv, int startIdx, SubCommand sc)
    {
        if (sc != SubCommand.Text)
        {
            // Helper subcommands don't accept bare "-"; pass the slice through as-is.
            string[] slice = new string[argv.Count - startIdx];
            for (int i = 0; i < slice.Length; i++) { slice[i] = argv[startIdx + i]; }
            return (slice, false);
        }

        List<string> acc = new(argv.Count);
        bool sawDash = false;
        for (int i = startIdx; i < argv.Count; i++)
        {
            if (argv[i] == "-")
            {
                sawDash = true;
                continue;
            }
            acc.Add(argv[i]);
        }
        return (acc.ToArray(), sawDash);
    }

    private static bool TryParseFormat(string s, out OutputFormat f)
    {
        switch (s)
        {
            case "auto":    f = OutputFormat.Auto;    return true;
            case "unicode": f = OutputFormat.Unicode; return true;
            case "ascii":   f = OutputFormat.Ascii;   return true;
            case "svg":     f = OutputFormat.Svg;     return true;
            case "png":     f = OutputFormat.Png;     return true;
            default:        f = OutputFormat.Auto;    return false;
        }
    }

    private static bool TryParseEcc(string s, out EccLevel e)
    {
        switch (s)
        {
            case "l": e = EccLevel.L; return true;
            case "m": e = EccLevel.M; return true;
            case "q": e = EccLevel.Q; return true;
            case "h": e = EccLevel.H; return true;
            default:  e = EccLevel.M; return false;
        }
    }

    // Read AssemblyInformationalVersion (injected via /p:Version by the release pipeline) and strip the
    // "+gitsha" SourceLink suffix. Falls back to AssemblyVersion for dev builds. Matches digest/ids/notify.
    private static string ResolveVersion()
    {
        string? informational = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            int plus = informational.IndexOf('+');
            return plus >= 0 ? informational.Substring(0, plus) : informational;
        }
        return typeof(ArgParser).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static Result Fail(string msg, bool useColor)
        => new(null, msg, false, false, 0, useColor);
}
