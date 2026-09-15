// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
#if NET
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
#endif

namespace OpenTelemetry.Internal;

/// <summary>
/// Reads the application's <c>.deps.json</c> manifest, which records the NuGet
/// packages that supplied the assemblies deployed with the application.
/// </summary>
/// <remarks>
/// The manifest is only produced for .NET (Core) applications. On .NET
/// Framework and .NET Standard there is no manifest, so every member reports
/// that nothing could be resolved.
/// </remarks>
internal static class DepsManifest
{
    /// <summary>
    /// Gets a value indicating whether the current runtime produces a
    /// <c>.deps.json</c> manifest at all.
    /// </summary>
    internal static bool IsSupported =>
#if NET
        true;
#else
        false;
#endif

    /// <summary>
    /// Locates the manifest belonging to the given assembly.
    /// </summary>
    /// <param name="assembly">The assembly whose manifest is wanted.</param>
    /// <returns>The manifest path, or <see langword="null"/> when absent.</returns>
    /// <remarks>
    /// The assembly's own directory is searched before the application's, so that
    /// an assembly loaded from elsewhere by a host resolves to its own manifest
    /// rather than the host's. This is the case for a serverless function loaded
    /// as a library, where the entry assembly is the platform's runtime host.
    /// </remarks>
    internal static string? FindPath(Assembly assembly)
    {
#if NET
        var assemblyName = assembly.GetName().Name;
        if (string.IsNullOrEmpty(assemblyName))
        {
            return null;
        }

        var expectedFileName = assemblyName + ".deps.json";

        // The assembly's own directory is authoritative when it is known. A
        // single-file or in-memory assembly reports an empty Location.
        var assemblyDirectory = GetAssemblyDirectory(assembly);
        if (assemblyDirectory != null)
        {
            var beside = Path.Combine(assemblyDirectory, expectedFileName);
            if (File.Exists(beside))
            {
                return beside;
            }
        }

        // The host records every manifest in use, which includes the shared
        // framework's as well as the application's. Match on the assembly's name
        // so that its manifest is selected rather than whichever is listed first.
        if (AppContext.GetData("APP_CONTEXT_DEPS_FILES") is string depsFiles
            && depsFiles.Length > 0)
        {
            foreach (var candidate in depsFiles.Split(Path.PathSeparator))
            {
                if (candidate.Length > 0
                    && string.Equals(
                        Path.GetFileName(candidate),
                        expectedFileName,
                        StringComparison.OrdinalIgnoreCase)
                    && File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // Fall back to the application's output directory. The assembly's name is
        // used rather than the process name, because the process is the shared
        // host ("dotnet") when the application is launched through it.
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory))
        {
            return null;
        }

        var probe = Path.Combine(baseDirectory, expectedFileName);
        return File.Exists(probe) ? probe : null;
#else
        _ = assembly;
        return null;
#endif
    }

#if NET
    /// <summary>
    /// Finds the package that supplied a given assembly.
    /// </summary>
    /// <param name="manifest">The <c>.deps.json</c> manifest to read.</param>
    /// <param name="assemblyName">The simple name of the assembly to resolve.</param>
    /// <returns>
    /// The package, or <see langword="null"/> when the assembly was not supplied
    /// by a package.
    /// </returns>
    /// <remarks>
    /// The package's own identifier is returned rather than the assembly name,
    /// because the two routinely differ: <c>Humanizer.Core</c> ships
    /// <c>Humanizer.dll</c>. Only the manifest's identifier corresponds to a
    /// published package.
    /// </remarks>
    internal static DepsManifestPackage? ReadPackageSupplying(Stream manifest, string assemblyName)
    {
        using var document = JsonDocument.Parse(manifest);

        foreach (var target in EnumerateTargets(document))
        {
            foreach (var library in target.EnumerateObject())
            {
                if (!SuppliesAssembly(library.Value, assemblyName)
                    || !TrySplitLibraryKey(library.Name, out var name, out var version)
                    || !IsPackage(document.RootElement, library.Name))
                {
                    continue;
                }

                return new DepsManifestPackage(
                    name,
                    version,
                    ReadRuntimeAssemblies(library.Value));
            }
        }

        return null;
    }

    /// <summary>
    /// Enumerates every package recorded in the manifest.
    /// </summary>
    /// <param name="manifest">The <c>.deps.json</c> manifest to read.</param>
    /// <returns>
    /// The name, version, and supplied assemblies of each package. Project
    /// output is excluded, because it has no published identity.
    /// </returns>
    internal static List<DepsManifestPackage> ReadPackages(Stream manifest)
    {
        using var document = JsonDocument.Parse(manifest);

        var packages = new List<DepsManifestPackage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in EnumerateTargets(document))
        {
            foreach (var library in target.EnumerateObject())
            {
                if (!TrySplitLibraryKey(library.Name, out var name, out var version)
                    || !IsPackage(document.RootElement, library.Name)
                    || !seen.Add(library.Name))
                {
                    continue;
                }

                packages.Add(new DepsManifestPackage(
                    name,
                    version,
                    ReadRuntimeAssemblies(library.Value)));
            }
        }

        return packages;
    }

