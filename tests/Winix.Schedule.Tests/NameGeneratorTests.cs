#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class NameGeneratorTests
{
    [Fact]
    public void FromCommand_SimpleCommand_ReturnsName()
    {
        string name = NameGenerator.FromCommand("dotnet build");

        Assert.Equal("dotnet-build", name);
    }

    [Fact]
    public void FromCommand_PathCommand_StripsPath()
    {
        string name = NameGenerator.FromCommand("/usr/bin/curl http://localhost");

        Assert.Equal("curl", name);
    }

    [Fact]
    public void FromCommand_WindowsPath_StripsPathAndExtension()
    {
        string name = NameGenerator.FromCommand(@"C:\tools\backup.bat");

        Assert.Equal("backup", name);
    }

    [Fact]
    public void FromCommand_SingleWord_ReturnsLowercase()
    {
        string name = NameGenerator.FromCommand("MyApp");

        Assert.Equal("myapp", name);
    }

    [Fact]
    public void FromCommand_EmptyString_ReturnsDefault()
    {
        string name = NameGenerator.FromCommand("");

        Assert.Equal("task", name);
    }
}
