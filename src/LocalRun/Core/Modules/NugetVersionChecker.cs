namespace LocalRun.Core.Modules;

using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Collections.Concurrent;

/// <summary>
/// Finds the highest version of packages across NuGet sources. One instance serves a whole <c>build</c>: each source is
/// opened once and shared by the lookups, which may run concurrently.
/// </summary>
public sealed class NugetVersionChecker : IDisposable
{
    private static readonly ILogger<NugetVersionChecker> logger = Log.For<NugetVersionChecker>();

    private readonly SourceCacheContext cacheContext = new();

    private readonly ConcurrentDictionary<string, Lazy<Task<PackageMetadataResource?>>> metadataPerSource = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, List<PackageSource>> sourcesPerFolder = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sources that failed once: they are not reported again for every package.</summary>
    private readonly ConcurrentDictionary<string, bool> failedSources = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="root">Folder whose NuGet.Config files apply (as for a restore from there); null for the user/machine ones only.</param>
    /// <param name="additionalSource">Queried too unless already a source, like <c>RestoreAdditionalProjectSources</c> (the local feed).</param>
    public async Task<NuGetVersion?> GetLatestVersionAcrossSourcesAsync(string packageId, string? root = null, string? additionalSource = null)
    {
        var sources = new List<PackageSource>(sourcesPerFolder.GetOrAdd(root ?? string.Empty, _ => EnabledSources(root)));
        if (additionalSource != null && !sources.Any(s => IsSameFolder(s.Source, additionalSource)))
            sources.Add(new PackageSource(additionalSource));

        var latestPerSource = await Task.WhenAll(sources.Select(source => GetLatestVersionAsync(source, packageId)));
        return latestPerSource.Max();
    }

    private static List<PackageSource> EnabledSources(string? root)
    {
        var settings = Settings.LoadDefaultSettings(root);
        return [.. new PackageSourceProvider(settings).LoadPackageSources().Where(s => s.IsEnabled)];
    }

    private async Task<NuGetVersion?> GetLatestVersionAsync(PackageSource source, string packageId)
    {
        try
        {
            var metadata = await metadataPerSource.GetOrAdd(source.Source,
                _ => new Lazy<Task<PackageMetadataResource?>>(() => OpenAsync(source))).Value;
            if (metadata == null)
                return null;
            var versions = await metadata.GetMetadataAsync(
                packageId,
                includePrerelease: true,
                includeUnlisted: false,
                sourceCacheContext: cacheContext,
                log: NullLogger.Instance,
                token: CancellationToken.None);
            return versions.Select(m => m.Identity.Version).Max();
        }
        catch (Exception ex)
        {
            if (failedSources.TryAdd(source.Source, true))
                logger.LogWarning("Failed to query source '{Source}': {Error}", source.Source, ex.Message);
            else
                logger.LogDebug("Failed to query source '{Source}' for {PackageId}: {Error}", source.Source, packageId, ex.Message);
            return null;
        }
    }

    private static async Task<PackageMetadataResource?> OpenAsync(PackageSource source)
    {
        // The PackageSource, not its URL: it carries the credentials of NuGet.Config (packageSourceCredentials)
        var metadata = await Repository.Factory.GetCoreV3(source).GetResourceAsync<PackageMetadataResource>();
        if (metadata == null)
            logger.LogDebug("Source '{Source}' does not provide package metadata, skipped", source.Source);
        return metadata;
    }

    private static bool IsSameFolder(string source, string folder)
    {
        if (!Path.IsPathFullyQualified(source))
            return false;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => cacheContext.Dispose();
}
