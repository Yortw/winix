#nullable enable
using System;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Digest;

/// <summary>
/// Parses <c>digest</c> command-line arguments into <see cref="DigestOptions"/>,
/// enforcing the Q-matrix of flag compatibility rules. Pure — produces no I/O
/// itself, though ShellKit's <see cref="CommandLineParser"/> prints help/version/describe
/// to stdout automatically when those flags are present (signalled via
/// <see cref="Result.IsHandled"/>).
/// </summary>
public static class ArgParser
{
    /// <summary>
    /// Parse result: success (<see cref="Options"/> populated + optional <see cref="KeySourceForHmac"/>),
    /// usage error (<see cref="Error"/> populated), or ShellKit-handled (<see cref="IsHandled"/>).
    /// </summary>
    /// <remarks>
    /// The HMAC key source is carried here rather than on <see cref="DigestOptions"/> so the
    /// resolved key bytes live only as a local in Program.cs — never inside a long-lived record.
    /// </remarks>
    public sealed record Result(
        DigestOptions? Options,
        string? Error,
        bool IsHandled,
        int HandledExitCode,
        bool UseColor,
        KeySource? KeySourceForHmac,
        bool StripKeyNewline)
    {
        /// <summary>True if the parse produced valid options.</summary>
        public bool Success => Options is not null;
    }

