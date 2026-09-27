// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.AssemblyLoad;

/// <summary>
/// Reports the NuGet packages the application loads.
/// </summary>
public static class AssemblyLoadInstrumentation
{
    private static int started;
#if NET
    private static LoadedPackageObserver? activeObserver;
#endif

    /// <summary>
    /// Reports loaded NuGet packages, then each package as it first loads.
    /// </summary>
    /// <param name="loggerFactory">Factory with the OpenTelemetry logging provider; must outlive the process.</param>
    /// <param name="options">Options, or <see langword="null"/> for the defaults.</param>
    /// <param name="assembly">Assembly whose <c>.deps.json</c> to read; <see langword="null"/> for the entry assembly.</param>
    /// <returns>Packages reported now; zero if disabled, unavailable, or already started.</returns>
    public static int Start(
        ILoggerFactory loggerFactory,
        AssemblyLoadInstrumentationOptions? options = null,
        Assembly? assembly = null)
    {
        Guard.ThrowIfNull(loggerFactory);

        if (Interlocked.CompareExchange(ref started, 1, 0) == 1)
        {
            return 0;
        }

        options ??= new AssemblyLoadInstrumentationOptions();

        if (options.Enabled)
        {
#if NET
            LoadedPackageObserver? observer = null;

            // Never fail the application; a disposed factory throws here.
            try
            {
                var logger = loggerFactory.CreateLogger(typeof(AssemblyLoadInstrumentation).FullName!);

                observer = LoadedPackageObserver.Create(logger, options, assembly);
                if (observer != null)
                {
                    Volatile.Write(ref activeObserver, observer);
                    return observer.Start();
                }
            }
            catch (Exception ex)
            {
                observer?.Stop();
                Interlocked.CompareExchange(ref activeObserver, null, observer);
                AssemblyLoadEventSource.Log.StartFailed(ex.Message);
            }
#else
            _ = assembly;
            AssemblyLoadEventSource.Log.ManifestNotSupported();
#endif
        }

        // Nothing started, so allow a retry.
        Interlocked.Exchange(ref started, 0);
        return 0;
    }

    /// <summary>For tests: stops observing and allows Start again.</summary>
    internal static void ResetForTesting()
    {
#if NET
        Interlocked.Exchange(ref activeObserver, null)?.Stop();
#endif
        Interlocked.Exchange(ref started, 0);
    }
}
