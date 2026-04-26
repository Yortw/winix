using System.IO;
using Winix.Peep;
using Xunit;

namespace Winix.Peep.Tests;

/// <summary>
/// Pins the alt-screen-buffer corruption-safety contract: <see cref="ScreenRenderer.EnterAlternateBuffer"/>
/// and <see cref="ScreenRenderer.ExitAlternateBuffer"/> must NOT propagate IOException
/// or ObjectDisposedException from the underlying writer. If they did, an exception
/// during teardown would leak past <c>InteractiveSession.RunAsync</c>'s outer finally
/// and leave the user's terminal stuck in alternate-buffer mode with the cursor
/// hidden for the rest of the shell session.
/// </summary>
public class ScreenRendererBufferGuardTests
{
    /// <summary>
    /// TextWriter whose Write methods unconditionally throw the configured exception.
    /// Models a stdout that's gone away (broken pipe, closed handle).
    /// </summary>
    private sealed class FaultingWriter : TextWriter
    {
        private readonly Func<Exception> _exceptionFactory;
        public FaultingWriter(Func<Exception> exceptionFactory) { _exceptionFactory = exceptionFactory; }
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) => throw _exceptionFactory();
        public override void Write(string? value) => throw _exceptionFactory();
        public override void Flush() => throw _exceptionFactory();
    }

    [Fact]
    public void EnterAlternateBuffer_WriterThrowsIOException_DoesNotPropagate()
    {
        var writer = new FaultingWriter(() => new IOException("broken pipe"));
        var ex = Record.Exception(() => ScreenRenderer.EnterAlternateBuffer(writer));
        Assert.Null(ex);
    }

    [Fact]
    public void EnterAlternateBuffer_WriterThrowsObjectDisposedException_DoesNotPropagate()
    {
        var writer = new FaultingWriter(() => new ObjectDisposedException("stdout"));
        var ex = Record.Exception(() => ScreenRenderer.EnterAlternateBuffer(writer));
        Assert.Null(ex);
    }

    [Fact]
    public void ExitAlternateBuffer_WriterThrowsIOException_DoesNotPropagate()
    {
        var writer = new FaultingWriter(() => new IOException("broken pipe"));
        var ex = Record.Exception(() => ScreenRenderer.ExitAlternateBuffer(writer));
        Assert.Null(ex);
    }

    [Fact]
    public void ExitAlternateBuffer_WriterThrowsObjectDisposedException_DoesNotPropagate()
    {
        var writer = new FaultingWriter(() => new ObjectDisposedException("stdout"));
        var ex = Record.Exception(() => ScreenRenderer.ExitAlternateBuffer(writer));
        Assert.Null(ex);
    }

    [Fact]
    public void EnterAlternateBuffer_WriterThrowsUnexpectedException_DoesPropagate()
    {
        // Unexpected exception types (NotSupportedException, InvalidOperationException, etc.)
        // are likely real bugs and MUST propagate, not be silently swallowed. The catch is
        // narrow on purpose — only IOException / ObjectDisposedException = "stdout is gone".
        var writer = new FaultingWriter(() => new InvalidOperationException("synthetic bug"));
        Assert.Throws<InvalidOperationException>(() => ScreenRenderer.EnterAlternateBuffer(writer));
    }
}
