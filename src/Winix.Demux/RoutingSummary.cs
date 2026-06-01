#nullable enable
using System.Collections.Generic;
using System.Linq;
using Yort.ShellKit;

namespace Winix.Demux;

/// <summary>
/// Aggregates per-sink outcomes into the process exit code and the human/JSON summary text
/// (written to stderr per the suite convention for passthrough tools).
/// </summary>
public sealed class RoutingSummary
{
    private readonly IReadOnlyList<ISink> _sinks;
    private readonly bool _exitOnChildError;

    /// <summary>Initialises the summary.</summary>
    /// <param name="sinks">All sinks that participated in the routing run (including stdout).</param>
    /// <param name="exitOnChildError">
    ///   When true, a non-zero <see cref="ISink.ChildExitCode"/> on any sink produces exit code 2.
    ///   Corresponds to the <c>--exit-on-child-error</c> flag.
    /// </param>
    public RoutingSummary(IReadOnlyList<ISink> sinks, bool exitOnChildError)
    {
        _sinks = sinks;
        _exitOnChildError = exitOnChildError;
    }

    /// <summary>
    /// Computes the process exit code from the aggregated sink outcomes.
    /// <list type="bullet">
    ///   <item><term>0</term><description>All records delivered; no watched-child failure.</description></item>
    ///   <item><term>1</term><description>
    ///     Partial delivery failure — at least one sink has <see cref="ISink.UndeliveredCount"/> &gt; 0
    ///     (data lost). Takes precedence over code 2.
    ///   </description></item>
    ///   <item><term>2</term><description>
    ///     Watched-child failure — under <c>--exit-on-child-error</c>, at least one sink has a
    ///     non-zero <see cref="ISink.ChildExitCode"/>. The killed-after-timeout sentinel <c>-1</c>
    ///     counts as non-zero.
    ///   </description></item>
    /// </list>
    /// </summary>
    public int ExitCode
    {
        get
        {
            if (_sinks.Any(s => s.UndeliveredCount > 0)) { return 1; }
            if (_exitOnChildError && _sinks.Any(s => s.ChildExitCode is int c && c != 0)) { return 2; }
            return 0;
        }
    }

    /// <summary>
    /// Renders the human-readable per-sink summary (delivered/undelivered counts, dead routes,
    /// child exits). The <c>-1</c> killed-after-timeout sentinel is rendered as
    /// "child killed after timeout" rather than the raw numeric value.
    /// </summary>
    /// <param name="useColor">Reserved for colourised output via <see cref="AnsiColor"/>; currently unused.</param>
    public string FormatHuman(bool useColor)
    {
        var lines = new List<string>(_sinks.Count);
        foreach (ISink s in _sinks)
        {
            string dead = s.IsDead ? $" [DEAD, {s.UndeliveredCount} undelivered]" : "";
            string child = s.ChildExitCode is int c
                ? (c == -1 ? " (child killed after timeout)" : $" (child exit {c})")
                : "";
            lines.Add($"  {s.Label}: {s.DeliveredCount} delivered{dead}{child}");
        }
        return "demux summary:\n" + string.Join("\n", lines);
    }

    /// <summary>
    /// Renders the summary as a JSON envelope suitable for stderr output.
    /// Each route object includes <c>child_exit_code</c> (when applicable) and
    /// <c>killed_after_timeout: true</c> for the <c>-1</c> sentinel so consumers
    /// need not magic-number the sentinel value.
    /// </summary>
    /// <param name="toolName">The tool name written to the <c>tool</c> field (e.g. "demux").</param>
    /// <param name="version">The tool version written to the <c>version</c> field.</param>
    public string FormatJson(string toolName, string version)
    {
        var (w, buffer) = JsonHelper.CreateWriter();
        using (w)
        {
            w.WriteStartObject();
            w.WriteString("tool", toolName);
            w.WriteString("version", version);
            w.WriteNumber("exit_code", ExitCode);
            w.WriteString("exit_reason", ExitCode switch
            {
                0 => "success",
                1 => "partial_delivery_failure",
                2 => "watched_child_failed",
                _ => "error",
            });
            w.WriteStartArray("routes");
            foreach (ISink s in _sinks)
            {
                w.WriteStartObject();
                w.WriteString("label", s.Label);
                w.WriteNumber("delivered", s.DeliveredCount);
                w.WriteNumber("undelivered", s.UndeliveredCount);
                w.WriteBoolean("dead", s.IsDead);
                if (s.ChildExitCode is int c)
                {
                    w.WriteNumber("child_exit_code", c);
                    // Expose the killed-after-timeout sentinel as a boolean so consumers
                    // don't have to magic-number -1.
                    if (c == -1) { w.WriteBoolean("killed_after_timeout", true); }
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }
}
