#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.MkSecret;

/// <summary>Parses argv into <see cref="MkSecretOptions"/>. Dispatches on the first positional
/// (password/phrase/key); bare invocation defaults to password. One <see cref="CommandLineParser"/>
/// per mode so <c>mksecret key --help</c> shows key-specific flags.</summary>
public static class ArgParser
{
    /// <summary>Parse outcome: <see cref="Options"/> on success, <see cref="Error"/> on usage error,
    /// or <see cref="IsHandled"/> when ShellKit already emitted help/version/describe.</summary>
    public sealed record Result(MkSecretOptions? Options, string? Error, bool IsHandled, int ExitCode, bool UseColor)
    {
        /// <summary>True when options parsed cleanly.</summary>
        public bool Success => Options is not null && Error is null && !IsHandled;
    }

    private static readonly Dictionary<string, SecretMode> Subcommands = new(StringComparer.Ordinal)
    {
        ["password"] = SecretMode.Password,
        ["phrase"] = SecretMode.Phrase,
        ["key"] = SecretMode.Key,
    };

    /// <summary>Parse argv (without the executable name).</summary>
    public static Result Parse(IReadOnlyList<string> argv)
    {
        SecretMode mode = SecretMode.Password;
        int start = 0;
        if (argv.Count > 0 && Subcommands.TryGetValue(argv[0], out SecretMode m))
        {
            mode = m;
            start = 1;
        }

        string[] slice = new string[argv.Count - start];
        for (int i = 0; i < slice.Length; i++) { slice[i] = argv[start + i]; }

        CommandLineParser parser = BuildParser(mode);
        ParseResult parsed = parser.Parse(slice);
        bool useColor = parsed.ResolveColor(checkStdErr: true);

        if (parsed.IsHandled) { return new Result(null, null, true, parsed.ExitCode, useColor); }
        if (parsed.HasErrors) { return Fail(parsed.Errors[0], useColor); }
        if (parsed.Positionals.Length > 0)
        {
            return Fail($"unexpected positional argument: {parsed.Positionals[0]}", useColor);
        }

        MkSecretOptions o = MkSecretOptions.Defaults with { Mode = mode };

        if (parsed.Has("--count")) { o = o with { Count = parsed.GetInt("--count") }; }
        o = o with { Json = parsed.Has("--json"), Quiet = parsed.Has("--quiet") };

        switch (mode)
        {
            case SecretMode.Password:
                if (parsed.Has("--length")) { o = o with { Length = parsed.GetInt("--length") }; }
                if (parsed.Has("--charset"))
                {
                    if (!TryParseCharset(parsed.GetString("--charset"), out Charset cs))
                    {
                        return Fail($"unknown --charset value: {parsed.GetString("--charset")}", useColor);
                    }
                    o = o with { Charset = cs };
                }
                break;
            case SecretMode.Phrase:
                if (parsed.Has("--words")) { o = o with { Words = parsed.GetInt("--words") }; }
                if (parsed.Has("--sep"))
                {
                    string sep = parsed.GetString("--sep");
                    // A newline anywhere in the separator would split one passphrase across output lines,
                    // silently corrupting line-per-secret consumers. Reject CONTAINS, not equals.
                    if (sep.Contains('\n') || sep.Contains('\r'))
                    {
                        return Fail("--sep must not contain newline characters", useColor);
                    }
                    o = o with { Separator = sep };
                }
                o = o with { Capitalize = parsed.Has("--capitalize"), Number = parsed.Has("--number") };
                break;
            case SecretMode.Key:
                if (parsed.Has("--bytes")) { o = o with { Bytes = parsed.GetInt("--bytes") }; }
                if (parsed.Has("--encoding"))
                {
                    if (!TryParseEncoding(parsed.GetString("--encoding"), out KeyEncoding enc))
                    {
                        return Fail($"unknown --encoding value: {parsed.GetString("--encoding")}", useColor);
                    }
                    o = o with { Encoding = enc };
                }
                break;
        }

        return new Result(o, null, false, 0, useColor);
    }

    private static CommandLineParser BuildParser(SecretMode mode)
    {
        string version = ResolveVersion();
        return mode switch
        {
            SecretMode.Phrase => BuildPhraseParser(version),
            SecretMode.Key => BuildKeyParser(version),
            _ => BuildPasswordParser(version),
        };
    }

