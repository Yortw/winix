#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Winix.Winix;
using Yort.ShellKit;

namespace Winix;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        string version = GetVersion();

        var parser = new CommandLineParser("winix", version)
            .Description("Install, update, and manage the Winix CLI tool suite.")
            .StandardFlags()
            .Option("--via", null, "PM", "Package manager to use: winget, scoop, brew, dotnet")
            .Flag("--dry-run", "Show commands that would be run without executing them")
            .Positional("command [tool...]")
            .Platform("cross-platform",
                new[] { "winget", "scoop", "brew" },
                "No cross-platform suite installer exists",
                "Manages all Winix tools with a single command across Windows and macOS")
            .StdinDescription("Not used")
            .StdoutDescription("JSON metadata when --json is set, otherwise unused")
            .StderrDescription("Progress and result lines, one per tool.")
            .Example("winix install", "Install all Winix tools using the default package manager")
            .Example("winix install timeit squeeze", "Install specific tools")
            .Example("winix install --via scoop", "Install all tools via Scoop")
            .Example("winix update", "Update all installed Winix tools")
            .Example("winix uninstall", "Remove all Winix tools")
            .Example("winix list", "List all tools and their install status")
            .Example("winix status", "Show a summary of installed tools")
            .Example("winix install --dry-run", "Preview what would be installed")
            .ExitCodes(
                (WinixExitCode.Success, "Success"),
                (WinixExitCode.ToolFailure, "One or more tools failed"),
                (WinixExitCode.UsageError, "Usage error (bad command or argument)"),
                (WinixExitCode.NoPackageManager, "No supported package manager found"),
                (WinixExitCode.InternalError, "Internal error (manifest fetch/parse failure)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // Require a command positional.
        if (result.Positionals.Length == 0)
        {
            return result.WriteError("missing command (expected install, update, uninstall, list, or status)", Console.Error);
        }

        string command = result.Positionals[0];
        // Use Skip(1) rather than range syntax per project style.
        string[] toolNames = result.Positionals.Skip(1).ToArray();

        // Validate the command.
        if (command != "install" && command != "update" && command != "uninstall"
            && command != "list" && command != "status")
        {
            return result.WriteError(
                $"unknown command '{command}' (expected install, update, uninstall, list, or status)",
                Console.Error);
        }

        // Validate --via value if provided.
        string? viaOverride = result.Has("--via") ? result.GetString("--via") : null;
        if (viaOverride != null
            && viaOverride != "winget"
            && viaOverride != "scoop"
            && viaOverride != "brew"
            && viaOverride != "dotnet")
        {
            return result.WriteError(
                $"invalid --via value '{viaOverride}' (expected winget, scoop, brew, or dotnet)",
                Console.Error);
        }

        bool dryRun = result.Has("--dry-run");
        bool useColor = result.ResolveColor(checkStdErr: true);

        // Build the full adapter map used for list/status/uninstall (multi-adapter).
        var allAdapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", new WingetAdapter() },
            { "scoop",  new ScoopAdapter()  },
            { "brew",   new BrewAdapter()   },
            { "dotnet", new DotnetToolAdapter() },
        };

        PlatformId platform = PlatformDetector.GetCurrentPlatform();

        // Fetch the manifest — needed for all commands.
        ToolManifest manifest;
        try
        {
            manifest = await ManifestLoader.LoadAsync().ConfigureAwait(false);
        }
        catch (ManifestParseException ex)
        {
            Console.Error.WriteLine($"winix: {ex.Message}");
            return WinixExitCode.InternalError;
        }

        bool jsonOutput = result.Has("--json");

        // Dispatch to the appropriate command.
        if (command == "list" || command == "status")
        {
            return await RunListOrStatusAsync(
                command, manifest, allAdapters, platform, useColor, jsonOutput, version).ConfigureAwait(false);
        }

        if (command == "uninstall")
        {
            var manager = new SuiteManager(manifest, allAdapters, platform);
            string[]? toolFilter = toolNames.Length > 0 ? toolNames : null;
            return await manager.UninstallAsync(
                toolFilter, dryRun, useColor,
                line => Console.Error.WriteLine(line)).ConfigureAwait(false);
        }

        // install or update: resolve a single adapter.
        IPackageManagerAdapter? adapter = PlatformDetector.ResolveAdapter(viaOverride, allAdapters, platform);
        if (adapter is null)
        {
            if (viaOverride != null)
            {
                Console.Error.WriteLine(
                    $"winix: package manager '{viaOverride}' is not available on this machine");
            }
            else
            {
                Console.Error.WriteLine("winix: no supported package manager found on this machine");
            }

            return WinixExitCode.NoPackageManager;
        }

        // On install, auto-setup the scoop bucket or brew tap so packages are
        // discoverable even when the user has not previously added them. Both
        // helpers return true only when they actually registered something, so we
        // emit the one-time stderr notice on first install and stay silent on
        // subsequent runs — opaque side effects are bad UX.
        if (command == "install" && !dryRun)
        {
            if (adapter is ScoopAdapter scoopAdapter)
            {
                try
                {
                    bool added = await scoopAdapter.EnsureBucket().ConfigureAwait(false);
                    if (added)
                    {
                        Console.Error.WriteLine("winix: registered scoop bucket 'winix' (https://github.com/Yortw/winix)");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"winix: warning: could not register scoop bucket: {ex.Message}");
                }
            }
            else if (adapter is BrewAdapter brewAdapter)
            {
                try
                {
                    bool added = await brewAdapter.EnsureTap().ConfigureAwait(false);
                    if (added)
                    {
                        Console.Error.WriteLine("winix: registered brew tap 'yortw/winix'");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"winix: warning: could not add brew tap: {ex.Message}");
                }
            }
        }

        var singleManager = new SuiteManager(manifest, adapter);
        string[]? filter = toolNames.Length > 0 ? toolNames : null;

        if (command == "install")
        {
            return await singleManager.InstallAsync(
                filter, dryRun, useColor,
                line => Console.Error.WriteLine(line)).ConfigureAwait(false);
        }

        // update
        return await singleManager.UpdateAsync(
            filter, dryRun, useColor,
            line => Console.Error.WriteLine(line)).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles the <c>list</c> and <c>status</c> commands.
    /// <c>list</c> prints a formatted table; <c>status</c> prints a one-line summary.
    /// Both use the multi-adapter SuiteManager so all installed PMs are probed.
    /// </summary>
    private static async Task<int> RunListOrStatusAsync(
        string command,
        ToolManifest manifest,
        IDictionary<string, IPackageManagerAdapter> adapters,
        PlatformId platform,
        bool useColor,
        bool jsonOutput,
        string winixVersion)
    {
        var manager = new SuiteManager(manifest, adapters, platform);
        List<ToolStatus> statuses = await manager.ListAsync().ConfigureAwait(false);

        if (jsonOutput)
        {
            // JSON goes to stdout per the suite-wide convention so 'winix list --json | jq'
            // works in pipelines. Errors and progress lines remain on stderr (none here for
            // the read-only list/status paths once we've successfully resolved statuses).
            string json = command == "status"
                ? Formatting.FormatStatusJson(statuses, manifest.Tools.Count, winixVersion, platform)
                : Formatting.FormatListJson(statuses, winixVersion, platform);
            Console.Out.WriteLine(json);
            return WinixExitCode.Success;
        }

        if (command == "status")
        {
            Console.Error.WriteLine(Formatting.FormatStatusSummary(statuses, manifest.Tools.Count));
            return WinixExitCode.Success;
        }

        // list: show table and hint when nothing is installed.
        bool anyInstalled = false;
        foreach (ToolStatus ts in statuses)
        {
            if (ts.IsInstalled)
            {
                anyInstalled = true;
                break;
            }
        }

        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in manifest.Tools)
        {
            descriptions[kvp.Key] = kvp.Value.Description;
        }

        Console.Error.Write(Formatting.FormatListTable(statuses, descriptions, useColor));

        if (!anyInstalled)
        {
            Console.Error.WriteLine(Formatting.FormatNoToolsHint());
        }

        return WinixExitCode.Success;
    }

    /// <summary>
    /// Returns the informational version from the Winix.Winix library assembly.
    /// </summary>
    private static string GetVersion()
    {
        return typeof(SuiteManager).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
