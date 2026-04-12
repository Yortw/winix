#nullable enable

using System;
using System.IO;
using Winix.Less;
using Xunit;

namespace Winix.Less.Tests;

public class InputSourceTests : IDisposable
{
    private readonly string _tempDir;

    public InputSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lesstest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    // 1. Read a file with 3 lines — all lines returned with correct content
    [Fact]
    public void FromFile_ReadsAllLines()
    {
        var path = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(path, "line one\nline two\nline three");

        var source = InputSource.FromFile(path);

        Assert.Equal(3, source.Lines.Count);
        Assert.Equal("line one", source.Lines[0]);
        Assert.Equal("line two", source.Lines[1]);
        Assert.Equal("line three", source.Lines[2]);
    }

    // 2. Name property returns the file path
    [Fact]
    public void FromFile_Name_ReturnsFilePath()
    {
        var path = Path.Combine(_tempDir, "named.txt");
        File.WriteAllText(path, "content");

        var source = InputSource.FromFile(path);

        Assert.Equal(path, source.Name);
    }

    // 3. Empty file returns exactly one empty line
    [Fact]
    public void FromFile_EmptyFile_ReturnsOneEmptyLine()
    {
        var path = Path.Combine(_tempDir, "empty.txt");
        File.WriteAllText(path, "");

        var source = InputSource.FromFile(path);

        var line = Assert.Single(source.Lines);
        Assert.Equal("", line);
    }

    // 4. Windows line endings are split correctly and no \r remains
    [Fact]
    public void FromFile_WindowsLineEndings_SplitsCorrectly()
    {
        var path = Path.Combine(_tempDir, "crlf.txt");
        File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes("one\r\ntwo\r\nthree"));

        var source = InputSource.FromFile(path);

        Assert.Equal(3, source.Lines.Count);
        Assert.Equal("one", source.Lines[0]);
        Assert.Equal("two", source.Lines[1]);
        Assert.Equal("three", source.Lines[2]);
    }

    // 5. FromString loads lines from content, using the supplied name
    [Fact]
    public void FromString_LoadsLinesFromContent()
    {
        var source = InputSource.FromString("alpha\nbeta\ngamma", "(test)");

        Assert.Equal(3, source.Lines.Count);
        Assert.Equal("alpha", source.Lines[0]);
        Assert.Equal("beta", source.Lines[1]);
        Assert.Equal("gamma", source.Lines[2]);
        Assert.Equal("(test)", source.Name);
    }

    // 6. FromFile throws FileNotFoundException for a non-existent path
    [Fact]
    public void FromFile_NonExistent_Throws()
    {
        var path = Path.Combine(_tempDir, "does-not-exist.txt");

        Assert.Throws<FileNotFoundException>(() => InputSource.FromFile(path));
    }

    // 7. PollForNewContent returns true and exposes the new line when the file has grown
    [Fact]
    public void PollForNewContent_FileGrew_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "growing.txt");
        File.WriteAllText(path, "line one\n");

        var source = InputSource.FromFile(path);

        // Append a second line after the source was created
        File.AppendAllText(path, "line two\n");

        var grew = source.PollForNewContent();

        Assert.True(grew);
        Assert.Equal(2, source.Lines.Count);
        Assert.Equal("line two", source.Lines[1]);
    }

    // 8. PollForNewContent returns false when the file has not changed
    [Fact]
    public void PollForNewContent_FileUnchanged_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "static.txt");
        File.WriteAllText(path, "line one\n");

        var source = InputSource.FromFile(path);

        var grew = source.PollForNewContent();

        Assert.False(grew);
    }
}
