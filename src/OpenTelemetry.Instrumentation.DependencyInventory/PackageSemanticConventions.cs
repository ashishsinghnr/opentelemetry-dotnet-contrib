// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Instrumentation.DependencyInventory;

/// <summary>
/// Attribute names for dependency records.
/// </summary>
/// <remarks>
/// OpenTelemetry does not define a convention for an application's dependency
/// inventory. The <c>artifact.*</c> registry group is not used, because it
/// describes objects "intended for distribution" by the application itself,
/// whereas these packages are consumed by it. A distinct namespace keeps the
/// two relationships separable and avoids colliding with a future convention.
/// </remarks>
internal static class PackageSemanticConventions
{
    public const string EventName = "package.dependency";

    public const string AttributeEventName = "event.name";
    public const string AttributePackageLoaded = "package.loaded";
    public const string AttributePackageName = "package.name";
    public const string AttributePackagePurl = "package.purl";
    public const string AttributePackageType = "package.type";
    public const string AttributePackageVersion = "package.version";

    public const string PackageTypeNuGet = "nuget";
}
