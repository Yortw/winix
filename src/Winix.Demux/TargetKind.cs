namespace Winix.Demux;

/// <summary>The kind of sink a route delivers to.</summary>
public enum TargetKind
{
    /// <summary>Write matching lines to a file.</summary>
    File,
    /// <summary>Feed matching lines to a shell-spawned command's stdin.</summary>
    Exec,
}
