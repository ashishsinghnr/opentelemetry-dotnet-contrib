// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.AssemblyLoad.Tests;

public class DepsManifestTests
{
    [Fact]
    public void EveryPackageInTheManifestIsRead()
    {
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Newtonsoft.Json/13.0.3": {
                "runtime": { "lib/net6.0/Newtonsoft.Json.dll": {} }
              },
              "Serilog/4.0.0": {
                "runtime": { "lib/net8.0/Serilog.dll": {} }
              },
              "AWSSDK.S3/3.7.1": {
                "runtime": { "lib/net8.0/AWSSDK.S3.dll": {} }
              }
            }
          },
          "libraries": {
            "Newtonsoft.Json/13.0.3": { "type": "package" },
            "Serilog/4.0.0": { "type": "package" },
            "AWSSDK.S3/3.7.1": { "type": "package" }
          }
        }
        """;

        var packages = ReadPackages(Manifest);

        Assert.Equal(3, packages.Count);
        Assert.Contains("pkg:nuget/Newtonsoft.Json@13.0.3", packages.Select(p => p.Purl));
        Assert.Contains("pkg:nuget/Serilog@4.0.0", packages.Select(p => p.Purl));
        Assert.Contains("pkg:nuget/AWSSDK.S3@3.7.1", packages.Select(p => p.Purl));
    }

    [Fact]
    public void PackageVersionComesFromLibraryKeyNotAssemblyVersion()
    {
        // Newtonsoft.Json 13.0.3 ships assembly version 13.0.0.0.
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
          "libraries": { "Newtonsoft.Json/13.0.3": { "type": "package" } }
        }
        """;

        var package = Assert.Single(ReadPackages(Manifest));

        Assert.Equal("13.0.3", package.Version);
        Assert.Equal("pkg:nuget/Newtonsoft.Json@13.0.3", package.Purl);
    }

    [Theory]
    [InlineData("Newtonsoft.Json", "13.0.3", "pkg:nuget/Newtonsoft.Json@13.0.3")]
    [InlineData("Microsoft_Some-Pkg.Core", "2.0.0-beta.1", "pkg:nuget/Microsoft_Some-Pkg.Core@2.0.0-beta.1")]
    [InlineData("Pakët.Core", "1.0.0", "pkg:nuget/Pak%C3%ABt.Core@1.0.0")]
    public void PurlPercentEncodesOnlyCharactersOutsideTheAllowedSet(string name, string version, string expected)
    {
        // Case is preserved; only non-ASCII characters are encoded, as UTF-8.
        Assert.Equal(expected, new DepsManifestPackage(name, version, []).Purl);
    }

    [Fact]
    public void ProjectOutputIsExcluded()
    {
        // Project output has no published identity.
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "MyApp/1.0.0": { "runtime": { "MyApp.dll": {} } },
              "Serilog/4.0.0": { "runtime": { "lib/net8.0/Serilog.dll": {} } }
            }
          },
          "libraries": {
            "MyApp/1.0.0": { "type": "project" },
            "Serilog/4.0.0": { "type": "package" }
          }
        }
        """;

        var package = Assert.Single(ReadPackages(Manifest));

        Assert.Equal("Serilog", package.Name);
    }

    [Fact]
    public void PackageInSeveralTargetsIsReadOnce()
    {
        // A runtime-specific manifest repeats libraries per target.
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Serilog/4.0.0": { "runtime": { "lib/net8.0/Serilog.dll": {} } }
            },
            ".NETCoreApp,Version=v8.0/osx-arm64": {
              "Serilog/4.0.0": { "runtime": { "lib/net8.0/Serilog.dll": {} } }
            }
          },
          "libraries": { "Serilog/4.0.0": { "type": "package" } }
        }
        """;

        var package = Assert.Single(ReadPackages(Manifest));

        Assert.Equal("pkg:nuget/Serilog@4.0.0", package.Purl);
    }

    [Fact]
    public void PackageWithNoRuntimeAssetsHasNoAssemblies()
    {
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Some.Analyzer/1.0.0": {}
            }
          },
          "libraries": { "Some.Analyzer/1.0.0": { "type": "package" } }
        }
        """;

        var package = Assert.Single(ReadPackages(Manifest));

        Assert.Equal("Some.Analyzer", package.Name);
        Assert.Empty(package.Assemblies);
    }

    [Fact]
    public void PlaceholderRuntimeAssetIsNotAnAssembly()
    {
        // "_._" means no assets for that framework.
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Some.Package/1.0.0": { "runtime": { "lib/net8.0/_._": {} } }
            }
          },
          "libraries": { "Some.Package/1.0.0": { "type": "package" } }
        }
        """;

        var package = Assert.Single(ReadPackages(Manifest));

        Assert.Empty(package.Assemblies);
    }

    [Fact]
    public void EveryRuntimeAssemblyOfAPackageIsRead()
    {
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Acme.Sdk/2.0.0": {
                "runtime": {
                  "lib/net8.0/Acme.Sdk.dll": {},
                  "lib/net8.0/Acme.Sdk.Core.dll": {}
                }
              }
            }
          },
          "libraries": { "Acme.Sdk/2.0.0": { "type": "package" } }
        }
        """;

        var package = Assert.Single(ReadPackages(Manifest));

        Assert.Equal(2, package.Assemblies.Count);
        Assert.Contains("Acme.Sdk", package.Assemblies);
        Assert.Contains("Acme.Sdk.Core", package.Assemblies);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"notanobject\"")]
    [InlineData("""{ "targets": null }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": null } }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "A/1.0": "notanobject" } } }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "A/1.0": { "runtime": null } } } }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "A/1.0": { "runtime": { "A.dll": {} } } } }, "libraries": null }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "A/1.0": { "runtime": { "A.dll": {} } } } }, "libraries": { "A/1.0": null } }""")]
    [InlineData("""{ "targets": { ".NETCoreApp,Version=v8.0": { "A/1.0": { "runtime": { "A.dll": {} } } } }, "libraries": { "A/1.0": { "type": 5 } } }""")]
    public void MalformedManifestYieldsNoPackages(string manifest)
    {
        // Valid JSON of an unexpected shape must not throw.
        Assert.Empty(ReadPackages(manifest));
    }

    [Fact]
    public void PlaceholderIsMatchedOnTheWholeFileName()
    {
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Empty.Package/1.0.0": { "runtime": { "lib/net8.0/_._": {} } },
              "Underscore.Package/2.0.0": { "runtime": { "lib/net8.0/_.dll": {} } }
            }
          },
          "libraries": {
            "Empty.Package/1.0.0": { "type": "package" },
            "Underscore.Package/2.0.0": { "type": "package" }
          }
        }
        """;

        var packages = ReadPackages(Manifest);

        Assert.Equal(2, packages.Count);

        var empty = Assert.Single(packages, p => p.Name == "Empty.Package");
        Assert.Empty(empty.Assemblies);

        var underscore = Assert.Single(packages, p => p.Name == "Underscore.Package");
        Assert.Equal("_", Assert.Single(underscore.Assemblies));
    }

    [Fact]
    public void LibraryNotDeclaredAPackageIsSkipped()
    {
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Mystery/1.0.0": { "runtime": { "lib/net8.0/Mystery.dll": {} } }
            }
          },
          "libraries": {}
        }
        """;

        Assert.Empty(ReadPackages(Manifest));
    }

    [Fact]
    public void HostManifestListIsSplitOnSemicolonOnEveryPlatform()
    {
        // Path.PathSeparator is ':' on Unix and would never match.
        var directory = Directory.CreateTempSubdirectory("DepsManifestTests");

        try
        {
            var wanted = Path.Combine(directory.FullName, "MyApp.deps.json");
            File.WriteAllText(wanted, "{}");

            var framework = Path.Combine(directory.FullName, "Microsoft.NETCore.App.deps.json");
            File.WriteAllText(framework, "{}");

            // Listed first, as the host does.
            var list = framework + ";" + wanted;

            Assert.Equal(
                wanted,
                DepsManifest.FindInDepsFilesList(list, "MyApp.deps.json"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(";")]
    [InlineData("/no/such/path/MyApp.deps.json")]
    [InlineData("/no/such/path/Other.deps.json")]
    public void HostManifestListWithoutAMatchResolvesToNull(string list)
    {
        Assert.Null(DepsManifest.FindInDepsFilesList(list, "MyApp.deps.json"));
    }

    private static List<DepsManifestPackage> ReadPackages(string manifest)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifest));
        return DepsManifest.ReadPackages(stream);
    }
}
