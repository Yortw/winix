using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class ContentDetectorTests
{
    [Fact]
    public void IsTextFile_PlainTextContent_ReturnsTrue()
    {
        string path = CreateTempFile("Hello, world!\nThis is a text file.\n");
        try { Assert.True(ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_BinaryContent_ReturnsFalse()
    {
        string path = CreateTempFileBytes(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });
        try { Assert.False(ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_EmptyFile_ReturnsTrue()
    {
        string path = CreateTempFile("");
        try { Assert.True(ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_TextWithUtf8Bom_ReturnsTrue()
    {
        byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
        byte[] content = System.Text.Encoding.UTF8.GetBytes("Hello UTF-8");
        byte[] full = new byte[bom.Length + content.Length];
        bom.CopyTo(full, 0);
        content.CopyTo(full, bom.Length);
        string path = CreateTempFileBytes(full);
        try { Assert.True(ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_NullByteInMiddle_ReturnsFalse()
    {
        byte[] content = System.Text.Encoding.UTF8.GetBytes("Hello\0World");
        string path = CreateTempFileBytes(content);
        try { Assert.False(ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_LargeTextFile_OnlyReadsFirst8KB()
    {
        byte[] textPart = new byte[8192];
        Array.Fill(textPart, (byte)'A');
        byte[] full = new byte[8193];
        textPart.CopyTo(full, 0);
        full[8192] = 0x00;
        string path = CreateTempFileBytes(full);
        try { Assert.True(ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_NonExistentFile_ReturnsFalse()
    {
        Assert.False(ContentDetector.IsTextFile("/nonexistent/file/path.txt"));
    }

    private static string CreateTempFile(string content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }

    private static string CreateTempFileBytes(byte[] content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        return path;
    }
}
