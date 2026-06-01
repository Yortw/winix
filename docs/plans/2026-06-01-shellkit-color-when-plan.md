# ShellKit `--color=when` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add GNU-style `--key=value` parsing to ShellKit's `CommandLineParser` and use it to make `--color` an optional-value flag accepting `auto|always|never`, with zero behaviour change for existing tools.

**Architecture:** Additive change to `Yort.ShellKit`. The `Parse` loop gains a `--key=value` split (long-options-only) routed to existing option kinds; a new `OptionalValueOptionDef` models `--color` (bare→`always`, `=value` enum-validated); `ParseResult.ResolveColor` reads `--color`'s value internally while keeping its public signature; `--help`/`--describe` advertise `--color[=auto|always|never]`. Space-separated forms, bare `--color`, and `--no-color` all keep working.

**Tech Stack:** C# / .NET 10, NativeAOT, xUnit, `Yort.ShellKit`.

**Spec:** `docs/plans/2026-06-01-shellkit-color-when-design.md` + ADR `docs/plans/2026-06-01-shellkit-color-when-adr.md`.

---

## File structure

- **Modify** `src/Yort.ShellKit/CommandLineParser.cs` — `=`-split in `Parse`; `OptionalValueOptionDef` record + `OptionalValueOption(...)` builder + `_optionalValueOptions`/`_optionalValueLookup`; `StandardFlags` `--color`; `GenerateHelp`/`GenerateDescribe` rendering.
- **Modify** `src/Yort.ShellKit/ParseResult.cs` — `ResolveColor` internals.
- **Create** `tests/Yort.ShellKit.Tests/EqualsSyntaxTests.cs` — general `--key=value` parsing.
- **Create** `tests/Yort.ShellKit.Tests/OptionalValueColorTests.cs` — `--color` optional-value parsing + describe/help rendering.
- **Create** `tests/Yort.ShellKit.Tests/ColorResolutionTests.cs` — `ResolveColor` + `ResolveUseColor` precedence.

No `ConsoleEnv.cs` change (we reuse `ResolveUseColor`).

---

## Task 1: General `--key=value` parsing for existing option kinds

