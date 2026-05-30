#nullable enable
using System;

namespace Winix.HCat;

/// <summary>Minimal <c>--exit-on</c> predicate over a <see cref="RequestRecord"/>. Grammar (v1):
/// <c>path=&lt;exact&gt;</c>, <c>method=&lt;exact, case-insensitive&gt;</c>, <c>body~&lt;substring&gt;</c>.
/// An unrecognised expression never matches (the caller validated it at parse time).</summary>
public sealed class ExitOnPredicate
{
    private readonly string _kind;   // "path" | "method" | "body" | "none"
    private readonly string _value;

    private ExitOnPredicate(string kind, string value)
    {
        _kind = kind;
        _value = value;
    }

    /// <summary>Parses an expression. Returns a predicate that never matches for null/blank/unknown.</summary>
    public static ExitOnPredicate Parse(string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) { return new ExitOnPredicate("none", ""); }
        int eq = expr.IndexOf('=');
        int tilde = expr.IndexOf('~');
        if (tilde >= 0 && (eq < 0 || tilde < eq))
        {
            return new ExitOnPredicate(expr.Substring(0, tilde).Trim().ToLowerInvariant(),
                expr.Substring(tilde + 1));
        }
        if (eq >= 0)
        {
            return new ExitOnPredicate(expr.Substring(0, eq).Trim().ToLowerInvariant(),
                expr.Substring(eq + 1));
        }
        return new ExitOnPredicate("none", "");
    }

    /// <summary>True when the request satisfies the predicate.</summary>
    public bool Matches(RequestRecord r) => _kind switch
    {
        "path" => string.Equals(r.Path, _value, StringComparison.Ordinal),
        "method" => string.Equals(r.Method, _value, StringComparison.OrdinalIgnoreCase),
        "body" => r.Body is not null && r.Body.Contains(_value, StringComparison.Ordinal),
        _ => false,
    };
}
