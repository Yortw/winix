#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Winix.EnvVault;

namespace Winix.EnvVault.Tests.Fakes;

public sealed class FakeConsolePrompt : IConsolePrompt
{
    private readonly Queue<string> _ttyValues;
    private readonly Queue<string?> _stdinValues;

    public bool IsInteractive { get; }
    public List<string> PromptsWritten { get; } = new();

    /// <summary>When non-null, <see cref="ReadLineEchoOff"/> throws this exception on its next call instead of returning a value. Used to simulate Ctrl+C (OperationCanceledException) and other interactive-prompt failures.</summary>
    public Exception? ThrowOnNextEchoOff { get; set; }

    public FakeConsolePrompt(bool isInteractive, IEnumerable<string>? ttyValues = null, IEnumerable<string?>? stdinValues = null)
    {
        IsInteractive = isInteractive;
        _ttyValues = new Queue<string>(ttyValues ?? Enumerable.Empty<string>());
        _stdinValues = new Queue<string?>(stdinValues ?? Enumerable.Empty<string?>());
    }

    public void WritePrompt(string text) => PromptsWritten.Add(text);
    public string ReadLineEchoOff()
    {
        if (ThrowOnNextEchoOff is { } ex)
        {
            ThrowOnNextEchoOff = null;
            throw ex;
        }
        return _ttyValues.Dequeue();
    }
    public string? ReadLineFromStdin() => _stdinValues.Count == 0 ? null : _stdinValues.Dequeue();
}
