#nullable enable

using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class BrewAdapterTests
{
    private const string TapListWithWinix =
        "homebrew/core\r\n" +
        "yortw/winix\r\n" +
        "homebrew/cask";

    private const string TapListWithoutWinix =
        "homebrew/core\r\n" +
        "homebrew/cask";

    [Fact]
    public void Name_IsBrew()
    {
        var adapter = new BrewAdapter();

        Assert.Equal("brew", adapter.Name);
    }

    [Fact]
    public async Task Install_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new BrewAdapter(recorder.RunAsync);

        await adapter.Install("timeit");

        Assert.Equal("brew", recorder.LastCommand);
        Assert.Equal(new[] { "install", "yortw/winix/timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Update_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new BrewAdapter(recorder.RunAsync);

        await adapter.Update("timeit");

        Assert.Equal("brew", recorder.LastCommand);
        Assert.Equal(new[] { "upgrade", "yortw/winix/timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Uninstall_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new BrewAdapter(recorder.RunAsync);

        await adapter.Uninstall("timeit");

        Assert.Equal("brew", recorder.LastCommand);
        Assert.Equal(new[] { "uninstall", "timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task IsInstalled_WhenListSucceeds_ReturnsTrue()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, "timeit 0.2.0", ""));
        var adapter = new BrewAdapter(recorder.RunAsync);

        bool result = await adapter.IsInstalled("timeit");

        Assert.True(result);
    }

    [Fact]
    public async Task IsInstalled_WhenListFails_ReturnsFalse()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "Error: No such keg: timeit"));
        var adapter = new BrewAdapter(recorder.RunAsync);

        bool result = await adapter.IsInstalled("timeit");

        Assert.False(result);
    }

    [Fact]
    public async Task GetInstalledVersion_ParsesVersion()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, "0.2.0", ""));
        var adapter = new BrewAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("timeit");

        Assert.Equal("0.2.0", version);
    }

    [Fact]
    public async Task EnsureTap_WhenTapMissing_AddsTap()
    {
        var calls = new List<(string Command, string[] Args)>();

        Task<ProcessResult> FakeRun(string command, string[] args)
        {
            calls.Add((command, args));

            // First call: tap list — returns list without yortw/winix.
            // Subsequent calls: tap add — returns success.
            if (args.Length >= 1 && args[0] == "tap")
            {
                return Task.FromResult(new ProcessResult(0, TapListWithoutWinix, ""));
            }

            return Task.FromResult(new ProcessResult(0, "", ""));
        }

        var adapter = new BrewAdapter(FakeRun);

        bool added = await adapter.EnsureTap();

        // Should have called tap (list) and then tap yortw/winix (add).
        Assert.Equal(2, calls.Count);
        Assert.Equal("brew", calls[0].Command);
        Assert.Equal(new[] { "tap" }, calls[0].Args);
        Assert.Equal("brew", calls[1].Command);
        Assert.Equal(new[] { "tap", "yortw/winix" }, calls[1].Args);
        // F7 contract: return true so the CLI can emit the one-time first-run notice.
        Assert.True(added);
    }

    [Fact]
    public async Task EnsureTap_WhenTapExists_DoesNothing()
    {
        var calls = new List<(string Command, string[] Args)>();

        Task<ProcessResult> FakeRun(string command, string[] args)
        {
            calls.Add((command, args));
            return Task.FromResult(new ProcessResult(0, TapListWithWinix, ""));
        }

        var adapter = new BrewAdapter(FakeRun);

        bool added = await adapter.EnsureTap();

        // Should only have called tap (list) — no tap add.
        (string Command, string[] Args) onlyCall = Assert.Single(calls);
        Assert.Equal("brew", onlyCall.Command);
        Assert.Equal(new[] { "tap" }, onlyCall.Args);
        // F7 contract: return false so the CLI stays quiet when nothing changed.
        Assert.False(added);
    }

    [Fact]
    public async Task EnsureTap_WhenAddFails_ThrowsInsteadOfClaimingSuccess()
    {
        // Round-1 fresh-eyes 2026-05-09 SFH-I3 + CR-I2 closure: same defect
        // class as ScoopAdapter.EnsureBucket — pre-fix the `brew tap` exit
        // code was discarded; EnsureTap returned true unconditionally. Now
        // non-zero exit surfaces as an exception so the caller's existing
        // catch produces a "could not add brew tap" warning instead of a
        // misleading positive notice.
        Task<ProcessResult> FakeRun(string command, string[] args)
        {
            if (args.Length == 1 && args[0] == "tap")
            {
                return Task.FromResult(new ProcessResult(0, TapListWithoutWinix, ""));
            }
            // tap add fails: simulate network unreachable.
            return Task.FromResult(new ProcessResult(1, "", "Error: Failure while executing; `git clone`"));
        }

        var adapter = new BrewAdapter(FakeRun);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.EnsureTap());
        Assert.Contains("brew tap", ex.Message, StringComparison.Ordinal);
        Assert.Contains("exit code 1", ex.Message, StringComparison.Ordinal);
    }

    // ── GetInstalled / ParseListOutput ──────────────────────────────────────

    [Fact]
    public async Task GetInstalled_ConstructsListVersionsWithoutFilter()
    {
        // Bulk path uses `brew list --versions` (no formula). The unfiltered call
        // returns one row per installed formula, which the parser splits on whitespace.
        var recorder = new ProcessRecorder(new ProcessResult(0, "", ""));
        var adapter = new BrewAdapter(recorder.RunAsync);

        await adapter.GetInstalled();

        Assert.Equal("brew", recorder.LastCommand);
        Assert.Equal(new[] { "list", "--versions" }, recorder.LastArguments);
    }

    [Fact]
    public async Task GetInstalled_NonZeroExitCode_ReturnsEmptySnapshot()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "brew not on PATH"));
        var adapter = new BrewAdapter(recorder.RunAsync);

        IReadOnlyDictionary<string, string?> snapshot = await adapter.GetInstalled();

        Assert.Empty(snapshot);
    }

    [Fact]
    public void ParseListOutput_HappyPath_PopulatesNameAndVersion()
    {
        // Standard case: each line is "name version", whitespace-separated.
        const string output =
            "timeit 0.2.0\n" +
            "squeeze 0.1.5\n" +
            "git 2.43.0";

        IReadOnlyDictionary<string, string?> result = BrewAdapter.ParseListOutput(output);

        Assert.Equal(3, result.Count);
        Assert.Equal("0.2.0", result["timeit"]);
        Assert.Equal("0.1.5", result["squeeze"]);
        Assert.Equal("2.43.0", result["git"]);
    }

    [Fact]
    public void ParseListOutput_MultipleVersionsOnSameLine_TakesLastToken()
    {
        // brew can report multiple installed versions for the same formula by
        // appending them as additional whitespace-separated tokens
        // (e.g. "openssl@3 3.1.0 3.2.0"). The parser takes the last token to
        // match GetInstalledVersion's "most-recently-installed" convention.
        const string output =
            "openssl@3 3.1.0 3.2.0\n" +
            "python 3.11.0 3.12.0";

        IReadOnlyDictionary<string, string?> result = BrewAdapter.ParseListOutput(output);

        Assert.Equal("3.2.0", result["openssl@3"]);
        Assert.Equal("3.12.0", result["python"]);
    }

    [Fact]
    public void ParseListOutput_NameWithoutVersion_StoresNullValue()
    {
        // Defensive: a malformed brew output line with only a formula name and
        // no version. Surface as "installed but no version" — null on the value,
        // key present — rather than dropping the row entirely.
        const string output =
            "timeit 0.2.0\n" +
            "broken-formula";

        IReadOnlyDictionary<string, string?> result = BrewAdapter.ParseListOutput(output);

        Assert.Equal("0.2.0", result["timeit"]);
        Assert.True(result.ContainsKey("broken-formula"));
        Assert.Null(result["broken-formula"]);
    }

    [Fact]
    public void ParseListOutput_LookupIsCaseInsensitive()
    {
        const string output = "timeit 0.2.0";

        IReadOnlyDictionary<string, string?> result = BrewAdapter.ParseListOutput(output);

        Assert.Equal("0.2.0", result["timeit"]);
        Assert.Equal("0.2.0", result["TIMEIT"]);
        Assert.Equal("0.2.0", result["TimeIt"]);
    }

    [Fact]
    public void ParseListOutput_EmptyOutput_ReturnsEmpty()
    {
        // brew with no installed formulae — common on a fresh machine. Empty
        // snapshot means every Winix manifest tool resolves to "not installed",
        // which is correct.
        IReadOnlyDictionary<string, string?> result = BrewAdapter.ParseListOutput("");

        Assert.Empty(result);
    }
}
