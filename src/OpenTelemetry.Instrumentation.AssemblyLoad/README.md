# Assembly Load Instrumentation

| Status | |
| ------ | --- |
| Stability | [Alpha](../../README.md#alpha) |
| Code Owners | [@ashishsinghnr](https://github.com/ashishsinghnr), [@alanwest](https://github.com/alanwest) |

[![NuGet version badge](https://img.shields.io/nuget/v/OpenTelemetry.Instrumentation.AssemblyLoad)](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.AssemblyLoad)
[![NuGet download count badge](https://img.shields.io/nuget/dt/OpenTelemetry.Instrumentation.AssemblyLoad)](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.AssemblyLoad)
[![codecov.io](https://codecov.io/gh/open-telemetry/opentelemetry-dotnet-contrib/branch/main/graphs/badge.svg?flag=unittests-Instrumentation.AssemblyLoad)](https://app.codecov.io/gh/open-telemetry/opentelemetry-dotnet-contrib?flags[0]=unittests-Instrumentation.AssemblyLoad)

Reports the NuGet packages an application actually loads at runtime, as one log
record per package emitted the first time one of its assemblies loads, so that a
backend can correlate them against known vulnerabilities.

> [!IMPORTANT]
> OpenTelemetry does not yet define an `artifact.loaded` event. The event is
> **not** part of any OpenTelemetry specification and may change. It is
> proposed in
> [semantic-conventions#4117](https://github.com/open-telemetry/semantic-conventions/issues/4117).
> The `artifact.purl` and `artifact.version` attributes it carries are existing
> registry attributes.

## What is reported, and why

This package reports what the process **loads**, not what it ships. The
deployed package graph is already described by the build's SBOM; re-sending it
on every start of every replica adds nothing. What the SBOM cannot tell you is
which of those packages a running process actually uses, and that is what this
package reports.

Assemblies load lazily, so a package is reported at the moment one of its
assemblies loads, including long after startup. Each package is reported at most
once per process.

The application's `.deps.json` manifest is still read, but only as a lookup
table from a loaded assembly to the package that supplied it. It is never
reported itself. The lookup is needed because an assembly's version routinely
differs from its package's (`Newtonsoft.Json` 13.0.3 ships assembly version
13.0.0.0), and only the package version resolves in a vulnerability database.

### Joining to the SBOM

A record identifies a package by its [Package URL](https://github.com/package-url/purl-spec).
The build the process is running, and so the SBOM that describes it, is
identified on the **resource** by existing attributes such as
`container.image.id` or `oci.manifest.digest`, set by a resource detector. A
backend joins in two steps: resource to SBOM by build identity, then record to
SBOM entry by `artifact.purl`.

This package sets no resource attributes. In particular it never places
`artifact.*` on the resource, so backends that flatten resource and record
attributes together see no collision.

## Getting Started

```shell
dotnet add package OpenTelemetry.Instrumentation.AssemblyLoad --prerelease
```

This package emits through `ILogger` and does not depend on the OpenTelemetry
SDK. The application registers the OpenTelemetry logging provider and an
exporter itself, as shown below.

## Usage

Call `Start` once, after the logging pipeline has been built.

```csharp
using Microsoft.Extensions.Logging;
using OpenTelemetry.Instrumentation.AssemblyLoad;
using OpenTelemetry.Logs;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddOpenTelemetry(options => options.AddOtlpExporter());
});

AssemblyLoadInstrumentation.Start(loggerFactory);
```

In a hosted application, call it once the host has started, so that the logger
factory exists:

```csharp
var app = builder.Build();

AssemblyLoadInstrumentation.Start(app.Services.GetRequiredService<ILoggerFactory>());

app.Run();
```

`Start` reports the packages already loaded, returns how many that was, and
then goes on observing loads for the life of the process. The logger factory
must therefore live as long as the process; records for packages that load
after it is disposed are lost.

Only the first call that starts observation takes effect; later calls do
nothing and return zero. A call that fails to start, because the manifest cannot
be read or the logger factory is unusable, does not consume that one chance, so
a caller that runs before the logging pipeline is ready can retry. Concurrent
calls never start twice, but a caller that arrives while another is in flight
also returns zero and cannot tell the two cases apart.

A package is reported once, when it loads, and never repeated. A pod that has
been up for a month reported most of its packages on day one, so a seven day log
retention window no longer contains them. A backend that needs the loaded set
for the life of the process should store these records when they arrive, rather
than relying on the raw records still being queryable.

`Start` parses the manifest synchronously on the calling thread, so its cost
scales with the size of the dependency graph. After that, each assembly load
costs one dictionary lookup, and one log record the first time a package is
seen.

### Applications loaded by a host

Some platforms load the application as a class library, so the entry assembly is
the platform's runtime host and its manifest describes the host's packages rather
than the application's. Pass the application's assembly to report its own
packages:

```csharp
AssemblyLoadInstrumentation.Start(
    loggerFactory,
    options: null,
    assembly: typeof(MyFunction).Assembly);
```

This applies to AWS Lambda functions using the default class-library handler
model and to in-process Azure Functions. Applications that run as an executable
need no override.

Only assemblies loaded into the same `AssemblyLoadContext` as that assembly are
matched, because the manifest describes that context. A plugin loaded into a
context of its own may carry a different version of the same assembly.

## Emitted records

One `artifact.loaded` event per loaded package:

| Attribute | Example | Description |
| --------- | ------- | ----------- |
| `event.name` | `artifact.loaded` | Identifies the record. |
| `artifact.purl` | `pkg:nuget/Newtonsoft.Json@13.0.3` | [Package URL](https://github.com/package-url/purl-spec) of the loaded package: the key to join on, both to the SBOM and to vulnerability databases. |
| `artifact.version` | `13.0.3` | Package version. |

The package name and ecosystem are inside the Package URL, so they are not
repeated as attributes.

Exported as OTLP, one record looks like this:

```json
{
  "body": { "stringValue": "Loaded pkg:nuget/Newtonsoft.Json@13.0.3" },
  "severityText": "Information",
  "attributes": [
    { "key": "event.name",       "value": { "stringValue": "artifact.loaded" } },
    { "key": "artifact.purl",    "value": { "stringValue": "pkg:nuget/Newtonsoft.Json@13.0.3" } },
    { "key": "artifact.version", "value": { "stringValue": "13.0.3" } }
  ]
}
```

Attributes are carried in each record's state, so they arrive in
`LogRecord.Attributes` and no `IncludeScopes` configuration is needed. Enable
`IncludeFormattedMessage` if you also want the human readable body; the
attributes are exported either way.

## Configuration

```csharp
AssemblyLoadInstrumentation.Start(loggerFactory, new AssemblyLoadInstrumentationOptions
{
    MaxPackages = 500,
});
```

| Option | Default | Description |
| ------ | ------- | ----------- |
| `Enabled` | `true` | Whether loaded packages are reported at all. |
| `MaxPackages` | `1000` | Caps the records over the life of the process. When it is reached a diagnostic is written and observation stops. |

### Environment variables

Every option can also be set by environment variable, so that a deployment can be
adjusted without rebuilding it. A value set in code takes precedence.

| Variable | Option | Accepted values |
| -------- | ------ | --------------- |
| `OTEL_DOTNET_EXPERIMENTAL_ASSEMBLY_LOAD_ENABLED` | `Enabled` | `true` or `false`, any casing |
| `OTEL_DOTNET_EXPERIMENTAL_ASSEMBLY_LOAD_MAX_PACKAGES` | `MaxPackages` | a positive integer, digits only |

Setting `..._ENABLED=false` turns the instrumentation off for an application that
already calls `Start`. Only `true` and `false` are recognized, so `1` and `0`
leave the default in place.

Any value that cannot be parsed is ignored, the default is kept, and a diagnostic
is written to the event source. `..._MAX_PACKAGES` also keeps the default when the
value is not positive, because a cap of zero would disable the instrumentation silently;
use `..._ENABLED` to turn it off.

## Supported runtimes

The assembly-to-package lookup is read from the `.deps.json` manifest, which only
.NET (Core) applications produce.

| Target | Behaviour |
| ------ | --------- |
| .NET 8 and later | Loaded packages are reported. |
| .NET Standard 2.0 | **Reports nothing.** No manifest exists. |
| .NET Framework 4.6.2 | **Reports nothing.** No manifest exists. |

On the unsupported targets `Start` returns `0` and writes a diagnostic to the
`OpenTelemetry-Instrumentation-AssemblyLoad` event source, so that an
empty result can be told apart from an application with no dependencies.

## Limitations

* **Single-file publish and Native AOT** do not carry a readable manifest, so
  nothing is reported. A diagnostic is written to the event source. Native AOT
  also performs no assembly loads to observe.
* **Assemblies absent from the manifest are never reported**, however they were
  loaded. A plugin resolved at runtime from outside the deployment has no entry
  to map it to a package.
* **An application loaded as a library by a host** describes the host, not
  itself, unless its own assembly is passed explicitly: it reports the host's
  packages when the host has a manifest, and nothing when it does not. See
  [Applications loaded by a host](#applications-loaded-by-a-host).
* **Framework assemblies** are not NuGet packages and are not reported.
* **Vulnerability correlation happens in the backend.** This package emits only
  package identifiers; it carries no vulnerability data. A CVE published after
  deployment therefore still raises an alert without the application being
  redeployed.

## Diagnostics

Failures are written to the `OpenTelemetry-Instrumentation-AssemblyLoad`
event source rather than thrown, because a telemetry feature must never prevent
an application from starting or an assembly from loading. If reporting a loaded
package ever fails, observation stops rather than failing again on every later
load.

## References

* [OpenTelemetry Project](https://opentelemetry.io/)
* [Package URL specification](https://github.com/package-url/purl-spec)
* [Artifact attributes registry](https://opentelemetry.io/docs/specs/semconv/registry/attributes/artifact/)
