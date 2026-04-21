#nullable enable
namespace Winix.Protect;

/// <summary>Error message formatting helpers. Prefixes messages with the invocation name (protect/unprotect) to match POSIX tool conventions.</summary>
public static class Formatting
{
    /// <summary>Formats a usage error (bad arguments, missing values) as <c>{invocationName}: {message}</c>.</summary>
    public static string UsageError(string invocationName, string message) => $"{invocationName}: {message}";

    /// <summary>Formats a runtime error (I/O failure, decryption failure, backend error) as <c>{invocationName}: {message}</c>.</summary>
    public static string RuntimeError(string invocationName, string message) => $"{invocationName}: {message}";
}
