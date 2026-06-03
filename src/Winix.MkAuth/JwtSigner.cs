#nullable enable

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Winix.MkAuth;

/// <summary>
/// JWT minting inputs. Provide <see cref="Key"/> for HS* (raw secret bytes) OR
/// <see cref="KeyPem"/> for RS*/ES* (PEM private key).
/// </summary>
public sealed class JwtRequest
{
    /// <summary>JWS algorithm: HS256/384/512, RS256/384/512, or ES256/384/512.</summary>
    public required string Algorithm { get; init; }

    /// <summary>Raw HMAC secret bytes. Required for HS* algorithms; must be null for RS*/ES*.</summary>
    public byte[]? Key { get; init; }

    /// <summary>PEM-encoded private key. Required for RS*/ES* algorithms; must be null for HS*.</summary>
    public string? KeyPem { get; init; }

    /// <summary>
    /// Payload claims. Numeric values (NumericDate claims such as exp/iat/nbf) are emitted as JSON
    /// numbers, bools as JSON bools, strings quoted — see the type-switch in <see cref="JwtSigner"/>.
    /// </summary>
    public Dictionary<string, object?> Claims { get; init; } = new();

    /// <summary>Extra JOSE header parameters (e.g. <c>kid</c>) merged after <c>alg</c>/<c>typ</c>.</summary>
    public Dictionary<string, string> HeaderParams { get; init; } = new();
}

/// <summary>The minted token plus its ready-to-send Authorization header.</summary>
public sealed class JwtResult
{
    /// <summary>The compact JWS string (<c>header.payload.signature</c>).</summary>
    public required string Token { get; init; }

    /// <summary>The <c>Authorization: Bearer &lt;token&gt;</c> header.</summary>
    public required HeaderResult Header { get; init; }
}

/// <summary>
/// Mints a signed compact JWS (RFC 7515/7519). Hand-built (no JWT library) to stay AOT-clean;
/// the payload is assembled with <see cref="JsonObject"/> which the AOT publish confirmed produces
/// no trim warnings.
/// </summary>
public static class JwtSigner
{
    /// <summary>
    /// Signs the request and returns the compact JWS and its Authorization header.
    /// </summary>
    /// <param name="req">The minting request (algorithm, key material, claims, header params).</param>
    /// <returns>The token and a <c>Bearer</c> header.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the key kind does not match the algorithm family (HS* needs <see cref="JwtRequest.Key"/>,
    /// RS*/ES* needs <see cref="JwtRequest.KeyPem"/>), or when the algorithm is unsupported.
    /// </exception>
    public static JwtResult Sign(JwtRequest req)
    {
        RequireKeyForAlg(req); // F4: HS* needs Key, RS*/ES* needs KeyPem — clear message, not an NRE

        var header = new JsonObject { ["alg"] = req.Algorithm, ["typ"] = "JWT" };
        foreach (var kv in req.HeaderParams)
        {
            header[kv.Key] = kv.Value;
        }

        var payload = new JsonObject();
        foreach (var kv in req.Claims)
        {
            payload[kv.Key] = ToJsonNode(kv.Value); // F2: preserve JSON type (number/bool/string)
        }

        string encodedHeader = Base64Url.EncodeNoPad(Encoding.UTF8.GetBytes(header.ToJsonString()));
        string encodedPayload = Base64Url.EncodeNoPad(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        string signingInput = $"{encodedHeader}.{encodedPayload}";
        byte[] signingBytes = Encoding.ASCII.GetBytes(signingInput);

        byte[] sig = req.Algorithm switch
        {
            "HS256" => Hmac<HMACSHA256>(req.Key!, signingBytes),
            "HS384" => Hmac<HMACSHA384>(req.Key!, signingBytes),
            "HS512" => Hmac<HMACSHA512>(req.Key!, signingBytes),
            "RS256" => Rsa(req.KeyPem!, signingBytes, HashAlgorithmName.SHA256),
            "RS384" => Rsa(req.KeyPem!, signingBytes, HashAlgorithmName.SHA384),
            "RS512" => Rsa(req.KeyPem!, signingBytes, HashAlgorithmName.SHA512),
            "ES256" => Ec(req.KeyPem!, signingBytes, HashAlgorithmName.SHA256),
            "ES384" => Ec(req.KeyPem!, signingBytes, HashAlgorithmName.SHA384),
            "ES512" => Ec(req.KeyPem!, signingBytes, HashAlgorithmName.SHA512),
            _ => throw new ArgumentException($"Unsupported JWT algorithm '{req.Algorithm}'."),
        };

        string token = $"{signingInput}.{Base64Url.EncodeNoPad(sig)}";
        return new JwtResult { Token = token, Header = new HeaderResult("Authorization", $"Bearer {token}") };
    }

    private static byte[] Hmac<T>(byte[] key, byte[] data) where T : HMAC, new()
    {
        using var h = new T { Key = key };
        return h.ComputeHash(data);
    }

    private static byte[] Rsa(string pem, byte[] data, HashAlgorithmName hash)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa.SignData(data, hash, RSASignaturePadding.Pkcs1);
    }

    private static byte[] Ec(string pem, byte[] data, HashAlgorithmName hash)
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(pem);
        // JOSE requires the raw fixed-length r||s concatenation (P1363), not the default DER sequence.
        return ec.SignData(data, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    // F2: preserve the JSON type of a claim value (so NumericDate stays a number, not a quoted string).
    private static JsonNode? ToJsonNode(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return JsonValue.Create(s);
        }

        if (value is bool b)
        {
            return JsonValue.Create(b);
        }

        if (value is long l)
        {
            return JsonValue.Create(l);
        }

        if (value is int i)
        {
            return JsonValue.Create((long)i);
        }

        if (value is double d)
        {
            return JsonValue.Create(d);
        }

        if (value is JsonNode n)
        {
            return n;
        }

        return JsonValue.Create(value.ToString());
    }

    // F4: fail fast with a clear message when the key kind doesn't match the algorithm family.
    private static void RequireKeyForAlg(JwtRequest req)
    {
        bool hmac = req.Algorithm.StartsWith("HS", StringComparison.Ordinal);
        if (hmac && req.Key is null)
        {
            throw new ArgumentException($"Algorithm {req.Algorithm} needs a raw secret key (e.g. --key env:SECRET), not a PEM.");
        }

        if (!hmac && req.KeyPem is null)
        {
            throw new ArgumentException($"Algorithm {req.Algorithm} needs a PEM private key (e.g. --key file:key.pem), not a raw secret.");
        }
    }
}
