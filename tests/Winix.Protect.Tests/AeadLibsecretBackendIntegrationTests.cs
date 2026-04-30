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
/// End-to-end integration tests that drive <see cref="AeadLibsecretBackend"/> against a real
/// libsecret service. Skipped unless the host is Linux <em>and</em> a secret service is reachable
/// (D-Bus session bus running, gnome-keyring-daemon registered).
///
/// Background: an earlier hardening pass tightened <c>LinuxLibsecretStore</c>'s namespace contract
/// without updating the AEAD backends' literal — Linux protect was broken for eight days before
/// a manual smoke test caught it. The portable unit test in
/// <c>AeadBackendNamespaceContractTests</c> locks the constant; this class is the
/// belt-and-braces end-to-end probe that catches any future regression in the
/// libsecret call chain (libsecret CLI argument shape, store layout, key-name
/// interaction, persistence across daemon restarts, etc.).
/// </summary>
public class AeadLibsecretBackendIntegrationTests
{
    [SkippableFact]
    public void RoundTrip_SmallPayload_ReturnsBytewiseIdenticalPlaintext()
    {
        Skip.IfNot(LibsecretProbe.IsServiceReachable(), LibsecretProbe.SkipReason);
        if (!OperatingSystem.IsLinux()) { return; } // satisfies CA1416 — real gate is Skip.IfNot above

        AssertRoundTripsCleanly(plaintext: System.Text.Encoding.UTF8.GetBytes("hello libsecret"));
    }

    [SkippableFact]
    public void RoundTrip_FiveMiBPayload_ExercisesMultiChunkPath()
    {
        // 5 MiB / 64 KiB = 80 chunks, exercising chunkIndex values 0..79 in the per-chunk AAD.
        // This is the same fixture size used in the cross-platform smoke playbook; multi-chunk
        // bugs (off-by-one in chunkIndex, reorder detection, final-flag handling) only surface
        // here — single-chunk tests cannot catch them.
        Skip.IfNot(LibsecretProbe.IsServiceReachable(), LibsecretProbe.SkipReason);
        if (!OperatingSystem.IsLinux()) { return; }

        byte[] plaintext = new byte[5 * 1024 * 1024];
        RandomNumberGenerator.Fill(plaintext);
        AssertRoundTripsCleanly(plaintext);
    }

    [SupportedOSPlatform("linux")]
    private static void AssertRoundTripsCleanly(byte[] plaintext)
    {
        // Construct the production backend with no test seam — this is deliberately the
        // parameterless ctor wired to LinuxLibsecretStore, so the namespace contract,
        // key-storage roundtrip and AEAD path all run end-to-end.
        using AeadLibsecretBackend backend = new();

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
/// Capability probe for the libsecret D-Bus service. Distinguishes "service unreachable"
/// (skip the test) from "service reachable but no entry yet" (run the test). The probe
/// shells out to <c>secret-tool lookup</c> with a non-existent label and inspects stderr
/// for sentinel strings emitted when the bus or daemon is missing.
///
/// Per <c>feedback_probe_must_observe.md</c>: we treat <em>observed</em> sentinel error
/// text as evidence of unreachability, not the absence of stdout output.
/// </summary>
internal static class LibsecretProbe
{
    public const string SkipReason =
        "libsecret service not reachable (run inside dbus-run-session with gnome-keyring-daemon — see CI ubuntu-latest steps)";

    public static bool IsServiceReachable()
    {
        if (!OperatingSystem.IsLinux()) { return false; }

        try
        {
            ProcessStartInfo psi = new("secret-tool")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("lookup");
            psi.ArgumentList.Add("winix-protect-probe");
            psi.ArgumentList.Add("nonexistent");

            using Process? p = Process.Start(psi);
            if (p is null) { return false; }
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(); } catch { /* best effort */ }
                return false;
            }
            string stderr = p.StandardError.ReadToEnd();
            // Empty stderr + exit 1 = "no entry found" (service reachable, no match).
            // Sentinel strings indicate the bus or daemon couldn't be contacted.
            string[] unreachableSentinels = ["machine-id", "Cannot spawn", "Cannot autolaunch", "No such interface"];
            return !unreachableSentinels.Any(stderr.Contains);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // secret-tool not on PATH — service definitely unreachable.
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
