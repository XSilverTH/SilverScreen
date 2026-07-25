using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YouTubeAccountProfileService : IAccountProfileService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YouTubeAccountProfileService>();
    private readonly Lock _cacheGate = new();
    private readonly HttpClient _httpClient;
    private readonly string _profileCachePath;
    private readonly ISessionService _sessionService;
    private AccountProfile? _cachedProfile;

    public YouTubeAccountProfileService(HttpClient httpClient, ISessionService sessionService,
        string? profileCachePath = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _profileCachePath = profileCachePath ?? GetDefaultProfileCachePath();
        _cachedProfile = LoadCachedProfile();
        _sessionService.SessionChanged += OnSessionChanged;
    }

    public Func<long> TimeSource { get; init; } = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public AccountProfile? GetCachedProfile()
    {
        if (_sessionService.GetManualSessionCookies() is null)
            return null;

        lock (_cacheGate)
        {
            return _cachedProfile;
        }
    }

    public async Task<AccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        var sessionCookies = _sessionService.GetManualSessionCookies();
        if (sessionCookies is null || string.IsNullOrWhiteSpace(sessionCookies.Content))
            return null;

        var credentials = YouTubeCredentials.ParseNetscape(sessionCookies.Content);
        if (credentials is null)
            return null;

        var configuration = await GetBootstrapConfigurationAsync(credentials, cancellationToken).ConfigureAwait(false);
        if (configuration is null)
            return null;

        var payload = new BrowseRequestPayload
        {
            Context = new BrowseRequestContext
            {
                Client = new BrowseRequestClientContext
                {
                    ClientName = "WEB",
                    ClientVersion = configuration.ClientVersion,
                    OriginalUrl = YouTubeHomeClientOptions.Referer,
                    Hl = "en",
                    Gl = "US",
                    VisitorData = configuration.VisitorData
                },
                User = new BrowseRequestUserContext { LockedSafetyMode = false }
            }
        };
        var content = JsonSerializer.Serialize(payload, YouTubeRequestJsonContext.Default.BrowseRequestPayload);
        var requestUri = $"https://www.youtube.com/youtubei/v1/account/account_menu?key={Uri.EscapeDataString(configuration.ApiKey)}&prettyPrint=false";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        AddAuthenticatedHeaders(request, credentials, configuration);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var profile = FindAccountProfile(document.RootElement);
        if (profile is not null && IsCurrentSession(sessionCookies))
            CacheProfile(profile);

        return profile;
    }

    public void Dispose()
    {
        _sessionService.SessionChanged -= OnSessionChanged;
        _httpClient.Dispose();
    }

    private void OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        lock (_cacheGate)
        {
            _cachedProfile = null;
        }

        try
        {
            File.Delete(_profileCachePath);
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Failed to clear cached YouTube account profile");
        }
    }

    private bool IsCurrentSession(ManualSessionCookies sessionCookies)
    {
        return string.Equals(_sessionService.GetManualSessionCookies()?.Content, sessionCookies.Content,
            StringComparison.Ordinal);
    }

    private void CacheProfile(AccountProfile profile)
    {
        lock (_cacheGate)
        {
            _cachedProfile = profile;
        }

        var directory = Path.GetDirectoryName(_profileCachePath);
        var temporaryPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(),
            $".{Path.GetFileName(_profileCachePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, profile, YouTubeRequestJsonContext.Default.AccountProfile);
                stream.Flush(true);
            }

            File.Move(temporaryPath, _profileCachePath, true);
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Failed to cache YouTube account profile");
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "Failed to remove temporary YouTube account profile cache");
            }
        }
    }

    private AccountProfile? LoadCachedProfile()
    {
        try
        {
            if (!File.Exists(_profileCachePath))
                return null;

            return JsonSerializer.Deserialize(File.ReadAllText(_profileCachePath),
                YouTubeRequestJsonContext.Default.AccountProfile);
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Failed to load cached YouTube account profile");
            return null;
        }
    }

    private static string GetDefaultProfileCachePath()
    {
        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cacheHome))
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            cacheHome = string.IsNullOrWhiteSpace(userHome)
                ? Path.GetTempPath()
                : Path.Combine(userHome, ".cache");
        }

        return Path.Combine(cacheHome, "SilverScreen", "account-profile.json");
    }

    private async Task<YouTubeBootstrapConfig?> GetBootstrapConfigurationAsync(YouTubeCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, YouTubeHomeClientOptions.Referer);
        request.Headers.UserAgent.ParseAdd(YouTubeHomeClientOptions.UserAgent);
        request.Headers.Add("Origin", YouTubeHomeClientOptions.Origin);
        request.Headers.Add("Cookie", credentials.CookieHeader);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return YouTubeConfigBootstrap.Extract(html);
    }

    private void AddAuthenticatedHeaders(HttpRequestMessage request, YouTubeCredentials credentials,
        YouTubeBootstrapConfig configuration)
    {
        request.Headers.UserAgent.ParseAdd(YouTubeHomeClientOptions.UserAgent);
        request.Headers.Add("Origin", YouTubeHomeClientOptions.Origin);
        request.Headers.Add("Referer", YouTubeHomeClientOptions.Referer);
        request.Headers.Add("X-Origin", YouTubeHomeClientOptions.Origin);
        request.Headers.Add("Cookie", credentials.CookieHeader);
        request.Headers.Add("X-Youtube-Client-Name", "1");
        request.Headers.Add("X-Youtube-Client-Version", configuration.ClientVersion);
        if (!string.IsNullOrEmpty(configuration.VisitorData))
            request.Headers.Add("X-Goog-Visitor-Id", configuration.VisitorData);

        var timestamp = TimeSource();
        request.Headers.Authorization = new AuthenticationHeaderValue("SAPISIDHASH",
            $"{timestamp}_{credentials.GenerateSapisidHash(timestamp)}");
    }

    private static AccountProfile? FindAccountProfile(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var directProfile = ParseAccountItem(element);
            if (directProfile is not null)
                return directProfile;

            if (element.TryGetProperty("accountItem", out var accountItem))
            {
                var profile = ParseAccountItem(accountItem);
                if (profile is not null)
                    return profile;
            }

            foreach (var property in element.EnumerateObject())
            {
                var profile = FindAccountProfile(property.Value);
                if (profile is not null)
                    return profile;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var profile = FindAccountProfile(item);
                if (profile is not null)
                    return profile;
            }
        }

        return null;
    }

    private static AccountProfile? ParseAccountItem(JsonElement accountItem)
    {
        var displayName = ExtractText(accountItem, "accountName");
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        return new AccountProfile(displayName, ExtractAvatarUrl(accountItem));
    }

    private static string? ExtractText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        if (value.TryGetProperty("simpleText", out var simpleText) && simpleText.ValueKind == JsonValueKind.String)
            return simpleText.GetString();

        if (!value.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            return null;

        var text = new StringBuilder();
        foreach (var run in runs.EnumerateArray())
        {
            if (run.TryGetProperty("text", out var runText) && runText.ValueKind == JsonValueKind.String)
                text.Append(runText.GetString());
        }

        return text.Length == 0 ? null : text.ToString();
    }


    private static string? ExtractAvatarUrl(JsonElement element)
    {
        if (!element.TryGetProperty("avatar", out var avatar) &&
            !element.TryGetProperty("accountPhoto", out avatar))
            return null;

        if (!avatar.TryGetProperty("thumbnails", out var thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Array)
            return null;

        string? url = null;
        foreach (var thumbnail in thumbnails.EnumerateArray())
        {
            if (thumbnail.TryGetProperty("url", out var candidate) && candidate.ValueKind == JsonValueKind.String)
                url = candidate.GetString();
        }

        return url;
    }
}
