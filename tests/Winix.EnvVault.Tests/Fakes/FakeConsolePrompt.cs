#nullable enable
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

    public FakeConsolePrompt(bool isInteractive, IEnumerable<string>? ttyValues = null, IEnumerable<string?>? stdinValues = null)
    {
        IsInteractive = isInteractive;
        _ttyValues = new Queue<string>(ttyValues ?? Enumerable.Empty<string>());
        _stdinValues = new Queue<string?>(stdinValues ?? Enumerable.Empty<string?>());
    }

    public void WritePrompt(string text) => PromptsWritten.Add(text);
    public string ReadLineEchoOff() => _ttyValues.Dequeue();
    public string? ReadLineFromStdin() => _stdinValues.Count == 0 ? null : _stdinValues.Dequeue();
}
