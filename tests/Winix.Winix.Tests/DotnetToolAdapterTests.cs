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

    // ── GetInstalled / ParseListOutput ──────────────────────────────────────

    [Fact]
    public async Task GetInstalled_ConstructsToolListGlobal()
    {
        // Bulk path runs `dotnet tool list -g` once. dotnet's per-package query
        // and the global-list query are the same command — there's no per-package
        // filter — so the bulk saving here is "1 process spawn instead of N", not
        // a query-shape change.
        var recorder = new ProcessRecorder(new ProcessResult(0, "", ""));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        await adapter.GetInstalled();

        Assert.Equal("dotnet", recorder.LastCommand);
        Assert.Equal(new[] { "tool", "list", "-g" }, recorder.LastArguments);
    }

    [Fact]
    public async Task GetInstalled_NonZeroExitCode_ReturnsEmptySnapshot()
    {
        var recorder = new ProcessRecorder(new ProcessResult(1, "", "dotnet sdk not installed"));
        var adapter = new DotnetToolAdapter(recorder.RunAsync);

        IReadOnlyDictionary<string, string?> snapshot = await adapter.GetInstalled();

        Assert.Empty(snapshot);
    }

    [Fact]
    public void ParseListOutput_HappyPath_PopulatesIdAndVersion()
    {
        // dotnet's tabular output uses fixed-column widths but is splittable on
        // whitespace because Package Ids never contain spaces. The parser takes
        // parts[0] (id) and parts[1] (version), skipping the "Package Id" header
        // and "---" separator rows.
        const string output =
            "Package Id      Version    Commands\r\n" +
            "----------------------------------------\r\n" +
            "winix.timeit    0.2.0      timeit\r\n" +
            "winix.squeeze   0.1.5      squeeze";

        IReadOnlyDictionary<string, string?> result = DotnetToolAdapter.ParseListOutput(output);

        Assert.Equal(2, result.Count);
        Assert.Equal("0.2.0", result["winix.timeit"]);
        Assert.Equal("0.1.5", result["winix.squeeze"]);
    }

    [Fact]
    public void ParseListOutput_LookupNormalisesPublishedCase()
    {
        // dotnet CLI lowercases every package id regardless of how it was
        // published. Manifests use the published case ("Winix.TimeIt"), so the
        // case-insensitive dictionary is what bridges them — without it, a
        // perfectly-installed tool would resolve to "not installed" purely
        // because of a casing mismatch.
        const string output =
            "Package Id      Version    Commands\r\n" +
            "----------------------------------------\r\n" +
            "winix.timeit    0.2.0      timeit";

        IReadOnlyDictionary<string, string?> result = DotnetToolAdapter.ParseListOutput(output);

        Assert.Equal("0.2.0", result["Winix.TimeIt"]);
        Assert.Equal("0.2.0", result["WINIX.TIMEIT"]);
        Assert.Equal("0.2.0", result["winix.timeit"]);
    }

    [Fact]
    public void ParseListOutput_HeaderAndSeparator_AreSkipped()
    {
        // The "Package Id" header line splits to 3 tokens — we mustn't accidentally
        // store it as a fake package. The "---" separator likewise. Both should
        // be filtered before the parts.Length-based ingest.
        const string output =
            "Package Id      Version    Commands\r\n" +
            "----------------------------------------\r\n" +
            "winix.timeit    0.2.0      timeit";

        IReadOnlyDictionary<string, string?> result = DotnetToolAdapter.ParseListOutput(output);

        Assert.False(result.ContainsKey("Package"));
        Assert.False(result.ContainsKey("---"));
        Assert.Single(result);
    }

    [Fact]
    public void ParseListOutput_EmptyToolList_ReturnsEmpty()
    {
        // `dotnet tool list -g` with no installed tools emits just the header.
        // Snapshot must be empty (not throw, not invent rows).
        const string output =
            "Package Id      Version    Commands\r\n" +
            "----------------------------------------\r\n";

        IReadOnlyDictionary<string, string?> result = DotnetToolAdapter.ParseListOutput(output);

        Assert.Empty(result);
    }
}
