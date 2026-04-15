#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Winix.Schedule;

/// <summary>
/// Generates short task names automatically from command strings.
/// </summary>
public static class NameGenerator
{
    private const int MaxLength = 50;
    private const string DefaultName = "task";

    // Replaces any run of non-alphanumeric characters with a single hyphen.
    private static readonly Regex NonAlphanumericRun = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled);

    // Matches a "bare word" sub-command token: only letters, digits, hyphens, and underscores.
    // URLs, file paths, and flags are excluded so they don't pollute generated names.
    private static readonly Regex BareWord = new Regex(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Derives a task name from a command string.
    /// Strips any directory path and file extension from the executable, then optionally
    /// appends a hyphen-separated second token (e.g. a sub-command) if present.
    /// The result is lowercased, non-alphanumeric characters replaced with hyphens,
    /// and limited to 50 characters. Returns <c>"task"</c> for empty or whitespace input.
    /// </summary>
    /// <param name="command">
    /// The command string to derive a name from, e.g. <c>"dotnet build"</c> or
    /// <c>"/usr/bin/curl http://example.com"</c>.
    /// </param>
    /// <returns>A sanitised, lowercased task name of at most 50 characters.</returns>
    public static string FromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return DefaultName;
        }

        // Split on whitespace and take the first two tokens.
        string[] tokens = command.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        // Strip path from the executable (first token). Handle both '/' and '\' explicitly
        // because Path.GetFileNameWithoutExtension only recognises the platform's native
        // separator — on Linux/macOS, '\' is a valid filename character, so a Windows-style
        // path like "C:\tools\backup.bat" would not be split correctly.
        string firstToken = tokens[0];
        int lastSeparator = Math.Max(firstToken.LastIndexOf('/'), firstToken.LastIndexOf('\\'));
        string fileNameWithExtension = lastSeparator >= 0
            ? firstToken.Substring(lastSeparator + 1)
            : firstToken;
        string baseName = Path.GetFileNameWithoutExtension(fileNameWithExtension);

        // Append the second token only when it looks like a bare sub-command word (letters, digits,
        // hyphens, underscores). URLs, file paths, and flags are skipped so they don't pollute the name.
        string raw;
        if (tokens.Length >= 2 && BareWord.IsMatch(tokens[1]))
        {
            raw = baseName + "-" + tokens[1];
        }
        else
        {
            raw = baseName;
        }

        // Lowercase and replace non-alphanumeric runs with a single hyphen.
        string sanitised = NonAlphanumericRun.Replace(raw.ToLowerInvariant(), "-").Trim('-');

        if (sanitised.Length == 0)
        {
            return DefaultName;
        }

        if (sanitised.Length > MaxLength)
        {
            sanitised = sanitised.Substring(0, MaxLength).TrimEnd('-');
        }

        return sanitised;
    }
}
