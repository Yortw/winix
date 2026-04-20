#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Winix.QrCode;

namespace Winix.Qr;

/// <summary>
/// Parses argv into <see cref="QrOptions"/>. Dispatches on subcommand (wifi/sms/mailto/geo/tel) or treats
/// the first positional as a text payload (default case).
/// </summary>
public static class ArgParser
{
    /// <summary>Outcome of parsing. Either <see cref="Options"/> is non-null (success) or <see cref="Error"/> is non-null (usage failure).</summary>
    public sealed record Result(
        QrOptions? Options,
        string? Error,
        bool ReadStdin,
        bool ShowHelp,
        bool ShowVersion,
        bool ShowDescribe);

    private static readonly HashSet<string> Subcommands = new(StringComparer.Ordinal)
    {
        "wifi", "sms", "mailto", "geo", "tel",
    };

    /// <summary>Parse argv.</summary>
    public static Result Parse(IReadOnlyList<string> argv)
    {
        // Quick pre-scan for --help / --version / --describe at any position.
        foreach (string a in argv)
        {
            if (a == "--help" || a == "-h")
            {
                return new Result(null, null, false, true, false, false);
            }
            if (a == "--version")
            {
                return new Result(null, null, false, false, true, false);
            }
            if (a == "--describe")
            {
                return new Result(null, null, false, false, false, true);
            }
        }

        SubCommand sc = SubCommand.Text;
        int startIdx = 0;

        if (argv.Count > 0 && Subcommands.Contains(argv[0]))
        {
            sc = argv[0] switch
            {
                "wifi"   => SubCommand.Wifi,
                "sms"    => SubCommand.Sms,
                "mailto" => SubCommand.Mailto,
                "geo"    => SubCommand.Geo,
                "tel"    => SubCommand.Tel,
                _ => throw new InvalidOperationException("unreachable"),
            };
            startIdx = 1;
        }

        // Default option values.
        string? textPayload = null;
        bool readStdin = false;
        OutputFormat format = OutputFormat.Auto;
        int pixels = 10;
        EccLevel ecc = EccLevel.M;
        bool noMargin = false;
        string? outputPath = null;
        bool forceBinary = false;

        string? wifiSsid = null, wifiPassword = null, wifiSecurity = null;
        bool wifiHidden = false;
        string? smsNumber = null, smsMessage = null;
        string? mailtoTo = null, mailtoSubject = null, mailtoBody = null, mailtoCc = null, mailtoBcc = null;
        double? geoLat = null, geoLon = null;
        string? geoQuery = null;
        string? telNumber = null;

        bool positionalSeen = false;

        for (int i = startIdx; i < argv.Count; i++)
        {
            string a = argv[i];

            // Global flags.
            switch (a)
            {
                case "--format":
                    if (++i >= argv.Count)
                    {
                        return Err("--format requires a value");
                    }
                    if (!TryParseFormat(argv[i], out format))
                    {
                        return Err($"unknown --format value: {argv[i]}");
                    }
                    continue;
                case "--error-correction":
                case "-e":
                    if (++i >= argv.Count)
                    {
                        return Err($"{a} requires a value");
                    }
                    if (!TryParseEcc(argv[i], out ecc))
                    {
                        return Err($"unknown --error-correction value: {argv[i]}");
                    }
                    continue;
                case "--size":
                case "-s":
                    if (++i >= argv.Count)
                    {
                        return Err($"{a} requires a value");
                    }
                    if (!int.TryParse(argv[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out pixels) || pixels <= 0)
                    {
                        return Err("--size must be a positive integer");
                    }
                    continue;
                case "--no-margin":
                    noMargin = true;
                    continue;
                case "--output":
                case "-o":
                    if (++i >= argv.Count)
                    {
                        return Err($"{a} requires a value");
                    }
                    outputPath = argv[i];
                    continue;
                case "--force-binary":
                    forceBinary = true;
                    continue;
                case "--color":
                case "--no-color":
                    // Respected by ConsoleEnv at Program.cs level; no options-side effect.
                    continue;
            }

            // Subcommand-specific flags.
            if (sc == SubCommand.Wifi)
            {
                switch (a)
                {
                    case "--ssid":
                        if (++i >= argv.Count)
                        {
                            return Err("--ssid requires a value");
                        }
                        wifiSsid = argv[i];
                        continue;
                    case "--password":
                        if (++i >= argv.Count)
                        {
                            return Err("--password requires a value");
                        }
                        wifiPassword = argv[i];
                        continue;
                    case "--security":
                        if (++i >= argv.Count)
                        {
                            return Err("--security requires a value");
                        }
                        wifiSecurity = argv[i];
                        continue;
                    case "--hidden":
                        wifiHidden = true;
                        continue;
                }
            }
            else if (sc == SubCommand.Sms)
            {
                switch (a)
                {
                    case "--number":
                        if (++i >= argv.Count)
                        {
                            return Err("--number requires a value");
                        }
                        smsNumber = argv[i];
                        continue;
                    case "--message":
                        if (++i >= argv.Count)
                        {
                            return Err("--message requires a value");
                        }
                        smsMessage = argv[i];
                        continue;
                }
            }
            else if (sc == SubCommand.Mailto)
            {
                switch (a)
                {
                    case "--to":
                        if (++i >= argv.Count)
                        {
                            return Err("--to requires a value");
                        }
                        mailtoTo = argv[i];
                        continue;
                    case "--subject":
                        if (++i >= argv.Count)
                        {
                            return Err("--subject requires a value");
                        }
                        mailtoSubject = argv[i];
                        continue;
                    case "--body":
                        if (++i >= argv.Count)
                        {
                            return Err("--body requires a value");
                        }
                        mailtoBody = argv[i];
                        continue;
                    case "--cc":
                        if (++i >= argv.Count)
                        {
                            return Err("--cc requires a value");
                        }
                        mailtoCc = argv[i];
                        continue;
                    case "--bcc":
                        if (++i >= argv.Count)
                        {
                            return Err("--bcc requires a value");
                        }
                        mailtoBcc = argv[i];
                        continue;
                }
            }
            else if (sc == SubCommand.Geo)
            {
                switch (a)
                {
                    case "--lat":
                        if (++i >= argv.Count)
                        {
                            return Err("--lat requires a value");
                        }
                        if (!double.TryParse(argv[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                        {
                            return Err($"invalid --lat value: {argv[i]}");
                        }
                        geoLat = lat;
                        continue;
                    case "--lon":
                        if (++i >= argv.Count)
                        {
                            return Err("--lon requires a value");
                        }
                        if (!double.TryParse(argv[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                        {
                            return Err($"invalid --lon value: {argv[i]}");
                        }
                        geoLon = lon;
                        continue;
                    case "--query":
                        if (++i >= argv.Count)
                        {
                            return Err("--query requires a value");
                        }
                        geoQuery = argv[i];
                        continue;
                }
            }
            else if (sc == SubCommand.Tel)
            {
                if (a == "--number")
                {
                    if (++i >= argv.Count)
                    {
                        return Err("--number requires a value");
                    }
                    telNumber = argv[i];
                    continue;
                }
            }

            // Text subcommand: first positional is the payload; '-' means read stdin.
            if (sc == SubCommand.Text)
            {
                if (a == "-")
                {
                    readStdin = true;
                    positionalSeen = true;
                    continue;
                }
                if (!a.StartsWith('-'))
                {
                    textPayload = a;
                    positionalSeen = true;
                    continue;
                }
            }

            return Err($"unknown option: {a}");
        }

        // Text mode: if no positional given, signal stdin read (Program.cs decides whether stdin is TTY).
        if (sc == SubCommand.Text && !positionalSeen)
        {
            readStdin = true;
        }

        // Helper-subcommand required-field enforcement.
        if (sc == SubCommand.Wifi && string.IsNullOrEmpty(wifiSsid))
        {
            return Err("wifi: missing required --ssid");
        }
        if (sc == SubCommand.Sms && string.IsNullOrEmpty(smsNumber))
        {
            return Err("sms: missing required --number");
        }
        if (sc == SubCommand.Mailto && string.IsNullOrEmpty(mailtoTo))
        {
            return Err("mailto: missing required --to");
        }
        if (sc == SubCommand.Geo && (geoLat is null || geoLon is null))
        {
            return Err("geo: missing required --lat and/or --lon");
        }
        if (sc == SubCommand.Tel && string.IsNullOrEmpty(telNumber))
        {
            return Err("tel: missing required --number");
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
            WifiSsid: wifiSsid, WifiPassword: wifiPassword, WifiSecurity: wifiSecurity, WifiHidden: wifiHidden,
            SmsNumber: smsNumber, SmsMessage: smsMessage,
            MailtoTo: mailtoTo, MailtoSubject: mailtoSubject, MailtoBody: mailtoBody, MailtoCc: mailtoCc, MailtoBcc: mailtoBcc,
            GeoLat: geoLat, GeoLon: geoLon, GeoQuery: geoQuery,
            TelNumber: telNumber);

        return new Result(opts, null, readStdin, false, false, false);
    }

    private static bool TryParseFormat(string s, out OutputFormat f)
    {
        switch (s)
        {
            case "auto":
                f = OutputFormat.Auto;
                return true;
            case "unicode":
                f = OutputFormat.Unicode;
                return true;
            case "ascii":
                f = OutputFormat.Ascii;
                return true;
            case "svg":
                f = OutputFormat.Svg;
                return true;
            case "png":
                f = OutputFormat.Png;
                return true;
            default:
                f = OutputFormat.Auto;
                return false;
        }
    }

    private static bool TryParseEcc(string s, out EccLevel e)
    {
        switch (s)
        {
            case "l":
                e = EccLevel.L;
                return true;
            case "m":
                e = EccLevel.M;
                return true;
            case "q":
                e = EccLevel.Q;
                return true;
            case "h":
                e = EccLevel.H;
                return true;
            default:
                e = EccLevel.M;
                return false;
        }
    }

    private static Result Err(string msg)
    {
        return new Result(null, msg, false, false, false, false);
    }
}
