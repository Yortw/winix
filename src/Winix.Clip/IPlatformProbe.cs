namespace Winix.Clip;

/// <summary>
/// OS / environment facts needed to pick a clipboard backend. Abstracted so tests
/// can inject a fake probe.
/// </summary>
public interface IPlatformProbe
{
    /// <summary>Returns the current OS.</summary>
    ClipPlatform Os { get; }

    /// <summary>Returns the value of the named environment variable, or <c>null</c> if unset.</summary>
    string? GetEnv(string name);

    /// <summary>Returns true if <paramref name="binary"/> is found on <c>PATH</c>.</summary>
    bool HasBinary(string binary);
}

/// <summary>Enumerates the OSes clip supports.</summary>
public enum ClipPlatform
{
    /// <summary>Any flavour of Windows.</summary>
    Windows,
    /// <summary>macOS / Darwin.</summary>
    MacOS,
    /// <summary>Any Linux distribution.</summary>
    Linux,
    /// <summary>An OS we do not know how to handle.</summary>
    Unknown,
}
