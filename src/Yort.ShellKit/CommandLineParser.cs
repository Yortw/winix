using System.Globalization;
using System.Text;

namespace Yort.ShellKit;

// Why a custom parser instead of System.CommandLine, Spectre.Console.Cli, or McMaster?
//
// - System.CommandLine: abandoned by Microsoft (stalled at preview for years). Unstable API,
//   heavy middleware pipeline designed for large CLI frameworks.
// - Spectre.Console.Cli: pulls in the full Spectre rendering library (~500KB+ AOT).
//   Overkill when you only need arg parsing.
// - McMaster.Extensions.CommandLineUtils: reflection-based model binding, hostile to AOT trimming.
// - All three target large apps with subcommand trees. Winix tools are single-purpose with flat
//   flag sets. This parser is smaller, fully AOT-safe, and enforces Winix CLI conventions
//   (exit codes, color precedence, JSON error format) by default.

/// <summary>
/// Declarative argument parser for Winix CLI tools. Register flags and options via the fluent
/// builder, then call <see cref="Parse"/> to get an immutable <see cref="ParseResult"/>.
/// </summary>
public sealed class CommandLineParser
{
    private readonly string _toolName;
    private readonly string _version;
    private string? _description;
    private bool _commandMode;
    private string? _positionalLabel;
    private int _usageErrorCode = ExitCode.UsageError;

    private readonly List<FlagDef> _flags = new();
    private readonly List<OptionDef> _options = new();
    private readonly List<ListOptionDef> _listOptions = new();
    private readonly List<AliasDef> _aliases = new();
    private readonly List<(string Title, string Body)> _sections = new();
    private readonly List<(int Code, string Description)> _exitCodes = new();

    // Lookup tables built lazily on first Parse()
    private Dictionary<string, FlagDef>? _flagLookup;
    private Dictionary<string, OptionDef>? _optionLookup;
    private Dictionary<string, ListOptionDef>? _listOptionLookup;
    private Dictionary<string, AliasDef>? _aliasLookup;
    private bool _standardFlagsRegistered;

    /// <summary>
    /// Creates a new parser for the specified tool.
    /// </summary>
    /// <param name="toolName">Tool executable name (e.g. "peep"). Used in error messages and help.</param>
    /// <param name="version">Tool version string. Used by --version.</param>
    public CommandLineParser(string toolName, string version)
    {
        _toolName = toolName;
        _version = version;
    }

    /// <summary>Sets the tool description shown in help output.</summary>
    public CommandLineParser Description(string text)
    {
        _description = text;
        return this;
    }

    /// <summary>
    /// Registers a boolean flag (no value). Access via <see cref="ParseResult.Has"/>.
    /// </summary>
    /// <param name="longName">Long flag name (e.g. "--verbose").</param>
    /// <param name="shortName">Optional short flag name (e.g. "-v").</param>
    /// <param name="description">Description shown in help output.</param>
    public CommandLineParser Flag(string longName, string? shortName, string description)
    {
        _flags.Add(new FlagDef(longName, shortName, description));
        return this;
    }

    /// <summary>
    /// Convenience overload for flags with no short name.
    /// </summary>
    public CommandLineParser Flag(string longName, string description)
    {
        return Flag(longName, null, description);
    }

    /// <summary>Registers a string-valued option.</summary>
    public CommandLineParser Option(string longName, string? shortName, string placeholder, string description)
    {
        _options.Add(new OptionDef(longName, shortName, placeholder, description, OptionType.String));
        return this;
    }

    /// <summary>Registers an integer-valued option with optional validation.</summary>
    public CommandLineParser IntOption(string longName, string? shortName, string placeholder, string description,
        Func<int, string?>? validate = null)
    {
        _options.Add(new OptionDef(longName, shortName, placeholder, description, OptionType.Int, IntValidate: validate));
        return this;
    }

    /// <summary>Registers a double-valued option with optional validation.</summary>
    public CommandLineParser DoubleOption(string longName, string? shortName, string placeholder, string description,
        Func<double, string?>? validate = null)
    {
        _options.Add(new OptionDef(longName, shortName, placeholder, description, OptionType.Double, DoubleValidate: validate));
        return this;
    }

