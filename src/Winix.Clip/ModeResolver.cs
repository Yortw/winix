namespace Winix.Clip;

/// <summary>
/// Decides which <see cref="ClipMode"/> to run, given the parsed options and
/// whether stdin is redirected.
/// </summary>
public static class ModeResolver
{
    /// <summary>
    /// Resolves the mode using this priority:
    /// <list type="number">
    ///   <item><c>--clear</c> always wins.</item>
    ///   <item><c>-c</c> / <c>--copy</c> forces copy.</item>
    ///   <item><c>-p</c> / <c>--paste</c> forces paste.</item>
    ///   <item>Stdin redirected → copy.</item>
    ///   <item>Otherwise → paste.</item>
    /// </list>
    /// </summary>
    /// <param name="stdinRedirected">True if stdin is not a terminal (piped or redirected).</param>
    /// <param name="options">The validated parsed options.</param>
    /// <returns>The <see cref="ClipMode"/> to execute.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static ClipMode Resolve(bool stdinRedirected, ClipOptions options)
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

        return stdinRedirected ? ClipMode.Copy : ClipMode.Paste;
    }
}
