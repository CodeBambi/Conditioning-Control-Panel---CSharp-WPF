using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service for fetching and caching quest definitions from the server.
/// Supports remote quest images hosted on CDN (Bunny).
/// </summary>
public class QuestDefinitionService : IDisposable
{
    private const string ServerBaseUrl = "https://codebambi-proxy.vercel.app";
    private const string QuestDefinitionsEndpoint = "/quests/definitions";
    private const string CacheFileName = "quest_definitions_cache.json";
    private const int CacheExpiryHours = 24;

    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;
    private readonly string _imageCacheDir;
    private readonly string _cacheFilePath;

    private QuestDefinitionsCache? _cache;
    private bool _isInitialized;

    /// <summary>
    /// Event fired when quest definitions are updated from server
    /// </summary>
    public event Action? QuestDefinitionsUpdated;

    /// <summary>
    /// Current version of quest definitions
    /// </summary>
    public int Version => _cache?.Version ?? 0;

    /// <summary>
    /// When the definitions were last updated from server
    /// </summary>
    public DateTime? LastUpdated => _cache?.FetchedAt;

    /// <summary>
    /// The permanent Quests header title. Monthly seasons ended with The Descent (v6.9.0):
    /// progress never resets and only the monthly board rotates, so the header no longer
    /// names a month.
    /// </summary>
    internal const string PermanentTitle = "Deeper Every Month";

    /// <summary>
    /// A month name as a whole word ("Airhead August", "No-nut November"), or one of the two
    /// punned months. "May" and "March" only count as the LAST word, so an event title such as
    /// "You May Obey" or "March of the Bimbos" is not mistaken for a dead season.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex DeadSeasonPattern = new(
        @"\b(january|february|april|june|july|august|september|october|november|december)\b"
        + @"|\b(may|march)\s*$|obey-?tober|dick-?ember",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>True for a title in the old monthly-season style, which must never be shown again.</summary>
    internal static bool IsDeadSeasonTitle(string title) => DeadSeasonPattern.IsMatch(title);

