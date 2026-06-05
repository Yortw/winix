using System.Text;

namespace Yort.ShellKit;

/// <summary>
/// A token parsed from a raw Windows command line: the text as the CRT rules resolve it,
/// plus whether any part of the token was enclosed in double quotes.
/// </summary>
/// <param name="Text">The parsed token text (quotes and escapes resolved).</param>
/// <param name="WasQuoted">True if any portion of the token was inside double quotes.</param>
public readonly record struct CommandLineToken(string Text, bool WasQuoted);

/// <summary>
/// Splits a raw Windows command line (as returned by <c>Environment.CommandLine</c> /
/// <c>GetCommandLineW</c>) into tokens using the same rules the .NET runtime uses to build
/// <c>Main</c>'s <c>args[]</c>, additionally tracking per-token quoting. Quoting information
/// is what lets glob expansion honour <c>tool "*.txt"</c> as a literal in cmd.exe.
/// </summary>
/// <remarks>
/// Rules implemented (CRT post-2008 / <c>CommandLineToArgvW</c>, mirrored by the runtime):
/// argv[0] has no escape processing (quoted → runs to closing quote; else to whitespace);
/// for later args, 2n backslashes before a quote emit n backslashes and the quote toggles
/// quoted mode, 2n+1 emit n backslashes plus a literal quote, backslashes not before a
/// quote are literal, and a doubled quote inside a quoted region emits one literal quote.
/// The doubled-quote rule is pinned empirically by RawCommandLineOracleTests_Windows.
/// </remarks>
public static class RawCommandLineTokenizer
{
    /// <summary>Tokenizes a raw command line. Returns an empty list for an empty input.</summary>
    /// <param name="rawCommandLine">The raw command line, including the program path (argv[0]).</param>
    public static IReadOnlyList<CommandLineToken> Tokenize(string rawCommandLine)
    {
        var tokens = new List<CommandLineToken>();
        int i = 0;
        int n = rawCommandLine.Length;

        if (n == 0)
        {
            return tokens;
        }

        // argv[0]: simpler rule — no backslash escaping.
        {
            var sb = new StringBuilder();
            bool quoted = false;
            if (rawCommandLine[i] == '"')
            {
                quoted = true;
                i++;
                while (i < n && rawCommandLine[i] != '"')
                {
                    sb.Append(rawCommandLine[i]);
                    i++;
                }
                if (i < n)
                {
                    i++; // skip closing quote
                }
            }
            else
            {
                while (i < n && rawCommandLine[i] != ' ' && rawCommandLine[i] != '\t')
                {
                    sb.Append(rawCommandLine[i]);
                    i++;
                }
            }
            tokens.Add(new CommandLineToken(sb.ToString(), quoted));
        }

        while (true)
        {
            while (i < n && (rawCommandLine[i] == ' ' || rawCommandLine[i] == '\t'))
            {
                i++;
            }
            if (i >= n)
            {
                break;
            }

            var sb = new StringBuilder();
            bool sawQuote = false;
            bool inQuotes = false;
            while (i < n)
            {
                char c = rawCommandLine[i];

                if (c == '\\')
                {
                    int backslashes = 0;
                    while (i < n && rawCommandLine[i] == '\\')
                    {
                        backslashes++;
                        i++;
                    }
                    if (i < n && rawCommandLine[i] == '"')
                    {
                        sb.Append('\\', backslashes / 2);
                        if (backslashes % 2 == 1)
                        {
                            sb.Append('"'); // escaped literal quote; quote char consumed
                            i++;
                        }
                        // even count: quote left in place — handled as delimiter next iteration
                    }
                    else
                    {
                        sb.Append('\\', backslashes);
                    }
                    continue;
                }

                if (c == '"')
                {
                    sawQuote = true;
                    if (inQuotes && i + 1 < n && rawCommandLine[i + 1] == '"')
                    {
                        sb.Append('"'); // "" inside quotes → literal quote (oracle-pinned)
                        i += 2;
                        continue;
                    }
                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }

                if (!inQuotes && (c == ' ' || c == '\t'))
                {
                    break;
                }

                sb.Append(c);
                i++;
            }
            tokens.Add(new CommandLineToken(sb.ToString(), sawQuote));
        }

        return tokens;
    }
}
