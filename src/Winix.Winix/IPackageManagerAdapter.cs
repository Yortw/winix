#nullable enable

namespace Winix.Winix;

/// <summary>
/// Defines the contract for a platform-specific package manager adapter.
/// Implementations wrap a single package manager (e.g. winget, scoop, brew,
/// dotnet tool) and provide availability detection plus install/update/uninstall
/// operations.
/// </summary>
public interface IPackageManagerAdapter
{
    /// <summary>
    /// The canonical short name of this package manager (e.g. <c>"winget"</c>,
    /// <c>"scoop"</c>, <c>"brew"</c>, <c>"dotnet"</c>). Used as the key in
    /// adapter dictionaries and in <c>--via</c> override resolution.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns <see langword="true"/> when this package manager is installed and
    /// reachable on the current machine; <see langword="false"/> otherwise.
    /// </summary>
    /// <remarks>
    /// Implementations should be fast (PATH probe only) — this is called during
    /// adapter resolution before any install operations.
    /// </remarks>
    bool IsAvailable();

    /// <summary>
    /// Returns <see langword="true"/> when the package identified by
    /// <paramref name="packageId"/> is currently installed via this package manager.
    /// </summary>
    /// <param name="packageId">
    /// The package manager's identifier for the tool (e.g. the winget package ID
    /// or the scoop app name).
    /// </param>
    Task<bool> IsInstalled(string packageId);

    /// <summary>
    /// Returns the installed version string for <paramref name="packageId"/>,
    /// or <see langword="null"/> when the package is not installed or the version
    /// cannot be determined.
    /// </summary>
    /// <param name="packageId">The package manager's identifier for the tool.</param>
    Task<string?> GetInstalledVersion(string packageId);

    /// <summary>
    /// Returns a snapshot of every package this manager currently considers installed,
    /// keyed by package ID with the installed version as the value (or
    /// <see langword="null"/> when the version field could not be parsed for that row).
    /// </summary>
    /// <remarks>
    /// <para>
    /// One subprocess call serves the whole snapshot. Used by suite-wide flows
    /// (<c>winix list</c>, <c>winix status</c>, <c>winix uninstall</c>) where iterating
    /// 22+ tools through the per-package <see cref="IsInstalled"/> path takes minutes
    /// because each call spawns a fresh PM subprocess. The bulk call typically runs in
    /// seconds because the PM only enumerates its index once.
    /// </para>
    /// <para>
    /// The returned dictionary uses case-insensitive keys: package IDs differ in case
    /// across PMs (winget preserves the published case, dotnet lowercases everything),
    /// and callers shouldn't have to track which PM normalised which way.
    /// </para>
    /// <para>
    /// Adapters whose underlying CLI does not support a bulk listing operation (or
    /// when bulk would be slower than per-package for the manager's design) may fall
    /// back to per-package iteration internally — but the returned snapshot must still
    /// be a single coherent view, not a streaming enumerable.
    /// </para>
    /// </remarks>
    Task<IReadOnlyDictionary<string, string?>> GetInstalled();

    /// <summary>
    /// Installs the package identified by <paramref name="packageId"/>.
    /// </summary>
    /// <param name="packageId">The package manager's identifier for the tool.</param>
    /// <returns>
    /// A <see cref="ProcessResult"/> describing the outcome of the install command.
    /// <see cref="ProcessResult.ExitCode"/> of <c>0</c> indicates success.
    /// </returns>
    Task<ProcessResult> Install(string packageId);

    /// <summary>
    /// Updates the package identified by <paramref name="packageId"/> to the
    /// latest available version.
    /// </summary>
    /// <param name="packageId">The package manager's identifier for the tool.</param>
    /// <returns>
    /// A <see cref="ProcessResult"/> describing the outcome of the update command.
    /// <see cref="ProcessResult.ExitCode"/> of <c>0</c> indicates success.
    /// </returns>
    Task<ProcessResult> Update(string packageId);

    /// <summary>
    /// Uninstalls the package identified by <paramref name="packageId"/>.
    /// </summary>
    /// <param name="packageId">The package manager's identifier for the tool.</param>
    /// <returns>
    /// A <see cref="ProcessResult"/> describing the outcome of the uninstall command.
    /// <see cref="ProcessResult.ExitCode"/> of <c>0</c> indicates success.
    /// </returns>
    Task<ProcessResult> Uninstall(string packageId);
}
