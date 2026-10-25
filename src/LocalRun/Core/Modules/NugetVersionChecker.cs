namespace LocalRun.Core.Modules;

using LocalRun.Logger;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Diagnostics;
using System.Text.Json;

public class NugetVersionChecker
{
    private static readonly ILogger<NugetVersionChecker> logger = Log.For<NugetVersionChecker>();

    private readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(() => new());

    public async Task<string?> GetLatestVersionAsync(string packageName, string? feedBase = null)
    {
        feedBase ??= "https://api.nuget.org/v3-flatcontainer/";
        string url = $"{feedBase.TrimEnd('/')}/{packageName.ToLowerInvariant()}/index.json";

        try
        {
            using var stream = await Http.Value.GetStreamAsync(url);
            var doc = await JsonDocument.ParseAsync(stream);

            if (!doc.RootElement.TryGetProperty("versions", out var versionsElem))
                return null;

            var versions = versionsElem
                .EnumerateArray()
                .Select(v => v.GetString())
                .Where(v => !string.IsNullOrEmpty(v))
                .Cast<string>()
                .ToList();

            if (versions.Count == 0)
                return null;

            // sort using NuGet-style version semantics
            var latest = versions
                .Select(v => (v, parsed: NuGetVersion.Parse(v)))
                .OrderByDescending(x => x.parsed)
                .First();

            return latest.v;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Unable to query NuGet feed for '{PackageId}': {Error}", packageName, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to parse NuGet versions for '{PackageId}': {Error}", packageName, ex.Message);
            return null;
        }
    }

    /// <param name="root">Folder whose NuGet.Config files apply (as for a restore from there); null for the user/machine ones only.</param>
    /// <param name="additionalSource">Queried too unless already a source, like <c>RestoreAdditionalProjectSources</c> (the local feed).</param>
    public async Task<NuGetVersion?> GetLatestVersionAcrossSourcesAsync(string packageId, string? root = null, string? additionalSource = null)
    {
        Stopwatch stopwatch = new();
        stopwatch.Start();
        var settings = Settings.LoadDefaultSettings(root);
        var sourceProvider = new PackageSourceProvider(settings);
        var sources = sourceProvider.LoadPackageSources()
            .Where(s => s.IsEnabled)
            .ToList();
        if (additionalSource != null && !sources.Any(s => IsSameFolder(s.Source, additionalSource)))
            sources.Add(new PackageSource(additionalSource));

        var nugetLogger = NullLogger.Instance;
        NuGetVersion? highest = null;

        foreach (var source in sources)
        {
            try
            {
                var repository = Repository.Factory.GetCoreV3(source.Source);
                var metadata = await repository.GetResourceAsync<PackageMetadataResource>();
                if (metadata == null)
                {
                    logger.LogDebug("Source '{Source}' does not provide package metadata, skipped", source.Source);
                    continue;
                }
                var versions = await metadata.GetMetadataAsync(
                    packageId,
                    includePrerelease: true,
                    includeUnlisted: false,
                    sourceCacheContext: new SourceCacheContext(),
                    log: nugetLogger,
                    token: CancellationToken.None);

                var latestVer = versions
                    .Select(m => m.Identity.Version)
                    .OrderByDescending(v => v)
                    .FirstOrDefault();

                if (latestVer != null && (highest == null || latestVer > highest))
                    highest = latestVer;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to query source '{Source}': {Error}", source.Source, ex.Message);
            }
        }
        stopwatch.Stop();
        logger.LogDebug("Fetched latest version across sources for {PackageId} in {ElapsedMs}ms", packageId, stopwatch.ElapsedMilliseconds);
        return highest;
    }

    private static bool IsSameFolder(string source, string folder)
    {
        if (!Path.IsPathFullyQualified(source))
            return false;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase);
    }

    private static NuGetVersion? GetLatestFromLocalFolder(string path, string packageId)
    {
        var pattern = Path.Combine(path, packageId.ToLowerInvariant() + ".*.nupkg");
        var files = Directory.GetFiles(path, $"{packageId.ToLowerInvariant()}*.nupkg", SearchOption.AllDirectories);
        if (files.Length == 0)
            return null;

        var versions = new List<NuGetVersion>();
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // e.g. mylib.1.2.3-alpha
            var parts = name.Split('.');
            var maybeVersion = name.Substring(packageId.Length + 1);
            if (NuGetVersion.TryParse(maybeVersion, out var v))
                versions.Add(v);
        }

        return versions.Count > 0 ? versions.Max() : null;
    }

    public async Task<bool> IsNewerThanRemoteAsync(string packageName, string localVersion, string? feedBase = null)
    {
        var remote = await GetLatestVersionAsync(packageName, feedBase);
        if (remote == null)
        {
            logger.LogInformation("[ℹ] No remote version found for {PackageId} — assuming local version is newer.", packageName);
            return true;
        }

        var localV = NuGetVersion.Parse(localVersion);
        var remoteV = NuGetVersion.Parse(remote);

        return localV > remoteV;
    }
}
