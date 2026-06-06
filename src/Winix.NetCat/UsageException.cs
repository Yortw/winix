#nullable enable

namespace Winix.NetCat;

/// <summary>
/// Signals an argument/option combination the CLI rejects with a usage error (exit 125).
/// Thrown by <see cref="Cli"/>'s option validation; relocated from the console app when the
/// validation moved into the library (seam ADR N4).
/// </summary>
internal sealed class UsageException : System.Exception
{
    /// <summary>Creates the exception with the user-facing message (printed verbatim).</summary>
    public UsageException(string message) : base(message) { }
}
