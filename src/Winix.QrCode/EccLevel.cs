#nullable enable
namespace Winix.QrCode;

/// <summary>QR error-correction levels. Higher levels tolerate more damage at the cost of larger output.</summary>
public enum EccLevel
{
    /// <summary>Low: ~7% recovery. Smallest code. Suitable for pristine digital display.</summary>
    L,
    /// <summary>Medium: ~15% recovery. Default.</summary>
    M,
    /// <summary>Quartile: ~25% recovery. Suitable for printed stickers.</summary>
    Q,
    /// <summary>High: ~30% recovery. Suitable for outdoor signage or codes with embedded logos.</summary>
    H,
}
