#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Winix.Winix;

/// <summary>
/// File-I/O seam for <see cref="AgentsManager"/>. Production uses a thin wrapper over
/// <see cref="System.IO.File"/>; tests inject in-memory or fault-injecting fakes so the
/// orchestration paths (target resolution, dry-run, drift, write-failure) are exercisable
/// without touching disk.
/// </summary>
public interface IAgentsFileSystem
{
    /// <summary>Returns whether a file exists at <paramref name="path"/>.</summary>
    bool FileExists(string path);

    /// <summary>Returns whether a directory exists at <paramref name="path"/>.</summary>
    bool DirectoryExists(string path);

    /// <summary>Reads the entire file at <paramref name="path"/> as text.</summary>
    string ReadAllText(string path);

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/>, overwriting.</summary>
    void WriteAllText(string path, string content);

    /// <summary>
    /// Returns the current user's home directory (the parent of agent-config dirs like
    /// <c>.claude</c>). Production honours the <c>WINIX_AGENTS_HOME</c> override (an absolute path,
    /// for test/smoke isolation only — it may point at a non-existent dir) before falling back to
    /// the OS user-profile path, so smoke tests can redirect user-scope writes to a scratch dir and
    /// never clobber a real agent config.
    /// </summary>
    string ResolveHome();

    /// <summary>
    /// Creates <paramref name="path"/> and any missing parents (idempotent). Used to force-create
    /// an agent home (<c>--claude</c>/<c>--codex</c>) whose directory does not yet exist.
    /// </summary>
    void CreateDirectory(string path);
}

/// <summary>
/// Reads, writes, and reports on the marker-delimited Winix discoverability block that
/// <c>winix agents</c> manages inside a project's <c>AGENTS.md</c> / <c>CLAUDE.md</c>.
/// </summary>
/// <remarks>
/// The block delegates "what's installed" to the runtime <c>winix list</c> pointer and to a
/// version-pinned repo URL, so the rendered content depends only on the running binary's
/// version — never on the tool manifest. That keeps this type free of manifest coupling and
/// fully unit-testable through the <see cref="IAgentsFileSystem"/> seam.
/// </remarks>
public static class AgentsManager
{
    /// <summary>
    /// Maps a binary version to the Git ref used in the version-pinned <c>AGENTS.md</c> URL.
    /// Stable releases (no <c>-</c> in the version string) pin to the matching <c>vX.Y.Z</c>
    /// tag; pre-release / dev builds (which have no tag) fall back to <c>main</c>. This is the
    /// same stable/pre-release discriminator the winget manifest generation keys on.
    /// </summary>
    internal static string UrlRef(string version)
    {
        return version.Contains('-') ? "main" : $"v{version}";
    }

    /// <summary>The fixed prefix of the block's opening marker (an HTML comment, invisible in rendered Markdown).</summary>
    internal const string StartMarkerPrefix = "<!-- winix:start";

    /// <summary>The block's closing marker.</summary>
    internal const string EndMarker = "<!-- winix:end -->";

    /// <summary>
    /// Selects the claim strength of the rendered block: <see cref="UserScope"/> asserts the tools
    /// are present (written into a per-user agent home, true for that machine); <see cref="ProjectScope"/>
    /// uses conditional wording (written into committed files shared with machines that may lack Winix).
    /// </summary>
    public enum RenderMode
    {
        /// <summary>Asserts availability — for per-user agent homes run once per machine.</summary>
        UserScope,

        /// <summary>Conditional "if available" wording — for committed, shared project files.</summary>
        ProjectScope,
    }

