// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.DependencyInventory;

/// <summary>
/// Reports the application's dependency inventory.
/// </summary>
public static class DependencyInventoryReporter
{
    private static int reported;

    /// <summary>
    /// Reports the NuGet packages deployed with the application, emitting one
    /// log record per package.
    /// </summary>
    /// <param name="loggerFactory">
    /// The logger factory to emit through. This must be the factory that the
    /// OpenTelemetry logging provider is registered on.
    /// </param>
    /// <param name="options">
    /// The inventory options, or <see langword="null"/> for the defaults.
    /// </param>
    /// <param name="assembly">
    /// The assembly whose <c>.deps.json</c> describes the deployment, or
    /// <see langword="null"/> for the entry assembly. Pass the assembly
    /// explicitly when the application is loaded as a library by a host, such as
    /// a serverless function deployed as a class library, because the entry
    /// assembly is then the platform's runtime host.
    /// </param>
    /// <returns>
    /// The number of packages reported. Zero when the inventory is unavailable,
    /// or when it has already been reported for this process.
    /// </returns>
    /// <remarks>
    /// The inventory of a running process cannot change, so only the first call
    /// that reports something takes effect; later calls do nothing and return
    /// zero. A call that reports nothing does not consume that one chance, so a
    /// caller that runs before the logging pipeline is ready, or that hits a
    /// transient failure reading the manifest, can retry.
    /// <para>
    /// Call this once at startup. Concurrent calls never report twice, but a
    /// caller that arrives while another is in flight also returns zero and
    /// cannot tell the two cases apart.
    /// </para>
    /// </remarks>
    public static int Report(
        ILoggerFactory loggerFactory,
        DependencyInventoryOptions? options = null,
        Assembly? assembly = null)
    {
        Guard.ThrowIfNull(loggerFactory);

        if (Interlocked.CompareExchange(ref reported, 1, 0) == 1)
        {
            return 0;
        }

        var emitted = 0;

        // Telemetry must never stop an application starting, so nothing here is
        // allowed to propagate. Creating the logger is guarded too: a disposed
        // factory throws.
        try
        {
            var logger = loggerFactory.CreateLogger(typeof(DependencyInventoryReporter).FullName!);

            emitted = DependencyInventory.Emit(logger, options ?? new DependencyInventoryOptions(), assembly);
        }
        catch (Exception ex)
        {
            DependencyInventoryEventSource.Log.ReportFailed(ex.Message);
        }

        if (emitted == 0)
        {
            // Nothing was reported, so release the guard rather than leaving the
            // inventory permanently disabled for the process.
            Interlocked.Exchange(ref reported, 0);
        }

        return emitted;
    }

    /// <summary>
    /// Resets the once-per-process guard. For testing only.
    /// </summary>
    internal static void ResetForTesting()
    {
        Interlocked.Exchange(ref reported, 0);
    }
}
