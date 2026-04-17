using Winix.Clip;

namespace Winix.Clip.Tests;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessRunResult> _responses = new();
    public List<(string File, string[] Args, string? Stdin)> Invocations { get; } = new();

    public void EnqueueResult(ProcessRunResult result) => _responses.Enqueue(result);

    public ProcessRunResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin)
    {
        Invocations.Add((fileName, arguments.ToArray(), stdin));
        return _responses.Count > 0
            ? _responses.Dequeue()
            : new ProcessRunResult(0, string.Empty, string.Empty);
    }
}
