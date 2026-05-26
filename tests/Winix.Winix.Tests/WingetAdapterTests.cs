#nullable enable

using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

/// <summary>
/// Records the last process invocation for assertion. Optionally returns a canned result.
/// Other adapter test classes (ScoopAdapterTests, BrewAdapterTests, DotnetToolAdapterTests)
/// reference this class directly, so it must be public.
/// </summary>
public sealed class ProcessRecorder
{
    private readonly ProcessResult _cannedResult;

    /// <summary>The command passed to the most recent <see cref="RunAsync"/> call.</summary>
    public string? LastCommand { get; private set; }

    /// <summary>The arguments array passed to the most recent <see cref="RunAsync"/> call.</summary>
    public string[]? LastArguments { get; private set; }

    /// <summary>
    /// Initialises a new <see cref="ProcessRecorder"/>.
    /// </summary>
    /// <param name="cannedResult">
    /// The result to return from <see cref="RunAsync"/>. Defaults to exit code 0
    /// with empty stdout/stderr when <see langword="null"/>.
    /// </param>
    public ProcessRecorder(ProcessResult? cannedResult = null)
    {
        _cannedResult = cannedResult ?? new ProcessResult(0, "", "");
    }

    /// <summary>
    /// Records the invocation details and returns the canned result.
    /// Signature matches <c>Func&lt;string, string[], Task&lt;ProcessResult&gt;&gt;</c>
    /// so it can be passed directly to adapter constructors.
    /// </summary>
    public Task<ProcessResult> RunAsync(string command, string[] arguments)
    {
        LastCommand = command;
        LastArguments = arguments;
        return Task.FromResult(_cannedResult);
    }
}

public class WingetAdapterTests
{
    // Winget list output format: header line, dashes line, then "Name Id Version" rows.
    private const string ListOutputWithVersion =
        "Name   Id              Version\r\n" +
        "---------------------------------\r\n" +
        "timeit Winix.TimeIt    0.2.0";

    [Fact]
    public void Name_IsWinget()
    {
        var adapter = new WingetAdapter();

        Assert.Equal("winget", adapter.Name);
    }

    [Fact]
    public async Task Install_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new WingetAdapter(recorder.RunAsync);

        await adapter.Install("Winix.TimeIt");

