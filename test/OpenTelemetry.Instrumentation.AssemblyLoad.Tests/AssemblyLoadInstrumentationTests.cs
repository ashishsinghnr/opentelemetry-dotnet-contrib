// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace OpenTelemetry.Instrumentation.AssemblyLoad.Tests;

// Tests pass their own assembly: under testhost the entry assembly is the host's.
[Collection(TestManifest.Collection)]
public sealed class AssemblyLoadInstrumentationTests : IDisposable
{
    public AssemblyLoadInstrumentationTests()
    {
        AssemblyLoadInstrumentation.ResetForTesting();
    }

    public void Dispose() => AssemblyLoadInstrumentation.ResetForTesting();

    [Fact]
    public void StartReportsEachLoadedPackageAsAnArtifactLoadedRecord()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var count = StartAndStop(loggerFactory);

        Assert.True(count > 0, "Expected at least one loaded package.");
        Assert.Equal(count, exported.Count);
        Assert.All(exported, record =>
        {
            var attributes = record.Attributes!.ToDictionary(a => a.Key, a => a.Value);
            var purl = Assert.IsType<string>(attributes[ArtifactSemanticConventions.AttributeArtifactPurl]);
            var version = Assert.IsType<string>(attributes[ArtifactSemanticConventions.AttributeArtifactVersion]);

            Assert.Equal(ArtifactSemanticConventions.EventName, attributes[ArtifactSemanticConventions.AttributeEventName]);
            Assert.StartsWith("pkg:nuget/", purl, StringComparison.Ordinal);
            Assert.EndsWith("@" + version, purl, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void StartReportsAPackageThisTestUses()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        StartAndStop(loggerFactory);

        Assert.Contains(Purls(exported), p => p.StartsWith("pkg:nuget/OpenTelemetry@", StringComparison.Ordinal));
    }

    [Fact]
    public void StartDoesNotReportPackagesThatAreNotLoaded()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        StartAndStop(loggerFactory);

        var loaded = TestManifest.LoadedAssemblyNames();
        var packagesByPurl = TestManifest.ReadPackages().ToDictionary(p => p.Purl);

        Assert.All(Purls(exported), purl =>
            Assert.Contains(packagesByPurl[purl].Assemblies, loaded.Contains));
    }

    [Fact]
    public void PackageLoadedAfterStartIsReportedWhenItLoads()
    {
        var (package, assemblyName) = TestManifest.FindUnloadedPackage();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        AssemblyLoadInstrumentation.Start(loggerFactory, options: null, TestManifest.Assembly);
        Assert.DoesNotContain(package.Purl, Purls(exported));

        Assembly.Load(new AssemblyName(assemblyName));

        Assert.Single(Purls(exported), p => p == package.Purl);
    }

    [Fact]
    public void PackageLoadedAfterStopIsNotReported()
    {
        var (package, assemblyName) = TestManifest.FindUnloadedPackage();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        StartAndStop(loggerFactory);
        Assembly.Load(new AssemblyName(assemblyName));

        Assert.DoesNotContain(package.Purl, Purls(exported));
    }

    [Fact]
    public void StartTakesEffectOnlyOncePerProcess()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var first = AssemblyLoadInstrumentation.Start(loggerFactory, options: null, TestManifest.Assembly);
        var second = AssemblyLoadInstrumentation.Start(loggerFactory, options: null, TestManifest.Assembly);
        AssemblyLoadInstrumentation.ResetForTesting();

        Assert.True(first > 0);
        Assert.Equal(0, second);
        Assert.Equal(first, exported.Count);
    }