    private static CommandLineParser CommonShell(string toolName, string version, string description)
        => new CommandLineParser(toolName, version)
            .Description(description)
            .Maturity(ToolMaturity.Fresh)
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "pwgen", "openssl rand", "diceware/xkcdpass", "PowerShell Get-SecureRandom" },
                valueOnWindows: "Windows ships no secure generator out of the box; the common Get-Random idiom is non-cryptographic. mksecret is secure-by-default with no PowerShell-version dependency.",
                valueOnUnix: "One self-contained binary covering passwords, diceware passphrases (missing on every OS), and encoded keys — secure-by-default (no pwgen -s footgun, no Python runtime for diceware).")
            .ExitCodes(
                (0, "Success (a closed downstream pipe, e.g. | head -1, also exits 0 — not an error)"),
                (ExitCode.UsageError, "Usage error: unknown flag, bad --charset/--encoding value, non-positive or oversized --length/--bytes/--words/--count, unexpected positional (including an unrecognised subcommand)"),
                (ExitCode.NotExecutable, "Runtime error: OS CSPRNG failure or output write failure (disk full, device error)"))
            .StdinDescription("Not used.")
            .StdoutDescription("The generated secret(s), one per line; or a JSON envelope under --json.")
            .StderrDescription("Entropy note (≈ N bits) unless --quiet/--json; errors.")
            .IntOption("--count", "-n", "N", "Number of secrets to emit, one per line. Default 1 (max 100000).",
                v => v > 0 && v <= 100000 ? null : "must be between 1 and 100000")
            // --json is already registered by StandardFlags() above (description "JSON output"); do not
            // re-add it or it appears twice in --help/--describe. The tool reads it via parsed.Has("--json").
            .Flag("--quiet", "Suppress the stderr entropy note.");

    private static CommandLineParser BuildPasswordParser(string version)
        => CommonShell("mksecret", version,
                "Generate a random secret. Default mode: a random-character password. Subcommands: phrase, key.")
            .IntOption("--length", "-l", "N", "Password length in characters. Default 20 (max 4096).",
                v => v > 0 && v <= 4096 ? null : "must be between 1 and 4096")
            .Option("--charset", "-c", "NAME", "Character set: alphanumeric (default), full, alpha, digits, safe.")
            .Example("mksecret", "20-char alphanumeric password")
            .Example("mksecret --length 32 --charset full", "32 chars including symbols")
            .Example("mksecret --charset safe", "Avoids visually-ambiguous characters")
            .Example("mksecret --count 5", "Five passwords, one per line")
            .ComposesWith("clip", "mksecret | clip", "Copy a generated password to the clipboard without it touching the terminal")
            .Section("Subcommands",
                "mksecret password [--length N] [--charset NAME]   (default)\n" +
                "mksecret phrase   [--words N] [--sep S] [--capitalize] [--number]\n" +
                "mksecret key      [--bytes N] [--encoding hex|base64|base64url|base32]\n\n" +
                "Run 'mksecret SUBCOMMAND --help' for mode-specific flags.");

    private static CommandLineParser BuildPhraseParser(string version)
        => CommonShell("mksecret phrase", version,
                "Generate a diceware passphrase from the EFF long wordlist (7776 words, ~12.9 bits/word).")
            .IntOption("--words", "-w", "N", "Number of words. Default 6 (~77 bits, max 1024).",
                v => v > 0 && v <= 1024 ? null : "must be between 1 and 1024")
            .Option("--sep", "-s", "STR", "Separator between words. Default '-'. Must not contain newlines.")
            .Flag("--capitalize", "Capitalise the first letter of each word.")
            .Flag("--number", "Append a random digit to the passphrase.")
            .Example("mksecret phrase", "Six-word passphrase, hyphen-separated")
            .Example("mksecret phrase --words 8 --sep ' '", "Eight words, space-separated")
            .Example("mksecret phrase --capitalize --number", "Title-cased with a trailing digit")
            .ComposesWith("clip", "mksecret phrase | clip", "Copy a generated passphrase to the clipboard");

    private static CommandLineParser BuildKeyParser(string version)
        => CommonShell("mksecret key", version,
                "Generate an encoded high-entropy key (API key, OAuth secret, HMAC key) from random bytes.")
            .IntOption("--bytes", "-b", "N", "Number of random bytes. Default 32 (256-bit, max 65536).",
                v => v > 0 && v <= 65536 ? null : "must be between 1 and 65536")
            .Option("--encoding", "-e", "NAME", "Encoding: base64url (default, unpadded), base64, hex, base32.")
            .Example("mksecret key", "32 random bytes as unpadded base64url")
            .Example("mksecret key --bytes 64 --encoding hex", "64 bytes as hex")
            .Example("mksecret key --encoding base32", "Crockford base32 (ambiguity-free)")
            .Section("Storing a key for reuse",
                "An HMAC/signing key must be PERSISTED to stay verifiable. Generate then store, e.g.:\n" +
                "  mksecret key --bytes 32 > signing.key\n" +
                "  digest --hmac sha256 --key-file signing.key -s \"payload\"\n" +
                "Do NOT pipe a generated key straight into digest --key-stdin — the key vanishes and the MAC is unverifiable.");

    private static bool TryParseCharset(string s, out Charset cs)
    {
        switch (s)
        {
            case "alphanumeric": cs = Charset.Alphanumeric; return true;
            case "full": cs = Charset.Full; return true;
            case "alpha": cs = Charset.Alpha; return true;
            case "digits": cs = Charset.Digits; return true;
            case "safe": cs = Charset.Safe; return true;
            default: cs = Charset.Alphanumeric; return false;
        }
    }

    private static bool TryParseEncoding(string s, out KeyEncoding enc)
    {
        switch (s)
        {
            case "hex": enc = KeyEncoding.Hex; return true;
            case "base64": enc = KeyEncoding.Base64; return true;
            case "base64url": enc = KeyEncoding.Base64Url; return true;
            case "base32": enc = KeyEncoding.Base32; return true;
            default: enc = KeyEncoding.Base64Url; return false;
        }
    }

    // Read AssemblyInformationalVersion (injected via /p:Version by the release pipeline) and strip the
    // "+gitsha" SourceLink suffix. Falls back to AssemblyVersion for dev builds. Matches qr/digest/ids/notify.
    private static string ResolveVersion()
    {
        string? info = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            int plus = info.IndexOf('+');
            return plus >= 0 ? info.Substring(0, plus) : info;
        }
        return typeof(ArgParser).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static Result Fail(string msg, bool useColor) => new(null, msg, false, 0, useColor);
}
