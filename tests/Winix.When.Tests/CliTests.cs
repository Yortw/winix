#nullable enable
using System;
using System.IO;
using Winix.When;
using Xunit;
using Yort.ShellKit;

namespace Winix.When.Tests;

// Round-1 review CR-I3 / TA-C1 — Program.cs's mode dispatch, mutual-exclusion checks,
// JSON-vs-plain error envelopes, and overflow-catch paths previously had zero coverage.
// Cli.Run now lives in the library so every dispatch path can be locked here.
public class CliTests
{
    private static (int exit, string stdout, string stderr) RunCli(params string[] args)
    {
        var stdoutWriter = new StringWriter();
        var stderrWriter = new StringWriter();
        int exit = Cli.Run(args, stdoutWriter, stderrWriter);
        return (exit, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    // ── Happy-path conversion ──

    [Fact]
    public void Run_NowConversion_ExitsZero()
    {
        var r = RunCli("now");
        Assert.Equal(0, r.exit);
        Assert.Contains("UTC:", r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_EpochInput_ExitsZero()
    {
        var r = RunCli("1718745600");
        Assert.Equal(0, r.exit);
        Assert.Contains("2024-06-18", r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Utc_PipeFriendlyOutput()
    {
        var r = RunCli("1718745600", "--utc");
        Assert.Equal(0, r.exit);
        Assert.Contains("Z", r.stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("UTC:", r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Json_EmitsAllFields()
    {
        var r = RunCli("1718745600", "--json");
        Assert.Equal(0, r.exit);
        Assert.Contains("\"tool\":\"when\"", r.stdout, StringComparison.Ordinal);
        Assert.Contains("\"unix_seconds\":1718745600", r.stdout, StringComparison.Ordinal);
    }

    // ── DOCS-IMP-1 / CR-I4 — pin that target/target_timezone JsonField is registered ──

    [Fact]
    public void Run_JsonWithTz_EmitsTargetFields()
    {
        var r = RunCli("1718745600", "--tz", "Asia/Tokyo", "--json");
        Assert.Equal(0, r.exit);
        Assert.Contains("\"target\":", r.stdout, StringComparison.Ordinal);
        Assert.Contains("\"target_timezone\":", r.stdout, StringComparison.Ordinal);
    }

    // ── Diff mode ──

    [Fact]
    public void Run_DiffMode_ExitsZero()
    {
        var r = RunCli("diff", "2024-06-18", "2024-06-25");
        Assert.Equal(0, r.exit);
        Assert.Contains("7 days", r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_DiffIso_PipeFriendly()
    {
        var r = RunCli("diff", "2024-06-18", "2024-06-25", "--iso");
        Assert.Equal(0, r.exit);
        Assert.Contains("P7D", r.stdout, StringComparison.Ordinal);
    }

    // ── Mutual-exclusion matrix (TA-I5) ──

    [Fact]
    public void Run_UtcAndLocal_RejectedAsMutuallyExclusive()
    {
        var r = RunCli("now", "--utc", "--local");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("mutually exclusive", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_UtcInDiffMode_RejectedAsConversionOnly()
    {
        var r = RunCli("diff", "2024-06-18", "2024-06-25", "--utc");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("conversion mode", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_LocalInDiffMode_Rejected()
    {
        var r = RunCli("diff", "2024-06-18", "2024-06-25", "--local");
        Assert.Equal(ExitCode.UsageError, r.exit);
    }

    [Fact]
    public void Run_IsoInConversionMode_Rejected()
    {
        var r = RunCli("now", "--iso");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("diff mode", r.stderr, StringComparison.Ordinal);
    }

    // ── Usage errors ──

    [Fact]
    public void Run_NoInput_ReturnsUsageError()
    {
        var r = RunCli();
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("missing input", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_DiffWithOneArg_RejectedNeedsTwoTimestamps()
    {
        var r = RunCli("diff", "2024-06-18");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("two timestamps", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_BadTimezone_ReturnsUsageError()
    {
        var r = RunCli("now", "--tz", "Garbage/NotReal");
        Assert.Equal(ExitCode.UsageError, r.exit);
    }

    [Fact]
    public void Run_BadInput_ReturnsUsageError()
    {
        var r = RunCli("totally-not-a-date");
        Assert.Equal(ExitCode.UsageError, r.exit);
    }

    // ── SFH-C1 / CR-C2 — DateTimeOffset.Add overflow ──

    [Fact]
    public void Run_OffsetOverflowsDateTimeOffsetMaxValue_RoutesThroughUsageError()
    {
        // `9999-12-31T23:59:59Z + P10000D` overflows DateTimeOffset.MaxValue.
        // Pre-fix this leaked an unhandled ArgumentOutOfRangeException with a stack
        // trace and undocumented exit code. Now routes through WriteError → exit 125
        // with a clear "overflows the supported date range" message.
        var r = RunCli("9999-12-31T23:59:59Z", "+P10000D");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("overflows", r.stderr, StringComparison.Ordinal);
        Assert.Contains("when:", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_OffsetOverflowsAsJson_EmitsValidJsonError()
    {
        // Same test in JSON mode — pre-fix the JSON contract was broken (no envelope on
        // stderr, just a stack trace). Now emits the standard usage_error JSON envelope.
        var r = RunCli("9999-12-31T23:59:59Z", "+P10000D", "--json");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("\"exit_reason\":\"usage_error\"", r.stderr, StringComparison.Ordinal);
    }

    // ── CR-C1 — IsoDurationParser overflow caught ──

    [Fact]
    public void Run_HugeIsoDurationSeconds_RejectedAsOutOfRange()
    {
        // PT99999999999999S overflows TimeSpan.MaxValue.TotalSeconds. Pre-fix this leaked
        // an unhandled OverflowException (the catch covered only AOOR, not OFE).
        var r = RunCli("now", "+PT99999999999999S");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("out of range", r.stderr, StringComparison.OrdinalIgnoreCase);
    }

    // ── SFH-I1 — bare year ambiguity rejection ──

    [Fact]
    public void Run_BareYear_RejectedAsAmbiguous()
    {
        // Pre-fix this silently parsed as Unix epoch second 2025 → 1970-01-01 00:33:45 UTC.
        // In --utc mode the user couldn't tell anything was wrong.
        var r = RunCli("2025", "--utc");
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("ambiguous", r.stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2025-01-01", r.stderr, StringComparison.Ordinal);
    }

    // ── CR-I7 — negative-epoch single-arg now reachable from CLI ──

    [Fact]
    public void Run_NegativeEpochSingleArg_ParsesViaInjector()
    {
        // Pre-fix `when -86400` errored as unknown flag because the injector skipped first
        // args. Now the injector treats `-<digit>` as positional and injects `--` before it.
        var r = RunCli("-86400");
        Assert.Equal(0, r.exit);
        Assert.Contains("1969-12-31", r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_NegativeEpoch_JsonOutput_HasValidJson()
    {
        var r = RunCli("-86400", "--json");
        Assert.Equal(0, r.exit);
        Assert.Contains("\"unix_seconds\":-86400", r.stdout, StringComparison.Ordinal);
    }

    // ── ShellKit-handled flags ──

    [Fact]
    public void Run_Help_ExitsZero()
    {
        var r = RunCli("--help");
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public void Help_DocumentsInputFormatVocabulary()
    {
        // Memory project_when_help_missing_input_vocab: the <input> positional was opaque in
        // --help — now/epoch/ISO formats lived only in README/man. This locks the condensed
        // Input Formats + Offsets sections into --help so a newcomer can self-serve offline.
        // ShellKit writes --help to Console.Out (not the injected writer), so capture there.
        // No other When test touches Console, so the redirect is collision-safe under parallelism.
        TextWriter original = Console.Out;
        var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            Cli.Run(new[] { "--help" }, sw, new StringWriter());
        }
        finally
        {
            Console.SetOut(original);
        }

        string help = sw.ToString();
        Assert.Contains("Input Formats", help, StringComparison.Ordinal);
        Assert.Contains("Unix epoch", help, StringComparison.Ordinal);
        Assert.Contains("ISO 8601", help, StringComparison.Ordinal);
        Assert.Contains("now", help, StringComparison.Ordinal);
        Assert.Contains("Offsets", help, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Version_ExitsZero()
    {
        var r = RunCli("--version");
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public void Run_Describe_ExitsZero()
    {
        var r = RunCli("--describe");
        Assert.Equal(0, r.exit);
    }

    // ── Round-1 review TA-I7 — Y2038 boundary. signed-int32 wraparound at this exact
    //    second is a famous foot-gun for tools that store seconds in a 32-bit type.
    //    .NET's DateTimeOffset uses Int64 ticks so this should pass cleanly — pin it.
    [Fact]
    public void Run_Y2038BoundarySecond_ParsesCorrectly()
    {
        // 2147483647 = 2038-01-19T03:14:07Z (last representable Int32 epoch second).
        var r = RunCli("2147483647", "--utc");
        Assert.Equal(0, r.exit);
        Assert.Contains("2038-01-19T03:14:07Z", r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Y2038BoundaryPlusOne_StillParses()
    {
        // 2147483648 = 2038-01-19T03:14:08Z. Would overflow Int32 but DateTimeOffset is Int64-backed.
        var r = RunCli("2147483648", "--utc");
        Assert.Equal(0, r.exit);
        Assert.Contains("2038-01-19T03:14:08Z", r.stdout, StringComparison.Ordinal);
    }

    // ── Round-1 review TA-I3 — DST transition (spring-forward). When --tz is used, a
    //    target instant near a DST jump should still produce a well-formed offset; the
    //    converter operates on absolute UTC instants so DST is just an offset choice.
    [Fact]
    public void Run_DstSpringForwardInstant_RendersTargetTimezone()
    {
        // 2024-03-10T07:00:00Z = 03:00 EDT (one hour after the US spring-forward at 02:00 EST).
        // The target line should render in America/New_York with the correct -04:00 (EDT) offset.
        var r = RunCli("2024-03-10T07:00:00Z", "--tz", "America/New_York");
        Assert.Equal(0, r.exit);
        Assert.Contains("-04:00", r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_DstFallBackInstant_RendersTargetTimezone()
    {
        // 2024-11-03T07:00:00Z = 02:00 EST (one hour after the US fall-back at 02:00 EDT).
        // Should render with EST -05:00 offset.
        var r = RunCli("2024-11-03T07:00:00Z", "--tz", "America/New_York");
        Assert.Equal(0, r.exit);
        Assert.Contains("-05:00", r.stdout, StringComparison.Ordinal);
    }
}
