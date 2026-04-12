#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Less;

/// <summary>
/// Main event loop for the interactive pager. Handles scrolling, searching, follow mode,
/// and interactive option toggles.
/// </summary>
public sealed class Pager
{
    // Mutable: interactive toggles (-N, -S) replace it via `with` expressions.
    private LessOptions _options;

    /// <summary>
    /// Initialises a new <see cref="Pager"/> with the supplied options.
    /// </summary>
    /// <param name="options">The resolved pager options. Must not be <see langword="null"/>.</param>
    public Pager(LessOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Runs the pager against the supplied <paramref name="source"/>, blocking until the user quits.
    /// </summary>
    /// <param name="source">The content source to display. Must not be <see langword="null"/>.</param>
    /// <returns>Exit code — always 0 on a clean quit.</returns>
    /// <remarks>
    /// If <see cref="LessOptions.QuitIfOneScreen"/> is set and the content fits within the
    /// terminal viewport, the content is written directly to stdout and the method returns
    /// without entering the interactive loop.
    /// </remarks>
    public int Run(InputSource source)
    {
        IReadOnlyList<string> lines = source.Lines;

        // Determine terminal dimensions early for quit-if-one-screen check.
        int termHeight;
        int termWidth;

        try
        {
            termHeight = Console.WindowHeight;
            termWidth = Console.WindowWidth;
        }
        catch (Exception)
        {
            termHeight = 0;
            termWidth = 0;
        }

        if (termHeight <= 0) { termHeight = 24; }
        if (termWidth <= 0) { termWidth = 80; }

        int viewHeight = termHeight - 1;

        // Quit-if-one-screen: if all content fits, just print it and exit.
        if (_options.QuitIfOneScreen)
        {
            int displayRows = LineWrapper.CalculateDisplayRows(lines, termWidth);
            if (displayRows <= viewHeight)
            {
                // Write lines without a trailing newline on the last line —
                // Console.WriteLine on the final line would scroll the screen
                // when content exactly fills the terminal height.
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i < lines.Count - 1)
                    {
                        Console.WriteLine(lines[i]);
                    }
                    else
                    {
                        Console.Write(lines[i]);
                    }
                }

                return 0;
            }
        }

        var searchEngine = new SearchEngine
        {
            IgnoreCase = _options.ForceIgnoreCase,
            SmartCase = _options.IgnoreCase
        };

        int topLine = 0;
        int leftColumn = 0;
        bool isFollowing = false;

        // Apply startup commands.
        if (!string.IsNullOrEmpty(_options.InitialSearch))
        {
            int? match = searchEngine.FindNext(lines, _options.InitialSearch, 0);
            if (match.HasValue)
            {
                topLine = match.Value;
            }
        }

        if (_options.StartAtEnd || _options.FollowOnStart)
        {
            topLine = Math.Max(0, lines.Count - viewHeight);
        }

        if (_options.FollowOnStart)
        {
            isFollowing = true;
        }

