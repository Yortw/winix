#nullable enable
using System;
using System.IO;
using System.Text;
using Winix.SecretStore;

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
    /// Resolves <paramref name="reference"/> to its secret string value.
    /// </summary>
    /// <param name="reference">The parsed secret reference to resolve.</param>
    /// <param name="warn">Invoked with a human-readable warning for exposure-prone sources (e.g. <c>literal:</c>).</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an env var is missing, a vault key is not found, or stdin is consumed a second time.
    /// </exception>
    /// <exception cref="FormatException">Thrown when a vault reference lacks the required NS/KEY slash separator.</exception>
    public string Resolve(SecretRef reference, Action<string> warn)
    {
        switch (reference.Kind)
        {
            case SecretRefKind.Env:
                return Environment.GetEnvironmentVariable(reference.Value)
                    ?? throw new InvalidOperationException($"Environment variable '{reference.Value}' is not set.");

            case SecretRefKind.File:
                // TrimEnd to strip the trailing newline that editors commonly append.
                return File.ReadAllText(reference.Value).TrimEnd('\r', '\n');

            case SecretRefKind.Vault:
                int slash = reference.Value.IndexOf('/');
                if (slash <= 0)
                {
                    throw new FormatException($"vault reference '{reference.Value}' must be NS/KEY (slash-separated namespace and key).");
                }
                string ns = reference.Value.Substring(0, slash);
                string key = reference.Value.Substring(slash + 1);
                // ISecretStore.Get returns byte[]? — null means the key does not exist.
                byte[]? raw = _store.Get(ns, key);
                if (raw == null)
                {
                    throw new InvalidOperationException($"No vault entry '{ns}/{key}'.");
                }
                // Vault values are stored as UTF-8 bytes (convention established by EnvVault/Protect).
                return Encoding.UTF8.GetString(raw);

            case SecretRefKind.Stdin:
                if (_stdinConsumed)
                {
                    throw new InvalidOperationException("stdin can supply only one secret per invocation.");
                }
                _stdinConsumed = true;
                return _stdin.ReadToEnd().TrimEnd('\r', '\n');

            case SecretRefKind.Literal:
                warn("a literal secret is visible in argv / shell history; prefer env:, file:, vault:, or stdin.");
                return reference.Value;

            default:
                throw new InvalidOperationException($"Unhandled secret-ref kind {reference.Kind}.");
        }
    }
}
