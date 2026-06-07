#nullable enable
using System;
using System.IO;
using System.Text;
using Winix.SecretStore;
using Yort.ShellKit;

namespace Winix.MkAuth;

/// <summary>
/// Resolves a <see cref="SecretRef"/> to its secret value. <c>stdin</c> may be used at most once
/// per resolver instance. <c>literal:</c> emits a warning (the value is visible in argv / shell
/// history). <c>vault:</c> reads from the OS keychain via <see cref="ISecretStore"/>.
/// </summary>
public sealed class SecretResolver
{
    private readonly ISecretStore _store;
    private readonly TextReader _stdin;
    private bool _stdinConsumed;

    /// <param name="store">OS-native secret store for <c>vault:</c> references.</param>
    /// <param name="stdin">Reader to consume for <c>stdin</c>/<c>-</c> references; single-use per instance.</param>
    public SecretResolver(ISecretStore store, TextReader stdin)
    {
        _store = store;
        _stdin = stdin;
    }

    /// <summary>
    /// Resolves <paramref name="reference"/> to its secret string value. Reference-resolved sources
    /// (<c>env:</c>, <c>file:</c>, <c>vault:</c>, <c>stdin</c>) have all surrounding whitespace stripped
    /// (<c>.Trim()</c>) and are rejected if the result is empty or whitespace-only. <c>literal:</c> is
    /// returned verbatim (no trim) and may be empty.
    /// </summary>
    /// <param name="reference">The parsed secret reference to resolve.</param>
    /// <param name="warn">Invoked with a human-readable warning for exposure-prone sources (e.g. <c>literal:</c>).</param>
    /// <exception cref="MkAuthException">
    /// Thrown when an env var is missing, a vault key is not found, a vault reference lacks the required
    /// NS/KEY slash separator, stdin is consumed a second time, or a reference-resolved secret is empty /
    /// whitespace-only.
    /// </exception>
    public string Resolve(SecretRef reference, Action<string> warn)
    {
        // literal: is the verbatim escape hatch — emitted exactly as given (empty/whitespace allowed,
        // covering the RFC 7617 empty-password class), and never trimmed. It returns early so the uniform
        // trim + empty-rejection below applies only to the reference-resolved sources.
        if (reference.Kind == SecretRefKind.Literal)
        {
            warn("a literal secret is visible in argv / shell history; prefer env:, file:, vault:, or stdin.");
            return reference.Value;
        }

        // Resolve to the raw value plus a human label naming the source, so an empty result can be reported
        // against the exact reference the user supplied. B2: every reference-resolved source is .Trim()'d
        // (ALL surrounding whitespace, not just a trailing newline) so the same key signs the same way
        // regardless of delivery mechanism — a file with a trailing newline, an env var with stray spaces,
        // and a piped value all reduce to the bare key. literal: (returned above) is the only verbatim path.
        string raw;
        string source;
        switch (reference.Kind)
        {
            case SecretRefKind.Env:
                raw = (Environment.GetEnvironmentVariable(reference.Value)
                    ?? throw new MkAuthException($"Environment variable '{reference.Value}' is not set.")).Trim();
                source = $"env:{reference.Value}";
                break;

            case SecretRefKind.File:
                // Wrap the read so a missing/unreadable file surfaces a named, actionable MkAuthException —
                // FileNotFoundException and DirectoryNotFoundException both derive from IOException, which Cli's
                // broken-pipe handler would otherwise have mistaken for a closed output pipe (silent exit 0).
                try
                {
                    raw = File.ReadAllText(reference.Value).Trim();
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new MkAuthException($"Cannot read secret file '{reference.Value}': access denied ({SafeError.Describe(ex)}).", ex);
                }
                catch (IOException ex)
                {
                    throw new MkAuthException($"Cannot read secret file '{reference.Value}': {SafeError.Describe(ex)}", ex);
                }
                source = $"file:{reference.Value}";
                break;

            case SecretRefKind.Vault:
                int slash = reference.Value.IndexOf('/');
                if (slash <= 0)
                {
                    throw new MkAuthException($"vault reference '{reference.Value}' must be NS/KEY (slash-separated namespace and key).");
                }
                string ns = reference.Value.Substring(0, slash);
                string key = reference.Value.Substring(slash + 1);
                // ISecretStore.Get returns byte[]? — null means the key does not exist.
                byte[]? bytes = _store.Get(ns, key);
                if (bytes == null)
                {
                    throw new MkAuthException($"No vault entry '{ns}/{key}'.");
                }
                // Vault values are stored as UTF-8 bytes (convention established by EnvVault/Protect).
                raw = Encoding.UTF8.GetString(bytes).Trim();
                source = $"vault:{reference.Value}";
                break;

            case SecretRefKind.Stdin:
                if (_stdinConsumed)
                {
                    throw new MkAuthException("stdin can supply only one secret per invocation.");
                }
                _stdinConsumed = true;
                raw = _stdin.ReadToEnd().Trim();
                source = "stdin";
                break;

            default:
                throw new InvalidOperationException($"Unhandled secret-ref kind {reference.Kind}.");
        }

        // SFH-1: a reference-resolved secret that is empty or whitespace-only would sign a silently-wrong
        // header (e.g. an HMAC over an empty key). Reject it, naming the source so the user can fix it. The
        // verbatim escape hatch is literal:, which returned above. There is deliberately no separate
        // "stdin not redirected" detection — an interactive non-redirected stdin reads to EOF as empty here,
        // which this same check rejects (it never silently signs).
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new MkAuthException($"resolved secret from {source} is empty. Provide a non-empty value (or use literal: to send an empty secret deliberately).");
        }

        return raw;
    }
}
