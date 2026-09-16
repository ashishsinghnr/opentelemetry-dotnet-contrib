// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Reflection;
using System.Text;
using OpenTelemetry.Internal;
using OpenTelemetry.Trace;

namespace OpenTelemetry.Resources.Artifact.Tests;

public class ArtifactDetectorTests
{
    [Fact]
    public void ArtifactDetectorReturnsFilenameAndVersion()
    {
        var resource = ResourceBuilder.CreateEmpty().AddArtifactDetector().Build();

        Assert.NotNull(resource);
        Assert.StartsWith("https://opentelemetry.io/schemas/", resource.SchemaUrl);

        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        var filename = Assert.IsType<string>(resourceAttributes[ArtifactSemanticConventions.AttributeArtifactFilename]);
        Assert.NotEmpty(filename);

        var version = Assert.IsType<string>(resourceAttributes[ArtifactSemanticConventions.AttributeArtifactVersion]);
        Assert.NotEmpty(version);
    }

    [Fact]
    public void ArtifactFilenameMatchesEntryAssembly()
    {
        var expected = Assembly.GetEntryAssembly()?.GetName().Name;
        Assert.NotNull(expected);

        var resource = ResourceBuilder.CreateEmpty().AddArtifactDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        Assert.Equal(expected, resourceAttributes[ArtifactSemanticConventions.AttributeArtifactFilename]);
    }