        Assert.Equal("winget", recorder.LastCommand);
        Assert.Equal(
            new[] { "install", "--id", "Winix.TimeIt", "--exact", "--accept-source-agreements" },
            recorder.LastArguments);
    }

    [Fact]
    public async Task Update_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new WingetAdapter(recorder.RunAsync);

        await adapter.Update("Winix.TimeIt");

        Assert.Equal("winget", recorder.LastCommand);
        Assert.Equal(
            new[] { "upgrade", "--id", "Winix.TimeIt", "--exact", "--accept-source-agreements" },
            recorder.LastArguments);
    }

    [Fact]
    public async Task Uninstall_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new WingetAdapter(recorder.RunAsync);

        await adapter.Uninstall("Winix.TimeIt");

        Assert.Equal("winget", recorder.LastCommand);
        Assert.Equal(
            new[] { "uninstall", "--id", "Winix.TimeIt", "--exact" },
            recorder.LastArguments);
    }

    [Fact]
    public async Task IsInstalled_WhenListSucceeds_ReturnsTrue()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, ListOutputWithVersion, ""));
        var adapter = new WingetAdapter(recorder.RunAsync);

        bool result = await adapter.IsInstalled("Winix.TimeIt");

        Assert.True(result);
    }

    [Fact]
    public async Task IsInstalled_WhenListFails_ReturnsFalse()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "No installed package found matching input criteria."));
        var adapter = new WingetAdapter(recorder.RunAsync);

        bool result = await adapter.IsInstalled("Winix.TimeIt");

        Assert.False(result);
    }

    [Fact]
    public async Task GetInstalledVersion_ParsesVersionFromOutput()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, ListOutputWithVersion, ""));
        var adapter = new WingetAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Equal("0.2.0", version);
    }

    [Fact]
    public async Task GetInstalledVersion_NotInstalled_ReturnsNull()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "No installed package found matching input criteria."));
        var adapter = new WingetAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Null(version);
    }

    // Round-1 fresh-eyes 2026-05-09 SFH-C1 closure: pre-fix the parser returned
    // parts[parts.Length - 1] which became the source name or available version
    // when winget emitted the 5-column "upgrade-pending" shape. The 3-column
    // happy-path test above passed because the version IS the last token in the
    // 3-column shape. These two new tests exercise the broken shapes.

    [Fact]
    public async Task GetInstalledVersion_UpgradePendingWithSource_ReturnsInstalledVersionNotSourceName()
    {
        // The exact shape SFH's reproducer hit: 5 columns, last token is "winix"
        // (the source name). Pre-fix this returned "winix" as the version,
        // which silently shipped to `winix list` and `winix list --json`.
        const string upgradePendingOutput =
            "Name           Id              Version  Available  Source\r\n" +
            "------------------------------------------------------------\r\n" +
            "Winix.TimeIt   Winix.TimeIt    0.3.0    0.4.0      winix";
        var recorder = new ProcessRecorder(new ProcessResult(0, upgradePendingOutput, ""));
        var adapter = new WingetAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Equal("0.3.0", version);
        // Must not be confused with the source name or the available version.
        Assert.NotEqual("winix", version);
        Assert.NotEqual("0.4.0", version);
    }

    [Fact]
    public async Task GetInstalledVersion_UpgradePendingWithoutSource_ReturnsInstalledVersionNotAvailableVersion()
    {
        // 4 columns: Name Id Version Available (no Source). Pre-fix the parser
        // returned the available version, silently misrepresenting the user's
        // current state.
        const string upgradePendingNoSource =
            "Name           Id              Version  Available\r\n" +
            "----------------------------------------------------\r\n" +
            "Winix.TimeIt   Winix.TimeIt    0.3.0    0.4.0";
        var recorder = new ProcessRecorder(new ProcessResult(0, upgradePendingNoSource, ""));
        var adapter = new WingetAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Equal("0.3.0", version);
        Assert.NotEqual("0.4.0", version);
    }

    [Fact]
    public async Task GetInstalledVersion_MultiWordName_AnchorsOnIdColumn()
    {
        // Defensive: a package whose Name contains spaces (e.g. "Visual Studio")
        // would have shifted the version to a different column index than the
        // Winix-tool case where Name == Id. The Id-anchored scan handles this
        // correctly because Id has no spaces.
        const string multiWordName =
            "Name             Id                       Version\r\n" +
            "------------------------------------------------------\r\n" +
            "Visual Studio    Microsoft.VisualStudio   17.0.0";
        var recorder = new ProcessRecorder(new ProcessResult(0, multiWordName, ""));
        var adapter = new WingetAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Microsoft.VisualStudio");

        Assert.Equal("17.0.0", version);
    }

    // ── GetInstalled / ParseListOutput ──────────────────────────────────────
    //
    // The bulk path replaces the per-tool IsInstalled/GetInstalledVersion loop
    // with a single `winget list` (no filter) parsed into a dictionary keyed by
    // Id. Pre-bulk the suite-wide flows (winix list / status / uninstall) took
    // 5-7 minutes against real winget; post-bulk they complete in ~8 seconds.

    [Fact]
    public async Task GetInstalled_ConstructsListWithoutFilter()
    {
        // Verify the bulk path uses the unfiltered `winget list` form, NOT
        // `winget list --id X --exact` per-tool. The unfiltered call lets winget
        // walk its index once for all packages instead of once per package.
        var recorder = new ProcessRecorder(new ProcessResult(0, "", ""));
        var adapter = new WingetAdapter(recorder.RunAsync);

        await adapter.GetInstalled();

        Assert.Equal("winget", recorder.LastCommand);
        Assert.Equal(new[] { "list" }, recorder.LastArguments);
    }

    [Fact]
    public async Task GetInstalled_NonZeroExitCode_ReturnsEmptySnapshot()
    {
        // Adapter contract on failure: empty snapshot, not throw. Caller treats
        // every tool as not-installed — matches the per-package path where
        // IsInstalled returns false on non-zero exit.
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "winget index unavailable"));
        var adapter = new WingetAdapter(recorder.RunAsync);

        IReadOnlyDictionary<string, string?> snapshot = await adapter.GetInstalled();

        Assert.Empty(snapshot);
    }

    [Fact]
    public void ParseListOutput_3ColumnShape_PopulatesIdAndVersion()
    {
        // The minimal "happy path" shape: Name | Id | Version, no upgrades pending.
        const string output =
            "Name     Id              Version\r\n" +
            "---------------------------------\r\n" +
            "timeit   Winix.TimeIt    0.2.0\r\n" +
            "squeeze  Winix.Squeeze   0.1.5";

        IReadOnlyDictionary<string, string?> result = WingetAdapter.ParseListOutput(output);

        Assert.Equal(2, result.Count);
        Assert.Equal("0.2.0", result["Winix.TimeIt"]);
        Assert.Equal("0.1.5", result["Winix.Squeeze"]);
    }

    [Fact]
    public void ParseListOutput_5ColumnShape_VersionIsInstalledNotAvailableNotSource()
    {
        // Mirrors the GetInstalledVersion SFH-C1 case at the bulk level: when ANY
        // package has an upgrade pending, winget appends Available + Source columns
        // for every row. Version cell must be the INSTALLED version, not the
        // available version, and certainly not the source name "winix".
        const string output =
            "Name           Id              Version  Available  Source\r\n" +
            "------------------------------------------------------------\r\n" +
            "timeit         Winix.TimeIt    0.3.0    0.4.0      winix\r\n" +
            "squeeze        Winix.Squeeze   0.1.5                       ";

        IReadOnlyDictionary<string, string?> result = WingetAdapter.ParseListOutput(output);

        Assert.Equal("0.3.0", result["Winix.TimeIt"]);
        Assert.NotEqual("0.4.0", result["Winix.TimeIt"]);
        Assert.NotEqual("winix", result["Winix.TimeIt"]);
        Assert.Equal("0.1.5", result["Winix.Squeeze"]);
    }

    [Fact]
    public void ParseListOutput_SpinnerPrefix_HeaderDetectionSkipsGarbage()
    {
        // winget streams progress glyphs (-\|/) BEFORE the header line. Header
        // detection scans for "Name…" followed by "---…" to skip this garbage.
        // The spinner length varies — sometimes a few dozen chars, sometimes
        // hundreds — but the structure is always "garbage line(s), then real table".
        const string output =
            "  -  \r\n" +
            "  \\ \r\n" +
            "  | \r\n" +
            "  / \r\n" +
            "Name     Id              Version\r\n" +
            "---------------------------------\r\n" +
            "timeit   Winix.TimeIt    0.2.0";

        IReadOnlyDictionary<string, string?> result = WingetAdapter.ParseListOutput(output);

        Assert.Single(result);
        Assert.Equal("0.2.0", result["Winix.TimeIt"]);
    }

    [Fact]
    public void ParseListOutput_MultiWordName_DoesNotCorruptIdOrVersion()
    {
        // A Name with embedded spaces ("Visual Studio") would defeat a
        // whitespace-split parser. Fixed-width slicing on header column offsets
        // is robust because the Name column has a known boundary regardless of
        // how many tokens its value contains.
        const string output =
            "Name             Id                       Version\r\n" +
            "------------------------------------------------------\r\n" +
            "Visual Studio    Microsoft.VisualStudio   17.0.0\r\n" +
            "Git              Git.Git                  2.43.0";

        IReadOnlyDictionary<string, string?> result = WingetAdapter.ParseListOutput(output);

        Assert.Equal("17.0.0", result["Microsoft.VisualStudio"]);
        Assert.Equal("2.43.0", result["Git.Git"]);
    }

    [Fact]
    public void ParseListOutput_EmptyVersionCell_StoresNullValue()
    {
        // winget can emit a row with a blank Version cell for ARP-detected
        // packages where the registry entry has no DisplayVersion. The parser
        // surfaces null rather than treating absence as "not in snapshot" —
        // membership (IsInstalled) and version (GetInstalledVersion) are
        // separate concerns and the bulk caller needs to differentiate them.
        const string output =
            "Name        Id              Version\r\n" +
            "----------------------------------------\r\n" +
            "tool-a      Some.Package    \r\n" +
            "tool-b      Other.Package   1.2.3";

        IReadOnlyDictionary<string, string?> result = WingetAdapter.ParseListOutput(output);

        Assert.True(result.ContainsKey("Some.Package"));
        Assert.Null(result["Some.Package"]);
        Assert.Equal("1.2.3", result["Other.Package"]);
    }

    [Fact]
    public void ParseListOutput_LookupIsCaseInsensitive()
    {
        // winget preserves the published case (e.g. "Microsoft.VisualStudio").
        // Manifests can use slightly different casing across tools, so the
        // snapshot is keyed case-insensitively to avoid forcing every adapter +
        // every manifest entry into a single canonical case.
        const string output =
            "Name     Id              Version\r\n" +
            "---------------------------------\r\n" +
            "timeit   Winix.TimeIt    0.2.0";

        IReadOnlyDictionary<string, string?> result = WingetAdapter.ParseListOutput(output);

        Assert.Equal("0.2.0", result["winix.timeit"]);
        Assert.Equal("0.2.0", result["WINIX.TIMEIT"]);
        Assert.Equal("0.2.0", result["Winix.TimeIt"]);
    }

    [Fact]
    public void ParseListOutput_NoHeader_ReturnsEmpty()
    {
        // Defensive: if winget changes its output shape (or emits an error to
        // stdout), header detection fails and we return empty rather than
        // attempting to slice arbitrary lines at column offsets we never
        // computed.
        const string output =
            "Some unexpected text without the Name/--- header pair.\r\n" +
            "Another line.";

        IReadOnlyDictionary<string, string?> result = WingetAdapter.ParseListOutput(output);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseListOutput_TruncatedRowShorterThanIdColumn_Skipped()
    {
        // A row physically too short to contain the Id column (e.g. a stray
        // blank line emitted between data rows) must be skipped, not throw an
        // out-of-range slice. Robustness against winget output variations.
        const string output =
            "Name     Id              Version\r\n" +
            "---------------------------------\r\n" +
            "timeit   Winix.TimeIt    0.2.0\r\n" +
            "x\r\n" +
            "squeeze  Winix.Squeeze   0.1.5";

        IReadOnlyDictionary<string, string?> result = WingetAdapter.ParseListOutput(output);

        Assert.Equal(2, result.Count);
        Assert.Equal("0.2.0", result["Winix.TimeIt"]);
        Assert.Equal("0.1.5", result["Winix.Squeeze"]);
    }
}
