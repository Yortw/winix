#nullable enable

using System;
using System.IO;
using Winix.EnvVault;
using Winix.EnvVault.Tests.Fakes;
using Winix.SecretStore;
using Xunit;

namespace Winix.EnvVault.Tests;

/// <summary>
/// Regression tests locking envvault's --color emission path.
/// Guards against a future regression where colour is silently unwired from the
/// Cli.Run production path (as occurred in trash/hcat/wargs).
/// </summary>
/// <remarks>
/// Colour path: Cli.Run → RunUnset → Formatting.ErrorLine(msg, o.UseColor) → stderr.
/// o.UseColor comes from ArgParser.Parse → parsed.UseColor → ResolveColor.
/// --color=always forces useColor=true even to a non-TTY StringWriter.
/// The "--unset" path is used because it emits a coloured ErrorLine on stderr when
/// the key does not exist — no interactive prompt, no system backend call.
/// NullSecretStore starts empty, so "missing.KEY" is always not-found.
/// </remarks>
public sealed class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    private static (int exit, string stdout, string stderr) RunCli(params string[] args)
    {
        var store = new NullSecretStore();
        var launcher = new FakeProcessLauncher();
        var prompt = new FakeConsolePrompt(isInteractive: false);
        var so = new StringWriter();
        var se = new StringWriter();
        int exit = Cli.Run(args, store, launcher, prompt, so, se, stdoutIsTty: false);
        return (exit, so.ToString(), se.ToString());
    }

    [Fact]
    public void Run_ColorAlways_ErrorLineContainsEscape()
    {
        // "--unset missing KEY" → RunUnset → store.Delete returns false → ErrorLine("... not found", useColor: true)
        // → AnsiColor.Red(true) + "envvault: ..." + AnsiColor.Reset(true) written to stderr.
        var r = RunCli("--color=always", "--unset", "missing", "KEY");
        // Exit code 127 (not found) confirms the error path ran.
        Assert.Equal(127, r.exit);
        Assert.Contains(Esc, r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_NoColor_ErrorLineContainsNoEscape()
    {
        var r = RunCli("--no-color", "--unset", "missing", "KEY");
        Assert.Equal(127, r.exit);
        Assert.DoesNotContain(Esc, r.stderr, StringComparison.Ordinal);
        // The error message must still be present — confirming plain output, not empty.
        Assert.Contains("not found", r.stderr, StringComparison.Ordinal);
    }
}