    /// <summary>
    /// Renders the full managed block (opening marker through closing marker) for the given
    /// version, joined with LF. EOL normalisation to the target file's convention happens in
    /// <see cref="MergeBlock"/>; this method always emits LF so its output is deterministic.
    /// </summary>
    /// <param name="version">The binary version recorded in the marker and pinned in the URL.</param>
    /// <param name="mode">Whether to assert availability (user scope) or use conditional wording
    /// (project scope). Defaults to <see cref="RenderMode.UserScope"/>.</param>
    internal static string RenderBlock(string version, RenderMode mode = RenderMode.UserScope)
    {
        string urlRef = UrlRef(version);
        string header = mode == RenderMode.ProjectScope
            ? "## Winix CLI tools (if available in your environment)"
            : "## Winix CLI tools (available on this machine)";

        string[] lead = mode == RenderMode.ProjectScope
            ? new[]
            {
                "If Winix tools are installed in your environment, prefer one only when it's genuinely the",
                "better choice for the task — not by default. If you can't say why it beats the platform",
                "default (`find`, `time`, `tree`, `date`, PowerShell, …), use the default. If Winix is not",
                "installed, ignore this section.",
            }
            : new[]
            {
                "Prefer a Winix tool only when it's genuinely the better choice for the task — not by",
                "default. If you can't say why it beats the platform default (`find`, `time`, `tree`,",
                "`date`, PowerShell, …), use the default.",
            };

        // Only the "authoritative for …" phrasing differs: a user home speaks for "this machine",
        // a committed file speaks for "the machine running them" (whichever clone is in use).
        string authority = mode == RenderMode.ProjectScope
            ? "  (structured JSON — authoritative for the machine running them)."
            : "  (structured JSON — authoritative for this machine).";

        var lines = new List<string>
        {
            $"<!-- winix:start v={version} — managed by `winix agents init`; edits between markers are overwritten -->",
            header,
            "",
        };
        lines.AddRange(lead);
        lines.Add("");
        lines.Add("- **What's installed, flags, JSON shapes:** `winix list` and `<tool> --describe`");
        lines.Add(authority);
        lines.Add("- **Full guidance (when to prefer each tool, what it replaces):**");
        lines.Add($"  https://github.com/Yortw/winix/blob/{urlRef}/AGENTS.md");
        lines.Add("- Conventions: every tool has `--describe` + `--json`; exit 0 = success, non-zero on");
        lines.Add("  failure (usage/runtime codes vary by tool — see `--describe`); summaries go to stderr");
        lines.Add("  so stdout stays pipe-clean; `NO_COLOR` respected.");
        lines.Add("<!-- winix:end -->");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Extracts the version recorded in a block's opening marker (<c>v=...</c>), or
    /// <see langword="null"/> when the content has no syntactically-complete block. A start
    /// marker with no matching end marker is treated as "no valid block" so callers append a
    /// fresh block rather than splicing into a half-deleted one.
    /// </summary>
    internal static string? FindBlockVersion(string content)
    {
        int start = content.IndexOf(StartMarkerPrefix, StringComparison.Ordinal);
        if (start < 0) { return null; }

        int end = content.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0) { return null; }

        int vIdx = content.IndexOf("v=", start, StringComparison.Ordinal);
        if (vIdx < 0 || vIdx > end) { return null; }

        vIdx += 2;
        int e = vIdx;
        // F5/I1: terminate at whitespace, at the start of "-->", at '<' (the first char of any
        // marker — never valid in a version), and never scan past `end`. This keeps real
        // pre-release versions intact ("0.4.0-dev" — single '-') while a mangled marker like
        // `v=-->` or `v=0.4.0<!-- winix:end` yields a clean token or null, never garbage.
        while (e < end
            && !char.IsWhiteSpace(content[e])
            && content[e] != '<'
            && !(content[e] == '-' && e + 1 < content.Length && content[e + 1] == '-'))
        {
            e++;
        }
        return e > vIdx ? content.Substring(vIdx, e - vIdx) : null;
    }

