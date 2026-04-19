#nullable enable
namespace Winix.Digest;

/// <summary>Supported hash algorithms.</summary>
public enum HashAlgorithm
{
    /// <summary>SHA-256 (default). Modern, widely supported.</summary>
    Sha256,

    /// <summary>SHA-384. SHA-2 family, 384-bit output.</summary>
    Sha384,

    /// <summary>SHA-512. SHA-2 family, 512-bit output.</summary>
    Sha512,

    /// <summary>SHA-1. Cryptographically broken for collision resistance; warning emitted on use.</summary>
    Sha1,

    /// <summary>MD5. Cryptographically broken; warning emitted on use.</summary>
    Md5,

    /// <summary>SHA3-256. Requires OS crypto backend with SHA-3 support (.NET 8+, newer OSes).</summary>
    Sha3_256,

    /// <summary>SHA3-512.</summary>
    Sha3_512,

    /// <summary>BLAKE2b-512. Provided by the SauceControl.Blake2Fast NuGet package.</summary>
    Blake2b,
}
