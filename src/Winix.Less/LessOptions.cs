#nullable enable

namespace Winix.Less;

/// <summary>
/// Resolved configuration for the less pager, combining the <c>LESS</c> environment variable
/// with any CLI flags supplied at invocation time. CLI flags take precedence over the env var.
/// </summary>
/// <remarks>
/// Use <see cref="Resolve"/> to construct an instance. Because this is a <c>sealed record</c>,
/// callers can use <c>with</c> expressions to derive a modified copy at runtime (e.g. toggling
/// <see cref="ShowLineNumbers"/> interactively).
/// </remarks>
public sealed record LessOptions
{
    /// <summary>
    /// Show line numbers in the left margin. Corresponds to <c>-N</c>.
    /// </summary>
    public bool ShowLineNumbers { get; init; }

    /// <summary>
    /// Chop (truncate) long lines instead of wrapping them. Corresponds to <c>-S</c>.
    /// </summary>
    public bool ChopLongLines { get; init; }

    /// <summary>
    /// Quit automatically if the entire file fits on one screen. Corresponds to <c>-F</c>.
    /// Defaults to <see langword="true"/> when neither the <c>LESS</c> env var nor a CLI flag is specified.
    /// </summary>
    public bool QuitIfOneScreen { get; init; }

    /// <summary>
    /// Pass raw ANSI escape sequences through to the terminal unchanged. Corresponds to <c>-R</c>.
    /// Defaults to <see langword="true"/> when neither the <c>LESS</c> env var nor a CLI flag is specified.
    /// </summary>
    public bool RawAnsi { get; init; }

    /// <summary>
    /// Do not clear the screen when exiting. Corresponds to <c>-X</c>.
    /// Defaults to <see langword="true"/> when neither the <c>LESS</c> env var nor a CLI flag is specified.
    /// </summary>
    public bool NoClearOnExit { get; init; }

    /// <summary>
    /// Perform case-insensitive search when the pattern contains no uppercase letters (smart-case).
    /// Corresponds to <c>-i</c>.
    /// </summary>
    public bool IgnoreCase { get; init; }

    /// <summary>
    /// Always perform case-insensitive search, regardless of the pattern's case. Corresponds to <c>-I</c>.
    /// </summary>
    public bool ForceIgnoreCase { get; init; }

    /// <summary>
    /// Begin tailing the file in follow mode immediately on startup. Corresponds to the <c>+F</c> command.
    /// </summary>
    public bool FollowOnStart { get; init; }

    /// <summary>
    /// Jump to the end of the file on startup. Corresponds to the <c>+G</c> command.
    /// </summary>
    public bool StartAtEnd { get; init; }

    /// <summary>
    /// An initial search pattern to apply immediately on startup. Corresponds to <c>+/pattern</c>.
    /// <see langword="null"/> means no initial search.
    /// </summary>
    public string? InitialSearch { get; init; }

    /// <summary>
    /// Resolves a <see cref="LessOptions"/> instance from the optional <c>LESS</c> environment variable
    /// and any CLI flags provided to the tool.
    /// </summary>
    /// <param name="cliFlags">
    /// CLI arguments that are flag-like (e.g. <c>-N</c>, <c>-S</c>, <c>+F</c>, <c>+/pattern</c>).
    /// Unknown flags are silently ignored.
    /// </param>
    /// <param name="lessEnvVar">
    /// The value of the <c>LESS</c> environment variable, or <see langword="null"/> / empty to use
    /// modern defaults (<c>-F -R -X</c>).
    /// </param>
    /// <returns>A fully resolved <see cref="LessOptions"/>.</returns>
    /// <remarks>
    /// Resolution order (lowest → highest priority):
    /// <list type="number">
    ///   <item>Modern defaults (<c>F=true, R=true, X=true</c>) — applied only when <paramref name="lessEnvVar"/> is null or empty.</item>
    ///   <item><c>LESS</c> env var flags — when non-empty, replaces defaults entirely (all-false baseline first).</item>
    ///   <item>CLI flags — always applied last, overriding whatever the env var produced.</item>
    /// </list>
    /// </remarks>
    public static LessOptions Resolve(string[] cliFlags, string? lessEnvVar)
    {
        // Start from the appropriate baseline.
        bool quitIfOneScreen;
        bool rawAnsi;
        bool noClearOnExit;
        bool showLineNumbers;
        bool chopLongLines;
        bool ignoreCase;
        bool forceIgnoreCase;

        if (string.IsNullOrEmpty(lessEnvVar))
        {
            // Null/empty env → modern defaults: -F -R -X on, everything else off.
            quitIfOneScreen = true;
            rawAnsi = true;
            noClearOnExit = true;
            showLineNumbers = false;
            chopLongLines = false;
            ignoreCase = false;
            forceIgnoreCase = false;
        }
        else
        {
            // Non-empty env → all-false baseline, then parse each char.
            quitIfOneScreen = false;
            rawAnsi = false;
            noClearOnExit = false;
            showLineNumbers = false;
            chopLongLines = false;
            ignoreCase = false;
            forceIgnoreCase = false;

            foreach (char ch in lessEnvVar)
            {
                switch (ch)
                {
                    case 'F': quitIfOneScreen = true; break;
                    case 'R': rawAnsi = true; break;
                    case 'X': noClearOnExit = true; break;
                    case 'N': showLineNumbers = true; break;
                    case 'S': chopLongLines = true; break;
                    case 'i': ignoreCase = true; break;
                    case 'I': forceIgnoreCase = true; break;
                    default: break; // unknown chars silently ignored
                }
            }
        }

        // CLI flags override whatever the env produced.
        bool followOnStart = false;
        bool startAtEnd = false;
        string? initialSearch = null;

        foreach (string flag in cliFlags)
        {
            switch (flag)
            {
                case "-F": quitIfOneScreen = true; break;
                case "-R": rawAnsi = true; break;
                case "-X": noClearOnExit = true; break;
                case "-N": showLineNumbers = true; break;
                case "-S": chopLongLines = true; break;
                case "-i": ignoreCase = true; break;
                case "-I": forceIgnoreCase = true; break;
                case "+F": followOnStart = true; break;
                case "+G": startAtEnd = true; break;
                default:
                    // +/pattern — CLI arg starts with "+/"
                    if (flag.StartsWith("+/", StringComparison.Ordinal))
                    {
                        initialSearch = flag.Substring(2);
                    }
                    break;
            }
        }

        return new LessOptions
        {
            QuitIfOneScreen = quitIfOneScreen,
            RawAnsi = rawAnsi,
            NoClearOnExit = noClearOnExit,
            ShowLineNumbers = showLineNumbers,
            ChopLongLines = chopLongLines,
            IgnoreCase = ignoreCase,
            ForceIgnoreCase = forceIgnoreCase,
            FollowOnStart = followOnStart,
            StartAtEnd = startAtEnd,
            InitialSearch = initialSearch,
        };
    }
}
