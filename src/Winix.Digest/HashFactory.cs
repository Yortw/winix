#nullable enable
using System;
using System.IO;
using Blake2Fast;
using CryptoAlgo = System.Security.Cryptography;

namespace Winix.Digest;

/// <summary>Creates <see cref="IHasher"/> instances for the supported hash algorithms.</summary>
public static class HashFactory
{
    /// <summary>
    /// Creates a hasher for the given algorithm.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown for <see cref="HashAlgorithm.Sha3_256"/> or <see cref="HashAlgorithm.Sha3_512"/>
    /// on platforms where the OS crypto backend does not support SHA-3 (requires .NET 8+ and a
    /// compatible OS; available on Windows 11 22H2+ and recent Linux kernels).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for an unrecognised <paramref name="algorithm"/> value.</exception>
    public static IHasher Create(HashAlgorithm algorithm) => algorithm switch
    {
        HashAlgorithm.Sha256   => new BclHasher(CryptoAlgo.SHA256.HashData,   CryptoAlgo.SHA256.HashData),
        HashAlgorithm.Sha384   => new BclHasher(CryptoAlgo.SHA384.HashData,   CryptoAlgo.SHA384.HashData),
        HashAlgorithm.Sha512   => new BclHasher(CryptoAlgo.SHA512.HashData,   CryptoAlgo.SHA512.HashData),
        HashAlgorithm.Sha1     => new BclHasher(CryptoAlgo.SHA1.HashData,     CryptoAlgo.SHA1.HashData),
        HashAlgorithm.Md5      => new BclHasher(CryptoAlgo.MD5.HashData,      CryptoAlgo.MD5.HashData),
        HashAlgorithm.Sha3_256 => CryptoAlgo.SHA3_256.IsSupported
            ? new BclHasher(CryptoAlgo.SHA3_256.HashData, CryptoAlgo.SHA3_256.HashData)
            : throw new PlatformNotSupportedException("SHA-3 is not available on this platform"),
        HashAlgorithm.Sha3_512 => CryptoAlgo.SHA3_512.IsSupported
            ? new BclHasher(CryptoAlgo.SHA3_512.HashData, CryptoAlgo.SHA3_512.HashData)
            : throw new PlatformNotSupportedException("SHA-3 is not available on this platform"),
        HashAlgorithm.Blake2b  => new Blake2bHasher(),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
    };

    private sealed class BclHasher : IHasher
    {
        private readonly Func<byte[], byte[]> _bytesFn;
        private readonly Func<Stream, byte[]> _streamFn;

        public BclHasher(Func<byte[], byte[]> bytesFn, Func<Stream, byte[]> streamFn)
        {
            _bytesFn = bytesFn;
            _streamFn = streamFn;
        }

        public byte[] Hash(ReadOnlySpan<byte> input) => _bytesFn(input.ToArray());

        public byte[] Hash(Stream input) => _streamFn(input);
    }

    private sealed class Blake2bHasher : IHasher
    {
        // SauceControl.Blake2Fast 2.0.0 exposes its types under the Blake2Fast namespace
        // (not SauceControl.Blake2Fast). Blake2b is a static class; the incremental hasher
        // is Blake2Fast.Implementation.Blake2bHashState (a struct).
        public byte[] Hash(ReadOnlySpan<byte> input) => Blake2b.ComputeHash(input);

        public byte[] Hash(Stream input)
        {
            var hasher = Blake2b.CreateIncrementalHasher();
            Span<byte> buffer = stackalloc byte[8192];
            int n;
            while ((n = input.Read(buffer)) > 0)
            {
                // Update accepts ReadOnlySpan<byte> directly.
                hasher.Update(buffer[..n]);
            }
            return hasher.Finish();
        }
    }
}
