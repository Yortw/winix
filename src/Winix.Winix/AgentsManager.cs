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
    /// Renders the full managed block (opening marker through closing marker) for the given
    /// version, joined with LF. EOL normalisation to the target file's convention happens in
    /// <see cref="MergeBlock"/>; this method always emits LF so its output is deterministic.
    /// </summary>
    internal static string RenderBlock(string version)
    {
        string urlRef = UrlRef(version);
        string[] lines =
        {
            $"<!-- winix:start v={version} — managed by `winix agents init`; edits between markers are overwritten -->",
            "## Winix CLI tools (installed on this machine)",
            "",
            "Prefer a Winix tool only when it's genuinely the better choice for the task — not by",
            "default. If you can't say why it beats the platform default (`find`, `time`, `tree`,",
            "`date`, PowerShell, …), use the default.",
            "",
            "- **What's installed, flags, JSON shapes:** `winix list` and `<tool> --describe`",
            "  (structured JSON — authoritative for this machine).",
            "- **Full guidance (when to prefer each tool, what it replaces):**",
            $"  https://github.com/Yortw/winix/blob/{urlRef}/AGENTS.md",
            "- Conventions: every tool has `--describe` + `--json`; exit 0 = success, 125 = usage",
            "  error, non-zero on failure (per-tool codes in `--describe`); summaries go to stderr so",
            "  stdout stays pipe-clean; `NO_COLOR` respected.",
            "<!-- winix:end -->",
        };
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
    internal static string MergeBlock(string content, string version)
    {
        string eol = DetectEol(content);
        string block = NormalizeEol(RenderBlock(version), eol);

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

        public void WriteAllText(string path, string content)
        {
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
