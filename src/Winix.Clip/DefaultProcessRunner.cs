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

        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }
}
