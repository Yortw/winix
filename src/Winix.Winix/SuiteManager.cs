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

        foreach (string toolName in targets)
        {
            if (!_manifest.Tools.TryGetValue(toolName, out ToolEntry? entry))
            {
                output(Formatting.FormatToolResult(toolName, "?", success: false, error: "not in manifest", useColor));
                failures++;
                continue;
            }

            // Walk the platform chain to find which PM owns this tool.
            IPackageManagerAdapter? owningAdapter = null;
            string? packageId = null;

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

                string? pkgId = entry.GetPackageId(pmName);
                if (pkgId is null)
                {
                    continue;
                }

                bool installed = await adapter.IsInstalled(pkgId).ConfigureAwait(false);
                if (installed)
                {
                    owningAdapter = adapter;
                    packageId = pkgId;
                    break;
                }
            }

            if (owningAdapter is null || packageId is null)
            {
                output(Formatting.FormatToolResult(toolName, "?", success: false, error: "not installed", useColor));
                failures++;
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

        return failures > 0 ? 1 : 0;
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

        foreach (var kvp in _manifest.Tools)
        {
            string toolName = kvp.Key;
            ToolEntry entry = kvp.Value;

            bool found = false;

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

                string? packageId = entry.GetPackageId(pmName);
                if (packageId is null)
                {
                    continue;
                }

                bool installed = await adapter.IsInstalled(packageId).ConfigureAwait(false);
                if (!installed)
                {
                    continue;
                }

                string? version = await adapter.GetInstalledVersion(packageId).ConfigureAwait(false);
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

            return 1;
        }

        int failures = 0;
        foreach (string toolName in ResolveTargets(toolNames))
        {
            if (!_manifest.Tools.TryGetValue(toolName, out ToolEntry? entry))
            {
                output(Formatting.FormatToolResult(toolName, adapter.Name, success: false, error: "not in manifest", useColor));
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

        return failures > 0 ? 1 : 0;
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
