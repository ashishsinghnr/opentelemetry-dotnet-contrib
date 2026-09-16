// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Resources.Artifact;

/// <summary>
/// Detector for the build artifact of the running application.
/// </summary>
/// <remarks>
/// Attributes come from the entry assembly unless another is given. A Package URL
/// is reported only on a match in the <c>.deps.json</c> manifest, never
/// synthesized, since an unpublished identifier cannot be resolved.
/// </remarks>
internal sealed class ArtifactDetector : IResourceDetector
{
    private static readonly Version SemanticConventionsVersion = new(1, 44, 0);

    private readonly Assembly? assembly;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactDetector"/> class
    /// describing the entry assembly.
    /// </summary>
    public ArtifactDetector()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactDetector"/> class
    /// describing a given assembly.
    /// </summary>
    /// <param name="assembly">
    /// The assembly to describe, or <see langword="null"/> for the entry assembly.
    /// </param>
    public ArtifactDetector(Assembly? assembly)
    {
        this.assembly = assembly;
    }

    /// <summary>
    /// Detects the resource attributes describing the application's build artifact.
    /// </summary>
    /// <returns>
    /// Resource with key-value pairs of resource attributes, or
    /// <see cref="Resource.Empty"/> when the assembly cannot be named.
    /// </returns>
    public Resource Detect()
    {
        var entryAssembly = this.assembly ?? Assembly.GetEntryAssembly();
        if (entryAssembly == null)
        {
            return Resource.Empty;
        }

        var assemblyName = entryAssembly.GetName();
        if (assemblyName.Name is not { Length: > 0 } simpleName)
        {
            return Resource.Empty;
        }

        var attributes = new List<KeyValuePair<string, object>>(3)
        {
            new(ArtifactSemanticConventions.AttributeArtifactFilename, simpleName),
        };

        // Each attribute is optional, so an assembly with no recorded version is
        // still reported with its name.
        if (GetVersion(entryAssembly, assemblyName) is { Length: > 0 } version)
        {
            attributes.Add(new(
                ArtifactSemanticConventions.AttributeArtifactVersion,
                version));
        }

        // artifact.purl identifies the package that shipped the assembly, whose
        // name often differs: Humanizer.Core ships Humanizer.dll. Requires the
        // .deps.json manifest, which only .NET (Core) produces.
#if NET
        var package = FindPackageSupplying(entryAssembly, simpleName);
        if (package != null)
        {
            attributes.Add(new(
                ArtifactSemanticConventions.AttributeArtifactPurl,
                package.Purl));
        }
#endif

        return new Resource(attributes, SchemaUrls.Get(SemanticConventionsVersion));
    }

    /// <summary>
    /// Resolves the artifact version, preferring the informational version
    /// because it carries any prerelease label that the assembly version drops.
    /// </summary>
    /// <param name="assembly">The entry assembly.</param>
    /// <param name="assemblyName">The entry assembly's name.</param>
    /// <returns>The version, or <see langword="null"/> when none is recorded.</returns>
    private static string? GetVersion(Assembly assembly, AssemblyName assemblyName)
    {
        // Presence is checked here because GetPackageVersion assumes the
        // attribute exists, which an arbitrary application need not carry.
        var hasInformationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion is { Length: > 0 };

        return hasInformationalVersion
            ? assembly.GetPackageVersion()
            : assemblyName.Version?.ToString();
    }

#if NET
    /// <summary>
    /// Finds the package that supplied the entry assembly.
    /// </summary>
    /// <param name="assembly">The assembly being described.</param>
    /// <param name="assemblyName">The simple name of that assembly.</param>
    /// <returns>
    /// The package, or <see langword="null"/> when no package supplied the
    /// assembly or the manifest could not be read.
    /// </returns>
    private static DepsManifestPackage? FindPackageSupplying(Assembly assembly, string assemblyName)
    {
        try
        {
            var manifestPath = DepsManifest.FindPath(assembly);
            if (manifestPath == null)
            {
                // A missing manifest is expected for single-file and Native AOT,
                // but is also how a platform host looks, so it is surfaced.
                ArtifactDetectorEventSource.Log.NoManifestBesideAssembly(assemblyName);
                return null;
            }

            using var stream = File.OpenRead(manifestPath);
            return DepsManifest.ReadPackageSupplying(stream, assemblyName);
        }
        catch (Exception ex)
        {
            // The Package URL is optional, so degrade quietly rather than
            // failing the provider being built.
            ArtifactDetectorEventSource.Log.FailedToReadDepsManifest(ex.Message);
            return null;
        }
    }
#endif
}
