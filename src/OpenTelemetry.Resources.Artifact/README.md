# Artifact Resource Detector

| Status | |
| ------ | --- |
| Stability | [Alpha](../../README.md#alpha) |
| Code Owners | [@ashishsinghnr](https://github.com/ashishsinghnr) |

[![NuGet version badge](https://img.shields.io/nuget/v/OpenTelemetry.Resources.Artifact)](https://www.nuget.org/packages/OpenTelemetry.Resources.Artifact)
[![NuGet download count badge](https://img.shields.io/nuget/dt/OpenTelemetry.Resources.Artifact)](https://www.nuget.org/packages/OpenTelemetry.Resources.Artifact)
[![codecov.io](https://codecov.io/gh/open-telemetry/opentelemetry-dotnet-contrib/branch/main/graphs/badge.svg?flag=unittests-Resources.Artifact)](https://app.codecov.io/gh/open-telemetry/opentelemetry-dotnet-contrib?flags[0]=unittests-Resources.Artifact)

> [!IMPORTANT]
> Resources detected by this package are defined by [experimental semantic
> convention](https://github.com/open-telemetry/semantic-conventions/blob/v1.44.0/docs/registry/attributes/artifact.md).
> These resources can be changed without prior notification.

This detector identifies the build artifact that produced the running
application, so that all telemetry emitted by a service can be attributed back
to a specific build. The [`artifact.*`
attributes](https://github.com/open-telemetry/semantic-conventions/blob/v1.44.0/docs/registry/attributes/artifact.md)
align with the [SLSA package
model](https://slsa.dev/spec/v1.0/terminology#package-model).

## Getting Started

You need to install the `OpenTelemetry.Resources.Artifact` package to be able
to use the Artifact Resource Detector.

```shell
dotnet add package OpenTelemetry.Resources.Artifact --prerelease
```

## Usage

You can configure the Artifact resource detector on the `ResourceBuilder` with
the following example.

```csharp
using OpenTelemetry;
using OpenTelemetry.Resources;

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .ConfigureResource(resource => resource.AddArtifactDetector())
    // other configurations
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .ConfigureResource(resource => resource.AddArtifactDetector())
    // other configurations
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddArtifactDetector());
    });
});
```

### Applications loaded by a host

Some platforms load the application as a class library rather than running it as
an executable, so the entry assembly is the platform's runtime host. A serverless
function deployed as a class library is the common case: an AWS Lambda function
using the default handler model, or an in-process Azure Function. In those
environments the default overload describes the host, not the function.

Pass the assembly explicitly so that the function is described instead:

```csharp
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .ConfigureResource(resource =>
        resource.AddArtifactDetector(typeof(MyFunction).Assembly))
    .Build();
```

Platforms that run the application as an executable need no override. This
includes ASP.NET Core apps, worker services, container images with their own
entry point, AWS Lambda custom runtimes, and the Azure Functions isolated worker
model.

## Detected attributes

The detector derives its attributes from the entry assembly and records the
following metadata:

- **ArtifactDetector**: `artifact.filename`, `artifact.version`, and, when it
  can be determined, `artifact.purl`.

`artifact.version` is taken from the assembly's informational version, which
carries the full package version including any prerelease label. Any Source
Link commit suffix (the `+<commit sha>` portion) is removed. When no
informational version is present, the assembly version is used instead.

### Package URL

`artifact.purl` is only reported when the entry assembly can be matched to a
package in the application's `.deps.json` manifest. A Package URL is never
synthesized from the assembly name alone, because an identifier that does not
correspond to a published package cannot be resolved by consumers.

Most applications are not themselves published as NuGet packages, so
`artifact.purl` is absent for them. This is expected, and `artifact.filename`
and `artifact.version` are still reported.

## Limitations

- The manifest is unavailable under some publish layouts, including single-file
  publish and Native AOT. In these cases `artifact.purl` is omitted while
  `artifact.filename` and `artifact.version` are unaffected. A diagnostic is
  written to the event source, which also covers the case of an assembly loaded
  by a host: see [Applications loaded by a host](#applications-loaded-by-a-host).
- On .NET Framework there is no `.deps.json` manifest, so `artifact.purl` is
  never reported.
- No attributes are reported when there is no entry assembly, which is the case
  for some hosts that load managed code through unmanaged entry points.

## References

- [OpenTelemetry Project](https://opentelemetry.io/)
- [Package URL specification](https://github.com/package-url/purl-spec)
- [SLSA package model](https://slsa.dev/spec/v1.0/terminology#package-model)
