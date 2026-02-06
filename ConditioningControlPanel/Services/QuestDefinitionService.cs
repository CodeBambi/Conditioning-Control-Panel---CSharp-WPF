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
    /// Default sissy-themed month names used when server doesn't provide a season title
    /// </summary>
    private static readonly Dictionary<int, string> DefaultMonthNames = new()
    {
        { 1, "Jerk-it January" },
        { 2, "Fucked-up February" },
        { 3, "Mindfuck March" },
        { 4, "Anal April" },
        { 5, "Mesmerize May" },
        { 6, "Juicy June" },
        { 7, "Jizzly July" },
        { 8, "Ass-up August" },
        { 9, "Sissygasm September" },
        { 10, "Obey-tober" },
        { 11, "No-nut November" },
        { 12, "Dick-ember" }
    };

    /// <summary>
    /// Current season title (from server or default month name)
    /// </summary>
    public string SeasonTitle => _cache?.SeasonTitle
        ?? DefaultMonthNames.GetValueOrDefault(DateTime.Now.Month, DateTime.Now.ToString("MMMM"));

    public QuestDefinitionService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // Set up cache directories
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel");
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
                    ImageUrl = q["imageUrl"]?.ToString(),
                    ImagePath = GetFallbackImagePath(q["category"]?.ToString()),
                    IsSeasonal = q["seasonal"]?.Value<bool>() ?? false,
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
            "flash" => "pack://application:,,,/Resources/features/flash.png",
            "spiral" => "pack://application:,,,/Resources/features/spiral_overlay.png",
            "bubbles" => "pack://application:,,,/Resources/features/Bubble_pop.png",
            "pinkfilter" => "pack://application:,,,/Resources/features/Pink_filter.png",
            "video" => "pack://application:,,,/Resources/features/mandatory_videos.png",
            "session" => "pack://application:,,,/Resources/features/bambi takeover.png",
            "lockcard" => "pack://application:,,,/Resources/features/Phrase_Lock.png",
            "bubblecount" => "pack://application:,,,/Resources/features/Bubble_count.png",
            "streak" => "pack://application:,,,/Resources/achievements/daily_maintenance.png",
            _ => "pack://application:,,,/Resources/logo.png"
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
                var fileName = $"{quest.Id}_{GetFileNameFromUrl(quest.ImageUrl)}";
                var localPath = Path.Combine(_imageCacheDir, fileName);

                // Skip if already cached
                if (File.Exists(localPath))
                {
                    quest.CachedImagePath = localPath;
                    continue;
                }

                // Download the image
                var imageBytes = await _httpClient.GetByteArrayAsync(quest.ImageUrl);
                await File.WriteAllBytesAsync(localPath, imageBytes);
                quest.CachedImagePath = localPath;

                App.Logger?.Debug("Cached quest image: {QuestId} -> {Path}", quest.Id, localPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to cache quest image for {QuestId}: {Url}", quest.Id, quest.ImageUrl);
            }
        }
    }

    /// <summary>
    /// Extract filename from URL
    /// </summary>
    private string GetFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return Path.GetFileName(uri.LocalPath);
        }
        catch
        {
            return "image.png";
        }
    }

    /// <summary>
    /// Check if the cached definitions are stale
    /// </summary>
    private bool IsCacheStale()
    {
        if (_cache == null) return true;
        if (!_cache.FetchedAt.HasValue) return true;

        var age = DateTime.UtcNow - _cache.FetchedAt.Value;
        return age.TotalHours >= CacheExpiryHours;
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

            var fileName = $"{quest.Id}_{GetFileNameFromUrl(quest.ImageUrl)}";
            var localPath = Path.Combine(_imageCacheDir, fileName);

            if (File.Exists(localPath))
                quest.CachedImagePath = localPath;
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
