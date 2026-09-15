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

    [Event(EventIdFailedToReadDepsManifest, Message = "Failed to read the dependency manifest, the Package URL will not be reported. Details: '{0}'", Level = EventLevel.Verbose)]
    public void FailedToReadDepsManifest(string error)
    {
        this.WriteEvent(EventIdFailedToReadDepsManifest, error);
    }

    [Event(EventIdNoManifestBesideAssembly, Message = "No dependency manifest was found beside assembly '{0}'. If this is not the application being described - for example a serverless function loaded as a class library by a platform host - pass the application's assembly to AddArtifactDetector so that it is described rather than the host.", Level = EventLevel.Warning)]
    public void NoManifestBesideAssembly(string assemblyName)
    {
        this.WriteEvent(EventIdNoManifestBesideAssembly, assemblyName);
    }
}
