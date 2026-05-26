#nullable enable
using System.Collections.Generic;

namespace Winix.Url;

/// <summary>Parsed CLI options. Carries the selected subcommand + subcommand-specific fields.</summary>
public sealed record UrlOptions(
    SubCommand SubCommand,
    // Primary positional input (URL, encoded/raw string). Null for Build.
    string? PrimaryInput,
    // encode/decode mode + form flag.
    EncodeMode Mode,
    bool Form,
    // Normalisation opt-out.
    bool Raw,
    // Output formatting.
    bool Json,
    // parse --field NAME.
    string? Field,
    // build options.
    string? BuildScheme,
    string? BuildHost,
    int? BuildPort,
    string? BuildPath,
    IReadOnlyList<(string Key, string Value)> BuildQuery,
    string? BuildFragment,
    // join: second positional (relative URL).
    string? JoinRelative,
    // query get/set/delete: key + optional value.
    string? QueryKey,
    string? QueryValue,
    // Round-1 review SFH-I3 — decode --strict: reject malformed percent-escapes.
    bool Strict,
    // Round-1 review SFH-I2 — query get --all: emit every duplicate value (one per line).
    bool All);
