#nullable enable

using Winix.Less;
using Xunit;

namespace Winix.Less.Tests;

public class LessOptionsTests
{
    // 1. Null env, no flags → modern defaults
    [Fact]
    public void Defaults_WhenNoEnvAndNoFlags_ModernDefaults()
    {
        var opts = LessOptions.Resolve([], null);

        Assert.True(opts.QuitIfOneScreen);
        Assert.True(opts.RawAnsi);
        Assert.True(opts.NoClearOnExit);
        Assert.False(opts.ShowLineNumbers);
        Assert.False(opts.ChopLongLines);
        Assert.False(opts.IgnoreCase);
        Assert.False(opts.ForceIgnoreCase);
        Assert.False(opts.FollowOnStart);
        Assert.False(opts.StartAtEnd);
        Assert.Null(opts.InitialSearch);
    }

    // 2. Empty env string behaves the same as unset (null)
    [Fact]
    public void Defaults_EmptyEnvVar_SameAsUnset()
    {
        var opts = LessOptions.Resolve([], "");

        Assert.True(opts.QuitIfOneScreen);
        Assert.True(opts.RawAnsi);
        Assert.True(opts.NoClearOnExit);
        Assert.False(opts.ShowLineNumbers);
        Assert.False(opts.ChopLongLines);
    }

    // 3. Non-empty env replaces defaults entirely (all-false baseline, then parsed flags applied)
    [Fact]
    public void EnvVar_ReplacesDefaults()
    {
        // "N" only → ShowLineNumbers=true; no F, R, X → those become false
        var opts = LessOptions.Resolve([], "N");

        Assert.True(opts.ShowLineNumbers);
        Assert.False(opts.QuitIfOneScreen);
        Assert.False(opts.RawAnsi);
        Assert.False(opts.NoClearOnExit);
    }

    // 4. Multiple flags in env are all respected
    [Fact]
    public void EnvVar_MultipleFlagsRespected()
    {
        var opts = LessOptions.Resolve([], "FRXNi");

        Assert.True(opts.QuitIfOneScreen);
        Assert.True(opts.RawAnsi);
        Assert.True(opts.NoClearOnExit);
        Assert.True(opts.ShowLineNumbers);
        Assert.True(opts.IgnoreCase);
        Assert.False(opts.ChopLongLines);
    }

    // 5. Unknown chars in env are silently ignored — no exception
    [Fact]
    public void EnvVar_UnknownFlagsIgnored()
    {
        var opts = LessOptions.Resolve([], "FRXqZM");

        Assert.True(opts.QuitIfOneScreen);
        Assert.True(opts.RawAnsi);
        Assert.True(opts.NoClearOnExit);
        // No exception thrown — that's the key assertion
    }

    // 6. CLI flags override env var settings
    [Fact]
    public void CliFlags_OverrideEnvVar()
    {
        // env "N" → N=true, F=false, R=false, X=false
        // CLI "-F" → QuitIfOneScreen flipped to true
        var opts = LessOptions.Resolve(["-F"], "N");

        Assert.True(opts.ShowLineNumbers);      // from env
        Assert.True(opts.QuitIfOneScreen);      // from CLI
    }

    // 7. CLI flags override defaults; untouched defaults remain
    [Fact]
    public void CliFlags_OverrideDefaults()
    {
        var opts = LessOptions.Resolve(["-N", "-S"], null);

        Assert.True(opts.ShowLineNumbers);      // CLI
        Assert.True(opts.ChopLongLines);        // CLI
        Assert.True(opts.QuitIfOneScreen);      // default still on
    }

    // 8. +F (tail-follow) sets FollowOnStart
    [Fact]
    public void CliFlags_PlusF_SetsFollowOnStart()
    {
        var opts = LessOptions.Resolve(["+F"], null);

        Assert.True(opts.FollowOnStart);
    }

    // 9. +G (start at end) sets StartAtEnd
    [Fact]
    public void CliFlags_PlusG_SetsStartAtEnd()
    {
        var opts = LessOptions.Resolve(["+G"], null);

        Assert.True(opts.StartAtEnd);
    }

    // 10. +/pattern sets InitialSearch
    [Fact]
    public void CliFlags_PlusSlashPattern_SetsInitialSearch()
    {
        var opts = LessOptions.Resolve(["+/error"], null);

        Assert.Equal("error", opts.InitialSearch);
    }

    // 11. -i → smart-case (IgnoreCase=true, ForceIgnoreCase=false)
    [Fact]
    public void CliFlags_CaseSensitivity_SmartCase()
    {
        var opts = LessOptions.Resolve(["-i"], null);

        Assert.True(opts.IgnoreCase);
        Assert.False(opts.ForceIgnoreCase);
    }

    // 12. -I → force-ignore-case (ForceIgnoreCase=true)
    [Fact]
    public void CliFlags_CaseSensitivity_ForceIgnore()
    {
        var opts = LessOptions.Resolve(["-I"], null);

        Assert.True(opts.ForceIgnoreCase);
    }
}
