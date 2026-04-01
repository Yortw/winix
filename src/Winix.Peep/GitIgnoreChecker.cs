using System.Collections.Concurrent;
using System.Diagnostics;

namespace Winix.Peep;

/// <summary>
/// Checks whether a file is ignored by git using <c>git check-ignore</c>.
/// Falls back gracefully (returns false) if git is not available.
/// Results are cached per path to avoid spawning a git process per FileSystemWatcher event.
/// </summary>
public static class GitIgnoreChecker
{
    // Cache gitignore results to avoid spawning a git process per FSW event.
    // FileSystemWatcher fires per-file before debouncing, so without caching a
    // git checkout touching many files would spawn hundreds of git processes.
    private static readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if the current directory is inside a git repository.
    /// </summary>
    public static bool IsGitRepo()
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("--is-inside-work-tree");

            using var process = Process.Start(psi);

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            // git not on PATH or not installed
            return false;
        }
    }

    /// <summary>
    /// Returns true if the specified file path is ignored by git (per .gitignore rules).
    /// Results are cached per path to avoid spawning a git process for every
    /// FileSystemWatcher event. Returns false if git is not available or the check fails.
    /// </summary>
    public static bool IsIgnored(string filePath)
    {
        return _cache.GetOrAdd(filePath, static path =>
        {
            try
            {
                var psi = new ProcessStartInfo("git")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("check-ignore");
                psi.ArgumentList.Add("-q");
                psi.ArgumentList.Add("--");
                psi.ArgumentList.Add(path);

                using var process = Process.Start(psi);

                if (process is null)
                {
                    return false;
                }

                process.WaitForExit(3000);

                // git check-ignore -q returns 0 if ignored, 1 if not ignored, 128 if error
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });
    }
}
