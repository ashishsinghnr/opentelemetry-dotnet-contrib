// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NET
using Microsoft.Extensions.Logging;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.AssemblyLoad;

/// <summary>
/// Writes one <c>artifact.loaded</c> log record.
/// </summary>
internal static class ArtifactLoadedRecord
{
    public static void Write(ILogger logger, DepsManifestPackage package)
    {
        // Carried as state, not a scope, so attributes export without
        // IncludeScopes. No "{OriginalFormat}" entry, which some exporters keep.
        var state = new List<KeyValuePair<string, object?>>(3)
        {
            new(ArtifactSemanticConventions.AttributeEventName, ArtifactSemanticConventions.EventName),
            new(ArtifactSemanticConventions.AttributeArtifactPurl, package.Purl),
            new(ArtifactSemanticConventions.AttributeArtifactVersion, package.Version),
        };

        logger.Log(
            LogLevel.Information,
            new EventId(1, ArtifactSemanticConventions.EventName),
            state,
            exception: null,
            formatter: static (s, _) => FormatBody(s));
    }

    private static string FormatBody(List<KeyValuePair<string, object?>> state)
    {
        foreach (var attribute in state)
        {
            if (attribute.Key == ArtifactSemanticConventions.AttributeArtifactPurl)
            {
                return $"Loaded {attribute.Value}";
            }
        }

        return "Loaded";
    }
}
#endif
