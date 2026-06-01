using System.Text.RegularExpressions;

namespace Winix.Demux;

/// <summary>
/// One route: a compiled regex predicate bound to a typed target. The predicate is null for the
/// default route (it matches everything unmatched by the explicit routes).
/// </summary>
public sealed class RouteSpec
{
    /// <summary>Creates a predicate route.</summary>
    public RouteSpec(Regex predicate, string patternText, TargetKind kind, string target)
    {
        Predicate = predicate;
        PatternText = patternText;
        Kind = kind;
        Target = target;
    }

    /// <summary>Creates the default (predicate-less) route.</summary>
    private RouteSpec(TargetKind kind, string target)
    {
        Predicate = null;
        PatternText = "(default)";
        Kind = kind;
        Target = target;
    }

    /// <summary>The compiled regex, or null for the default route.</summary>
    public Regex? Predicate { get; }

    /// <summary>The original pattern text (for labels/summary). "(default)" for the default route.</summary>
    public string PatternText { get; }

    /// <summary>File or Exec.</summary>
    public TargetKind Kind { get; }

    /// <summary>The file path (File) or command string (Exec).</summary>
    public string Target { get; }

    /// <summary>True if this is the default route.</summary>
    public bool IsDefault => Predicate is null;

    /// <summary>Factory for the default route.</summary>
    public static RouteSpec Default(TargetKind kind, string target) => new(kind, target);
}
