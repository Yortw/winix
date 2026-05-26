#nullable enable
namespace Winix.Notify;

/// <summary>Urgency levels mapped to platform-specific behaviours per the design urgency table.</summary>
public enum Urgency
{
    /// <summary>Quiet, non-attention-seeking notification. Silent on all backends.</summary>
    Low,
    /// <summary>Default. Standard toast/notification appearance.</summary>
    Normal,
    /// <summary>Attention-seeking. Sound on every backend, ntfy priority 5, Windows urgent scenario.</summary>
    Critical,
}
