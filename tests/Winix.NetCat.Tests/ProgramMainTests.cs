#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Winix.NetCat.Tests;

/// <summary>
/// Integration tests that spawn the compiled nc binary (via <c>dotnet nc.dll</c>) and assert on
/// its real stdout/stderr/exit code. Same pattern as retry and envvault — in-process library
/// tests can't detect Program.cs regressions (hand-rolled introspection shims, dropped
/// JsonField registrations, arg-parse error arms that never fire).
/// </summary>
public class ProgramMainTests
{
    private static (int ExitCode, string Stdout, string Stderr) RunNc(params string[] args)
    {
        string ncDll = LocateNcDll();
        if (!File.Exists(ncDll))
        {
            throw new System.InvalidOperationException(
                $"nc.dll not built at '{ncDll}'. Run 'dotnet build src/nc' before running these tests.");
        }
        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        psi.ArgumentList.Add(ncDll);
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using Process p = Process.Start(psi) ?? throw new System.InvalidOperationException("failed to start dotnet");
        // Close stdin immediately so modes that block on stdin return quickly.
        p.StandardInput.Close();
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(30_000))
        {
            p.Kill(entireProcessTree: true);
            throw new System.TimeoutException("nc process did not exit within 30 seconds");
        }
        return (p.ExitCode, stdout, stderr);
    }

    private static string LocateNcDll()
    {
        string testAsmPath = typeof(ProgramMainTests).Assembly.Location;
        string testTfmDir = Path.GetDirectoryName(testAsmPath)!;
        string tfm = Path.GetFileName(testTfmDir);
        string configDir = Path.GetDirectoryName(testTfmDir)!;
        string config = Path.GetFileName(configDir);
        string testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(configDir))!;
        string testsDir = Path.GetDirectoryName(testProjectDir)!;
        string repoRoot = Path.GetDirectoryName(testsDir)!;
        return Path.Combine(repoRoot, "src", "nc", "bin", config, tfm, "nc.dll");
    }

    // --- Introspection ---

    [Fact]
    public void Help_ViaProcessSpawn_ProducesShellKitFormattedOutput()
    {
        var result = RunNc("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^Usage: nc\b", result.Stdout);
        Assert.Contains("Exit Codes:", result.Stdout);
        Assert.Contains("125", result.Stdout);
        Assert.Contains("126", result.Stdout);
        Assert.Contains("130", result.Stdout);
    }

    [Fact]
    public void Version_ViaProcessSpawn_PrintsSemver()
    {
        var result = RunNc("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^nc \d+\.\d+\.\d+", result.Stdout);
    }

    [Fact]
    public void Describe_ViaProcessSpawn_IsValidJsonWithAllDocumentedFields()
    {
        var result = RunNc("--describe");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument doc = JsonDocument.Parse(result.Stdout);
        JsonElement root = doc.RootElement;
        Assert.Equal("nc", root.GetProperty("tool").GetString());

        JsonElement options = root.GetProperty("options");
        HashSet<string> advertisedLongs = new();
        foreach (JsonElement opt in options.EnumerateArray())
        {
            if (opt.TryGetProperty("long", out JsonElement lo))
            {
                advertisedLongs.Add(lo.GetString()!);
            }
        }
        // Every flag Program.cs registers must be advertised. A dropped .Flag line is invisible
        // otherwise — same defect class that hit retry's round-6 review.
        foreach (string required in new[]
            {
                "--listen", "--check", "--udp", "--tls", "--insecure",
                "--ipv4", "--ipv6", "--no-shutdown", "--verbose",
                "--timeout", "--bind",
                "--help", "--version", "--describe", "--json", "--color", "--no-color",
            })
        {
            Assert.Contains(required, advertisedLongs);
        }

        JsonElement exitCodes = root.GetProperty("exit_codes");
        HashSet<int> advertisedCodes = new();
        foreach (JsonElement ec in exitCodes.EnumerateArray())
        {
            if (ec.TryGetProperty("code", out JsonElement c))
            {
                advertisedCodes.Add(c.GetInt32());
            }
        }
        foreach (int required in new[] { 0, 1, 2, 125, 126, 130 })
        {
            Assert.Contains(required, advertisedCodes);
        }
    }

    // --- Usage-error arms (the 17 BuildOptions throw sites) ---

    [Fact]
    public void ListenAndCheck_Together_ExitsUsageError()
    {
        var result = RunNc("--listen", "--check", "host", "80");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--listen", result.Stderr);
        Assert.Contains("--check", result.Stderr);
    }

    [Fact]
    public void Insecure_WithoutTls_ExitsUsageError()
    {
        var result = RunNc("--insecure", "host", "443");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--insecure", result.Stderr);
        Assert.Contains("--tls", result.Stderr);
    }

    [Fact]
    public void BadPortSpec_ExitsUsageError_NotCrash()
    {
        // Round-1 C3: `nc -z host invalid` previously produced an unhandled FormatException
        // stack trace. Now routed through UsageException → clean error + 125.
        var result = RunNc("-z", "host", "invalid-port-spec");

        Assert.Equal(125, result.ExitCode);
        Assert.DoesNotContain("   at ", result.Stderr);
        Assert.DoesNotContain("Unhandled exception", result.Stderr);
    }

    [Fact]
    public void BadPortNumber_OutOfRange_ExitsUsageError()
    {
        var result = RunNc("-z", "host", "70000");

        Assert.Equal(125, result.ExitCode);
        Assert.DoesNotContain("   at ", result.Stderr);
    }

    [Fact]
    public void BadBindAddress_NonParseableIp_ExitsUsageError()
    {
        // Round-1 I-1: `--bind 10.0.0..5` typo previously silently fell through to IPAddress.Any
        // — defeating the security intent of --bind. Now validated at parse time.
        var result = RunNc("--listen", "--bind", "10.0.0..5", "8080");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--bind", result.Stderr);
        Assert.Contains("10.0.0..5", result.Stderr);
    }

    [Fact]
    public void BadBindAddress_Hostname_ExitsUsageError()
    {
        // Hostnames not accepted — must be an IP literal. Without the validation, the listener
        // would silently bind to IPAddress.Any.
        var result = RunNc("--listen", "--bind", "localhost", "8080");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--bind", result.Stderr);
    }

    [Fact]
    public void BindWithoutListen_ExitsUsageError()
    {
        var result = RunNc("--bind", "127.0.0.1", "host", "80");

        Assert.Equal(125, result.ExitCode);
        Assert.Contains("--bind", result.Stderr);
        Assert.Contains("--listen", result.Stderr);
    }
}
