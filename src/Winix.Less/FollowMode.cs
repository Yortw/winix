#nullable enable

using System;
using System.Threading;

namespace Winix.Less;

/// <summary>
/// Provides the tail-follow loop used when the pager is in follow mode (<c>F</c> key or <c>+F</c> flag).
/// Polls the input source for new content and notifies the caller when lines are appended.
/// </summary>
internal static class FollowMode
{
    /// <summary>
    /// Enters follow mode: polls <paramref name="source"/> for new content until a key is pressed.
    /// </summary>
    /// <param name="source">The input source to poll. Must not be <see langword="null"/>.</param>
    /// <param name="onNewContent">
    /// Callback invoked whenever <paramref name="source"/> reports new content.
    /// Typically triggers a screen re-render.
    /// </param>
    /// <param name="checkForKeyPress">
    /// A function that returns <see langword="true"/> when a keypress is available.
    /// When it returns <see langword="true"/> the loop exits immediately so the caller
    /// can read and process the key.
    /// </param>
    internal static void Enter(InputSource source, Action onNewContent, Func<bool> checkForKeyPress)
    {
        while (true)
        {
            // Exit as soon as a key is waiting — the caller will read and handle it.
            if (checkForKeyPress())
            {
                return;
            }

            if (source.PollForNewContent())
            {
                onNewContent();
            }

            // Short sleep to avoid busy-polling while still being responsive.
            Thread.Sleep(250);
        }
    }
}