    /// <summary>
    /// Returns <paramref name="content"/> with the managed block inserted or refreshed: an
    /// existing complete block is replaced in place (surrounding text untouched); otherwise a
    /// fresh block is appended after exactly one blank line. The result's line endings match
    /// the file's existing convention (CRLF if the file already uses it, LF otherwise), so the
    /// operation is byte-stable on re-run at the same version.
    /// </summary>
    internal static string MergeBlock(string content, string version, RenderMode mode = RenderMode.UserScope)
    {
        string eol = DetectEol(content);
        string block = NormalizeEol(RenderBlock(version, mode), eol);

        int start = content.IndexOf(StartMarkerPrefix, StringComparison.Ordinal);
        if (start >= 0)
        {
            int end = content.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (end >= 0)
            {
                int endFull = end + EndMarker.Length;
                string before = content.Substring(0, start);
                string after = content.Substring(endFull);
                return before + block + after;
            }
            // Start marker with no end: fall through and append a fresh, well-formed block.
        }

        if (content.Length == 0)
        {
            return block + eol;
        }

        string trimmed = content.TrimEnd('\r', '\n');
        return trimmed + eol + eol + block + eol;
    }

    /// <summary>Returns <c>"\r\n"</c> if the content already uses Windows line endings, else <c>"\n"</c>.</summary>
    private static string DetectEol(string content)
    {
        return content.Contains("\r\n") ? "\r\n" : "\n";
    }

    /// <summary>Rewrites every LF in <paramref name="text"/> (which uses LF only) to <paramref name="eol"/>.</summary>
    private static string NormalizeEol(string text, string eol)
    {
        return eol == "\n" ? text : text.Replace("\n", eol);
    }

    /// <summary>
    /// Returns <paramref name="content"/> with the managed block removed. The blank-line
    /// separator introduced on append is collapsed so the surrounding text rejoins cleanly.
    /// Content with no complete block (absent, or a start marker with no end) is returned
    /// unchanged — removing a half-block could delete user text.
    /// </summary>
    internal static string RemoveBlock(string content)
    {
        string eol = DetectEol(content);
        string current = content;

        // F6: strip EVERY complete block (a duplicate is an anomaly from a bad merge; leaving
        // one behind would let `status` keep reporting a block after `remove`). A start marker
        // with no matching end is left untouched — removing a half-block could eat user text.
        while (true)
        {
            int start = current.IndexOf(StartMarkerPrefix, StringComparison.Ordinal);
            if (start < 0) { return current; }

            int end = current.IndexOf(EndMarker, start, StringComparison.Ordinal);
            if (end < 0) { return current; }

            int endFull = end + EndMarker.Length;
            string before = current.Substring(0, start).TrimEnd('\r', '\n');
            string after = current.Substring(endFull).TrimStart('\r', '\n');

            if (before.Length == 0)
            {
                current = after;
            }
            else if (after.Length == 0)
            {
                current = before + eol;
            }
            else
            {
                current = before + eol + eol + after;
            }
        }
    }

    /// <summary>
    /// Target location for the managed block: <see cref="User"/> = per-user agent homes (the
    /// default); <see cref="Project"/> = committed files in a repository directory.
    /// </summary>
    public enum AgentsScope
    {
        /// <summary>Per-user agent homes (<c>~/.claude/CLAUDE.md</c>, <c>~/.codex/AGENTS.md</c>).</summary>
        User,

        /// <summary>Committed project files in <see cref="AgentsOptions.BaseDir"/>.</summary>
        Project,
    }

    /// <summary>
    /// Parsed inputs for an <c>agents</c> run. <paramref name="Verb"/> is the subcommand
    /// (<c>init</c>/<c>remove</c>/<c>status</c>), or <see langword="null"/> when none was given.
    /// </summary>
    /// <param name="Verb">The subcommand, or <see langword="null"/> when none was given.</param>
    /// <param name="Scope">User (per-user homes, default) or Project (committed repo files).</param>
    /// <param name="BaseDir">Project directory; only meaningful when <paramref name="Scope"/> is Project.</param>
    /// <param name="ForceClaude">Force the Claude home/file even when absent.</param>
    /// <param name="ForceCodex">Force the Codex user home even when absent (user scope only).</param>
    /// <param name="DryRun">Report what would change, write nothing.</param>
    /// <param name="Json">Emit a JSON envelope on stdout.</param>
    /// <param name="Version">The running binary's version (recorded in the marker / pinned URL).</param>
    public sealed record AgentsOptions(
        string? Verb,
        AgentsScope Scope,
        string BaseDir,
        bool ForceClaude,
        bool ForceCodex,
        bool DryRun,
        bool Json,
        string Version);

