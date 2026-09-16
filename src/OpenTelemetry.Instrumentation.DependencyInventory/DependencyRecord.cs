// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NET
using Microsoft.Extensions.Logging;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.DependencyInventory;

/// <summary>
/// Writes a single dependency record.
/// </summary>
/// <remarks>
/// Attributes are carried in the log record's state rather than on a logging
/// scope. Scope values are only exported when <c>IncludeScopes</c> is enabled,
/// which is off by default, and they arrive separately from the record's own
/// attributes; state is always exported and lands directly in
/// <c>LogRecord.Attributes</c>.
/// </remarks>
internal static class DependencyRecord
{
    public static void Write(ILogger logger, DepsManifestPackage package, bool isLoaded)
    {
        // The provider maps these pairs onto log record attributes. No
        // "{OriginalFormat}" entry is included, because only some exporters strip
        // it and the rest would export it.
        var state = new List<KeyValuePair<string, object?>>(6)
        {
            new(PackageSemanticConventions.AttributeEventName, PackageSemanticConventions.EventName),
            new(PackageSemanticConventions.AttributePackagePurl, package.Purl),
            new(PackageSemanticConventions.AttributePackageName, package.Name),
            new(PackageSemanticConventions.AttributePackageVersion, package.Version),
            new(PackageSemanticConventions.AttributePackageType, PackageSemanticConventions.PackageTypeNuGet),
            new(PackageSemanticConventions.AttributePackageLoaded, isLoaded),
        };

        logger.Log(
            LogLevel.Information,
            new EventId(1, PackageSemanticConventions.EventName),
            state,
            exception: null,
            formatter: static (s, _) => Format(s));
    }

    /// <summary>
    /// Renders the record's body. The formatter's result is what the exporter
    /// reports as the log body, so it must describe the package rather than
    /// returning the state's default representation.
    /// </summary>
    /// <param name="state">The record's attributes.</param>
    /// <returns>A human readable description of the dependency.</returns>
    private static string Format(List<KeyValuePair<string, object?>> state)
    {
        string? name = null;
        string? version = null;

        foreach (var entry in state)
        {
            if (entry.Key == PackageSemanticConventions.AttributePackageName)
            {
                name = entry.Value as string;
            }
            else if (entry.Key == PackageSemanticConventions.AttributePackageVersion)
            {
                version = entry.Value as string;
            }
        }

        return $"Dependency {name} {version}";
    }
}
#endif
