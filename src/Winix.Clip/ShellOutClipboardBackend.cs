namespace Winix.Clip;

/// <summary>
/// Clipboard backend that shells out to a helper binary (wl-copy, xclip, xsel,
/// pbcopy). Uses <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> to
/// avoid Windows argument-quoting bugs (per project convention).
/// </summary>
public sealed class ShellOutClipboardBackend : IClipboardBackend
{
    private readonly ClipboardHelperSet _helpers;
    private readonly IProcessRunner _runner;

    /// <summary>
    /// Constructs a backend wired to the given helper set and process runner.
    /// </summary>
    public ShellOutClipboardBackend(ClipboardHelperSet helpers, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(helpers);
        ArgumentNullException.ThrowIfNull(runner);
        _helpers = helpers;
        _runner = runner;
    }

    /// <inheritdoc />
    public void CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = _runner.Run(_helpers.CopyBinary, _helpers.CopyArgs, text);
        ThrowIfFailed(result, _helpers.CopyBinary);
    }

    /// <inheritdoc />
    public string PasteText()
    {
        var result = _runner.Run(_helpers.PasteBinary, _helpers.PasteArgs, stdin: null);
        ThrowIfFailed(result, _helpers.PasteBinary);
        return result.Stdout;
    }

    /// <inheritdoc />
    public void Clear()
    {
        string? stdin = _helpers.ClearUsesEmptyStdin ? string.Empty : null;
        var result = _runner.Run(_helpers.ClearBinary, _helpers.ClearArgs, stdin);
        ThrowIfFailed(result, _helpers.ClearBinary);
    }

    private static void ThrowIfFailed(ProcessRunResult result, string binary)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        string detail = string.IsNullOrWhiteSpace(result.Stderr)
            ? $"{binary} exited with {result.ExitCode}"
            : $"{binary} exited with {result.ExitCode}: {result.Stderr.Trim()}";
        throw new ClipboardException(detail);
    }
}
