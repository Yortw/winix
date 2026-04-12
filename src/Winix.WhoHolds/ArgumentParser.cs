#nullable enable

using System;
using System.IO;

namespace Winix.WhoHolds;

/// <summary>
/// Parses a single command-line argument into a file path, a port number, or an error.
/// </summary>
/// <remarks>
/// Resolution order:
/// <list type="number">
///   <item>Empty or whitespace-only string → error.</item>
///   <item>Colon-prefixed (e.g. ":8080") → parse port; error if out of range or non-numeric.</item>
///   <item><see cref="File.Exists"/> or <see cref="Directory.Exists"/> → file result.</item>
///   <item>Bare integer that is a valid port number → port result.</item>
///   <item>Otherwise → "not found" error.</item>
/// </list>
/// </remarks>
public static class ArgumentParser
{
    private const int MinPort = 1;
    private const int MaxPort = 65535;

    /// <summary>
    /// Parses <paramref name="argument"/> and returns a <see cref="ParsedArgument"/>
    /// indicating whether it represents a file path, a port number, or an error.
    /// </summary>
    /// <param name="argument">The raw command-line argument to parse.</param>
    /// <returns>
    /// A <see cref="ParsedArgument"/> with exactly one of
    /// <see cref="ParsedArgument.IsFile"/>, <see cref="ParsedArgument.IsPort"/>,
    /// or <see cref="ParsedArgument.IsError"/> set to <see langword="true"/>.
    /// </returns>
    public static ParsedArgument Parse(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return ParsedArgument.Error("Argument must not be empty.");
        }

        if (argument.StartsWith(":", StringComparison.Ordinal))
        {
            return ParseColonPrefixedPort(argument.Substring(1));
        }

        if (File.Exists(argument) || Directory.Exists(argument))
        {
            return ParsedArgument.ForFile(argument);
        }

        if (int.TryParse(argument, out int bareNumber))
        {
            return ValidateAndReturnPort(bareNumber);
        }

        return ParsedArgument.Error($"'{argument}' was not found as a file or directory.");
    }

    private static ParsedArgument ParseColonPrefixedPort(string portText)
    {
        if (!int.TryParse(portText, out int port))
        {
            return ParsedArgument.Error($"Invalid port: '{portText}' is not a number.");
        }

        return ValidateAndReturnPort(port);
    }

    private static ParsedArgument ValidateAndReturnPort(int port)
    {
        if (port < MinPort || port > MaxPort)
        {
            return ParsedArgument.Error($"Invalid port: {port}. Must be between {MinPort} and {MaxPort}.");
        }

        return ParsedArgument.ForPort(port);
    }
}
