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
}
