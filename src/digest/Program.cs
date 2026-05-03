using System;
using System.IO;
using Winix.Digest;
using Yort.ShellKit;

namespace Digest;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        var parse = ArgParser.Parse(args);

        if (parse.IsHandled)
        {
            return parse.HandledExitCode;
        }
        if (!parse.Success)
        {
            Console.Error.WriteLine($"digest: {parse.Error}");
            Console.Error.WriteLine("Run 'digest --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = parse.Options!;

        if (opts.Algorithm == HashAlgorithm.Md5)
        {
            Console.Error.WriteLine("digest: warning: MD5 is cryptographically broken; do not use for security-sensitive purposes.");
        }
        else if (opts.Algorithm == HashAlgorithm.Sha1)
        {
            Console.Error.WriteLine("digest: warning: SHA-1 is broken for collision resistance; HMAC-SHA-1 is still acceptable for signing but prefer HMAC-SHA-256 for new systems.");
        }

        try
        {
            // Resolve HMAC key if needed. Reading both the key and the payload from stdin
            // cannot be satisfied — the single stdin stream can only be read once.
            byte[]? key = null;
            if (opts.IsHmac)
            {
                if (parse.KeySourceForHmac is KeySource.StdinSource && opts.Source is StdinInput)
                {
                    Console.Error.WriteLine("digest: --key-stdin cannot be combined with stdin payload");
                    return ExitCode.UsageError;
                }
                key = KeyResolver.Resolve(
                    source: parse.KeySourceForHmac!,
                    stdin: Console.In,
                    stripTrailingNewline: parse.StripKeyNewline,
                    stderr: Console.Error,
                    out string? keyError);
                if (keyError is not null)
                {
                    Console.Error.WriteLine($"digest: {keyError}");
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
                Console.Error.WriteLine("digest: SHA-3 is not available on this platform (OS crypto backend missing)");
                return ExitCode.NotExecutable;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // e.g. BLAKE2b key over 64 bytes — surface the library's helpful message,
                // trimmed to first line to avoid a noisy multi-line dump.
                int nl = ex.Message.IndexOf('\n');
                string msg = nl >= 0 ? ex.Message.Substring(0, nl) : ex.Message;
                Console.Error.WriteLine($"digest: {msg}");
                return ExitCode.UsageError;
            }

            var results = HashRunner.Run(
                source: opts.Source,
                hasher: hasher,
                stdin: Console.In,
                out string? runError);
            if (runError is not null)
            {
                Console.Error.WriteLine($"digest: {runError}");
                return ExitCode.NotExecutable;
            }

            if (opts.VerifyExpected is not null)
            {
                bool match = Verifier.Verify(results[0].Hash, opts.VerifyExpected, opts.Format);
                if (!match)
                {
                    Console.Error.WriteLine("digest: verification failed");
                    return 1;
                }
                return ExitCode.Success;
            }

            if (opts.Json)
            {
                if (results.Count == 1)
                {
                    Console.Out.WriteLine(Formatting.JsonElement(results[0].Hash, results[0].Path, opts));
                }
                else
                {
                    Console.Out.Write('[');
                    for (int i = 0; i < results.Count; i++)
                    {
                        if (i > 0) Console.Out.Write(',');
                        Console.Out.Write(Formatting.JsonElement(results[i].Hash, results[i].Path, opts));
                    }
                    Console.Out.WriteLine(']');
                }
            }
            else
            {
                if (results.Count == 1 && results[0].Path is null)
                {
                    Console.Out.WriteLine(Formatting.PlainSingle(results[0].Hash, opts.Format, opts.Uppercase));
                }
                else
                {
                    foreach (var result in results)
                    {
                        Console.Out.WriteLine(Formatting.PlainMultiLine(result.Hash, result.Path!, opts.Format, opts.Uppercase));
                    }
                }
            }

            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            // Round-1 review I9 + SFH-12: the prior outer `catch (IOException) → exit 0`
            // arm was too broad. It was added for broken-pipe (`digest *.log | head`)
            // but also swallowed real read failures from HashRunner — file disappears
            // mid-multi-file scan, permission denied on a previously-readable file,
            // network share dropped — converting them to silent success. Per memory
            // `project_broken_pipe_design_question.md`, .NET 10 already absorbs broken-pipe
            // at the Console.Out layer on both Windows and Linux, so the catch was
            // also unnecessary for its stated purpose. HashRunner now wraps File.OpenRead
            // in scoped catches that produce typed errors via `runError`, so any
            // IOException reaching here is unexpected and gets the generic-error path.
            Console.Error.WriteLine($"digest: error: {ex.Message}");
            return 1;
        }
    }
}
