#nullable enable
namespace Winix.Protect;

/// <summary>Parsed CLI options produced by <see cref="ArgParser"/>.</summary>
public sealed record ProtectOptions(
    SubCommand SubCommand,
    // Input: file path or null for stdin streaming.
    string? InputPath,
    // Output: file path or null for stdout streaming. Mutually exclusive with InPlace.
    string? OutputPath,
    bool InPlace,
    bool RemoveSource,
    Scope Scope,
    bool NoVerify);
