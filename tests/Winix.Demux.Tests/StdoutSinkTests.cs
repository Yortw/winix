#nullable enable
using System.IO;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class StdoutSinkTests
{
    [Fact]
    public void Write_AppendsLinesAndCountsDelivered()
    {
        var sw = new StringWriter();
        var sink = new StdoutSink(sw);

        sink.Write("alpha");
        sink.Write("beta");

        Assert.Equal("alpha\nbeta\n", sw.ToString().Replace("\r\n", "\n"));
        Assert.Equal(2, sink.DeliveredCount);
        Assert.Equal(0, sink.UndeliveredCount);
        Assert.False(sink.IsDead);
    }

    [Fact]
    public void Write_OnBrokenPipe_MarksDeadAndCountsUndelivered()
    {
        var sink = new StdoutSink(new ThrowingWriter());

        sink.Write("x");
        sink.Write("y");

        Assert.True(sink.IsDead);
        Assert.Equal(0, sink.DeliveredCount);
        Assert.Equal(2, sink.UndeliveredCount);
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        // StdoutSink calls _writer.Write(string) and _writer.Write('\n') — never WriteLine.
        // Both of those funnel through Write(char) at the TextWriter base level, so overriding
        // Write(char) is the single point we must throw from to exercise the broken-pipe catch.
        public override void Write(char value) => throw new IOException("broken pipe");
    }
}
