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
    /// Returns <see langword="true"/> when <paramref name="command"/> can be
    /// found and started from <c>PATH</c>; <see langword="false"/> otherwise.
    /// </summary>
    /// <remarks>
    /// The command is started with <c>--version</c> and killed immediately — we
    /// only care whether <see cref="Process.Start"/> succeeds, not what the
    /// process does.
    /// </remarks>
    /// <param name="command">The executable name to probe.</param>
    public static bool IsOnPath(string command)
    {
        var psi = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--version");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            // Kill immediately — we only needed to confirm the binary exists.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited before we could kill it — that's fine.
            }

            return true;
        }
        catch (Win32Exception)
        {
            // Any Win32Exception here means the executable wasn't found or
            // couldn't be launched.
            return false;
        }
    }
}
