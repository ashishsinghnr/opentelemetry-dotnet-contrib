// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
#if NET
using System.Text.Json;
#endif
using OpenTelemetry.Internal;

namespace OpenTelemetry.Resources.Artifact;

/// <summary>
/// Detector for the build artifact of the running application.
/// </summary>
/// <remarks>
/// Attributes are derived from the entry assembly unless another is given. The
/// Package URL is only reported when that assembly can be matched to a package
/// in its <c>.deps.json</c> manifest; a Package URL is never synthesized,
/// because an identifier that does not correspond to a published package cannot
/// be resolved by consumers.
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
    /// <returns>Resource with key-value pairs of resource attributes.</returns>
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

        if (GetVersion(entryAssembly, assemblyName) is not { Length: > 0 } version)
        {
            return Resource.Empty;
        }

        var attributes = new List<KeyValuePair<string, object>>(3)
        {
            new(ArtifactSemanticConventions.AttributeArtifactFilename, simpleName),
            new(ArtifactSemanticConventions.AttributeArtifactVersion, version),
        };

#if NET
        // The Package URL names the package that supplied the entry assembly,
        // which is not necessarily the assembly's own name. There is no
        // dependency manifest on .NET Framework or .NET Standard, so no Package
        // URL can be determined there.
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
    /// recorded at build time because it carries the full package version
    /// (including any prerelease label), which the assembly version does not.
    /// </summary>
    /// <param name="assembly">The entry assembly.</param>
    /// <param name="assemblyName">The entry assembly's name.</param>
    /// <returns>The version, or <see langword="null"/> when none is recorded.</returns>
    private static string? GetVersion(Assembly assembly, AssemblyName assemblyName)
    {
        // The informational version is only read through the shared helper when
        // one is present. Unlike the instrumentation assemblies that helper was
        // written for, an arbitrary application need not carry the attribute,
        // and the helper asserts that it does.
        var hasInformationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion is { Length: > 0 };

        return hasInformationalVersion
            ? assembly.GetPackageVersion()
            : assemblyName.Version?.ToString();
    }

#if NET
    /// <summary>
    /// Finds the package that supplied the entry assembly, or returns
    /// <see langword="null"/> when it was not supplied by a package.
    /// </summary>
    /// <param name="assembly">The assembly being described.</param>
    /// <param name="assemblyName">The simple name of that assembly.</param>
    /// <returns>The package, or <see langword="null"/>.</returns>
    private static DepsManifestPackage? FindPackageSupplying(Assembly assembly, string assemblyName)
    {
        try
        {
            var manifestPath = DepsManifest.FindPath(assembly);
            if (manifestPath == null)
            {
                // No manifest could be found for this assembly. That is expected
                // for a single-file or Native AOT publish, but it is also what
                // happens when the assembly being described is a platform host
                // rather than the application, so it is worth surfacing.
                ArtifactDetectorEventSource.Log.NoManifestBesideAssembly(assemblyName);
                return null;
            }

            using var stream = File.OpenRead(manifestPath);
            return DepsManifest.ReadPackageSupplying(stream, assemblyName);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // The manifest is absent (single-file publish, Native AOT) or
            // unreadable. The Package URL is optional, so degrade quietly.
            ArtifactDetectorEventSource.Log.FailedToReadDepsManifest(ex.Message);
            return null;
        }
    }
#endif
}
