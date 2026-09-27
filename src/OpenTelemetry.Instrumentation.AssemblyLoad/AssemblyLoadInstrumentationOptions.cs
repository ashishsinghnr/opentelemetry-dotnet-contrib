// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;

namespace OpenTelemetry.Instrumentation.AssemblyLoad;

/// <summary>
/// Options for the assembly load instrumentation.
/// </summary>
/// <remarks>
/// Each option can also be set by environment variable. A value set in code
/// takes precedence.
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
    /// Initializes a new instance of the <see cref="AssemblyLoadInstrumentationOptions"/>
    /// class, reading any values set by environment variable.
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
    /// Default value: <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Can also be set by the
    /// <c>OTEL_DOTNET_EXPERIMENTAL_ASSEMBLY_LOAD_ENABLED</c> environment variable.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the most packages reported over the life of the process.
    /// Default value: 1000.
    /// </summary>
    /// <remarks>
    /// Once reached, a diagnostic is written and observation stops. A value that
    /// is not positive keeps the default, because zero would disable reporting
    /// silently; use <see cref="Enabled"/> instead. Can also be set by the
    /// <c>OTEL_DOTNET_EXPERIMENTAL_ASSEMBLY_LOAD_MAX_PACKAGES</c> environment
    /// variable.
    /// </remarks>
    public int MaxPackages
    {
        get => this.maxPackages;
        set => this.maxPackages = value > 0 ? value : DefaultMaxPackages;
    }
}
