// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Internal;
using OpenTelemetry.Logs;

namespace OpenTelemetry.Instrumentation.AssemblyLoad.Tests;

[Collection(TestManifest.Collection)]
public sealed class LoadedPackageObserverTests : IDisposable
{
    private readonly List<LogRecord> exported = [];
    private readonly ILoggerFactory loggerFactory;

    public LoadedPackageObserverTests()
    {
        this.loggerFactory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
            options.AddInMemoryExporter(this.exported)));
    }

    public void Dispose() => this.loggerFactory.Dispose();

    [Fact]
    public void PackageIsReportedWhenAnyOfItsAssembliesLoads()
    {
        var observer = this.CreateObserver(Package("Acme.Sdk", "2.1.0", "Acme.Sdk", "Acme.Sdk.Core"));

        Assert.True(observer.TryReport("Acme.Sdk.Core"));

        Assert.Equal(["pkg:nuget/Acme.Sdk@2.1.0"], this.ReportedPurls());
    }

    [Fact]
    public void PackageIsReportedOnce()
    {
        var observer = this.CreateObserver(Package("Acme.Sdk", "2.1.0", "Acme.Sdk", "Acme.Sdk.Core"));

        Assert.True(observer.TryReport("Acme.Sdk"));
        Assert.False(observer.TryReport("Acme.Sdk.Core"));
        Assert.False(observer.TryReport("Acme.Sdk"));

        Assert.Single(this.exported);
    }

    [Fact]
    public void AssemblyAbsentFromTheManifestIsNotReported()
    {
        var observer = this.CreateObserver(Package("Acme.Sdk", "2.1.0", "Acme.Sdk"));

        Assert.False(observer.TryReport("System.Private.CoreLib"));

        Assert.Empty(this.exported);
    }

    [Fact]
    public void AssemblyNameMatchingIgnoresCase()
    {
        var observer = this.CreateObserver(Package("Newtonsoft.Json", "13.0.3", "Newtonsoft.Json"));

        Assert.True(observer.TryReport("newtonsoft.json"));
    }

    [Fact]
    public void AssemblySuppliedByTwoPackagesIsAttributedToTheFirst()
    {
        var observer = this.CreateObserver(
            Package("First", "1.0.0", "Shared"),
            Package("Second", "1.0.0", "Shared"));

        Assert.True(observer.TryReport("Shared"));

        Assert.Equal(["pkg:nuget/First@1.0.0"], this.ReportedPurls());
    }

    [Fact]
    public void RecordCarriesOnlyTheArtifactLoadedAttributes()
    {
        var observer = this.CreateObserver(Package("Newtonsoft.Json", "13.0.3", "Newtonsoft.Json"));

        observer.TryReport("Newtonsoft.Json");

        var attributes = Assert.Single(this.exported).Attributes!.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal(
            new Dictionary<string, object?>
            {
                ["event.name"] = "artifact.loaded",
                ["artifact.purl"] = "pkg:nuget/Newtonsoft.Json@13.0.3",
                ["artifact.version"] = "13.0.3",
            },
            attributes);
    }

    [Fact]
    public void ReachingMaxPackagesStopsReporting()
    {
        var observer = this.CreateObserver(
            maxPackages: 1,
            Package("First", "1.0.0", "First"),
            Package("Second", "1.0.0", "Second"),
            Package("Third", "1.0.0", "Third"));

        Assert.True(observer.TryReport("First"));
        Assert.False(observer.TryReport("Second"));
        Assert.False(observer.TryReport("Third"));

        Assert.Single(this.exported);
    }

    [Fact]
    public void StoppedObserverReportsNothing()
    {
        var observer = this.CreateObserver(Package("Acme.Sdk", "2.1.0", "Acme.Sdk"));

        observer.Stop();
        observer.Stop();

        Assert.False(observer.TryReport("Acme.Sdk"));
        Assert.Empty(this.exported);
    }

    [Fact]
    public void AssemblyInTheManifestsLoadContextIsReported()
    {
        var assembly = TestManifest.Assembly;
        var observer = this.CreateObserver(
            AssemblyLoadContext.GetLoadContext(assembly)!,
            Package("Acme.Tests", "1.0.0", assembly.GetName().Name!));

        Assert.True(observer.TryReport(assembly));
    }

    [Fact]
    public void AssemblyInAnotherLoadContextIsNotReported()
    {
        // Another context may load a version the manifest does not describe.
        var assembly = TestManifest.Assembly;
        var other = new AssemblyLoadContext("other", isCollectible: true);

        try
        {
            var observer = this.CreateObserver(
                other,
                Package("Acme.Tests", "1.0.0", assembly.GetName().Name!));

            Assert.False(observer.TryReport(assembly));
            Assert.Empty(this.exported);
        }
        finally
        {
            other.Unload();
        }
    }

    [Fact]
    public void DynamicAssemblyIsNotReported()
    {
        var dynamic = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Acme.Dynamic"),
            AssemblyBuilderAccess.Run);

        var observer = this.CreateObserver(Package("Acme.Dynamic", "1.0.0", "Acme.Dynamic"));

        Assert.False(observer.TryReport(dynamic));
    }

    [Fact]
    public void FailureWhileObservingStopsWithoutFailingTheLoad()
    {
        var (package, assemblyName) = TestManifest.FindUnloadedPackage();
        var observer = new LoadedPackageObserver(
            new ThrowingLogger(),
            [package, Package("Acme.Sdk", "2.1.0", "Acme.Sdk")],
            AssemblyLoadInstrumentationOptions.DefaultMaxPackages,
            loadContext: null);

        try
        {
            observer.Start();

            // The handler's logger throws; the load itself must still succeed.
            Assert.NotNull(Assembly.Load(new AssemblyName(assemblyName)));

            // Stopped, so the throwing logger is not reached again.
            Assert.False(observer.TryReport("Acme.Sdk"));
        }
        finally
        {
            observer.Stop();
        }
    }

    private static DepsManifestPackage Package(string name, string version, params string[] assemblies) =>
        new(name, version, [.. assemblies]);

    private LoadedPackageObserver CreateObserver(params DepsManifestPackage[] packages) =>
        this.CreateObserver(AssemblyLoadInstrumentationOptions.DefaultMaxPackages, packages);

    private LoadedPackageObserver CreateObserver(int maxPackages, params DepsManifestPackage[] packages) =>
        new(this.loggerFactory.CreateLogger("test"), [.. packages], maxPackages, loadContext: null);

    private LoadedPackageObserver CreateObserver(AssemblyLoadContext loadContext, params DepsManifestPackage[] packages) =>
        new(this.loggerFactory.CreateLogger("test"), [.. packages], AssemblyLoadInstrumentationOptions.DefaultMaxPackages, loadContext);

    private List<string> ReportedPurls() =>
        [.. this.exported
            .SelectMany(r => r.Attributes!)
            .Where(a => a.Key == ArtifactSemanticConventions.AttributeArtifactPurl)
            .Select(a => (string)a.Value!)];

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("Logging failed.");
    }
}
