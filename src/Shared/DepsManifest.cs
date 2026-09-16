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
/// Only .NET (Core) produces a manifest, so on .NET Framework and .NET Standard
/// <see cref="FindPath"/> finds nothing and the reading members are not compiled.
/// </remarks>
internal static class DepsManifest
{
    /// <summary>
    /// Locates the manifest belonging to the given assembly.
    /// </summary>
    /// <param name="assembly">The assembly whose manifest is wanted.</param>
    /// <returns>The manifest path, or <see langword="null"/> when absent.</returns>
    /// <remarks>
    /// The assembly's own directory is searched first, so an assembly loaded by a
    /// host resolves to its own manifest rather than the host's.
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

        // The assembly's own directory wins when known; a single-file or dynamic
        // assembly reports no location.
        var assemblyDirectory = GetAssemblyDirectory(assembly);
        if (assemblyDirectory != null)
        {
            var beside = Path.Combine(assemblyDirectory, expectedFileName);
            if (File.Exists(beside))
            {
                return beside;
            }
        }

        // The host lists the framework's manifests too, so match on name.
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

        // Fall back to the output directory, keyed on the assembly name because
        // the process may be the shared host ("dotnet").
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
    /// <returns>The package, or <see langword="null"/> when there is no match.</returns>
    /// <remarks>
    /// The package identifier is returned, not the assembly name: the two often
    /// differ (<c>Humanizer.Core</c> ships <c>Humanizer.dll</c>).
    /// </remarks>
    internal static DepsManifestPackage? ReadPackageSupplying(Stream manifest, string assemblyName)
    {
        using var document = JsonDocument.Parse(manifest);

        foreach (var target in EnumerateTargets(document))
        {
            foreach (var library in target.EnumerateObject())
            {
                if (library.Value.ValueKind != JsonValueKind.Object
                    || !SuppliesAssembly(library.Value, assemblyName)
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
    /// Gets the directory an assembly was loaded from.
    /// </summary>
    /// <param name="assembly">The assembly to locate.</param>
    /// <returns>
    /// The directory, or <see langword="null"/> when it cannot be determined: a
    /// dynamic assembly, a single-file publish, or an unreadable path.
    /// </returns>
    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "An empty location is expected here and returns null so the caller falls back to its other probes. The base directory is not substituted, being wrong for an assembly loaded by a host.")]
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

        // The root is kind-checked too: a manifest whose root is an array or a
        // scalar is valid JSON, and TryGetProperty throws on anything but object.
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("targets", out var targetsElement)
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

        if (library.ValueKind != JsonValueKind.Object
            || !library.TryGetProperty("runtime", out var runtime)
            || runtime.ValueKind != JsonValueKind.Object)
        {
            return assemblies;
        }

        foreach (var asset in runtime.EnumerateObject())
        {
            // Asset paths look like "lib/net8.0/Foo.dll". "_._" is a placeholder
            // for shipping nothing; matched whole so a real "_.dll" is kept.
            if (Path.GetFileName(asset.Name) == "_._")
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(asset.Name);
            if (fileName.Length > 0)
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
        // Every level is kind-checked before it is read, so a malformed manifest
        // reports "not a package" instead of throwing.
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("libraries", out var libraries)
            || libraries.ValueKind != JsonValueKind.Object
            || !libraries.TryGetProperty(libraryKey, out var library)
            || library.ValueKind != JsonValueKind.Object
            || !library.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase);
    }
#endif
}
