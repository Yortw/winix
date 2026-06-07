#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Protect;

/// <summary>Parses <c>protect</c> / <c>unprotect</c> command-line arguments into a <see cref="ProtectOptions"/> or an error.</summary>
public static class ArgParser
{
    /// <summary>
    /// Outcome of <see cref="Parse"/>. Either <see cref="Options"/> is populated (success), <see cref="Error"/>
    /// is populated (usage error), or <see cref="IsHandled"/> is true (ShellKit printed help/version/describe
    /// already and the caller should exit with <see cref="ExitCode"/>).
    /// </summary>
    public sealed record Result(
        ProtectOptions? Options,
        string? Error,
        bool IsHandled,
        int ExitCode,
        bool UseColor);

    /// <summary>Parses <paramref name="argv"/> for the given <paramref name="subCommand"/>. Delegates to <see cref="CommandLineParser"/> for flag recognition and rich --describe metadata, then applies protect-specific post-parse validation.</summary>
    public static Result Parse(IReadOnlyList<string> argv, SubCommand subCommand)
    {
        CommandLineParser parser = BuildParser(subCommand);
        string[] args = argv is string[] arr ? arr : ToArray(argv);
        ParseResult parsed = parser.Parse(args);

        bool useColor = parsed.ResolveColor(checkStdErr: false);

        if (parsed.IsHandled)
        {
            return new Result(null, null, true, parsed.ExitCode, useColor);
        }
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0], useColor);
        }

        if (parsed.Positionals.Length > 1)
        {
            return Fail($"unexpected positional argument: {parsed.Positionals[1]}", useColor);
        }
        string? inputPath = parsed.Positionals.Length == 1 ? parsed.Positionals[0] : null;

        string? outputPath = parsed.Has("--output") ? parsed.GetString("--output") : null;
        bool inPlace = parsed.Has("--in-place");
        // --rm / --remove-source are aliases; either flag being set means "delete source after success".
        bool removeSource = parsed.Has("--rm") || parsed.Has("--remove-source");
        bool noVerify = parsed.Has("--no-verify");
        bool force = parsed.Has("--force");

        Scope scope = Scope.User;
        if (parsed.Has("--scope"))
        {
            string scopeStr = parsed.GetString("--scope");
            Scope? parsedScope = scopeStr switch
            {
                "user" => Scope.User,
                "machine" => Scope.Machine,
                _ => null,
            };
            if (parsedScope is null)
            {
                return Fail($"unknown --scope value: {scopeStr}", useColor);
            }
            scope = parsedScope.Value;
        }

        if (inPlace && outputPath is not null)
        {
            return Fail("--in-place and --output are mutually exclusive", useColor);
        }
        if (inputPath is not null && outputPath is not null)
        {
            // Path.GetFullPath normalises separators and relative segments; OrdinalIgnoreCase matches typical Windows
            // filesystem semantics. On case-sensitive *nix filesystems this is still the safer default — two paths
            // differing only in case almost always refer to the same file in practice.
            string inAbs = Path.GetFullPath(inputPath);
            string outAbs = Path.GetFullPath(outputPath);
            if (string.Equals(inAbs, outAbs, StringComparison.OrdinalIgnoreCase))
            {
                return Fail("input and output paths are the same. Use '-o different-path' or '--in-place'", useColor);
            }
        }

        ProtectOptions options = new(subCommand, inputPath, outputPath, inPlace, removeSource, scope, noVerify, force);
        return new Result(options, null, false, 0, useColor);
    }

    private static CommandLineParser BuildParser(SubCommand subCommand)
    {
        bool isProtect = subCommand == SubCommand.Protect;
        string toolName = isProtect ? "protect" : "unprotect";
        string description = isProtect
            ? "Cross-platform encrypt-at-rest CLI wrapping native OS key-storage primitives (DPAPI / Keychain / libsecret). Files are NOT portable between users, machines, or scopes — use age or gpg for portable encryption."
            : "Cross-platform decrypt-at-rest CLI for files produced by protect. Auto-selects backend (DPAPI / Keychain / libsecret) from the WPRT header marker.";
        string valueOnWindows = isProtect
            ? "Zero-dependency DPAPI wrapper with chunked streaming, atomic --in-place mode, and round-trip verification — no PowerShell scripting or certificate provisioning needed."
            : "Header-based backend auto-dispatch: decrypts any protect-produced file without the caller remembering which scope was used.";
        string valueOnUnix = isProtect
            ? "Uniform --scope flag and chunked WPRT format across macOS Keychain and Linux libsecret. Single AOT binary — replaces ad-hoc openssl + security/secret-tool glue."
            : "Streaming decryption from stdin, implicit .prot-suffix stripping on output, and automatic Keychain/libsecret backend selection from the header marker.";
        string stdinDesc = isProtect
            ? "Plaintext to encrypt when no FILE is given."
            : "Ciphertext (.prot stream) to decrypt when no FILE is given.";
        string stdoutDesc = isProtect
            ? "Ciphertext when neither FILE nor -o is given (streaming mode)."
            : "Plaintext when neither FILE nor -o is given (streaming mode).";

        CommandLineParser p = new CommandLineParser(toolName, ResolveVersion())
            .Description(description)
            .Maturity(ToolMaturity.Core)
            .PreferDefaultWhen(
                "portable encryption (cross-machine/user) — use age or gpg",
                "encrypting structured data with external KMS — use sops",
                "whole-disk or filesystem encryption — use FileVault, BitLocker, or LUKS")
            .StandardFlags()
            .Platform("cross-platform",
                replaces: new[] { "dpapi.ps1", "security add-generic-password + openssl", "secret-tool + openssl", "gpg --symmetric (for single-user at-rest)" },
                valueOnWindows: valueOnWindows,
                valueOnUnix: valueOnUnix)
            .ExitCodes(
                (0, "Success"),
                (ExitCode.UsageError, "Usage error: bad flags, path collision, --in-place without a FILE, unsupported scope on Linux"),
                (ExitCode.NotExecutable, "Runtime error: file not found, encryption/decryption failure, key store unavailable, round-trip verify failed, header magic mismatch, user/machine/scope mismatch on decrypt"))
            .StdinDescription(stdinDesc)
            .StdoutDescription(stdoutDesc)
            .StderrDescription("Errors, retention warnings (plaintext not removed when --rm omitted), and diagnostic messages.")
            .Positional("FILE")
            .Option("--output", "-o", "PATH",
                isProtect
                    ? "Explicit output path. Default: FILE.prot for file input, stdout for stdin."
                    : "Explicit output path. Default: FILE with .prot stripped for file input, stdout for stdin.")
            .Flag("--in-place", "Replace FILE atomically via temp + rename. Mutually exclusive with --output.")
            .Flag("--rm", "Delete source FILE after successful round-trip verification.")
            .Flag("--remove-source", "Alias for --rm.")
            .Flag("--keep", "-k", "Retain source FILE (explicit default; accepted for symmetry with --rm).")
            .Option("--scope", null, "user|machine",
                "Key-derivation scope. 'user' (default) — key bound to current OS user, decryptable only by that user on that machine. 'machine' — key bound to machine credential, decryptable by any user on that machine (Windows needs DPAPI LocalMachine access; macOS needs sudo for System Keychain; Linux: unsupported — tool exits with usage error).")
            .Flag("--no-verify", "Skip the post-encrypt round-trip integrity check (encrypt path only). Faster, less safe.")
            .Flag("--force", "-f", "Overwrite an existing destination file. Without this flag, the tool refuses to clobber existing data. Symlink-safe: the destination is unlinked before exclusive create, so an attacker-planted symlink cannot redirect the write.");

        if (isProtect)
        {
            p.Example("protect secrets.json", "Encrypt to secrets.json.prot (plaintext retained; stderr warning)")
             .Example("protect secrets.json --rm", "Encrypt, verify, then delete the plaintext source")
             .Example("protect config.xml --in-place", "Atomic replace: writes to temp, renames over original")
             .Example("protect api.key -o api.enc --no-verify", "Explicit output path; skip the round-trip check")
             .Example("protect --scope machine service.conf", "Machine-scoped (Windows LocalMachine or macOS System Keychain; Linux not supported)")
             .Example("cat api.key | protect > api.key.prot", "Stream-encrypt via pipe")
             .ComposesWith("digest", "digest config.json; protect config.json --rm", "Hash a file for audit, then encrypt and remove plaintext")
             .ComposesWith("clip", "clip --paste | protect -o clip.prot", "Encrypt clipboard contents to a file")
             .Section("WPRT Format",
                "Header (22 bytes): magic 'WPRT' + version 0x01 + backend-marker byte (0x01 DPAPI-user, 0x02 DPAPI-machine, 0x10 Keychain-user, 0x11 Keychain-machine, 0x20 libsecret-user) + 16-byte random FileId.\n" +
                "Body: 64 KB chunks. AEAD path (Keychain/libsecret): AES-256-GCM with AAD = header || chunkIndex || isFinal — every chunk is bound to this specific file at this specific position.\n" +
                "DPAPI path (Windows): the same FileId+chunkIndex binding lives inside the protected blob, so chunk reorder and cross-file substitution are detected even though DPAPI itself has no AAD slot.\n" +
                "Final chunk carries a truncation-detection flag.");
        }
        else
        {
            p.Example("unprotect secrets.json.prot", "Decrypt to secrets.json (implicit .prot-suffix strip)")
             .Example("unprotect secrets.json.prot -o /tmp/out.json", "Decrypt to an explicit output path")
             .Example("unprotect config.prot --in-place", "Atomic decrypt-over-ciphertext via temp + rename")
             .Example("unprotect config.prot --rm", "Decrypt then delete the ciphertext source")
             .Example("cat api.key.prot | unprotect", "Stream-decrypt via pipe")
             .Example("unprotect < api.key.prot | digest --hmac sha256 --key-stdin -s payload", "Decrypt an HMAC key in memory (never hits disk) and hash a payload")
             .ComposesWith("digest", "unprotect key.prot | digest --hmac sha256 --key-stdin -s payload", "Use an encrypted key without writing plaintext to disk")
             .ComposesWith("clip", "unprotect token.prot | clip", "Decrypt and copy to the clipboard (pair with --rm for single-use tokens)");
        }

        return p;
    }

    // Read AssemblyInformationalVersion (injected via /p:Version by the release pipeline) and strip the
    // "+gitsha" SourceLink suffix. Falls back to AssemblyVersion for dev builds. Matches digest/ids/notify.
    private static string ResolveVersion()
    {
        string? informational = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            int plus = informational.IndexOf('+');
            return plus >= 0 ? informational.Substring(0, plus) : informational;
        }
        return typeof(ArgParser).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string[] ToArray(IReadOnlyList<string> list)
    {
        string[] a = new string[list.Count];
        for (int i = 0; i < list.Count; i++) { a[i] = list[i]; }
        return a;
    }

    private static Result Fail(string msg, bool useColor)
        => new(null, msg, false, 0, useColor);
}
