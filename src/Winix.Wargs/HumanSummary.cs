#nullable enable
using System.IO;
using Yort.ShellKit;

namespace Winix.Wargs;

/// <summary>
/// Resolves colour from the parsed arguments and emits the human-readable failure summary
/// to stderr. Extracted from <c>Program.cs</c> so the resolve→colour→emit wiring is
/// testable without a full process spawn (wargs has no <c>Cli.Run</c> library seam).
/// </summary>
public static class HumanSummary
{
    /// <summary>
    /// Resolves colour from <paramref name="parsed"/> (checking stderr's terminal state)
    /// and writes the human failure summary — if any — to <paramref name="stderr"/>.
    /// When there are no failures (<see cref="WargsResult.Failed"/> is zero) nothing is written.
    /// </summary>
    /// <param name="parsed">The <see cref="ParseResult"/> from the wargs command-line parser,
    /// used to resolve <c>--color</c> / <c>--no-color</c> / <c>NO_COLOR</c>.</param>
    /// <param name="result">The completed wargs run result.</param>
    /// <param name="stderr">The writer to emit the summary line to (typically <see cref="System.Console.Error"/>).</param>
    public static void Emit(ParseResult parsed, WargsResult result, TextWriter stderr)
    {
        bool useColor = parsed.ResolveColor(checkStdErr: true);
        string? summary = Formatting.FormatHumanSummary(result, useColor);
        if (summary is not null)
        {
            // The summary is a diagnostic stderr write — it must never fail the caller and mask the
            // real exit code (the suite-wide SafeWriteLine convention; CLAUDE.md "diagnostic logging
            // must never fail the caller"). Broken-pipe is absorbed by the runtime on Win/Linux, but
            // a disposed/faulted stderr would otherwise propagate; suppress narrowly.
            try { stderr.WriteLine(summary); }
            catch (IOException) { }
            catch (System.ObjectDisposedException) { }
        }
    }
}
