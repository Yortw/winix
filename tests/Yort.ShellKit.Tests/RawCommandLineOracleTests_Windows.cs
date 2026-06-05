using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class RawCommandLineOracleTests_Windows
{
    // Raw argument tails fed verbatim to ArgvEcho. No newlines (oracle prints line-per-arg).
    public static TheoryData<string> RawTails() => new()
    {
        "plain a b",
        "\"quoted arg\" x",
        "foo\"*\"bar",
        "a\\\\\\b dir\\",
        "a\\\"b",
        "a\\\\\"b c\" d",
        "\"\" x",
        "\"a\"\"b\"",
        "\"unterminated",
        "*.txt \"*.txt\"",
        "mixed\ttabs and  spaces",
    };

    [SkippableTheory]
    [MemberData(nameof(RawTails))]
    public void Tokenizer_Matches_DotnetArgvSplitting(string rawTail)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Raw command line semantics are Windows-only");
        if (!OperatingSystem.IsWindows()) { return; } // deliberate redundancy for CA1416

        string exe = Path.Combine(AppContext.BaseDirectory, "ArgvEcho.exe");
        Assert.True(File.Exists(exe), $"oracle missing: {exe}");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            // Deliberate use of the string Arguments property (not ArgumentList): the whole
            // point is to hand the child an exact raw tail so its argv shows how the runtime
            // splits it. ArgumentList would re-quote and defeat the oracle.
            Arguments = rawTail,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        using var proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        string[] childArgv = output.Length == 0
            ? Array.Empty<string>()
            : output.TrimEnd('\r', '\n').Split(Environment.NewLine);

        // The child's raw command line is "<exe>" + " " + rawTail (built by Process.Start).
        var tokens = RawCommandLineTokenizer.Tokenize($"\"{exe}\" {rawTail}");
        string[] ourArgv = tokens.Skip(1).Select(t => t.Text).ToArray();

        Assert.Equal(childArgv, ourArgv);
    }

    [SkippableFact]
    public void Tokenizer_Matches_CurrentProcess()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Raw command line semantics are Windows-only");
        if (!OperatingSystem.IsWindows()) { return; } // deliberate redundancy for CA1416

        // The test host's own command line is a free real-world vector.
        var tokens = RawCommandLineTokenizer.Tokenize(Environment.CommandLine);
        string[] runtimeArgv = Environment.GetCommandLineArgs();
        Assert.Equal(runtimeArgv, tokens.Select(t => t.Text).ToArray());
    }
}
