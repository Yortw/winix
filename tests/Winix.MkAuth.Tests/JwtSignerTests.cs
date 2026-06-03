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

    private static byte[] Base64UrlDecode(string s)
    {
        string b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
