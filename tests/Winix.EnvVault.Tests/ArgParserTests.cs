#nullable enable
using System;
using System.Linq;
using Winix.EnvVault;
using Xunit;

namespace Winix.EnvVault.Tests;

public class ArgParserTests
{
    [Fact]
    public void BareForm_SingleNamespace_ParsesAsExec()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "github", "gh", "pr", "list" });
        Assert.Null(r.Error);
        Assert.NotNull(r.Options);
        Assert.Equal(SubCommand.Exec, r.Options!.SubCommand);
        Assert.Equal(new[] { "github" }, r.Options.Namespaces);
        Assert.Equal(new[] { "gh", "pr", "list" }, r.Options.CommandArgv);
    }

    [Fact]
    public void BareForm_CommaSeparatedNamespaces_ParsesBothInOrder()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "github,aws", "deploy.sh" });
        Assert.NotNull(r.Options);
        Assert.Equal(new[] { "github", "aws" }, r.Options!.Namespaces);
        Assert.Equal(new[] { "deploy.sh" }, r.Options.CommandArgv);
    }

    [Fact]
    public void SetFlag_MultipleKeys_ParsesAllKeys()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--set", "aws", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY" });
        Assert.NotNull(r.Options);
        Assert.Equal(SubCommand.Set, r.Options!.SubCommand);
        Assert.Equal(new[] { "aws" }, r.Options.Namespaces);
        Assert.Equal(new[] { "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY" }, r.Options.Keys);
    }

    [Fact]
    public void ListFlag_NoPositional_ParsesAsListNamespaces()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--list" });
        Assert.NotNull(r.Options);
        Assert.Equal(SubCommand.List, r.Options!.SubCommand);
        Assert.Empty(r.Options.Namespaces);
    }

    [Fact]
    public void ListFlag_WithNamespace_ParsesAsListKeys()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--list", "github" });
        Assert.NotNull(r.Options);
        Assert.Equal(SubCommand.List, r.Options!.SubCommand);
        Assert.Equal(new[] { "github" }, r.Options.Namespaces);
    }

    [Fact]
    public void GetFlag_ParsesAsGet()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--get", "github", "TOKEN" });
        Assert.NotNull(r.Options);
        Assert.Equal(SubCommand.Get, r.Options!.SubCommand);
        Assert.Equal(new[] { "github" }, r.Options.Namespaces);
        Assert.Equal(new[] { "TOKEN" }, r.Options.Keys);
    }

    [Fact]
    public void UnsetFlag_ParsesAsUnset()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--unset", "github", "TOKEN" });
        Assert.NotNull(r.Options);
        Assert.Equal(SubCommand.Unset, r.Options!.SubCommand);
    }

    [Fact]
    public void ValueFlag_WithSet_ParsesValue()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--value", "hunter2", "--set", "github", "TOKEN" });
        Assert.NotNull(r.Options);
        Assert.Equal(SubCommand.Set, r.Options!.SubCommand);
        Assert.Equal("hunter2", r.Options.ExplicitValue);
    }

    [Fact]
    public void NoEchoFlag_Accepted_NoOp()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--noecho", "--set", "github", "TOKEN" });
        Assert.NotNull(r.Options);
        Assert.True(r.Options!.NoEcho);
    }

    [Fact]
    public void RequirePassphrase_Parsed_AsFlag()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--require-passphrase", "--set", "x", "Y" });
        Assert.NotNull(r.Options);
        Assert.True(r.Options!.RequirePassphrase);
    }

    [Fact]
    public void MultipleActionFlags_Error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--set", "--list", "x", "Y" });
        Assert.NotNull(r.Error);
        Assert.Contains("mutually exclusive", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyArgs_Error()
    {
        ArgParser.Result r = ArgParser.Parse(Array.Empty<string>());
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void BareForm_DownstreamCommandHasSetFlag_StillExecMode()
    {
        // Regression: --set appearing in downstream argv must not mis-dispatch to flag mode.
        ArgParser.Result r = ArgParser.Parse(new[] { "myns", "helm", "upgrade", "release", "--set", "image.tag=foo", "./chart" });
        Assert.Null(r.Error);
        Assert.NotNull(r.Options);
        Assert.Equal(SubCommand.Exec, r.Options!.SubCommand);
        Assert.Equal(new[] { "myns" }, r.Options.Namespaces);
        Assert.Equal(new[] { "helm", "upgrade", "release", "--set", "image.tag=foo", "./chart" }, r.Options.CommandArgv);
    }

    [Fact]
    public void BareForm_NoEchoAfterNamespace_PassesThroughToCommand()
    {
        // Regression: --noecho after the namespace positional belongs to the command, not envvault.
        ArgParser.Result r = ArgParser.Parse(new[] { "--noecho", "github", "gh", "--noecho", "deploy" });
        Assert.Null(r.Error);
        Assert.NotNull(r.Options);
        Assert.True(r.Options!.NoEcho);
        Assert.Equal(new[] { "gh", "--noecho", "deploy" }, r.Options.CommandArgv);
    }

    [Fact]
    public void ExecMode_RequirePassphraseThenOptOut_OptOutWins()
    {
        // Both modes should agree: explicit --no-require-passphrase always wins.
        ArgParser.Result r = ArgParser.Parse(new[] { "--require-passphrase", "--no-require-passphrase", "myns", "cmd" });
        Assert.NotNull(r.Options);
        Assert.False(r.Options!.RequirePassphrase);
    }

    [Fact]
    public void ExecMode_OptOutThenRequirePassphrase_OptOutStillWins()
    {
        // Ordering-independent: even when --no-require-passphrase comes first, it wins.
        ArgParser.Result r = ArgParser.Parse(new[] { "--no-require-passphrase", "--require-passphrase", "myns", "cmd" });
        Assert.NotNull(r.Options);
        Assert.False(r.Options!.RequirePassphrase);
    }

    [Fact]
    public void ListFlag_TooManyPositionals_Error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--list", "a", "b" });
        Assert.NotNull(r.Error);
        Assert.Contains("at most one", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetFlag_MissingKey_Error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--get", "github" });
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void SetFlag_NamespaceOnly_Error()
    {
        // Regression: --set with only a namespace (no keys) hits the InterpretPositionals
        // error arm "--set requires a namespace and at least one key". Untested before; easy
        // typo (`envvault --set github` when the user meant `envvault --set github TOKEN`).
        ArgParser.Result r = ArgParser.Parse(new[] { "--set", "github" });
        Assert.NotNull(r.Error);
        Assert.Contains("requires a namespace and at least one key", r.Error!);
    }

    [Fact]
    public void SetFlag_NoPositionals_Error()
    {
        // `envvault --set` with nothing else.
        ArgParser.Result r = ArgParser.Parse(new[] { "--set" });
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void UnsetFlag_NamespaceOnly_Error()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--unset", "github" });
        Assert.NotNull(r.Error);
        Assert.Contains("exactly one namespace and one key", r.Error!);
    }

    [Fact]
    public void ExecMode_NamespaceOnly_Error()
    {
        // Regression for ArgParser.ParseExecMode's `if (i + 1 >= argv.Count)` branch. `envvault
        // github` alone (namespace without command) is a common typo — without this test, a
        // regression that built an empty CommandArgv would crash ExecRunner.Run at
        // commandArgv[0] with IndexOutOfRangeException.
        ArgParser.Result r = ArgParser.Parse(new[] { "github" });
        Assert.NotNull(r.Error);
        Assert.Contains("requires a namespace and a command", r.Error!);
    }

    [Fact]
    public void FlagMode_UnknownFlag_ReportsShellKitError()
    {
        // Regression for ParseFlagMode's `if (parsed.HasErrors)` branch (ArgParser.cs:131-134).
        // ShellKit catches unknown flags and puts the error on parsed.Errors. Without this test,
        // a regression that dropped the HasErrors branch would silently produce a bogus Options.
        ArgParser.Result r = ArgParser.Parse(new[] { "--set", "--not-a-flag", "ns", "K" });
        Assert.NotNull(r.Error);
        Assert.Equal(Yort.ShellKit.ExitCode.UsageError, r.ExitCode);
    }

    [Fact]
    public void BareHelp_RoutesThroughShellKit_IsHandled()
    {
        // Regression: --help in isolation must go through ShellKit (StandardFlags), not a hand-rolled
        // shim. Before 2026-04-22 Program.cs short-circuited --help/--version/--describe with its own
        // hardcoded text; that drifted from the parser's advertised flags and exit codes. The fix
        // routes introspection flags into flag mode so ShellKit is the single source of truth.
        ArgParser.Result r = ArgParser.Parse(new[] { "--help" });
        Assert.True(r.IsHandled);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void BareVersion_RoutesThroughShellKit_IsHandled()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--version" });
        Assert.True(r.IsHandled);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void BareDescribe_RoutesThroughShellKit_IsHandled()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "--describe" });
        Assert.True(r.IsHandled);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void BareDashH_RoutesThroughShellKit_IsHandled()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "-h" });
        Assert.True(r.IsHandled);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void NoColorEnvVar_DisablesColor()
    {
        // CLAUDE.md: "Respect NO_COLOR env var (no-color.org)". ArgParser.DetectColorFromEnv and
        // ShellKit's ResolveColor both honour it. Set it, re-parse an error path that goes through
        // the pre-parse color path (empty argv), confirm UseColor flips false.
        string? prior = System.Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            System.Environment.SetEnvironmentVariable("NO_COLOR", "1");
            ArgParser.Result r = ArgParser.Parse(System.Array.Empty<string>());
            Assert.False(r.UseColor);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("NO_COLOR", prior);
        }
    }

    [Fact]
    public void ExecMode_CommaSeparatorsAllEmpty_Error()
    {
        // argv[0] is all commas → namespaces after Split+RemoveEmptyEntries is empty → error.
        ArgParser.Result r = ArgParser.Parse(new[] { ",,,", "cmd" });
        Assert.NotNull(r.Error);
        Assert.Contains("empty", r.Error!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueConsumesNextToken_ActionFlagAsValue_NotMisdetectedAsAction()
    {
        // Regression: `envvault --value --set ns K` means --set is the VALUE of --value. The
        // pre-scan must exclude value-consumed tokens from the action-flag scan — otherwise it
        // dispatches to flag mode believing --set was an action, but ShellKit's own parse treats
        // --set as the value for --value, so the two views disagree.
        //
        // With the fix, there's no action flag in the unconsumed leading region, so we fall
        // through to exec mode (with --value treated as an unusual but legal namespace string).
        // The critical invariant: we do NOT dispatch to SubCommand.Set.
        ArgParser.Result r = ArgParser.Parse(new[] { "--value", "--set", "ns", "K" });
        Assert.NotNull(r.Options);
        Assert.NotEqual(SubCommand.Set, r.Options!.SubCommand);
        Assert.Equal(SubCommand.Exec, r.Options.SubCommand);
    }

    [Fact]
    public void HelpAfterNamespace_BelongsToChildCommand_ExecMode()
    {
        // `envvault github gh --help` — --help is after the first positional so it belongs to the
        // downstream command (gh), not envvault. Must still dispatch to exec mode.
        ArgParser.Result r = ArgParser.Parse(new[] { "github", "gh", "--help" });
        Assert.False(r.IsHandled);
        Assert.Null(r.Error);
        Assert.NotNull(r.Options);
        Assert.Equal(SubCommand.Exec, r.Options!.SubCommand);
        Assert.Equal(new[] { "gh", "--help" }, r.Options.CommandArgv);
    }
}
