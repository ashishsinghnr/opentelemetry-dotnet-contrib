// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Instrumentation.AssemblyLoad;

/// <summary>
/// Names for the <c>artifact.loaded</c> event proposed in semantic-conventions#4117.
/// </summary>
/// <remarks>
/// <c>artifact.*</c> is never set on the resource, so flattened attributes cannot collide.
/// </remarks>
internal static class ArtifactSemanticConventions
{
    public const string EventName = "artifact.loaded";

    public const string AttributeEventName = "event.name";

    // Joins the record to the SBOM.
    public const string AttributeArtifactPurl = "artifact.purl";

    public const string AttributeArtifactVersion = "artifact.version";
}
