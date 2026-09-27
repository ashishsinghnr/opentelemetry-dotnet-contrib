// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.AssemblyLoad.Tests;

/// <summary>
/// Reads this test project's own manifest, for tests that load real packages.
/// </summary>
internal static class TestManifest
{
    // Loading assemblies and the instrumentation's guard are process-wide, so
    // tests that touch either run in this collection, one at a time.
    public const string Collection = "AssemblyLoad";

    public static Assembly Assembly { get; } = typeof(TestManifest).Assembly;

    public static List<DepsManifestPackage> ReadPackages()
    {
        using var stream = File.OpenRead(DepsManifest.FindPath(Assembly)!);
        return DepsManifest.ReadPackages(stream);
    }

    public static HashSet<string> LoadedAssemblyNames() =>
        new(
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Select(a => a.GetName().Name!),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds a package none of whose assemblies is loaded yet. Loading it uses it
    /// up, so each call returns a different package.
    /// </summary>
    /// <returns>The package and one of its assembly names.</returns>
    public static (DepsManifestPackage Package, string AssemblyName) FindUnloadedPackage()
    {
        var loaded = LoadedAssemblyNames();

        foreach (var package in ReadPackages())
        {
            if (package.Assemblies.Any(loaded.Contains))
            {
                continue;
            }

            var assemblyName = package.Assemblies.FirstOrDefault(
                a => File.Exists(Path.Combine(AppContext.BaseDirectory, a + ".dll")));

            if (assemblyName != null)
            {
                return (package, assemblyName);
            }
        }

        throw new InvalidOperationException("The test manifest lists no package that is not yet loaded.");
    }
}
