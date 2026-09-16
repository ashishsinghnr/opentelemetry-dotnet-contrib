// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using OpenTelemetry.Internal;
using OpenTelemetry.Resources.Artifact;

namespace OpenTelemetry.Resources;

/// <summary>
/// Extension methods to simplify registering of the artifact resource detector.
/// </summary>
public static class ArtifactResourceBuilderExtensions
{
    /// <summary>
    /// Enables the artifact resource detector.
    /// </summary>
    /// <param name="builder">The <see cref="ResourceBuilder"/> being configured.</param>
    /// <returns>The instance of <see cref="ResourceBuilder"/> being configured.</returns>
    public static ResourceBuilder AddArtifactDetector(this ResourceBuilder builder)
    {
        Guard.ThrowIfNull(builder);
        return builder.AddDetector(new ArtifactDetector());
    }

    /// <summary>
    /// Enables the artifact resource detector for a given assembly.
    /// </summary>
    /// <param name="builder">The <see cref="ResourceBuilder"/> being configured.</param>
    /// <param name="assembly">The assembly to describe.</param>
    /// <returns>The instance of <see cref="ResourceBuilder"/> being configured.</returns>
    /// <remarks>
    /// Use this when a host loads the application as a library and so is itself
    /// the entry assembly, as for a serverless function deployed as a class
    /// library. Pass the function's assembly to describe it rather than the host.
    /// </remarks>
    public static ResourceBuilder AddArtifactDetector(this ResourceBuilder builder, Assembly assembly)
    {
        Guard.ThrowIfNull(builder);
        Guard.ThrowIfNull(assembly);
        return builder.AddDetector(new ArtifactDetector(assembly));
    }
}