    /// <summary>Registers a repeatable option that collects values into a list.</summary>
    public CommandLineParser ListOption(string longName, string? shortName, string placeholder, string description)
    {
        _listOptions.Add(new ListOptionDef(longName, shortName, placeholder, description));
        return this;
    }

    /// <summary>
    /// Registers a flag alias that expands to an option+value pair during parsing.
    /// Used for backward-compatibility shortcuts (e.g. -9 → --level 9).
    /// Aliases are not shown in the auto-generated options table.
    /// </summary>
    public CommandLineParser FlagAlias(string alias, string targetOption, string value)
    {
        _aliases.Add(new AliasDef(alias, targetOption, value));
        return this;
    }

    /// <summary>
    /// Enables command mode: the first non-flag argument stops flag parsing,
    /// and it plus all remaining arguments become the command array.
    /// Used by tools that run child commands (timeit, peep).
    /// </summary>
    public CommandLineParser CommandMode()
    {
        _commandMode = true;
        return this;
    }

    /// <summary>Sets the label for positional args in the usage line (e.g. "files...").</summary>
    public CommandLineParser Positional(string label)
    {
        _positionalLabel = label;
        return this;
    }

    /// <summary>
    /// Registers the standard Winix CLI flags: --help, -h, --version, --color, --no-color, --json.
    /// </summary>
    public CommandLineParser StandardFlags()
    {
        _standardFlagsRegistered = true;
        Flag("--help", "-h", "Show help");
        Flag("--version", "Show version");
        Flag("--color", "Force colored output");
        Flag("--no-color", "Disable colored output");
        Flag("--json", "JSON output to stderr");
        return this;
    }

    /// <summary>
    /// Overrides the exit code returned by <see cref="ParseResult.WriteErrors"/>.
    /// Default is 125 (POSIX usage error). Squeeze uses 2.
    /// </summary>
    public CommandLineParser UsageErrorCode(int code)
    {
        _usageErrorCode = code;
        return this;
    }

    /// <summary>Adds a free-form text section to the help output.</summary>
    public CommandLineParser Section(string title, string body)
    {
        _sections.Add((title, body));
        return this;
    }

    /// <summary>Adds exit code descriptions to the help output.</summary>
    public CommandLineParser ExitCodes(params (int Code, string Description)[] codes)
    {
        _exitCodes.AddRange(codes);
        return this;
    }

    /// <summary>
    /// Parses command-line arguments against registered flags and options.
    /// </summary>
    public ParseResult Parse(string[] args)
    {
        BuildLookups();

        var flagsSet = new HashSet<string>(StringComparer.Ordinal);
        var optionValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var listValues = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var errors = new List<string>();
        var positionals = new List<string>();
        var command = new List<string>();
        bool isHandled = false;
        int handledExitCode = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            // -- stops flag parsing
            if (arg == "--")
            {
                for (int j = i + 1; j < args.Length; j++)
                {
                    if (_commandMode)
                    {
                        command.Add(args[j]);
                    }
                    else
                    {
                        positionals.Add(args[j]);
                    }
                }
                break;
            }

            // Non-flag arg
            if (!arg.StartsWith('-'))
            {
                if (_commandMode)
                {
                    // First non-flag stops parsing in command mode
                    for (int j = i; j < args.Length; j++)
                    {
                        command.Add(args[j]);
                    }
                    break;
                }
                else
                {
                    positionals.Add(arg);
                    continue;
                }
            }

            // Check aliases first
            if (_aliasLookup!.TryGetValue(arg, out AliasDef? alias))
            {
                optionValues[alias.TargetOption] = alias.Value;
                continue;
            }

            // Check flags
            if (_flagLookup!.TryGetValue(arg, out FlagDef? flag))
            {
                flagsSet.Add(flag.LongName);
                continue;
            }

            // Check options (string, int, double)
            if (_optionLookup!.TryGetValue(arg, out OptionDef? option))
            {
                if (i + 1 >= args.Length)
                {
                    errors.Add($"{option.LongName} requires a value");
                    continue;
                }

                i++;
                string rawValue = args[i];

                // Type validation
                if (option.Type == OptionType.Int)
                {
                    if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                    {
                        errors.Add($"{option.LongName}: '{rawValue}' is not a valid integer");
                        continue;
                    }
                    if (option.IntValidate is not null)
                    {
                        string? validationError = option.IntValidate(intVal);
                        if (validationError is not null)
                        {
                            errors.Add($"{option.LongName}: {validationError}");
                            continue;
                        }
                    }
                }
                else if (option.Type == OptionType.Double)
                {
                    if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                    {
                        errors.Add($"{option.LongName}: '{rawValue}' is not a valid number");
                        continue;
                    }
                    if (option.DoubleValidate is not null)
                    {
                        string? validationError = option.DoubleValidate(dblVal);
                        if (validationError is not null)
                        {
                            errors.Add($"{option.LongName}: {validationError}");
                            continue;
                        }
                    }
                }

                optionValues[option.LongName] = rawValue;
                continue;
            }

            // Check list options
            if (_listOptionLookup!.TryGetValue(arg, out ListOptionDef? listOption))
            {
                if (i + 1 >= args.Length)
                {
                    errors.Add($"{listOption.LongName} requires a value");
                    continue;
                }

                i++;
                if (!listValues.TryGetValue(listOption.LongName, out List<string>? list))
                {
                    list = new List<string>();
                    listValues[listOption.LongName] = list;
                }
                list.Add(args[i]);
                continue;
            }

            // Unknown flag
            errors.Add($"unknown option: {arg}");
        }

