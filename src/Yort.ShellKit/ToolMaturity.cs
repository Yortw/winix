namespace Yort.ShellKit;

/// <summary>
/// Maturity tier advertised in the --describe envelope. Winix rule (docs/STABILITY.md):
/// Core = multi-round-reviewed AND survived at least one stable release without
/// interface-breaking changes; Fresh = everything else (reviewed but unexposed —
/// the interface may still move).
/// </summary>
public enum ToolMaturity
{
    /// <summary>Stable, supported; deprecation policy binds strictly.</summary>
    Core,
    /// <summary>Reviewed but not yet through a stable release in the wild; interface may move.</summary>
    Fresh,
}
