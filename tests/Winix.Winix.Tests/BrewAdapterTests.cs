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

        await adapter.EnsureTap();

        // Should have called tap (list) and then tap yortw/winix (add).
        Assert.Equal(2, calls.Count);
        Assert.Equal("brew", calls[0].Command);
        Assert.Equal(new[] { "tap" }, calls[0].Args);
        Assert.Equal("brew", calls[1].Command);
        Assert.Equal(new[] { "tap", "yortw/winix" }, calls[1].Args);
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

        await adapter.EnsureTap();

        // Should only have called tap (list) — no tap add.
        (string Command, string[] Args) onlyCall = Assert.Single(calls);
        Assert.Equal("brew", onlyCall.Command);
        Assert.Equal(new[] { "tap" }, onlyCall.Args);
    }
}