Adds the `=`-split and routes the attached value to string/int/double/list options; a boolean flag with `=value` is an error. `--color` is NOT touched yet (still a boolean `Flag`), so this task is self-contained.

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs` (the `Parse` per-arg routing region)
- Test: `tests/Yort.ShellKit.Tests/EqualsSyntaxTests.cs`

- [ ] **Step 1: Write the failing tests** — create `tests/Yort.ShellKit.Tests/EqualsSyntaxTests.cs`:

```csharp
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class EqualsSyntaxTests
{
    private static CommandLineParser NewParser() =>
        new CommandLineParser("t", "1.0")
            .Option("--output", "-o", "FILE", "output file")
            .IntOption("--level", null, "N", "level", validate: v => v < 0 ? "must be >= 0" : null)
            .DoubleOption("--ratio", null, "R", "ratio")
            .ListOption("--ext", null, "EXT", "extensions")
            .Flag("--verbose", "-v", "verbose");

    [Fact]
    public void StringOption_EqualsForm_ParsesValue()
    {
        var r = NewParser().Parse(new[] { "--output=out.txt" });
        Assert.False(r.HasErrors);
        Assert.Equal("out.txt", r.GetString("--output"));
    }

    [Fact]
    public void StringOption_SpaceForm_StillWorks()
    {
        var r = NewParser().Parse(new[] { "--output", "out.txt" });
        Assert.False(r.HasErrors);
        Assert.Equal("out.txt", r.GetString("--output"));
    }

    [Fact]
    public void IntOption_EqualsForm_ParsesAndValidates()
    {
        var ok = NewParser().Parse(new[] { "--level=3" });
        Assert.False(ok.HasErrors);
        Assert.Equal(3, ok.GetInt("--level"));

        var bad = NewParser().Parse(new[] { "--level=abc" });
        Assert.True(bad.HasErrors);

        var invalid = NewParser().Parse(new[] { "--level=-1" });
        Assert.True(invalid.HasErrors); // validator: must be >= 0
    }

    [Fact]
    public void ListOption_EqualsForm_CollectsAndMixesWithSpaceForm()
    {
        var r = NewParser().Parse(new[] { "--ext=.cs", "--ext", ".txt" });
        Assert.False(r.HasErrors);
        Assert.Equal(new[] { ".cs", ".txt" }, r.GetList("--ext"));
    }

    [Fact]
    public void ValueContainingEquals_SplitsOnFirstOnly()
    {
        var r = NewParser().Parse(new[] { "--output=a=b.txt" });
        Assert.False(r.HasErrors);
        Assert.Equal("a=b.txt", r.GetString("--output"));
    }

    [Fact]
    public void EmptyAttachedValue_OnStringOption_IsAllowed()
    {
        var r = NewParser().Parse(new[] { "--output=" });
        Assert.False(r.HasErrors);
        Assert.Equal("", r.GetString("--output"));
    }

    [Fact]
    public void BooleanFlag_WithAttachedValue_IsError()
    {
        var r = NewParser().Parse(new[] { "--verbose=true" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("takes no value"));
    }

    [Fact]
    public void UnknownEqualsToken_ReportsKeyNotWholeToken()
    {
        var r = NewParser().Parse(new[] { "--nope=x" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("--nope") && !e.Contains("--nope=x"));
    }

    [Fact]
    public void ShortFlag_IsNotEqualsSplit()
    {
        // "-o=x" is not a long token; it should NOT be split. -o expects a space-separated value,
        // so "-o=x" with no following value is treated as -o consuming "=x"? No: "-o=x" is a single
        // token that doesn't match "-o", so it is an unknown option.
        var r = NewParser().Parse(new[] { "-o=x" });
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void SpaceSeparatedValueContainingEquals_Unaffected()
    {
        var r = NewParser().Parse(new[] { "--output", "k=v" });
        Assert.False(r.HasErrors);
        Assert.Equal("k=v", r.GetString("--output"));
    }
}
```

- [ ] **Step 2: Run the tests — expect FAIL**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~EqualsSyntaxTests"`
Expected: FAIL (`--output=out.txt` is currently "unknown option").

- [ ] **Step 3: Implement the `=`-split in `Parse`**

In `src/Yort.ShellKit/CommandLineParser.cs`, replace the routing region of the `Parse` per-arg loop — everything from the `// Check aliases first` comment down to and including the `// Unknown flag` `errors.Add(...)` line (currently approx. lines 332–458) — with this block. (The alias body is unchanged; only the surrounding routing gains `=`-handling. The optional-value block is added in Task 2.)

```csharp
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
```

- [ ] **Step 4: Run the tests — expect PASS**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~EqualsSyntaxTests"`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/EqualsSyntaxTests.cs
git commit -m "feat(shellkit): general --key=value parsing for all options (long-only, additive)"
```

---

## Task 2: `OptionalValueOptionDef` + `--color` optional-value flag

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs` (record, builder, fields, lookup, optional-value routing, `StandardFlags`)
- Test: `tests/Yort.ShellKit.Tests/OptionalValueColorTests.cs`

- [ ] **Step 1: Write the failing tests** — create `tests/Yort.ShellKit.Tests/OptionalValueColorTests.cs`:

```csharp
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class OptionalValueColorTests
{
    private static CommandLineParser NewParser() =>
        new CommandLineParser("t", "1.0").StandardFlags();

    [Fact]
    public void Bare_Color_ResolvesToDefaultAlways()
    {
        var r = NewParser().Parse(new[] { "--color" });
        Assert.False(r.HasErrors);
        Assert.True(r.Has("--color"));
        Assert.Equal("always", r.GetString("--color"));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("always")]
    [InlineData("never")]
    public void Color_EqualsValue_Parses(string when)
    {
        var r = NewParser().Parse(new[] { $"--color={when}" });
        Assert.False(r.HasErrors);
        Assert.Equal(when, r.GetString("--color"));
    }

    [Fact]
    public void Color_BadValue_IsError()
    {
        var r = NewParser().Parse(new[] { "--color=purple" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("--color") && e.Contains("auto, always, never"));
    }

    [Fact]
    public void Color_EmptyValue_IsError()
    {
        var r = NewParser().Parse(new[] { "--color=" });
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void Color_Absent_IsNotPresent()
    {
        var r = NewParser().Parse(System.Array.Empty<string>());
        Assert.False(r.Has("--color"));
    }

    [Fact]
    public void NoColor_StillABooleanFlag()
    {
        var r = NewParser().Parse(new[] { "--no-color" });
        Assert.False(r.HasErrors);
        Assert.True(r.Has("--no-color"));
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~OptionalValueColorTests"`
Expected: FAIL (`--color=auto` currently "takes no value" since `--color` is still a boolean `Flag`).

- [ ] **Step 3: Add the record, fields, builder, and lookup**

In `CommandLineParser.cs`:

(a) Add the record next to the other defs (near `FlagDef`/`OptionDef`, ~line 901):
```csharp
    /// <summary>Definition of an optional-value flag (e.g. --color: bare uses DefaultWhenBare,
    /// or --color=VALUE where VALUE must be one of AllowedValues).</summary>
    internal sealed record OptionalValueOptionDef(
        string LongName, string? ShortName, string Description, string[] AllowedValues, string DefaultWhenBare);
```

(b) Add the backing list with the other lists (~line 33):
```csharp
    private readonly List<OptionalValueOptionDef> _optionalValueOptions = new();
```

(c) Add the lookup field with the other lookups (~line 53):
```csharp
    private Dictionary<string, OptionalValueOptionDef>? _optionalValueLookup;
```

(d) Add the fluent builder next to `ListOption` (~line 140):
```csharp
    /// <summary>Registers an optional-value flag: valid bare (resolves to <paramref name="defaultWhenBare"/>)
    /// or as --name=VALUE where VALUE must be one of <paramref name="allowedValues"/>.</summary>
    public CommandLineParser OptionalValueOption(string longName, string? shortName, string description,
        string[] allowedValues, string defaultWhenBare)
    {
        ThrowIfParsed();
        _optionalValueOptions.Add(new OptionalValueOptionDef(longName, shortName, description, allowedValues, defaultWhenBare));
        return this;
    }
```

(e) Build the lookup in `BuildLookups()` (after the alias loop, ~line 549):
```csharp
        _optionalValueLookup = new Dictionary<string, OptionalValueOptionDef>(StringComparer.Ordinal);
        foreach (OptionalValueOptionDef ov in _optionalValueOptions)
        {
            _optionalValueLookup[ov.LongName] = ov;
            if (ov.ShortName is not null)
            {
                _optionalValueLookup[ov.ShortName] = ov;
            }
        }
```
Also change the early-return guard at the top of `BuildLookups` if needed — it currently returns when `_flagLookup is not null`; that still correctly guards all lookups since they are all built together. No change needed.

- [ ] **Step 4: Insert the optional-value routing block in `Parse`**

In the routing region (from Task 1), insert this block **immediately before `// Check flags`** (so `--color` is matched before the generic flag lookup):
```csharp
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
```

- [ ] **Step 5: Change `StandardFlags` to register `--color` as optional-value**

In `StandardFlags()` (~line 180), replace:
```csharp
        Flag("--color", "Force colored output");
```
with:
```csharp
        OptionalValueOption("--color", null, "Coloured output: auto (default when omitted), always, or never",
            new[] { "auto", "always", "never" }, "always");
```
Leave `Flag("--no-color", "Disable colored output")` unchanged.

- [ ] **Step 6: Run — expect PASS**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~OptionalValueColorTests"`
Expected: PASS (9 tests incl. Theory cases).

- [ ] **Step 7: Commit**

```bash
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/OptionalValueColorTests.cs
git commit -m "feat(shellkit): --color optional-value flag (bare=always, =auto|always|never, enum-validated)"
```

---

## Task 3: `ResolveColor` rework (signature unchanged, precedence preserved)

**Files:**
- Modify: `src/Yort.ShellKit/ParseResult.cs` (`ResolveColor`)
- Test: `tests/Yort.ShellKit.Tests/ColorResolutionTests.cs`

- [ ] **Step 1: Write the failing tests** — create `tests/Yort.ShellKit.Tests/ColorResolutionTests.cs`:

```csharp
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class ColorResolutionTests
{
    private static ParseResult Parse(params string[] args) =>
        new CommandLineParser("t", "1.0").StandardFlags().Parse(args);

    // Env-forced cases: result does not depend on TTY/NO_COLOR, so deterministic in any test host.

    [Fact]
    public void ColorAlways_ForcesOn()
    {
        Assert.True(Parse("--color=always").ResolveColor());
        Assert.True(Parse("--color").ResolveColor()); // bare → always
    }

    [Fact]
    public void ColorNever_ForcesOff()
    {
        Assert.False(Parse("--color=never").ResolveColor());
    }

    [Fact]
    public void NoColorFlag_ForcesOff()
    {
        Assert.False(Parse("--no-color").ResolveColor());
    }

    [Fact]
    public void ColorAlwaysAndNoColor_Tie_ColorWins()
    {
        // Preserves the existing "colorFlag wins" precedence in ConsoleEnv.ResolveUseColor.
        Assert.True(Parse("--color=always", "--no-color").ResolveColor());
    }

    // Full precedence table tested against the pure helper (no env/TTY dependence).

    [Theory]
    // colorFlag, noColorFlag, noColorEnv, isTerminal => expected
    [InlineData(true,  false, true,  false, true)]   // --color/always overrides NO_COLOR
    [InlineData(true,  true,  false, false, true)]   // colorFlag beats noColorFlag (tie → on)
    [InlineData(false, true,  false, true,  false)]  // never/--no-color off even on a TTY
    [InlineData(false, false, true,  true,  false)]  // NO_COLOR off
    [InlineData(false, false, false, true,  true)]   // auto + TTY → on
    [InlineData(false, false, false, false, false)]  // auto + non-TTY → off
    public void ResolveUseColor_Precedence(bool color, bool noColor, bool noColorEnv, bool isTty, bool expected)
    {
        Assert.Equal(expected, ConsoleEnv.ResolveUseColor(color, noColor, noColorEnv, isTty));
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~ColorResolutionTests"`
Expected: FAIL on the `ColorAlways*`/`ColorNever`/tie cases — current `ResolveColor` calls `Has("--color")` which is now true for any `--color` form (incl. `--color=never`), so `--color=never` would wrongly resolve on. (The `ResolveUseColor_Precedence` Theory passes already — it tests the unchanged pure helper.)

- [ ] **Step 3: Rework `ResolveColor`**

In `src/Yort.ShellKit/ParseResult.cs`, replace the `ResolveColor` body:
```csharp
    public bool ResolveColor(bool checkStdErr = false)
    {
        // --color is now an optional-value flag: read its resolved value (always/never/auto).
        // Map to the existing (colorFlag, noColorFlag) precedence so behaviour is unchanged:
        // always/bare → force on (still overrides NO_COLOR); never/--no-color → force off;
        // auto/absent → fall through to NO_COLOR env then terminal auto-detection.
        bool colorAlways = false;
        bool colorNever = false;
        if (_optionValues.TryGetValue("--color", out string? colorValue))
        {
            colorAlways = string.Equals(colorValue, "always", StringComparison.Ordinal);
            colorNever = string.Equals(colorValue, "never", StringComparison.Ordinal);
        }

        return ConsoleEnv.ResolveUseColor(
            colorAlways,
            colorNever || _flagsSet.Contains("--no-color"),
            ConsoleEnv.IsNoColorEnvSet(),
            ConsoleEnv.IsTerminal(checkStdErr));
    }
```
Update the XML `<summary>` precedence note to read: "explicit `--color`/`--no-color` (always|never) &gt; NO_COLOR env &gt; terminal auto-detection; `--color`/always overrides NO_COLOR."

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~ColorResolutionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Yort.ShellKit/ParseResult.cs tests/Yort.ShellKit.Tests/ColorResolutionTests.cs
git commit -m "feat(shellkit): ResolveColor reads --color value (signature unchanged, precedence preserved)"
```

---

## Task 4: `--help` / `--describe` rendering for optional-value flags

**Files:**
- Modify: `src/Yort.ShellKit/CommandLineParser.cs` (`GenerateHelp`, `GenerateDescribe`)
- Test: add to `tests/Yort.ShellKit.Tests/OptionalValueColorTests.cs`

- [ ] **Step 1: Write the failing tests** — append to `OptionalValueColorTests.cs`:

```csharp
    [Fact]
    public void Help_RendersColorWithAllowedValues()
    {
        string help = new CommandLineParser("t", "1.0").StandardFlags().GenerateHelp();
        Assert.Contains("--color[=auto|always|never]", help);
    }

    [Fact]
    public void Describe_EmitsOptionalValueTypeAndAllowedValues()
    {
        string json = new CommandLineParser("t", "1.0").StandardFlags().GenerateDescribe();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var options = doc.RootElement.GetProperty("options");
        System.Text.Json.JsonElement color = default;
        foreach (var o in options.EnumerateArray())
        {
            if (o.GetProperty("long").GetString() == "--color") { color = o; break; }
        }
        Assert.Equal("optional-value", color.GetProperty("type").GetString());
        Assert.Equal("always", color.GetProperty("default_when_bare").GetString());
        var allowed = color.GetProperty("allowed_values");
        Assert.Equal(3, allowed.GetArrayLength());
        Assert.Equal("auto", allowed[0].GetString());
    }
```
(`GenerateHelp`/`GenerateDescribe` are `internal`; the test project already has `InternalsVisibleTo` for ShellKit.Tests — confirm; if not, add it to `Yort.ShellKit.csproj`.)

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~OptionalValueColorTests"`
Expected: FAIL (help has no `--color` line now that it left `_flags`; describe omits it).

- [ ] **Step 3: Render optional-value in `GenerateHelp`**

In `GenerateHelp`, after the `foreach (ListOptionDef l in _listOptions)` block (~line 606, still within scope of the local `standardNames` array), add:
```csharp
        foreach (OptionalValueOptionDef ov in _optionalValueOptions)
        {
            bool isStd = Array.IndexOf(standardNames, ov.LongName) >= 0;
            string valueHint = $"[={string.Join("|", ov.AllowedValues)}]";
            string left = ov.ShortName is not null
                ? $"  {ov.ShortName}, {ov.LongName}{valueHint}"
                : $"  {ov.LongName}{valueHint}";
            optionLines.Add((left, ov.Description, isStd));
        }
```

- [ ] **Step 4: Render optional-value in `GenerateDescribe`**

(a) Update the `hasOptions` guard (~line 758):
```csharp
            bool hasOptions = _flags.Count > 0 || _options.Count > 0 || _listOptions.Count > 0 || _optionalValueOptions.Count > 0;
```
(b) After the `foreach (ListOptionDef l in _listOptions)` describe block (~line 811, before `writer.WriteEndArray();`), add:
```csharp
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
```

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test tests/Yort.ShellKit.Tests --filter "FullyQualifiedName~OptionalValueColorTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Yort.ShellKit/CommandLineParser.cs tests/Yort.ShellKit.Tests/OptionalValueColorTests.cs
git commit -m "feat(shellkit): render --color[=auto|always|never] in --help and --describe"
```

---

## Task 5: Full-solution back-compat verification + AOT smoke

The keystone: every existing tool must build and pass unchanged (no tool code was touched).

- [ ] **Step 1: Full solution build**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 2: Full solution test**

Run: `dotnet test Winix.sln`
Expected: 0 failed across all assemblies (ShellKit gains the new tests; all other tools' suites pass unchanged — this is the back-compat proof).

- [ ] **Step 3: AOT smoke of a representative tool's `--color` forms**

A tool that emits colour (e.g. `when`) exercises the real ResolveColor path through a native binary.
Run: `dotnet publish src/when/when.csproj -c Release -r win-x64`
Then run each and confirm no usage error and sensible behaviour:
- `when --color=always now` (forces colour on stderr/stdout per the tool)
- `when --color=never now`
- `when --color now` (bare → always)
- `when --color=bogus now` → exit 125 with the allowed-values message
- `when --no-color now`
Capture outputs to `artifacts/color-when-smoke/` (gitignored).

- [ ] **Step 4: Verify `--describe`/`--help` for a tool show the new form**

Run: `dotnet run --project src/when -- --describe` and confirm the `--color` option object has `type: "optional-value"` and `allowed_values: ["auto","always","never"]`. Run `--help` and confirm `--color[=auto|always|never]` appears.

- [ ] **Step 5: Commit (if any artifacts/notes warrant) or note completion**

No code commit expected here (verification only). If the AOT smoke surfaced an issue, fix it under the relevant task and re-run.

---

## Final verification

- [ ] `dotnet test Winix.sln` — 0 failed.
- [ ] `dotnet build Winix.sln` — 0 warnings.
- [ ] AOT smoke (Step 3) green: all `--color` forms behave; bad value → 125.
- [ ] `--describe`/`--help` advertise `--color[=auto|always|never]` for a rebuilt tool.

## Notes / known plan risks (resolve during execution)

- **Routing-region replacement (Task 1 Step 3)** is the largest single edit — it replaces a contiguous block. Verify the alias body is preserved verbatim (only the surrounding routing changed) and that `lookupKey` is used for every lookup while `arg` is still used for the alias check and the earlier `--`/non-flag checks.
- **`InternalsVisibleTo`** — Task 4 tests call `internal` `GenerateHelp`/`GenerateDescribe`. Confirm `Yort.ShellKit.csproj` exposes internals to `Yort.ShellKit.Tests` (it should, since existing tests cover internals); if not, add it.
- **`auto`/absent ResolveColor cases** are TTY-dependent and intentionally NOT asserted via `ResolveColor` (only via the pure `ResolveUseColor` Theory) to keep tests host-independent.
- This plan is the **parser surface only**. It emits no colour. The emit-fixes (trash/hcat/wargs), end-to-end colour regression tests, and the 15-surface `WHEN`→`=WHEN` doc fix are the separate colour sweep (task #15).

---

## Adversarial Review Integration — Pass 1 (2026-06-01)

A fresh subagent reviewed this plan against the 15-category taxonomy: **3 blockers, 6 test gaps, 5 explicit defers**. Dispositions below; fold the added tests into the named test files and apply the two code changes (F8 placement, F9 seam) during the relevant task.

| ID | Bucket | Disposition |
|---|---|---|
| **F1** positional/CommandMode arg starting `--` containing `=` not split | Test gap | **Tests added** (T-A below). The split is correctly short-circuited (the `--` and non-flag branches run *before* the routing region), but it was unverified. |
| **F2** `--=x` / leading-`=` value contract unspecified | Test gap + design note | **Tests added** (T-A) + design §3 contract line. (`--=x` → `unknown option: --`; `--key==v` → value `=v` passes through.) Downgraded from blocker: behaviour is acceptable, just was unpinned. |
| **F3** duplicate / last-wins contract untested | Test gap + design note | **Tests added** (T-A, T-B) + design §3/§4 line: options & optional-value → last-wins; lists → append. |
| **F4** enum case-sensitivity (`--color=Always`) | Test gap + design note | **Test added** (T-B) + design §4 / ADR D6: values are case-sensitive. |
| **F5** value-looks-like-flag (`--output=--foo`) | Test gap | **Test added** (T-A). |
| **F6** flag-shaped enum value (`--color=--always`) | Test gap | **Test added** (T-B). |
| **F7** loose bad-value error assertion | Test gap | **Tightened** (T-B `Color_BadValue_ErrorMessageIsExact` pins the exact string). |
| **F8** `BuildLookups` early-return could leave `_optionalValueLookup` null on 2nd parse | Plan blocker | **Fixed in plan:** Task 2 Step 3(e) MUST place the `_optionalValueLookup` assignment *after* the `if (_flagLookup is not null) return;` guard, alongside the other lookups (it already is, in the spec) — and **test added** (T-B `Parse_CalledTwice_OptionalValueStillResolves`) to lock it. |
| **F9** `ResolveColor` never verifies auto/absent → TTY mapping | Plan blocker | **Fixed in plan:** Task 3 implementation now splits into a testable `internal ResolveColorCore(isTerminal, noColorEnv)` seam (code below); **tests added** (T-C) to verify the auto/absent argument-mapping deterministically. |
| **F10** `Has("--color")`/`GetString("--color")` blast radius unaudited | Plan blocker | **Fixed in plan:** Task 5 gains a caller-audit step (below) — grep all tool source for direct `--color` reads outside `ResolveColor`; any direct caller is a regression site. |
| **F11** `-o=x` short-flag loose assertion | Explicit defer + tighten | **Tightened** (T-A `ShortFlag_IsNotEqualsSplit` asserts `unknown option: -o=x`) + ADR D4 note. |
| **F12** `--help=x` / handled-flag-with-value precedence unspecified | Explicit defer + test | **Test added** (T-B `Help_WithAttachedValue_IsError`) + design §7 line. |

### T-A — add to `EqualsSyntaxTests.cs`

```csharp
    [Fact]
    public void CommandMode_ArgsAfterSeparator_NotEqualsSplit()  // F1
    {
        var p = new CommandLineParser("t", "1.0").CommandMode().Flag("--verbose", "-v", "v");
        var r = p.Parse(new[] { "--", "child", "--opt=v" });
        Assert.False(r.HasErrors);
        Assert.Equal(new[] { "child", "--opt=v" }, r.Command);
    }

    [Fact]
    public void CommandMode_FirstNonFlagStops_LaterEqualsTokenNotSplit()  // F1
    {
        var p = new CommandLineParser("t", "1.0").CommandMode().Flag("--verbose", "-v", "v");
        var r = p.Parse(new[] { "child", "--opt=v" });
        Assert.False(r.HasErrors);
        Assert.Equal(new[] { "child", "--opt=v" }, r.Command);
    }

    [Fact]
    public void Positional_TokenWithEquals_NotSplit()  // F1
    {
        var p = new CommandLineParser("t", "1.0").Positional("files").Flag("--verbose", "-v", "v");
        var r = p.Parse(new[] { "foo=bar" });
        Assert.False(r.HasErrors);
        Assert.Equal(new[] { "foo=bar" }, r.Positionals);
    }

    [Fact]
    public void EmptyKey_DashDashEquals_IsUnknownOption()  // F2
    {
        var r = NewParser().Parse(new[] { "--=x" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("unknown option: --"));
    }

    [Fact]
    public void LeadingEqualsInValue_PassesThroughToStringOption()  // F2
    {
        var r = NewParser().Parse(new[] { "--output==v" });
        Assert.False(r.HasErrors);
        Assert.Equal("=v", r.GetString("--output"));
    }

    [Fact]
    public void DuplicateOption_LastWins()  // F3
    {
        var r = NewParser().Parse(new[] { "--output=a", "--output=b" });
        Assert.False(r.HasErrors);
        Assert.Equal("b", r.GetString("--output"));
    }

    [Fact]
    public void StringOption_EqualsForm_ValueLooksLikeFlag()  // F5
    {
        var r = NewParser().Parse(new[] { "--output=--foo" });
        Assert.False(r.HasErrors);
        Assert.Equal("--foo", r.GetString("--output"));
    }
```

Also **replace** the existing `ShortFlag_IsNotEqualsSplit` body (F11 tighten) with:
```csharp
    [Fact]
    public void ShortFlag_IsNotEqualsSplit()  // F11: -o=x is a single unknown token (long-only =-split)
    {
        var r = NewParser().Parse(new[] { "-o=x" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("unknown option: -o=x"));
    }
```

### T-B — add to `OptionalValueColorTests.cs`

```csharp
    [Fact]
    public void Color_DuplicateValues_LastWins()  // F3
    {
        var r = NewParser().Parse(new[] { "--color=always", "--color=never" });
        Assert.False(r.HasErrors);
        Assert.Equal("never", r.GetString("--color"));
    }

    [Fact]
    public void Color_MixedCaseValue_IsError()  // F4: values are case-sensitive
    {
        var r = NewParser().Parse(new[] { "--color=Always" });
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void Color_FlagShapedValue_IsError()  // F6
    {
        var r = NewParser().Parse(new[] { "--color=--always" });
        Assert.True(r.HasErrors);
    }

    [Fact]
    public void Color_BadValue_ErrorMessageIsExact()  // F7: pin the docs-referenced contract
    {
        var r = NewParser().Parse(new[] { "--color=purple" });
        Assert.Contains(r.Errors, e => e.Contains("--color: 'purple' is not one of: auto, always, never"));
    }

    [Fact]
    public void Parse_CalledTwice_OptionalValueStillResolves()  // F8: locks the BuildLookups guard
    {
        var p = NewParser();
        var r1 = p.Parse(new[] { "--color=never" });
        Assert.Equal("never", r1.GetString("--color"));
        var r2 = p.Parse(new[] { "--color=always" });
        Assert.Equal("always", r2.GetString("--color"));
    }

    [Fact]
    public void Help_WithAttachedValue_IsError()  // F12
    {
        var r = NewParser().Parse(new[] { "--help=x" });
        Assert.True(r.HasErrors);
        Assert.False(r.IsHandled); // malformed --help=x does not trigger help; it errors
    }
```

### F9 — Task 3 implementation change (testable seam)

Replace the Task 3 Step 3 `ResolveColor` body with a thin wrapper over an internal seam so the auto/absent → env/TTY mapping is testable without host-TTY dependence:
```csharp
    public bool ResolveColor(bool checkStdErr = false)
        => ResolveColorCore(ConsoleEnv.IsTerminal(checkStdErr), ConsoleEnv.IsNoColorEnvSet());

    /// <summary>Testable core of <see cref="ResolveColor"/>: maps the --color value
    /// (always/never/auto) and --no-color to the colour decision given the supplied
    /// terminal/NO_COLOR state. Internal seam so auto/absent cases are deterministic in tests.</summary>
    internal bool ResolveColorCore(bool isTerminal, bool noColorEnv)
    {
        bool colorAlways = false;
        bool colorNever = false;
        if (_optionValues.TryGetValue("--color", out string? colorValue))
        {
            colorAlways = string.Equals(colorValue, "always", StringComparison.Ordinal);
            colorNever = string.Equals(colorValue, "never", StringComparison.Ordinal);
        }

        return ConsoleEnv.ResolveUseColor(
            colorAlways,
            colorNever || _flagsSet.Contains("--no-color"),
            noColorEnv,
            isTerminal);
    }
```
(`ResolveColorCore` is `internal`; the ShellKit.Tests project already sees internals — confirm `InternalsVisibleTo`, same note as Task 4.)

### T-C — add to `ColorResolutionTests.cs` (verifies the auto/absent mapping F9)

```csharp
    [Theory]
    [InlineData("--color=auto", true, false, true)]    // auto + TTY → on
    [InlineData("--color=auto", false, false, false)]  // auto + non-TTY → off
    [InlineData("--color=always", false, false, true)] // always forced on even non-TTY
    [InlineData("--color=never", true, false, false)]  // never forced off even on a TTY
    public void ResolveColorCore_MapsValueAndEnv(string arg, bool isTty, bool noColorEnv, bool expected)
    {
        var r = new CommandLineParser("t", "1.0").StandardFlags().Parse(new[] { arg });
        Assert.Equal(expected, r.ResolveColorCore(isTty, noColorEnv));
    }

    [Fact]
    public void ResolveColorCore_Absent_UsesTerminalBranch()
    {
        var r = new CommandLineParser("t", "1.0").StandardFlags().Parse(System.Array.Empty<string>());
        Assert.True(r.ResolveColorCore(isTerminal: true, noColorEnv: false));
        Assert.False(r.ResolveColorCore(isTerminal: false, noColorEnv: false));
    }
```

### F10 — Task 5 added step (caller audit, the back-compat keystone)

- [ ] **Task 5 Step 2a: Audit direct `--color` callers**

`Has("--color")` now returns true for `--color=never` too (it stores into `flagsSet`). `ResolveColor` is reworked to compensate, but any tool reading `--color` directly (bypassing `ResolveColor`) would now force colour on even for `--color=never`.
Run a content search across `src/` for `Has("--color")` and `GetString("--color")`. Expected: the ONLY consumer is `ParseResult.ResolveColor`/`ResolveColorCore` (ShellKit). If any tool's own code calls either directly, that is a regression site — report it; it must route through `ResolveColor` instead. (This is read-only verification; fix any hit before declaring done.)

---

## Adversarial Review Integration — Pass 2 (confirming, 2026-06-01)

A second fresh subagent confirmed the architecture is sound (integrable edge/test hardening, not a return to brainstorming) and that the Pass-1 integration is internally consistent (F8 test genuinely guards the lookup; F9 `ResolveColorCore` rows match the design precedence with no host-TTY dependence; the added tests' asserted outcomes match the routing-block code; no design/ADR/plan contradictions). **0 new blockers.** Two minor items, both resolved:

| ID | Bucket | Disposition |
|---|---|---|
| **P2-F1** no `=`-form parse-failure test for the `double` option (int path is tested; double routes through the structurally-identical branch) | Test gap | **Test added** — append to `EqualsSyntaxTests` (below), closing int/double symmetry. |
| **P2-F2** `Help_WithAttachedValue_IsError` asserts `r.IsHandled` — reviewer couldn't read the code to confirm the member name | Verify (resolved) | **Confirmed:** `ParseResult.IsHandled` exists (`public bool IsHandled { get; }`) and `Parse` sets it from `flagsSet.Contains("--help")`; `--help=x` takes the flag-with-value error path and never adds `--help` to `flagsSet`, so `IsHandled` stays false. The test compiles and is correct as written. No change. |

### P2-F1 test — add to `EqualsSyntaxTests.cs`
```csharp
    [Fact]
    public void DoubleOption_EqualsForm_BadValue_IsError()  // P2-F1: int/double symmetry
    {
        var r = NewParser().Parse(new[] { "--ratio=abc" });
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("--ratio") && e.Contains("not a valid number"));
    }
```

**Review status: COMPLETE.** Two passes run (the skill's maximum). The architecture converged at pass 1; pass 2 confirmed the integration with only two minor items, both now closed. Proceed to execution.
