// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Tracing;
using Microsoft.Extensions.Configuration;

namespace OpenTelemetry.Instrumentation.DependencyInventory;

[EventSource(Name = "OpenTelemetry-Instrumentation-DependencyInventory")]
internal sealed class DependencyInventoryEventSource : EventSource, IConfigurationExtensionsLogger
{
    public static readonly DependencyInventoryEventSource Log = new();

    private const int EventIdManifestNotSupported = 1;
    private const int EventIdManifestNotFound = 2;
    private const int EventIdFailedToReadManifest = 3;
    private const int EventIdInventoryTruncated = 4;
    private const int EventIdNoEntryAssembly = 5;
    private const int EventIdReportFailed = 6;
    private const int EventIdInvalidConfigurationValue = 7;

    [Event(EventIdManifestNotSupported, Message = "No dependency inventory was reported: this runtime does not produce a dependency manifest. The inventory requires .NET (Core).", Level = EventLevel.Warning)]
    public void ManifestNotSupported()
    {
        this.WriteEvent(EventIdManifestNotSupported);
    }

    [Event(EventIdManifestNotFound, Message = "No dependency inventory was reported: no manifest was found for entry assembly '{0}'. Single-file and Native AOT publishes do not carry a readable manifest.", Level = EventLevel.Warning)]
    public void ManifestNotFound(string entryAssemblyName)
    {
        this.WriteEvent(EventIdManifestNotFound, entryAssemblyName);
    }

    [Event(EventIdFailedToReadManifest, Message = "No dependency inventory was reported: the manifest could not be read. Details: '{0}'", Level = EventLevel.Warning)]
    public void FailedToReadManifest(string error)
    {
        this.WriteEvent(EventIdFailedToReadManifest, error);
    }

    [Event(EventIdInventoryTruncated, Message = "The dependency inventory was truncated at {0} of {1} packages. Raise MaxPackages to report the remainder.", Level = EventLevel.Warning)]
    public void InventoryTruncated(int reported, int total)
    {
        this.WriteEvent(EventIdInventoryTruncated, reported, total);
    }

    [Event(EventIdNoEntryAssembly, Message = "No dependency inventory was reported: the process has no entry assembly.", Level = EventLevel.Warning)]
    public void NoEntryAssembly()
    {
        this.WriteEvent(EventIdNoEntryAssembly);
    }

    [Event(EventIdReportFailed, Message = "No dependency inventory was reported: the attempt failed. Details: '{0}'", Level = EventLevel.Warning)]
    public void ReportFailed(string error)
    {
        this.WriteEvent(EventIdReportFailed, error);
    }

    [Event(EventIdInvalidConfigurationValue, Message = "Configuration key '{0}' has an invalid value: '{1}'. The default was used instead.", Level = EventLevel.Warning)]
    public void InvalidConfigurationValue(string key, string value)
    {
        this.WriteEvent(EventIdInvalidConfigurationValue, key, value);
    }

    void IConfigurationExtensionsLogger.LogInvalidConfigurationValue(string key, string value)
    {
        this.InvalidConfigurationValue(key, value);
    }
}
