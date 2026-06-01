using System.Collections.Generic;

namespace Winix.Demux;

/// <summary>Fully-parsed, validated run configuration for a demux invocation.</summary>
public sealed class DemuxOptions
{
    public DemuxOptions(
        IReadOnlyList<RouteSpec> routes,
        RouteSpec? defaultRoute,
        int? field,
        string delimiter,
        bool all,
        bool append,
        bool exitOnChildError,
        bool json,
        bool useColor)
    {
        Routes = routes;
        DefaultRoute = defaultRoute;
        Field = field;
        Delimiter = delimiter;
        All = all;
        Append = append;
        ExitOnChildError = exitOnChildError;
        Json = json;
        UseColor = useColor;
    }

    /// <summary>Explicit routes, in declaration order (at least one).</summary>
    public IReadOnlyList<RouteSpec> Routes { get; }

    /// <summary>The default route, or null (unmatched → stdout).</summary>
    public RouteSpec? DefaultRoute { get; }

    /// <summary>1-based field index to test, or null (test the whole line).</summary>
    public int? Field { get; }

    /// <summary>Field delimiter; the sentinel "" means "runs of whitespace" (awk default).</summary>
    public string Delimiter { get; }

    /// <summary>Broadcast to every matching route instead of first-match.</summary>
    public bool All { get; }

    /// <summary>File targets append instead of truncate.</summary>
    public bool Append { get; }

    /// <summary>A watched child's non-zero exit makes demux exit 2.</summary>
    public bool ExitOnChildError { get; }

    /// <summary>Emit the summary as JSON (to stderr).</summary>
    public bool Json { get; }

    /// <summary>Use ANSI colour in the human summary.</summary>
    public bool UseColor { get; }
}
