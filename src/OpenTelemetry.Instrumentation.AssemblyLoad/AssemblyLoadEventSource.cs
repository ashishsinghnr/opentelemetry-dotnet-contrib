// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Tracing;
using Microsoft.Extensions.Configuration;

namespace OpenTelemetry.Instrumentation.AssemblyLoad;

[EventSource(Name = "OpenTelemetry-Instrumentation-AssemblyLoad")]
internal sealed class AssemblyLoadEventSource : EventSource, IConfigurationExtensionsLogger
{
    public static readonly AssemblyLoadEventSource Log = new();

    private const int EventIdManifestNotSupported = 1;
    private const int EventIdManifestNotFound = 2;
    private const int EventIdFailedToReadManifest = 3;
    private const int EventIdPackageLimitReached = 4;
    private const int EventIdNoEntryAssembly = 5;
    private const int EventIdStartFailed = 6;
    private const int EventIdInvalidConfigurationValue = 7;
    private const int EventIdObservationFailed = 8;

    [Event(EventIdManifestNotSupported, Message = "No loaded packages are reported: this runtime does not produce a dependency manifest. The instrumentation requires .NET (Core).", Level = EventLevel.Warning)]
    public void ManifestNotSupported()
    {
        this.WriteEvent(EventIdManifestNotSupported);
    }

    [Event(EventIdManifestNotFound, Message = "No loaded packages are reported: no manifest was found for assembly '{0}'. Single-file and Native AOT publishes do not carry a readable manifest.", Level = EventLevel.Warning)]
    public void ManifestNotFound(string assemblyName)
    {
        this.WriteEvent(EventIdManifestNotFound, assemblyName);
    }

    [Event(EventIdFailedToReadManifest, Message = "No loaded packages are reported: the manifest could not be read. Details: '{0}'", Level = EventLevel.Warning)]
    public void FailedToReadManifest(string error)
    {
        this.WriteEvent(EventIdFailedToReadManifest, error);
    }

    [Event(EventIdPackageLimitReached, Message = "The limit of {0} reported packages was reached. Packages loaded later are not reported; raise MaxPackages to report them.", Level = EventLevel.Warning)]
    public void PackageLimitReached(int limit)
    {
        this.WriteEvent(EventIdPackageLimitReached, limit);
    }

    [Event(EventIdNoEntryAssembly, Message = "No loaded packages are reported: the process has no entry assembly.", Level = EventLevel.Warning)]
    public void NoEntryAssembly()
    {
        this.WriteEvent(EventIdNoEntryAssembly);
    }

    [Event(EventIdStartFailed, Message = "No loaded packages are reported: the instrumentation failed to start. Details: '{0}'", Level = EventLevel.Warning)]
    public void StartFailed(string error)
    {
        this.WriteEvent(EventIdStartFailed, error);
    }

    [Event(EventIdInvalidConfigurationValue, Message = "Configuration key '{0}' has an invalid value: '{1}'. The default was used instead.", Level = EventLevel.Warning)]
    public void InvalidConfigurationValue(string key, string value)
    {
        this.WriteEvent(EventIdInvalidConfigurationValue, key, value);
    }

    [Event(EventIdObservationFailed, Message = "Loaded packages are no longer reported: reporting one failed. Details: '{0}'", Level = EventLevel.Warning)]
    public void ObservationFailed(string error)
    {
        this.WriteEvent(EventIdObservationFailed, error);
    }

    void IConfigurationExtensionsLogger.LogInvalidConfigurationValue(string key, string value)
    {
        this.InvalidConfigurationValue(key, value);
    }
}
