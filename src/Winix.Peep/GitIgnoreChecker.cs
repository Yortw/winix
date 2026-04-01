using System.Diagnostics;

namespace Winix.Peep;

/// <summary>
/// Checks whether a file is ignored by git using <c>git check-ignore</c>.
/// Falls back gracefully (returns false) if git is not available.
/// </summary>
public static class GitIgnoreChecker
{
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
    /// Returns false if git is not available or the check fails.
    /// </summary>
    public static bool IsIgnored(string filePath)
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
            psi.ArgumentList.Add(filePath);

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
    }
}
