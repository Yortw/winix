#nullable enable
using System;
using System.IO;
using Blake2Fast;
using CryptoAlgo = System.Security.Cryptography;

namespace Winix.Digest;

/// <summary>
/// Creates HMAC-capable <see cref="IHasher"/> instances. The key is copied on
/// construction; callers may discard their reference afterwards.
/// </summary>
public static class HmacFactory
{
    /// <summary>
    /// Creates an HMAC hasher using the given hash algorithm and key.
    /// </summary>
    /// <param name="algorithm">The hash algorithm to use as the HMAC primitive.</param>
    /// <param name="key">The HMAC key. A defensive copy is taken; the caller's array is not retained.</param>
    /// <returns>An <see cref="IHasher"/> that computes HMAC over bytes or a stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown for <see cref="HashAlgorithm.Sha3_256"/> or <see cref="HashAlgorithm.Sha3_512"/> on
    /// platforms where the OS crypto backend does not support SHA-3.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for an unrecognised <paramref name="algorithm"/> value.</exception>
    public static IHasher Create(HashAlgorithm algorithm, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return algorithm switch
        {
            HashAlgorithm.Sha256   => new BclHmac(key, CryptoAlgo.HMACSHA256.HashData, CryptoAlgo.HMACSHA256.HashData),
            HashAlgorithm.Sha384   => new BclHmac(key, CryptoAlgo.HMACSHA384.HashData, CryptoAlgo.HMACSHA384.HashData),
            HashAlgorithm.Sha512   => new BclHmac(key, CryptoAlgo.HMACSHA512.HashData, CryptoAlgo.HMACSHA512.HashData),
            HashAlgorithm.Sha1     => new BclHmac(key, CryptoAlgo.HMACSHA1.HashData,   CryptoAlgo.HMACSHA1.HashData),
            HashAlgorithm.Md5      => new BclHmac(key, CryptoAlgo.HMACMD5.HashData,    CryptoAlgo.HMACMD5.HashData),
            HashAlgorithm.Sha3_256 => CryptoAlgo.HMACSHA3_256.IsSupported
                ? new BclHmac(key, CryptoAlgo.HMACSHA3_256.HashData, CryptoAlgo.HMACSHA3_256.HashData)
                : throw new PlatformNotSupportedException("HMAC-SHA-3 is not available on this platform"),
            HashAlgorithm.Sha3_512 => CryptoAlgo.HMACSHA3_512.IsSupported
                ? new BclHmac(key, CryptoAlgo.HMACSHA3_512.HashData, CryptoAlgo.HMACSHA3_512.HashData)
                : throw new PlatformNotSupportedException("HMAC-SHA-3 is not available on this platform"),
            HashAlgorithm.Blake2b  => new Blake2bKeyedHasher(key),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
        };
    }

    /// <summary>
    /// HMAC wrapper around BCL static HashData methods. Both the bytes overload and the stream
    /// overload use the same method-group delegate shapes exposed by the BCL HMAC static APIs.
    /// </summary>
    private sealed class BclHmac : IHasher
    {
        private readonly byte[] _key;
        // Func<byte[], byte[], byte[]> — BCL signature: HashData(byte[] key, byte[] source)
        private readonly Func<byte[], byte[], byte[]> _bytesFn;
        // Func<byte[], Stream, byte[]> — BCL signature: HashData(byte[] key, Stream source)
        private readonly Func<byte[], Stream, byte[]> _streamFn;

        public BclHmac(byte[] key, Func<byte[], byte[], byte[]> bytesFn, Func<byte[], Stream, byte[]> streamFn)
        {
            _key = (byte[])key.Clone();
            _bytesFn = bytesFn;
            _streamFn = streamFn;
        }

        // ReadOnlySpan<byte> can't be a generic type argument, so we materialise to array here.
        // The BCL HashData methods already avoid re-allocating internally for the round-trip.
        public byte[] Hash(ReadOnlySpan<byte> input) => _bytesFn(_key, input.ToArray());

        public byte[] Hash(Stream input) => _streamFn(_key, input);
    }

    /// <summary>
    /// BLAKE2b keyed hasher using Blake2Fast's native keyed mode.
    /// BLAKE2b keyed mode is the BLAKE2 equivalent of HMAC — it is defined in RFC 7693 §2.9
    /// and is more efficient than wrapping BLAKE2b in HMAC-BLAKE2b.
    /// </summary>
    private sealed class Blake2bKeyedHasher : IHasher
    {
        private readonly byte[] _key;
        public Blake2bKeyedHasher(byte[] key) => _key = (byte[])key.Clone();

        public byte[] Hash(ReadOnlySpan<byte> input) => Blake2b.ComputeHash(64, _key, input);

        public byte[] Hash(Stream input)
        {
            var hasher = Blake2b.CreateIncrementalHasher(64, _key);
            Span<byte> buffer = stackalloc byte[8192];
            int n;
            while ((n = input.Read(buffer)) > 0)
            {
                hasher.Update(buffer.Slice(0, n));
            }
            return hasher.Finish();
        }
    }
}
