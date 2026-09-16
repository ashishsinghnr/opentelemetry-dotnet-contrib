# Dependency Inventory Instrumentation

| Status | |
| ------ | --- |
| Stability | [Alpha](../../README.md#alpha) |
| Code Owners | [@ashishsinghnr](https://github.com/ashishsinghnr) |

[![NuGet version badge](https://img.shields.io/nuget/v/OpenTelemetry.Instrumentation.DependencyInventory)](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.DependencyInventory)
[![NuGet download count badge](https://img.shields.io/nuget/dt/OpenTelemetry.Instrumentation.DependencyInventory)](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.DependencyInventory)
[![codecov.io](https://codecov.io/gh/open-telemetry/opentelemetry-dotnet-contrib/branch/main/graphs/badge.svg?flag=unittests-Instrumentation.DependencyInventory)](https://app.codecov.io/gh/open-telemetry/opentelemetry-dotnet-contrib?flags[0]=unittests-Instrumentation.DependencyInventory)

Reports the NuGet packages deployed with an application, as one log record per
package, so that a backend can correlate them against known vulnerabilities.

> [!IMPORTANT]
> OpenTelemetry does not define a semantic convention for an application's
> dependency inventory. The `package.*` attributes emitted by this package are
> **not** part of any OpenTelemetry specification and may change. They follow the
> names `opentelemetry-java-instrumentation` already emits, rather than being
> invented here.

Attributes are carried in each record's state, so they arrive in
`LogRecord.Attributes` and no `IncludeScopes` configuration is needed. Enable
`IncludeFormattedMessage` if you also want the human readable body
(`Dependency Newtonsoft.Json 13.0.3`); the attributes are exported either way.

## Getting Started

```shell
dotnet add package OpenTelemetry.Instrumentation.DependencyInventory --prerelease
```

## Usage

Call `Report` once, after the logging pipeline has been built.

```csharp
using Microsoft.Extensions.Logging;
using OpenTelemetry.Instrumentation.DependencyInventory;
using OpenTelemetry.Logs;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddOpenTelemetry(options => options.AddOtlpExporter());
});

DependencyInventoryReporter.Report(loggerFactory);
```

In a hosted application, call it once the host has started, so that the logger
factory exists:

```csharp
var app = builder.Build();

DependencyInventoryReporter.Report(app.Services.GetRequiredService<ILoggerFactory>());

app.Run();
```

The inventory of a running process cannot change, so only the first call that
reports something takes effect; later calls do nothing and return zero. A call
that reports nothing does not consume that one chance, so a caller that runs
before the logging pipeline is ready, or that hits a transient failure, can
retry.

Call `Report` once at startup. Concurrent calls never report twice, but a caller
that arrives while another is in flight also returns zero and cannot tell the
two cases apart.

`Report` is synchronous and reads the manifest on the calling thread: roughly
13 ms for 54 packages, scaling with the size of the dependency graph. Call it off
the startup path if that latency matters.

### Applications loaded by a host

Some platforms load the application as a class library, so the entry assembly is
the platform's runtime host and its manifest describes the host's packages rather
than the application's. Pass the application's assembly to report its own
inventory:

```csharp
DependencyInventoryReporter.Report(
    loggerFactory,
    options: null,
    assembly: typeof(MyFunction).Assembly);
```

This applies to AWS Lambda functions using the default class-library handler
model and to in-process Azure Functions. Applications that run as an executable
need no override.

## Emitted records

One record per package:

| Attribute | Example | Description |
| --------- | ------- | ----------- |
| `event.name` | `package.info` | Identifies the record. |
| `package.purl` | `pkg:nuget/Newtonsoft.Json@13.0.3` | [Package URL](https://github.com/package-url/purl-spec), the identifier vulnerability databases key on. |
| `package.name` | `Newtonsoft.Json` | Package identifier. |
| `package.version` | `13.0.3` | Package version. |
| `package.type` | `nuget` | Package ecosystem. |
| `package.loaded` | `true` | Whether any of the package's assemblies were loaded when the inventory was captured. |
| `package.checksum` | `LM5IKPSylMYp42YC9bnL...` | Hash of the package's content, verifying it against the one published. Omitted when the manifest records none. |
| `package.checksum_algorithm` | `sha512` | Algorithm the checksum was computed with. Omitted with the checksum. |

The event name and the `package.*` attributes match those emitted by
[`opentelemetry-java-instrumentation`][java-jar-analyzer], so that one backend
rule reads both runtimes. `package.purl` and `package.loaded` are additional.

Exported as OTLP, one record looks like this:

```json
{
  "body": { "stringValue": "Dependency Newtonsoft.Json 13.0.3" },
  "severityText": "Information",
  "attributes": [
    { "key": "event.name",      "value": { "stringValue": "package.info" } },
    { "key": "package.purl",    "value": { "stringValue": "pkg:nuget/Newtonsoft.Json@13.0.3" } },
    { "key": "package.name",    "value": { "stringValue": "Newtonsoft.Json" } },
    { "key": "package.version", "value": { "stringValue": "13.0.3" } },
    { "key": "package.type",    "value": { "stringValue": "nuget" } },
    { "key": "package.loaded",  "value": { "boolValue": true } },
    { "key": "package.checksum", "value": { "stringValue": "LM5IKPSylMYp42YC9bnL..." } },
    { "key": "package.checksum_algorithm", "value": { "stringValue": "sha512" } }
  ]
}
```

[java-jar-analyzer]: https://github.com/open-telemetry/opentelemetry-java-instrumentation/pull/9301

The package version is the **package's** version, which routinely differs from
the version of the assemblies it ships: `Newtonsoft.Json` 13.0.3 ships assembly
version 13.0.0.0. The Package URL always carries the package version, because
that is what a vulnerability database can resolve.

### `package.loaded`

The inventory is read from the `.deps.json` manifest, which lists everything
deployed with the application, including transitive dependencies that may never
be used. `package.loaded` distinguishes the two:

* `true` — at least one of the package's assemblies was loaded, so the code is
  in use by this process.
* `false` — the package is deployed but nothing from it has been loaded. It is
  still present on disk and still carries any known vulnerabilities, so it is
  reported by default. Set `IncludeUnloadedPackages` to `false` to omit these.

`package.loaded` is a **snapshot taken when the inventory is emitted**. A package
whose assemblies load later is not re-reported.

## Configuration

```csharp
DependencyInventoryReporter.Report(loggerFactory, new DependencyInventoryOptions
{
    MaxPackages = 500,
    IncludeUnloadedPackages = false,
});
```

| Option | Default | Description |
| ------ | ------- | ----------- |
| `Enabled` | `true` | Whether the inventory is reported at all. |
| `MaxPackages` | `1000` | Caps the number of records. A diagnostic is written when the inventory is truncated. |
| `IncludeUnloadedPackages` | `true` | Whether deployed-but-unloaded packages are reported. |

### Environment variables

Every option can also be set by environment variable, so that a deployment can be
adjusted without rebuilding it. A value set in code takes precedence.

| Variable | Option |
| -------- | ------ |
| `OTEL_DOTNET_EXPERIMENTAL_DEPENDENCY_INVENTORY_ENABLED` | `Enabled` |
| `OTEL_DOTNET_EXPERIMENTAL_DEPENDENCY_INVENTORY_MAX_PACKAGES` | `MaxPackages` |
| `OTEL_DOTNET_EXPERIMENTAL_DEPENDENCY_INVENTORY_INCLUDE_UNLOADED_PACKAGES` | `IncludeUnloadedPackages` |

Setting `..._ENABLED=false` turns the inventory off for an application that
already calls `Report`. A malformed value is ignored, the default is kept, and a
diagnostic is written to the event source.

## Supported runtimes

The inventory is read from the `.deps.json` manifest, which only .NET (Core)
applications produce.

| Target | Behaviour |
| ------ | --------- |
| .NET 8 and later | Full inventory. |
| .NET Standard 2.0 | **Reports nothing.** No manifest exists. |
| .NET Framework 4.6.2 | **Reports nothing.** No manifest exists. |

On the unsupported targets `Report` returns `0` and writes a diagnostic to the
`OpenTelemetry-Instrumentation-DependencyInventory` event source, so that an
empty inventory can be told apart from an application with no dependencies.

## Limitations

* **Single-file publish and Native AOT** do not carry a readable manifest, so
  nothing is reported. A diagnostic is written to the event source.
* **Trimmed applications** list packages in the manifest whose assemblies were
  trimmed away. These are correctly reported with `package.loaded` set to
  `false`.
* **Assemblies loaded at runtime** through `Assembly.LoadFrom` and similar are
  not in the manifest and are not reported.
* **An application loaded as a library by a host** reports the host's inventory
  unless its own assembly is passed explicitly. See
  [Applications loaded by a host](#applications-loaded-by-a-host).
* **Framework assemblies** are not NuGet packages and are not reported.
* **Vulnerability correlation happens in the backend.** This package emits only
  package identifiers; it carries no vulnerability data. A CVE published after
  deployment therefore still raises an alert without the application being
  redeployed.

## Diagnostics

Failures are written to the `OpenTelemetry-Instrumentation-DependencyInventory`
event source rather than thrown, because a telemetry feature must never prevent
an application from starting.

## References

* [OpenTelemetry Project](https://opentelemetry.io/)
* [Package URL specification](https://github.com/package-url/purl-spec)
