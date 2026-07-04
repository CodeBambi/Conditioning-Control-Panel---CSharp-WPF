using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Update;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Cross-platform quest definition provider. Loads cached definitions, refreshes from
/// the server when stale, and resolves quest art to bundled PNGs (no CDN fetches).
/// </summary>
public sealed class QuestDefinitionService : IQuestDefinitionService, IDisposable
{
    private const string ServerBaseUrl = "https://codebambi-proxy.vercel.app";
    private const string QuestDefinitionsEndpoint = "/quests/definitions";
    private const string CacheFileName = "quest_definitions_cache.json";
    private const int CacheExpiryHours = 24;

    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;
    private readonly ILogger<QuestDefinitionService>? _logger;

    private QuestDefinitionsCache? _cache;
    private bool _isInitialized;

    /// <inheritdoc />
    public event Action? QuestDefinitionsUpdated;

    /// <inheritdoc />
    public int Version => _cache?.Version ?? 0;

    /// <inheritdoc />
    public DateTime? LastUpdated => _cache?.FetchedAt;

    private static readonly Dictionary<int, string> DefaultMonthNames = new()
    {
        { 1, "Jerk-it January" },
        { 2, "Fucked-up February" },
        { 3, "Mindless March" },
        { 4, "Airhead April" },
        { 5, "Mooing May" },
        { 6, "Juicy June" },
        { 7, "Jizzly July" },
        { 8, "Ass-up August" },
        { 9, "Sissygasm September" },
        { 10, "Obey-tober" },
        { 11, "No-nut November" },
        { 12, "Dick-ember" }
    };

    /// <inheritdoc />
    /// <remarks>The cached server title is only trusted while it was fetched in the CURRENT
    /// UTC month: the month-rollover refetch fires on startup, but when it fails — offline
    /// launch, server hiccup — the old cache stays loaded and the Quests header kept showing
    /// last month's season ("Juicy June" in July, WPF #480). Falling back to the local month
    /// name self-corrects on the 1st even with no network; the next successful refetch
    /// restores the server title. Mirrors WPF QuestDefinitionService.SeasonTitle (6.2.8).</remarks>
    public string SeasonTitle =>
        (_cache?.FetchedAt is { } fetched
            && fetched.Year == DateTime.UtcNow.Year
            && fetched.Month == DateTime.UtcNow.Month
            ? _cache?.SeasonTitle : null)
        ?? DefaultMonthNames.GetValueOrDefault(DateTime.Now.Month, DateTime.Now.ToString("MMMM"));

    /// <inheritdoc />
    public bool HasSeasonalQuests => _cache?.Seasonal?.Count > 0;

    public QuestDefinitionService(
        IAppEnvironment appEnvironment,
        IUpdateService updateService,
        ILogger<QuestDefinitionService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(appEnvironment);
        ArgumentNullException.ThrowIfNull(updateService);

        _logger = logger;
        var version = updateService.CurrentVersion;

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", version);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{version}");

        _cacheFilePath = Path.Combine(appEnvironment.UserDataPath, CacheFileName);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        if (_isInitialized) return Task.CompletedTask;

