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

    // 0 = git is usable, 1 = a prior call timed out / failed in a way that means
    // git is unusable in this environment (broken install, hung credential helper,
    // antivirus locking .pack files, network FS with submodule trouble, etc.).
    // Once disabled, subsequent calls short-circuit to false rather than repeatedly
    // spawning git children that will leak or hang again. Without this, FileSystemWatcher
    // would call IsIgnored once per file event and accumulate orphan git.exe processes.
    private static int _gitDisabled;

    // Ensures the "git unusable" warning fires exactly once per process lifetime
    // even if many callers hit the timeout concurrently before _gitDisabled propagates.
    private static int _gitDisabledWarned;

    /// <summary>
    /// Where the one-shot "git unusable" warning is written. Defaults to <see cref="Console.Error"/>.
    /// Test seam: tests override to capture and assert the warning.
    /// </summary>
    internal static TextWriter FailureWriter { get; set; } = Console.Error;

    /// <summary>
    /// Resets the cache and the disabled-state. For test use only — production code
    /// should never need this. Without it, tests that exercise the disable path would
    /// permanently disable git for the rest of the test run.
    /// </summary>
    internal static void ResetForTests()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _gitDisabled, 0);
        Interlocked.Exchange(ref _gitDisabledWarned, 0);
    }

    /// <summary>
    /// Test-only view: true if a prior call disabled git via timeout or process failure.
    /// </summary>
    internal static bool IsDisabledForTests => Volatile.Read(ref _gitDisabled) != 0;

    /// <summary>
    /// Clears the cached gitignore results. Call this when <c>.gitignore</c> changes
    /// so that subsequent checks reflect the updated rules.
    /// </summary>
    public static void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Returns true if the current directory is inside a git repository.
    /// Returns false if git is not on PATH, the call times out, or git is otherwise
    /// unusable. After a single failure all subsequent calls short-circuit to false.
    /// </summary>
    public static bool IsGitRepo()
    {
        if (Volatile.Read(ref _gitDisabled) != 0) { return false; }

        try
        {
            var psi = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                // R4 SFH I3: redirect stdin + close it immediately. Git itself doesn't
                // read stdin for `rev-parse`, but credential helpers (Git Credential
                // Manager, askpass) and pre-/post-command hooks can. Without redirection
                // the spawned git inherits peep's console stdin and a hung credential
                // helper holds the parent until the 5s timeout — surfacing as "git
                // rev-parse timed out" when the real cause is a credential prompt.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("--is-inside-work-tree");

            using var process = Process.Start(psi);

            if (process is null)
            {
                DisableGit("git could not be started; gitignore filtering disabled");
                return false;
            }

            // Close stdin immediately so any helper attempting to read sees EOF and
            // either falls through silently or fails fast (rather than hanging).
            // Wrap in try/catch to mirror CommandExecutor's precedent — a fast-exiting
            // git child can have stdin gone before Close() runs.
            try { process.StandardInput.Close(); }
            catch (IOException) { /* benign — pipe already gone */ }
            catch (ObjectDisposedException) { /* same */ }

            bool exited = process.WaitForExit(5000);
            if (!exited)
            {
                // Hung child — kill it and disable git so we don't keep accumulating
                // orphan processes per FileSystemWatcher event.
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                DisableGit("git rev-parse timed out after 5s; gitignore filtering disabled");
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            // git not on PATH, permission denied, etc. Disable so we don't keep retrying.
            DisableGit("git could not be invoked; gitignore filtering disabled");
            return false;
        }
    }

    /// <summary>
    /// Returns true if the specified file path is ignored by git (per .gitignore rules).
    /// Results are cached per path to avoid spawning a git process for every
    /// FileSystemWatcher event. Returns false if git is not available, the call times
    /// out, or the check fails. After a single failure all subsequent calls
    /// short-circuit to false.
    /// </summary>
    public static bool IsIgnored(string filePath)
    {
        if (Volatile.Read(ref _gitDisabled) != 0) { return false; }

        return _cache.GetOrAdd(filePath, static path =>
        {
            // Recheck inside the factory: another thread may have disabled git
            // between our outer check and now.
            if (Volatile.Read(ref _gitDisabled) != 0) { return false; }

            try
            {
                var psi = new ProcessStartInfo("git")
                {
                    UseShellExecute = false,
                    // R4 SFH I3: see IsGitRepo above for stdin-redirect rationale.
                    RedirectStandardInput = true,
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
                    DisableGit("git check-ignore could not be started; gitignore filtering disabled");
                    return false;
                }

                try { process.StandardInput.Close(); }
                catch (IOException) { /* benign */ }
                catch (ObjectDisposedException) { /* same */ }

                bool exited = process.WaitForExit(3000);
                if (!exited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    DisableGit("git check-ignore timed out after 3s; gitignore filtering disabled");
                    return false;
                }

                // git check-ignore -q returns 0 if ignored, 1 if not ignored, 128 if error
                return process.ExitCode == 0;
            }
            catch
            {
                DisableGit("git check-ignore could not be invoked; gitignore filtering disabled");
                return false;
            }
        });
    }

    /// <summary>
    /// Marks git as unusable for the remainder of this process's lifetime and emits
    /// a one-shot stderr warning so the user knows gitignore filtering has stopped.
    /// Internal so tests can verify the disable path without invoking real git.
    /// </summary>
    internal static void DisableGit(string reason)
    {
        Interlocked.Exchange(ref _gitDisabled, 1);
        if (Interlocked.CompareExchange(ref _gitDisabledWarned, 1, 0) == 0)
        {
            // Diagnostic write must be strictly weaker than production: a failing
            // FailureWriter must not mask the original git failure.
            try
            {
                FailureWriter.WriteLine($"peep: warning: {reason}");
            }
            catch
            {
                // best-effort — see comment above.
            }
        }
    }
}
