#nullable enable
using System.Collections.Generic;
using System.Text;

namespace Winix.EnvVault;

/// <summary>Pure-function output formatters for envvault's list operations, warnings, and error messages.</summary>
public static class Formatting
{
    /// <summary>Format a namespace list for <c>--list</c>. Plain mode yields one namespace per line; JSON mode emits a compact array. Both forms end with a trailing newline so shell prompts don't run into the output.</summary>
    public static string FormatNamespaceList(IReadOnlyList<string> namespaces, bool json)
    {
        if (json)
        {
            return JsonArray(namespaces) + "\n";
        }
        StringBuilder sb = new();
        foreach (string ns in namespaces)
        {
            sb.Append(ns).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Format a key list for <c>--list &lt;ns&gt;</c>. Plain mode yields one key per line; JSON mode emits a compact array. Both forms end with a trailing newline so shell prompts don't run into the output.</summary>
    public static string FormatKeyList(IReadOnlyList<string> keys, bool json)
    {
        if (json)
        {
            return JsonArray(keys) + "\n";
        }
        StringBuilder sb = new();
        foreach (string key in keys)
        {
            sb.Append(key).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Warning text when the user supplied a secret via <c>--value</c>: secrets on argv are visible to ps(1) and may land in shell history.</summary>
    public static string ValueOnArgvWarning() =>
        "warning: --value puts the secret on argv, which is visible via ps(1) and may be written to shell history. "
        + "Prefer an interactive prompt or --set reading from stdin where possible.";

    /// <summary>Warning text emitted when <c>--get</c> writes a secret to a tty: plaintext may persist in terminal scrollback.</summary>
    public static string GetToTtyWarning() =>
        "warning: --get output to a tty may land in scrollback. Prefer 'envvault <NAMESPACE> -- cmd' so the value "
        + "never leaves the child process env.";

    /// <summary>Error message surfaced when the user passes <c>--require-passphrase</c> in v1 (deferred to v1.1 with native Security.framework backend).</summary>
    public static string RequirePassphraseDeferredError() =>
        "--require-passphrase requires the native macOS Security.framework backend (v1.1). "
        + "The v1 macOS implementation uses the 'security' CLI wrapper, which cannot set item ACLs. "
        + "Track https://github.com/Yortw/winix for the v1.1 release, or omit the flag to use default Keychain access.";

    private static string JsonArray(IReadOnlyList<string> items)
    {
        StringBuilder sb = new();
        sb.Append('[');
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append('"').Append(items[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
        }
        sb.Append(']');
        return sb.ToString();
    }
}
