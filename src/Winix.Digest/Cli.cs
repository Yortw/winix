#nullable enable
using System;
using System.IO;
using Yort.ShellKit;

namespace Winix.Digest;

/// <summary>
/// Library-level entry point for the digest CLI. Program.cs is a thin shim around
/// <see cref="Run"/> that wires up Console.* and forwards exit codes; all behaviour
/// lives here so it can be exercised by unit tests.
/// </summary>
/// <remarks>
/// Round-2 review test gap — extract a testable Run() seam so --verify exit codes,
/// JSON multi-file array glue, the --key-stdin + stdin-payload conflict, and the
/// generic-catch exit code (SFH-I2) can all be pinned without spawning a process.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the digest pipeline: parse args, resolve key, hash, format/verify, return exit code.
    /// </summary>
    /// <param name="args">Command-line arguments (without the executable name).</param>
    /// <param name="keyStdin">TextReader for <c>--key-stdin</c>; tests inject a fake.</param>
    /// <param name="payloadStdin">Raw byte <see cref="Stream"/> for stdin payload; tests inject a MemoryStream.</param>
    /// <param name="stdout">Output writer for hash / JSON output.</param>
    /// <param name="stderr">Error writer for warnings, errors, and verify-mismatch diagnostics.</param>
    /// <returns>Process exit code (0 success, 1 verify-mismatch, 125 usage error, 126 runtime error).</returns>
    public static int Run(
        string[] args,
        TextReader keyStdin,
        Stream payloadStdin,
        TextWriter stdout,
        TextWriter stderr)
    {
        var parse = ArgParser.Parse(args);

        if (parse.IsHandled)
        {
            return parse.HandledExitCode;
        }
        if (!parse.Success)
        {
            stderr.WriteLine($"digest: {parse.Error}");
            stderr.WriteLine("Run 'digest --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = parse.Options!;

        AlgorithmWarning.EmitIfLegacy(opts.Algorithm, stderr);

        try
        {
            byte[]? key = null;
            if (opts.IsHmac)
            {
                // Reading both the key and the payload from stdin can't be satisfied —
                // the single stdin stream can only be read once.
                if (parse.KeySourceForHmac is KeySource.StdinSource && opts.Source is StdinInput)
                {
                    stderr.WriteLine("digest: --key-stdin cannot be combined with stdin payload");
                    return ExitCode.UsageError;
                }
                key = KeyResolver.Resolve(
                    source: parse.KeySourceForHmac!,
                    stdin: keyStdin,
                    stripTrailingNewline: parse.StripKeyNewline,
                    stderr: stderr,
                    out string? keyError);
                if (keyError is not null)
                {
                    stderr.WriteLine($"digest: {keyError}");
                    return ExitCode.UsageError;
                }
            }

            IHasher hasher;
            try
            {
                hasher = opts.IsHmac
                    ? HmacFactory.Create(opts.Algorithm, key!)
                    : HashFactory.Create(opts.Algorithm);
            }
            catch (PlatformNotSupportedException)
            {
                stderr.WriteLine("digest: SHA-3 is not available on this platform (OS crypto backend missing)");
                return ExitCode.NotExecutable;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // e.g. BLAKE2b key over 64 bytes — surface the library's helpful message,
                // trimmed to first line to avoid a noisy multi-line dump.
                int nl = ex.Message.IndexOf('\n');
                string msg = nl >= 0 ? ex.Message.Substring(0, nl) : ex.Message;
                stderr.WriteLine($"digest: {msg}");
                return ExitCode.UsageError;
            }

            var results = HashRunner.Run(
                source: opts.Source,
                hasher: hasher,
                stdinPayload: payloadStdin,
                out string? runError);
            if (runError is not null)
            {
                stderr.WriteLine($"digest: {runError}");
                return ExitCode.NotExecutable;
            }

            if (opts.VerifyExpected is not null)
            {
                bool match = Verifier.Verify(results[0].Hash, opts.VerifyExpected, opts.Format);
                if (!match)
                {
                    stderr.WriteLine("digest: verification failed");
                    return 1;
                }
                return ExitCode.Success;
            }

            if (opts.Json)
            {
                if (results.Count == 1)
                {
                    stdout.WriteLine(Formatting.JsonElement(results[0].Hash, results[0].Path, opts));
                }
                else
                {
                    stdout.Write('[');
                    for (int i = 0; i < results.Count; i++)
                    {
                        if (i > 0) stdout.Write(',');
                        stdout.Write(Formatting.JsonElement(results[i].Hash, results[i].Path, opts));
                    }
                    stdout.WriteLine(']');
                }
            }
            else
            {
                if (results.Count == 1 && results[0].Path is null)
                {
                    stdout.WriteLine(Formatting.PlainSingle(results[0].Hash, opts.Format, opts.Uppercase));
                }
                else
                {
                    foreach (var result in results)
                    {
                        stdout.WriteLine(Formatting.PlainMultiLine(result.Hash, result.Path!, opts.Format, opts.Uppercase));
                    }
                }
            }

            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            // Round-2 review SFH-I2 — return ExitCode.NotExecutable, not 1. Exit 1 is the
            // documented `--verify` mismatch code (per ArgParser's ExitCodes registration);
            // a runtime crash that returns 1 would be indistinguishable from a successful
            // mismatch detection in a `digest --verify ... || alert` script. Anything
            // unexpected reaching here is a runtime fault, which POSIX convention maps to
            // 125 in this suite (Winix's NotExecutable / 'tool's own error' code).
            stderr.WriteLine($"digest: error: {ex.Message}");
            return ExitCode.NotExecutable;
        }
    }
}