        try
        {
            LoadCache();

            if (IsCacheStale())
            {
                // Refresh in the background; do not block startup on network.
                _ = Task.Run(RefreshFromServerAsync);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize quest definitions");
        }

        _isInitialized = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public List<QuestDefinition> GetDailyQuests()
    {
        var quests = new List<QuestDefinition>();

        if (_cache?.Daily != null)
            quests.AddRange(_cache.Daily);

        if (_cache?.Seasonal != null)
            quests.AddRange(_cache.Seasonal.Where(q => q.Type == QuestType.Daily));

        if (quests.Count == 0)
            return QuestDefinition.DailyQuests.ToList();

        return quests;
    }

    /// <inheritdoc />
    public List<QuestDefinition> GetWeeklyQuests()
    {
        var quests = new List<QuestDefinition>();

        if (_cache?.Weekly != null)
            quests.AddRange(_cache.Weekly);

        if (_cache?.Seasonal != null)
            quests.AddRange(_cache.Seasonal.Where(q => q.Type == QuestType.Weekly));

        if (quests.Count == 0)
            return QuestDefinition.WeeklyQuests.ToList();

        return quests;
    }

    /// <inheritdoc />
    public List<QuestDefinition> GetSeasonalQuests()
    {
        return _cache?.Seasonal?.ToList() ?? new List<QuestDefinition>();
    }

    /// <inheritdoc />
    public async Task RefreshFromServerAsync()
    {
        try
        {
            _logger?.LogInformation("Fetching quest definitions from server...");

            var response = await _httpClient.GetAsync($"{ServerBaseUrl}{QuestDefinitionsEndpoint}");
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Failed to fetch quest definitions: {StatusCode}", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var serverResponse = JsonConvert.DeserializeObject<ServerQuestResponse>(json);

            if (serverResponse?.Success != true || serverResponse.Quests == null)
            {
                _logger?.LogWarning("Invalid quest definitions response from server");
                return;
            }

            var newCache = new QuestDefinitionsCache
            {
                Version = serverResponse.Version,
                FetchedAt = DateTime.UtcNow,
                SeasonTitle = serverResponse.SeasonTitle,
                Daily = ParseQuests(serverResponse.Quests.Daily),
                Weekly = ParseQuests(serverResponse.Quests.Weekly),
                Seasonal = ParseQuests(serverResponse.Quests.Seasonal)
            };

            _cache = newCache;
            SaveCache();

            _logger?.LogInformation(
                "Quest definitions updated: v{Version}, {Daily} daily, {Weekly} weekly, {Seasonal} seasonal",
                newCache.Version, newCache.Daily.Count, newCache.Weekly.Count, newCache.Seasonal.Count);

            QuestDefinitionsUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error fetching quest definitions from server");
        }
    }

    private List<QuestDefinition> ParseQuests(List<JObject>? questsJson)
    {
        if (questsJson == null) return new List<QuestDefinition>();

        var quests = new List<QuestDefinition>();
        foreach (var q in questsJson)
        {
            try
            {
                var id = q["id"]?.ToString() ?? "";
                var quest = new QuestDefinition
                {
                    Id = id,
                    Name = q["name"]?.ToString() ?? "",
                    Description = q["description"]?.ToString() ?? "",
                    Type = QuestDefinition.ParseType(q["type"]?.ToString() ?? "daily"),
                    Category = QuestDefinition.ParseCategory(q["category"]?.ToString() ?? "combined"),
                    TargetValue = q["targetValue"]?.Value<int>() ?? 0,
                    XPReward = q["xpReward"]?.Value<int>() ?? 0,
                    Icon = q["icon"]?.ToString() ?? "⭐",
                    // Bundled art only: never fetch remote images, prefer per-id PNGs.
                    ImageUrl = null,
                    ImagePath = QuestDefinition.HasBundledArt(id)
                        ? QuestDefinition.BundledArtPath(id)
                        : GetFallbackImagePath(q["category"]?.ToString()),
                    RequiresPremium = (bool?)q["requiresPremium"] ?? false,
                    IsSeasonal = (bool?)q["seasonal"] ?? false,
                    ActiveFrom = q["activeFrom"]?.ToString(),
                    ActiveUntil = q["activeUntil"]?.ToString()
                };

                if (!string.IsNullOrEmpty(quest.Id))
                    quests.Add(quest);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to parse quest: {Quest}", q.ToString());
            }
        }

        return quests;
    }

    private static string GetFallbackImagePath(string? category)
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

    private bool IsCacheStale()
    {
        if (_cache == null) return true;
        if (!_cache.FetchedAt.HasValue) return true;

        var fetched = _cache.FetchedAt.Value;
        var nowUtc = DateTime.UtcNow;

        // Refetch when the month rolls over so seasonal titles/quests rotate promptly.
        if (fetched.Year != nowUtc.Year || fetched.Month != nowUtc.Month) return true;

        return (nowUtc - fetched).TotalHours >= CacheExpiryHours;
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
                return;

            var json = File.ReadAllText(_cacheFilePath);
            _cache = JsonConvert.DeserializeObject<QuestDefinitionsCache>(json);

            _logger?.LogDebug("Loaded quest definitions cache: v{Version}", _cache?.Version ?? 0);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load quest definitions cache");
            _cache = null;
        }
    }

    private void SaveCache()
    {
        try
        {
            if (_cache == null) return;

            var directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonConvert.SerializeObject(_cache, Formatting.Indented);
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save quest definitions cache");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private class QuestDefinitionsCache
    {
        public int Version { get; set; }
        public DateTime? FetchedAt { get; set; }
        public string? SeasonTitle { get; set; }
        public List<QuestDefinition> Daily { get; set; } = new();
        public List<QuestDefinition> Weekly { get; set; } = new();
        public List<QuestDefinition> Seasonal { get; set; } = new();
    }

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
