#nullable enable
using System.IO;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class FileSinkTests
{
    [Fact]
    public void Write_Truncate_OverwritesAndWritesLines()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "pre-existing\n");
        try
        {
            var sink = new FileSink(path, "p", append: false);
            sink.Write("one");
            sink.Write("two");
            sink.Close();

            // Raw-byte compare (no Replace mask): a regression to WriteLine would emit CRLF and fail here.
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes("one\ntwo\n"), File.ReadAllBytes(path));
            Assert.Equal(2, sink.DeliveredCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_Append_PreservesExistingContent()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "old\n");
        try
        {
            var sink = new FileSink(path, "p", append: true);
            sink.Write("new");
            sink.Close();

            // Raw-byte compare (no Replace mask): append must preserve LF-only bytes end to end.
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes("old\nnew\n"), File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Constructor_UnopenablePath_Throws()
    {
        string bad = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "nope.log"); // missing dir
        Assert.ThrowsAny<IOException>(() => new FileSink(bad, "p", append: false));
    }

    /// <summary>
    /// D13 newline-policy: FileSink must write exactly LF ('\n'), never CRLF.
    /// Uses raw bytes so a regression to WriteLine (which emits CRLF on Windows) is
    /// caught immediately — no Replace mask.
    /// </summary>
    [Fact]
    public void Write_PreservesLf_DoesNotEmitCrlf()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var sink = new FileSink(path, "p", append: false);
            sink.Write("line");
            sink.Close();

            byte[] bytes = File.ReadAllBytes(path);
            // Content must be exactly the UTF-8 bytes for "line\n" — no \r anywhere.
            Assert.Equal(new byte[] { (byte)'l', (byte)'i', (byte)'n', (byte)'e', (byte)'\n' }, bytes);
            Assert.DoesNotContain((byte)'\r', bytes);
        }
        finally { File.Delete(path); }
    }
}
