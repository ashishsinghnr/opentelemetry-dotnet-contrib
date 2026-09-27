// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NET
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.AssemblyLoad;

/// <summary>
/// Reports each NuGet package the first time one of its assemblies loads.
/// </summary>
/// <remarks>
/// The <c>.deps.json</c> manifest is only a lookup from assembly to package; it
/// is never reported whole. It is needed because only the package version, not
/// the assembly version, resolves in a vulnerability database.
/// </remarks>
internal sealed class LoadedPackageObserver
{
    private readonly ILogger logger;
    private readonly int maxPackages;
    private readonly AssemblyLoadContext? loadContext;
    private readonly Dictionary<string, DepsManifestPackage> packagesByAssemblyName;
    private readonly ConcurrentDictionary<DepsManifestPackage, byte> seenPackages = new();
    private int seenCount;
    private int stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoadedPackageObserver"/> class.
    /// </summary>
    /// <param name="logger">The logger to emit records through.</param>
    /// <param name="packages">The packages the manifest records.</param>
    /// <param name="maxPackages">The most packages to report.</param>
    /// <param name="loadContext">
    /// The load context the manifest describes, or <see langword="null"/> for any.
    /// </param>
    internal LoadedPackageObserver(
        ILogger logger,
        List<DepsManifestPackage> packages,
        int maxPackages,
        AssemblyLoadContext? loadContext)
    {
        this.logger = logger;
        this.maxPackages = maxPackages;
        this.loadContext = loadContext;
        this.packagesByAssemblyName = new(StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            foreach (var assemblyName in package.Assemblies)
            {
                // The first package wins, as it does for the host.
                this.packagesByAssemblyName.TryAdd(assemblyName, package);
            }
        }
    }

    /// <summary>
    /// Creates an observer from the manifest that describes an assembly.
    /// </summary>
    /// <param name="logger">The logger to emit records through.</param>
    /// <param name="options">The instrumentation options.</param>
    /// <param name="assembly">
    /// The assembly whose manifest to read, or <see langword="null"/> for the
    /// entry assembly.
    /// </param>
    /// <returns>
    /// The observer, not yet started, or <see langword="null"/> when no manifest
    /// could be read.
    /// </returns>
    public static LoadedPackageObserver? Create(
        ILogger logger,
        AssemblyLoadInstrumentationOptions options,
        Assembly? assembly)
    {
        try
        {
            var target = assembly ?? Assembly.GetEntryAssembly();
            if (target?.GetName().Name is not { Length: > 0 } targetName)
            {
                AssemblyLoadEventSource.Log.NoEntryAssembly();
                return null;
            }

            var manifestPath = DepsManifest.FindPath(target);
            if (manifestPath == null)
            {
                AssemblyLoadEventSource.Log.ManifestNotFound(targetName);
                return null;
            }

            List<DepsManifestPackage> packages;
            using (var stream = File.OpenRead(manifestPath))
            {
                packages = DepsManifest.ReadPackages(stream);
            }

            // A plugin context may load another version the manifest does not
            // describe, so only the target's own context is matched.
            return new LoadedPackageObserver(
                logger,
                packages,
                options.MaxPackages,
                AssemblyLoadContext.GetLoadContext(target));
        }
        catch (Exception ex)
        {
            AssemblyLoadEventSource.Log.FailedToReadManifest(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reports the packages already loaded and begins observing later loads.
    /// </summary>
    /// <returns>The number of packages already loaded.</returns>
    public int Start()
    {
        // Subscribe before sweeping so no load is missed; duplicates are ignored.
        AppDomain.CurrentDomain.AssemblyLoad += this.OnAssemblyLoad;

        var reportedCount = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (this.TryReport(assembly))
            {
                reportedCount++;
            }
        }

        return reportedCount;
    }

    /// <summary>
    /// Stops observing loads. Safe to call more than once.
    /// </summary>
    public void Stop() => this.TryStop();

    /// <summary>
    /// Reports the package that supplied an assembly, unless already reported.
    /// </summary>
    /// <param name="assembly">The loaded assembly.</param>
    /// <returns><see langword="true"/> when a record was emitted.</returns>
    internal bool TryReport(Assembly assembly)
    {
        if (assembly.IsDynamic
            || (this.loadContext != null && AssemblyLoadContext.GetLoadContext(assembly) != this.loadContext))
        {
            return false;
        }

        return assembly.GetName().Name is { Length: > 0 } name && this.TryReport(name);
    }

    /// <summary>
    /// Reports the package that supplies an assembly, unless already reported.
    /// </summary>
    /// <param name="assemblyName">The simple name of the loaded assembly.</param>
    /// <returns><see langword="true"/> when a record was emitted.</returns>
    internal bool TryReport(string assemblyName)
    {
        // Framework assemblies are absent from the manifest and not packages.
        if (Volatile.Read(ref this.stopped) == 1
            || !this.packagesByAssemblyName.TryGetValue(assemblyName, out var package)
            || !this.seenPackages.TryAdd(package, 0))
        {
            return false;
        }

        if (Interlocked.Increment(ref this.seenCount) > this.maxPackages)
        {
            // Nothing more can be reported, so stop paying for every load.
            if (this.TryStop())
            {
                AssemblyLoadEventSource.Log.PackageLimitReached(this.maxPackages);
            }

            return false;
        }

        ArtifactLoadedRecord.Write(this.logger, package);
        return true;
    }

    private bool TryStop()
    {
        if (Interlocked.Exchange(ref this.stopped, 1) == 1)
        {
            return false;
        }

        AppDomain.CurrentDomain.AssemblyLoad -= this.OnAssemblyLoad;
        return true;
    }

    private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        // Runs inside the application's load; an escaping exception would fail it.
        try
        {
            this.TryReport(args.LoadedAssembly);
        }
        catch (Exception ex)
        {
            this.Stop();
            AssemblyLoadEventSource.Log.ObservationFailed(ex.Message);
        }
    }
}
#endif
