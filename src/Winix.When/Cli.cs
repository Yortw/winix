#nullable enable
using System;
using System.IO;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.When;

/// <summary>
/// Library-level entry point for the when CLI. Program.cs is a thin shim around
/// <see cref="Run"/>; all orchestration (mode dispatch, mutual-exclusion checks,
/// error formatting, the negative-offset injector) lives here so the contracts
/// can be exercised by unit tests.
/// </summary>
/// <remarks>
/// Round-1 review CR-I3 / TA-C1 — extracted to mirror the digest/notify/url/timeit/ids
/// Cli seam pattern. Pre-fix, Program.cs was 358 lines of orchestration with zero
/// test coverage of mode dispatch, exit-code mapping, JSON-vs-plain error envelopes,
/// or the negative-offset injector state machine.
/// </remarks>
public static class Cli
{
    /// <summary>Runs the when pipeline: parse, dispatch on mode, format, return exit code.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string version = GetVersion();

        // Pre-process args: negative offsets like -3h start with '-' which the parser
        // treats as flags. Insert '--' before offset-like args so the parser passes
        // them through as positionals.
        args = InjectDoubleDashBeforeNegativeOffsets(args);

        var parser = BuildParser(version);

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        bool jsonOutput = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: false);
        bool wantUtc = result.Has("--utc");
        bool wantLocal = result.Has("--local");
        bool wantIso = result.Has("--iso");

        // --- Mutual exclusion checks ---
        int exclusiveCount = (wantUtc ? 1 : 0) + (wantLocal ? 1 : 0) + (wantIso ? 1 : 0);
        if (exclusiveCount > 1)
        {
            return WriteError(stderr, jsonOutput, version,
                "--utc, --local, and --iso are mutually exclusive.");
        }

        if (result.Positionals.Length == 0)
        {
            return WriteError(stderr, jsonOutput, version,
                "missing input. Run 'when --help' for usage.");
        }

        // --- Resolve --tz ---
        TimeZoneInfo? extraTz = null;
        if (result.Has("--tz"))
        {
            string tzId = result.GetString("--tz");
            if (!TimezoneResolver.TryResolve(tzId, out extraTz, out string? tzError))
            {
                return WriteError(stderr, jsonOutput, version, tzError!);
            }
        }

        // --- Detect mode ---
        string firstArg = result.Positionals[0];

        if (firstArg.Equals("diff", StringComparison.OrdinalIgnoreCase))
        {
            return RunDiffMode(result, version, stdout, stderr, jsonOutput, useColor, wantUtc, wantLocal, wantIso, extraTz);
        }

        return RunConversionMode(result, version, stdout, stderr, jsonOutput, useColor, wantUtc, wantLocal, wantIso, extraTz);
    }

    private static int RunConversionMode(ParseResult result, string version,
        TextWriter stdout, TextWriter stderr,
        bool jsonOutput, bool useColor, bool wantUtc, bool wantLocal, bool wantIso,
        TimeZoneInfo? extraTz)
    {
        if (wantIso)
        {
            return WriteError(stderr, jsonOutput, version,
                "--iso is only valid in diff mode.");
        }

        string inputStr = result.Positionals[0];

        DateTimeOffset timestamp;
        if (InputParser.IsNow(inputStr))
        {
            timestamp = DateTimeOffset.UtcNow;
        }
        else
        {
            if (!InputParser.TryParse(inputStr, out timestamp, out string? parseError))
            {
                return WriteError(stderr, jsonOutput, version, parseError!);
            }
        }

        string? offsetStr = null;
        if (result.Positionals.Length > 1)
        {
            string rawOffset = result.Positionals[1];
            if (rawOffset.Length > 0 && (rawOffset[0] == '+' || rawOffset[0] == '-'))
            {
                offsetStr = rawOffset;
                bool isNegative = rawOffset[0] == '-';
                string offsetValue = rawOffset.Substring(1);

                if (!OffsetParser.TryParse(offsetValue, out TimeSpan offset, out string? offsetError))
                {
                    return WriteError(stderr, jsonOutput, version,
                        $"invalid offset '{rawOffset}': {offsetError}");
                }

                if (isNegative)
                {
                    offset = offset.Negate();
                }

                // Round-1 review SFH-C1 / CR-C2 — DateTimeOffset.Add throws AOOR on overflow.
                try
                {
                    timestamp = timestamp.Add(offset);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return WriteError(stderr, jsonOutput, version,
                        $"applying offset '{rawOffset}' to '{result.Positionals[0]}' overflows the supported date range (year 1 to 9999).");
                }

                if (result.Positionals.Length > 2)
                {
                    return WriteError(stderr, jsonOutput, version,
                        $"unexpected argument '{result.Positionals[2]}'. Expected: when <input> [+/-offset]");
                }
            }
            else
            {
                return WriteError(stderr, jsonOutput, version,
                    $"unexpected argument '{rawOffset}'. Offsets must start with + or -.");
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeZoneInfo localTz = TimeZoneInfo.Local;

        if (jsonOutput)
        {
            stdout.WriteLine(Formatting.FormatJson(timestamp, localTz, extraTz, now,
                inputStr, offsetStr, "when", version));
            return ExitCode.Success;
        }

        if (wantUtc)
        {
            stdout.WriteLine(Formatting.FormatUtc(timestamp));
            return ExitCode.Success;
        }

        if (wantLocal)
        {
            TimeZoneInfo outputTz = extraTz ?? localTz;
            stdout.WriteLine(Formatting.FormatLocal(timestamp, outputTz));
            return ExitCode.Success;
        }

        stdout.WriteLine(Formatting.FormatDefault(timestamp, localTz, extraTz, now, useColor));
        return ExitCode.Success;
    }

    private static int RunDiffMode(ParseResult result, string version,
        TextWriter stdout, TextWriter stderr,
        bool jsonOutput, bool useColor, bool wantUtc, bool wantLocal, bool wantIso,
        TimeZoneInfo? extraTz)
    {
        if (wantUtc)
        {
            return WriteError(stderr, jsonOutput, version,
                "--utc is only valid in conversion mode.");
        }
        if (wantLocal)
        {
            return WriteError(stderr, jsonOutput, version,
                "--local is only valid in conversion mode.");
        }

        if (result.Positionals.Length < 3)
        {
            return WriteError(stderr, jsonOutput, version,
                "diff mode requires two timestamps: when diff <time1> <time2>");
        }

        if (result.Positionals.Length > 3)
        {
            return WriteError(stderr, jsonOutput, version,
                $"unexpected argument '{result.Positionals[3]}'. Expected: when diff <time1> <time2>");
        }

        string input1 = result.Positionals[1];
        string input2 = result.Positionals[2];

        DateTimeOffset time1;
        if (InputParser.IsNow(input1))
        {
            time1 = DateTimeOffset.UtcNow;
        }
        else
        {
            if (!InputParser.TryParse(input1, out time1, out string? error1))
            {
                return WriteError(stderr, jsonOutput, version,
                    $"cannot parse first timestamp: {error1}");
            }
        }

        DateTimeOffset time2;
        if (InputParser.IsNow(input2))
        {
            time2 = DateTimeOffset.UtcNow;
        }
        else
        {
            if (!InputParser.TryParse(input2, out time2, out string? error2))
            {
                return WriteError(stderr, jsonOutput, version,
                    $"cannot parse second timestamp: {error2}");
            }
        }

        TimeSpan duration = time2 - time1;

        if (jsonOutput)
        {
            stdout.WriteLine(Formatting.FormatDiffJson(duration, time1, time2, "when", version));
            return ExitCode.Success;
        }

        if (wantIso)
        {
            stdout.WriteLine(Formatting.FormatDiffIso(duration));
            return ExitCode.Success;
        }

        stdout.WriteLine(Formatting.FormatDiff(duration, time1, time2, extraTz, useColor));
        return ExitCode.Success;
    }

    private static int WriteError(TextWriter stderr, bool jsonOutput, string version, string message)
    {
        if (jsonOutput)
        {
            stderr.WriteLine(Formatting.FormatJsonError(
                ExitCode.UsageError, "usage_error", message, "when", version));
        }
        else
        {
            stderr.WriteLine($"when: {message}");
        }
        return ExitCode.UsageError;
    }

    /// <summary>
    /// Reorders arguments so that real flags come before any <c>--</c> separator and
    /// negative-shape positionals come after, so ShellKit can parse flags correctly while
    /// also accepting <c>-3h</c>, <c>-86400</c>, or <c>-P3DT4H</c> as positional values
    /// rather than unknown short-flags.
    /// </summary>
    /// <remarks>
    /// Round-1 review CR-I7 + post-fix bug — handles three cases:
    /// (1) Negative offset AFTER a positional: <c>when 2024-06-18 -3h</c>
    /// (2) Negative-epoch BEFORE any positional: <c>when -86400</c>
    /// (3) Negative-shape combined with flags: <c>when -86400 --json</c> — flags must come
    ///     BEFORE <c>--</c> or ShellKit treats them as positionals (POSIX <c>--</c> semantics).
    ///
    /// The original implementation just inserted <c>--</c> at the position of the first
    /// negative-shape arg. That broke case 3 because subsequent <c>--json</c> ended up
    /// after the <c>--</c> separator. This implementation scans all args, partitions into
    /// flags vs negative-shape positionals vs ordinary positionals, and re-emits as
    /// <c>flags + [--] + positionals</c> when a negative-shape arg is present.
    /// Order WITHIN the flags group and WITHIN the positionals group is preserved.
    /// Internal so tests can pin the state-machine cases directly.
    /// </remarks>
    internal static string[] InjectDoubleDashBeforeNegativeOffsets(string[] args)
    {
        // Already have '--' explicitly — user knows what they're doing, return as-is.
        for (int j = 0; j < args.Length; j++)
        {
            if (args[j] == "--") return args;
        }

        // Single-pass classify: each arg is either (a) a real flag, (b) a negative-shape
        // value that ShellKit would mis-parse, or (c) an ordinary positional (no leading
        // dash, or leading + which ShellKit accepts as positional).
        var flags = new System.Collections.Generic.List<string>(args.Length);
        var positionals = new System.Collections.Generic.List<string>(args.Length);
        bool anyNegativeShape = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            bool isNegativeShape = arg.Length >= 2 && arg[0] == '-'
                                && (char.IsDigit(arg[1]) || arg[1] == 'P');
            if (isNegativeShape)
            {
                anyNegativeShape = true;
                positionals.Add(arg);
                continue;
            }

            // Real flag if starts with '-' (and isn't negative-shape — already handled above)
            if (arg.Length > 0 && arg[0] == '-')
            {
                flags.Add(arg);
                // Value-taking options (--tz Asia/Tokyo) consume the next arg as their value.
                // Without this, `when --tz Asia/Tokyo -86400` would put "Asia/Tokyo" in
                // positionals (correct slot for the value? no — it's --tz's value, must
                // stay adjacent in the flags group).
                if (i + 1 < args.Length && IsValueTakingOption(arg))
                {
                    flags.Add(args[++i]);
                }
                continue;
            }

            // Ordinary positional (no leading dash, or starts with + which ShellKit accepts).
            positionals.Add(arg);
        }

        if (!anyNegativeShape)
        {
            return args; // No negative-shape values → no rewrite needed.
        }

        // flags + ["--"] + positionals, preserving within-group order.
        var result = new string[flags.Count + 1 + positionals.Count];
        int p = 0;
        foreach (var f in flags) result[p++] = f;
        result[p++] = "--";
        foreach (var pos in positionals) result[p++] = pos;
        return result;
    }

    /// <summary>
    /// True if the given long-form flag is a value-taking option in when's parser surface.
    /// Used by the negative-shape rewriter to keep option-value pairs adjacent.
    /// </summary>
    private static bool IsValueTakingOption(string flag)
    {
        return flag == "--tz";
    }

    private static CommandLineParser BuildParser(string version)
    {
        return new CommandLineParser("when", version)
            .Description("Convert timestamps between formats, apply time arithmetic, and calculate durations.")
            .StandardFlags()
            .Option("--tz", null, "ZONE", "Display in this timezone (IANA or Windows ID)")
            .Flag("--utc", null, "Output only UTC ISO 8601 string (conversion mode)")
            .Flag("--local", null, "Output only local ISO 8601 string (conversion mode)")
            .Flag("--iso", null, "Output only ISO 8601 duration string (diff mode)")
            .Positional("<input> [+/-offset] | diff <time1> <time2>")
            // Condensed vocab for the otherwise-opaque <input> positional. The full ambiguity
            // rules (year-vs-epoch, ISO-duration restrictions) stay in README/man to limit the
            // sync surface; --help carries just enough for a newcomer to self-serve offline.
            .Section("Input Formats",
                "now                  Current instant\n" +
                "Unix epoch           1718745600 (seconds, 1-10 digits) or 1718745600000 (millis, 11-13 digits)\n" +
                "ISO 8601 date        2024-06-18 (treated as midnight UTC)\n" +
                "ISO 8601 datetime    2024-06-18T16:00:00Z (with or without timezone offset)\n" +
                "Note: bare 4-digit values like 2025 are rejected as year/epoch-ambiguous (see 'man when').")
            .Section("Offsets",
                "+/-N{d,h,m,s}        Relative offset, e.g. +7d or -2h30m\n" +
                "ISO 8601 duration    e.g. +P1DT12H (days/hours/minutes/seconds only)")
            .ExitCodes(
                (0, "Success"),
                (ExitCode.UsageError, "Usage error — bad arguments, unparseable input, unknown timezone"))
            .Platform("cross-platform",
                replaces: new[] { "date" },
                valueOnWindows: "No native date conversion command; replaces web tools and PowerShell gymnastics",
                valueOnUnix: "Simpler than arcane date(1) syntax; cross-platform, consistent output")
            .StdinDescription("Not used")
            .StdoutDescription("Timestamp conversions, durations, or JSON output")
            .StderrDescription("Error messages only")
            .Example("when 1718745600", "Convert a Unix epoch to all formats")
            .Example("when now", "Show current time in all formats")
            .Example("when now --tz Asia/Tokyo", "Show current time in Tokyo")
            .Example("when now +7d", "Show the date 7 days from now")
            .Example("when now --utc", "Pipe-friendly UTC timestamp")
            .Example("when diff 2024-06-18 2024-06-25", "Calculate duration between two dates")
            .Example("when diff now 2024-12-25 --iso", "ISO 8601 duration until Christmas")
            .ComposesWith("wargs", "cat epochs.txt | wargs when", "Convert a list of epochs")
            .ComposesWith("timeit", "timeit when diff 2024-01-01 2024-12-31", "Time the diff calculation")
            .JsonField("tool", "string", "Tool name (\"when\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "0 = success, 125 = usage error")
            .JsonField("exit_reason", "string", "Machine-readable exit reason: success, usage_error")
            .JsonField("input", "string", "Original input string (conversion mode)")
            .JsonField("offset", "string|null", "Offset string if applied (conversion mode)")
            .JsonField("utc", "string", "UTC ISO 8601 timestamp (conversion mode)")
            .JsonField("local", "string", "Local ISO 8601 timestamp (conversion mode)")
            .JsonField("local_timezone", "string", "Local timezone abbreviation (conversion mode)")
            .JsonField("target", "string|null", "Target-timezone ISO 8601 timestamp (only when --tz set, conversion mode)")
            .JsonField("target_timezone", "string|null", "Target timezone abbreviation (only when --tz set, conversion mode)")
            .JsonField("unix_seconds", "int", "Unix epoch in seconds (conversion mode)")
            .JsonField("unix_milliseconds", "int", "Unix epoch in milliseconds (conversion mode)")
            .JsonField("relative", "string", "Relative time string (conversion mode)")
            .JsonField("from", "string", "First timestamp, UTC (diff mode)")
            .JsonField("to", "string", "Second timestamp, UTC (diff mode)")
            .JsonField("duration_iso", "string", "ISO 8601 duration, signed (diff mode)")
            .JsonField("total_seconds", "int", "Total seconds, signed (diff mode)")
            .JsonField("days", "int", "Day component, signed (diff mode)")
            .JsonField("hours", "int", "Hour component, signed (diff mode)")
            .JsonField("minutes", "int", "Minute component, signed (diff mode)")
            .JsonField("seconds", "int", "Second component, signed (diff mode)");
    }

    private static string GetVersion()
    {
        // SDK appends a SourceLink "+gitsha" suffix to AssemblyInformationalVersion
        // by default; strip it so users see plain "X.Y.Z" — matches the convention
        // adopted across clip / digest / ids / schedule / etc.
        string raw = typeof(InputParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
