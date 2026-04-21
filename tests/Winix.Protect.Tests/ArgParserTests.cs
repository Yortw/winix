#nullable enable
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class ArgParserTests
{
    [Fact]
    public void Parse_NoArgs_NullInputOutput_StreamingMode()
    {
        ArgParser.Result r = ArgParser.Parse([], SubCommand.Protect);
        Assert.Null(r.Error);
        Assert.Null(r.Options!.InputPath);
        Assert.Null(r.Options.OutputPath);
        Assert.False(r.Options.InPlace);
        Assert.False(r.Options.RemoveSource);
        Assert.Equal(Scope.User, r.Options.Scope);
        Assert.False(r.Options.NoVerify);
    }

    [Fact]
    public void Parse_FilePositional_SetsInputPath()
    {
        ArgParser.Result r = ArgParser.Parse(["file.txt"], SubCommand.Protect);
        Assert.Equal("file.txt", r.Options!.InputPath);
    }

    [Fact]
    public void Parse_OutputFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["in.txt", "-o", "out.prot"], SubCommand.Protect);
        Assert.Equal("in.txt", r.Options!.InputPath);
        Assert.Equal("out.prot", r.Options.OutputPath);
    }

    [Fact]
    public void Parse_InPlaceFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["file.txt", "--in-place"], SubCommand.Protect);
        Assert.True(r.Options!.InPlace);
    }

    [Fact]
    public void Parse_RemoveSourceFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["file.txt", "--rm"], SubCommand.Protect);
        Assert.True(r.Options!.RemoveSource);
    }

    [Fact]
    public void Parse_MachineScope()
    {
        ArgParser.Result r = ArgParser.Parse(["--scope", "machine"], SubCommand.Protect);
        Assert.Equal(Scope.Machine, r.Options!.Scope);
    }

    [Fact]
    public void Parse_NoVerifyFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["--no-verify"], SubCommand.Protect);
        Assert.True(r.Options!.NoVerify);
    }

    [Fact]
    public void Parse_InputEqualsOutput_Errors()
    {
        string abs = Path.Combine(Path.GetTempPath(), "x.txt");
        ArgParser.Result r = ArgParser.Parse([abs, "-o", abs], SubCommand.Protect);
        Assert.NotNull(r.Error);
        Assert.Contains("same", r.Error!);
    }

    [Fact]
    public void Parse_InPlaceAndOutput_Errors()
    {
        ArgParser.Result r = ArgParser.Parse(["file.txt", "--in-place", "-o", "other.prot"], SubCommand.Protect);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Parse_UnknownScope_Errors()
    {
        ArgParser.Result r = ArgParser.Parse(["--scope", "process"], SubCommand.Protect);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Parse_Help_Flag()
    {
        ArgParser.Result r = ArgParser.Parse(["--help"], SubCommand.Protect);
        Assert.True(r.ShowHelp);
    }

    [Fact]
    public void Parse_Version_Flag()
    {
        ArgParser.Result r = ArgParser.Parse(["--version"], SubCommand.Protect);
        Assert.True(r.ShowVersion);
    }

    [Fact]
    public void Parse_Describe_Flag()
    {
        ArgParser.Result r = ArgParser.Parse(["--describe"], SubCommand.Protect);
        Assert.True(r.ShowDescribe);
    }
}
