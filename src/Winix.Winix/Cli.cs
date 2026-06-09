#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Yort.ShellKit;

namespace Winix.Winix;

/// <summary>
/// Library-seam entry point for the <c>winix</c> suite-installer tool. The
/// console-app <c>Main</c> at <c>src/winix/Program.cs</c> is a thin shim that
/// hands stdio + adapter map + platform observations into <see cref="RunAsync"/>;
/// tests bypass the shim entirely and pin orchestration contracts (exit codes,
/// JSON-to-stdout, error-routing, --via whitelist) through this method directly.
/// </summary>
/// <remarks>
/// <para>
/// Round-1 fresh-eyes 2026-05-09 code-reviewer I1 + pr-test-analyzer W1 closure:
/// pre-fix the orchestration layer (~200 LOC of <c>Main</c>) was unreachable from
/// tests. The seam mirrors the precedent set by 9 other tier-2 tools (clip,
/// whoholds, treex, files, less, man, plus the original digest/url/qr/timeit/
/// notify/ids/when set) — public Run method, console app reduced to a one-liner
/// forwarder, all behaviour testable with <see cref="TextWriter"/> sinks and
/// explicit adapter/platform/manifest-loader injection.
/// </para>
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the <c>winix</c> suite-installer against the supplied arguments and writers.
    /// </summary>
    /// <param name="args">CLI arguments as passed to <c>Main</c>.</param>
    /// <param name="stdout">Writer for JSON metadata (when --json is set).</param>
    /// <param name="stderr">Writer for progress, errors, warnings, and the human-readable list/status output.</param>
    /// <param name="adapters">
    /// Optional adapter map keyed by package-manager name (winget/scoop/brew/dotnet).
    /// Tests pass a stub map; production code passes <see langword="null"/> to use the
    /// standard four real adapters.
    /// </param>
    /// <param name="platform">
    /// Optional platform override. Tests pass a synthesised value to pin per-platform
    /// behaviour; production code passes <see langword="null"/> to use
    /// <see cref="PlatformDetector.GetCurrentPlatform"/>.
    /// </param>
    /// <param name="manifestLoader">
    /// Optional manifest loader. Receives the warnings sink (typically <paramref name="stderr"/>)
    /// and returns the parsed manifest. Tests pass a fake; production code passes
    /// <see langword="null"/> to use <see cref="ManifestLoader.LoadAsync"/>.
    /// </param>
    /// <returns>The winix tool's exit code (see <see cref="WinixExitCode"/>).</returns>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        IDictionary<string, IPackageManagerAdapter>? adapters = null,
        PlatformId? platform = null,
        Func<TextWriter?, Task<ToolManifest>>? manifestLoader = null)
    {
        if (stdout == null) throw new ArgumentNullException(nameof(stdout));
        if (stderr == null) throw new ArgumentNullException(nameof(stderr));

        string version = GetVersion();

        var parser = new CommandLineParser("winix", version)
            .Description("Install, update, and manage the Winix CLI tool suite.")
            .Maturity(ToolMaturity.Core)
            .StandardFlags()
            .Option("--via", null, "PM", "Package manager to use: winget, scoop, brew, dotnet")
            .Flag("--dry-run", "Show commands that would be run without executing them")
            .Flag("--project", "Write into committed project files (AGENTS.md/CLAUDE.md) instead of user/global agent config (agents only)")
            .Flag("--claude", "Force the Claude home/file even when absent: user scope → ~/.claude/CLAUDE.md; --project → include CLAUDE.md (agents only)")
            .Flag("--codex", "Force the Codex user home (~/.codex/AGENTS.md) even when absent (agents user scope only)")
            .Option("--path", null, "DIR", "Project directory for --project (agents only; default: current directory)")
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
            .Example("winix agents init", "Write the Winix pointer into your user agent config (~/.claude/CLAUDE.md, ~/.codex/AGENTS.md)")
            .Example("winix agents init --project", "Write a conditional pointer into this repo's AGENTS.md/CLAUDE.md (for teams standardized on Winix)")
            .Example("winix agents status", "Report whether your user agent config carries a current pointer (exit 1 if not)")
            .Example("winix agents remove", "Remove the Winix pointer block from your user agent config")
            .ExitCodes(
                (WinixExitCode.Success, "Success"),
                (WinixExitCode.ToolFailure, "One or more tools failed; or (agents) the pointer block is absent or stale, or (user scope) no agent home exists"),
                (WinixExitCode.UsageError, "Usage error (bad command or argument)"),
                (WinixExitCode.NoPackageManager, "No supported package manager found"),
                (WinixExitCode.InternalError, "Internal error (manifest fetch/parse failure, or agents file I/O failure)"));

        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        if (result.Positionals.Length == 0)
        {
            return result.WriteError("missing command (expected install, update, uninstall, list, status, or agents)", stderr);
        }

        string command = result.Positionals[0];
        string[] toolNames = result.Positionals.Skip(1).ToArray();

        if (command != "install" && command != "update" && command != "uninstall"
            && command != "list" && command != "status" && command != "agents")
        {
            return result.WriteError(
                $"unknown command '{command}' (expected install, update, uninstall, list, status, or agents)",
                stderr);
        }

        if (command == "agents")
        {
            // agents never needs the tool manifest (the block delegates "what's installed" to
            // the runtime `winix list` pointer), so dispatch before --via validation and the
            // manifest fetch — neither applies, and a manifest fetch failure must not block it.
            bool project = result.Has("--project");

            // --path is only meaningful for a project directory; reject it in user scope so a
            // misplaced --path can't silently no-op against the wrong target.
            if (result.Has("--path") && !project)
            {
                return result.WriteError(
                    "--path is only valid with --project (user scope writes to your agent home)", stderr);
            }
            // --codex names a user home; it has no meaning when writing committed project files.
            if (result.Has("--codex") && project)
            {
                return result.WriteError(
                    "--codex is a user-scope flag and cannot be combined with --project", stderr);
            }

            string? verb = result.Positionals.Length > 1 ? result.Positionals[1] : null;
            string baseDir = result.Has("--path")
                ? result.GetString("--path")!
                : Directory.GetCurrentDirectory();

            var agentsOptions = new AgentsManager.AgentsOptions(
                Verb: verb,
                Scope: project ? AgentsManager.AgentsScope.Project : AgentsManager.AgentsScope.User,
                BaseDir: baseDir,
                ForceClaude: result.Has("--claude"),
                ForceCodex: result.Has("--codex"),
                DryRun: result.Has("--dry-run"),
                Json: result.Has("--json"),
                Version: version);

            return AgentsManager.Run(agentsOptions, stdout, stderr);
        }

        string? viaOverride = result.Has("--via") ? result.GetString("--via") : null;
        if (viaOverride != null
            && viaOverride != "winget"
            && viaOverride != "scoop"
            && viaOverride != "brew"
            && viaOverride != "dotnet")
        {
            return result.WriteError(
                $"invalid --via value '{viaOverride}' (expected winget, scoop, brew, or dotnet)",
                stderr);
        }

        bool dryRun = result.Has("--dry-run");
        bool useColor = result.ResolveColor(checkStdErr: true);

        IDictionary<string, IPackageManagerAdapter> resolvedAdapters = adapters ?? new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", new WingetAdapter() },
            { "scoop",  new ScoopAdapter()  },
            { "brew",   new BrewAdapter()   },
            { "dotnet", new DotnetToolAdapter() },
        };

        PlatformId resolvedPlatform = platform ?? PlatformDetector.GetCurrentPlatform();

        ToolManifest manifest;
        try
        {
            Func<TextWriter?, Task<ToolManifest>> loader = manifestLoader
                ?? (warnings => ManifestLoader.LoadAsync(warnings: warnings));
            manifest = await loader(stderr).ConfigureAwait(false);
        }
        catch (ManifestParseException ex)
        {
            stderr.WriteLine($"winix: {ex.Message}");
            return WinixExitCode.InternalError;
        }

        bool jsonOutput = result.Has("--json");

        if (command == "list" || command == "status")
        {
            return await RunListOrStatusAsync(
                command, manifest, resolvedAdapters, resolvedPlatform,
                useColor, jsonOutput, version, stdout, stderr).ConfigureAwait(false);
        }

        if (command == "uninstall")
        {
            var manager = new SuiteManager(manifest, resolvedAdapters, resolvedPlatform);
            string[]? toolFilter = toolNames.Length > 0 ? toolNames : null;
            // Per-PM "querying X…" progress fires synchronously inside UninstallAsync
            // immediately before each adapter's bulk subprocess. Without this the tool
            // sits silent for ~8s while winget walks its index. Suppressed under
            // --json so the human-progress lines don't contaminate piped output.
            Action<string>? onPmQuery = jsonOutput
                ? null
                : pmName => stderr.WriteLine($"winix: querying {pmName}…");
            return await manager.UninstallAsync(
                toolFilter, dryRun, useColor,
                line => stderr.WriteLine(line),
                onPmQuery).ConfigureAwait(false);
        }

        IPackageManagerAdapter? adapter = PlatformDetector.ResolveAdapter(viaOverride, resolvedAdapters, resolvedPlatform);
        if (adapter is null)
        {
            if (viaOverride != null)
            {
                stderr.WriteLine(
                    $"winix: package manager '{viaOverride}' is not available on this machine");
            }
            else
            {
                stderr.WriteLine("winix: no supported package manager found on this machine");
            }

            return WinixExitCode.NoPackageManager;
        }

        if (command == "install" && !dryRun)
        {
            if (adapter is ScoopAdapter scoopAdapter)
            {
                try
                {
                    bool added = await scoopAdapter.EnsureBucket().ConfigureAwait(false);
                    if (added)
                    {
                        stderr.WriteLine("winix: registered scoop bucket 'winix' (https://github.com/Yortw/winix)");
                    }
                }
                catch (Exception ex)
                {
                    // Don't pipe ex.Message under InvariantGlobalization — framework
                    // exceptions return SR resource keys, not English. Surface the
                    // type discriminator so logs are still useful.
                    stderr.WriteLine($"winix: warning: could not register scoop bucket ({ex.GetType().Name})");
                }
            }
            else if (adapter is BrewAdapter brewAdapter)
            {
                try
                {
                    bool added = await brewAdapter.EnsureTap().ConfigureAwait(false);
                    if (added)
                    {
                        stderr.WriteLine("winix: registered brew tap 'yortw/winix'");
                    }
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"winix: warning: could not add brew tap ({ex.GetType().Name})");
                }
            }
        }

        var singleManager = new SuiteManager(manifest, adapter);
        string[]? filter = toolNames.Length > 0 ? toolNames : null;

        if (command == "install")
        {
            return await singleManager.InstallAsync(
                filter, dryRun, useColor,
                line => stderr.WriteLine(line)).ConfigureAwait(false);
        }

        // update
        return await singleManager.UpdateAsync(
            filter, dryRun, useColor,
            line => stderr.WriteLine(line)).ConfigureAwait(false);
    }

    private static async Task<int> RunListOrStatusAsync(
        string command,
        ToolManifest manifest,
        IDictionary<string, IPackageManagerAdapter> adapters,
        PlatformId platform,
        bool useColor,
        bool jsonOutput,
        string winixVersion,
        TextWriter stdout,
        TextWriter stderr)
    {
        var manager = new SuiteManager(manifest, adapters, platform);
        // Per-PM "querying X…" progress; suppressed under --json (see UninstallAsync
        // call site for rationale).
        Action<string>? onPmQuery = jsonOutput
            ? null
            : pmName => stderr.WriteLine($"winix: querying {pmName}…");
        List<ToolStatus> statuses = await manager.ListAsync(onPmQuery).ConfigureAwait(false);

        if (jsonOutput)
        {
            // F3: JSON goes to stdout per the suite-wide convention so
            // 'winix list --json | jq' works in pipelines. Errors and progress
            // lines remain on stderr (none here for the read-only list/status
            // paths once we've successfully resolved statuses).
            string json = command == "status"
                ? Formatting.FormatStatusJson(statuses, manifest.Tools.Count, winixVersion, platform)
                : Formatting.FormatListJson(statuses, winixVersion, platform);
            stdout.WriteLine(json);
            return WinixExitCode.Success;
        }

        if (command == "status")
        {
            stderr.WriteLine(Formatting.FormatStatusSummary(statuses, manifest.Tools.Count));
            return WinixExitCode.Success;
        }

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

        stderr.Write(Formatting.FormatListTable(statuses, descriptions, useColor));

        if (!anyInstalled)
        {
            stderr.WriteLine(Formatting.FormatNoToolsHint());
        }

        return WinixExitCode.Success;
    }

    private static string GetVersion()
    {
        // SDK appends a SourceLink "+gitsha" suffix to AssemblyInformationalVersion
        // by default (e.g. "0.3.0+abc123…"); strip it so users see "0.3.0" — matches
        // the convention adopted across clip / digest / ids / schedule / etc.
        string raw = typeof(SuiteManager).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
