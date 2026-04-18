#nullable enable
using System;
using Yort.ShellKit;

namespace Winix.Ids;

/// <summary>
/// Parses <c>ids</c> command-line arguments into <see cref="IdsOptions"/>,
/// enforcing flag/type compatibility. Pure — produces no I/O itself, though
/// ShellKit's <see cref="CommandLineParser"/> prints help/version/describe
/// to stdout automatically when those flags are present (signalled via
/// <see cref="Result.IsHandled"/>).
/// </summary>
public static class ArgParser
{
    /// <summary>
    /// Parse result: success (<see cref="Options"/> populated), usage error
    /// (<see cref="Error"/> populated), or ShellKit-handled (<see cref="IsHandled"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="Error"/> is a bare message (no tool-name prefix) — the caller
    /// should pass it through <c>ParseResult.WriteError</c> which prepends
    /// <c>"ids: "</c> automatically, consistent with how ShellKit formats its own
    /// parse errors.
    /// </remarks>
    public sealed record Result(
        IdsOptions? Options,
        string? Error,
        bool IsHandled,
        int HandledExitCode,
        bool UseColor)
    {
        /// <summary>True if the parse produced valid options.</summary>
        public bool Success => Options is not null;
    }

    /// <summary>
    /// Parses the argument vector. The returned <see cref="Result"/> tells the caller
    /// whether to use <see cref="Result.Options"/>, print <see cref="Result.Error"/>,
    /// or exit with <see cref="Result.HandledExitCode"/>.
    /// </summary>
    public static Result Parse(string[] argv)
    {
        var parser = BuildParser();
        var parsed = parser.Parse(argv);

        // Resolve colour once — we need it in every return path, including error paths
        // so that `ids --color --type bad` still produces coloured error output.
        bool useColor = parsed.ResolveColor(checkStdErr: false);

        Result Fail(string error) => new(
            Options: null,
            Error: error,
            IsHandled: false,
            HandledExitCode: 0,
            UseColor: useColor);

        if (parsed.IsHandled)
        {
            return new Result(
                Options: null,
                Error: null,
                IsHandled: true,
                HandledExitCode: parsed.ExitCode,
                UseColor: useColor);
        }

        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0]);
        }

        // --- parse type ---
        string typeStr = parsed.GetString("--type", "uuid7");
        if (!TryParseType(typeStr, out IdType type, out string typeErr))
        {
            return Fail(typeErr);
        }

        // --- parse --count (-n) — manual so we control the error message ---
        int count = 1;
        if (parsed.Has("--count"))
        {
            string raw = parsed.GetString("--count");
            if (!int.TryParse(raw, out count))
            {
                return Fail($"--count must be an integer (got '{raw}')");
            }

            if (count < 1)
            {
                return Fail("--count must be ≥ 1");
            }
        }

        // --- parse --length (-l) ---
        int length = 21;
        if (parsed.Has("--length"))
        {
            string raw = parsed.GetString("--length");
            if (!int.TryParse(raw, out length))
            {
                return Fail($"--length must be an integer (got '{raw}')");
            }

            if (length < 1)
            {
                return Fail("--length must be ≥ 1");
            }
        }

        // --- parse --alphabet ---
        var alphabet = NanoidAlphabet.UrlSafe;
        if (parsed.Has("--alphabet"))
        {
            if (!TryParseAlphabet(parsed.GetString("--alphabet"), out alphabet, out string alphabetErr))
            {
                return Fail(alphabetErr);
            }
        }

        // --- parse --format ---
        var format = UuidFormat.Default;
        if (parsed.Has("--format"))
        {
            if (!TryParseFormat(parsed.GetString("--format"), out format, out string formatErr))
            {
                return Fail(formatErr);
            }
        }

        bool uppercase = parsed.Has("--uppercase");
        bool json = parsed.Has("--json");

        // --- Q5 compatibility matrix ---
        bool isNanoid = type == IdType.Nanoid;
        bool isUuid = type is IdType.Uuid4 or IdType.Uuid7;

        if (!isNanoid && parsed.Has("--length"))
        {
            return Fail("--length only applies to --type nanoid");
        }

        if (!isNanoid && parsed.Has("--alphabet"))
        {
            return Fail("--alphabet only applies to --type nanoid");
        }

        if (!isUuid && parsed.Has("--format"))
        {
            return Fail("--format only applies to --type uuid4 or uuid7");
        }

        if (uppercase && type == IdType.Ulid)
        {
            return Fail("ULID output is already uppercase");
        }

        if (uppercase && type == IdType.Nanoid)
        {
            return Fail("use --alphabet upper for uppercase NanoID");
        }

        var options = new IdsOptions(
            Type: type,
            Count: count,
            Length: length,
            Alphabet: alphabet,
            Format: format,
            Uppercase: uppercase,
            Json: json,
            Help: false,
            Version: false,
            Describe: false);

        return new Result(
            Options: options,
            Error: null,
            IsHandled: false,
            HandledExitCode: 0,
            UseColor: useColor);
    }

    private static CommandLineParser BuildParser()
    {
        // Version string — for now, hardcode "0.3.0" (matches Directory.Build.props).
        // Task 11 will establish the reflection-based VersionInfo pattern; until then
        // a literal avoids blocking this task on that.
        return new CommandLineParser("ids", "0.3.0")
            .Description("Cross-platform identifier generator — UUID v4, UUID v7, ULID, NanoID.")
            .StandardFlags()
            .Option("--type", "-t", "TYPE", "Identifier type: uuid4, uuid7, ulid, nanoid (default: uuid7)")
            .Option("--count", "-n", "N", "Number of IDs to generate (default: 1)")
            .Option("--length", "-l", "N", "NanoID length (default: 21, nanoid only)")
            .Option("--alphabet", null, "ALPHA", "NanoID alphabet: url-safe, alphanum, hex, lower, upper (nanoid only)")
            .Option("--format", null, "FORMAT", "UUID shape: default, hex, braces, urn (uuid only)")
            .Flag("--uppercase", "-u", "Uppercase UUID hex output");
    }

    private static bool TryParseType(string value, out IdType type, out string error)
    {
        error = "";
        switch (value)
        {
            case "uuid4":
                type = IdType.Uuid4;
                return true;
            case "uuid7":
                type = IdType.Uuid7;
                return true;
            case "ulid":
                type = IdType.Ulid;
                return true;
            case "nanoid":
                type = IdType.Nanoid;
                return true;
            default:
                type = default;
                error = $"unknown --type '{value}' (expected: uuid4, uuid7, ulid, nanoid)";
                return false;
        }
    }

    private static bool TryParseAlphabet(string value, out NanoidAlphabet alphabet, out string error)
    {
        error = "";
        switch (value)
        {
            case "url-safe":
                alphabet = NanoidAlphabet.UrlSafe;
                return true;
            case "alphanum":
                alphabet = NanoidAlphabet.Alphanum;
                return true;
            case "hex":
                alphabet = NanoidAlphabet.Hex;
                return true;
            case "lower":
                alphabet = NanoidAlphabet.Lower;
                return true;
            case "upper":
                alphabet = NanoidAlphabet.Upper;
                return true;
            default:
                alphabet = default;
                error = $"unknown --alphabet '{value}' (expected: url-safe, alphanum, hex, lower, upper)";
                return false;
        }
    }

    private static bool TryParseFormat(string value, out UuidFormat format, out string error)
    {
        error = "";
        switch (value)
        {
            case "default":
                format = UuidFormat.Default;
                return true;
            case "hex":
                format = UuidFormat.Hex;
                return true;
            case "braces":
                format = UuidFormat.Braces;
                return true;
            case "urn":
                format = UuidFormat.Urn;
                return true;
            default:
                format = default;
                error = $"unknown --format '{value}' (expected: default, hex, braces, urn)";
                return false;
        }
    }
}
