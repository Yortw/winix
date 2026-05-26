namespace Winix.Clip;

/// <summary>
/// Decides which <see cref="ClipMode"/> to run, given the parsed options and
/// whether stdin actually contains bytes.
/// </summary>
/// <remarks>
/// Tier-2 re-verification 2026-05-06 finding F2: the predicate used to be
/// <c>Console.IsInputRedirected</c>, but under Git Bash / MSYS that always reports
/// true (MSYS pipes stdin even for interactive terminals). Bare <c>clip</c> in
/// Git Bash auto-detected as copy mode, read empty stdin, and silently emptied
/// the user's clipboard — contradicting the documented "bare clip = paste" contract.
/// The predicate is now "did stdin actually contain content?" The caller buffers
/// stdin before calling Resolve so the decision is made on actual byte presence
/// rather than file-descriptor type.
/// </remarks>
public static class ModeResolver
{
    /// <summary>
    /// Resolves the mode using this priority:
    /// <list type="number">
    ///   <item><c>--clear</c> always wins.</item>
    ///   <item><c>-c</c> / <c>--copy</c> forces copy.</item>
    ///   <item><c>-p</c> / <c>--paste</c> forces paste.</item>
    ///   <item>Stdin contains content → copy.</item>
    ///   <item>Otherwise → paste.</item>
    /// </list>
    /// </summary>
    /// <param name="stdinHasContent">
    /// True if stdin actually contained one or more bytes. The caller is responsible for
    /// buffering stdin and computing this predicate; the value is unused for the
    /// <c>--clear</c> / <c>-c</c> / <c>-p</c> branches but must still be supplied.
    /// </param>
    /// <param name="options">The validated parsed options.</param>
    /// <returns>The <see cref="ClipMode"/> to execute.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static ClipMode Resolve(bool stdinHasContent, ClipOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Clear)
        {
            return ClipMode.Clear;
        }

        if (options.ForceCopy)
        {
            return ClipMode.Copy;
        }

        if (options.ForcePaste)
        {
            return ClipMode.Paste;
        }

        return stdinHasContent ? ClipMode.Copy : ClipMode.Paste;
    }
}
