#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Winix.Winix;
using Xunit;

namespace Winix.Winix.Tests;

/// <summary>
/// Regression tests locking winix's --color emission path.
/// Guards against a future regression where colour is silently unwired from the
/// Cli.RunAsync production path (as occurred in trash/hcat/wargs).
/// </summary>
/// <remarks>
/// Colour path: Cli.RunAsync "install" → SuiteManager.ExecuteAsync →
/// Formatting.FormatToolResult(toolName, pmName, success:true, error:null, useColor)
/// → AnsiColor.Green(useColor) + "✓" + AnsiColor.Reset(useColor) → stderr.
/// The stub adapter returns ProcessResult(0,"","") so the success branch always fires,
/// producing a green-check line per tool on stderr.
/// useColor is resolved via result.ResolveColor(checkStdErr: true); --color=always
/// forces useColor=true even to a non-TTY StringWriter, overriding NO_COLOR.
/// Manifest and manifestLoader are fakes so no real process or network call is made.
/// </remarks>
public sealed class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    // Minimal manifest: one tool with a winget package ID.
    private static ToolManifest BuildManifest()
    {
        return ToolManifest.Parse(
            "{\"version\":\"0.3.0\",\"tools\":{\"timeit\":{\"description\":\"Time a command\",\"packages\":{\"winget\":\"Winix.TimeIt\"}}}}");
    }

    private static Func<TextWriter?, Task<ToolManifest>> StubLoader()
    {
        ToolManifest manifest = BuildManifest();
        return _ => Task.FromResult(manifest);
    }

    private static async Task<(int exit, string stdout, string stderr)> RunCliAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        // Stub adapter: IsAvailable=true, Install returns exit 0 → triggers FormatToolResult(success:true).
        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", new SucceedingStubAdapter("winget") },
        };
        int exit = await Cli.RunAsync(
            args,
            stdout,
            stderr,
            adapters: adapters,
            platform: PlatformId.Windows,
            manifestLoader: StubLoader());
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_ColorAlways_InstallResultLineContainsEscape()
    {
        // "install timeit" → SuiteManager.ExecuteAsync → StubAdapter.Install → exit 0
        // → FormatToolResult(success:true, useColor:true) → ESC + "✓" + ESC → stderr.
        var r = await RunCliAsync("--color=always", "install", "timeit");

        Assert.Equal(WinixExitCode.Success, r.exit);
        Assert.Contains(Esc, r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_NoColor_InstallResultLineContainsNoEscape()
    {
        var r = await RunCliAsync("--no-color", "install", "timeit");

        Assert.Equal(WinixExitCode.Success, r.exit);
        // Confirm the result line is still present (not suppressed).
        Assert.Contains("timeit", r.stderr, StringComparison.Ordinal);
        Assert.Contains("✓", r.stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, r.stderr, StringComparison.Ordinal);
    }

    // ── Minimal stub adapter ────────────────────────────────────────────────

    /// <summary>
    /// Stub adapter that always reports itself as available and returns success
    /// for every install/update/uninstall call, without spawning any real process.
    /// </summary>
    private sealed class SucceedingStubAdapter : IPackageManagerAdapter
    {
        public SucceedingStubAdapter(string name) => Name = name;
        public string Name { get; }
        public bool IsAvailable() => true;
        public Task<bool> IsInstalled(string packageId) => Task.FromResult(false);
        public Task<string?> GetInstalledVersion(string packageId) => Task.FromResult<string?>(null);
        public Task<IReadOnlyDictionary<string, string?>> GetInstalled()
            => Task.FromResult<IReadOnlyDictionary<string, string?>>(
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
        public Task<ProcessResult> Install(string packageId) => Task.FromResult(new ProcessResult(0, "", ""));
        public Task<ProcessResult> Update(string packageId) => Task.FromResult(new ProcessResult(0, "", ""));
        public Task<ProcessResult> Uninstall(string packageId) => Task.FromResult(new ProcessResult(0, "", ""));
    }
}
