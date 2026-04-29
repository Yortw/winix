#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using Yort.ShellKit;

namespace Winix.Protect;

public static class Cli
{
    private const int RuntimeErrorExit = 126;

    public static int Run(string[] args, string invocationName)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        SubCommand subCommand = invocationName == "unprotect" ? SubCommand.Unprotect : SubCommand.Protect;
        ArgParser.Result parsed = ArgParser.Parse(args, subCommand);

        // ShellKit auto-writes --help / --version / --describe output during Parse and sets IsHandled=true.
        if (parsed.IsHandled)
        {
            return parsed.ExitCode;
        }

        if (parsed.Error is not null)
        {
            Console.Error.WriteLine(Formatting.UsageError(invocationName, parsed.Error));
            return ExitCode.UsageError;
        }

        ProtectOptions opts = parsed.Options!;

        try
        {
            if (subCommand == SubCommand.Protect)
            {
                return RunProtect(opts, invocationName);
            }
            return RunUnprotect(opts, invocationName);
        }
        catch (PlatformNotSupportedException ex)
        {
            Console.Error.WriteLine(Formatting.UsageError(invocationName, ex.Message));
            return ExitCode.UsageError;
        }
        catch (CryptographicException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName,
                $"decryption failed — this file was encrypted by a different user or on a different machine ({ex.GetType().Name})."));
            return RuntimeErrorExit;
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, ex.Message));
            return RuntimeErrorExit;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, ex.Message));
            return RuntimeErrorExit;
        }
        catch (FileNotFoundException ex)
        {
            // .NET's FileNotFoundException.Message format is "{SR resource key}, {path}" (e.g.
            // "IO_FileNotFound_FileName, d:\foo.txt"). The resource key is leaked when SR
            // localisation isn't resolved — observed on .NET 10 debug builds and likely under
            // AOT. Emit our own message with just the path so users see something sensible.
            string path = ex.FileName ?? opts.InputPath ?? "(unknown)";
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, $"could not find file '{path}'"));
            return RuntimeErrorExit;
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, ex.Message));
            return RuntimeErrorExit;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, $"access denied: {ex.Message}"));
            return RuntimeErrorExit;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, ex.Message));
            return RuntimeErrorExit;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(invocationName, $"error: {ex.Message}"));
            return 1;
        }
    }

    private static int RunProtect(ProtectOptions opts, string invocationName)
    {
        IProtectBackend backend = BackendFactory.Create(opts.Scope);

        if (opts.InPlace)
        {
            if (opts.InputPath is null)
            {
                Console.Error.WriteLine(Formatting.UsageError(invocationName, "--in-place requires a file argument"));
                return ExitCode.UsageError;
            }
            InPlaceExecutor.ExecuteEncrypt(opts.InputPath, backend, verify: !opts.NoVerify);
            return ExitCode.Success;
        }

        Stream input = opts.InputPath is not null
            ? new FileStream(opts.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            : Console.OpenStandardInput();

        string? outputPath = opts.OutputPath ?? (opts.InputPath is not null ? opts.InputPath + ".prot" : null);

        using (input)
        {
            if (outputPath is not null)
            {
                byte[] sourceHash;
                using (FileStream dest = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    byte[] fileId = Header.NewFileId();
                    byte[] header = Header.SerializeForAad(backend.Marker, fileId);
                    using TeeStream tee = new(input, hasher);
                    ChunkWriter.Write(tee, dest, backend, header);
                    sourceHash = hasher.GetCurrentHash();
                }

                if (!opts.NoVerify)
                {
                    using FileStream encrypted = new(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    RoundTripVerifier.Verify(encrypted, backend, sourceHash);
                }
            }
            else
            {
                byte[] fileId = Header.NewFileId();
                byte[] header = Header.SerializeForAad(backend.Marker, fileId);
                using Stream stdout = Console.OpenStandardOutput();
                ChunkWriter.Write(input, stdout, backend, header);
            }
        }

        if (opts.RemoveSource && opts.InputPath is not null)
        {
            File.Delete(opts.InputPath);
        }
        else if (opts.InputPath is not null)
        {
            Console.Error.WriteLine($"{invocationName}: plaintext retained at {opts.InputPath}. Use --rm to remove after encryption.");
        }

        return ExitCode.Success;
    }

    private static int RunUnprotect(ProtectOptions opts, string invocationName)
    {
        if (opts.InPlace)
        {
            if (opts.InputPath is null)
            {
                Console.Error.WriteLine(Formatting.UsageError(invocationName, "--in-place requires a file argument"));
                return ExitCode.UsageError;
            }
            // Peek the header to pick a backend, then close and let InPlaceExecutor reopen.
            PlatformMarker marker;
            using (FileStream peek = new(opts.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                marker = Header.Read(peek).Marker;
            }
            IProtectBackend backend = BackendFactory.CreateForMarker(marker);
            InPlaceExecutor.ExecuteDecrypt(opts.InputPath, backend);
            return ExitCode.Success;
        }

        Stream input = opts.InputPath is not null
            ? new FileStream(opts.InputPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            : Console.OpenStandardInput();

        using (input)
        {
            Header.ReadResult hdr = Header.Read(input);
            IProtectBackend backend = BackendFactory.CreateForMarker(hdr.Marker);
            byte[] headerBytes = Header.SerializeForAad(hdr.Marker, hdr.FileId);

            string? outputPath = opts.OutputPath;
            if (outputPath is null && opts.InputPath is not null && opts.InputPath.EndsWith(".prot"))
            {
                outputPath = opts.InputPath.Substring(0, opts.InputPath.Length - ".prot".Length);
            }

            if (outputPath is not null)
            {
                using FileStream dest = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                ChunkReader.Read(input, dest, backend, headerBytes);
            }
            else
            {
                using Stream stdout = Console.OpenStandardOutput();
                ChunkReader.Read(input, stdout, backend, headerBytes);
            }
        }

        if (opts.RemoveSource && opts.InputPath is not null)
        {
            File.Delete(opts.InputPath);
        }

        return ExitCode.Success;
    }

    private sealed class TeeStream : Stream
    {
        private readonly Stream _underlying;
        private readonly IncrementalHash _hasher;
        public TeeStream(Stream underlying, IncrementalHash hasher) { _underlying = underlying; _hasher = hasher; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _underlying.Length;
        public override long Position { get => _underlying.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _underlying.Read(buffer, offset, count);
            if (n > 0) _hasher.AppendData(buffer, offset, n);
            return n;
        }
    }
}