    /// <summary>
    /// The server's title wins only when it is set, was fetched in the CURRENT UTC month (a failed
    /// month-rollover refetch leaves the old cache loaded, #480) and is not a dead season name. The
    /// server still says "Airhead August", so without that last check every client would show it.
    /// Anything else resolves to <see cref="PermanentTitle"/>, which leaves the server free to
    /// override the header for an event later.
    /// </summary>
    internal static string ResolveSeasonTitle(string? serverTitle, DateTime? fetchedAtUtc, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(serverTitle)) return PermanentTitle;
        if (fetchedAtUtc is not { } fetched || fetched.Year != utcNow.Year || fetched.Month != utcNow.Month)
            return PermanentTitle;
        return IsDeadSeasonTitle(serverTitle) ? PermanentTitle : serverTitle.Trim();
    }

    /// <summary>Current Quests header title. See <see cref="ResolveSeasonTitle"/>.</summary>
    public string SeasonTitle => ResolveSeasonTitle(_cache?.SeasonTitle, _cache?.FetchedAt, DateTime.UtcNow);

    public QuestDefinitionService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");

        // Set up cache directories
        var appDataPath = App.UserDataPath;
        _cacheDir = appDataPath;
        _imageCacheDir = Path.Combine(appDataPath, "quest-images");
        _cacheFilePath = Path.Combine(_cacheDir, CacheFileName);

        // Ensure directories exist
        Directory.CreateDirectory(_cacheDir);
        Directory.CreateDirectory(_imageCacheDir);
    }

    /// <summary>
    /// Initialize the service - loads cache and optionally fetches from server
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // Load cached definitions first (for fast startup)
        LoadCache();

        // Check if cache is stale and fetch from server
        if (IsCacheStale())
        {
            await RefreshFromServerAsync();
        }

        _isInitialized = true;
    }

    /// <summary>
    /// Get all active daily quests (including seasonal)
    /// </summary>
    public List<QuestDefinition> GetDailyQuests()
    {
        var quests = new List<QuestDefinition>();

        if (_cache?.Daily != null)
            quests.AddRange(_cache.Daily);

        if (_cache?.Seasonal != null)
            quests.AddRange(_cache.Seasonal.Where(q => q.Type == QuestType.Daily));

        // Fall back to embedded if no remote quests
        if (quests.Count == 0)
            return QuestDefinition.DailyQuests.ToList();

        return quests;
    }

    /// <summary>
    /// Get all active weekly quests (including seasonal)
    /// </summary>
    public List<QuestDefinition> GetWeeklyQuests()
    {
        var quests = new List<QuestDefinition>();

        if (_cache?.Weekly != null)
            quests.AddRange(_cache.Weekly);

        if (_cache?.Seasonal != null)
            quests.AddRange(_cache.Seasonal.Where(q => q.Type == QuestType.Weekly));

        // Fall back to embedded if no remote quests
        if (quests.Count == 0)
            return QuestDefinition.WeeklyQuests.ToList();

        return quests;
    }

    /// <summary>
    /// Get only seasonal quests (for display in special section)
    /// </summary>
    public List<QuestDefinition> GetSeasonalQuests()
    {
        return _cache?.Seasonal?.ToList() ?? new List<QuestDefinition>();
    }

    /// <summary>
    /// Check if there are any active seasonal quests
    /// </summary>
    public bool HasSeasonalQuests => _cache?.Seasonal?.Count > 0;

    /// <summary>
    /// Force refresh quest definitions from server
    /// </summary>
    public async Task RefreshFromServerAsync()
    {
        try
        {
            App.Logger?.Information("Fetching quest definitions from server...");

            var response = await _httpClient.GetAsync($"{ServerBaseUrl}{QuestDefinitionsEndpoint}");
            if (!response.IsSuccessStatusCode)
            {
                App.Logger?.Warning("Failed to fetch quest definitions: {StatusCode}", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var serverResponse = JsonConvert.DeserializeObject<ServerQuestResponse>(json);

            if (serverResponse?.Success != true || serverResponse.Quests == null)
            {
                App.Logger?.Warning("Invalid quest definitions response from server");
                return;
            }

            // Parse the server response into QuestDefinitions
            var newCache = new QuestDefinitionsCache
            {
                Version = serverResponse.Version,
                FetchedAt = DateTime.UtcNow,
                SeasonTitle = serverResponse.SeasonTitle,
                Daily = ParseQuests(serverResponse.Quests.Daily),
                Weekly = ParseQuests(serverResponse.Quests.Weekly),
                Seasonal = ParseQuests(serverResponse.Quests.Seasonal)
            };

            // Download and cache images for all quests
            var allQuests = newCache.Daily
                .Concat(newCache.Weekly)
                .Concat(newCache.Seasonal);

            await CacheQuestImagesAsync(allQuests);

            // Save to cache
            _cache = newCache;
            SaveCache();

            App.Logger?.Information("Quest definitions updated: v{Version}, {Daily} daily, {Weekly} weekly, {Seasonal} seasonal",
                newCache.Version, newCache.Daily.Count, newCache.Weekly.Count, newCache.Seasonal.Count);

            QuestDefinitionsUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error fetching quest definitions from server");
        }
    }

    /// <summary>
    /// Parse quest definitions from server JSON
    /// </summary>
    private List<QuestDefinition> ParseQuests(List<JObject>? questsJson)
    {
        if (questsJson == null) return new List<QuestDefinition>();

        var quests = new List<QuestDefinition>();
        foreach (var q in questsJson)
        {
            try
            {
                var quest = new QuestDefinition
                {
                    Id = q["id"]?.ToString() ?? "",
                    Name = q["name"]?.ToString() ?? "",
                    Description = q["description"]?.ToString() ?? "",
                    Type = QuestDefinition.ParseType(q["type"]?.ToString() ?? "daily"),
                    Category = QuestDefinition.ParseCategory(q["category"]?.ToString() ?? "combined"),
                    TargetValue = q["targetValue"]?.Value<int>() ?? 0,
                    XPReward = q["xpReward"]?.Value<int>() ?? 0,
                    Icon = q["icon"]?.ToString() ?? "⭐",
                    // Quest art is bundled in-app (no CDN). Prefer the bespoke per-id
                    // PNG when this build carries it; otherwise fall back to a shared
                    // category icon. ImageUrl is intentionally left null so the client
                    // never fetches/caches from a CDN even if the server still sends one.
                    ImageUrl = null,
                    ImagePath = QuestDefinition.HasBundledArt(q["id"]?.ToString())
                        ? QuestDefinition.BundledArtPath(q["id"]!.ToString())
                        : GetFallbackImagePath(q["category"]?.ToString()),
                    RequiresPremium = (bool?)q["requiresPremium"] ?? false,
                    // ccp-bugs#1151: lets the channel publish a quest that needs a camera or a
                    // microphone without a client update. Read by QuestHardwareGate.
                    RequiresHardware = q["requiresHardware"]?.ToString(),
                    IsSeasonal = (bool?)q["seasonal"] ?? false,
                    ActiveFrom = q["activeFrom"]?.ToString(),
                    ActiveUntil = q["activeUntil"]?.ToString()
                };

                if (!string.IsNullOrEmpty(quest.Id))
                    quests.Add(quest);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to parse quest: {Quest}", q.ToString());
            }
        }

        return quests;
    }

    /// <summary>
    /// Get fallback embedded image path based on category
    /// </summary>
    private string GetFallbackImagePath(string? category)
    {
        return category?.ToLowerInvariant() switch
        {
            "flash" => ModResourceResolver.ResolveUri("features/flash.png"),
            "spiral" => ModResourceResolver.ResolveUri("features/spiral_overlay.png"),
            "bubbles" => ModResourceResolver.ResolveUri("features/Bubble_pop.png"),
            "pinkfilter" => ModResourceResolver.ResolveUri("features/Pink_filter.png"),
            "video" => ModResourceResolver.ResolveUri("features/mandatory_videos.png"),
            "session" => ModResourceResolver.ResolveUri("features/bambi takeover.png"),
            "lockcard" => ModResourceResolver.ResolveUri("features/Phrase_Lock.png"),
            "bubblecount" => ModResourceResolver.ResolveUri("features/Bubble_count.png"),
            "streak" => ModResourceResolver.ResolveUri("achievements/daily_maintenance.png"),
            _ => ModResourceResolver.ResolveUri("logo.png")
        };
    }

    /// <summary>
    /// Download and cache quest images from CDN
    /// </summary>
    private async Task CacheQuestImagesAsync(IEnumerable<QuestDefinition> quests)
    {
        foreach (var quest in quests)
        {
            if (string.IsNullOrEmpty(quest.ImageUrl))
                continue;

            try
            {
                // The extension is NOT known until the bytes are here: a CDN image URL is
                // routinely a bare hash. So the cache name is matched on its stem and the
                // extension is derived from the response (see MediaTypeSniffer).
                var stem = $"{quest.Id}_{GetFileStemFromUrl(quest.ImageUrl)}";

                // Skip if already cached, whatever extension that cached copy ended up with.
                var existing = Directory.Exists(_imageCacheDir)
                    ? Directory.GetFiles(_imageCacheDir, stem + ".*")
                    : Array.Empty<string>();
                if (existing.Length > 0)
                {
                    quest.CachedImagePath = existing[0];
                    continue;
                }

                // Download the image
                using var response = await _httpClient.GetAsync(quest.ImageUrl);
                response.EnsureSuccessStatusCode();
                var imageBytes = await response.Content.ReadAsByteArrayAsync();

                var ext = Helpers.MediaTypeSniffer.ResolveExtension(
                    response.Content.Headers.ContentType?.ToString(),
                    imageBytes,
                    quest.ImageUrl,
                    Helpers.MediaTypeSniffer.DefaultImageExtension,
                    "QuestDefinitionService");

                var localPath = Path.Combine(_imageCacheDir, stem + ext);
                await File.WriteAllBytesAsync(localPath, imageBytes);
                quest.CachedImagePath = localPath;

                App.Logger?.Debug("Cached quest image: {QuestId} -> {Path}", quest.Id, localPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to cache quest image for {QuestId} from {Host}", quest.Id, Logging.UrlLog.Host(quest.ImageUrl));
            }
        }
    }

    /// <summary>
    /// The URL's last path segment WITHOUT its extension — the cache filename's stem. The
    /// extension is never taken from here: a CDN image URL is often a bare hash, and a name
    /// built from one would be extensionless (which no image loader in the app will touch).
    /// See <see cref="Helpers.MediaTypeSniffer"/>.
    /// </summary>
    private string GetFileStemFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var stem = Path.GetFileNameWithoutExtension(uri.LocalPath);
            // Whatever survives must be a legal, non-empty filename fragment.
            foreach (var c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
            stem = stem.Trim();
            if (stem.Length > 64) stem = stem[..64];
            return stem.Length > 0 ? stem : "image";
        }
        catch
        {
            return "image";
        }
    }

    /// <summary>
    /// Check if the cached definitions are stale
    /// </summary>
    private bool IsCacheStale()
    {
        if (_cache == null) return true;
        if (!_cache.FetchedAt.HasValue) return true;

        var fetched = _cache.FetchedAt.Value;
        var nowUtc = DateTime.UtcNow;

        // Month rolled over since the cache was written — the season title (and
        // seasonal quests) may have changed, so refetch promptly rather than
        // waiting out the 24h TTL. Seasons rotate on the 1st of each month UTC.
        if (fetched.Year != nowUtc.Year || fetched.Month != nowUtc.Month) return true;

        return (nowUtc - fetched).TotalHours >= CacheExpiryHours;
    }

    /// <summary>
    /// Load cached definitions from disk
    /// </summary>
    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
                return;

            var json = File.ReadAllText(_cacheFilePath);
            _cache = JsonConvert.DeserializeObject<QuestDefinitionsCache>(json);

            // Restore cached image paths
            if (_cache != null)
            {
                RestoreCachedImagePaths(_cache.Daily);
                RestoreCachedImagePaths(_cache.Weekly);
                RestoreCachedImagePaths(_cache.Seasonal);
            }

            App.Logger?.Debug("Loaded quest definitions cache: v{Version}", _cache?.Version ?? 0);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Failed to load quest definitions cache");
            _cache = null;
        }
    }

    /// <summary>
    /// Restore cached image paths for quests
    /// </summary>
    private void RestoreCachedImagePaths(List<QuestDefinition>? quests)
    {
        if (quests == null) return;

        foreach (var quest in quests)
        {
            if (string.IsNullOrEmpty(quest.ImageUrl))
                continue;

            // Matched on the stem: the cached copy's extension was decided by the response
            // when it was downloaded, not by the URL, so it is not knowable from here.
            var stem = $"{quest.Id}_{GetFileStemFromUrl(quest.ImageUrl)}";
            if (!Directory.Exists(_imageCacheDir)) continue;

            var matches = Directory.GetFiles(_imageCacheDir, stem + ".*");
            if (matches.Length > 0)
                quest.CachedImagePath = matches[0];
        }
    }

    /// <summary>
    /// Save current cache to disk
    /// </summary>
    private void SaveCache()
    {
        try
        {
            if (_cache == null) return;

            var json = JsonConvert.SerializeObject(_cache, Formatting.Indented);
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Failed to save quest definitions cache");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>
    /// Internal cache structure
    /// </summary>
    private class QuestDefinitionsCache
    {
        public int Version { get; set; }
        public DateTime? FetchedAt { get; set; }
        public string? SeasonTitle { get; set; }
        public List<QuestDefinition> Daily { get; set; } = new();
        public List<QuestDefinition> Weekly { get; set; } = new();
        public List<QuestDefinition> Seasonal { get; set; } = new();
    }

    /// <summary>
    /// Server response structure
    /// </summary>
    private class ServerQuestResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("updatedAt")]
        public string? UpdatedAt { get; set; }

        [JsonProperty("seasonTitle")]
        public string? SeasonTitle { get; set; }

        [JsonProperty("quests")]
        public ServerQuests? Quests { get; set; }
    }

    private class ServerQuests
    {
        [JsonProperty("daily")]
        public List<JObject>? Daily { get; set; }

        [JsonProperty("weekly")]
        public List<JObject>? Weekly { get; set; }

        [JsonProperty("seasonal")]
        public List<JObject>? Seasonal { get; set; }
    }
}
