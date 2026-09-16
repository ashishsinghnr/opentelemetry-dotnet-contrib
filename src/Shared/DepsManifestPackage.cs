// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Internal;

/// <summary>
/// A NuGet package recorded in the application's <c>.deps.json</c> manifest.
/// </summary>
internal sealed class DepsManifestPackage
{
    public DepsManifestPackage(
        string name,
        string version,
        List<string> assemblies,
        string? checksum = null,
        string? checksumAlgorithm = null)
    {
        this.Name = name;
        this.Version = version;
        this.Assemblies = assemblies;
        this.Checksum = checksum;
        this.ChecksumAlgorithm = checksumAlgorithm;
    }

    /// <summary>
    /// Gets the package identifier, for example <c>Newtonsoft.Json</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the package version, for example <c>13.0.3</c>.
    /// </summary>
    /// <remarks>
    /// This is the package version, which routinely differs from the version of
    /// the assemblies the package ships.
    /// </remarks>
    public string Version { get; }

    /// <summary>
    /// Gets the simple names of the assemblies this package supplies at runtime.
    /// </summary>
    public IReadOnlyList<string> Assemblies { get; }

    /// <summary>
    /// Gets the hash of the package's content, or <see langword="null"/> when the
    /// manifest records none. This verifies the package against the one published.
    /// </summary>
    public string? Checksum { get; }

    /// <summary>
    /// Gets the algorithm <see cref="Checksum"/> was computed with, for example
    /// <c>sha512</c>, or <see langword="null"/> when there is no checksum.
    /// </summary>
    public string? ChecksumAlgorithm { get; }

    /// <summary>
    /// Gets the <a href="https://github.com/package-url/purl-spec">Package URL</a>
    /// identifying this package.
    /// </summary>
    public string Purl => $"pkg:nuget/{this.Name}@{this.Version}";
}
