#nullable enable
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Winix.EnvVault.Tests;

/// <summary>
/// Integration tests that spawn the compiled envvault binary (via <c>dotnet envvault.dll</c>) and
/// assert on its real stdout. The in-process <see cref="ArgParser"/>-level tests cannot detect a
/// regression in <c>Program.Main</c>'s introspection routing — if someone reintroduces a hand-rolled
/// <c>PrintHelp</c>/<c>PrintDescribe</c> shim, ArgParser is never called for bare <c>--help</c> and
/// the ArgParser-level tests pass despite the shim being live. These tests drive the entry point.
/// </summary>
public class ProgramMainTests
{
    private static (int ExitCode, string Stdout, string Stderr) RunEnvvault(params string[] args)
    {
        string envvaultDll = LocateEnvvaultDll();
        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(envvaultDll);
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using Process p = Process.Start(psi) ?? throw new System.InvalidOperationException("failed to start dotnet");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        // 30s timeout defends against a pathological CI runner where the spawned process hangs;
        // without it a test-suite-wide hang is possible.
        if (!p.WaitForExit(30_000))
        {
            p.Kill(entireProcessTree: true);
            throw new System.TimeoutException("envvault process did not exit within 30 seconds");
        }
        return (p.ExitCode, stdout, stderr);
    }

    /// <summary>Resolves the path to envvault.dll by mirroring this test assembly's Debug/Release+TFM into the src/envvault output directory. Depends on the ProjectReference in the test csproj building envvault first.</summary>
    private static string LocateEnvvaultDll()
    {
        string testAsmPath = typeof(ProgramMainTests).Assembly.Location;
        string testTfmDir = Path.GetDirectoryName(testAsmPath)!;          // .../bin/<Config>/<TFM>
        string tfm = Path.GetFileName(testTfmDir);                         // net10.0
        string configDir = Path.GetDirectoryName(testTfmDir)!;             // .../bin/<Config>
        string config = Path.GetFileName(configDir);                       // Debug | Release
        string testProjectDir = Path.GetDirectoryName(Path.GetDirectoryName(configDir))!;
        string testsDir = Path.GetDirectoryName(testProjectDir)!;
        string repoRoot = Path.GetDirectoryName(testsDir)!;
        return Path.Combine(repoRoot, "src", "envvault", "bin", config, tfm, "envvault.dll");
    }

    [Fact]
    public void Help_ViaProcessSpawn_ProducesShellKitFormattedOutput()
    {
        // Regression guard for the removed Program.cs PrintHelp/PrintDescribe shim (commit 07538e9).
        // The ArgParser-level tests would pass even if someone reintroduced the shim because
        // Program.Main would short-circuit before ArgParser ran. Asserting on the REAL spawned
        // binary's output catches that regression.
        var result = RunEnvvault("--help");

        Assert.Equal(0, result.ExitCode);
        // 'Usage:' prefix is ShellKit's StandardFlags rendering. The old hand-rolled shim started
        // with the tagline 'envvault — cross-platform keychain-backed env var manager'.
        Assert.Matches(@"^Usage: envvault\b", result.Stdout);
        // Exit Codes section — only ShellKit's ExitCodes(...) declaration produces this block.
        Assert.Contains("Exit Codes:", result.Stdout);
        // All three POSIX codes should appear (would only land here if ArgParser's ExitCodes
        // registration is actually used — a hardcoded shim would need to duplicate the text).
        Assert.Contains("125", result.Stdout);
        Assert.Contains("126", result.Stdout);
        Assert.Contains("127", result.Stdout);
    }

    [Fact]
    public void Describe_ViaProcessSpawn_ProducesShellKitJsonSchema()
    {
        // The old shim emitted a minimal 4-field JSON. ShellKit's --describe emits a rich schema
        // including options[], exit_codes[], examples[], and platform{}. Parsing and asserting on
        // that schema catches a shim regression.
        var result = RunEnvvault("--describe");

        Assert.Equal(0, result.ExitCode);
        using JsonDocument doc = JsonDocument.Parse(result.Stdout);
        JsonElement root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("options", out JsonElement options), "ShellKit --describe must include options[]");
        Assert.Equal(JsonValueKind.Array, options.ValueKind);
        Assert.True(root.TryGetProperty("exit_codes", out JsonElement exitCodes), "ShellKit --describe must include exit_codes[]");
        Assert.Equal(JsonValueKind.Array, exitCodes.ValueKind);
        Assert.True(root.TryGetProperty("examples", out JsonElement examples), "ShellKit --describe must include examples[]");
        Assert.True(root.TryGetProperty("platform", out _), "envvault --describe registers a .Platform(...) stanza");
    }

    [Fact]
    public void Version_ViaProcessSpawn_Returns0AndPrintsVersion()
    {
        var result = RunEnvvault("--version");

        Assert.Equal(0, result.ExitCode);
        // Expected format: 'envvault <semver>'.
        Assert.Matches(@"^envvault \d+\.\d+\.\d+", result.Stdout);
    }
}
