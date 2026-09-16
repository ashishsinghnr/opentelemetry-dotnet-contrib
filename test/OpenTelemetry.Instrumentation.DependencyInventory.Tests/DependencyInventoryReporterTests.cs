// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace OpenTelemetry.Instrumentation.DependencyInventory.Tests;

public class DependencyInventoryReporterTests
{
    // Under a test host the entry assembly is testhost, whose manifest describes
    // the SDK rather than this project. Tests that assert on this project's own
    // dependencies name its assembly explicitly, which is the same code path a
    // serverless function deployed as a class library uses.
    private static readonly System.Reflection.Assembly TestAssembly =
        typeof(DependencyInventoryReporterTests).Assembly;

    [Fact]
    public void ReportEmitsARecordPerPackageWithPurlAttributes()
    {
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var count = DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);

        // The test project has real NuGet dependencies, so the inventory is not
        // empty. Asserting on a fixed count would be brittle.
        Assert.True(count > 0, "Expected the inventory to report at least one package.");
        Assert.Equal(count, exported.Count);

        var attributes = exported
            .Select(r => r.Attributes!.ToDictionary(a => a.Key, a => a.Value))
            .ToList();

        Assert.All(attributes, a =>
        {
            var purl = Assert.IsType<string>(a[PackageSemanticConventions.AttributePackagePurl]);
            Assert.StartsWith("pkg:nuget/", purl, StringComparison.Ordinal);
            Assert.Contains('@', purl);

            Assert.Equal(
                PackageSemanticConventions.EventName,
                a[PackageSemanticConventions.AttributeEventName]);
            Assert.Equal(
                PackageSemanticConventions.PackageTypeNuGet,
                a[PackageSemanticConventions.AttributePackageType]);
            Assert.NotEmpty(Assert.IsType<string>(a[PackageSemanticConventions.AttributePackageName]));
            Assert.NotEmpty(Assert.IsType<string>(a[PackageSemanticConventions.AttributePackageVersion]));
            Assert.IsType<bool>(a[PackageSemanticConventions.AttributePackageLoaded]);
        });
    }

    [Fact]
    public void ReportedRecordsCarryTheirPackageChecksum()
    {
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);

        // Restored packages carry a hash in the manifest, so the records for this
        // project's own dependencies report one.
        var attributes = exported
            .Select(r => r.Attributes!.ToDictionary(a => a.Key, a => a.Value))
            .Where(a => a.ContainsKey(PackageSemanticConventions.AttributePackageChecksum))
            .ToList();

        Assert.NotEmpty(attributes);

        Assert.All(attributes, a =>
        {
            Assert.NotEmpty(Assert.IsType<string>(a[PackageSemanticConventions.AttributePackageChecksum]));
            Assert.Equal(
                "sha512",
                a[PackageSemanticConventions.AttributePackageChecksumAlgorithm]);
        });
    }

    [Fact]
    public void ReportIncludesAKnownDependencyOfTheTestProject()
    {
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);

        var purls = exported
            .SelectMany(r => r.Attributes!)
            .Where(a => a.Key == PackageSemanticConventions.AttributePackagePurl)
            .Select(a => (string)a.Value!)
            .ToList();

        // The test project references the OpenTelemetry SDK, so it must appear
        // in the inventory.
        Assert.Contains(purls, p => p.StartsWith("pkg:nuget/OpenTelemetry@", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportMarksLoadedPackagesAsLoaded()
    {
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);

        // The OpenTelemetry SDK is in use by this very test, so its assemblies
        // are loaded. A reported inventory where nothing is loaded would mean
        // the overlay is not working.
        var openTelemetry = exported
            .Select(r => r.Attributes!.ToDictionary(a => a.Key, a => a.Value))
            .Single(a => (string)a[PackageSemanticConventions.AttributePackageName]! == "OpenTelemetry");

        Assert.True((bool)openTelemetry[PackageSemanticConventions.AttributePackageLoaded]!);
    }

    [Fact]
    public void ReportIsEmittedOnlyOncePerProcess()
    {
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var first = DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);
        var second = DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);

        Assert.True(first > 0);
        Assert.Equal(0, second);
        Assert.Equal(first, exported.Count);
    }

    [Fact]
    public void MaxPackagesBoundsTheNumberOfRecords()
    {
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var count = DependencyInventoryReporter.Report(
            loggerFactory,
            new DependencyInventoryOptions { MaxPackages = 2 },
            TestAssembly);

        Assert.Equal(2, count);
        Assert.Equal(2, exported.Count);
    }

    [Fact]
    public void ReportingNothingDoesNotConsumeTheOneShotGuard()
    {
        // A caller that runs before the logging pipeline is ready, or that hits a
        // transient failure reading the manifest, reports nothing. That must not
        // consume the process's one chance, or the inventory is permanently
        // disabled and the caller cannot tell that apart from having already
        // reported it.
        //
        // A logger factory with no OpenTelemetry provider produces records that
        // reach no exporter, but the inventory itself still succeeds, so failure
        // is simulated instead by disposing the factory first: creating a logger
        // from it throws, which the reporter swallows.
        DependencyInventoryReporter.ResetForTesting();

        var disposed = LoggerFactory.Create(builder => { });
        disposed.Dispose();

        var failed = DependencyInventoryReporter.Report(disposed, options: null, assembly: TestAssembly);
        Assert.Equal(0, failed);

        // The guard was released, so a healthy factory still reports.
        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var succeeded = DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);

        Assert.True(succeeded > 0, "Expected the retry to report the inventory.");
        Assert.Equal(succeeded, exported.Count);

        // Having reported, the guard now holds.
        Assert.Equal(0, DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly));
    }

    [Fact]
    public async Task ConcurrentCallersReportTheInventoryOnlyOnce()
    {
        // The guard admits one caller at a time, so racing callers cannot emit the
        // inventory twice. Exactly one reports it and the rest return zero.
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        using var start = new ManualResetEventSlim(false);

        var callers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() =>
            {
                start.Wait(TimeSpan.FromSeconds(5));
                return DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);
            }))
            .ToArray();

        start.Set();

        var results = await Task.WhenAll(callers);

        var reported = Assert.Single(results, r => r > 0);
        Assert.Equal(reported, exported.Count);
    }

    [Fact]
    public void ExcludingUnloadedPackagesReportsFewerRecords()
    {
        DependencyInventoryReporter.ResetForTesting();

        var all = new List<LogRecord>();
        using var allFactory = CreateLoggerFactory(all);
        var withUnloaded = DependencyInventoryReporter.Report(
            allFactory,
            new DependencyInventoryOptions { IncludeUnloadedPackages = true },
            TestAssembly);

        DependencyInventoryReporter.ResetForTesting();

        var onlyLoaded = new List<LogRecord>();
        using var loadedFactory = CreateLoggerFactory(onlyLoaded);
        var withoutUnloaded = DependencyInventoryReporter.Report(
            loadedFactory,
            new DependencyInventoryOptions { IncludeUnloadedPackages = false },
            TestAssembly);

        Assert.True(withUnloaded > 0);
        Assert.True(withoutUnloaded > 0);
        Assert.True(
            withoutUnloaded <= withUnloaded,
            $"Excluding unloaded packages reported {withoutUnloaded}, more than the {withUnloaded} reported including them.");

        // Every record that survives the filter must be a loaded package.
        Assert.All(onlyLoaded, record =>
        {
            var isLoaded = record.Attributes!
                .First(a => a.Key == PackageSemanticConventions.AttributePackageLoaded).Value;
            Assert.True((bool)isLoaded!);
        });
    }

    [Fact]
    public void GivenAssemblyDeterminesTheInventory()
    {
        // Under a host that loads the application as a library, the entry
        // assembly is the host and its manifest describes the host's packages,
        // not the application's. Passing the assembly explicitly must resolve the
        // manifest next to that assembly instead.
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var count = DependencyInventoryReporter.Report(
            loggerFactory,
            options: null,
            assembly: typeof(DependencyInventoryReporterTests).Assembly);

        Assert.True(count > 0, "Expected the inventory of the given assembly to be reported.");

        var purls = exported
            .SelectMany(r => r.Attributes!)
            .Where(a => a.Key == PackageSemanticConventions.AttributePackagePurl)
            .Select(a => (string)a.Value!)
            .ToList();

        Assert.Contains(purls, p => p.StartsWith("pkg:nuget/OpenTelemetry@", StringComparison.Ordinal));
    }

    [Fact]
    public void DisabledOptionsReportNothing()
    {
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        var count = DependencyInventoryReporter.Report(
            loggerFactory,
            new DependencyInventoryOptions { Enabled = false },
            TestAssembly);

        Assert.Equal(0, count);
        Assert.Empty(exported);
    }

    [Fact]
    public void OptionsAreReadFromEnvironmentVariables()
    {
        // A deployment must be adjustable without rebuilding, so each option is
        // also settable by environment variable.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DependencyInventoryOptions.EnabledEnvVar] = "false",
                [DependencyInventoryOptions.MaxPackagesEnvVar] = "7",
                [DependencyInventoryOptions.IncludeUnloadedEnvVar] = "false",
            })
            .Build();

        var options = new DependencyInventoryOptions(configuration);

        Assert.False(options.Enabled);
        Assert.Equal(7, options.MaxPackages);
        Assert.False(options.IncludeUnloadedPackages);
    }

    [Fact]
    public void OptionsKeepDefaultsWhenEnvironmentVariablesAreAbsent()
    {
        var options = new DependencyInventoryOptions(
            new ConfigurationBuilder().Build());

        Assert.True(options.Enabled);
        Assert.Equal(DependencyInventoryOptions.DefaultMaxPackages, options.MaxPackages);
        Assert.True(options.IncludeUnloadedPackages);
    }

    [Fact]
    public void InvalidEnvironmentVariableValueKeepsTheDefault()
    {
        // A malformed value must not disable the inventory or throw; the default
        // is kept and a diagnostic is written.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DependencyInventoryOptions.MaxPackagesEnvVar] = "not-a-number",
            })
            .Build();

        var options = new DependencyInventoryOptions(configuration);

        Assert.Equal(DependencyInventoryOptions.DefaultMaxPackages, options.MaxPackages);
    }

    [Fact]
    public void ReportThrowsOnNullLoggerFactory()
    {
        Assert.Throws<ArgumentNullException>(
            () => DependencyInventoryReporter.Report(null!));
    }

    [Fact]
    public void MaxPackagesRejectsNonPositiveValues()
    {
        // A zero or negative cap would silently disable the inventory, so the
        // default is restored instead.
        Assert.Equal(
            DependencyInventoryOptions.DefaultMaxPackages,
            new DependencyInventoryOptions { MaxPackages = 0 }.MaxPackages);

        Assert.Equal(
            DependencyInventoryOptions.DefaultMaxPackages,
            new DependencyInventoryOptions { MaxPackages = -5 }.MaxPackages);
    }

    [Fact]
    public void RecordsCarryNoLoggingFrameworkInternals()
    {
        // "{OriginalFormat}" is an implementation detail of the logging
        // framework. Only some exporters strip it, so it must not be part of the
        // emitted state or it reaches the wire through the others.
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = CreateLoggerFactory(exported);

        DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);

        Assert.NotEmpty(exported);
        Assert.All(exported, record =>
            Assert.DoesNotContain("{OriginalFormat}", record.Attributes!.Select(a => a.Key)));
    }

    [Fact]
    public void RecordsHaveAReadableBody()
    {
        // The formatter's result is what an exporter reports as the log body. It
        // must describe the package, so that a record is legible without
        // reading its attributes.
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.AddInMemoryExporter(exported);
            }));

        DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);

        Assert.NotEmpty(exported);
        Assert.All(exported, record =>
        {
            Assert.NotNull(record.FormattedMessage);
            Assert.StartsWith("Dependency ", record.FormattedMessage, StringComparison.Ordinal);

            var name = record.Attributes!
                .First(a => a.Key == PackageSemanticConventions.AttributePackageName).Value;
            Assert.Contains((string)name!, record.FormattedMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AttributesArePresentWithoutIncludeScopes()
    {
        // Attributes are carried in the record's state rather than on a logging
        // scope, so they must survive with scope export left at its default of
        // disabled. Carrying them on a scope instead would make every attribute
        // silently absent for anyone who had not enabled IncludeScopes.
        DependencyInventoryReporter.ResetForTesting();

        var exported = new List<LogRecord>();
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(options => options.AddInMemoryExporter(exported)));

        DependencyInventoryReporter.Report(loggerFactory, options: null, assembly: TestAssembly);

        Assert.NotEmpty(exported);
        Assert.All(exported, record =>
        {
            var attributes = record.Attributes!.ToDictionary(a => a.Key, a => a.Value);
            Assert.True(attributes.ContainsKey(PackageSemanticConventions.AttributePackagePurl));
        });
    }

    private static ILoggerFactory CreateLoggerFactory(List<LogRecord> exported) =>
        LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
            options.AddInMemoryExporter(exported)));
}
