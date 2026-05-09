#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Winix.Winix;
using Xunit;

namespace Winix.Winix.Tests;

/// <summary>
/// Orchestration-layer tests for <see cref="Cli.RunAsync"/>. Pin contracts that
/// previously lived only in <c>winix/Program.cs</c> with no test coverage:
/// the --via whitelist, exit-code routing for no-command / unknown-command /
/// no-package-manager / manifest-parse-failure, F3 --json to stdout, and F10
/// "(via X)" annotation drop on not-in-manifest errors.
///
/// Round-1 fresh-eyes 2026-05-09 closure for code-reviewer I1 (no Cli.Run seam)
/// and pr-test-analyzer I2 (F4 exit codes 126/127), I3 (F10 no-via wiring), and
/// W1 (F3 --json to stdout).
/// </summary>
public sealed class CliTests
{
    private static (StringWriter stdout, StringWriter stderr) Sinks()
    {
        return (new StringWriter(), new StringWriter());
    }

    private static ToolManifest BuildManifest(params (string Name, string WingetId)[] tools)
    {
        // Use ToolManifest.Parse — the class has only a private ctor, so synthesise
        // a JSON document equivalent to the requested manifest.
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"version\":\"0.4.0\",\"tools\":{");
        for (int i = 0; i < tools.Length; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append($"\"{tools[i].Name}\":{{\"description\":\"{tools[i].Name} description\",\"packages\":{{\"winget\":\"{tools[i].WingetId}\"}}}}");
        }
        sb.Append("}}");
        return ToolManifest.Parse(sb.ToString());
    }

    private static Func<TextWriter?, Task<ToolManifest>> StubLoader(ToolManifest manifest)
    {
        return _ => Task.FromResult(manifest);
    }

    private static Func<TextWriter?, Task<ToolManifest>> ThrowingLoader(string message)
    {
        return _ => Task.FromException<ToolManifest>(new ManifestParseException(message));
    }

    // ── --via whitelist: invalid value → UsageError (125) ─────────────────────────

    [Fact]
    public async Task RunAsync_InvalidViaValue_ReturnsUsageErrorWithMessage()
    {
        var (stdout, stderr) = Sinks();

        int exit = await Cli.RunAsync(
            new[] { "--via", "bogus-pm", "install" },
            stdout, stderr,
            adapters: new Dictionary<string, IPackageManagerAdapter>(),
            platform: PlatformId.Linux,
            manifestLoader: StubLoader(BuildManifest()));

        Assert.Equal(WinixExitCode.UsageError, exit);
        Assert.Contains("invalid --via value", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("'bogus-pm'", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── No command → UsageError ────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoCommand_ReturnsUsageErrorWithMessage()
    {
        var (stdout, stderr) = Sinks();

        int exit = await Cli.RunAsync(
            Array.Empty<string>(),
            stdout, stderr,
            adapters: new Dictionary<string, IPackageManagerAdapter>(),
            platform: PlatformId.Linux,
            manifestLoader: StubLoader(BuildManifest()));

        Assert.Equal(WinixExitCode.UsageError, exit);
        Assert.Contains("missing command", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_ReturnsUsageErrorWithMessage()
    {
        var (stdout, stderr) = Sinks();

        int exit = await Cli.RunAsync(
            new[] { "frobnicate" },
            stdout, stderr,
            adapters: new Dictionary<string, IPackageManagerAdapter>(),
            platform: PlatformId.Linux,
            manifestLoader: StubLoader(BuildManifest()));

        Assert.Equal(WinixExitCode.UsageError, exit);
        Assert.Contains("unknown command", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("'frobnicate'", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── F4 documented exit codes 126 / 127 ─────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoAdapterAvailableWithoutOverride_ReturnsNoPackageManager126()
    {
        // Round-1 fresh-eyes 2026-05-09 pr-test-analyzer I2 closure: F4 exit
        // codes 126/127 were documented but never asserted. A regression
        // changing WinixExitCode.NoPackageManager to a different integer would
        // not have failed any test pre-fix.
        var (stdout, stderr) = Sinks();

        int exit = await Cli.RunAsync(
            new[] { "install" },
            stdout, stderr,
            adapters: new Dictionary<string, IPackageManagerAdapter>(),  // empty: no PM available
            platform: PlatformId.Linux,
            manifestLoader: StubLoader(BuildManifest(("timeit", "Winix.TimeIt"))));

        Assert.Equal(WinixExitCode.NoPackageManager, exit);
        Assert.Equal(126, exit);  // explicit pin: documented value must not drift
        Assert.Contains("no supported package manager", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_NoAdapterAvailableWithOverride_ReturnsNoPackageManager126WithSpecificMessage()
    {
        // Inverse case: the override-was-specified-but-not-available branch emits
        // a different message naming the override. Both branches must return 126.
        var (stdout, stderr) = Sinks();

        int exit = await Cli.RunAsync(
            new[] { "--via", "scoop", "install" },
            stdout, stderr,
            adapters: new Dictionary<string, IPackageManagerAdapter>(),
            platform: PlatformId.Linux,
            manifestLoader: StubLoader(BuildManifest(("timeit", "Winix.TimeIt"))));

        Assert.Equal(WinixExitCode.NoPackageManager, exit);
        Assert.Equal(126, exit);
        Assert.Contains("'scoop'", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("not available", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ManifestParseException_ReturnsInternalError127()
    {
        // F4: manifest fetch/parse failure routes to 127 (InternalError).
        var (stdout, stderr) = Sinks();

        int exit = await Cli.RunAsync(
            new[] { "install" },
            stdout, stderr,
            adapters: new Dictionary<string, IPackageManagerAdapter>(),
            platform: PlatformId.Linux,
            manifestLoader: ThrowingLoader("Failed to download manifest from 'http://example/...'"));

        Assert.Equal(WinixExitCode.InternalError, exit);
        Assert.Equal(127, exit);  // explicit pin
        Assert.Contains("Failed to download manifest", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── F3 --json to stdout (suite convention) ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_ListJson_RoutesToStdoutNotStderr()
    {
        // Round-1 fresh-eyes 2026-05-09 pr-test-analyzer W1 closure: F3 contract
        // (--json to stdout) was unguarded. A regression flipping the routing
        // back to stderr would silently break `winix list --json | jq` consumers.
        var (stdout, stderr) = Sinks();

        var stubAdapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", new StubAdapter("winget") },
        };

        int exit = await Cli.RunAsync(
            new[] { "--json", "list" },
            stdout, stderr,
            adapters: stubAdapters,
            platform: PlatformId.Windows,
            manifestLoader: StubLoader(BuildManifest(("timeit", "Winix.TimeIt"))));

        Assert.Equal(WinixExitCode.Success, exit);
        // JSON went to stdout, not stderr.
        string out_ = stdout.ToString();
        Assert.Contains("\"tool\":\"winix\"", out_, StringComparison.Ordinal);
        Assert.Contains("\"command\":\"list\"", out_, StringComparison.Ordinal);
        // No human-readable progress / list output should leak into stderr when
        // --json was the request.
        Assert.DoesNotContain("✓", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("✗", stderr.ToString(), StringComparison.Ordinal);
    }

    // ── pr-test I3 closure: F10 (via X) annotation drop wired through orchestration ─

    [Fact]
    public async Task RunAsync_InstallUnknownTool_EmitsNotInManifestWithoutViaAnnotation()
    {
        // Round-1 fresh-eyes 2026-05-09 pr-test-analyzer I3 closure: F10's
        // FormatToolError is unit-tested in isolation, but the wiring — that
        // SuiteManager calls FormatToolError (no via) for unknown tool names
        // rather than the "(via X)"-emitting FormatToolResult — was unpinned.
        // A refactor flipping back to the pre-F10 shape would silently re-
        // introduce the misleading "(via winget)" annotation on errors that
        // have nothing to do with the package manager.
        var (stdout, stderr) = Sinks();

        var stubAdapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", new StubAdapter("winget") },
        };

        int exit = await Cli.RunAsync(
            new[] { "install", "bogus-tool-not-in-manifest" },
            stdout, stderr,
            adapters: stubAdapters,
            platform: PlatformId.Windows,
            manifestLoader: StubLoader(BuildManifest(("timeit", "Winix.TimeIt"))));

        // The orchestration completes (manifest parsed, adapter resolved, then
        // SuiteManager iterates the requested tool list and fails on the
        // unknown one). Exit code is ToolFailure.
        Assert.Equal(WinixExitCode.ToolFailure, exit);

        string err = stderr.ToString();
        Assert.Contains("bogus-tool-not-in-manifest", err, StringComparison.Ordinal);
        Assert.Contains("not in manifest", err, StringComparison.Ordinal);
        // F10 contract: NO "(via winget)" or any other "(via ...)" annotation
        // on a not-in-manifest error. The adapter wasn't even consulted —
        // surfacing "via X" misleads the user about where the error came from.
        Assert.DoesNotContain("(via", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_List_EmitsQueryingProgressLineToStderr()
    {
        // Pre-fix `winix list` was silent for ~8s while winget walked its index.
        // The progress line tells the user which PM is currently being queried.
        // Suppression under --json is verified separately below.
        var (stdout, stderr) = Sinks();

        var stubAdapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", new StubAdapter("winget") },
        };

        int exit = await Cli.RunAsync(
            new[] { "list" },
            stdout, stderr,
            adapters: stubAdapters,
            platform: PlatformId.Windows,
            manifestLoader: StubLoader(BuildManifest(("timeit", "Winix.TimeIt"))));

        Assert.Equal(WinixExitCode.Success, exit);
        string err = stderr.ToString();
        Assert.Contains("winix: querying winget", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ListJson_DoesNotEmitProgressLineToStderr()
    {
        // F3 + UX rule: under --json, no human-progress lines on stderr — the
        // expected stderr surface is errors only. A user piping `winix list --json
        // | jq` won't typically see stderr but the convention still holds:
        // suppress non-error stderr text in machine-mode.
        var (stdout, stderr) = Sinks();

        var stubAdapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", new StubAdapter("winget") },
        };

        int exit = await Cli.RunAsync(
            new[] { "--json", "list" },
            stdout, stderr,
            adapters: stubAdapters,
            platform: PlatformId.Windows,
            manifestLoader: StubLoader(BuildManifest(("timeit", "Winix.TimeIt"))));

        Assert.Equal(WinixExitCode.Success, exit);
        string err = stderr.ToString();
        Assert.DoesNotContain("querying", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_StatusJson_RoutesToStdoutNotStderr()
    {
        // Same contract for the status command.
        var (stdout, stderr) = Sinks();

        var stubAdapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", new StubAdapter("winget") },
        };

        int exit = await Cli.RunAsync(
            new[] { "--json", "status" },
            stdout, stderr,
            adapters: stubAdapters,
            platform: PlatformId.Windows,
            manifestLoader: StubLoader(BuildManifest(("timeit", "Winix.TimeIt"))));

        Assert.Equal(WinixExitCode.Success, exit);
        Assert.Contains("\"tool\":\"winix\"", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"command\":\"status\"", stdout.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Minimal stub adapter for tests — reports "not installed" for every query
    /// without spawning a real process.
    /// </summary>
    private sealed class StubAdapter : IPackageManagerAdapter
    {
        public StubAdapter(string name) => Name = name;
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
