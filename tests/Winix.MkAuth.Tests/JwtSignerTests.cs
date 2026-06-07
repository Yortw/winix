using System.Security.Cryptography;
using System.Text;
using Winix.MkAuth;
using Xunit;

public class JwtSignerTests
{
    [Fact]
    public void Hs256_token_has_three_parts_and_verifies()
    {
        var req = new JwtRequest
        {
            Algorithm = "HS256",
            Key = Encoding.UTF8.GetBytes("supersecretkey"),
            Claims = new() { ["iss"] = "me", ["sub"] = "42" },
        };
        var jwt = JwtSigner.Sign(req).Token;

        string[] parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        // Independently verify the signature (do NOT reuse the impl's base64url helper here).
        string signingInput = parts[0] + "." + parts[1];
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes("supersecretkey"));
        byte[] expected = h.ComputeHash(Encoding.ASCII.GetBytes(signingInput));
        string expectedSig = Convert.ToBase64String(expected).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedSig, parts[2]);
    }

    [Fact]                                              // F2: NumericDate must be a JSON number
    public void Numeric_exp_serializes_as_json_number()
    {
        var req = new JwtRequest
        {
            Algorithm = "HS256",
            Key = Encoding.UTF8.GetBytes("k"),
            Claims = new() { ["exp"] = 1700000000L, ["sub"] = "x" },
        };
        string payload = Encoding.UTF8.GetString(Base64UrlDecode(JwtSigner.Sign(req).Token.Split('.')[1]));
        Assert.Contains("\"exp\":1700000000", payload);     // number, no quotes
        Assert.DoesNotContain("\"exp\":\"1700000000\"", payload);
        Assert.Contains("\"sub\":\"x\"", payload);          // string claim stays quoted
    }

    [Fact]
    public void Header_line_wraps_as_bearer()
    {
        var req = new JwtRequest { Algorithm = "HS256", Key = Encoding.UTF8.GetBytes("k"), Claims = new() };
        var r = JwtSigner.Sign(req);
        Assert.Equal("Authorization", r.Header.HeaderName);
        Assert.StartsWith("Bearer ", r.Header.HeaderValue);
    }

    [Fact]
    public void Rs256_signs_and_verifies_with_public_key()
    {
        using var rsa = RSA.Create(2048);
        var req = new JwtRequest { Algorithm = "RS256", KeyPem = rsa.ExportRSAPrivateKeyPem(), Claims = new() { ["sub"] = "x" } };
        var jwt = JwtSigner.Sign(req).Token;
        string[] parts = jwt.Split('.');
        byte[] sig = Base64UrlDecode(parts[2]);
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Es256_signs_in_ieee_p1363_and_verifies()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new JwtRequest { Algorithm = "ES256", KeyPem = ec.ExportECPrivateKeyPem(), Claims = new() { ["sub"] = "x" } };
        var jwt = JwtSigner.Sign(req).Token;
        string[] parts = jwt.Split('.');
        byte[] sig = Base64UrlDecode(parts[2]);
        Assert.Equal(64, sig.Length); // P-256 r||s is fixed 64 bytes (the JOSE requirement)
        Assert.True(ec.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]                                              // F4: HS alg given a PEM
    public void Hs_alg_without_raw_key_is_clear_error()
    {
        var req = new JwtRequest { Algorithm = "HS256", KeyPem = "-----BEGIN PRIVATE KEY-----\n...", Claims = new() };
        var ex = Assert.Throws<MkAuthException>(() => JwtSigner.Sign(req));
        Assert.Contains("secret", ex.Message, StringComparison.OrdinalIgnoreCase); // readable, not an SR key
    }

    [Fact]                                              // F4: RS alg given raw bytes
    public void Rs_alg_without_pem_is_clear_error()
    {
        var req = new JwtRequest { Algorithm = "RS256", Key = Encoding.UTF8.GetBytes("notapem"), Claims = new() };
        var ex = Assert.Throws<MkAuthException>(() => JwtSigner.Sign(req));
        Assert.Contains("PEM", ex.Message);
    }

    [Fact]                                              // FIX 7: HS alg with a stray PEM is rejected
    public void Hs_alg_with_pem_set_is_rejected()
    {
        var req = new JwtRequest
        {
            Algorithm = "HS256",
            Key = Encoding.UTF8.GetBytes("k"),
            KeyPem = "-----BEGIN PRIVATE KEY-----\n...",
            Claims = new(),
        };
        var ex = Assert.Throws<MkAuthException>(() => JwtSigner.Sign(req));
        Assert.Contains("not a PEM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]                                              // FIX 7: RS alg with a stray raw key is rejected
    public void Rs_alg_with_raw_key_set_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var req = new JwtRequest
        {
            Algorithm = "RS256",
            Key = Encoding.UTF8.GetBytes("stray"),
            KeyPem = rsa.ExportRSAPrivateKeyPem(),
            Claims = new(),
        };
        var ex = Assert.Throws<MkAuthException>(() => JwtSigner.Sign(req));
        Assert.Contains("not a raw secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]                                              // FIX 8: malformed PEM gives a friendly message
    public void Rs_alg_malformed_pem_gives_friendly_message()
    {
        var req = new JwtRequest { Algorithm = "RS256", KeyPem = "not-a-pem", Claims = new() };
        var ex = Assert.Throws<MkAuthException>(() => JwtSigner.Sign(req));
        Assert.Contains("valid PEM private key", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RS256", ex.Message, StringComparison.Ordinal);
        // Not a bare framework SR key / "ArgumentException".
        Assert.DoesNotContain("ArgumentException", ex.Message, StringComparison.Ordinal);
    }

    [Fact]                                              // regression (round 2): armored PEM with corrupt DER -> friendly message (CryptographicException branch)
    public void Rs_alg_armored_but_corrupt_pem_gives_friendly_message()
    {
        // Valid PEM armor wrapping invalid DER makes ImportFromPem throw CryptographicException
        // (not ArgumentException); the widened catch must still yield the friendly MkAuthException.
        var req = new JwtRequest
        {
            Algorithm = "RS256",
            KeyPem = "-----BEGIN PRIVATE KEY-----\nAAAAAAAAAAAAAAAA\n-----END PRIVATE KEY-----",
            Claims = new(),
        };
        var ex = Assert.Throws<MkAuthException>(() => JwtSigner.Sign(req));
        Assert.Contains("valid PEM private key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // A1 (TA-C1): the six previously-untested algs (HS384/512, RS384/512, ES384/512). Each signs a
    // token and the signature is verified with the framework primitive bound to the MATCHING hash,
    // HARD-CODED per case — so a hash-family swap in JwtSigner (e.g. HS384 silently signing with
    // SHA-512) fails this test. Base64url decode here is the test's own hand-rolled helper, NEVER the
    // production Base64Url class (protocol-fake rule).
    [Theory]
    [InlineData("HS384")]
    [InlineData("HS512")]
    public void Hs_untested_algs_sign_and_verify_with_matching_hash(string alg)
    {
        byte[] key = Encoding.UTF8.GetBytes("supersecretkey");
        var req = new JwtRequest { Algorithm = alg, Key = key, Claims = new() { ["sub"] = "x" } };
        string[] parts = JwtSigner.Sign(req).Token.Split('.');
        Assert.Equal(3, parts.Length);

        byte[] signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        byte[] expected = alg switch
        {
            "HS384" => HmacWith<HMACSHA384>(key, signingInput),
            "HS512" => HmacWith<HMACSHA512>(key, signingInput),
            _ => throw new InvalidOperationException(alg),
        };
        string expectedSig = Convert.ToBase64String(expected).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedSig, parts[2]);
    }

    [Theory]
    [InlineData("RS384")]
    [InlineData("RS512")]
    public void Rs_untested_algs_sign_and_verify_with_matching_hash(string alg)
    {
        using var rsa = RSA.Create(2048);
        var req = new JwtRequest { Algorithm = alg, KeyPem = rsa.ExportRSAPrivateKeyPem(), Claims = new() { ["sub"] = "x" } };
        string[] parts = JwtSigner.Sign(req).Token.Split('.');
        byte[] sig = Base64UrlDecode(parts[2]);
        byte[] signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);

        HashAlgorithmName hash = alg switch
        {
            "RS384" => HashAlgorithmName.SHA384,
            "RS512" => HashAlgorithmName.SHA512,
            _ => throw new InvalidOperationException(alg),
        };
        Assert.True(rsa.VerifyData(signingInput, sig, hash, RSASignaturePadding.Pkcs1));
    }

    [Theory]
    [InlineData("ES384", "nistP384", 96)]   // P-384 r||s is fixed 96 bytes (the JOSE requirement)
    [InlineData("ES512", "nistP521", 132)]  // P-521 r||s is fixed 132 bytes
    public void Es_untested_algs_sign_in_ieee_p1363_and_verify_with_matching_hash(string alg, string curveName, int rawSigLen)
    {
        ECCurve curve = curveName switch
        {
            "nistP384" => ECCurve.NamedCurves.nistP384,
            "nistP521" => ECCurve.NamedCurves.nistP521,
            _ => throw new InvalidOperationException(curveName),
        };
        using var ec = ECDsa.Create(curve);
        var req = new JwtRequest { Algorithm = alg, KeyPem = ec.ExportECPrivateKeyPem(), Claims = new() { ["sub"] = "x" } };
        string[] parts = JwtSigner.Sign(req).Token.Split('.');
        byte[] sig = Base64UrlDecode(parts[2]);
        Assert.Equal(rawSigLen, sig.Length); // raw P1363 concatenation, not DER

        byte[] signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        HashAlgorithmName hash = alg switch
        {
            "ES384" => HashAlgorithmName.SHA384,
            "ES512" => HashAlgorithmName.SHA512,
            _ => throw new InvalidOperationException(alg),
        };
        Assert.True(ec.VerifyData(signingInput, sig, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    private static byte[] HmacWith<T>(byte[] key, byte[] data) where T : HMAC, new()
    {
        using var h = new T { Key = key };
        return h.ComputeHash(data);
    }

    private static byte[] Base64UrlDecode(string s)
    {
        string b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
