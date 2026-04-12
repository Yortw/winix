#nullable enable

using System;
using System.IO;
using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

public sealed class ArgumentParserTests
{
    [Fact]
    public void Parse_ColonPrefix_ReturnsPort()
    {
        var result = ArgumentParser.Parse(":8080");

        Assert.True(result.IsPort);
        Assert.Equal(8080, result.Port);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public void Parse_ColonPrefixZero_ReturnsError()
    {
        var result = ArgumentParser.Parse(":0");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ColonPrefixNegative_ReturnsError()
    {
        var result = ArgumentParser.Parse(":-1");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ColonPrefixTooLarge_ReturnsError()
    {
        var result = ArgumentParser.Parse(":99999");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ColonPrefixNotANumber_ReturnsError()
    {
        var result = ArgumentParser.Parse(":abc");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ExistingFile_ReturnsFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            var result = ArgumentParser.Parse(path);

            Assert.True(result.IsFile);
            Assert.Equal(path, result.FilePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_ExistingDirectory_ReturnsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        try
        {
            var result = ArgumentParser.Parse(path);

            Assert.True(result.IsFile);
            Assert.Equal(path, result.FilePath);
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    [Fact]
    public void Parse_BareNumber_NoSuchFile_ReturnsPort()
    {
        // Uses a port number unlikely to correspond to a real file on disk.
        var result = ArgumentParser.Parse("8080");

        Assert.True(result.IsPort);
        Assert.Equal(8080, result.Port);
    }

    [Fact]
    public void Parse_BareNumber_FileExists_ReturnsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "8080");
        File.WriteAllText(path, "");
        try
        {
            var result = ArgumentParser.Parse(path);

            Assert.True(result.IsFile);
            Assert.Equal(path, result.FilePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_NonExistentFile_ReturnsError()
    {
        var result = ArgumentParser.Parse("/no/such/file/ever.dll");

        Assert.True(result.IsError);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsError()
    {
        var result = ArgumentParser.Parse("");

        Assert.True(result.IsError);
    }

    [Fact]
    public void Parse_BareNumberPortZero_ReturnsError()
    {
        // "0" is not a valid port and no file named "0" should exist.
        var result = ArgumentParser.Parse("0");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_BareNumberPortTooLarge_ReturnsError()
    {
        // "70000" is not a valid port and no file named "70000" should exist.
        var result = ArgumentParser.Parse("70000");

        Assert.True(result.IsError);
        Assert.Contains("invalid port", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
