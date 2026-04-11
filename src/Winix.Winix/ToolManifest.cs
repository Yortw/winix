#nullable enable

using System.Text.Json;

namespace Winix.Winix;

/// <summary>
/// Represents the Winix suite manifest: the version of the suite and the
/// set of tools it contains, each with their per-package-manager identifiers.
/// </summary>
public sealed class ToolManifest
{
    /// <summary>Gets the suite version declared in the manifest.</summary>
    public string Version { get; }

    /// <summary>
    /// Gets all tool entries keyed by short tool name (case-insensitive).
    /// The dictionary is read-only; key lookup is always case-insensitive.
    /// </summary>
    public IReadOnlyDictionary<string, ToolEntry> Tools { get; }

    private ToolManifest(string version, Dictionary<string, ToolEntry> tools)
    {
        Version = version;
        Tools = tools;
    }

    /// <summary>
    /// Returns the short names of all tools declared in the manifest.
    /// </summary>
    public string[] GetToolNames()
    {
        return Tools.Keys.ToArray();
    }

    /// <summary>
    /// Parses a Winix manifest from a JSON string.
    /// </summary>
    /// <param name="json">The raw JSON text of the manifest.</param>
    /// <returns>A populated <see cref="ToolManifest"/>.</returns>
    /// <exception cref="ManifestParseException">
    /// Thrown when <paramref name="json"/> is not valid JSON, or when required
    /// top-level fields (<c>version</c> or <c>tools</c>) are absent or have the
    /// wrong type.
    /// </exception>
    public static ToolManifest Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ManifestParseException("Manifest JSON is invalid: " + ex.Message, ex);
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.String)
            {
                throw new ManifestParseException("Manifest is missing required field 'version'.");
            }

            var version = versionElement.GetString()!;

            if (!root.TryGetProperty("tools", out var toolsElement) ||
                toolsElement.ValueKind != JsonValueKind.Object)
            {
                throw new ManifestParseException("Manifest is missing required field 'tools'.");
            }

            var tools = new Dictionary<string, ToolEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var toolProperty in toolsElement.EnumerateObject())
            {
                var toolName = toolProperty.Name;
                var toolElement = toolProperty.Value;

                var description = "";
                if (toolElement.TryGetProperty("description", out var descElement) &&
                    descElement.ValueKind == JsonValueKind.String)
                {
                    description = descElement.GetString() ?? "";
                }

                var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (toolElement.TryGetProperty("packages", out var packagesElement) &&
                    packagesElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pkgProperty in packagesElement.EnumerateObject())
                    {
                        if (pkgProperty.Value.ValueKind == JsonValueKind.String)
                        {
                            packages[pkgProperty.Name] = pkgProperty.Value.GetString()!;
                        }
                    }
                }

                tools[toolName] = new ToolEntry(description, packages);
            }

            return new ToolManifest(version, tools);
        }
    }
}

/// <summary>
/// Describes a single tool in the Winix suite, including a human-readable
/// description and the package identifier for each supported package manager.
/// </summary>
public sealed class ToolEntry
{
    private readonly Dictionary<string, string> _packages;

    /// <summary>Gets a short human-readable description of the tool.</summary>
    public string Description { get; }

    /// <summary>
    /// Initialises a new <see cref="ToolEntry"/> with the given description and package map.
    /// </summary>
    internal ToolEntry(string description, Dictionary<string, string> packages)
    {
        Description = description;
        _packages = packages;
    }

    /// <summary>
    /// Returns the package identifier for the specified package manager, or
    /// <see langword="null"/> if the tool has no entry for that package manager.
    /// </summary>
    /// <param name="pmName">
    /// The package manager name (e.g. <c>"winget"</c>, <c>"scoop"</c>,
    /// <c>"brew"</c>, <c>"dotnet"</c>). Comparison is case-insensitive.
    /// </param>
    public string? GetPackageId(string pmName)
    {
        _packages.TryGetValue(pmName, out var id);
        return id;
    }
}

/// <summary>
/// Thrown when the Winix manifest cannot be parsed — either because the JSON
/// is malformed or because required fields are missing or have the wrong type.
/// </summary>
public sealed class ManifestParseException : Exception
{
    /// <summary>Initialises a new instance with the given message.</summary>
    public ManifestParseException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message and an inner exception
    /// that describes the underlying parse failure.
    /// </summary>
    public ManifestParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
