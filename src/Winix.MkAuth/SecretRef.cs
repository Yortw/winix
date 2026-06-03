#nullable enable

namespace Winix.MkAuth;

/// <summary>The kind of source a <see cref="SecretRef"/> points at.</summary>
public enum SecretRefKind { Env, File, Vault, Stdin, Literal }

/// <summary>
/// A parsed secret reference. Secrets are never required on argv; a reference names where the
/// value comes from. Syntax: <c>env:NAME</c>, <c>file:PATH</c>, <c>vault:NS/KEY</c>,
/// <c>literal:VALUE</c>, or <c>stdin</c>/<c>-</c>. A bare value (no scheme) is rejected as
/// ambiguous.
/// </summary>
public readonly record struct SecretRef(SecretRefKind Kind, string Value)
{
    /// <summary>Parses a reference string. Throws <see cref="FormatException"/> on an unknown or
    /// missing scheme.</summary>
    public static SecretRef Parse(string input)
    {
        if (input is "stdin" or "-")
        {
            return new SecretRef(SecretRefKind.Stdin, "");
        }

        int colon = input.IndexOf(':');
        if (colon <= 0)
        {
            throw new FormatException(
                $"Secret reference '{input}' has no scheme. Use env:NAME, file:PATH, vault:NS/KEY, literal:VALUE, or stdin.");
        }

        string scheme = input.Substring(0, colon);
        string value = input.Substring(colon + 1);
        return scheme switch
        {
            "env" => new SecretRef(SecretRefKind.Env, value),
            "file" => new SecretRef(SecretRefKind.File, value),
            "vault" => new SecretRef(SecretRefKind.Vault, value),
            "literal" => new SecretRef(SecretRefKind.Literal, value),
            _ => throw new FormatException(
                $"Unknown secret-reference scheme '{scheme}'. Use env, file, vault, literal, or stdin."),
        };
    }
}
