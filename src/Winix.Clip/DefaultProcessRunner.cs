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

        using var process = Process.Start(psi)
            ?? throw new ClipboardException($"failed to launch '{fileName}'");

        // Start draining both pipes before writing stdin, otherwise a child that
        // fills its stdout/stderr pipe buffer can deadlock waiting for a reader.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        process.WaitForExit();

        return new ProcessRunResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