    /// <summary>
    /// A known agent-config home: the directory under the user profile and the Markdown file
    /// within it that the agent reads as global context.
    /// </summary>
    internal readonly record struct AgentHome(string Id, string Dir, string File);

    /// <summary>
    /// The user-scope homes <c>winix agents</c> manages, in write order. Adding a third agent is a
    /// single row — no other code changes.
    /// </summary>
    internal static readonly AgentHome[] KnownHomes =
    {
        new AgentHome("claude", ".claude", "CLAUDE.md"),
        new AgentHome("codex", ".codex", "AGENTS.md"),
    };

    /// <summary>
    /// Resolves the user-home files to act on: every known home whose directory exists, plus any
    /// home named by a force flag (<c>--claude</c>/<c>--codex</c>) even when its directory is absent.
    /// </summary>
    internal static List<string> ResolveUserTargets(AgentsOptions opts, IAgentsFileSystem fs)
    {
        string home = fs.ResolveHome();
        var targets = new List<string>();
        foreach (AgentHome h in KnownHomes)
        {
            string dir = Path.Combine(home, h.Dir);
            bool forced = (h.Id == "claude" && opts.ForceClaude) || (h.Id == "codex" && opts.ForceCodex);
            if (forced || fs.DirectoryExists(dir))
            {
                targets.Add(Path.Combine(dir, h.File));
            }
        }
        return targets;
    }

    /// <summary>
    /// Resolves the files <c>init</c> would write (and that <c>status</c> evaluates): always
    /// <c>AGENTS.md</c>, plus <c>CLAUDE.md</c> when it already exists or <c>--claude</c> forces it.
    /// </summary>
    internal static List<string> ResolveInitTargets(AgentsOptions opts, IAgentsFileSystem fs)
    {
        var targets = new List<string> { Path.Combine(opts.BaseDir, "AGENTS.md") };
        string claude = Path.Combine(opts.BaseDir, "CLAUDE.md");
        if (opts.ForceClaude || fs.FileExists(claude))
        {
            targets.Add(claude);
        }
        return targets;
    }