        using (var screen = new Screen(_options))
        {
            while (true)
            {
                screen.RefreshDimensions();
                int maxTop = Math.Max(0, lines.Count - screen.ViewHeight);

                if (isFollowing)
                {
                    // Clamp in case new content arrived.
                    topLine = Math.Max(0, lines.Count - screen.ViewHeight);

                    screen.Render(
                        lines,
                        topLine,
                        leftColumn,
                        source.Name,
                        lines.Count,
                        searchEngine.CurrentPattern,
                        isFollowing: true,
                        isAtEnd: false);

                    FollowMode.Enter(
                        source,
                        onNewContent: () =>
                        {
                            // Re-read lines reference in case list grew.
                            lines = source.Lines;
                            topLine = Math.Max(0, lines.Count - screen.ViewHeight);
                            screen.Render(
                                lines,
                                topLine,
                                leftColumn,
                                source.Name,
                                lines.Count,
                                searchEngine.CurrentPattern,
                                isFollowing: true,
                                isAtEnd: false);
                        },
                        checkForKeyPress: () => Console.KeyAvailable);

                    // Consume the keypress that ended follow mode.
                    Console.ReadKey(intercept: true);
                    isFollowing = false;
                    lines = source.Lines;
                    continue;
                }

                // Refresh lines (follow mode may have added content in a prior iteration).
                lines = source.Lines;
                maxTop = Math.Max(0, lines.Count - screen.ViewHeight);

                // Clamp topLine after a potential resize or content change.
                if (topLine > maxTop)
                {
                    topLine = maxTop;
                }

                bool isAtEnd = lines.Count > 0 && topLine >= maxTop;

                screen.Render(
                    lines,
                    topLine,
                    leftColumn,
                    source.Name,
                    lines.Count,
                    searchEngine.CurrentPattern,
                    isFollowing: false,
                    isAtEnd: isAtEnd);

                var key = screen.ReadKey();

                int result = HandleKey(key, ref topLine, ref leftColumn, ref isFollowing, maxTop, screen, lines, searchEngine);
                if (result >= 0)
                {
                    return result;
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Private key-handling helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dispatches a keypress to the appropriate handler.
    /// </summary>
    /// <returns>
    /// An exit code (≥ 0) when the user has quit; -1 to continue the loop.
    /// </returns>
    private int HandleKey(
        ConsoleKeyInfo key,
        ref int topLine,
        ref int leftColumn,
        ref bool isFollowing,
        int maxTop,
        Screen screen,
        IReadOnlyList<string> lines,
        SearchEngine searchEngine)
    {
        switch (key.Key)
        {
            case ConsoleKey.Q:
                return 0;

            case ConsoleKey.J:
            case ConsoleKey.DownArrow:
            case ConsoleKey.Enter:
                if (topLine < maxTop) { topLine++; }
                return -1;

            case ConsoleKey.K:
            case ConsoleKey.UpArrow:
                if (topLine > 0) { topLine--; }
                return -1;

            case ConsoleKey.Spacebar:
            case ConsoleKey.PageDown:
                topLine = Math.Min(maxTop, topLine + screen.ViewHeight);
                return -1;

            case ConsoleKey.PageUp:
                topLine = Math.Max(0, topLine - screen.ViewHeight);
                return -1;

            case ConsoleKey.Home:
                topLine = 0;
                leftColumn = 0;
                return -1;

            case ConsoleKey.End:
                topLine = maxTop;
                return -1;

            case ConsoleKey.RightArrow:
                if (_options.ChopLongLines) { leftColumn += 8; }
                return -1;

            case ConsoleKey.LeftArrow:
                if (_options.ChopLongLines) { leftColumn = Math.Max(0, leftColumn - 8); }
                return -1;

            default:
                // Delegate printable characters to the char-key handler.
                if (key.KeyChar != '\0')
                {
                    return HandleCharKey(key.KeyChar, ref topLine, ref leftColumn, ref isFollowing, maxTop, screen, lines, searchEngine);
                }
                return -1;
        }
    }

    /// <summary>
    /// Handles printable character bindings.
    /// </summary>
    /// <returns>An exit code (≥ 0) when the user quits; -1 to continue the loop.</returns>
    private int HandleCharKey(
        char ch,
        ref int topLine,
        ref int leftColumn,
        ref bool isFollowing,
        int maxTop,
        Screen screen,
        IReadOnlyList<string> lines,
        SearchEngine searchEngine)
    {
        switch (ch)
        {
            case 'q':
            case 'Q':
                return 0;

            case 'g':
                topLine = 0;
                leftColumn = 0;
                return -1;

            case 'G':
                topLine = maxTop;
                return -1;

            case 'd':
                topLine = Math.Min(maxTop, topLine + screen.ViewHeight);
                return -1;

            case 'u':
                topLine = Math.Max(0, topLine - screen.ViewHeight);
                return -1;

            case '/':
            {
                string? pattern = screen.ReadPrompt('/');
                if (!string.IsNullOrEmpty(pattern))
                {
                    int? match = searchEngine.FindNext(lines, pattern, topLine + 1);
                    if (match.HasValue)
                    {
                        topLine = Math.Min(maxTop, match.Value);
                    }
                }
                return -1;
            }

            case '?':
            {
                string? pattern = screen.ReadPrompt('?');
                if (!string.IsNullOrEmpty(pattern))
                {
                    int? match = searchEngine.FindPrevious(lines, pattern, topLine);
                    if (match.HasValue)
                    {
                        topLine = Math.Min(maxTop, match.Value);
                    }
                }
                return -1;
            }

            case 'n':
            {
                if (!string.IsNullOrEmpty(searchEngine.CurrentPattern))
                {
                    int? match = searchEngine.FindNext(lines, searchEngine.CurrentPattern, topLine + 1);
                    if (match.HasValue)
                    {
                        topLine = Math.Min(maxTop, match.Value);
                    }
                }
                return -1;
            }

            case 'N':
            {
                if (!string.IsNullOrEmpty(searchEngine.CurrentPattern))
                {
                    int? match = searchEngine.FindPrevious(lines, searchEngine.CurrentPattern, topLine);
                    if (match.HasValue)
                    {
                        topLine = Math.Min(maxTop, match.Value);
                    }
                }
                return -1;
            }

            case 'F':
                isFollowing = true;
                return -1;

            case '-':
            {
                // Read the option key that follows '-'.
                var optKey = screen.ReadKey();

                switch (optKey.KeyChar)
                {
                    case 'N':
                        _options = _options with { ShowLineNumbers = !_options.ShowLineNumbers };
                        break;

                    case 'S':
                        _options = _options with { ChopLongLines = !_options.ChopLongLines };
                        // Reset horizontal scroll when leaving chop mode.
                        if (!_options.ChopLongLines)
                        {
                            leftColumn = 0;
                        }
                        break;
                }

                return -1;
            }

            default:
                return -1;
        }
    }
}
