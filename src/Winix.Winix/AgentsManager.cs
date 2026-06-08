#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Winix.Winix;

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
        // F5: terminate at whitespace OR at the start of "-->" so a mangled marker like
        // `v=-->` yields null (not the garbage token "-->"). A single "-" is kept — real
        // pre-release versions contain one (e.g. "0.4.0-dev").
        while (e < content.Length
            && !char.IsWhiteSpace(content[e])
            && !(content[e] == '-' && e + 1 < content.Length && content[e + 1] == '-'))
        {
            e++;
        }
        return e > vIdx ? content.Substring(vIdx, e - vIdx) : null;
    }
}
