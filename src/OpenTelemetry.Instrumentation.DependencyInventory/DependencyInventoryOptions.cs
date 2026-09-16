// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace OpenTelemetry.Instrumentation.DependencyInventory;

/// <summary>
/// Options for the dependency inventory.
/// </summary>
/// <remarks>
/// Each option can also be set by environment variable, so that a deployment can
/// be adjusted without rebuilding. A value set in code takes precedence.
/// </remarks>
public class DependencyInventoryOptions
{
    internal const int DefaultMaxPackages = 1000;

    internal const string EnabledEnvVar =
        "OTEL_DOTNET_EXPERIMENTAL_DEPENDENCY_INVENTORY_ENABLED";

    internal const string MaxPackagesEnvVar =
        "OTEL_DOTNET_EXPERIMENTAL_DEPENDENCY_INVENTORY_MAX_PACKAGES";

    internal const string IncludeUnloadedEnvVar =
        "OTEL_DOTNET_EXPERIMENTAL_DEPENDENCY_INVENTORY_INCLUDE_UNLOADED_PACKAGES";

    private int maxPackages = DefaultMaxPackages;

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyInventoryOptions"/>
    /// class, reading any values set by environment variable.
    /// </summary>
    public DependencyInventoryOptions()
        : this(new ConfigurationBuilder().AddEnvironmentVariables().Build())
    {
    }

    internal DependencyInventoryOptions(IConfiguration configuration)
    {
        Debug.Assert(configuration != null, "configuration was null");

        if (configuration!.TryGetBoolValue(
            DependencyInventoryEventSource.Log,
            EnabledEnvVar,
            out var enabled))
        {
            this.Enabled = enabled;
        }

        if (configuration!.TryGetIntValue(
            DependencyInventoryEventSource.Log,
            MaxPackagesEnvVar,
            out var maxPackagesValue))
        {
            this.MaxPackages = maxPackagesValue;
        }

        if (configuration!.TryGetBoolValue(
            DependencyInventoryEventSource.Log,
            IncludeUnloadedEnvVar,
            out var includeUnloaded))
        {
            this.IncludeUnloadedPackages = includeUnloaded;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the inventory is reported at all.
    /// Default value: <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Set the
    /// <c>OTEL_DOTNET_EXPERIMENTAL_DEPENDENCY_INVENTORY_ENABLED</c> environment
    /// variable to <c>false</c> to turn the inventory off for a deployment that
    /// already calls
    /// <see cref="DependencyInventoryReporter.Report(Microsoft.Extensions.Logging.ILoggerFactory, DependencyInventoryOptions?, System.Reflection.Assembly?)"/>,
    /// without rebuilding it.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of packages to report. A truncation
    /// notice is recorded when the inventory is larger. Default value: 1000.
    /// </summary>
    /// <remarks>
    /// The inventory is emitted once per process, so this bounds a single burst
    /// of records rather than an ongoing rate. A value that is not positive is
    /// ignored and the default is kept, because a cap of zero would disable the
    /// inventory silently; use <see cref="Enabled"/> to turn it off. Can also be
    /// set by the
    /// <c>OTEL_DOTNET_EXPERIMENTAL_DEPENDENCY_INVENTORY_MAX_PACKAGES</c>
    /// environment variable.
    /// </remarks>
    public int MaxPackages
    {
        get => this.maxPackages;
        set => this.maxPackages = value > 0 ? value : DefaultMaxPackages;
    }

    /// <summary>
    /// Gets or sets a value indicating whether packages that are deployed but
    /// whose assemblies have not been loaded are reported. Default value:
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Unloaded packages are still deployed and still carry any known
    /// vulnerabilities, so they are reported by default. Set this to
    /// <see langword="false"/> to report only what the process has loaded. Can
    /// also be set by the
    /// <c>OTEL_DOTNET_EXPERIMENTAL_DEPENDENCY_INVENTORY_INCLUDE_UNLOADED_PACKAGES</c>
    /// environment variable.
    /// </remarks>
    public bool IncludeUnloadedPackages { get; set; } = true;
}
