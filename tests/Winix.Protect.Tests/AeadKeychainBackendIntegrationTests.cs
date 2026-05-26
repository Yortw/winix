#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Winix.Protect;
using Xunit;

namespace Winix.Protect.Tests;

/// <summary>
/// End-to-end integration tests that drive <see cref="AeadKeychainBackend"/> against the real
/// macOS Keychain. Skipped unless the host is macOS <em>and</em> the <c>security</c> CLI can
/// store + look up + clear a throwaway entry in the user keychain (the keychain is unlocked).
///
/// Mirrors <see cref="AeadLibsecretBackendIntegrationTests"/> for symmetry: every AEAD backend
/// has a matching CI-grade integration test that exercises the real platform secret store. Without
/// this, the macOS path was only covered by <see cref="AeadBackendNamespaceContractTests"/> (a
/// portable unit test) and any latent breakage in the Keychain CLI invocation, store layout,
/// or secret-store namespace contract could ride to production undetected.
/// </summary>
[Collection(SharedKeystoreCollection.Name)]
public class AeadKeychainBackendIntegrationTests
{
    [SkippableFact]
    public void RoundTrip_SmallPayload_ReturnsBytewiseIdenticalPlaintext()
    {
        Skip.IfNot(KeychainProbe.IsServiceReachable(), KeychainProbe.SkipReason);
        if (!OperatingSystem.IsMacOS()) { return; } // satisfies CA1416 — real gate is Skip.IfNot above

        AssertRoundTripsCleanly(plaintext: System.Text.Encoding.UTF8.GetBytes("hello keychain"));
    }

    [SkippableFact]
    public void RoundTrip_FiveMiBPayload_ExercisesMultiChunkPath()
    {
        // 5 MiB / 64 KiB = 80 chunks, exercising chunkIndex values 0..79 in the per-chunk AAD.
        // Mirrors the libsecret integration test sizing — multi-chunk bugs (off-by-one in
        // chunkIndex, reorder detection, final-flag handling) only surface here.
        Skip.IfNot(KeychainProbe.IsServiceReachable(), KeychainProbe.SkipReason);
        if (!OperatingSystem.IsMacOS()) { return; }

        byte[] plaintext = new byte[5 * 1024 * 1024];
        RandomNumberGenerator.Fill(plaintext);
        AssertRoundTripsCleanly(plaintext);
    }

    [SupportedOSPlatform("macos")]
    private static void AssertRoundTripsCleanly(byte[] plaintext)
    {
        // Construct the production backend with no test seam — parameterless ctor wires
        // straight to MacOsKeychainStore via Scope.User, so the namespace contract,
        // index-aware key storage, and AEAD path all run end-to-end.
        using AeadKeychainBackend backend = new(Scope.User);

        byte[] header = Header.SerializeForAad(backend.Marker, RandomFileId());

        using MemoryStream ciphertext = new();
        using (MemoryStream source = new(plaintext, writable: false))
        {
            ChunkWriter.Write(source, ciphertext, backend, header);
        }

        // ChunkWriter emits the header first, but ChunkReader expects the caller
        // to have already consumed it — skip past the 22-byte header before reading.
        ciphertext.Position = Header.Length;
        using MemoryStream recovered = new();
        ChunkReader.Read(ciphertext, recovered, backend, header);

        Assert.Equal(plaintext, recovered.ToArray());
    }

    private static byte[] RandomFileId()
    {
        byte[] id = new byte[16];
        RandomNumberGenerator.Fill(id);
        return id;
    }
}

/// <summary>
/// Capability probe for the macOS Keychain. Same shape as <see cref="LibsecretProbe"/>: do a
/// real <c>add</c> → <c>find</c> → <c>delete</c> round-trip via the <c>security</c> CLI with
/// a throwaway attribute set, verify the stored value matches, clean up. Any deviation = the
/// keychain is not usable for our purposes (locked, missing, ACL-restricted, etc.).
/// </summary>
internal static class KeychainProbe
{
    public const string SkipReason =
        "macOS Keychain not reachable (security CLI failed a probe round-trip — keychain may be locked or unavailable)";

    private static string? _lastFailureDetail;

    public static bool IsServiceReachable()
    {
        if (!OperatingSystem.IsMacOS()) { return false; }

        string probeService = $"winix-probe-{Guid.NewGuid():N}";
        const string probeAccount = "v";
        const string probeValue = "DEADBEEF";

        try
        {
            // Step 1: store the sentinel.
            (int addExit, string _, string addErr) = RunSecurity(
                ["add-generic-password", "-a", probeAccount, "-s", probeService, "-w", probeValue]);
            if (addExit != 0)
            {
                Report($"add-generic-password exit={addExit} stderr='{Truncate(addErr)}'");
                return false;
            }

            try
            {
                // Step 2: read it back. -w prints the password to stdout when found.
                (int findExit, string findOut, string findErr) = RunSecurity(
                    ["find-generic-password", "-a", probeAccount, "-s", probeService, "-w"]);
                if (findExit != 0 || findOut.Trim() != probeValue)
                {
                    Report($"find-generic-password exit={findExit} stdout='{Truncate(findOut)}' stderr='{Truncate(findErr)}'");
                    return false;
                }
                return true;
            }
            finally
            {
                // Step 3: best-effort cleanup so the probe leaves no residue.
                RunSecurity(["delete-generic-password", "-a", probeAccount, "-s", probeService]);
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // security CLI not on PATH — keychain definitely unreachable.
            Report($"security CLI not found: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Report($"unexpected: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static (int exitCode, string stdout, string stderr) RunSecurity(string[] args)
    {
        ProcessStartInfo psi = new("security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using Process? p = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null for security.");
        if (!p.WaitForExit(5000))
        {
            try { p.Kill(); } catch { /* best effort */ }
            return (-1, string.Empty, "probe timed out after 5s");
        }
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        return (p.ExitCode, stdout, stderr);
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s.Substring(0, 200) + "...";

    private static void Report(string detail)
    {
        if (_lastFailureDetail == detail) { return; }
        _lastFailureDetail = detail;
        Console.Error.WriteLine($"KeychainProbe: service unreachable — {detail}");
    }
}