    /// <summary>
    /// Gets the directory an assembly was loaded from.
    /// </summary>
    /// <param name="assembly">The assembly to locate.</param>
    /// <returns>
    /// The directory, or <see langword="null"/> when the assembly has no file on
    /// disk, which is the case for a single-file publish or a dynamic assembly.
    /// </returns>
    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "An empty location is the documented result for an assembly with no file on disk, and is handled by returning null so that the caller falls back to its other probes. The application's base directory is deliberately not substituted, because it is the wrong directory for an assembly loaded by a host.")]
    private static string? GetAssemblyDirectory(Assembly assembly)
    {
        if (assembly.IsDynamic)
        {
            return null;
        }

        try
        {
            var location = assembly.Location;
            return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static List<JsonElement> EnumerateTargets(JsonDocument document)
    {
        var targets = new List<JsonElement>();

        if (!document.RootElement.TryGetProperty("targets", out var targetsElement)
            || targetsElement.ValueKind != JsonValueKind.Object)
        {
            return targets;
        }

        foreach (var target in targetsElement.EnumerateObject())
        {
            if (target.Value.ValueKind == JsonValueKind.Object)
            {
                targets.Add(target.Value);
            }
        }

        return targets;
    }

    private static List<string> ReadRuntimeAssemblies(JsonElement library)
    {
        var assemblies = new List<string>();

        if (!library.TryGetProperty("runtime", out var runtime)
            || runtime.ValueKind != JsonValueKind.Object)
        {
            return assemblies;
        }

        foreach (var asset in runtime.EnumerateObject())
        {
            // Asset paths look like "lib/net8.0/Foo.dll". A placeholder entry
            // ("_._") marks a framework that intentionally ships nothing.
            var fileName = Path.GetFileNameWithoutExtension(asset.Name);
            if (fileName.Length > 0 && fileName != "_")
            {
                assemblies.Add(fileName);
            }
        }

        return assemblies;
    }

    private static bool SuppliesAssembly(JsonElement library, string assemblyName)
    {
        foreach (var candidate in ReadRuntimeAssemblies(library))
        {
            if (string.Equals(candidate, assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits a library key of the form <c>"&lt;package id&gt;/&lt;version&gt;"</c>.
    /// </summary>
    /// <param name="libraryKey">The manifest's library key.</param>
    /// <param name="name">The package identifier.</param>
    /// <param name="version">The package version.</param>
    /// <returns><see langword="true"/> when the key was well formed.</returns>
    private static bool TrySplitLibraryKey(string libraryKey, out string name, out string version)
    {
        var separatorIndex = libraryKey.LastIndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == libraryKey.Length - 1)
        {
            name = string.Empty;
            version = string.Empty;
            return false;
        }

        name = libraryKey.Substring(0, separatorIndex);
        version = libraryKey.Substring(separatorIndex + 1);
        return true;
    }

    private static bool IsPackage(JsonElement root, string libraryKey)
    {
        if (!root.TryGetProperty("libraries", out var libraries)
            || !libraries.TryGetProperty(libraryKey, out var library)
            || !library.TryGetProperty("type", out var type))
        {
            return false;
        }

        return string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase);
    }
#endif
}
