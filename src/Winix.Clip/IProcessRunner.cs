namespace Winix.Clip;

/// <summary>
/// Runs a helper binary with arguments. Abstracted so shell-out backends can be
/// tested by injecting a fake.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/>. If
    /// <paramref name="stdin"/> is non-null it is written to the process's stdin and
    /// the stream is closed. Stdout and stderr are captured into <see cref="ProcessRunResult"/>.
    /// </summary>
    /// <remarks>
    /// Arguments are passed via <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>
    /// to avoid Windows quoting/escaping bugs.
    /// </remarks>
    ProcessRunResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin);
}
