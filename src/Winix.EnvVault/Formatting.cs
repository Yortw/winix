#nullable enable
using System.Collections.Generic;
using System.Text;
using Yort.ShellKit;

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

    /// <summary>
    /// Format an error line with the standard <c>envvault:</c> prefix. Red when <paramref name="useColor"/>
    /// is true. The entire line (prefix + message) is wrapped in the colour code — matches the pattern
    /// used by <c>Winix.NetCat.Formatting.FormatErrorLine</c> for consistency across the suite.
    /// </summary>
    public static string ErrorLine(string message, bool useColor) =>
        useColor
            ? AnsiColor.Red(true) + "envvault: " + message + AnsiColor.Reset(true)
            : "envvault: " + message;

    /// <summary>
    /// Format a warning line with the standard <c>envvault: warning:</c> prefix. Yellow when
    /// <paramref name="useColor"/> is true. Prefixing every warning consistently lets shell users
    /// grep for warnings without ambiguity.
    /// </summary>
    public static string WarningLine(string message, bool useColor) =>
        useColor
            ? AnsiColor.Yellow(true) + "envvault: warning: " + message + AnsiColor.Reset(true)
            : "envvault: warning: " + message;

    /// <summary>Warning text when the user supplied a secret via <c>--value</c>: secrets on argv are visible to ps(1) and may land in shell history.</summary>
    public static string ValueOnArgvWarning(bool useColor) =>
        WarningLine(
            "--value puts the secret on argv, which is visible via ps(1) and may be written to shell history. "
            + "Prefer an interactive prompt or --set reading from stdin where possible.",
            useColor);

    /// <summary>Warning text emitted when <c>--get</c> writes a secret to a tty: plaintext may persist in terminal scrollback.</summary>
    public static string GetToTtyWarning(bool useColor) =>
        WarningLine(
            "--get output to a tty may land in scrollback. Prefer 'envvault <NAMESPACE> <cmd>' so the value "
            + "is injected into the child environment and never printed.",
            useColor);

    /// <summary>Error message surfaced when the user passes <c>--require-passphrase</c> in v1 (deferred to v1.1 with native Security.framework backend).</summary>
    public static string RequirePassphraseDeferredError(bool useColor) =>
        ErrorLine(
            "--require-passphrase requires the native macOS Security.framework backend (v1.1). "
            + "The v1 macOS implementation uses the 'security' CLI wrapper, which cannot set item ACLs. "
            + "Omit the flag to use default Keychain access; the v1.1 release will add passphrase-protected entries.",
            useColor);

    // Uses the shared ShellKit helper so control characters (\n, \t, \0, etc.) in a namespace or
    // key name are correctly JSON-escaped. The previous hand-rolled version only escaped \ and "
    // and would emit invalid JSON for any input containing a control char. Also keeps the whole
    // suite on one JSON writer (see feedback_new_tool_use_commandlineparser for the "use the
    // user's infrastructure" rationale).
    private static string JsonArray(IReadOnlyList<string> items)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartArray();
            foreach (string item in items)
            {
                writer.WriteStringValue(item);
            }
            writer.WriteEndArray();
        }
        return JsonHelper.GetString(buffer);
    }
}
