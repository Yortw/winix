using System.Globalization;
using System.Text.Json;

namespace Yort.ShellKit;

/// <summary>
/// Immutable result of parsing command-line arguments. Provides typed access to
/// flag and option values, error reporting, and color resolution.
/// </summary>
public sealed class ParseResult
{
    private readonly string _toolName;
    private readonly string _version;
    private readonly HashSet<string> _flagsSet;
    private readonly Dictionary<string, string> _optionValues;
    private readonly Dictionary<string, List<string>> _listValues;
    private readonly int _usageErrorCode;
    private readonly bool _hasJson;

    internal ParseResult(
        string toolName,
        string version,
        HashSet<string> flagsSet,
        Dictionary<string, string> optionValues,
        Dictionary<string, List<string>> listValues,
        string[] command,
        string[] positionals,
        List<string> errors,
        bool isHandled,
        int handledExitCode,
        int usageErrorCode,
        bool hasJson)
    {
        _toolName = toolName;
        _version = version;
        _flagsSet = flagsSet;
        _optionValues = optionValues;
        _listValues = listValues;
        _usageErrorCode = usageErrorCode;
        _hasJson = hasJson;
        Command = command;
        Positionals = positionals;
        Errors = errors.AsReadOnly();
        IsHandled = isHandled;
        ExitCode = handledExitCode;
    }

    /// <summary>Args after the command boundary (CommandMode only).</summary>
    public string[] Command { get; }

    /// <summary>Non-flag positional args (non-CommandMode).</summary>
    public string[] Positionals { get; }

    /// <summary>Parse error messages.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True if --help or --version was handled (output already printed).</summary>
    public bool IsHandled { get; }

    /// <summary>Exit code when IsHandled is true.</summary>
    public int ExitCode { get; }

    /// <summary>True if any parse errors were detected.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>
    /// Returns true if the specified flag was present, or if a value option or list option
    /// with the given name was provided.
    /// </summary>
    /// <param name="name">Long name (e.g. "--verbose", "--output", "--watch").</param>
    public bool Has(string name)
    {
        return _flagsSet.Contains(name)
            || _optionValues.ContainsKey(name)
            || _listValues.ContainsKey(name);
    }

    /// <summary>Returns the string value of an option, or the default if not provided.</summary>
    /// <exception cref="InvalidOperationException">Option was not provided and no default given.</exception>
    public string GetString(string name, string? defaultValue = null)
    {
        if (_optionValues.TryGetValue(name, out string? value))
        {
            return value;
        }

        if (defaultValue is not null)
        {
            return defaultValue;
        }

        throw new InvalidOperationException($"Option {name} was not provided.");
    }

    /// <summary>Returns the int value of an option, or the default if not provided.</summary>
    /// <exception cref="InvalidOperationException">Option was not provided and no default given.</exception>
    public int GetInt(string name, int? defaultValue = null)
    {
        if (_optionValues.TryGetValue(name, out string? raw))
        {
            return int.Parse(raw, CultureInfo.InvariantCulture);
        }

        if (defaultValue.HasValue)
        {
            return defaultValue.Value;
        }

        throw new InvalidOperationException($"Option {name} was not provided.");
    }

    /// <summary>Returns the double value of an option, or the default if not provided.</summary>
    /// <exception cref="InvalidOperationException">Option was not provided and no default given.</exception>
    public double GetDouble(string name, double? defaultValue = null)
    {
        if (_optionValues.TryGetValue(name, out string? raw))
        {
            return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        if (defaultValue.HasValue)
        {
            return defaultValue.Value;
        }

        throw new InvalidOperationException($"Option {name} was not provided.");
    }

    /// <summary>Returns all values for a list option (empty array if none).</summary>
    public string[] GetList(string name)
    {
        if (_listValues.TryGetValue(name, out List<string>? values))
        {
            return values.ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Resolves whether colour output should be used, applying Winix precedence:
    /// explicit --color/--no-color flag &gt; NO_COLOR env var &gt; terminal auto-detection.
    /// </summary>
    public bool ResolveColor()
    {
        return ConsoleEnv.ResolveUseColor(
            Has("--color"),
            Has("--no-color"),
            ConsoleEnv.IsNoColorEnvSet(),
            ConsoleEnv.IsTerminal(checkStdErr: false));
    }

    /// <summary>
    /// Writes parse errors to the specified writer and returns the usage error exit code.
    /// If --json was set, writes a JSON error object instead of plain text.
    /// </summary>
    public int WriteErrors(TextWriter writer)
    {
        if (_hasJson)
        {
            writer.WriteLine(FormatUsageErrorJson(Errors));
        }
        else
        {
            foreach (string error in Errors)
            {
                writer.WriteLine($"{_toolName}: {error}");
            }
        }

        return _usageErrorCode;
    }

    /// <summary>
    /// Writes a single error message and returns the usage error exit code.
    /// If --json was set, writes a JSON error object including the message.
    /// Use for post-parse validation errors (e.g. "no command specified").
    /// </summary>
    public int WriteError(string message, TextWriter writer)
    {
        if (_hasJson)
        {
            writer.WriteLine(FormatUsageErrorJson(new[] { message }));
        }
        else
        {
            writer.WriteLine($"{_toolName}: {message}");
        }

        return _usageErrorCode;
    }

    private string FormatUsageErrorJson(IEnumerable<string> errors)
    {
        var (w, buffer) = JsonHelper.CreateWriter();
        using (w)
        {
            w.WriteStartObject();
            w.WriteString("tool", _toolName);
            w.WriteString("version", _version);
            w.WriteNumber("exit_code", _usageErrorCode);
            w.WriteString("exit_reason", "usage_error");
            w.WriteStartArray("errors");
            foreach (string error in errors)
            {
                w.WriteStringValue(error);
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }
}