    /// <summary>Parses the argument vector.</summary>
    public static Result Parse(string[] argv)
    {
        var parser = BuildParser();
        // Strip bare "-" from argv (Unix "read from stdin" convention). ShellKit treats
        // anything starting with '-' as a flag, so a lone "-" would be rejected as
        // "unknown option". Remember it locally so input-source resolution still honours it.
        (string[] argvForShellKit, bool sawStdinDash) = ExtractBareDash(argv);
        var parsed = parser.Parse(argvForShellKit);

        bool useColor = parsed.ResolveColor(checkStdErr: false);

        Result Fail(string error) => new(null, error, false, 0, useColor, null, false);

        if (parsed.IsHandled)
        {
            return new Result(null, null, true, parsed.ExitCode, useColor, null, false);
        }
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0]);
        }

        // Algorithm resolution: at most one of the individual flags OR --algo.
        bool[] algoFlagsPresent =
        {
            parsed.Has("--sha256"), parsed.Has("--sha384"), parsed.Has("--sha512"),
            parsed.Has("--sha1"), parsed.Has("--md5"),
            parsed.Has("--sha3-256"), parsed.Has("--sha3-512"),
            parsed.Has("--blake2b"),
        };
        int algoCount = 0;
        foreach (bool f in algoFlagsPresent)
        {
            if (f) algoCount++;
        }
        bool algoFromFlag = algoCount == 1;
        bool algoExplicit = algoFromFlag || parsed.Has("--algo");

        if (algoCount > 1)
        {
            return Fail("multiple algorithms specified — choose one");
        }
        if (algoFromFlag && parsed.Has("--algo"))
        {
            return Fail("multiple algorithms specified — choose one");
        }

        HashAlgorithm algorithm = HashAlgorithm.Sha256;
        if (parsed.Has("--sha256")) algorithm = HashAlgorithm.Sha256;
        else if (parsed.Has("--sha384")) algorithm = HashAlgorithm.Sha384;
        else if (parsed.Has("--sha512")) algorithm = HashAlgorithm.Sha512;
        else if (parsed.Has("--sha1")) algorithm = HashAlgorithm.Sha1;
        else if (parsed.Has("--md5")) algorithm = HashAlgorithm.Md5;
        else if (parsed.Has("--sha3-256")) algorithm = HashAlgorithm.Sha3_256;
        else if (parsed.Has("--sha3-512")) algorithm = HashAlgorithm.Sha3_512;
        else if (parsed.Has("--blake2b")) algorithm = HashAlgorithm.Blake2b;
        else if (parsed.Has("--algo"))
        {
            string algoStr = parsed.GetString("--algo");
            if (!TryParseAlgo(algoStr, out algorithm))
            {
                return Fail($"unknown algorithm '{algoStr}' (expected: sha256, sha384, sha512, sha1, md5, sha3-256, sha3-512, blake2b)");
            }
        }

        // HMAC resolution.
        bool isHmac = parsed.Has("--hmac");
        if (isHmac)
        {
            if (algoExplicit)
            {
                return Fail("--hmac carries its own algorithm; do not combine with --sha256 / --algo / etc.");
            }
            string hmacStr = parsed.GetString("--hmac");
            if (!TryParseAlgo(hmacStr, out HashAlgorithm hmacAlgorithm))
            {
                return Fail($"unknown algorithm '{hmacStr}' (expected: sha256, sha384, sha512, sha1, md5, sha3-256, sha3-512, blake2b)");
            }
            algorithm = hmacAlgorithm;
        }

        // HMAC key source resolution.
        KeySource? keySource = null;
        if (isHmac)
        {
            int keyCount = 0;
            if (parsed.Has("--key-env")) keyCount++;
            if (parsed.Has("--key-file")) keyCount++;
            if (parsed.Has("--key-stdin")) keyCount++;
            if (parsed.Has("--key")) keyCount++;

            if (keyCount == 0)
            {
                return Fail("--hmac requires one of --key-env, --key-file, --key-stdin, --key");
            }
            if (keyCount > 1)
            {
                return Fail("exactly one of --key-env, --key-file, --key-stdin, --key must be specified");
            }
            if (parsed.Has("--key-env")) keySource = KeySource.EnvVariable(parsed.GetString("--key-env"));
            else if (parsed.Has("--key-file")) keySource = KeySource.File(parsed.GetString("--key-file"));
            else if (parsed.Has("--key-stdin")) keySource = KeySource.Stdin();
            else keySource = KeySource.Literal(parsed.GetString("--key"));
        }
        bool stripKeyNewline = !parsed.Has("--key-raw");

        // Output format resolution.
        int formatCount = 0;
        if (parsed.Has("--hex")) formatCount++;
        if (parsed.Has("--base64")) formatCount++;
        if (parsed.Has("--base64-url")) formatCount++;
        if (parsed.Has("--base32")) formatCount++;
        if (formatCount > 1)
        {
            return Fail("multiple output formats specified — choose one");
        }
        OutputFormat format = OutputFormat.Hex;
        if (parsed.Has("--base64")) format = OutputFormat.Base64;
        else if (parsed.Has("--base64-url")) format = OutputFormat.Base64Url;
        else if (parsed.Has("--base32")) format = OutputFormat.Base32;

        bool uppercase = parsed.Has("--uppercase");

        // Input source resolution — positionals are *always* files (never auto-detected
        // as literal strings) so a typo doesn't silently hash the argument text.
        bool hasString = parsed.Has("--string");
        int stringCount = CountOccurrences(argv, "--string", "-s");
        string[] positionals = parsed.Positionals;

        if (stringCount > 1)
        {
            return Fail("--string can only be specified once");
        }

        InputSource source;
        if (hasString)
        {
            if (positionals.Length > 0 || sawStdinDash)
            {
                return Fail("--string cannot be combined with file arguments");
            }
            source = new StringInput(parsed.GetString("--string"));
        }
        else if (sawStdinDash && positionals.Length == 0)
        {
            source = new StdinInput();
        }
        else if (positionals.Length == 0)
        {
            source = new StdinInput();
        }
        else if (positionals.Length == 1)
        {
            if (!System.IO.File.Exists(positionals[0]))
            {
                return Fail($"'{positionals[0]}' not found — use --string to hash as a literal, or pass a valid file path");
            }
            source = new SingleFileInput(positionals[0]);
        }
        else
        {
            source = new MultiFileInput(positionals);
        }

        // Verify mode.
        string? verify = parsed.Has("--verify") ? parsed.GetString("--verify") : null;
        if (verify is not null && source is MultiFileInput)
        {
            return Fail("--verify is not supported with multiple files");
        }

        bool json = parsed.Has("--json");

        var options = new DigestOptions(
            Algorithm: algorithm,
            IsHmac: isHmac,
            Format: format,
            Uppercase: uppercase,
            Source: source,
            VerifyExpected: verify,
            Json: json);

        return new Result(options, null, false, 0, useColor, keySource, stripKeyNewline);
    }

    // Strip bare "-" arguments (Unix stdin sentinel) from argv before handing to ShellKit,
    // which would reject them as unknown flags. Anything after "--" is left untouched so
    // filenames that legitimately are "-" can be passed positionally via `digest -- -`.
    private static (string[] argv, bool sawStdinDash) ExtractBareDash(string[] argv)
    {
        bool seen = false;
        var filtered = new System.Collections.Generic.List<string>(argv.Length);
        bool afterDashDash = false;
        foreach (string a in argv)
        {
            if (!afterDashDash && a == "--")
            {
                afterDashDash = true;
                filtered.Add(a);
                continue;
            }
            if (!afterDashDash && a == "-")
            {
                seen = true;
                continue;
            }
            filtered.Add(a);
        }
        return (filtered.ToArray(), seen);
    }

    // ShellKit's ParseResult doesn't expose "how many times was this option given" —
    // we scan the original argv to enforce --string-specified-once. Counts any of
    // the long name or short alias, in their standalone forms. The equivalents
    // "--string=X" / "-s=X" are also counted.
    private static int CountOccurrences(string[] argv, string longName, string shortName)
    {
        int count = 0;
        string longEq = longName + "=";
        string shortEq = shortName + "=";
        foreach (string a in argv)
        {
            if (a == longName || a == shortName) count++;
            else if (a.StartsWith(longEq, StringComparison.Ordinal)) count++;
            else if (a.StartsWith(shortEq, StringComparison.Ordinal)) count++;
        }
        return count;
    }

    private static bool TryParseAlgo(string value, out HashAlgorithm algo)
    {
        switch (value)
        {
            case "sha256":   algo = HashAlgorithm.Sha256;   return true;
            case "sha384":   algo = HashAlgorithm.Sha384;   return true;
            case "sha512":   algo = HashAlgorithm.Sha512;   return true;
            case "sha1":     algo = HashAlgorithm.Sha1;     return true;
            case "md5":      algo = HashAlgorithm.Md5;      return true;
            case "sha3-256": algo = HashAlgorithm.Sha3_256; return true;
            case "sha3-512": algo = HashAlgorithm.Sha3_512; return true;
            case "blake2b":  algo = HashAlgorithm.Blake2b;  return true;
            default:         algo = default;                return false;
        }
    }

    private static CommandLineParser BuildParser()
    {
        return new CommandLineParser("digest", ResolveVersion())
            .Description("Cross-platform cryptographic hashing and HMAC — SHA-2/SHA-3/BLAKE2b, safe HMAC key handling.")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "sha256sum", "md5sum", "openssl dgst" },
                valueOnWindows: "Native gap-fill — Windows has no first-class HMAC CLI; certutil and Get-FileHash don't cover HMAC; openssl requires a separate install.",
                valueOnUnix: "Consistent flag surface across sha256sum / md5sum variants, plus built-in HMAC, base64/base32 output, and --describe metadata.")
            .ExitCodes(
                (0, "Success"),
                (ExitCode.UsageError, "Usage error: bad flags, unknown value, or flag conflict"),
                (1, "Verification failed (digest --verify mismatch)"),
                (ExitCode.NotExecutable, "Runtime error (file read failure, SHA-3 unavailable)"))
            .StdinDescription("Payload to hash (default when no positional files), or key with --key-stdin")
            .StdoutDescription("Hash (plain single line, sha256sum-compatible multi-file lines, or JSON)")
            .StderrDescription("Warnings (legacy algorithms, insecure --key literal, group-readable key files) and errors")
            .Example("digest file.iso", "SHA-256 of a file")
            .Example("digest *.txt", "Hash every matching file, sha256sum-compatible output")
            .Example("digest --sha512 -s \"hello\"", "SHA-512 of a literal string")
            .Example("digest --hmac sha256 --key-env API_SECRET -s \"payload\"", "HMAC-SHA-256 with key from env var")
            .Example("digest --hmac sha256 --key-file ~/.secret file.bin", "HMAC of a file with key from file")
            .Example("age --decrypt key.age | digest --hmac sha256 --key-stdin -s \"msg\"", "HMAC with key piped from age")
            .Example("digest --verify \"abc123...\" file.bin", "Exit 0 if hash matches")
            .ComposesWith("clip", "digest file.bin | clip", "Copy a hash to the clipboard")
            .ComposesWith("age", "age --decrypt key.age | digest --hmac sha256 --key-stdin ...", "Read HMAC key from an age-encrypted file")
            .ComposesWith("pass", "pass show mykey | digest --hmac sha256 --key-stdin ...", "Read HMAC key from passwordstore")
            .JsonField("algorithm", "string", "Hash algorithm (sha256, sha3-256, hmac-sha256, etc.)")
            .JsonField("format", "string", "Output encoding (hex, base64, base64url, base32)")
            .JsonField("hash", "string", "The encoded hash value")
            .JsonField("source", "string", "Input source (string, stdin, file)")
            .JsonField("path", "string", "(file source only) file path")
            .Flag("--sha256", "SHA-256 (default)")
            .Flag("--sha384", "SHA-384")
            .Flag("--sha512", "SHA-512")
            .Flag("--sha1", "SHA-1 (legacy; emits warning)")
            .Flag("--md5", "MD5 (legacy; emits warning)")
            .Flag("--sha3-256", "SHA3-256")
            .Flag("--sha3-512", "SHA3-512")
            .Flag("--blake2b", "BLAKE2b-512")
            .Option("--algo", "-a", "ALGO", "Alternative to individual algorithm flags: sha256, sha384, sha512, sha1, md5, sha3-256, sha3-512, blake2b")
            .Option("--hmac", null, "ALGO", "HMAC mode using the given hash algorithm (requires a key source)")
            .Option("--key-env", null, "VAR", "Read HMAC key from environment variable")
            .Option("--key-file", null, "PATH", "Read HMAC key from file (Unix permission warning if group/other readable)")
            .Flag("--key-stdin", "Read HMAC key from stdin")
            .Option("--key", null, "KEY", "HMAC key as literal argument (emits warning about process visibility)")
            .Flag("--key-raw", "Preserve bytes on --key-file / --key-stdin (skip trailing-newline strip)")
            .Flag("--hex", "Hex output (default, lowercase)")
            .Flag("--base64", "Base64 output (standard alphabet)")
            .Flag("--base64-url", "Base64 URL-safe variant")
            .Flag("--base32", "Crockford base32 output")
            .Flag("--uppercase", "-u", "Uppercase hex output")
            .Option("--string", "-s", "VALUE", "Hash the literal string VALUE (UTF-8 bytes). Exclusive with positional file args.")
            .Option("--verify", null, "EXPECTED", "Compare output (constant-time); exit 0 if match, 1 if mismatch");
    }

    // Matches the ids tool's approach: read AssemblyInformationalVersion (injected via
    // /p:Version by the release pipeline) and strip the "+gitsha" SourceLink suffix.
    private static string ResolveVersion()
    {
        string raw = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
