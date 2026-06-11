#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Winix.Contract.Tests;

/// <summary>
/// Contract-lock snapshot tests and registry-completeness guards for all
/// <c>--describe</c> surfaces in the Winix suite.
/// </summary>
/// <remarks>
/// Snapshot files live under <c>tests/Winix.Contract.Tests/snapshots/</c>
/// and are committed to source. The <c>WINIX_UPDATE_SNAPSHOTS=1</c> env var
/// regenerates them. See <c>docs/STABILITY.md</c> for the update/bump protocol.
/// </remarks>
public sealed class ContractSnapshotTests
{
    /// <summary>
    /// Theory data: all keys from <see cref="DescribeSurfaces.All"/>.
    /// </summary>
    public static IEnumerable<object[]> Surfaces()
        => DescribeSurfaces.All.Keys.Select(k => new object[] { k });

    /// <summary>
    /// Each <c>--describe</c> surface must match its committed snapshot byte-for-byte
    /// (after version-field masking and LF normalisation). An intentional change
    /// requires regenerating with <c>WINIX_UPDATE_SNAPSHOTS=1</c> and committing the diff.
    /// </summary>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public async Task Describe_matches_committed_snapshot(string key)
    {
        string[] parts = key.Split('/');
        string[] args = parts.Length == 2
            ? new[] { parts[1], "--describe" }
            : new[] { "--describe" };

        var (stdout, stderr, exit) = await ConsoleCapture.RunAsync(() => DescribeSurfaces.All[key](args));

        Assert.Equal(0, exit);
        Assert.Equal("", stderr);

        string actual = Normalise(stdout);

        // Maturity gate (ADR D3): a Winix tool may not ship untiered.
        JsonNode node = JsonNode.Parse(stdout)!;
        Assert.NotNull(node["schema_version"]);
        string? maturity = node["maturity"]?.GetValue<string>();
        Assert.True(maturity is "core" or "fresh",
            $"{key}: maturity is unset or invalid ('{maturity}') — every Winix tool must call .Maturity(...)");

        string snapshotPath = Path.Combine(SnapshotDir, key.Replace('/', '_') + ".describe.json");

        if (Environment.GetEnvironmentVariable("WINIX_UPDATE_SNAPSHOTS") == "1")
        {
            // (Adversarial F4) Update mode writes BOTH the source-tree snapshot (the
            // committed truth, [CallerFilePath]-anchored) AND the bin copy (so a re-run
            // without rebuild compares fresh, not stale). Guard the source path: on a
            // machine where it doesn't exist (CI, relocated checkout) fail with a clear
            // dev-only message rather than writing to a phantom path.
            string sourceDir = SourceSnapshotDir();
            Assert.True(Directory.Exists(sourceDir),
                $"source snapshot dir not found ({sourceDir}) — update mode is a dev-checkout-only operation, not for CI");
            // Ensure the bin snapshots directory exists (first-time generation).
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(Path.Combine(sourceDir, Path.GetFileName(snapshotPath)), actual);
            File.WriteAllText(snapshotPath, actual);
            Assert.Fail($"snapshot regenerated for {key} — update mode always fails so CI can never silently self-update; commit the diff");
        }

        Assert.True(File.Exists(snapshotPath),
            $"no snapshot for {key} — run with WINIX_UPDATE_SNAPSHOTS=1 once and commit snapshots/");
        string expected = File.ReadAllText(snapshotPath);

        // Byte-equal after normalisation. On mismatch the message carries the contract
        // instructions (docs/STABILITY.md): regenerate if intentional; bump
        // CommandLineParser.DescribeSchemaVersion if the envelope STRUCTURE changed.
        Assert.True(expected == actual,
            $"{key}: --describe drifted from the committed contract snapshot.\n" +
            $"Intentional? Re-run with WINIX_UPDATE_SNAPSHOTS=1, commit the snapshot diff, " +
            $"and bump schema_version if the envelope STRUCTURE changed. See docs/STABILITY.md.\n" +
            $"--- expected ---\n{expected}\n--- actual ---\n{actual}");
    }

    /// <summary>
    /// The registry must contain exactly 28 top-level (non-subcommand) surfaces,
    /// one per tool in the suite. A new tool must be added here before shipping.
    /// </summary>
    [Fact]
    public void Every_tool_in_the_suite_is_registered()
    {
        // Tripwire: 28 is the NuGet-ID canon in CLAUDE.md. A new tool added without a
        // registry entry (and snapshot) fails here. The message lists what IS registered
        // so the gap is self-diagnosing rather than a bare "expected 28, got 27".
        var topLevel = DescribeSurfaces.All.Keys.Where(k => !k.Contains('/')).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(topLevel.Count == 29,
            $"expected 29 top-level surfaces (CLAUDE.md NuGet-ID canon), got {topLevel.Count}. " +
            $"Registered: {string.Join(", ", topLevel)}");
    }

