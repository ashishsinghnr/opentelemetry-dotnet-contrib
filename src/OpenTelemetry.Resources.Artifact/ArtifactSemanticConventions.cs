// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Resources.Artifact;

/// <summary>
/// Attribute names from the <c>artifact.*</c> registry group.
/// </summary>
/// <remarks>
/// Declared locally, as every detector here does, rather than taken from the
/// independently versioned <c>OpenTelemetry.SemanticConventions</c> package.
/// </remarks>
internal static class ArtifactSemanticConventions
{
    public const string AttributeArtifactFilename = "artifact.filename";
    public const string AttributeArtifactPurl = "artifact.purl";
    public const string AttributeArtifactVersion = "artifact.version";
}
