#nullable enable

using System.ComponentModel;
using System.Diagnostics;

namespace Winix.Winix;

/// <summary>
/// Holds the result of running a child process via <see cref="ProcessHelper"/>.
/// </summary>
public sealed class ProcessResult
{
    /// <summary>
    /// The process exit code, or <c>-1</c> if the process could not be started.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// The text written to stdout by the process, trimmed of leading/trailing whitespace.
    /// Empty string when nothing was written or the process could not be started.
    /// </summary>
    public string Stdout { get; }

    /// <summary>
    /// The text written to stderr by the process, trimmed of leading/trailing whitespace.
    /// Empty string when nothing was written or the process could not be started.
    /// </summary>
    public string Stderr { get; }

    /// <summary>
    /// <see langword="true"/> when the command was not found on <c>PATH</c>
    /// (Win32 error 2 or 3 on Windows; <see cref="Win32Exception"/> with those
    /// native error codes on Unix).
    /// </summary>
    public bool IsNotFound { get; }

    internal ProcessResult(int exitCode, string stdout, string stderr, bool isNotFound = false)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
        IsNotFound = isNotFound;
    }

    /// <summary>Returns a sentinel result indicating the command was not found on PATH.</summary>
    internal static ProcessResult NotFound() =>
        new ProcessResult(-1, string.Empty, string.Empty, isNotFound: true);
}

/// <summary>
/// Runs external processes and captures their output. Provides a thin, safe
/// wrapper around <see cref="Process"/> for use by package-manager adapters.
/// </summary>
public static class ProcessHelper
{
    /// <summary>
    /// Runs <paramref name="command"/> with the given <paramref name="arguments"/>,
    /// capturing stdout, stderr, and the exit code.
    /// </summary>
    /// <param name="command">
    /// The executable name or full path. Resolved via <c>PATH</c> when just a name.
    /// </param>
    /// <param name="arguments">
    /// Arguments passed via <see cref="ProcessStartInfo.ArgumentList"/> — never
    /// concatenated into a string, avoiding quoting/escaping injection bugs.
    /// </param>
    /// <returns>
    /// A <see cref="ProcessResult"/> with captured output. <see cref="ProcessResult.IsNotFound"/>
    /// is <see langword="true"/> when the command was not found on <c>PATH</c>.
    /// </returns>
    public static async Task<ProcessResult> RunAsync(string command, string[] arguments)
    {
        var psi = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Process.Start returned null for '{command}'.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            // Error 2 = ERROR_FILE_NOT_FOUND, error 3 = ERROR_PATH_NOT_FOUND —
            // the executable doesn't exist on PATH.
            return ProcessResult.NotFound();
        }

        using (process)
        {
            // Close stdin immediately; we never write to the child process.
            process.StandardInput.Close();

            // Read stdout and stderr concurrently to avoid deadlock: if we read
            // one stream synchronously while the child is blocked writing to the
            // other, both sides stall.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            return new ProcessResult(
                process.ExitCode,
                stdoutTask.Result.Trim(),
                stderrTask.Result.Trim());
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="command"/> exists as an
    /// executable in any directory on <c>PATH</c>; <see langword="false"/> otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Performs a PATH walk rather than spawning the executable. The previous
    /// implementation invoked <c>command --version</c> and killed it, which incurred
    /// process-startup latency on every probe (multiplied by 22 manifest tools × every
    /// adapter on every <c>winix list</c>), risked accidental side effects from the
    /// <c>--version</c> invocation, and produced transient process-tree entries.
    /// </para>
    /// <para>
    /// On Windows the probe consults <c>PATHEXT</c> to honour <c>.exe</c>, <c>.cmd</c>,
    /// <c>.bat</c>, etc. so package-manager shims like <c>scoop.cmd</c> are recognised
    /// even though they are not bare <c>.exe</c> files.
    /// </para>
    /// </remarks>
    /// <param name="command">The executable name (no path or extension) to probe.</param>
    public static bool IsOnPath(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return false;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return false;
        }

        string[] extensions = GetExecutableExtensions();

        foreach (string rawDir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir = rawDir.Trim();
            if (dir.Length == 0)
            {
                continue;
            }

            // Bare-name probe first — covers Linux/macOS executables and any
            // already-extensioned name on Windows (e.g. caller passed "scoop.cmd").
            string bare = Path.Combine(dir, command);
            if (File.Exists(bare))
            {
                return true;
            }

            // Each PATHEXT extension is tested only when the bare-name probe missed,
            // because the bare path may already match for tools the user typed with
            // their extension included.
            foreach (string ext in extensions)
            {
                string candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the list of executable extensions to try when probing <c>PATH</c>.
    /// On Windows, parses <c>PATHEXT</c> (e.g. <c>.COM;.EXE;.BAT;.CMD;.PS1</c>) and
    /// returns each entry lowercased and dot-prefixed. On Unix, returns an empty
    /// array so only bare-name probes apply.
    /// </summary>
    private static string[] GetExecutableExtensions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<string>();
        }

        string pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        string[] parts = pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            // Normalise: ensure dot prefix and lowercase for case-insensitive matching
            // on the comparing-against-File.Exists side (Windows filesystem is
            // case-insensitive but normalising keeps the candidate set tidy).
            if (!trimmed.StartsWith('.'))
            {
                trimmed = "." + trimmed;
            }
            result.Add(trimmed.ToLowerInvariant());
        }

        return result.ToArray();
    }
}
