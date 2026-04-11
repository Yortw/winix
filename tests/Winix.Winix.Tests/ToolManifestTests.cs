#nullable enable

using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class ToolManifestTests
{
    // JSON with two tools — used by multiple tests.
    private const string TwoToolJson = """
        {
          "version": "0.2.0",
          "tools": {
            "timeit": {
              "description": "Time a command.",
              "packages": {
                "winget": "Winix.TimeIt",
                "scoop": "timeit",
                "brew": "timeit",
                "dotnet": "Winix.TimeIt"
              }
            },
            "squeeze": {
              "description": "Compress files.",
              "packages": {
                "winget": "Winix.Squeeze",
                "scoop": "squeeze",
                "brew": "squeeze",
                "dotnet": "Winix.Squeeze"
              }
            }
          }
        }
        """;

    // JSON with a single tool and explicit description — used by package-ID and GetPackageId tests.
    private const string OneToolJson = """
        {
          "version": "0.1.0",
          "tools": {
            "timeit": {
              "description": "Time a command.",
              "packages": {
                "winget": "Winix.TimeIt",
                "scoop": "timeit",
                "brew": "timeit",
                "dotnet": "Winix.TimeIt"
              }
            }
          }
        }
        """;

    [Fact]
    public void Parse_ValidManifest_ReturnsAllTools()
    {
        var manifest = ToolManifest.Parse(TwoToolJson);

        Assert.Equal("0.2.0", manifest.Version);
        Assert.Equal(2, manifest.Tools.Count);
        Assert.True(manifest.Tools.ContainsKey("timeit"));
        Assert.True(manifest.Tools.ContainsKey("squeeze"));
    }

    [Fact]
    public void Parse_ToolEntry_HasCorrectPackageIds()
    {
        var manifest = ToolManifest.Parse(OneToolJson);

        Assert.True(manifest.Tools.TryGetValue("timeit", out var tool));
        Assert.NotNull(tool);
        Assert.Equal("Time a command.", tool.Description);
        Assert.Equal("Winix.TimeIt", tool.GetPackageId("winget"));
        Assert.Equal("timeit", tool.GetPackageId("scoop"));
        Assert.Equal("timeit", tool.GetPackageId("brew"));
        Assert.Equal("Winix.TimeIt", tool.GetPackageId("dotnet"));
    }

    [Fact]
    public void Parse_MissingVersion_Throws()
    {
        const string json = """{ "tools": {} }""";

        var ex = Assert.Throws<ManifestParseException>(() => ToolManifest.Parse(json));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingTools_Throws()
    {
        const string json = """{ "version": "0.1.0" }""";

        var ex = Assert.Throws<ManifestParseException>(() => ToolManifest.Parse(json));
        Assert.Contains("tools", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_EmptyTools_ReturnsEmptyDictionary()
    {
        const string json = """{ "version": "0.1.0", "tools": {} }""";

        var manifest = ToolManifest.Parse(json);

        Assert.Empty(manifest.Tools);
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<ManifestParseException>(() => ToolManifest.Parse("not json"));
    }

    [Fact]
    public void GetPackageId_UnknownPm_ReturnsNull()
    {
        const string json = """
            {
              "version": "0.1.0",
              "tools": {
                "timeit": {
                  "packages": {
                    "winget": "Winix.TimeIt"
                  }
                }
              }
            }
            """;

        var manifest = ToolManifest.Parse(json);
        var tool = manifest.Tools["timeit"];

        Assert.Null(tool.GetPackageId("brew"));
    }

    [Fact]
    public void GetToolNames_ReturnsAllKeys()
    {
        var manifest = ToolManifest.Parse(TwoToolJson);

        var names = manifest.GetToolNames();

        Assert.Contains("timeit", names);
        Assert.Contains("squeeze", names);
        Assert.Equal(2, names.Length);
    }
}
