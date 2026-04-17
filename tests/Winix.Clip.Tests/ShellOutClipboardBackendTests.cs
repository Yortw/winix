using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

public class ShellOutClipboardBackendTests
{
    [Fact]
    public void Copy_InvokesCopyCommand_WithStdinPayload()
    {
        var runner = new FakeProcessRunner();
        var backend = new ShellOutClipboardBackend(HelperSets.XClip, runner);

        backend.CopyText("hello");

        Assert.Single(runner.Invocations);
        Assert.Equal("xclip", runner.Invocations[0].File);
        Assert.Equal(new[] { "-selection", "clipboard", "-i" }, runner.Invocations[0].Args);
        Assert.Equal("hello", runner.Invocations[0].Stdin);
    }

    [Fact]
    public void Paste_InvokesPasteCommand_ReturnsStdout()
    {
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(new ProcessRunResult(0, "clipboard contents", string.Empty));
        var backend = new ShellOutClipboardBackend(HelperSets.XClip, runner);

        string result = backend.PasteText();

        Assert.Equal("clipboard contents", result);
        Assert.Equal("xclip", runner.Invocations[0].File);
        Assert.Equal(new[] { "-selection", "clipboard", "-o" }, runner.Invocations[0].Args);
        Assert.Null(runner.Invocations[0].Stdin);
    }

    [Fact]
    public void Clear_XclipVariant_InvokesCopyWithEmptyStdin()
    {
        var runner = new FakeProcessRunner();
        var backend = new ShellOutClipboardBackend(HelperSets.XClip, runner);

        backend.Clear();

        Assert.Single(runner.Invocations);
        Assert.Equal("xclip", runner.Invocations[0].File);
        Assert.Equal(new[] { "-selection", "clipboard", "-i" }, runner.Invocations[0].Args);
        Assert.Equal(string.Empty, runner.Invocations[0].Stdin);
    }

    [Fact]
    public void Clear_WlClipboardVariant_UsesWlCopyClear()
    {
        var runner = new FakeProcessRunner();
        var backend = new ShellOutClipboardBackend(HelperSets.WlClipboard, runner);

        backend.Clear();

        Assert.Single(runner.Invocations);
        Assert.Equal("wl-copy", runner.Invocations[0].File);
        Assert.Equal(new[] { "--clear" }, runner.Invocations[0].Args);
        Assert.Null(runner.Invocations[0].Stdin);
    }

    [Fact]
    public void Clear_XselVariant_UsesXselClear()
    {
        var runner = new FakeProcessRunner();
        var backend = new ShellOutClipboardBackend(HelperSets.XSel, runner);

        backend.Clear();

        Assert.Single(runner.Invocations);
        Assert.Equal("xsel", runner.Invocations[0].File);
        Assert.Equal(new[] { "--clipboard", "--clear" }, runner.Invocations[0].Args);
    }

    [Fact]
    public void Primary_Xclip_SwapsSelection()
    {
        var runner = new FakeProcessRunner();
        var backend = new ShellOutClipboardBackend(HelperSets.XClip.WithPrimary(), runner);

        backend.CopyText("x");

        Assert.Equal(new[] { "-selection", "primary", "-i" }, runner.Invocations[0].Args);
    }

    [Fact]
    public void Primary_Xsel_SwapsSelection()
    {
        var runner = new FakeProcessRunner();
        var backend = new ShellOutClipboardBackend(HelperSets.XSel.WithPrimary(), runner);

        backend.CopyText("x");

        Assert.Equal(new[] { "--primary", "--input" }, runner.Invocations[0].Args);
    }

    [Fact]
    public void Primary_WlClipboard_AddsPrimaryFlag()
    {
        var runner = new FakeProcessRunner();
        var backend = new ShellOutClipboardBackend(HelperSets.WlClipboard.WithPrimary(), runner);

        backend.PasteText();

        Assert.Equal(new[] { "--no-newline", "--primary" }, runner.Invocations[0].Args);
    }

    [Fact]
    public void Primary_Pb_Ignored()
    {
        var runner = new FakeProcessRunner();
        var backend = new ShellOutClipboardBackend(HelperSets.Pb.WithPrimary(), runner);

        backend.CopyText("x");

        // pbcopy has no primary concept — args vector unchanged.
        Assert.Empty(runner.Invocations[0].Args);
    }

    [Fact]
    public void NonZeroExit_RaisesClipboardException_WithStderrMessage()
    {
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(new ProcessRunResult(1, string.Empty, "xclip: error: Can't open display"));
        var backend = new ShellOutClipboardBackend(HelperSets.XClip, runner);

        var ex = Assert.Throws<ClipboardException>(() => backend.PasteText());
        Assert.Contains("xclip", ex.Message);
        Assert.Contains("Can't open display", ex.Message);
    }
}
