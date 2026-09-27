// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Instrumentation.AssemblyLoad;

/// <summary>
/// Names for the <c>artifact.loaded</c> event proposed in semantic-conventions#4117.
/// </summary>
/// <remarks>
/// A consumed package is an artifact in SLSA's terms, so the existing
/// <c>artifact.*</c> attributes are used and the event name says it was loaded.
/// The service's own build is identified on the resource by existing attributes
/// such as <c>container.image.id</c>, never by <c>artifact.*</c>, so flattened
/// resource and record attributes cannot collide.
/// </remarks>
internal static class ArtifactSemanticConventions
{
    public const string EventName = "artifact.loaded";

    public const string AttributeEventName = "event.name";

    // Carries ecosystem, name, and version; the key to join the SBOM on.
    public const string AttributeArtifactPurl = "artifact.purl";

    public const string AttributeArtifactVersion = "artifact.version";
}
