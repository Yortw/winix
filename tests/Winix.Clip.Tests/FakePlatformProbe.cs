using Winix.Clip;

namespace Winix.Clip.Tests;

internal sealed class FakePlatformProbe : IPlatformProbe
{
    public ClipPlatform Os { get; set; }
    public Dictionary<string, string> Env { get; } = new();
    public HashSet<string> PresentBinaries { get; } = new();

    public string? GetEnv(string name) => Env.TryGetValue(name, out var v) ? v : null;

    public bool HasBinary(string binary) => PresentBinaries.Contains(binary);
}
