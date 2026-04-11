#nullable enable

using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class ScoopAdapterTests
{
    // Scoop list output format:
    // Installed apps:
    //
    //   Name   Version Source
    //   ----   ------- ------
    //   timeit 0.2.0   winix
    private const string ListOutputWithVersion =
        "Installed apps:\r\n" +
        "\r\n" +
        "  Name   Version Source\r\n" +
        "  ----   ------- ------\r\n" +
        "  timeit 0.2.0   winix";

    private const string BucketListWithWinix =
        "winix\r\n" +
        "extras\r\n" +
        "main";

    private const string BucketListWithoutWinix =
        "extras\r\n" +
        "main";

    [Fact]
    public void Name_IsScoop()
    {
        var adapter = new ScoopAdapter();

        Assert.Equal("scoop", adapter.Name);
    }

    [Fact]
    public async Task Install_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new ScoopAdapter(recorder.RunAsync);

        await adapter.Install("timeit");

        Assert.Equal("scoop", recorder.LastCommand);
        Assert.Equal(new[] { "install", "timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Update_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new ScoopAdapter(recorder.RunAsync);

        await adapter.Update("timeit");

        Assert.Equal("scoop", recorder.LastCommand);
        Assert.Equal(new[] { "update", "timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Uninstall_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new ScoopAdapter(recorder.RunAsync);

        await adapter.Uninstall("timeit");

        Assert.Equal("scoop", recorder.LastCommand);
        Assert.Equal(new[] { "uninstall", "timeit" }, recorder.LastArguments);
    }

    [Fact]
    public async Task IsInstalled_WhenListSucceeds_ReturnsTrue()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, ListOutputWithVersion, ""));
        var adapter = new ScoopAdapter(recorder.RunAsync);

        bool result = await adapter.IsInstalled("timeit");

        Assert.True(result);
    }

    [Fact]
    public async Task IsInstalled_WhenListFails_ReturnsFalse()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", ""));
        var adapter = new ScoopAdapter(recorder.RunAsync);

        bool result = await adapter.IsInstalled("timeit");

        Assert.False(result);
    }

    [Fact]
    public async Task GetInstalledVersion_ParsesVersion()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, ListOutputWithVersion, ""));
        var adapter = new ScoopAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("timeit");

        Assert.Equal("0.2.0", version);
    }

    [Fact]
    public async Task EnsureBucket_WhenBucketMissing_AddsBucket()
    {
        var calls = new List<(string Command, string[] Args)>();

        Task<ProcessResult> FakeRun(string command, string[] args)
        {
            calls.Add((command, args));

            // First call: bucket list — returns list without winix.
            // Subsequent calls: bucket add — returns success.
            if (args.Length >= 2 && args[0] == "bucket" && args[1] == "list")
            {
                return Task.FromResult(new ProcessResult(0, BucketListWithoutWinix, ""));
            }

            return Task.FromResult(new ProcessResult(0, "", ""));
        }

        var adapter = new ScoopAdapter(FakeRun);

        await adapter.EnsureBucket();

        // Should have called bucket list and then bucket add.
        Assert.Equal(2, calls.Count);
        Assert.Equal("scoop", calls[0].Command);
        Assert.Equal(new[] { "bucket", "list" }, calls[0].Args);
        Assert.Equal("scoop", calls[1].Command);
        Assert.Equal(new[] { "bucket", "add", "winix", "https://github.com/Yortw/winix" }, calls[1].Args);
    }

    [Fact]
    public async Task EnsureBucket_WhenBucketExists_DoesNothing()
    {
        var calls = new List<(string Command, string[] Args)>();

        Task<ProcessResult> FakeRun(string command, string[] args)
        {
            calls.Add((command, args));
            return Task.FromResult(new ProcessResult(0, BucketListWithWinix, ""));
        }

        var adapter = new ScoopAdapter(FakeRun);

        await adapter.EnsureBucket();

        // Should only have called bucket list — no bucket add.
        (string Command, string[] Args) onlyCall = Assert.Single(calls);
        Assert.Equal("scoop", onlyCall.Command);
        Assert.Equal(new[] { "bucket", "list" }, onlyCall.Args);
    }
}
