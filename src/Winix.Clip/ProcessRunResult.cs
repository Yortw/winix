namespace Winix.Clip;

/// <summary>
/// Outcome of running a helper process.
/// </summary>
/// <param name="ExitCode">The helper's exit code.</param>
/// <param name="Stdout">Captured stdout (may be empty).</param>
/// <param name="Stderr">Captured stderr (may be empty).</param>
public readonly record struct ProcessRunResult(int ExitCode, string Stdout, string Stderr);
