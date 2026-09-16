// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Instrumentation.DependencyInventory;

/// <summary>
/// The set of assembly names currently loaded into the process.
/// </summary>
/// <remarks>
/// This is a snapshot taken when the inventory is emitted. An assembly loaded
/// later is not reflected, which is why the reported attribute is named for the
/// moment of capture rather than implying an ongoing truth.
/// </remarks>
internal static class LoadedAssemblySet
{
    /// <summary>
    /// Captures the simple names of the assemblies loaded in this process.
    /// </summary>
    /// <returns>A case-insensitive set of assembly names.</returns>
    public static HashSet<string> Capture()
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // A dynamic assembly has no file on disk and cannot correspond to a
            // package. Its Location also throws on some runtimes.
            if (assembly.IsDynamic)
            {
                continue;
            }

            if (assembly.GetName().Name is { Length: > 0 } name)
            {
                loaded.Add(name);
            }
        }

        return loaded;
    }

    /// <summary>
    /// Determines whether any of a package's assemblies are loaded.
    /// </summary>
    /// <param name="loaded">The captured set of loaded assembly names.</param>
    /// <param name="assemblies">The assemblies a package supplies.</param>
    /// <returns><see langword="true"/> when at least one is loaded.</returns>
    public static bool IsAnyLoaded(HashSet<string> loaded, IReadOnlyList<string> assemblies)
    {
        for (var i = 0; i < assemblies.Count; i++)
        {
            if (loaded.Contains(assemblies[i]))
            {
                return true;
            }
        }

        return false;
    }
}
