#nullable enable
namespace Winix.Qr;

/// <summary>Error-message composition for <c>qr</c>. Kept simple — one prefix function per exit-code class.</summary>
public static class Formatting
{
    /// <summary>Prefix a message for a 125 (usage) error.</summary>
    public static string UsageError(string message) => $"qr: {message}";

    /// <summary>Prefix a message for a 126 (runtime) error.</summary>
    public static string RuntimeError(string message) => $"qr: {message}";

    /// <summary>Suggestion line for payload-capacity failures.</summary>
    public static string CapacityExceededHint(string eccLevel) =>
        $"qr: payload too long for ECC level {eccLevel}; try -e l or shorten the payload";
}