    [Fact]
    public async Task ConcurrentStartsTakeEffectOnlyOnce()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);
        using var gate = new ManualResetEventSlim(false);

        var callers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() =>
            {
                gate.Wait(TimeSpan.FromSeconds(5));
                return AssemblyLoadInstrumentation.Start(loggerFactory, options: null, TestManifest.Assembly);
            }))
            .ToArray();

        gate.Set();
        var results = await Task.WhenAll(callers);
        AssemblyLoadInstrumentation.ResetForTesting();

        var reported = Assert.Single(results, r => r > 0);
        Assert.Equal(reported, exported.Count);
    }

    [Fact]
    public void FailedStartCanBeRetried()
    {
        // A disposed factory throws from CreateLogger.
        var disposed = LoggerFactory.Create(builder => { });
        disposed.Dispose();

        Assert.Equal(0, AssemblyLoadInstrumentation.Start(disposed, options: null, TestManifest.Assembly));

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        Assert.True(AssemblyLoadInstrumentation.Start(loggerFactory, options: null, TestManifest.Assembly) > 0);
        Assert.Equal(0, AssemblyLoadInstrumentation.Start(loggerFactory, options: null, TestManifest.Assembly));
    }

    [Fact]
    public void MaxPackagesBoundsTheRecords()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var count = StartAndStop(loggerFactory, new AssemblyLoadInstrumentationOptions { MaxPackages = 2 });

        Assert.Equal(2, count);
        Assert.Equal(2, exported.Count);
    }

    [Fact]
    public void DisabledInstrumentationReportsNothing()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var count = StartAndStop(loggerFactory, new AssemblyLoadInstrumentationOptions { Enabled = false });

        Assert.Equal(0, count);
        Assert.Empty(exported);
    }

    [Fact]
    public void StartThrowsOnNullLoggerFactory()
    {
        Assert.Throws<ArgumentNullException>(() => AssemblyLoadInstrumentation.Start(null!));
    }

    [Fact]
    public void RecordsCarryNoOriginalFormat()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        StartAndStop(loggerFactory);

        Assert.NotEmpty(exported);
        Assert.All(exported, record =>
            Assert.DoesNotContain("{OriginalFormat}", record.Attributes!.Select(a => a.Key)));
    }

    [Fact]
    public void RecordBodyNamesThePackage()
    {
        var exported = new List<LogRecord>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddInMemoryExporter(exported);
        }));

        StartAndStop(loggerFactory);

        Assert.NotEmpty(exported);
        Assert.All(exported, record =>
        {
            var purl = record.Attributes!.First(a => a.Key == ArtifactSemanticConventions.AttributeArtifactPurl).Value;
            Assert.Equal($"Loaded {purl}", record.FormattedMessage);
        });
    }

    [Fact]
    public void OptionsAreReadFromEnvironmentVariables()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AssemblyLoadInstrumentationOptions.EnabledEnvVar] = "false",
                [AssemblyLoadInstrumentationOptions.MaxPackagesEnvVar] = "7",
            })
            .Build();

        var options = new AssemblyLoadInstrumentationOptions(configuration);

        Assert.False(options.Enabled);
        Assert.Equal(7, options.MaxPackages);
    }

    [Fact]
    public void OptionsKeepDefaultsWithoutEnvironmentVariables()
    {
        var options = new AssemblyLoadInstrumentationOptions(new ConfigurationBuilder().Build());

        Assert.True(options.Enabled);
        Assert.Equal(AssemblyLoadInstrumentationOptions.DefaultMaxPackages, options.MaxPackages);
    }

    [Fact]
    public void InvalidEnvironmentVariableKeepsTheDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AssemblyLoadInstrumentationOptions.MaxPackagesEnvVar] = "not-a-number",
            })
            .Build();

        var options = new AssemblyLoadInstrumentationOptions(configuration);

        Assert.Equal(AssemblyLoadInstrumentationOptions.DefaultMaxPackages, options.MaxPackages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveMaxPackagesKeepsTheDefault(int value)
    {
        var options = new AssemblyLoadInstrumentationOptions { MaxPackages = value };

        Assert.Equal(AssemblyLoadInstrumentationOptions.DefaultMaxPackages, options.MaxPackages);
    }

    private static ILoggerFactory CreateLoggerFactory(List<LogRecord> exported) =>
        LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
            options.AddInMemoryExporter(exported)));

    // Stops at once so a later load cannot change the count.
    private static int StartAndStop(ILoggerFactory loggerFactory, AssemblyLoadInstrumentationOptions? options = null)
    {
        var count = AssemblyLoadInstrumentation.Start(loggerFactory, options, TestManifest.Assembly);
        AssemblyLoadInstrumentation.ResetForTesting();
        return count;
    }

    private static List<string> Purls(List<LogRecord> exported) =>
        [.. exported
            .SelectMany(r => r.Attributes!)
            .Where(a => a.Key == ArtifactSemanticConventions.AttributeArtifactPurl)
            .Select(a => (string)a.Value!)];
}
