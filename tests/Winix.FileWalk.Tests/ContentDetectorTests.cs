using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

/// <summary>
/// Round-2 fresh-eyes 2026-05-09 contract update (silent-failure-hunter C1):
/// <see cref="ContentDetector.IsTextFile"/> now returns <c>bool?</c> (null on read
/// failure) with a <c>readError</c> out param, instead of conflating "binary" with
/// "couldn't read." Existing assertions migrate from <c>Assert.True/False</c> to
/// nullable comparisons; the non-existent-file case migrates from "returns false"
/// to "returns null with a populated read-error."
/// </summary>
public class ContentDetectorTests
{
    [Fact]
    public void IsTextFile_PlainTextContent_ReturnsTrue()
    {
        string path = CreateTempFile("Hello, world!\nThis is a text file.\n");
        try { Assert.Equal(true, ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_BinaryContent_ReturnsFalse()
    {
        string path = CreateTempFileBytes(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });
        try { Assert.Equal(false, ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_EmptyFile_ReturnsTrue()
    {
        string path = CreateTempFile("");
        try { Assert.Equal(true, ContentDetector.IsTextFile(path)); }
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
        try { Assert.Equal(true, ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_NullByteInMiddle_ReturnsFalse()
    {
        byte[] content = System.Text.Encoding.UTF8.GetBytes("Hello\0World");
        string path = CreateTempFileBytes(content);
        try { Assert.Equal(false, ContentDetector.IsTextFile(path)); }
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
        try { Assert.Equal(true, ContentDetector.IsTextFile(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsTextFile_NonExistentFile_ReturnsNullWithReadError()
    {
        // Round-2 contract change (SFH C1 re-opened): non-existent files were previously
        // collapsed to "false" (binary), masking the read failure. Now the failure is
        // surfaced via null-return + a populated readError so callers can route to
        // a walk-error rather than misclassify.
        bool? result = ContentDetector.IsTextFile("/nonexistent/file/path.txt", out string? readError);

        Assert.Null(result);
        Assert.NotNull(readError);
        // The exact exception type depends on whether the missing element is the file
        // (FileNotFoundException) or a parent directory (DirectoryNotFoundException);
        // both are IOException-derived. Either is acceptable for the read-failure
        // contract — what matters is that readError is populated with a recognisable
        // exception type name (not localised SR-key text).
        Assert.True(
            readError!.Contains("FileNotFoundException", StringComparison.Ordinal)
            || readError.Contains("DirectoryNotFoundException", StringComparison.Ordinal),
            $"readError should name a not-found exception type; got: {readError}");
    }

    [Fact]
    public void IsTextFile_NoOutOverload_DiscardsReadError()
    {
        // Convenience overload used where the failure detail isn't needed (e.g.
        // ad-hoc calls). Still returns null on failure.
        Assert.Null(ContentDetector.IsTextFile("/nonexistent/file/path.txt"));
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
