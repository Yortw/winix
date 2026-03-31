using System.Diagnostics;

namespace Yort.ShellKit;

/// <summary>
/// Checks whether file paths are ignored by gitignore rules. Wraps <c>git check-ignore</c>
/// for correctness — handles nested .gitignore, .git/info/exclude, and the user's global
/// core.excludesFile without reimplementing the matching logic.
/// </summary>
/// <remarks>
/// <para>
/// Each call to <see cref="IsIgnored"/> spawns a short-lived <c>git check-ignore -q</c>
/// process. A long-running <c>--stdin</c> process would be more efficient, but git buffers
/// all output until stdin closes rather than flushing line-by-line, making the
/// stdin/stdout ping-pong protocol unworkable.
/// </para>
/// <para>
/// For typical file-walking scenarios the per-invocation overhead is acceptable; git
/// starts quickly and results are I/O-bound anyway.
/// </para>
/// </remarks>
public sealed class GitIgnoreFilter : IDisposable
{
    private readonly string _rootPath;
    private bool _disposed;

    private GitIgnoreFilter(string rootPath)
    {
        _rootPath = rootPath;
    }

    /// <summary>
    /// Creates a <see cref="GitIgnoreFilter"/> for the given directory. Returns <see langword="null"/>
    /// if git is not on PATH or <paramref name="rootPath"/> is not inside a git working tree.
    /// </summary>
    /// <param name="rootPath">Absolute path to the directory to use as the git working directory.</param>
    public static GitIgnoreFilter? Create(string rootPath)
    {
        // Verify git is available and the directory is inside a git repo before constructing.
        try
        {
            var checkPsi = new ProcessStartInfo("git", "rev-parse --git-dir")
            {
                WorkingDirectory = rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var checkProcess = Process.Start(checkPsi);
            if (checkProcess is null) { return null; }
            checkProcess.WaitForExit(5000);
            if (checkProcess.ExitCode != 0) { return null; }
        }
        catch { return null; }

        return new GitIgnoreFilter(rootPath);
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="relativePath"/> is ignored by gitignore rules.
    /// </summary>
    /// <param name="relativePath">
    /// Path relative to the git root. Use forward slashes. Append a trailing <c>/</c> when
    /// querying a directory (e.g. <c>bin/</c>) so git evaluates directory-specific patterns.
    /// </param>
    /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
    public bool IsIgnored(string relativePath)
    {
        if (_disposed) { throw new ObjectDisposedException(nameof(GitIgnoreFilter)); }

        try
        {
            // -q: no output, exit code only. Exit 0 = ignored, 1 = not ignored, 128 = error.
            var psi = new ProcessStartInfo("git", $"check-ignore -q -- {EscapeArg(relativePath)}")
            {
                WorkingDirectory = _rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) { return false; }
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Checks multiple paths in a single <c>git check-ignore --stdin</c> invocation per chunk.
    /// Returns the subset of paths that are ignored. Far more efficient than calling
    /// <see cref="IsIgnored"/> per file — one process per batch instead of one per path.
    /// </summary>
    /// <remarks>
    /// Paths are processed in chunks of 100 to avoid pipe buffer deadlock. Each chunk's
    /// output is small relative to the OS pipe buffer (4 KB on Windows, 64 KB on Linux),
    /// so synchronous write-then-read within a chunk is safe.
    /// </remarks>
    /// <param name="relativePaths">Paths relative to the git root, using forward slashes.</param>
    /// <returns>A set of the paths from <paramref name="relativePaths"/> that are ignored.</returns>
    public HashSet<string> CheckBatch(IReadOnlyList<string> relativePaths)
    {
        if (_disposed) { throw new ObjectDisposedException(nameof(GitIgnoreFilter)); }

        var ignored = new HashSet<string>(StringComparer.Ordinal);
        if (relativePaths.Count == 0) { return ignored; }

        const int chunkSize = 100;
        for (int offset = 0; offset < relativePaths.Count; offset += chunkSize)
        {
            int count = Math.Min(chunkSize, relativePaths.Count - offset);
            CheckBatchChunk(relativePaths, offset, count, ignored);
        }

        return ignored;
    }

    private void CheckBatchChunk(IReadOnlyList<string> paths, int offset, int count, HashSet<string> ignored)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "check-ignore --stdin")
            {
                WorkingDirectory = _rootPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) { return; }

            for (int i = offset; i < offset + count; i++)
            {
                process.StandardInput.WriteLine(paths[i]);
            }
            process.StandardInput.Close();

            string stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            foreach (string line in stdout.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    ignored.Add(trimmed);
                }
            }
        }
        catch
        {
            // If git fails, treat nothing as ignored
        }
    }

    /// <summary>Disposes this instance. No process is held open, so this is a no-op.</summary>
    public void Dispose()
    {
        _disposed = true;
    }

    // Wraps the path in double quotes and escapes any embedded double quotes.
    // git on Windows handles this correctly when UseShellExecute = false.
    private static string EscapeArg(string path)
    {
        return "\"" + path.Replace("\"", "\\\"") + "\"";
    }
}
