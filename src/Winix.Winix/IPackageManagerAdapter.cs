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
