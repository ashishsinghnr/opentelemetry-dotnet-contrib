// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Resources.Artifact;

/// <summary>
/// Attribute names from the <c>artifact.*</c> registry group.
/// </summary>
/// <remarks>
/// These names are also generated into the <c>OpenTelemetry.SemanticConventions</c>
/// package. They are declared locally instead of taken from there, because that
/// package is versioned and released independently and no component in this
/// repository depends on it; every resource detector here declares the names it
/// uses. The duplication is deliberate.
/// </remarks>
internal static class ArtifactSemanticConventions
{
    public const string AttributeArtifactFilename = "artifact.filename";
    public const string AttributeArtifactPurl = "artifact.purl";
    public const string AttributeArtifactVersion = "artifact.version";
}