    /// <summary>
    /// Resolves the files an action operates on for the given scope, plus the render mode. Returns
    /// <see langword="false"/> with <paramref name="errorMessage"/>/<paramref name="errorCode"/>
    /// set when a scope precondition fails (project base dir missing, or user scope with no home
    /// and no force flag). The user-scope no-home case is surfaced as
    /// <see cref="WinixExitCode.UsageError"/>; callers that need a different exit code (status)
    /// re-map it themselves.
    /// </summary>
    internal static bool TryResolveTargets(
        AgentsOptions opts, IAgentsFileSystem fs,
        out List<string> targets, out RenderMode mode, out string? errorMessage, out int errorCode)
    {
        errorMessage = null;
        errorCode = WinixExitCode.Success;
        if (opts.Scope == AgentsScope.Project)
        {
            mode = RenderMode.ProjectScope;
            if (!fs.DirectoryExists(opts.BaseDir))
            {
                targets = new List<string>();
                errorMessage = $"winix: path '{opts.BaseDir}' is not a directory";
                errorCode = WinixExitCode.UsageError;
                return false;
            }
            targets = ResolveInitTargets(opts, fs);
            return true;
        }

        mode = RenderMode.UserScope;
        targets = ResolveUserTargets(opts, fs);
        if (targets.Count == 0)
        {
            errorMessage = "winix: no agent home found (use --claude or --codex to create one)";
            errorCode = WinixExitCode.UsageError;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Writes or refreshes the managed block in every applicable target for the options' scope.
    /// Returns <see cref="WinixExitCode.Success"/> on success, <see cref="WinixExitCode.UsageError"/>
    /// when a scope precondition fails (project base dir missing, or user scope with no home and no
    /// force flag), or <see cref="WinixExitCode.InternalError"/> on an I/O failure (reported as a
    /// clean one-line message — never a framework stack trace).
    /// </summary>
    internal static int RunInit(AgentsOptions opts, IAgentsFileSystem fs, TextWriter stdout, TextWriter stderr)
    {
        if (!TryResolveTargets(opts, fs, out List<string> targets, out RenderMode mode, out string? err, out int code))
        {
            stderr.WriteLine(err);
            return code;
        }

        var changed = new List<string>();
        string current = string.Empty;
        try
        {
            foreach (string target in targets)
            {
                current = target;
                string existing = fs.FileExists(target) ? fs.ReadAllText(target) : string.Empty;
                string merged = MergeBlock(existing, opts.Version, mode);
                if (!opts.DryRun)
                {
                    // Ensure the target dir exists through the SEAM before writing — this makes
                    // force-create (--claude/--codex into an absent home) deterministically unit-
                    // testable (assert fs.Created) instead of relying on the production writer's own
                    // dir-creation, which the in-memory fake can't observe. Idempotent for existing dirs.
                    string? dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir)) { fs.CreateDirectory(dir); }
                    fs.WriteAllText(target, merged);
                }
                changed.Add(target);
                if (!opts.Json)
                {
                    stderr.WriteLine(opts.DryRun ? $"winix: would write {target}" : $"winix: wrote {target}");
                }
            }

            if (opts.Json)
            {
                stdout.WriteLine(FormatActionJson("init", opts.DryRun, changed));
            }
            return WinixExitCode.Success;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // F3: name the file that failed so a partial multi-file write is diagnosable.
            // Don't pipe ex.Message under InvariantGlobalization — framework messages return
            // SR resource keys, not English. The type discriminator is the safe minimum.
            stderr.WriteLine($"winix: failed to write agents pointer to {current} ({ex.GetType().Name})");
            return WinixExitCode.InternalError;
        }
    }

