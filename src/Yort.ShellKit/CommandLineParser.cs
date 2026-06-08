using System.Globalization;
using System.Text;
using System.Text.Json;

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
    /// <summary>
    /// Version of the --describe envelope STRUCTURE. Bump ONLY on renames, removals,
    /// or type changes of envelope fields — never for additive fields. docs/STABILITY.md
    /// documents the rule for consumers.
    /// </summary>
    public const int DescribeSchemaVersion = 1;

    private readonly string _toolName;
    private readonly string _version;
    private string? _description;
    private bool _commandMode;
    private string? _positionalLabel;
    private int _usageErrorCode = ExitCode.UsageError;

    private readonly List<FlagDef> _flags = new();
    private readonly List<OptionDef> _options = new();
    private readonly List<ListOptionDef> _listOptions = new();
    private readonly List<OptionalValueOptionDef> _optionalValueOptions = new();
    private readonly List<AliasDef> _aliases = new();
    private readonly List<(string Title, string Body)> _sections = new();
    private readonly List<(int Code, string Description)> _exitCodes = new();

    private string? _stdinDescription;
    private string? _stdoutDescription;
    private string? _stderrDescription;
    private readonly List<(string Command, string Description)> _examples = new();
    private readonly List<(string Tool, string Pattern, string Description)> _composability = new();
    private readonly List<(string Name, string Type, string Description)> _jsonFields = new();
    private string? _platformScope;
    private string[]? _platformReplaces;
    private string? _platformValueWindows;
    private string? _platformValueUnix;
    private ToolMaturity? _maturity;
    private string[]? _preferDefaultWhen;

    // Lookup tables built on first Parse(). Registering flags/options after Parse() is
    // unsupported — the lookups would be stale and the new registrations silently ignored.
    private Dictionary<string, FlagDef>? _flagLookup;
    private Dictionary<string, OptionDef>? _optionLookup;
    private Dictionary<string, ListOptionDef>? _listOptionLookup;
    private Dictionary<string, OptionalValueOptionDef>? _optionalValueLookup;
    private Dictionary<string, AliasDef>? _aliasLookup;
    private bool _standardFlagsRegistered;
    private bool _parsed;

    private bool _expandGlobPositionals;
    private int _globSkipFirst;

    // Test seams for glob expansion (precedent: ResolveColorCore). Null → production behaviour.
    internal Func<bool>? GlobWindowsGateOverride;
    internal Func<string?>? GlobRawCommandLineProvider;
    internal GlobArgExpander? GlobExpanderOverride;

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

    private void ThrowIfParsed()
    {
        if (_parsed)
        {
            throw new InvalidOperationException(
                "Cannot modify CommandLineParser after Parse() has been called. " +
                "Register all flags/options before calling Parse().");
        }
    }

    /// <summary>Sets the tool description shown in help output.</summary>
    public CommandLineParser Description(string text)
    {
        ThrowIfParsed();
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
        ThrowIfParsed();
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
        ThrowIfParsed();
        _options.Add(new OptionDef(longName, shortName, placeholder, description, OptionType.String));
        return this;
    }

    /// <summary>Registers an integer-valued option with optional validation.</summary>
    public CommandLineParser IntOption(string longName, string? shortName, string placeholder, string description,
        Func<int, string?>? validate = null)
    {
        ThrowIfParsed();
        _options.Add(new OptionDef(longName, shortName, placeholder, description, OptionType.Int, IntValidate: validate));
        return this;
    }

    /// <summary>Registers a double-valued option with optional validation.</summary>
    public CommandLineParser DoubleOption(string longName, string? shortName, string placeholder, string description,
        Func<double, string?>? validate = null)
    {
        ThrowIfParsed();
        _options.Add(new OptionDef(longName, shortName, placeholder, description, OptionType.Double, DoubleValidate: validate));
        return this;
    }

    /// <summary>Registers a repeatable option that collects values into a list.</summary>
    public CommandLineParser ListOption(string longName, string? shortName, string placeholder, string description)
    {
        ThrowIfParsed();
        _listOptions.Add(new ListOptionDef(longName, shortName, placeholder, description));
        return this;
    }

    /// <summary>Registers an optional-value flag: valid bare (resolves to <paramref name="defaultWhenBare"/>)
    /// or as --name=VALUE where VALUE must be one of <paramref name="allowedValues"/>.</summary>
    public CommandLineParser OptionalValueOption(string longName, string? shortName, string description,
        string[] allowedValues, string defaultWhenBare)
    {
        ThrowIfParsed();
        _optionalValueOptions.Add(new OptionalValueOptionDef(longName, shortName, description, allowedValues, defaultWhenBare));
        return this;
    }

    /// <summary>
    /// Registers a flag alias that expands to an option+value pair during parsing.
    /// Used for backward-compatibility shortcuts (e.g. -9 → --level 9).
    /// Aliases are not shown in the auto-generated options table.
    /// </summary>
    public CommandLineParser FlagAlias(string alias, string targetOption, string value)
    {
        ThrowIfParsed();
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
    /// Opts this tool into Windows glob expansion of positional arguments. On Windows
    /// (cmd.exe and PowerShell do not expand wildcards), positionals containing
    /// <c>*</c> or <c>?</c> are expanded in-process before they reach
    /// <see cref="ParseResult.Positionals"/>; on other platforms this is a no-op (the
    /// shell has already expanded). A pattern matching nothing passes through literally;
    /// <c>[...]</c> is never treated as a pattern; <c>**</c> produces a usage error.
    /// Arguments quoted on the raw command line are not expanded (effective from cmd.exe;
    /// PowerShell strips quotes before the process starts). Only opt in when ALL
    /// positionals (after <paramref name="skipFirst"/>) are file paths — never for
    /// subcommand verbs or cron expressions.
    /// </summary>
    /// <param name="skipFirst">Number of leading positionals to exempt (e.g. 1 for a subcommand verb).</param>
    public CommandLineParser ExpandGlobPositionals(int skipFirst = 0)
    {
        ThrowIfParsed();
        _expandGlobPositionals = true;
        _globSkipFirst = skipFirst;
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
        OptionalValueOption("--color", null, "Coloured output: auto (default when omitted), always, or never",
            new[] { "auto", "always", "never" }, "always");
        Flag("--no-color", "Disable colored output");
        Flag("--json", "JSON output");
        Flag("--describe", "Structured JSON metadata for AI agents");
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

    /// <summary>Sets the description of what the tool reads from stdin.</summary>
    public CommandLineParser StdinDescription(string text)
    {
        _stdinDescription = text;
        return this;
    }

    /// <summary>Sets the description of what the tool writes to stdout.</summary>
    public CommandLineParser StdoutDescription(string text)
    {
        _stdoutDescription = text;
        return this;
    }

    /// <summary>Sets the description of what the tool writes to stderr.</summary>
    public CommandLineParser StderrDescription(string text)
    {
        _stderrDescription = text;
        return this;
    }

    /// <summary>Adds a usage example shown in --describe output.</summary>
    /// <param name="command">The example command line.</param>
    /// <param name="description">What the example demonstrates.</param>
    public CommandLineParser Example(string command, string description)
    {
        _examples.Add((command, description));
        return this;
    }

    /// <summary>Documents how this tool composes with another tool in a pipeline.</summary>
    /// <param name="tool">Name of the other tool (e.g. "jq", "grep").</param>
    /// <param name="pattern">Example pipeline pattern.</param>
    /// <param name="description">What the composition achieves.</param>
    public CommandLineParser ComposesWith(string tool, string pattern, string description)
    {
        _composability.Add((tool, pattern, description));
        return this;
    }

    /// <summary>Documents a field in the tool's --json output.</summary>
    /// <param name="name">JSON field name.</param>
    /// <param name="type">JSON type (e.g. "number", "string", "boolean").</param>
    /// <param name="description">What the field contains.</param>
    public CommandLineParser JsonField(string name, string type, string description)
    {
        _jsonFields.Add((name, type, description));
        return this;
    }

    /// <summary>Documents the tool's cross-platform scope and which native tools it replaces.</summary>
    /// <param name="scope">Platform scope (e.g. "cross-platform").</param>
    /// <param name="replaces">Native tools this replaces (e.g. "time" on Unix, "Measure-Command" on Windows).</param>
    /// <param name="valueOnWindows">What value this tool adds on Windows.</param>
    /// <param name="valueOnUnix">What value this tool adds on Unix/macOS.</param>
    public CommandLineParser Platform(string scope, string[] replaces, string valueOnWindows, string valueOnUnix)
    {
        _platformScope = scope;
        _platformReplaces = replaces;
        _platformValueWindows = valueOnWindows;
        _platformValueUnix = valueOnUnix;
        return this;
    }

    /// <summary>
    /// Advertises the tool's maturity tier in --describe. Omit to leave the field absent;
    /// ShellKit stays unopinionated — the "every Winix tool must declare maturity" rule is
    /// enforced by the contract harness, not here. See <see cref="ToolMaturity"/>.
    /// </summary>
    /// <param name="maturity">The tier to advertise.</param>
    public CommandLineParser Maturity(ToolMaturity maturity)
    {
        _maturity = maturity;
        return this;
    }

    /// <summary>
    /// Machine-readable "prefer the incumbent when…" hints emitted in --describe as
    /// <c>prefer_default_when</c>. Entries must condense existing reviewed docs prose
    /// (never new claims). Omit entirely when the tool has no real incumbent case —
    /// absence means "no guidance".
    /// </summary>
    /// <param name="cases">One or more scenarios where the incumbent tool is the better choice.</param>
    public CommandLineParser PreferDefaultWhen(params string[] cases)
    {
        _preferDefaultWhen = cases;
        return this;
    }

    /// <summary>
    /// Parses command-line arguments against registered flags and options.
    /// </summary>
    public ParseResult Parse(string[] args)
    {
        _parsed = true;
        BuildLookups();

        var flagsSet = new HashSet<string>(StringComparer.Ordinal);
        var optionValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var listValues = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var errors = new List<string>();
        var positionals = new List<string>();
        var positionalArgvIndices = new List<int>();
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
                        positionalArgvIndices.Add(j);
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
                    positionalArgvIndices.Add(i);
                    continue;
                }
            }

            // Check aliases first (e.g. -9 → --level 9). Aliases match the exact token (no =-split).
            if (_aliasLookup!.TryGetValue(arg, out AliasDef? alias))
            {
                if (_optionLookup!.TryGetValue(alias.TargetOption, out OptionDef? aliasTarget))
                {
                    if (aliasTarget.Type == OptionType.Int)
                    {
                        if (!int.TryParse(alias.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                        {
                            errors.Add($"{alias.TargetOption}: '{alias.Value}' is not a valid integer");
                            continue;
                        }
                        if (aliasTarget.IntValidate is not null)
                        {
                            string? validationError = aliasTarget.IntValidate(intVal);
                            if (validationError is not null)
                            {
                                errors.Add($"{alias.TargetOption}: {validationError}");
                                continue;
                            }
                        }
                    }
                    else if (aliasTarget.Type == OptionType.Double)
                    {
                        if (!double.TryParse(alias.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                        {
                            errors.Add($"{alias.TargetOption}: '{alias.Value}' is not a valid number");
                            continue;
                        }
                        if (aliasTarget.DoubleValidate is not null)
                        {
                            string? validationError = aliasTarget.DoubleValidate(dblVal);
                            if (validationError is not null)
                            {
                                errors.Add($"{alias.TargetOption}: {validationError}");
                                continue;
                            }
                        }
                    }
                }

                optionValues[alias.TargetOption] = alias.Value;
                continue;
            }

            // Split a long --key=value token into key + attached value. Short flags and bare long
            // flags are unaffected (attachedValue stays null). The value may itself contain '='
            // (we split on the first '=' only). GNU convention: long options use '=', short don't.
            string lookupKey = arg;
            string? attachedValue = null;
            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2)
            {
                int eq = arg.IndexOf('=');
                if (eq >= 0)
                {
                    lookupKey = arg.Substring(0, eq);
                    attachedValue = arg.Substring(eq + 1);
                }
            }

            // Optional-value flag (e.g. --color: bare → default, --color=never → explicit, enum-validated)
            if (_optionalValueLookup!.TryGetValue(lookupKey, out OptionalValueOptionDef? ovOption))
            {
                if (attachedValue is not null && Array.IndexOf(ovOption.AllowedValues, attachedValue) < 0)
                {
                    errors.Add($"{ovOption.LongName}: '{attachedValue}' is not one of: {string.Join(", ", ovOption.AllowedValues)}");
                    continue;
                }
                flagsSet.Add(ovOption.LongName);
                optionValues[ovOption.LongName] = attachedValue ?? ovOption.DefaultWhenBare;
                continue;
            }

            // Check flags
            if (_flagLookup!.TryGetValue(lookupKey, out FlagDef? flag))
            {
                if (attachedValue is not null)
                {
                    errors.Add($"{flag.LongName} takes no value");
                    continue;
                }
                flagsSet.Add(flag.LongName);
                continue;
            }

            // Check options (string, int, double)
            if (_optionLookup!.TryGetValue(lookupKey, out OptionDef? option))
            {
                string rawValue;
                if (attachedValue is not null)
                {
                    rawValue = attachedValue;
                }
                else
                {
                    if (i + 1 >= args.Length)
                    {
                        errors.Add($"{option.LongName} requires a value");
                        continue;
                    }
                    i++;
                    rawValue = args[i];
                }

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
            if (_listOptionLookup!.TryGetValue(lookupKey, out ListOptionDef? listOption))
            {
                string listItem;
                if (attachedValue is not null)
                {
                    listItem = attachedValue;
                }
                else
                {
                    if (i + 1 >= args.Length)
                    {
                        errors.Add($"{listOption.LongName} requires a value");
                        continue;
                    }
                    i++;
                    listItem = args[i];
                }
                if (!listValues.TryGetValue(listOption.LongName, out List<string>? list))
                {
                    list = new List<string>();
                    listValues[listOption.LongName] = list;
                }
                list.Add(listItem);
                continue;
            }

            // Unknown flag (report the key, not the whole --key=value token)
            errors.Add($"unknown option: {lookupKey}");
        }

        // Handle --help, --version, --describe. Each writes a single line to stdout.
        // Defensive note: empirically both Windows (.NET 10) and Linux (.NET 8) silently
        // absorb broken-pipe at the Console.Out layer for the standard `tool --help | head`
        // idiom — see docs/plans/2026-04-26-shellkit-broken-pipe.md / tmp/iox-probe findings
        // (gitignored). The try/catch (IOException) wrap is belt-and-braces in case a future
        // runtime version stops absorbing, AND so the suite stays consistent with the Safe*
        // convention. We do NOT widen to Exception — a thrown InvalidOperationException from
        // GenerateHelp/GenerateDescribe is a real bug (parser-config defect) that MUST surface,
        // not be hidden as "tool exits clean with no output."
        if (flagsSet.Contains("--help") && _standardFlagsRegistered)
        {
            try { Console.WriteLine(GenerateHelp()); }
            catch (IOException) { }
            isHandled = true;
            handledExitCode = 0;
        }
        else if (flagsSet.Contains("--version") && _standardFlagsRegistered)
        {
            try { Console.WriteLine($"{_toolName} {_version}"); }
            catch (IOException) { }
            isHandled = true;
            handledExitCode = 0;
        }
        else if (flagsSet.Contains("--describe") && _standardFlagsRegistered)
        {
            try { Console.WriteLine(GenerateDescribe()); }
            catch (IOException) { }
            isHandled = true;
            handledExitCode = 0;
        }

        if (_expandGlobPositionals && !_commandMode && !isHandled
            && positionals.Count > _globSkipFirst
            && (GlobWindowsGateOverride?.Invoke() ?? OperatingSystem.IsWindows()))
        {
            ExpandGlobPositionalsInPlace(args, positionals, positionalArgvIndices, errors);
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
        _optionalValueLookup = new Dictionary<string, OptionalValueOptionDef>(StringComparer.Ordinal);
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

        foreach (OptionalValueOptionDef ov in _optionalValueOptions)
        {
            _optionalValueLookup[ov.LongName] = ov;
            if (ov.ShortName is not null)
            {
                _optionalValueLookup[ov.ShortName] = ov;
            }
        }
    }

    private void ExpandGlobPositionalsInPlace(string[] args, List<string> positionals,
        List<int> argvIndices, List<string> errors)
    {
        bool[]? quoted = TryGetQuotedFlags(args);
        GlobArgExpander expander = GlobExpanderOverride ?? new GlobArgExpander();
        var result = new List<string>(positionals.Count);

        for (int p = 0; p < positionals.Count; p++)
        {
            string value = positionals[p];
            if (p < _globSkipFirst)
            {
                result.Add(value);
                continue;
            }

            // quoted == null → raw line unavailable/misaligned → fail OPEN (expand). A quoted
            // glob falling back to expansion is benign (a '*'-bearing literal can never name a
            // real Windows file); silently disabling expansion would be an invisible outage.
            if (quoted is not null && quoted[argvIndices[p]])
            {
                result.Add(value);
                continue;
            }

            GlobExpansionResult expansion = expander.Expand(value);
            switch (expansion.Kind)
            {
                case GlobExpansionKind.Expanded:
                    result.AddRange(expansion.Matches);
                    break;
                case GlobExpansionKind.UnsupportedRecursive:
                    errors.Add($"recursive glob '**' is not supported in argument expansion: {value} (use the tool's recursive options, or 'files' to find files recursively)");
                    result.Add(value);
                    break;
                default: // NotAPattern, NoMatch → literal passthrough
                    result.Add(value);
                    break;
            }
        }

        positionals.Clear();
        positionals.AddRange(result);
    }

    /// <summary>
    /// Maps each argv index to whether that token was quoted on the raw command line.
    /// Returns null (caller fails open) when the raw line is unavailable or does not
    /// align with args — e.g. <c>dotnet tool.dll args</c> hosting, or a tokenizer gap.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="NativeCommandLine.Get"/> (P/Invoke <c>GetCommandLineW</c>) rather than
    /// <c>Environment.CommandLine</c>. On .NET Core, <c>Environment.CommandLine</c> is
    /// <c>GetCommandLineArgs()</c> re-joined with spaces — quote information is destroyed,
    /// so quoting detection would silently see every token as unquoted (verified 2026-06-05).
    /// </remarks>
    private bool[]? TryGetQuotedFlags(string[] args)
    {
        string? raw = GlobRawCommandLineProvider is not null
            ? GlobRawCommandLineProvider()
            : NativeCommandLine.Get();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        IReadOnlyList<CommandLineToken> tokens = RawCommandLineTokenizer.Tokenize(raw);
        if (tokens.Count != args.Length + 1) // + argv[0]
        {
            return null;
        }
        for (int i = 0; i < args.Length; i++)
        {
            // Text alignment too, not just count — belt-and-braces against rule drift.
            if (!string.Equals(tokens[i + 1].Text, args[i], StringComparison.Ordinal))
            {
                return null;
            }
        }

        var quoted = new bool[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            quoted[i] = tokens[i + 1].WasQuoted;
        }
        return quoted;
    }

    internal string GenerateHelp()
    {
        var sb = new StringBuilder();

        // Usage line
        sb.Append($"Usage: {_toolName} [options]");
        if (_commandMode)
        {
            sb.Append(" [--] <command> [args...]");
        }
        else if (_positionalLabel is not null)
        {
            sb.Append($" [{_positionalLabel}]");
        }
        sb.AppendLine();

        // Description
        if (_description is not null)
        {
            sb.AppendLine();
            sb.AppendLine(_description);
        }

        // Options table
        sb.AppendLine();
        sb.AppendLine("Options:");

        // Collect all option lines: (leftColumn, description, isStandard)
        var optionLines = new List<(string Left, string Desc, bool IsStandard)>();
        string[] standardNames = { "--help", "--version", "--color", "--no-color", "--json", "--describe" };

        foreach (FlagDef f in _flags)
        {
            bool isStd = Array.IndexOf(standardNames, f.LongName) >= 0;
            string left = f.ShortName is not null
                ? $"  {f.ShortName}, {f.LongName}"
                : $"  {f.LongName}";
            optionLines.Add((left, f.Description, isStd));
        }

        foreach (OptionDef o in _options)
        {
            string left = o.ShortName is not null
                ? $"  {o.ShortName}, {o.LongName} {o.Placeholder}"
                : $"  {o.LongName} {o.Placeholder}";
            optionLines.Add((left, o.Description, false));
        }

        foreach (ListOptionDef l in _listOptions)
        {
            string left = l.ShortName is not null
                ? $"  {l.ShortName}, {l.LongName} {l.Placeholder}"
                : $"  {l.LongName} {l.Placeholder}";
            optionLines.Add((left, l.Description + " (repeatable)", false));
        }

        foreach (OptionalValueOptionDef ov in _optionalValueOptions)
        {
            bool isStd = Array.IndexOf(standardNames, ov.LongName) >= 0;
            string valueHint = $"[={string.Join("|", ov.AllowedValues)}]";
            string left = ov.ShortName is not null
                ? $"  {ov.ShortName}, {ov.LongName}{valueHint}"
                : $"  {ov.LongName}{valueHint}";
            optionLines.Add((left, ov.Description, isStd));
        }

        // Sort: non-standard first (in registration order), then standard
        var nonStandard = optionLines.Where(o => !o.IsStandard).ToList();
        var standard = optionLines.Where(o => o.IsStandard).ToList();
        var sorted = nonStandard.Concat(standard).ToList();

        // Calculate alignment
        int maxLeft = sorted.Count > 0 ? sorted.Max(o => o.Left.Length) : 0;
        int alignCol = maxLeft + 2; // minimum 2-space gap

        foreach (var (left, desc, _) in sorted)
        {
            sb.Append(left.PadRight(alignCol));
            sb.AppendLine(desc);
        }

        // Custom sections
        foreach (var (title, body) in _sections)
        {
            sb.AppendLine();
            sb.AppendLine($"{title}:");
            // Indent each line of the body by 2 spaces.
            // Split on both \r\n and \n so Windows-edited strings don't carry trailing \r.
            foreach (string line in body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string trimmed = line.TrimStart();
                if (trimmed.Length > 0)
                {
                    sb.AppendLine($"  {trimmed}");
                }
                else
                {
                    sb.AppendLine();
                }
            }
        }

        if (_expandGlobPositionals && !_commandMode)
        {
            sb.AppendLine();
            sb.AppendLine("Wildcards (Windows):");
            sb.AppendLine($"  On Windows, * and ? in path arguments are expanded by {_toolName} itself");
            sb.AppendLine("  (cmd and PowerShell don't expand them). [...] is matched literally and");
            sb.AppendLine("  '**' is not supported. A pattern matching nothing is passed through");
            sb.AppendLine("  unchanged. In cmd, quote the pattern to suppress expansion.");
        }

        // Examples (same data the --describe JSON carries; rendered here so --help is self-contained)
        if (_examples.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Examples:");
            int maxCmd = _examples.Max(e => e.Command.Length);
            int exampleAlign = maxCmd + 2; // minimum 2-space gap, matching the options table
            foreach (var (command, description) in _examples)
            {
                if (description.Length > 0)
                {
                    sb.Append("  ").Append(command.PadRight(exampleAlign));
                    sb.AppendLine(description);
                }
                else
                {
                    sb.AppendLine($"  {command}");
                }
            }
        }

        // Composes with (same data --describe carries; rendered here so --help is self-contained
        // and a human reading it can discover the cross-tool pipelines the JSON surface advertises)
        if (_composability.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Composes with:");
            int maxPattern = _composability.Max(c => c.Pattern.Length);
            int composeAlign = maxPattern + 2; // minimum 2-space gap, matching the options/examples tables
            foreach (var (_, pattern, description) in _composability)
            {
                if (description.Length > 0)
                {
                    sb.Append("  ").Append(pattern.PadRight(composeAlign));
                    sb.AppendLine(description);
                }
                else
                {
                    sb.AppendLine($"  {pattern}");
                }
            }
        }

        // Exit codes
        if (_exitCodes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Exit Codes:");
            int maxCode = _exitCodes.Max(e => e.Code.ToString(CultureInfo.InvariantCulture).Length);
            foreach (var (code, desc) in _exitCodes)
            {
                string codeStr = code.ToString(CultureInfo.InvariantCulture);
                sb.AppendLine($"  {codeStr.PadRight(maxCode + 2)}{desc}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    internal string GenerateDescribe()
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();

            // Versions the STRUCTURE of this envelope only (field names/nesting/types).
            // Additive fields do NOT bump it; renames/removals/type changes DO.
            // See docs/STABILITY.md. Bump rule must be honoured by any future editor.
            writer.WriteNumber("schema_version", DescribeSchemaVersion);

            // tool, version, maturity (optional), description
            writer.WriteString("tool", _toolName);
            writer.WriteString("version", _version);
            if (_maturity is not null)
            {
                // Exhaustive switch (not a ternary): a future third tier maps to a loud
                // throw, never silently to "fresh" — the contract snapshots would otherwise
                // bless the wrong value on a new tool's first generation.
                writer.WriteString("maturity", _maturity.Value switch
                {
                    ToolMaturity.Core => "core",
                    ToolMaturity.Fresh => "fresh",
                    _ => throw new ArgumentOutOfRangeException(nameof(_maturity), _maturity,
                        "Unhandled ToolMaturity value — add a case here when extending the enum."),
                });
            }
            if (_description is not null)
            {
                writer.WriteString("description", _description);
            }

            // platform
            if (_platformScope is not null)
            {
                writer.WritePropertyName("platform");
                writer.WriteStartObject();
                writer.WriteString("scope", _platformScope);
                if (_platformReplaces is not null && _platformReplaces.Length > 0)
                {
                    writer.WriteStartArray("replaces");
                    foreach (string r in _platformReplaces)
                    {
                        writer.WriteStringValue(r);
                    }
                    writer.WriteEndArray();
                }
                if (_platformValueWindows is not null)
                {
                    writer.WriteString("value_on_windows", _platformValueWindows);
                }
                if (_platformValueUnix is not null)
                {
                    writer.WriteString("value_on_unix", _platformValueUnix);
                }
                writer.WriteEndObject();
            }

            // prefer_default_when — omitted entirely when unset or empty (absence = no guidance)
            if (_preferDefaultWhen is not null && _preferDefaultWhen.Length > 0)
            {
                writer.WriteStartArray("prefer_default_when");
                foreach (string entry in _preferDefaultWhen)
                {
                    writer.WriteStringValue(entry);
                }
                writer.WriteEndArray();
            }

            // glob_expansion — gate on !_commandMode to match the runtime hook and --help
            // section, so a misconfigured CommandMode tool can't advertise a capability
            // the runtime never applies (quality-review finding, Tasks 6-7).
            if (_expandGlobPositionals && !_commandMode)
            {
                writer.WriteStartObject("glob_expansion");
                writer.WriteBoolean("positionals", true);
                if (_globSkipFirst > 0)
                {
                    writer.WriteNumber("skip_first", _globSkipFirst);
                }
                writer.WriteString("syntax", "* and ? in any path segment");
                writer.WriteStartArray("not_patterns");
                writer.WriteStringValue("[...] (matched literally; legal Windows filename chars)");
                writer.WriteStringValue("** (rejected with usage error)");
                writer.WriteEndArray();
                writer.WriteString("no_match", "literal passthrough");
                writer.WriteString("quoting", "quoted args not expanded (effective from cmd; PowerShell strips quotes)");
                writer.WriteBoolean("windows_only", true);
                writer.WriteEndObject();
            }

            // usage
            var usageSb = new StringBuilder();
            usageSb.Append($"{_toolName} [options]");
            if (_commandMode)
            {
                usageSb.Append(" [--] <command> [args...]");
            }
            else if (_positionalLabel is not null)
            {
                usageSb.Append($" [{_positionalLabel}]");
            }
            writer.WriteString("usage", usageSb.ToString());

            // options (flags + options + list options + optional-value options)
            bool hasOptions = _flags.Count > 0 || _options.Count > 0 || _listOptions.Count > 0 || _optionalValueOptions.Count > 0;
            if (hasOptions)
            {
                writer.WriteStartArray("options");

                foreach (FlagDef f in _flags)
                {
                    writer.WriteStartObject();
                    writer.WriteString("long", f.LongName);
                    if (f.ShortName is not null)
                    {
                        writer.WriteString("short", f.ShortName);
                    }
                    writer.WriteString("type", "flag");
                    writer.WriteString("description", f.Description);
                    writer.WriteBoolean("repeatable", false);
                    writer.WriteEndObject();
                }

                foreach (OptionDef o in _options)
                {
                    writer.WriteStartObject();
                    writer.WriteString("long", o.LongName);
                    if (o.ShortName is not null)
                    {
                        writer.WriteString("short", o.ShortName);
                    }
                    string typeStr = o.Type switch
                    {
                        OptionType.Int => "int",
                        OptionType.Double => "double",
                        _ => "string"
                    };
                    writer.WriteString("type", typeStr);
                    writer.WriteString("placeholder", o.Placeholder);
                    writer.WriteString("description", o.Description);
                    writer.WriteBoolean("repeatable", false);
                    writer.WriteEndObject();
                }

                foreach (ListOptionDef l in _listOptions)
                {
                    writer.WriteStartObject();
                    writer.WriteString("long", l.LongName);
                    if (l.ShortName is not null)
                    {
                        writer.WriteString("short", l.ShortName);
                    }
                    writer.WriteString("type", "string");
                    writer.WriteString("placeholder", l.Placeholder);
                    writer.WriteString("description", l.Description);
                    writer.WriteBoolean("repeatable", true);
                    writer.WriteEndObject();
                }

                foreach (OptionalValueOptionDef ov in _optionalValueOptions)
                {
                    writer.WriteStartObject();
                    writer.WriteString("long", ov.LongName);
                    if (ov.ShortName is not null)
                    {
                        writer.WriteString("short", ov.ShortName);
                    }
                    writer.WriteString("type", "optional-value");
                    writer.WriteStartArray("allowed_values");
                    foreach (string v in ov.AllowedValues)
                    {
                        writer.WriteStringValue(v);
                    }
                    writer.WriteEndArray();
                    writer.WriteString("default_when_bare", ov.DefaultWhenBare);
                    writer.WriteString("description", ov.Description);
                    writer.WriteBoolean("repeatable", false);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            // exit_codes
            if (_exitCodes.Count > 0)
            {
                writer.WriteStartArray("exit_codes");
                foreach (var (code, description) in _exitCodes)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("code", code);
                    writer.WriteString("description", description);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            // io
            if (_stdinDescription is not null || _stdoutDescription is not null || _stderrDescription is not null)
            {
                writer.WritePropertyName("io");
                writer.WriteStartObject();
                if (_stdinDescription is not null)
                {
                    writer.WriteString("stdin", _stdinDescription);
                }
                if (_stdoutDescription is not null)
                {
                    writer.WriteString("stdout", _stdoutDescription);
                }
                if (_stderrDescription is not null)
                {
                    writer.WriteString("stderr", _stderrDescription);
                }
                writer.WriteEndObject();
            }

            // examples
            if (_examples.Count > 0)
            {
                writer.WriteStartArray("examples");
                foreach (var (command, description) in _examples)
                {
                    writer.WriteStartObject();
                    writer.WriteString("command", command);
                    writer.WriteString("description", description);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            // composes_with
            if (_composability.Count > 0)
            {
                writer.WriteStartArray("composes_with");
                foreach (var (tool, pattern, description) in _composability)
                {
                    writer.WriteStartObject();
                    writer.WriteString("tool", tool);
                    writer.WriteString("pattern", pattern);
                    writer.WriteString("description", description);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            // json_output_fields
            if (_jsonFields.Count > 0)
            {
                writer.WriteStartArray("json_output_fields");
                foreach (var (name, type, description) in _jsonFields)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", name);
                    writer.WriteString("type", type);
                    writer.WriteString("description", description);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>Definition of a boolean flag (e.g. --verbose).</summary>
    internal sealed record FlagDef(string LongName, string? ShortName, string Description);

    /// <summary>Definition of an optional-value flag (e.g. --color: bare uses DefaultWhenBare,
    /// or --color=VALUE where VALUE must be one of AllowedValues).</summary>
    internal sealed record OptionalValueOptionDef(
        string LongName, string? ShortName, string Description, string[] AllowedValues, string DefaultWhenBare);

    /// <summary>The value type of a parsed option.</summary>
    internal enum OptionType
    {
        /// <summary>String value.</summary>
        String,
        /// <summary>Integer value.</summary>
        Int,
        /// <summary>Double-precision floating-point value.</summary>
        Double
    }

    /// <summary>Definition of a single-value option (e.g. --output FILE).</summary>
    internal sealed record OptionDef(
        string LongName, string? ShortName, string Placeholder, string Description,
        OptionType Type,
        Func<int, string?>? IntValidate = null,
        Func<double, string?>? DoubleValidate = null);

    /// <summary>Definition of a repeatable list option (e.g. --ext .cs --ext .txt).</summary>
    internal sealed record ListOptionDef(string LongName, string? ShortName, string Placeholder, string Description);

    /// <summary>Definition of a flag alias that expands to an option+value pair (e.g. -9 → --level 9).</summary>
    internal sealed record AliasDef(string Alias, string TargetOption, string Value);
}
