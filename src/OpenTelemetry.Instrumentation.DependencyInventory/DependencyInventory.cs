// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.DependencyInventory;

/// <summary>
/// Reports the NuGet packages deployed with the application, so that a backend
/// can correlate them against known vulnerabilities.
/// </summary>
/// <remarks>
/// The inventory is read from the application's <c>.deps.json</c> manifest,
/// which records the resolved package graph including transitive dependencies.
/// Whether each package's assemblies are loaded is reported alongside it, which
/// distinguishes what the process is actually running from what merely shipped
/// with it.
/// </remarks>
internal static class DependencyInventory
{
    /// <summary>
    /// Emits one record per package. The once-per-process guard is the caller's;
    /// see <see cref="DependencyInventoryReporter.Report"/>.
    /// </summary>
    /// <param name="logger">The logger to emit records through.</param>
    /// <param name="options">The inventory options.</param>
    /// <param name="assembly">
    /// The assembly whose manifest describes the inventory, or
    /// <see langword="null"/> for the entry assembly.
    /// </param>
    /// <returns>The number of packages reported.</returns>
    public static int Emit(ILogger logger, DependencyInventoryOptions options, Assembly? assembly)
    {
        if (!options.Enabled)
        {
            return 0;
        }

        if (!DepsManifest.IsSupported)
        {
            // Reported as a diagnostic rather than as an empty inventory, which
            // would look like an application with no dependencies.
            DependencyInventoryEventSource.Log.ManifestNotSupported();
            return 0;
        }

#if NET
        List<DepsManifestPackage> packages;
        try
        {
            var target = assembly ?? Assembly.GetEntryAssembly();
            if (target?.GetName().Name is not { Length: > 0 } entryAssemblyName)
            {
                DependencyInventoryEventSource.Log.NoEntryAssembly();
                return 0;
            }

            var manifestPath = DepsManifest.FindPath(target);
            if (manifestPath == null)
            {
                // Single-file and Native AOT publishes do not carry a readable
                // manifest.
                DependencyInventoryEventSource.Log.ManifestNotFound(entryAssemblyName);
                return 0;
            }

            using var stream = File.OpenRead(manifestPath);
            packages = DepsManifest.ReadPackages(stream);
        }
        catch (Exception ex)
        {
            // The manifest is unreadable or malformed. An empty inventory is
            // reported either way, so no exception type is singled out.
            DependencyInventoryEventSource.Log.FailedToReadManifest(ex.Message);
            return 0;
        }

        var loaded = LoadedAssemblySet.Capture();
        var emitted = 0;
        var eligible = 0;

        foreach (var package in packages)
        {
            var isLoaded = LoadedAssemblySet.IsAnyLoaded(loaded, package.Assemblies);

            if (!isLoaded && !options.IncludeUnloadedPackages)
            {
                continue;
            }

            eligible++;

            if (emitted == options.MaxPackages)
            {
                // Keep counting so the notice reports how many packages were
                // eligible, not how many the manifest held.
                continue;
            }

            DependencyRecord.Write(logger, package, isLoaded);
            emitted++;
        }

        if (eligible > emitted)
        {
            // Report the truncation rather than dropping the remainder silently,
            // so that a partial inventory is not mistaken for a complete one.
            DependencyInventoryEventSource.Log.InventoryTruncated(emitted, eligible);
        }

        return emitted;
#else
        return 0;
#endif
    }
}
