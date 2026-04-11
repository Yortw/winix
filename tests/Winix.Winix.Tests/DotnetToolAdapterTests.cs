#nullable enable

using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class DotnetToolAdapterTests
{
    // dotnet tool list -g output format:
    // Package Id      Version      Commands
    // -------------------------------------------
    // winix.timeit    0.2.0        timeit
    // winix.squeeze   0.2.0        squeeze
    //
    // Note: package IDs are always lowercase in dotnet tool list output even
    // though the NuGet package IDs use mixed case (e.g. "Winix.TimeIt").
    private const string ListOutputWithTimeIt =
        "Package Id      Version      Commands\r\n" +
        "-------------------------------------------\r\n" +
        "winix.timeit    0.2.0        timeit\r\n" +
        "winix.squeeze   0.2.0        squeeze";

    private const string ListOutputWithoutTimeIt =
        "Package Id      Version      Commands\r\n" +
        "-------------------------------------------\r\n" +
        "winix.squeeze   0.2.0        squeeze";

    [Fact]
    public void Name_IsDotnet()
    {
        var adapter = new DotnetToolAdapter();

        Assert.Equal("dotnet", adapter.Name);
    }

    [Fact]
    public async Task Install_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        await adapter.Install("Winix.TimeIt");

        Assert.Equal("dotnet", recorder.LastCommand);
        Assert.Equal(new[] { "tool", "install", "-g", "Winix.TimeIt" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Update_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        await adapter.Update("Winix.TimeIt");

        Assert.Equal("dotnet", recorder.LastCommand);
        Assert.Equal(new[] { "tool", "update", "-g", "Winix.TimeIt" }, recorder.LastArguments);
    }

    [Fact]
    public async Task Uninstall_ConstructsCorrectArguments()
    {
        var recorder = new ProcessRecorder();
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        await adapter.Uninstall("Winix.TimeIt");

        Assert.Equal("dotnet", recorder.LastCommand);
        Assert.Equal(new[] { "tool", "uninstall", "-g", "Winix.TimeIt" }, recorder.LastArguments);
    }

    [Fact]
    public async Task IsInstalled_WhenToolInList_ReturnsTrue()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, ListOutputWithTimeIt, ""));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        bool result = await adapter.IsInstalled("Winix.TimeIt");

        Assert.True(result);
    }

    [Fact]
    public async Task IsInstalled_WhenToolNotInList_ReturnsFalse()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, ListOutputWithoutTimeIt, ""));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        bool result = await adapter.IsInstalled("Winix.TimeIt");

        Assert.False(result);
    }

    [Fact]
    public async Task GetInstalledVersion_ParsesVersion()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, ListOutputWithTimeIt, ""));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Equal("0.2.0", version);
    }

    [Fact]
    public async Task GetInstalledVersion_NotInstalled_ReturnsNull()
    {
        var recorder = new ProcessRecorder(new ProcessResult(0, ListOutputWithoutTimeIt, ""));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        string? version = await adapter.GetInstalledVersion("Winix.TimeIt");

        Assert.Null(version);
    }
}
