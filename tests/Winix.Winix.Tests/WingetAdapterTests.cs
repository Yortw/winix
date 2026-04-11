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
}
