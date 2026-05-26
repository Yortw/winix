#nullable enable
namespace Winix.Protect;

/// <summary>Which operation the tool is performing. Derived from the invocation name (protect vs unprotect).</summary>
public enum SubCommand
{
    Protect,
    Unprotect,
}
