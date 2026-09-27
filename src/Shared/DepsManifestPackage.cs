// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Internal;

/// <summary>
/// A NuGet package recorded in the application's <c>.deps.json</c> manifest.
/// </summary>
internal sealed class DepsManifestPackage
{
    public DepsManifestPackage(string name, string version, List<string> assemblies)
    {
        this.Name = name;
        this.Version = version;
        this.Assemblies = assemblies;
    }

    /// <summary>
    /// Gets the package identifier, for example <c>Newtonsoft.Json</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the package version, for example <c>13.0.3</c>; not the assembly version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the simple names of the runtime assemblies the package supplies.
    /// </summary>
    public IReadOnlyList<string> Assemblies { get; }

    /// <summary>
    /// Gets the <a href="https://github.com/package-url/purl-spec">Package URL</a>,
    /// percent-encoded as the specification requires.
    /// </summary>
    public string Purl => $"pkg:nuget/{Uri.EscapeDataString(this.Name)}@{Uri.EscapeDataString(this.Version)}";
}
