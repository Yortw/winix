#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Winix.Winix;

/// <summary>
/// Orchestrates install, update, uninstall, and list operations across all
/// Winix suite tools. Connects the manifest, package-manager adapters, and
/// output formatting into coherent operations callable by the console entry point.
/// </summary>
public sealed class SuiteManager
{
    private readonly ToolManifest _manifest;
    private readonly IDictionary<string, IPackageManagerAdapter> _adapters;
    private readonly PlatformId _platform;

    /// <summary>
    /// Initialises a <see cref="SuiteManager"/> with a single adapter, used for
    /// install and update operations where only one package manager is needed.
    /// </summary>
    /// <param name="manifest">The Winix suite manifest listing all tools.</param>
    /// <param name="adapter">The package-manager adapter to use for install/update.</param>
    public SuiteManager(ToolManifest manifest, IPackageManagerAdapter adapter)
    {
        _manifest = manifest;
        _adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { adapter.Name, adapter },
        };
        _platform = PlatformDetector.GetCurrentPlatform();
    }

    /// <summary>
    /// Initialises a <see cref="SuiteManager"/> with the full set of adapters
    /// and a known platform, used for list and uninstall operations where the
    /// full platform chain needs to be walked.
    /// </summary>
    /// <param name="manifest">The Winix suite manifest listing all tools.</param>
    /// <param name="adapters">All registered adapters, keyed by adapter name.</param>
    /// <param name="platform">The platform whose preference chain governs adapter selection.</param>
    public SuiteManager(ToolManifest manifest, IDictionary<string, IPackageManagerAdapter> adapters, PlatformId platform)
    {
        _manifest = manifest;
        _adapters = adapters;
        _platform = platform;
    }

    /// <summary>
    /// Installs one or more tools from the suite.
    /// </summary>
    /// <param name="toolNames">
    /// The short names of tools to install, or <see langword="null"/> to install all
    /// tools in the manifest.
    /// </param>
    /// <param name="dryRun">
    /// When <see langword="true"/>, no packages are installed — output shows the
    /// commands that would be run.
    /// </param>
    /// <param name="useColor">Whether to emit ANSI colour codes in output lines.</param>
    /// <param name="output">Callback invoked once per tool with a formatted result line.</param>
    /// <returns>
    /// <c>0</c> when all installs succeed (or dry-run); <c>1</c> when at least one fails.
    /// </returns>
    public Task<int> InstallAsync(string[]? toolNames, bool dryRun, bool useColor, Action<string> output)
    {
        return ExecuteAsync(toolNames, (adapter, packageId) => adapter.Install(packageId), dryRun, useColor, output);
    }

    /// <summary>
    /// Updates one or more tools in the suite.
    /// </summary>
    /// <param name="toolNames">
    /// The short names of tools to update, or <see langword="null"/> to update all
    /// tools in the manifest.
    /// </param>
    /// <param name="dryRun">
    /// When <see langword="true"/>, no packages are updated — output shows the
    /// commands that would be run.
    /// </param>
    /// <param name="useColor">Whether to emit ANSI colour codes in output lines.</param>
    /// <param name="output">Callback invoked once per tool with a formatted result line.</param>
    /// <returns>
    /// <c>0</c> when all updates succeed (or dry-run); <c>1</c> when at least one fails.
    /// </returns>
    public Task<int> UpdateAsync(string[]? toolNames, bool dryRun, bool useColor, Action<string> output)
    {
        return ExecuteAsync(toolNames, (adapter, packageId) => adapter.Update(packageId), dryRun, useColor, output);
    }

    /// <summary>
    /// Uninstalls one or more tools from the suite.
    /// For each tool, the platform chain is probed to discover which package manager
    /// owns it before uninstalling. Tools not found via any package manager are
    /// reported as errors.
    /// </summary>
    /// <param name="toolNames">
    /// The short names of tools to uninstall, or <see langword="null"/> to uninstall
    /// all tools in the manifest.
    /// </param>
    /// <param name="dryRun">
    /// When <see langword="true"/>, no packages are uninstalled — output shows the
    /// commands that would be run.
    /// </param>
    /// <param name="useColor">Whether to emit ANSI colour codes in output lines.</param>
    /// <param name="output">Callback invoked once per tool with a formatted result line.</param>
    /// <returns>
    /// <c>0</c> when all uninstalls succeed (or dry-run); <c>1</c> when at least one fails.
    /// </returns>
    public async Task<int> UninstallAsync(string[]? toolNames, bool dryRun, bool useColor, Action<string> output)
    {
        string[] targets = ResolveTargets(toolNames);
        string[] chain = PlatformDetector.GetDefaultChain(_platform);
        int failures = 0;

        // Bulk-snapshot every available adapter UP FRONT — one subprocess per PM rather
        // than per-tool. Pre-bulk this method called IsInstalled per (tool, PM) pair to
        // discover ownership before uninstalling, which on real winget took ~7-19 seconds
        // per call and pushed `winix uninstall --dry-run` past the 60 s smoke timeout
        // after only ~9 tools. The snapshot's value (version string) is discarded —
        // ownership is just a key-membership check — but reusing the same shape as
        // ListAsync keeps a single bulk method on the adapter interface.
        var snapshots = new Dictionary<string, IReadOnlyDictionary<string, string?>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (string pmName in chain)
        {
            if (!_adapters.TryGetValue(pmName, out IPackageManagerAdapter? adapter))
            {
                continue;
            }

            if (!adapter.IsAvailable())
            {
                continue;
            }

            if (!snapshots.ContainsKey(pmName))
            {
                snapshots[pmName] = await adapter.GetInstalled().ConfigureAwait(false);
            }
        }

        foreach (string toolName in targets)
        {
            if (!_manifest.Tools.TryGetValue(toolName, out ToolEntry? entry))
            {
                // No adapter to attribute — this is a user-input error (typo or wrong
                // tool name), not a per-PM failure. Emit without "(via X)".
                output(Formatting.FormatToolError(toolName, "not in manifest", useColor));
                failures++;
                continue;
            }

            // Walk the platform chain to find which PM owns this tool, using the
            // pre-built snapshot (O(1) hash lookup, no subprocess).
            IPackageManagerAdapter? owningAdapter = null;
            string? packageId = null;

            foreach (string pmName in chain)
            {
                if (!snapshots.TryGetValue(pmName, out IReadOnlyDictionary<string, string?>? snapshot))
                {
                    continue;
                }

                string? pkgId = entry.GetPackageId(pmName);
                if (pkgId is null)
                {
                    continue;
                }

                if (snapshot.ContainsKey(pkgId))
                {
                    // _adapters is guaranteed to have pmName here because the snapshot
                    // was built by walking the same chain through _adapters.
                    owningAdapter = _adapters[pmName];
                    packageId = pkgId;
                    break;
                }
            }

            if (owningAdapter is null || packageId is null)
            {
                // Idempotent uninstall: a tool that was never installed is treated as a
                // successful no-op rather than a failure. CI workflows that call
                // 'winix uninstall' for cleanup should succeed even when the tool was
                // never installed, matching apt's "nothing to do" behaviour. The ○
                // glyph distinguishes "no action needed" from the ✗ used for real errors.
                output(Formatting.FormatNoOpResult(toolName, "not installed", useColor));
                continue;
            }

            if (dryRun)
            {
                output(Formatting.FormatDryRun(owningAdapter.Name, new[] { "uninstall", packageId }));
                continue;
            }

            ProcessResult result = await owningAdapter.Uninstall(packageId).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                output(Formatting.FormatToolResult(toolName, owningAdapter.Name, success: true, error: null, useColor));
            }
            else
            {
                string error = string.IsNullOrEmpty(result.Stderr) ? result.Stdout : result.Stderr;
                output(Formatting.FormatToolResult(toolName, owningAdapter.Name, success: false, error: error, useColor));
                failures++;
            }
        }

        return failures > 0 ? WinixExitCode.ToolFailure : WinixExitCode.Success;
    }

    /// <summary>
    /// Lists the installation status of all tools in the suite.
    /// For each tool, the platform chain is walked to find the first package manager
    /// that reports it as installed.
    /// </summary>
    /// <returns>
    /// A list of <see cref="ToolStatus"/> records, one per tool in manifest order.
    /// </returns>
    public async Task<List<ToolStatus>> ListAsync()
    {
        string[] chain = PlatformDetector.GetDefaultChain(_platform);
        var statuses = new List<ToolStatus>();

        // Bulk-snapshot every available adapter UP FRONT — one subprocess per PM rather
        // than per-tool. The pre-bulk path called IsInstalled then GetInstalledVersion
        // on every tool against every PM in the chain (44+ filtered subprocess calls
        // for a 22-tool manifest with 2 available PMs); each `winget list --id X` was
        // measured at ~7-19 seconds against a real machine, so `winix list` could take
        // 5-7 minutes before timing out at 60 s. The bulk path runs `winget list` once
        // (no filter) and then performs O(1) hash lookups per tool.
        var snapshots = new Dictionary<string, IReadOnlyDictionary<string, string?>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (string pmName in chain)
        {
            if (!_adapters.TryGetValue(pmName, out IPackageManagerAdapter? adapter))
            {
                continue;
            }

            if (!adapter.IsAvailable())
            {
                continue;
            }

            // Cache snapshots so a chain that mentions the same PM twice (shouldn't
            // happen in current PlatformDetector output, but cheap defence-in-depth)
            // doesn't re-spawn the subprocess.
            if (!snapshots.ContainsKey(pmName))
            {
                snapshots[pmName] = await adapter.GetInstalled().ConfigureAwait(false);
            }
        }

        foreach (var kvp in _manifest.Tools)
        {
            string toolName = kvp.Key;
            ToolEntry entry = kvp.Value;

            bool found = false;

            foreach (string pmName in chain)
            {
                if (!snapshots.TryGetValue(pmName, out IReadOnlyDictionary<string, string?>? snapshot))
                {
                    continue;
                }

                string? packageId = entry.GetPackageId(pmName);
                if (packageId is null)
                {
                    continue;
                }

                if (!snapshot.TryGetValue(packageId, out string? version))
                {
                    continue;
                }

                statuses.Add(new ToolStatus(toolName, isInstalled: true, version: version, packageManager: pmName));
                found = true;
                break;
            }

            if (!found)
            {
                statuses.Add(new ToolStatus(toolName, isInstalled: false, version: null, packageManager: null));
            }
        }

        return statuses;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Common implementation shared by <see cref="InstallAsync"/> and <see cref="UpdateAsync"/>.
    /// Uses the first adapter in <see cref="_adapters"/> as the target package manager.
    /// </summary>
    /// <param name="toolNames">Tool filter; <see langword="null"/> means all tools.</param>
    /// <param name="action">The per-tool operation: Install or Update.</param>
    /// <param name="dryRun">When <see langword="true"/>, skip real invocations.</param>
    /// <param name="useColor">Whether ANSI colour codes are emitted.</param>
    /// <param name="output">Callback for formatted result lines.</param>
    private async Task<int> ExecuteAsync(
        string[]? toolNames,
        Func<IPackageManagerAdapter, string, Task<ProcessResult>> action,
        bool dryRun,
        bool useColor,
        Action<string> output)
    {
        // Single-adapter path: pick the first (and typically only) available adapter.
        IPackageManagerAdapter? adapter = null;
        foreach (IPackageManagerAdapter candidate in _adapters.Values)
        {
            if (candidate.IsAvailable())
            {
                adapter = candidate;
                break;
            }
        }

        if (adapter is null)
        {
            // No adapter is available — emit an error for every target and return failure.
            string[] targets = ResolveTargets(toolNames);
            foreach (string toolName in targets)
            {
                output(Formatting.FormatToolResult(toolName, "?", success: false, error: "no package manager available", useColor));
            }

            return WinixExitCode.NoPackageManager;
        }

        int failures = 0;
        foreach (string toolName in ResolveTargets(toolNames))
        {
            if (!_manifest.Tools.TryGetValue(toolName, out ToolEntry? entry))
            {
                // Tool name isn't in the manifest — user-input error rather than
                // per-PM failure. Emit without the misleading "(via X)" annotation.
                output(Formatting.FormatToolError(toolName, "not in manifest", useColor));
                failures++;
                continue;
            }

            string? packageId = entry.GetPackageId(adapter.Name);
            if (packageId is null)
            {
                output(Formatting.FormatToolResult(toolName, adapter.Name, success: false,
                    error: $"no package ID for {adapter.Name}", useColor));
                failures++;
                continue;
            }

            if (dryRun)
            {
                // Emit the conceptual command name and package ID rather than
                // the real CLI invocation — the adapter owns those details.
                output(Formatting.FormatDryRun(adapter.Name, new[] { "install", packageId }));
                continue;
            }

            ProcessResult result = await action(adapter, packageId).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                output(Formatting.FormatToolResult(toolName, adapter.Name, success: true, error: null, useColor));
            }
            else
            {
                string error = string.IsNullOrEmpty(result.Stderr) ? result.Stdout : result.Stderr;
                output(Formatting.FormatToolResult(toolName, adapter.Name, success: false, error: error, useColor));
                failures++;
            }
        }

        return failures > 0 ? WinixExitCode.ToolFailure : WinixExitCode.Success;
    }

    /// <summary>
    /// Returns the tool names to operate on: the supplied filter if non-null and
    /// non-empty, otherwise all tools declared in the manifest.
    /// </summary>
    /// <param name="toolNames">Caller-supplied filter, or <see langword="null"/> for all.</param>
    private string[] ResolveTargets(string[]? toolNames)
    {
        if (toolNames is null || toolNames.Length == 0)
        {
            return _manifest.GetToolNames();
        }

        return toolNames;
    }
}