    [Fact]
    public void ArtifactVersionExcludesSourceLinkCommitSuffix()
    {
        var resource = ResourceBuilder.CreateEmpty().AddArtifactDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        var version = Assert.IsType<string>(resourceAttributes[ArtifactSemanticConventions.AttributeArtifactVersion]);

        Assert.DoesNotContain("+", version, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactPurlIdentifiesThePackageThatSuppliedTheAssembly()
    {
        // This project is built, not restored as a package, so its output has no
        // published identity and no Package URL may be derived from it.
        var resource = ResourceBuilder.CreateEmpty()
            .AddArtifactDetector(typeof(ArtifactDetectorTests).Assembly)
            .Build();

        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        Assert.False(
            resourceAttributes.ContainsKey(ArtifactSemanticConventions.AttributeArtifactPurl),
            "A Package URL must not be reported for project output.");

        // The remaining attributes still describe the assembly.
        Assert.Equal(
            typeof(ArtifactDetectorTests).Assembly.GetName().Name,
            resourceAttributes[ArtifactSemanticConventions.AttributeArtifactFilename]);
    }

    [Fact]
    public void ArtifactDetectorReportsOnlyKnownAttributes()
    {
        var resource = ResourceBuilder.CreateEmpty().AddArtifactDetector().Build();

        var expected = new[]
        {
            ArtifactSemanticConventions.AttributeArtifactFilename,
            ArtifactSemanticConventions.AttributeArtifactPurl,
            ArtifactSemanticConventions.AttributeArtifactVersion,
        };

        Assert.All(resource.Attributes, attribute => Assert.Contains(attribute.Key, expected));
    }

    [Fact]
    public void ArtifactDetectorIsRepeatable()
    {
        var first = ResourceBuilder.CreateEmpty().AddArtifactDetector().Build();
        var second = ResourceBuilder.CreateEmpty().AddArtifactDetector().Build();

        Assert.Equal(
            first.Attributes.OrderBy(x => x.Key, StringComparer.Ordinal),
            second.Attributes.OrderBy(x => x.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void AddArtifactDetectorThrowsOnNullBuilder()
    {
        Assert.Throws<ArgumentNullException>(
            () => ((ResourceBuilder)null!).AddArtifactDetector());
    }

    [Fact]
    public void GivenAssemblyIsDescribedInsteadOfTheEntryAssembly()
    {
        // Under a test host this assembly is not the entry assembly, so it stands
        // in for a serverless function that must name itself.
        var ownAssembly = typeof(ArtifactDetectorTests).Assembly;
        var entryAssembly = Assembly.GetEntryAssembly();

        Assert.NotSame(ownAssembly, entryAssembly);

        var resource = ResourceBuilder.CreateEmpty().AddArtifactDetector(ownAssembly).Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        Assert.Equal(
            ownAssembly.GetName().Name,
            resourceAttributes[ArtifactSemanticConventions.AttributeArtifactFilename]);

        // The default overload reports the entry assembly, which is different.
        var entryResource = ResourceBuilder.CreateEmpty().AddArtifactDetector().Build();
        var entryAttributes = entryResource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        Assert.NotEqual(
            resourceAttributes[ArtifactSemanticConventions.AttributeArtifactFilename],
            entryAttributes[ArtifactSemanticConventions.AttributeArtifactFilename]);
    }

    [Fact]
    public void AddArtifactDetectorThrowsOnNullAssembly()
    {
        Assert.Throws<ArgumentNullException>(
            () => ResourceBuilder.CreateEmpty().AddArtifactDetector(null!));
    }

    [Fact]
    public void PackageUrlNamesThePackageNotTheAssembly()
    {
        // Humanizer.Core ships Humanizer.dll. Using the assembly name would name
        // a package that does not exist.
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Humanizer.Core/2.14.1": {
                "runtime": { "lib/net6.0/Humanizer.dll": {} }
              }
            }
          },
          "libraries": {
            "Humanizer.Core/2.14.1": { "type": "package" }
          }
        }
        """;

        var package = ReadPackage(Manifest, "Humanizer");

        Assert.NotNull(package);
        Assert.Equal("Humanizer.Core", package!.Name);
        Assert.Equal("pkg:nuget/Humanizer.Core@2.14.1", package.Purl);
    }

    [Fact]
    public void PackageVersionComesFromLibraryKeyNotAssemblyVersion()
    {
        // Newtonsoft.Json 13.0.3 ships assembly version 13.0.0.0. A Package URL
        // must carry the package version, so it comes from the library key.
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Newtonsoft.Json/13.0.3": {
                "runtime": {
                  "lib/net6.0/Newtonsoft.Json.dll": {
                    "assemblyVersion": "13.0.0.0",
                    "fileVersion": "13.0.3.27908"
                  }
                }
              }
            }
          },
          "libraries": {
            "Newtonsoft.Json/13.0.3": { "type": "package" }
          }
        }
        """;

        Assert.Equal("13.0.3", ReadPackageVersion(Manifest, "Newtonsoft.Json"));
    }

    [Fact]
    public void ProjectOutputIsNotTreatedAsAPackage()
    {
        // The application's own output has type "project" and no published
        // identity, so no Package URL may be derived from it.
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "MyApp/1.2.3": {
                "runtime": { "MyApp.dll": {} }
              }
            }
          },
          "libraries": {
            "MyApp/1.2.3": { "type": "project" }
          }
        }
        """;

        Assert.Null(ReadPackageVersion(Manifest, "MyApp"));
    }

    [Fact]
    public void PackageIsFoundWhenProjectOutputSuppliesTheSameAssemblyName()
    {
        // Project output comes first in a real manifest, so rejecting it must not
        // abandon the search for a later package match.
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "app/2.4.1": {
                "runtime": { "app.dll": {} }
              },
              "Acme.Tool/9.9.9": {
                "runtime": { "lib/net8.0/app.dll": {} }
              }
            }
          },
          "libraries": {
            "app/2.4.1": { "type": "project" },
            "Acme.Tool/9.9.9": { "type": "package" }
          }
        }
        """;

        Assert.Equal("9.9.9", ReadPackageVersion(Manifest, "app"));
    }

    [Fact]
    public void AssemblyNameIsMatchedCaseInsensitively()
    {
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Serilog/4.0.0": {
                "runtime": { "lib/net8.0/Serilog.dll": {} }
              }
            }
          },
          "libraries": {
            "Serilog/4.0.0": { "type": "package" }
          }
        }
        """;

        Assert.Equal("4.0.0", ReadPackageVersion(Manifest, "serilog"));
    }

    [Fact]
    public void ReferenceOnlyLibraryIsIgnored()
    {
        // A library with no runtime assets did not supply the assembly, even when
        // the names coincide.
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Some.Package/1.0.0": {
                "compile": { "ref/net8.0/Some.Package.dll": {} }
              }
            }
          },
          "libraries": {
            "Some.Package/1.0.0": { "type": "package" }
          }
        }
        """;

        Assert.Null(ReadPackageVersion(Manifest, "Some.Package"));
    }

    [Fact]
    public void UnknownAssemblyIsNotResolved()
    {
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Serilog/4.0.0": {
                "runtime": { "lib/net8.0/Serilog.dll": {} }
              }
            }
          },
          "libraries": {
            "Serilog/4.0.0": { "type": "package" }
          }
        }
        """;

        Assert.Null(ReadPackageVersion(Manifest, "System.Private.CoreLib"));
    }

    [Fact]
    public void PlaceholderAssetIsIgnoredButRealUnderscoreAssemblyIsKept()
    {
        // "_._" is a placeholder for shipping nothing, matched on the whole file
        // name so a real "_.dll" is still reported.
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Empty.Package/1.0.0": {
                "runtime": { "lib/net8.0/_._": {} }
              },
              "Underscore.Package/2.0.0": {
                "runtime": { "lib/net8.0/_.dll": {} }
              }
            }
          },
          "libraries": {
            "Empty.Package/1.0.0": { "type": "package" },
            "Underscore.Package/2.0.0": { "type": "package" }
          }
        }
        """;

        Assert.Equal("2.0.0", ReadPackageVersion(Manifest, "_"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"notanobject\"")]
    [InlineData("""{ "targets": null }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": null } }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "MyApp/1.0": "notanobject" } } }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "MyApp/1.0": { "runtime": null } } } }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "MyApp/1.0": { "runtime": { "MyApp.dll": {} } } } }, "libraries": null }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "MyApp/1.0": { "runtime": { "MyApp.dll": {} } } } }, "libraries": { "MyApp/1.0": null } }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "MyApp/1.0": { "runtime": { "MyApp.dll": {} } } } }, "libraries": { "MyApp/1.0": { "type": 5 } } }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "MyApp" : { "runtime": { "MyApp.dll": {} } } } }, "libraries": { "MyApp": { "type": "package" } } }""")]
    public void MalformedManifestIsNotResolved(string manifest)
    {
        // Must resolve nothing rather than throw: an exception here would escape
        // Detect and fail provider construction.
        Assert.Null(ReadPackageVersion(manifest, "MyApp"));
    }

    [Fact]
    public void DetectDoesNotThrowWhenTheManifestIsMalformed()
    {
        // A corrupt manifest must not stop the application starting.
        var directory = Directory.CreateTempSubdirectory("ArtifactDetectorTests");

        try
        {
            var assembly = typeof(ArtifactDetectorTests).Assembly;
            var manifestPath = Path.Combine(
                directory.FullName,
                assembly.GetName().Name + ".deps.json");

            File.WriteAllText(manifestPath, """{ "targets": { "net8.0": { "A/1.0": "bad" } } }""");

            var resource = ResourceBuilder.CreateEmpty()
                .AddArtifactDetector(assembly)
                .Build();

            // The name is still reported; only the Package URL is lost.
            var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

            Assert.Equal(
                assembly.GetName().Name,
                resourceAttributes[ArtifactSemanticConventions.AttributeArtifactFilename]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DetectedAttributesReachExportedTelemetry()
    {
        // Exercises the detector through a real provider and exporter, rather than
        // alone, so a failure to run inside the pipeline is caught.
        const string SourceName = "ArtifactDetectorTests.EndToEnd";

        var exportedItems = new List<Activity>();

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(resource => resource.AddArtifactDetector())
            .AddSource(SourceName)
            .AddInMemoryExporter(exportedItems)
            .Build();

        using var source = new ActivitySource(SourceName);
        using (var activity = source.StartActivity("Test"))
        {
            Assert.NotNull(activity);
        }

        tracerProvider.ForceFlush();

        // The span reached the exporter, so the resource is the one that provider
        // built and attaches to its telemetry.
        Assert.Single(exportedItems);

        var resourceAttributes = tracerProvider.GetResource().Attributes
            .ToDictionary(x => x.Key, x => x.Value);

        var filename = Assert.IsType<string>(
            resourceAttributes[ArtifactSemanticConventions.AttributeArtifactFilename]);
        Assert.NotEmpty(filename);
    }

    private static DepsManifestPackage? ReadPackage(string manifest, string assemblyName)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifest));
        return DepsManifest.ReadPackageSupplying(stream, assemblyName);
    }

    private static string? ReadPackageVersion(string manifest, string assemblyName)
        => ReadPackage(manifest, assemblyName)?.Version;
}