    /// <summary>
    /// Reports the managed-block state of every applicable target (the same set
    /// <see cref="ResolveInitTargets"/> returns). Returns <see cref="WinixExitCode.Success"/>
    /// only when every applicable file carries a block at the current version; otherwise
    /// <see cref="WinixExitCode.ToolFailure"/> (the worst case across the set).
    /// </summary>
    internal static int RunStatus(AgentsOptions opts, IAgentsFileSystem fs, TextWriter stdout, TextWriter stderr)
    {
        List<string> targets;
        if (opts.Scope == AgentsScope.Project)
        {
            if (!fs.DirectoryExists(opts.BaseDir))
            {
                stderr.WriteLine($"winix: path '{opts.BaseDir}' is not a directory");
                return WinixExitCode.UsageError;
            }
            targets = ResolveInitTargets(opts, fs);
        }
        else
        {
            targets = ResolveUserTargets(opts, fs);
            if (targets.Count == 0)
            {
                // "Nothing set up" is a not-current state, not success — return ToolFailure (not
                // UsageError) so the `status || init` bootstrap idiom still triggers an init.
                if (opts.Json) { stdout.WriteLine(FormatStatusJson(new(), allCurrent: false)); }
                else { stderr.WriteLine("winix: no agent home found"); }
                return WinixExitCode.ToolFailure;
            }
        }

        var results = new List<(string Path, string State, string? Version)>();
        bool allCurrent = true;
        string current = string.Empty;

        try
        {
            foreach (string target in targets)
            {
                current = target;
                string? blockVer = fs.FileExists(target) ? FindBlockVersion(fs.ReadAllText(target)) : null;
                string state;
                if (blockVer == null)
                {
                    state = "absent";
                    allCurrent = false;
                }
                else if (string.Equals(blockVer, opts.Version, StringComparison.Ordinal))
                {
                    state = "current";
                }
                else
                {
                    state = "stale";
                    allCurrent = false;
                }
                results.Add((target, state, blockVer));
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // F8: an existing-but-unreadable target (permission denied) must not leak a stack
            // trace out of status — map to a clean InternalError like init/remove.
            // Name the file so a partial read across two targets is diagnosable.
            stderr.WriteLine($"winix: failed to read agents pointer from {current} ({ex.GetType().Name})");
            return WinixExitCode.InternalError;
        }

        if (opts.Json)
        {
            stdout.WriteLine(FormatStatusJson(results, allCurrent));
        }
        else
        {
            foreach ((string path, string state, string? version) in results)
            {
                string suffix = version != null ? $" (v{version})" : string.Empty;
                stderr.WriteLine($"winix: {path}: {state}{suffix}");
            }
        }

        return allCurrent ? WinixExitCode.Success : WinixExitCode.ToolFailure;
    }

    /// <summary>Builds the <c>--json</c> status envelope with <see cref="Utf8JsonWriter"/> (AOT-safe).</summary>
    private static string FormatStatusJson(
        List<(string Path, string State, string? Version)> results, bool allCurrent)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("current", allCurrent);
            writer.WriteStartArray("files");
            foreach ((string path, string state, string? version) in results)
            {
                writer.WriteStartObject();
                writer.WriteString("path", path);
                writer.WriteString("state", state);
                if (version != null) { writer.WriteString("version", version); }
                else { writer.WriteNull("version"); }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Strips the managed block from <c>AGENTS.md</c> and <c>CLAUDE.md</c> wherever each
    /// exists and actually contains a block. Files are never deleted (an emptied file is left
    /// empty). Returns <see cref="WinixExitCode.Success"/>, or
    /// <see cref="WinixExitCode.InternalError"/> on an I/O failure.
    /// </summary>
    internal static int RunRemove(AgentsOptions opts, IAgentsFileSystem fs, TextWriter stdout, TextWriter stderr)
    {
        string[] candidates;
        if (opts.Scope == AgentsScope.Project)
        {
            if (!fs.DirectoryExists(opts.BaseDir))
            {
                stderr.WriteLine($"winix: path '{opts.BaseDir}' is not a directory");
                return WinixExitCode.UsageError;
            }
            candidates = new[]
            {
                Path.Combine(opts.BaseDir, "AGENTS.md"),
                Path.Combine(opts.BaseDir, "CLAUDE.md"),
            };
        }
        else
        {
            // User scope strips from every known home file regardless of force flags — removing a
            // block where none exists is a safe, idempotent no-op (handled by the FindBlockVersion
            // guard in the loop), so there is no empty-home error path for remove.
            string home = fs.ResolveHome();
            var list = new List<string>();
            foreach (AgentHome h in KnownHomes) { list.Add(Path.Combine(home, h.Dir, h.File)); }
            candidates = list.ToArray();
        }

        var changed = new List<string>();
        string current = string.Empty;
        try
        {
            foreach (string target in candidates)
            {
                current = target;
                if (!fs.FileExists(target)) { continue; }
                string existing = fs.ReadAllText(target);
                if (FindBlockVersion(existing) == null) { continue; }

                if (!opts.DryRun)
                {
                    fs.WriteAllText(target, RemoveBlock(existing));
                }
                changed.Add(target);
                if (!opts.Json)
                {
                    stderr.WriteLine(opts.DryRun ? $"winix: would update {target}" : $"winix: removed block from {target}");
                }
            }

            if (opts.Json)
            {
                stdout.WriteLine(FormatActionJson("remove", opts.DryRun, changed));
            }
            return WinixExitCode.Success;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // F3: name the file that failed for a diagnosable partial update.
            stderr.WriteLine($"winix: failed to update agents pointer to {current} ({ex.GetType().Name})");
            return WinixExitCode.InternalError;
        }
    }

    /// <summary>Builds the <c>--json</c> envelope for <c>init</c>/<c>remove</c> (AOT-safe).</summary>
    private static string FormatActionJson(string action, bool dryRun, List<string> files)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("action", action);
            writer.WriteBoolean("dryRun", dryRun);
            writer.WriteStartArray("files");
            foreach (string f in files)
            {
                writer.WriteStringValue(f);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Entry point for the <c>winix agents</c> command. Dispatches on
    /// <see cref="AgentsOptions.Verb"/> to init / remove / status, returning
    /// <see cref="WinixExitCode.UsageError"/> for a missing or unknown verb.
    /// </summary>
    /// <param name="fs">File-I/O seam; production passes <see langword="null"/> to use the default.</param>
    public static int Run(AgentsOptions opts, TextWriter stdout, TextWriter stderr, IAgentsFileSystem? fs = null)
    {
        IAgentsFileSystem resolved = fs ?? CreateDefaultFileSystem();

        switch (opts.Verb)
        {
            case "init":
                return RunInit(opts, resolved, stdout, stderr);
            case "remove":
                return RunRemove(opts, resolved, stdout, stderr);
            case "status":
                return RunStatus(opts, resolved, stdout, stderr);
            case null:
            case "":
                stderr.WriteLine("winix: missing agents verb (expected init, remove, or status)");
                return WinixExitCode.UsageError;
            default:
                stderr.WriteLine($"winix: unknown agents verb '{opts.Verb}' (expected init, remove, or status)");
                return WinixExitCode.UsageError;
        }
    }

    /// <summary>Returns the production file system (a thin wrapper over <see cref="System.IO.File"/>).</summary>
    public static IAgentsFileSystem CreateDefaultFileSystem()
    {
        return new DefaultAgentsFileSystem();
    }

    private sealed class DefaultAgentsFileSystem : IAgentsFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);

        public string ResolveHome()
        {
            // WINIX_AGENTS_HOME lets smokes/integration tests redirect user-scope writes to a
            // scratch dir instead of the developer's real ~/.claude — never clobber a real agent
            // config in CI. Contract (test/smoke use only): must be an ABSOLUTE path. A relative
            // value is normalised against CWD via GetFullPath so behaviour is deterministic rather
            // than silently CWD-relative at each Path.Combine. It need NOT exist — a non-existent
            // override resolves to the empty-home path (no homes found) for a non-force init, which
            // is the intended "nothing set up" outcome.
            string? overrideHome = Environment.GetEnvironmentVariable("WINIX_AGENTS_HOME");
            return !string.IsNullOrEmpty(overrideHome)
                ? Path.GetFullPath(overrideHome)
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteAllText(string path, string content)
        {
            // Force-created homes (--claude/--codex into an absent dir) have no directory yet;
            // the atomic temp+move below needs it to exist. Idempotent for existing dirs.
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) { Directory.CreateDirectory(parent); }

            // F1: atomic per-file replace. Write a sibling temp on the same volume, then move
            // it over the target. A crash / Ctrl+C mid-write leaves the user's existing file
            // intact — a plain File.WriteAllText truncates in place and would lose their
            // content (init is explicitly designed to run against files that already exist).
            string? dir = Path.GetDirectoryName(path);
            string tempDir = string.IsNullOrEmpty(dir) ? "." : dir;
            string temp = Path.Combine(tempDir, "." + Path.GetFileName(path) + ".winix-" + Path.GetRandomFileName());
            File.WriteAllText(temp, content);
            try
            {
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(temp); } catch { /* best-effort cleanup of the sidecar */ }
                throw;
            }
        }
    }
}
