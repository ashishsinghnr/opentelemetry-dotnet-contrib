// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text;
using OpenTelemetry.Internal;

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
        // This test project is built here rather than restored as a package, so
        // its own output has no published identity and no Package URL may be
        // derived from it. Reporting one would mean an identifier that resolves
        // to nothing, which a vulnerability database reads as "no known
        // vulnerabilities" rather than as an error.
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
        // A host that loads the application as a library is the entry assembly,
        // so a serverless function deployed as a class library must be able to
        // name itself. This test's own assembly stands in for that function: it
        // is not the entry assembly under a test host.
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
        // A package's identifier routinely differs from the name of the
        // assembly it ships: Humanizer.Core ships Humanizer.dll. Deriving the
        // Package URL from the assembly name would name a package that does not
        // exist, which resolves to nothing in a vulnerability database and so
        // reads as "no known vulnerabilities" rather than as an error.
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
        // A package's assembly version routinely differs from its package
        // version: Newtonsoft.Json 13.0.3 ships assembly version 13.0.0.0. The
        // package version is the one a Package URL must carry, so it is taken
        // from the library key rather than the runtime asset's metadata.
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
        // The application's own output appears in the manifest with type
        // "project". It has no published identity, so no Package URL may be
        // derived from it.
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
        // Project output is enumerated first in a real manifest. Rejecting it
        // must not abandon the search, or a genuine package match that appears
        // later would be missed.
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
        // A library that contributes no runtime assets did not supply the
        // assembly, even when the names coincide.
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
    public void MalformedManifestIsNotResolved()
    {
        Assert.Null(ReadPackageVersion("{}", "MyApp"));
        Assert.Null(ReadPackageVersion("""{ "targets": null }""", "MyApp"));
        Assert.Null(ReadPackageVersion("""{ "targets": { ".NETCoreApp,Version=v8.0": null } }""", "MyApp"));
    }

    private static DepsManifestPackage? ReadPackage(string manifest, string assemblyName)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifest));
        return DepsManifest.ReadPackageSupplying(stream, assemblyName);
    }

    private static string? ReadPackageVersion(string manifest, string assemblyName)
        => ReadPackage(manifest, assemblyName)?.Version;
}
