#nullable enable
using System;
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class ArgParserTests
{
    [Fact]
    public void Parse_NoArgs_Errors()
    {
        var r = ArgParser.Parse(System.Array.Empty<string>());
        Assert.False(r.Success);
        Assert.Contains("TITLE is required", r.Error);
    }

    [Fact]
    public void Parse_TitleOnly_Succeeds()
    {
        var r = ArgParser.Parse(new[] { "hello" });
        Assert.True(r.Success);
        Assert.Equal("hello", r.Options!.Title);
        Assert.Null(r.Options.Body);
    }

    [Fact]
    public void Parse_TitleAndBody_BothPopulated()
    {
        var r = ArgParser.Parse(new[] { "hello", "world" });
        Assert.True(r.Success);
        Assert.Equal("hello", r.Options!.Title);
        Assert.Equal("world", r.Options.Body);
    }

    [Fact]
    public void Parse_TooManyPositionals_Errors()
    {
        var r = ArgParser.Parse(new[] { "a", "b", "c" });
        Assert.False(r.Success);
        Assert.Contains("at most TITLE and BODY", r.Error);
    }

    [Theory]
    [InlineData("low", Urgency.Low)]
    [InlineData("normal", Urgency.Normal)]
    [InlineData("critical", Urgency.Critical)]
    public void Parse_UrgencyFlag_MapsCorrectly(string value, Urgency expected)
    {
        var r = ArgParser.Parse(new[] { "--urgency", value, "hi" });
        Assert.True(r.Success);
        Assert.Equal(expected, r.Options!.Urgency);
    }

    [Fact]
    public void Parse_UrgencyDefault_IsNormal()
    {
        var r = ArgParser.Parse(new[] { "hi" });
        Assert.Equal(Urgency.Normal, r.Options!.Urgency);
    }

    [Fact]
    public void Parse_UrgencyUnknown_Errors()
    {
        var r = ArgParser.Parse(new[] { "--urgency", "shouty", "hi" });
        Assert.False(r.Success);
        Assert.Contains("unknown --urgency", r.Error);
    }

    [Fact]
    public void Parse_IconFlag_PopulatesOption()
    {
        var r = ArgParser.Parse(new[] { "--icon", "/tmp/i.png", "hi" });
        Assert.True(r.Success);
        Assert.Equal("/tmp/i.png", r.Options!.IconPath);
    }

    [Fact]
    public void Parse_NtfyTopic_EnablesNtfy()
    {
        var r = ArgParser.Parse(new[] { "--ntfy", "alerts", "hi" });
        Assert.True(r.Success);
        Assert.True(r.Options!.NtfyEnabled);
        Assert.Equal("alerts", r.Options.NtfyTopic);
    }

    [Fact]
    public void Parse_NtfyServerOverride_AppliedWhenNtfyEnabled()
    {
        var r = ArgParser.Parse(new[] { "--ntfy", "alerts", "--ntfy-server", "https://ntfy.example.com", "hi" });
        Assert.True(r.Success);
        Assert.Equal("https://ntfy.example.com", r.Options!.NtfyServer);
    }

    [Fact]
    public void Parse_NtfyServer_DefaultIsNtfySh()
    {
        var r = ArgParser.Parse(new[] { "--ntfy", "alerts", "hi" });
        Assert.Equal("https://ntfy.sh", r.Options!.NtfyServer);
    }

    [Fact]
    public void Parse_NtfyToken_PopulatesOption()
    {
        var r = ArgParser.Parse(new[] { "--ntfy", "alerts", "--ntfy-token", "tk_abc", "hi" });
        Assert.True(r.Success);
        Assert.Equal("tk_abc", r.Options!.NtfyToken);
    }

    [Fact]
    public void Parse_NtfyEnvFallback_TopicComesFromEnv()
    {
        Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", "envtopic");
        try
        {
            var r = ArgParser.Parse(new[] { "hi" });
            Assert.True(r.Success);
            Assert.True(r.Options!.NtfyEnabled);
            Assert.Equal("envtopic", r.Options.NtfyTopic);
        }
        finally { Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", null); }
    }

    [Fact]
    public void Parse_NtfyServerEnvFallback_AppliedWhenNoFlag()
    {
        Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", "envtopic");
        Environment.SetEnvironmentVariable("NOTIFY_NTFY_SERVER", "https://envserver.example.com");
        try
        {
            var r = ArgParser.Parse(new[] { "hi" });
            Assert.True(r.Success);
            Assert.Equal("https://envserver.example.com", r.Options!.NtfyServer);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", null);
            Environment.SetEnvironmentVariable("NOTIFY_NTFY_SERVER", null);
        }
    }

    [Fact]
    public void Parse_DefaultsTo_DesktopOnly()
    {
        var r = ArgParser.Parse(new[] { "hi" });
        Assert.True(r.Options!.DesktopEnabled);
        Assert.False(r.Options.NtfyEnabled);
    }

    [Fact]
    public void Parse_NoDesktop_DisablesDesktop()
    {
        var r = ArgParser.Parse(new[] { "--no-desktop", "--ntfy", "alerts", "hi" });
        Assert.True(r.Success);
        Assert.False(r.Options!.DesktopEnabled);
        Assert.True(r.Options.NtfyEnabled);
    }

    [Fact]
    public void Parse_NoNtfy_DisablesNtfy_EvenIfEnvSet()
    {
        Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", "envtopic");
        try
        {
            var r = ArgParser.Parse(new[] { "--no-ntfy", "hi" });
            Assert.True(r.Success);
            Assert.False(r.Options!.NtfyEnabled);
        }
        finally { Environment.SetEnvironmentVariable("NOTIFY_NTFY_TOPIC", null); }
    }

    [Fact]
    public void Parse_NoBackendsConfigured_Errors()
    {
        var r = ArgParser.Parse(new[] { "--no-desktop", "hi" });
        Assert.False(r.Success);
        Assert.Contains("no backends configured", r.Error);
    }

    [Fact]
    public void Parse_Strict_PopulatesFlag()
    {
        var r = ArgParser.Parse(new[] { "--strict", "hi" });
        Assert.True(r.Options!.Strict);
    }

    [Fact]
    public void Parse_Json_PopulatesFlag()
    {
        var r = ArgParser.Parse(new[] { "--json", "hi" });
        Assert.True(r.Options!.Json);
    }
}
