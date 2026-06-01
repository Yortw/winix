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
