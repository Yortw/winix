namespace Winix.TimeIt;

/// <summary>
/// Terminal environment detection. Proto-Yort.ShellKit — will be extracted to the shared library
/// when the second Winix tool is built.
/// </summary>
public static class ConsoleEnv
{
    /// <summary>
    /// Returns true if the <c>NO_COLOR</c> environment variable is set (any value, including empty).
    /// See https://no-color.org.
    /// </summary>
    public static bool IsNoColorEnvSet()
    {
        return Environment.GetEnvironmentVariable("NO_COLOR") is not null;
    }

    /// <summary>
    /// Returns true if the given stream (stdout or stderr) is connected to a terminal, not a pipe.
    /// </summary>
    public static bool IsTerminal(bool checkStdErr)
    {
        return checkStdErr ? !Console.IsErrorRedirected : !Console.IsOutputRedirected;
    }

    /// <summary>
    /// Resolves whether colour output should be used, applying the precedence rules:
    /// explicit flag > NO_COLOR env var > auto-detection (is terminal?).
    /// </summary>
    /// <param name="colorFlag">True if --color was passed.</param>
    /// <param name="noColorFlag">True if --no-color was passed.</param>
    /// <param name="noColorEnv">True if NO_COLOR environment variable is set.</param>
    /// <param name="isTerminal">True if the output stream is a terminal.</param>
    public static bool ResolveUseColor(bool colorFlag, bool noColorFlag, bool noColorEnv, bool isTerminal)
    {
        // Explicit flags take highest priority
        if (colorFlag)
        {
            return true;
        }

        if (noColorFlag)
        {
            return false;
        }

        // NO_COLOR env var takes next priority
        if (noColorEnv)
        {
            return false;
        }

        // Fall back to terminal detection
        return isTerminal;
    }
}
