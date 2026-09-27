// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;

namespace OpenTelemetry.Instrumentation.AssemblyLoad;

/// <summary>
/// Options for the assembly load instrumentation.
/// </summary>
/// <remarks>
/// Each option can also be set by environment variable; code takes precedence.
/// </remarks>
public class AssemblyLoadInstrumentationOptions
{
    internal const int DefaultMaxPackages = 1000;

    internal const string EnabledEnvVar =
        "OTEL_DOTNET_EXPERIMENTAL_ASSEMBLY_LOAD_ENABLED";

    internal const string MaxPackagesEnvVar =
        "OTEL_DOTNET_EXPERIMENTAL_ASSEMBLY_LOAD_MAX_PACKAGES";

    private int maxPackages = DefaultMaxPackages;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssemblyLoadInstrumentationOptions"/> class.
    /// </summary>
    public AssemblyLoadInstrumentationOptions()
        : this(new ConfigurationBuilder().AddEnvironmentVariables().Build())
    {
    }

    internal AssemblyLoadInstrumentationOptions(IConfiguration configuration)
    {
        if (configuration.TryGetBoolValue(
            AssemblyLoadEventSource.Log,
            EnabledEnvVar,
            out var enabled))
        {
            this.Enabled = enabled;
        }

        if (configuration.TryGetIntValue(
            AssemblyLoadEventSource.Log,
            MaxPackagesEnvVar,
            out var maxPackagesValue))
        {
            this.MaxPackages = maxPackagesValue;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether loaded packages are reported.
    /// Default: <see langword="true"/>. Env: <c>OTEL_DOTNET_EXPERIMENTAL_ASSEMBLY_LOAD_ENABLED</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the most packages reported per process; observation then stops.
    /// Default: 1000. Env: <c>OTEL_DOTNET_EXPERIMENTAL_ASSEMBLY_LOAD_MAX_PACKAGES</c>.
    /// </summary>
    /// <remarks>
    /// A non-positive value keeps the default; use <see cref="Enabled"/> to turn reporting off.
    /// </remarks>
    public int MaxPackages
    {
        get => this.maxPackages;
        set => this.maxPackages = value > 0 ? value : DefaultMaxPackages;
    }
}
