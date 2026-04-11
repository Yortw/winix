#nullable enable

using System.IO;
using System.IO.Compression;
using System.Text;

namespace Winix.Man;

/// <summary>
/// Reads man page files from disk, transparently decompressing gzip-compressed files (.gz extension).
/// </summary>
/// <remarks>
/// Many Linux distributions store man pages compressed to save disk space. This reader handles both
/// plain text man pages and gzip-compressed pages without the caller needing to know which format is used.
/// </remarks>
public static class ManPageFileReader
{
    /// <summary>
    /// Reads the content of a man page file, decompressing it if the file has a <c>.gz</c> extension.
    /// </summary>
    /// <param name="filePath">The full path to the man page file (e.g. <c>/usr/share/man/man1/ls.1</c> or <c>/usr/share/man/man1/ls.1.gz</c>).</param>
    /// <returns>The raw groff/troff source text of the man page.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="filePath"/> does not exist on disk.</exception>
    public static string Read(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Man page file not found: {filePath}", filePath);
        }

        if (filePath.EndsWith(".gz", System.StringComparison.OrdinalIgnoreCase))
        {
            using var fs = File.OpenRead(filePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        return File.ReadAllText(filePath, Encoding.UTF8);
    }
}
