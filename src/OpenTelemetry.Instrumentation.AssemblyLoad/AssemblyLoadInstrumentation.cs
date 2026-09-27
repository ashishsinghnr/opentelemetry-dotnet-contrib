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
    /// Reports the NuGet packages already loaded, one log record per package, then
    /// reports each further package the first time one of its assemblies loads.
    /// </summary>
    /// <param name="loggerFactory">
    /// The logger factory the OpenTelemetry logging provider is registered on. It
    /// must live as long as the process.
    /// </param>
    /// <param name="options">
    /// The instrumentation options, or <see langword="null"/> for the defaults.
    /// </param>
    /// <param name="assembly">
    /// The assembly whose <c>.deps.json</c> describes the deployment, or
    /// <see langword="null"/> for the entry assembly. Pass it when a host loads
    /// the application as a library, such as a serverless function, because the
    /// entry assembly is then the host's.
    /// </param>
    /// <returns>
    /// The number of packages already loaded. Zero when the instrumentation is
    /// unavailable or has already started.
    /// </returns>
    /// <remarks>
    /// Only the first call that starts takes effect. A call that fails to start
    /// can be retried.
    /// </remarks>
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

            // Telemetry must never stop an application starting. A disposed
            // factory throws from CreateLogger.
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

    /// <summary>
    /// Stops observation and resets the once-per-process guard. For testing only.
    /// </summary>
    internal static void ResetForTesting()
    {
#if NET
        Interlocked.Exchange(ref activeObserver, null)?.Stop();
#endif
        Interlocked.Exchange(ref started, 0);
    }
}
