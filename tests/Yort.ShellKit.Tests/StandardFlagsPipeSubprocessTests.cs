using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Yort.ShellKit.Tests;

/// <summary>
/// Externally observable contract: a Winix tool whose --help / --version / --describe
/// output is consumed by an early-closing pipe MUST exit clean (0) with no error
/// envelope on stderr. Empirically the .NET runtime already absorbs broken-pipe at
/// the Console.Out layer on both Windows .NET 10 and Linux .NET 8, so these tests
/// pass without the parser-level try/catch — they exist as regression guards in case
/// the runtime ever stops absorbing, AND to lock the externally-visible behaviour
/// across all standard flags. See docs/plans/2026-04-26-shellkit-broken-pipe.md.
/// </summary>
public class StandardFlagsPipeSubprocessTests
{
    [Theory]
    [InlineData("timeit", "--help")]
    [InlineData("timeit", "--version")]
    [InlineData("timeit", "--describe")]
    [InlineData("wargs", "--help")]
    [InlineData("wargs", "--version")]
    [InlineData("wargs", "--describe")]
    public void Tool_StandardFlagWithEarlyClosedStdout_ExitsCleanAndNoErrorEnvelope(string tool, string flag)
    {
        string toolPath = ResolveToolPath(tool);
        var psi = new ProcessStartInfo(toolPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(flag);

        using var proc = Process.Start(psi)!;
        proc.StandardInput.Close();

        // Read first line from stdout, then close — equivalent to `| head -1`.
        string? firstLine = proc.StandardOutput.ReadLine();
        proc.StandardOutput.Close();

        string stderr = proc.StandardError.ReadToEnd();

        bool exited = proc.WaitForExit(15_000);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
            try { proc.WaitForExit(5_000); } catch { /* best-effort cleanup */ }
            Assert.Fail($"{tool} {flag} did not exit within 15s. firstLine='{firstLine}' stderr='{stderr}'");
        }

        Assert.NotNull(firstLine);
        Assert.Equal(0, proc.ExitCode);
        Assert.DoesNotContain("unexpected_error", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("IOException", stderr, StringComparison.Ordinal);
    }

    private static string ResolveToolPath(string toolName)
    {
        string asmDir = Path.GetDirectoryName(typeof(StandardFlagsPipeSubprocessTests).Assembly.Location)!;

        // Walk up to the repo root (where Winix.sln lives) so the candidate path is
        // independent of where the tests get launched from.
        string? cursor = asmDir;
        while (cursor is not null && !File.Exists(Path.Combine(cursor, "Winix.sln")))
        {
            cursor = Path.GetDirectoryName(cursor);
        }
        Assert.NotNull(cursor);

        string config = asmDir.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar)
            ? "Release" : "Debug";

        // Validate TFM-shaped folder name before using it. If a future runtime identifier
        // ("win-x64") gets injected as an extra path segment, fail loudly with the asmDir
        // rather than producing a confusing "binary not found" path.
        string tfm = new DirectoryInfo(asmDir).Name;
        Assert.Matches(@"^net\d+\.\d+$", tfm);

        string exe = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        string candidate = Path.Combine(cursor!, "src", toolName, "bin", config, tfm, exe);
        Assert.True(File.Exists(candidate), $"{toolName} binary not found at {candidate}. " +
            "Build the solution first (`dotnet build Winix.sln`).");
        return candidate;
    }
}