        // Handle --help and --version
        if (flagsSet.Contains("--help") && _standardFlagsRegistered)
        {
            Console.WriteLine(GenerateHelp());
            isHandled = true;
            handledExitCode = 0;
        }
        else if (flagsSet.Contains("--version") && _standardFlagsRegistered)
        {
            Console.WriteLine($"{_toolName} {_version}");
            isHandled = true;
            handledExitCode = 0;
        }

        return new ParseResult(
            toolName: _toolName,
            version: _version,
            flagsSet: flagsSet,
            optionValues: optionValues,
            listValues: listValues,
            command: command.ToArray(),
            positionals: positionals.ToArray(),
            errors: errors,
            isHandled: isHandled,
            handledExitCode: handledExitCode,
            usageErrorCode: _usageErrorCode,
            hasJson: flagsSet.Contains("--json"));
    }

    private void BuildLookups()
    {
        if (_flagLookup is not null)
        {
            return;
        }

        _flagLookup = new Dictionary<string, FlagDef>(StringComparer.Ordinal);
        _optionLookup = new Dictionary<string, OptionDef>(StringComparer.Ordinal);
        _listOptionLookup = new Dictionary<string, ListOptionDef>(StringComparer.Ordinal);
        _aliasLookup = new Dictionary<string, AliasDef>(StringComparer.Ordinal);

        foreach (FlagDef f in _flags)
        {
            _flagLookup[f.LongName] = f;
            if (f.ShortName is not null)
            {
                _flagLookup[f.ShortName] = f;
            }
        }

        foreach (OptionDef o in _options)
        {
            _optionLookup[o.LongName] = o;
            if (o.ShortName is not null)
            {
                _optionLookup[o.ShortName] = o;
            }
        }

        foreach (ListOptionDef l in _listOptions)
        {
            _listOptionLookup[l.LongName] = l;
            if (l.ShortName is not null)
            {
                _listOptionLookup[l.ShortName] = l;
            }
        }

        foreach (AliasDef a in _aliases)
        {
            _aliasLookup[a.Alias] = a;
        }
    }

    // Help generation placeholder — implemented in Task 7
    internal string GenerateHelp()
    {
        return $"Usage: {_toolName} [options]";
    }

    // Internal definition types
    internal sealed record FlagDef(string LongName, string? ShortName, string Description);

    internal enum OptionType { String, Int, Double }

    internal sealed record OptionDef(
        string LongName, string? ShortName, string Placeholder, string Description,
        OptionType Type,
        Func<int, string?>? IntValidate = null,
        Func<double, string?>? DoubleValidate = null);

    internal sealed record ListOptionDef(string LongName, string? ShortName, string Placeholder, string Description);

    internal sealed record AliasDef(string Alias, string TargetOption, string Value);
}
