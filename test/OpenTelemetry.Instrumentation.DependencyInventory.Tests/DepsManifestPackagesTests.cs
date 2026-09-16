// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.DependencyInventory.Tests;

public class DepsManifestPackagesTests
{
    [Fact]
    public void EveryPackageInTheManifestIsReported()
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
        // Newtonsoft.Json 13.0.3 ships assembly version 13.0.0.0. The Package
        // URL must carry the package version, which only the library key holds.
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

    [Fact]
    public void ProjectOutputIsExcluded()
    {
        // The application's own output has no published identity, so it is not
        // part of the dependency inventory.
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
    public void PackagesAppearingInSeveralTargetsAreReportedOnce()
    {
        // A runtime-specific manifest repeats libraries across targets.
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
    public void PackageWithNoRuntimeAssetsIsStillReported()
    {
        // A package can be deployed while contributing no runtime assembly, and
        // it still carries any known vulnerabilities. It is reported with no
        // assemblies, which leaves it correctly marked as not loaded.
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
    public void PlaceholderRuntimeAssetIsNotTreatedAsAnAssembly()
    {
        // "_._" marks a target framework that intentionally ships nothing.
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
    public void SuppliedAssembliesAreRecorded()
    {
        // One package can ship several assemblies, and the loaded overlay needs
        // all of them to decide whether the package is in use.
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
        // A malformed manifest must yield an empty inventory rather than throw:
        // an exception here is reported as a failure and no packages at all, which
        // a backend cannot distinguish from an application with no dependencies.
        Assert.Empty(ReadPackages(manifest));
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
    public void LibraryWithNoTypeIsNotReported()
    {
        // Without a "libraries" entry declaring it a package, a library cannot
        // be confirmed as published and is left out.
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
    public void ChecksumIsSplitFromItsAlgorithmPrefix()
    {
        // NuGet records the hash as "<algorithm>-<base64>".
        const string Manifest = """
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Serilog/4.0.0": { "runtime": { "lib/net8.0/Serilog.dll": {} } }
            }
          },
          "libraries": {
            "Serilog/4.0.0": {
              "type": "package",
              "sha512": "sha512-LM5IKPSylMYp42YC9bnL+JNUnQUS2y9TgoFLjnqeXLpDvqeyiQ=="
            }
          }
        }
        """;

        var package = Assert.Single(ReadPackages(Manifest));

        Assert.Equal("sha512", package.ChecksumAlgorithm);
        Assert.Equal("LM5IKPSylMYp42YC9bnL+JNUnQUS2y9TgoFLjnqeXLpDvqeyiQ==", package.Checksum);
    }

    [Theory]
    [InlineData("""{ "type": "package" }""")]
    [InlineData("""{ "type": "package", "sha512": "" }""")]
    [InlineData("""{ "type": "package", "sha512": "nodashhere" }""")]
    [InlineData("""{ "type": "package", "sha512": "-leadingdash" }""")]
    [InlineData("""{ "type": "package", "sha512": "trailingdash-" }""")]
    [InlineData("""{ "type": "package", "sha512": 5 }""")]
    public void PackageWithNoUsableChecksumReportsNone(string libraryEntry)
    {
        // An absent or unprefixed hash is reported as none rather than guessed at,
        // because a hash of an unknown algorithm cannot be verified.
        var manifest = $$"""
        {
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "Serilog/4.0.0": { "runtime": { "lib/net8.0/Serilog.dll": {} } }
            }
          },
          "libraries": { "Serilog/4.0.0": {{libraryEntry}} }
        }
        """;

        var package = Assert.Single(ReadPackages(manifest));

        Assert.Null(package.Checksum);
        Assert.Null(package.ChecksumAlgorithm);
    }

    [Fact]
    public void HostManifestListIsSplitOnSemicolonOnEveryPlatform()
    {
        // The host separates the paths with a semicolon everywhere, so splitting
        // on Path.PathSeparator would yield the whole list as one entry on Unix
        // and never match.
        var directory = Directory.CreateTempSubdirectory("DepsManifestTests");

        try
        {
            var wanted = Path.Combine(directory.FullName, "MyApp.deps.json");
            File.WriteAllText(wanted, "{}");

            var framework = Path.Combine(directory.FullName, "Microsoft.NETCore.App.deps.json");
            File.WriteAllText(framework, "{}");

            // The framework's manifest is listed first, as the host lists it.
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
