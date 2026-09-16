// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Tracing;

namespace OpenTelemetry.Resources.Artifact;

[EventSource(Name = "OpenTelemetry-Resources-Artifact")]
internal sealed class ArtifactDetectorEventSource : EventSource
{
    public static readonly ArtifactDetectorEventSource Log = new();

    private const int EventIdFailedToReadDepsManifest = 1;
    private const int EventIdNoManifestBesideAssembly = 2;

    [Event(EventIdFailedToReadDepsManifest, Message = "Failed to read the dependency manifest, the Package URL will not be reported. Details: '{0}'", Level = EventLevel.Warning)]
    public void FailedToReadDepsManifest(string error)
    {
        this.WriteEvent(EventIdFailedToReadDepsManifest, error);
    }

    // Verbose: expected for single-file, Native AOT, and test hosts.
    [Event(EventIdNoManifestBesideAssembly, Message = "No dependency manifest was found beside assembly '{0}'. If this is a platform host rather than the application, pass the application's assembly to AddArtifactDetector.", Level = EventLevel.Verbose)]
    public void NoManifestBesideAssembly(string assemblyName)
    {
        this.WriteEvent(EventIdNoManifestBesideAssembly, assemblyName);
    }
}
