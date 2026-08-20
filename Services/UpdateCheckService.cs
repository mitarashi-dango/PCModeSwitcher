using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public interface IUpdateCheckService
{
    Task<OperationResult<AppUpdateInfo>> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default);
}

internal sealed class UpdateCheckService : IUpdateCheckService, IDisposable
{
    internal const string LatestReleaseApiUrl =
        "https://api.github.com/repos/mitarashi-dango/PCModeSwitcher/releases/latest";
    private const string ReleasePathPrefix = "/mitarashi-dango/PCModeSwitcher/releases/tag/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public UpdateCheckService(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<OperationResult<AppUpdateInfo>> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(
                "PCModeSwitcher",
                NormalizeVersion(currentVersion).ToString(3)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var release = await JsonSerializer.DeserializeAsync<LatestRelease>(
                stream,
                cancellationToken: timeout.Token);
            if (release is null ||
                release.Draft ||
                release.Prerelease ||
                !TryParseRelease(release.TagName, release.HtmlUrl, out var latestVersion, out var releaseUri))
            {
                return OperationResult<AppUpdateInfo>.Failure(
                    LocalizationService.Get("Update.CheckFailed"),
                    "The latest release response was incomplete or invalid.");
            }

            var normalizedLatest = NormalizeVersion(latestVersion);
            return OperationResult<AppUpdateInfo>.Success(new AppUpdateInfo(
                normalizedLatest,
                $"v{normalizedLatest.ToString(3)}",
                releaseUri,
                normalizedLatest > NormalizeVersion(currentVersion)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationResult<AppUpdateInfo>.Failure(
                LocalizationService.Get("Update.CheckFailed"),
                "The update request timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            return OperationResult<AppUpdateInfo>.Failure(
                LocalizationService.Get("Update.CheckFailed"),
                ex.ToString());
        }
    }

    internal static bool TryParseRelease(
        string? tagName,
        string? htmlUrl,
        out Version version,
        out Uri releaseUri)
    {
        version = new Version();
        releaseUri = null!;
        var versionText = tagName?.Trim().TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(versionText) ||
            !Version.TryParse(versionText, out var parsedVersion) ||
            parsedVersion.Major < 0 ||
            parsedVersion.Minor < 0 ||
            parsedVersion.Build < 0 ||
            !Uri.TryCreate(htmlUrl, UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps ||
            !candidate.IdnHost.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !candidate.IsDefaultPort ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment) ||
            !candidate.AbsolutePath.Equals(
                ReleasePathPrefix + tagName!.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        version = parsedVersion;
        releaseUri = candidate;
        return true;
    }

    internal static Version NormalizeVersion(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed class LatestRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }
    }
}
