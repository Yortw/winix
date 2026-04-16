using System.Reflection;
using Winix.When;
using Yort.ShellKit;

namespace When;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("when", version)
            .Description("Convert timestamps between formats, apply time arithmetic, and calculate durations.")
            .StandardFlags()
            .Option("--tz", null, "ZONE", "Display in this timezone (IANA or Windows ID)")
            .Flag("--utc", null, "Output only UTC ISO 8601 string (conversion mode)")
            .Flag("--local", null, "Output only local ISO 8601 string (conversion mode)")
            .Flag("--iso", null, "Output only ISO 8601 duration string (diff mode)")
            .Positional("<input> [+/-offset] | diff <time1> <time2>")
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
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("input", "string", "Original input string (conversion mode)")
            .JsonField("offset", "string|null", "Offset string if applied (conversion mode)")
            .JsonField("utc", "string", "UTC ISO 8601 timestamp (conversion mode)")
            .JsonField("local", "string", "Local ISO 8601 timestamp (conversion mode)")
            .JsonField("local_timezone", "string", "Local timezone abbreviation (conversion mode)")
            .JsonField("unix_seconds", "int", "Unix epoch in seconds (conversion mode)")
            .JsonField("unix_milliseconds", "int", "Unix epoch in milliseconds (conversion mode)")
            .JsonField("relative", "string", "Relative time string (conversion mode)")
            .JsonField("from", "string", "First timestamp, UTC (diff mode)")
            .JsonField("to", "string", "Second timestamp, UTC (diff mode)")
            .JsonField("duration_iso", "string", "ISO 8601 duration, signed (diff mode)")
            .JsonField("total_seconds", "int", "Total seconds, signed (diff mode)")
            .JsonField("days", "int", "Day component, signed (diff mode)")
            .JsonField("hours", "int", "Hour component (diff mode)")
            .JsonField("minutes", "int", "Minute component (diff mode)")
            .JsonField("seconds", "int", "Second component (diff mode)");

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        bool jsonOutput = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: false);
        bool wantUtc = result.Has("--utc");
        bool wantLocal = result.Has("--local");
        bool wantIso = result.Has("--iso");

        // --- Mutual exclusion checks ---
        int exclusiveCount = (wantUtc ? 1 : 0) + (wantLocal ? 1 : 0) + (wantIso ? 1 : 0);
        if (exclusiveCount > 1)
        {
            return WriteError(result, jsonOutput, "when", version,
                "--utc, --local, and --iso are mutually exclusive.");
        }

        if (result.Positionals.Length == 0)
        {
            return WriteError(result, jsonOutput, "when", version,
                "missing input. Run 'when --help' for usage.");
        }

        // --- Resolve --tz ---
        TimeZoneInfo? extraTz = null;
        if (result.Has("--tz"))
        {
            string tzId = result.GetString("--tz");
            if (!TimezoneResolver.TryResolve(tzId, out extraTz, out string? tzError))
            {
                return WriteError(result, jsonOutput, "when", version, tzError!);
            }
        }

        // --- Detect mode ---
        string firstArg = result.Positionals[0];

        if (firstArg.Equals("diff", StringComparison.OrdinalIgnoreCase))
        {
            return RunDiffMode(result, version, jsonOutput, useColor, wantUtc, wantLocal, wantIso, extraTz);
        }

        return RunConversionMode(result, version, jsonOutput, useColor, wantUtc, wantLocal, wantIso, extraTz);
    }

    private static int RunConversionMode(ParseResult result, string version,
        bool jsonOutput, bool useColor, bool wantUtc, bool wantLocal, bool wantIso,
        TimeZoneInfo? extraTz)
    {
        // --iso is invalid in conversion mode
        if (wantIso)
        {
            return WriteError(result, jsonOutput, "when", version,
                "--iso is only valid in diff mode.");
        }

        string inputStr = result.Positionals[0];

        // Parse the timestamp
        DateTimeOffset timestamp;
        if (InputParser.IsNow(inputStr))
        {
            timestamp = DateTimeOffset.UtcNow;
        }
        else
        {
            if (!InputParser.TryParse(inputStr, out timestamp, out string? parseError))
            {
                return WriteError(result, jsonOutput, "when", version, parseError!);
            }
        }

        // Parse optional offset (positionals[1] if it starts with + or -)
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
                    return WriteError(result, jsonOutput, "when", version,
                        $"invalid offset '{rawOffset}': {offsetError}");
                }

                if (isNegative)
                {
                    offset = offset.Negate();
                }

                timestamp = timestamp.Add(offset);

                // Reject extra positionals after offset
                if (result.Positionals.Length > 2)
                {
                    return WriteError(result, jsonOutput, "when", version,
                        $"unexpected argument '{result.Positionals[2]}'. Expected: when <input> [+/-offset]");
                }
            }
            else
            {
                return WriteError(result, jsonOutput, "when", version,
                    $"unexpected argument '{rawOffset}'. Offsets must start with + or -.");
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeZoneInfo localTz = TimeZoneInfo.Local;

        // --- Output ---
        if (jsonOutput)
        {
            Console.Out.WriteLine(Formatting.FormatJson(timestamp, localTz, extraTz, now,
                inputStr, offsetStr, "when", version));
            return ExitCode.Success;
        }

        if (wantUtc)
        {
            Console.Out.WriteLine(Formatting.FormatUtc(timestamp));
            return ExitCode.Success;
        }

        if (wantLocal)
        {
            // --local with --tz uses the --tz timezone, not system local
            TimeZoneInfo outputTz = extraTz ?? localTz;
            Console.Out.WriteLine(Formatting.FormatLocal(timestamp, outputTz));
            return ExitCode.Success;
        }

        Console.Out.WriteLine(Formatting.FormatDefault(timestamp, localTz, extraTz, now, useColor));
        return ExitCode.Success;
    }

    private static int RunDiffMode(ParseResult result, string version,
        bool jsonOutput, bool useColor, bool wantUtc, bool wantLocal, bool wantIso,
        TimeZoneInfo? extraTz)
    {
        // --utc and --local are invalid in diff mode
        if (wantUtc)
        {
            return WriteError(result, jsonOutput, "when", version,
                "--utc is only valid in conversion mode.");
        }
        if (wantLocal)
        {
            return WriteError(result, jsonOutput, "when", version,
                "--local is only valid in conversion mode.");
        }

        // Need at least 2 more positionals after "diff"
        if (result.Positionals.Length < 3)
        {
            return WriteError(result, jsonOutput, "when", version,
                "diff mode requires two timestamps: when diff <time1> <time2>");
        }

        // Reject extra positionals after the two timestamps
        if (result.Positionals.Length > 3)
        {
            return WriteError(result, jsonOutput, "when", version,
                $"unexpected argument '{result.Positionals[3]}'. Expected: when diff <time1> <time2>");
        }

        string input1 = result.Positionals[1];
        string input2 = result.Positionals[2];

        // Parse time1
        DateTimeOffset time1;
        if (InputParser.IsNow(input1))
        {
            time1 = DateTimeOffset.UtcNow;
        }
        else
        {
            if (!InputParser.TryParse(input1, out time1, out string? error1))
            {
                return WriteError(result, jsonOutput, "when", version,
                    $"cannot parse first timestamp: {error1}");
            }
        }

        // Parse time2
        DateTimeOffset time2;
        if (InputParser.IsNow(input2))
        {
            time2 = DateTimeOffset.UtcNow;
        }
        else
        {
            if (!InputParser.TryParse(input2, out time2, out string? error2))
            {
                return WriteError(result, jsonOutput, "when", version,
                    $"cannot parse second timestamp: {error2}");
            }
        }

        TimeSpan duration = time2 - time1;

        // --- Output ---
        if (jsonOutput)
        {
            Console.Out.WriteLine(Formatting.FormatDiffJson(duration, time1, time2, "when", version));
            return ExitCode.Success;
        }

        if (wantIso)
        {
            Console.Out.WriteLine(Formatting.FormatDiffIso(duration));
            return ExitCode.Success;
        }

        Console.Out.WriteLine(Formatting.FormatDiff(duration, time1, time2, extraTz, useColor));
        return ExitCode.Success;
    }

    private static int WriteError(ParseResult result, bool jsonOutput,
        string toolName, string version, string message)
    {
        if (jsonOutput)
        {
            Console.Error.WriteLine(Formatting.FormatJsonError(
                ExitCode.UsageError, "usage_error", message, toolName, version));
        }
        else
        {
            Console.Error.WriteLine($"when: {message}");
        }
        return ExitCode.UsageError;
    }

    private static string GetVersion()
    {
        return typeof(InputParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
