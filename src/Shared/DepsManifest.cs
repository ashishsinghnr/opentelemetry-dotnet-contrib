// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace OpenTelemetry.Internal;

/// <summary>
/// Reads the <c>.deps.json</c> manifest, which maps each deployed assembly to its NuGet package.
/// </summary>
internal static class DepsManifest
{
    /// <summary>
    /// Finds an assembly's manifest, looking beside the assembly first.
    /// </summary>
    /// <param name="assembly">Assembly whose manifest to find.</param>
    /// <returns>The path, or <see langword="null"/> if absent.</returns>
    internal static string? FindPath(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name;
        if (string.IsNullOrEmpty(assemblyName))
        {
            return null;
        }

        var expectedFileName = assemblyName + ".deps.json";

        // A host-loaded assembly's own manifest sits beside it.
        var assemblyDirectory = GetAssemblyDirectory(assembly);
        if (assemblyDirectory != null)
        {
            var beside = Path.Combine(assemblyDirectory, expectedFileName);
            if (File.Exists(beside))
            {
                return beside;
            }
        }

        // The list also holds framework manifests, so match by name.
        if (AppContext.GetData("APP_CONTEXT_DEPS_FILES") is string depsFiles)
        {
            var listed = FindInDepsFilesList(depsFiles, expectedFileName);
            if (listed != null)
            {
                return listed;
            }
        }

        // Keyed on the assembly name: the process may be the "dotnet" host.
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory))
        {
            return null;
        }

        var probe = Path.Combine(baseDirectory, expectedFileName);
        return File.Exists(probe) ? probe : null;
    }

    /// <summary>
    /// Reads every package in the manifest, excluding project output.
    /// </summary>
    /// <param name="manifest">The <c>.deps.json</c> content.</param>
    /// <returns>The packages.</returns>
    internal static List<DepsManifestPackage> ReadPackages(Stream manifest)
    {
        using var document = JsonDocument.Parse(manifest);

        var packages = new List<DepsManifestPackage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in EnumerateTargets(document))
        {
            foreach (var library in target.EnumerateObject())
            {
                if (library.Value.ValueKind != JsonValueKind.Object
                    || !TrySplitLibraryKey(library.Name, out var name, out var version)
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
    /// Finds a manifest by file name in the host's <c>APP_CONTEXT_DEPS_FILES</c> list.
    /// </summary>
    /// <param name="depsFiles">The list.</param>
    /// <param name="expectedFileName">File name to find.</param>
    /// <returns>The path, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Always ';'-separated; <see cref="Path.PathSeparator"/> is ':' on Unix.
    /// </remarks>
    internal static string? FindInDepsFilesList(string depsFiles, string expectedFileName)
    {
        foreach (var candidate in depsFiles.Split(';'))
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

        return null;
    }

    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "An empty location returns null and the caller tries its other probes.")]
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

        // TryGetProperty throws on a non-object root.
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
            // "_._" is a placeholder for no assets; matched whole to keep "_.dll".
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

    // Splits "<package id>/<version>".
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
        // Kind-checked at every level so a malformed manifest does not throw.
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("libraries", out var libraries)
            || libraries.ValueKind != JsonValueKind.Object
            || !libraries.TryGetProperty(libraryKey, out var entry)
            || entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase);
    }
}
