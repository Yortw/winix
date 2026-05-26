using System.Runtime.InteropServices;

namespace Winix.Clip;

/// <summary>Runtime implementation of <see cref="IPlatformProbe"/>.</summary>
public sealed class DefaultPlatformProbe : IPlatformProbe
{
    /// <inheritdoc />
    public ClipPlatform Os =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ClipPlatform.Windows :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? ClipPlatform.MacOS :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)   ? ClipPlatform.Linux :
        ClipPlatform.Unknown;

    /// <inheritdoc />
    public string? GetEnv(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc />
    public bool HasBinary(string binary)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        char sep = Path.PathSeparator;
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool hasExt = Path.HasExtension(binary);
        string[] exts = isWindows && !hasExt
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };

        foreach (string dir in path.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string ext in exts)
            {
                string candidate = Path.Combine(dir.Trim(), binary + ext);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
