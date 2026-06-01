using System.Collections.Generic;
using System.IO;

namespace Winix.Demux;

/// <summary>Core routing loop. Streams the input one line at a time and dispatches each line to the
/// matching route sink(s); unmatched lines go to the default sink or the stdout passthrough.</summary>
public sealed class Router
{
    private static readonly char[] Whitespace = { ' ', '\t' };

    /// <summary>Reads <paramref name="input"/> to EOF, routing each line. Sinks are injected and
    /// owned by the caller (the caller closes them and reads their counters afterwards).</summary>
    public void Run(
        TextReader input,
        DemuxOptions options,
        IReadOnlyList<(RouteSpec Spec, ISink Sink)> routes,
        ISink? defaultSink,
        ISink stdoutSink)
    {
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            string? subject = Subject(line, options);
            bool matchedAny = false;
            if (subject is not null)
            {
                foreach (var (spec, sink) in routes)
                {
                    if (spec.Predicate!.IsMatch(subject))
                    {
                        sink.Write(line); // always the full original line
                        matchedAny = true;
                        if (!options.All) { break; } // first-match
                    }
                }
            }
            if (!matchedAny) { (defaultSink ?? stdoutSink).Write(line); }
        }
    }

    /// <summary>The text the predicate is tested against: a chosen field, the whole line, or null when
    /// the requested field is out of range (caller treats null as "no match" — never delivered).</summary>
    private static string? Subject(string line, DemuxOptions options)
    {
        if (options.Field is not int n) { return line; }

        string[] parts = options.Delimiter.Length == 0
            ? line.Split(Whitespace, System.StringSplitOptions.RemoveEmptyEntries)
            : line.Split(options.Delimiter);

        return (n >= 1 && n <= parts.Length) ? parts[n - 1] : null; // out-of-range → null → unmatched
    }
}
