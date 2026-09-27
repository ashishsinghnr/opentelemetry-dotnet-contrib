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
/// <c>.deps.json</c> is only a lookup: assembly names and versions do not
/// reliably identify their package.
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
    /// <param name="logger">Logger to emit through.</param>
    /// <param name="packages">Packages from the manifest.</param>
    /// <param name="maxPackages">Most packages to report.</param>
    /// <param name="loadContext">Context to match, or <see langword="null"/> for any.</param>
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
                // Keep the first package that lists the assembly.
                this.packagesByAssemblyName.TryAdd(assemblyName, package);
            }
        }
    }

    /// <summary>
    /// Creates an observer from an assembly's manifest.
    /// </summary>
    /// <param name="logger">Logger to emit through.</param>
    /// <param name="options">Instrumentation options.</param>
    /// <param name="assembly">Assembly whose manifest to read; <see langword="null"/> for the entry assembly.</param>
    /// <returns>An unstarted observer, or <see langword="null"/> if no manifest was read.</returns>
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

            // Other contexts may load versions this manifest does not describe.
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
    /// Reports loaded packages, then observes later loads.
    /// </summary>
    /// <returns>Packages reported now.</returns>
    public int Start()
    {
        // Subscribe first so no load is missed.
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
    /// Stops observing. Idempotent.
    /// </summary>
    public void Stop() => this.TryStop();

    /// <summary>
    /// Reports the package of a loaded assembly, once.
    /// </summary>
    /// <param name="assembly">Loaded assembly.</param>
    /// <returns><see langword="true"/> if a record was emitted.</returns>
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
    /// Reports the package of a loaded assembly, once.
    /// </summary>
    /// <param name="assemblyName">Simple name of the loaded assembly.</param>
    /// <returns><see langword="true"/> if a record was emitted.</returns>
    internal bool TryReport(string assemblyName)
    {
        if (Volatile.Read(ref this.stopped) == 1
            || !this.packagesByAssemblyName.TryGetValue(assemblyName, out var package)
            || !this.seenPackages.TryAdd(package, 0))
        {
            return false;
        }

        if (Interlocked.Increment(ref this.seenCount) > this.maxPackages)
        {
            // Nothing more can be reported; stop observing.
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
        // Runs inside the app's load: an exception here would fail it.
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
