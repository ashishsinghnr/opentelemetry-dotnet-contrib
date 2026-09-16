// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Instrumentation.DependencyInventory.Tests;

public class LoadedAssemblySetTests
{
    [Fact]
    public void CaptureIncludesThisTestAssembly()
    {
        var loaded = LoadedAssemblySet.Capture();

        var thisAssembly = typeof(LoadedAssemblySetTests).Assembly.GetName().Name;
        Assert.NotNull(thisAssembly);
        Assert.Contains(thisAssembly, loaded);
    }

    [Fact]
    public void CaptureIsCaseInsensitive()
    {
        var loaded = LoadedAssemblySet.Capture();

        var thisAssembly = typeof(LoadedAssemblySetTests).Assembly.GetName().Name!;

        Assert.Contains(thisAssembly.ToUpperInvariant(), loaded);
    }

    [Fact]
    public void PackageIsLoadedWhenAnyOfItsAssembliesIsLoaded()
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Acme.Sdk.Core" };

        // Only the second assembly is loaded, which still means the package is
        // in use.
        Assert.True(LoadedAssemblySet.IsAnyLoaded(loaded, ["Acme.Sdk", "Acme.Sdk.Core"]));
    }

    [Fact]
    public void PackageIsNotLoadedWhenNoneOfItsAssembliesIsLoaded()
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Something.Else" };

        Assert.False(LoadedAssemblySet.IsAnyLoaded(loaded, ["Acme.Sdk", "Acme.Sdk.Core"]));
    }

    [Fact]
    public void PackageWithNoAssembliesIsNotLoaded()
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Anything" };

        Assert.False(LoadedAssemblySet.IsAnyLoaded(loaded, []));
    }

    [Fact]
    public void AssemblyNameMatchingIgnoresCase()
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "newtonsoft.json" };

        Assert.True(LoadedAssemblySet.IsAnyLoaded(loaded, ["Newtonsoft.Json"]));
    }
}
