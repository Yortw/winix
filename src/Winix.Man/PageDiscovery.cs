#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Winix.Man;

/// <summary>
/// Locates man page files by searching a prioritised list of base directories.
/// </summary>
/// <remarks>
/// <para>
/// Searches are performed in the order: bundled pages → MANPATH environment variable entries →
/// platform-detected well-known locations (via <see cref="WellKnownPaths"/>).
/// </para>
/// <para>
/// When no section is specified, sections are tried in the traditional Unix preference order:
/// 1, 8, 6, 2, 3, 4, 5, 7.  Both plain files (<c>name.N</c>) and gzip-compressed files
/// (<c>name.N.gz</c>) are recognised; plain files take priority over compressed ones within
/// the same section directory.
/// </para>
/// </remarks>
public sealed class PageDiscovery
{
    /// <summary>
    /// Section search order when no explicit section is requested.
    /// Matches traditional man(1) behaviour: user commands first, then admin, games, syscalls, library, etc.
    /// </summary>
    private static readonly int[] SectionSearchOrder = { 1, 8, 6, 2, 3, 4, 5, 7 };

    private readonly IReadOnlyList<string> _searchPaths;

    /// <summary>
    /// Initialises a new <see cref="PageDiscovery"/> instance with the given search paths.
    /// </summary>
    /// <param name="searchPaths">
    /// Ordered list of base directories to search. Each entry is expected to contain
    /// subdirectories named <c>man1</c>, <c>man2</c>, … <c>man8</c>.
    /// </param>
    public PageDiscovery(IReadOnlyList<string> searchPaths)
    {
        _searchPaths = searchPaths;
    }

    /// <summary>
    /// Searches for a man page by name, optionally restricting the search to a specific section.
    /// </summary>
    /// <param name="name">The page name to search for (e.g. <c>"ls"</c>).</param>
    /// <param name="section">
    /// When provided, only the specified section is searched.
    /// When <see langword="null"/>, all sections are searched in the traditional preference order.
    /// </param>
    /// <returns>
    /// The full path to the first matching man page file, or <see langword="null"/> if no page was found.
    /// </returns>
    public string? FindPage(string name, int? section = null)
    {
        if (section.HasValue)
        {
            return FindInSection(name, section.Value);
        }

        foreach (int sec in SectionSearchOrder)
        {
            string? result = FindInSection(name, sec);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the ordered list of base directories this instance was constructed with.
    /// </summary>
    /// <returns>The effective search path, in priority order.</returns>
    public IReadOnlyList<string> GetEffectiveSearchPath()
    {
        return _searchPaths;
    }

    /// <summary>
    /// Searches all base directories for a page in the specified section.
    /// </summary>
    /// <param name="name">The page name.</param>
    /// <param name="section">The manual section number.</param>
    /// <returns>Full path to the page, or <see langword="null"/> if not found.</returns>
    private string? FindInSection(string name, int section)
    {
        foreach (string basePath in _searchPaths)
        {
            string sectionDir = Path.Combine(basePath, $"man{section}");
            if (!Directory.Exists(sectionDir))
            {
                continue;
            }

            // Prefer plain text over compressed when both are present.
            string plain = Path.Combine(sectionDir, $"{name}.{section}");
            if (File.Exists(plain))
            {
                return plain;
            }

            string gz = Path.Combine(sectionDir, $"{name}.{section}.gz");
            if (File.Exists(gz))
            {
                return gz;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a prioritised search path from three sources: a bundled <c>man/</c> subdirectory
    /// next to the executable, the <c>MANPATH</c> environment variable, and platform-detected
    /// well-known locations.
    /// </summary>
    /// <param name="exeDirectory">
    /// The directory containing the running executable. A <c>man</c> subdirectory here is
    /// prepended to the path so bundled pages always take highest priority.
    /// </param>
    /// <param name="manpathEnv">
    /// The value of the <c>MANPATH</c> environment variable, or <see langword="null"/> if it is not set.
    /// Entries are separated by <see cref="Path.PathSeparator"/>; non-existent paths are silently skipped.
    /// </param>
    /// <returns>
    /// An ordered, deduplicated list of existing directories ready to pass to a
    /// <see cref="PageDiscovery"/> constructor.
    /// </returns>
    public static IReadOnlyList<string> BuildSearchPaths(string exeDirectory, string? manpathEnv)
    {
        var paths = new List<string>();

        // 1. Bundled pages shipped alongside the binary take highest priority.
        string bundled = Path.Combine(exeDirectory, "man");
        if (Directory.Exists(bundled))
        {
            paths.Add(bundled);
        }

        // 2. MANPATH environment variable — explicit user configuration.
        if (manpathEnv is not null)
        {
            foreach (string entry in manpathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = entry.Trim();
                if (trimmed.Length > 0 && Directory.Exists(trimmed))
                {
                    paths.Add(trimmed);
                }
            }
        }

        // 3. Platform-detected well-known locations (deduplicated against entries already added).
        foreach (string path in WellKnownPaths.Detect())
        {
            if (!paths.Contains(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}