    /// <summary>
    /// The only tools that emit distinct per-subcommand <c>--describe</c> envelopes are
    /// qr (5 helpers: wifi, sms, mailto, geo, tel) and mksecret (2 sub-modes: phrase, key);
    /// every other tool's subcommand positional is ignored by <c>--describe</c>. Adding a
    /// subcommand surface without updating these counts fails here.
    /// </summary>
    [Fact]
    public void Subcommand_surface_counts_are_correct()
    {
        // qr: 5 helper surfaces (wifi/sms/mailto/geo/tel); mksecret: 2 sub-modes (phrase/key).
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["qr"] = 5,
            ["mksecret"] = 2,
        };
        foreach (var kv in expected)
        {
            int actual = DescribeSurfaces.All.Keys.Count(
                k => k.StartsWith(kv.Key + "/", StringComparison.Ordinal));
            Assert.True(kv.Value == actual,
                $"{kv.Key}: expected {kv.Value} subcommand surfaces, registry has {actual}");
        }

        // Also lock the TOTAL: a subcommand surface on any OTHER tool would register a
        // slash-key that the per-tool checks above and the 28-count guard both miss.
        int totalSubcommand = DescribeSurfaces.All.Keys.Count(k => k.Contains('/'));
        Assert.True(expected.Values.Sum() == totalSubcommand,
            $"expected {expected.Values.Sum()} subcommand surfaces total, registry has {totalSubcommand} " +
            $"— an unexpected tool grew a subcommand --describe surface.");
    }

    /// <summary>
    /// Every top-level surface must report the maturity tier that matches the
    /// design table: "fresh" for the five tools added in v0.4.0, "core" for everything else.
    /// </summary>
    [Fact]
    public async Task Tier_assignments_match_the_design_table()
    {
        var fresh = new HashSet<string>(StringComparer.Ordinal)
            { "mksecret", "trash", "hcat", "mkauth", "demux", "online" };
        foreach (string key in DescribeSurfaces.All.Keys.Where(k => !k.Contains('/')))
        {
            var (stdout, stderr, exit) = await ConsoleCapture.RunAsync(
                () => DescribeSurfaces.All[key](new[] { "--describe" }));
            // --describe must be a clean exit-0/empty-stderr path; a tool that emits valid
            // JSON yet exits non-zero would otherwise slip past this tier check.
            Assert.True(exit == 0, $"{key}: --describe exited {exit}");
            Assert.True(stderr == "", $"{key}: --describe wrote to stderr: {stderr}");
            string? maturity = JsonNode.Parse(stdout)!["maturity"]?.GetValue<string>();
            string expectedTier = fresh.Contains(key) ? "fresh" : "core";
            Assert.True(expectedTier == maturity, $"{key}: expected maturity '{expectedTier}', got '{maturity}'");
        }
    }

    /// <summary>
    /// No two registry keys may produce the same snapshot filename
    /// (after <c>'/'</c> → <c>'_'</c> substitution). A collision would cause one
    /// snapshot to silently overwrite another during update mode.
    /// </summary>
    [Fact]
    public void No_two_registry_keys_collide_on_snapshot_filename()
    {
        var names = DescribeSurfaces.All.Keys
            .Select(k => k.Replace('/', '_'))
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(names.Count == 0, "snapshot filename collision: " + string.Join(", ", names));
    }

    private static string Normalise(string describeJson)
    {
        JsonNode node = JsonNode.Parse(describeJson)!;
        // version is the dev/release build number — masked because it's runtime-derived,
        // not a stable contract value. Anything else that varies is a contract finding,
        // not noise — EXCEPT the one documented runtime-capability field below.
        node["version"] = "<normalised>";
        // digest reshapes its description by SHA-3 availability, probed at runtime
        // (Winix.Digest I-SFH-18: don't advertise SHA-3 where the running platform lacks it).
        // That makes the description runtime-capability-derived, like version — so mask it too,
        // else the snapshot would only match the platform it was generated on (Windows 10 lacks
        // SHA-3; recent Linux has it). digest's STRUCTURE (options/exit_codes/prefer_default_when/
        // maturity) stays fully locked; the description prose is reconciled against
        // README/man/docs/ai separately.
        if (node["tool"]?.GetValue<string>() == "digest")
        {
            node["description"] = "<platform-variant: SHA-3 availability probed at runtime>";
        }
        string indented = node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        // (Adversarial F5) WriteIndented emits OS-specific newlines (CRLF on Windows,
        // LF on Linux) — byte-equality across OSes REQUIRES LF normalisation here, and
        // LF-pinned snapshot files (.gitattributes). Without this the Windows/WSL parity
        // probe fails on line endings alone, masking real findings.
        return indented.Replace("\r\n", "\n");
    }

    private static string SnapshotDir =>
        Path.Combine(AppContext.BaseDirectory, "snapshots");

    private static string SourceSnapshotDir(
        [System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        => Path.Combine(Path.GetDirectoryName(thisFile)!, "snapshots");
}
