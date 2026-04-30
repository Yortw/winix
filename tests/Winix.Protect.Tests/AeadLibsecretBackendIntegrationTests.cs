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
/// Capability probe for the libsecret D-Bus service. Per <c>feedback_probe_must_observe.md</c>,
/// the probe actually exercises the path it gates rather than inferring reachability from
/// stderr heuristics: it does a one-shot store → lookup → clear round-trip with a
/// uniquely-named throwaway attribute set. If all three steps succeed, the service is
/// reachable for our purposes; any failure (timeout, non-zero exit, mismatched value)
/// returns false. This replaces an earlier heuristic probe that returned false negatives
/// on the GitHub Actions ubuntu-latest runner.
/// </summary>
internal static class LibsecretProbe
{
    public const string SkipReason =
        "libsecret service not reachable (run inside dbus-run-session with gnome-keyring-daemon — see CI ubuntu-latest steps)";

    /// <summary>Last failure detail captured during the most recent <see cref="IsServiceReachable"/> call. Surfaced via <see cref="Console.Error"/> so a CI log shows why the integration tests skipped.</summary>
    private static string? _lastFailureDetail;

    public static bool IsServiceReachable()
    {
        if (!OperatingSystem.IsLinux()) { return false; }

        // Use a guaranteed-unique attribute pair so we don't collide with anything else
        // in the keyring and so a stale entry from a previous failed probe doesn't poison
        // the current call.
        string probeService = $"winix-probe-{Guid.NewGuid():N}";
        const string probeKey = "v";
        const string probeValue = "DEADBEEF";

        try
        {
            // Step 1: store a sentinel value via stdin.
            (int storeExit, string _, string storeErr) = RunSecretTool(
                ["store", "--label=winix-probe", "service", probeService, "key", probeKey],
                stdin: probeValue);
            if (storeExit != 0)
            {
                Report($"store exit={storeExit} stderr='{Truncate(storeErr)}'");
                return false;
            }

            // Step 2: read it back. Any deviation from the sentinel = service is reachable
            // but not behaving correctly; treat as unreachable for test gating purposes.
            (int lookupExit, string lookupOut, string lookupErr) = RunSecretTool(
                ["lookup", "service", probeService, "key", probeKey], stdin: null);
            try
            {
                if (lookupExit != 0 || lookupOut.Trim() != probeValue)
                {
                    Report($"lookup exit={lookupExit} stdout='{Truncate(lookupOut)}' stderr='{Truncate(lookupErr)}'");
                    return false;
                }
                return true;
            }
            finally
            {
                // Step 3: best-effort cleanup — never let a leftover entry confuse the next probe.
                RunSecretTool(["clear", "service", probeService, "key", probeKey], stdin: null);
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // secret-tool not on PATH — service definitely unreachable.
            Report($"secret-tool not found: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Report($"unexpected: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static (int exitCode, string stdout, string stderr) RunSecretTool(string[] args, string? stdin)
    {
        ProcessStartInfo psi = new("secret-tool")
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using Process? p = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null for secret-tool.");
        if (stdin is not null)
        {
            p.StandardInput.Write(stdin);
            p.StandardInput.Close();
        }
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
        // xUnit captures stderr per-test process and surfaces it in CI logs when tests skip
        // unexpectedly. This is the only diagnostic signal the integration tests have.
        Console.Error.WriteLine($"LibsecretProbe: service unreachable — {detail}");
    }
}
