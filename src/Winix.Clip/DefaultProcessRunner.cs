using System.ComponentModel;
using System.Diagnostics;

namespace Winix.Clip;

/// <summary>Real <see cref="IProcessRunner"/> that spawns a child process.</summary>
public sealed class DefaultProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public ProcessRunResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            // Process.Start throws Win32Exception when the binary can't be found or
            // can't be executed (missing /usr/bin/pbcopy on a corrupted macOS install,
            // a non-executable file on PATH, or PATH stripped to nothing). Wrap it as
            // ClipboardException so Cli.Run's catch boundary surfaces it with the
            // documented "clip:" prefix and exit code instead of a naked .NET stack
            // trace. Don't pipe ex.Message under InvariantGlobalization — surface the
            // exception type as a stable English discriminator.
            Process? started = Process.Start(psi);
            if (started is null)
            {
                throw new ClipboardException($"failed to launch '{fileName}'");
            }
            process = started;
        }
        catch (Win32Exception ex)
        {
            throw new ClipboardException(
                $"failed to launch '{fileName}' ({ex.GetType().Name})", ex);
        }

        using (process)
        {
            // Start draining both pipes before writing stdin, otherwise a child that
            // fills its stdout/stderr pipe buffer can deadlock waiting for a reader.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (stdin is not null)
            {
                try
                {
                    process.StandardInput.Write(stdin);
                    process.StandardInput.Close();
                }
                catch (IOException ex)
                {
                    // Helper exited or crashed before consuming all of stdin (broken
                    // pipe). Same wrap-as-ClipboardException treatment as the launch
                    // failure above so the dispatch catch surfaces a friendly error.
                    throw new ClipboardException(
                        $"helper '{fileName}' closed stdin before consuming the payload ({ex.GetType().Name})", ex);
                }
            }

            process.WaitForExit();

            return new ProcessRunResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
    }
}
