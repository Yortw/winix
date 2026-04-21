#nullable enable
using System.Linq;
using Winix.EnvVault;
using Winix.EnvVault.Tests.Fakes;
using Xunit;

namespace Winix.EnvVault.Tests;

public class ValuePromptTests
{
    [Fact]
    public void PromptForKeys_TtyMode_UsesEchoOffAndWritesPromptPerKey()
    {
        FakeConsolePrompt fake = new(isInteractive: true, ttyValues: new[] { "hunter2", "s3cret" });
        ValuePrompt prompt = new(fake);

        var values = prompt.PromptForKeys("github", new[] { "TOKEN", "USER" });

        Assert.Equal(new[] { ("TOKEN", "hunter2"), ("USER", "s3cret") }, values.ToArray());
        Assert.Equal(new[] { "github.TOKEN: ", "github.USER: " }, fake.PromptsWritten);
    }

    [Fact]
    public void PromptForKeys_StdinMode_ReadsOneValuePerLine()
    {
        FakeConsolePrompt fake = new(isInteractive: false, stdinValues: new string?[] { "hunter2", "s3cret" });
        ValuePrompt prompt = new(fake);

        var values = prompt.PromptForKeys("github", new[] { "TOKEN", "USER" });

        Assert.Equal(new[] { ("TOKEN", "hunter2"), ("USER", "s3cret") }, values.ToArray());
    }

    [Fact]
    public void PromptForKeys_StdinMode_EofBeforeAllKeys_Throws()
    {
        FakeConsolePrompt fake = new(isInteractive: false, stdinValues: new string?[] { "only-one" });
        ValuePrompt prompt = new(fake);

        var ex = Assert.Throws<System.IO.EndOfStreamException>(() =>
            prompt.PromptForKeys("github", new[] { "TOKEN", "USER" }).ToArray());
        Assert.Contains("USER", ex.Message);
    }
}
