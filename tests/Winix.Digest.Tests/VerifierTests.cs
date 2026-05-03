#nullable enable
using System;
using System.Reflection;
using Xunit;
using Winix.Codec;
using Winix.Digest;

namespace Winix.Digest.Tests;

public class VerifierTests
{
    [Fact]
    public void Verify_HexMatch_ReturnsTrue()
    {
        string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        byte[] computed = Winix.Codec.Hex.Decode(expected);
        Assert.True(Verifier.Verify(computed, expected, OutputFormat.Hex));
    }

    [Fact]
    public void Verify_HexMismatch_ReturnsFalse()
    {
        string expected = "0000000000000000000000000000000000000000000000000000000000000000";
        byte[] computed = Winix.Codec.Hex.Decode("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        Assert.False(Verifier.Verify(computed, expected, OutputFormat.Hex));
    }

    [Fact]
    public void Verify_HexCaseInsensitive()
    {
        byte[] computed = new byte[] { 0xab, 0xcd, 0xef };
        Assert.True(Verifier.Verify(computed, "abcdef", OutputFormat.Hex));
        Assert.True(Verifier.Verify(computed, "ABCDEF", OutputFormat.Hex));
        Assert.True(Verifier.Verify(computed, "AbCdEf", OutputFormat.Hex));
    }

    [Fact]
    public void Verify_Base64_CaseSensitive()
    {
        byte[] computed = new byte[] { 0x66, 0x6f, 0x6f }; // "foo"
        Assert.True(Verifier.Verify(computed, "Zm9v", OutputFormat.Base64));
        // Base64 alphabet is case-sensitive — lower-case "zm9v" is a different value entirely.
        Assert.False(Verifier.Verify(computed, "zm9v", OutputFormat.Base64));
    }

    [Fact]
    public void Verify_Base64Url_Matches_And_RejectsStandardWhenSlashOrPlusAppears()
    {
        // Choose bytes that produce a + or / in standard base64 so URL-safe encoding differs.
        byte[] computed = new byte[] { 0xff, 0xfe, 0xfd, 0xfc };
        string urlSafe = Winix.Codec.Base64.Encode(computed, urlSafe: true);
        string standard = Winix.Codec.Base64.Encode(computed, urlSafe: false);
        Assert.NotEqual(urlSafe, standard);
        Assert.True(Verifier.Verify(computed, urlSafe, OutputFormat.Base64Url));
        Assert.False(Verifier.Verify(computed, standard, OutputFormat.Base64Url));
    }

    [Fact]
    public void Verify_Base32_MatchesCrockfordUpperCase()
    {
        byte[] computed = new byte[] { 0x12, 0x34, 0x56, 0x78 };
        string expected = Winix.Codec.Base32Crockford.Encode(computed);
        Assert.True(Verifier.Verify(computed, expected, OutputFormat.Base32));
    }

    // -- Round-2 review CR-I1 — Crockford base32 is case-insensitive on decode.
    //    The encoder produces uppercase but a user typing --verify in lowercase
    //    (a common shell-history situation) must still match. Pre-fix the comparison
    //    rejected lowercase as a mismatch; post-fix it accepts. --
    [Fact]
    public void Verify_Base32_MatchesCrockfordLowerCase()
    {
        byte[] computed = new byte[] { 0x12, 0x34, 0x56, 0x78 };
        string expectedUpper = Winix.Codec.Base32Crockford.Encode(computed);
        string expectedLower = expectedUpper.ToLowerInvariant();
        Assert.NotEqual(expectedUpper, expectedLower); // sanity: there's actual case to fold
        Assert.True(Verifier.Verify(computed, expectedLower, OutputFormat.Base32));
    }

    [Fact]
    public void Verify_Base32_MatchesCrockfordMixedCase()
    {
        byte[] computed = new byte[] { 0x12, 0x34, 0x56, 0x78 };
        string expectedUpper = Winix.Codec.Base32Crockford.Encode(computed);
        // Toggle every other char's case.
        var sb = new System.Text.StringBuilder(expectedUpper.Length);
        for (int i = 0; i < expectedUpper.Length; i++)
        {
            char c = expectedUpper[i];
            sb.Append((i % 2 == 0 && c >= 'A' && c <= 'Z') ? char.ToLowerInvariant(c) : c);
        }
        Assert.True(Verifier.Verify(computed, sb.ToString(), OutputFormat.Base32));
    }

    [Fact]
    public void Verify_NullExpected_ReturnsFalse()
    {
        Assert.False(Verifier.Verify(new byte[] { 0x00 }, null!, OutputFormat.Hex));
    }

    // -- Round-1 review test gap — binding pin for constant-time comparison.
    //    If a future refactor swaps `ConstantTimeCompare.StringEqualsAscii` for `==` or
    //    `string.Equals`, the verify path silently regresses to data-dependent timing —
    //    and HMAC verification in --verify mode becomes vulnerable to remote timing
    //    side-channels. Behavioural tests can't distinguish constant-time from short-
    //    circuit compare, so this scans the IL of Verifier.Verify for a call to
    //    ConstantTimeCompare.StringEqualsAscii. A regression that drops constant-time
    //    will fail the test even if all the other behavioural cases above still pass. --
    // The IL-scan binding pin uses MethodBody.GetILAsByteArray() and Module.ResolveMethod,
    // which carry RequiresUnreferencedCode attributes. These warnings only matter for
    // trim/AOT scenarios — irrelevant for an xUnit test assembly. Suppress at the call sites.
#pragma warning disable IL2026
    [Fact]
    public void Verify_BindsToConstantTimeCompare()
    {
        MethodInfo method = typeof(Verifier).GetMethod(nameof(Verifier.Verify))!;
        Module module = method.Module;
        byte[]? il = method.GetMethodBody()!.GetILAsByteArray();
        Assert.NotNull(il);

        bool found = false;
        for (int i = 0; i < il!.Length - 4; i++)
        {
            byte opcode = il[i];
            // 0x28 = call, 0x6F = callvirt — both use a 4-byte method token immediately after.
            if (opcode != 0x28 && opcode != 0x6F) continue;
            int token = BitConverter.ToInt32(il, i + 1);
            MethodBase? resolved;
            try { resolved = module.ResolveMethod(token); }
            catch { continue; } // not a real method token at this position, just scan past
            if (resolved?.DeclaringType == typeof(ConstantTimeCompare) &&
                resolved.Name == nameof(ConstantTimeCompare.StringEqualsAscii))
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Verifier.Verify must call ConstantTimeCompare.StringEqualsAscii");
    }
#pragma warning restore IL2026
}
