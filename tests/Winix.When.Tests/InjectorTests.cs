#nullable enable
using Winix.When;
using Xunit;

namespace Winix.When.Tests;

// Round-1 review TA-C2 — the InjectDoubleDashBeforeNegativeOffsets state machine
// previously had zero direct test coverage. It's a non-trivial argument rewriter:
// tracks positional position, recognizes negative-shape patterns (-<digit>, -P),
// preserves real flags (-h, -v, --color), and short-circuits on '--'. Each branch
// needs explicit pinning so a future refactor can't subtly mis-parse e.g.
// `when 2024-06-18 -3h` (negative offset after positional) vs `when -3h`
// (would be a flag if not handled).
public class InjectorTests
{
    // The injector partitions args into [flags, "--", positionals]. Because POSIX '--'
    // means "everything after is positional", emitting it before the positionals group
    // (rather than in-place) is equivalent for ShellKit when no real flags are present —
    // and is required when real flags ARE present (e.g. `-86400 --json`), otherwise '--'
    // would consume '--json' as a positional. These tests pin the partitioned shape.
    [Fact]
    public void Inject_NegativeOffsetAfterPositional_InjectsDoubleDash()
    {
        var args = new[] { "2024-06-18", "-3h" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "--", "2024-06-18", "-3h" }, result);
    }

    [Fact]
    public void Inject_NegativeIsoDurationAfterPositional_InjectsDoubleDash()
    {
        var args = new[] { "2024-06-18", "-P3D" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "--", "2024-06-18", "-P3D" }, result);
    }

    // -- Round-1 review CR-I7 — the negative-epoch single-arg case was previously
    //    unreachable: the injector skipped the first arg unless a positional had been
    //    seen, so `when -86400` hit ShellKit as an unknown flag. Fix treats first-arg
    //    negative-shape as the positional. --
    [Fact]
    public void Inject_NegativeEpochAsFirstArg_InjectsDoubleDash()
    {
        var args = new[] { "-86400" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "--", "-86400" }, result);
    }

    [Fact]
    public void Inject_NegativeIsoDurationAsFirstArg_InjectsDoubleDash()
    {
        // `when -P30D` could in principle be ambiguous (offset before any positional),
        // but per the round-1 fix it's treated as the positional input (which then fails
        // because P30D isn't a valid timestamp — the failure is at parse, not at flag-parse).
        var args = new[] { "-P30D" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "--", "-P30D" }, result);
    }

    // ── Real flags must NOT be injected ──

    [Fact]
    public void Inject_DashH_NotInjected_RealShortFlag()
    {
        var args = new[] { "-h" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "-h" }, result);
    }

    [Fact]
    public void Inject_DoubleDashLong_NotInjected_RealLongFlag()
    {
        var args = new[] { "--utc" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "--utc" }, result);
    }

    [Fact]
    public void Inject_DashHFollowedByPositional_NotInjected()
    {
        var args = new[] { "-h", "now" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "-h", "now" }, result);
    }

    // ── Already-present '--' short-circuits ──

    [Fact]
    public void Inject_AlreadyHasDoubleDash_ReturnsAsIs()
    {
        var args = new[] { "--", "-3h" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Same(args, result); // returned unchanged, same reference
    }

    [Fact]
    public void Inject_DoubleDashLater_ReturnsAsIs()
    {
        var args = new[] { "now", "--", "-3h" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Same(args, result);
    }

    // ── Positive offsets pass through (parser accepts +) ──

    [Fact]
    public void Inject_PositiveOffset_NotInjected()
    {
        var args = new[] { "now", "+3h" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        // No change — '+' isn't ambiguous with flag syntax.
        Assert.Equal(new[] { "now", "+3h" }, result);
    }

    // ── Empty + single-element edge cases ──

    [Fact]
    public void Inject_EmptyArgs_ReturnsEmpty()
    {
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(System.Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public void Inject_DiffWithNegativeEpoch_HandlesPositionalAfterDiff()
    {
        // `when diff now -86400` — "diff" and "now" are positionals, -86400 is a negative-
        // shape positional. Partitioning emits flags=∅, then "--", then all three positionals
        // in original order. Equivalent to the old in-place form for ShellKit's purposes.
        var args = new[] { "diff", "now", "-86400" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "--", "diff", "now", "-86400" }, result);
    }

    // ── Real flags interleaved with negative-shape positionals — the case that motivated
    //    moving '--' to before the positionals group. If '--' were inserted in-place, it
    //    would consume the trailing '--json' as a positional and break JSON output.
    [Fact]
    public void Inject_NegativeEpochWithTrailingFlag_FlagSurvives()
    {
        var args = new[] { "-86400", "--json" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "--json", "--", "-86400" }, result);
    }

    [Fact]
    public void Inject_TzWithValueAndNegativeOffset_KeepsTzValueAdjacent()
    {
        // --tz consumes Asia/Tokyo; the value must stay adjacent to its option in the
        // flags group, not get hoisted into positionals.
        var args = new[] { "--tz", "Asia/Tokyo", "-86400" };
        var result = Cli.InjectDoubleDashBeforeNegativeOffsets(args);
        Assert.Equal(new[] { "--tz", "Asia/Tokyo", "--", "-86400" }, result);
    }
}
