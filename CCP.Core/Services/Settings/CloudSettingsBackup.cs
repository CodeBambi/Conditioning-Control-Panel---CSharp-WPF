using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>Public metadata about a cloud settings backup (WPF <c>SettingsBackupInfo</c>).</summary>
    public sealed class CloudBackupInfo
    {
        public string? AppVersion { get; set; }
        public DateTime? BackedUpAt { get; set; }
        public int SizeBytes { get; set; }
    }

    /// <summary>
    /// Cloud settings backup for the cross-platform head: the backup half of WPF
    /// <c>ProfileSyncService</c> (BackupSettingsAsync, GetSettingsBackupInfoAsync,
    /// RestoreSettingsFromCloudAsync, ExcludedBackupProperties, PreserveLocalOnlyFields) and
    /// <c>SettingsBackupBudget</c>, on the same two routes with the same body
    /// (<c>POST /v2/user/backup-settings</c>, <c>POST /v2/user/settings-backup</c>).
    /// Named apart from the WPF types on purpose: the in-tree WPF app sees Core's internals and still
    /// carries its own copies.
    ///
    /// <para>What a backup is: the whole settings object as JSON, minus <see cref="IsExcludedFromBackup"/>,
    /// gzipped and base64'd. What a restore is: that object with every identity, progression,
    /// entitlement and machine-local field copied back from this machine (<see cref="ApplyLocalWins"/>),
    /// then held to the safety floor (<see cref="HoldSafetyFloor"/>).</para>
    /// </summary>
    public sealed class CloudSettingsBackup
    {
        public const string ServerUrl = "https://codebambi-proxy.vercel.app";

        /// <summary>Base64 bytes we are willing to send (WPF SettingsBackupBudget.MaxEncodedBytes).</summary>
        public const int MaxEncodedBytes = 660_000;

        /// <summary>Dropped in this order, only while the backup is still over budget.</summary>
        public static readonly IReadOnlyList<string> TrimOrder = new[]
        {
            "ActiveAssetPaths", "DisabledAssetPaths", "AssetPresets",
        };

        /// <summary>Server-authoritative, identity and machine-local settings (WPF ExcludedBackupProperties,
        /// plus the Tier-2 window the WPF restore already keeps local and the port's panic notice flag).</summary>
        private static readonly string[] ExcludedPropertyNames =
        {
            nameof(AppSettings.UnifiedId),
            nameof(AppSettings.OpenRouterApiKey),
            nameof(AppSettings.PlayerLevel),
            nameof(AppSettings.PlayerXP),
            nameof(AppSettings.SkillPoints),
            nameof(AppSettings.UnlockedSkills),
            nameof(AppSettings.HighestLevelEver),
            nameof(AppSettings.IsSeason0Og),
            nameof(AppSettings.CurrentSeason),
            nameof(AppSettings.PendingSkillsResetAck),
            nameof(AppSettings.UserDisplayName),
            nameof(AppSettings.PatreonTier),
            nameof(AppSettings.PatreonPremiumValidUntil),
            nameof(AppSettings.PatreonLabValidUntil),
            nameof(AppSettings.InviteGrantUntil),
            nameof(AppSettings.LastPatreonVerification),
            nameof(AppSettings.AuthToken),
            nameof(AppSettings.CustomAssetsPath),
            nameof(AppSettings.DiscordWebhookUrl),
            nameof(AppSettings.LastSeenUtc),
            nameof(AppSettings.FriendsPresenceShared),
            nameof(AppSettings.ModPersonalityPreset),
            // A panic switched the screen read off on THIS machine: the notice stays here.
            nameof(AppSettings.KeywordTriggersOffByPanic),
        };

        /// <summary>A secret never leaves the machine, whatever it is called the day it is added.</summary>
        private static readonly string[] SecretMarkers = { "Token", "ApiKey", "Secret", "Password", "Webhook", "Credential" };

        private static readonly PropertyInfo[] SettingsProperties =
            typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>Every Chaster setting, found by name so a new one is covered the day it is added
        /// (WPF ChasterLocalProperties). Device-local like the link itself.</summary>
        internal static readonly PropertyInfo[] ChasterLocalProperties = SettingsProperties
            .Where(p => p.Name.StartsWith("Chaster", StringComparison.Ordinal)
                        && p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                        && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute)))
            .ToArray();

        /// <summary>C# names AND the JSON names they are written under (a <c>[JsonProperty("x")]</c> name
        /// differs from the C# one, and the strip runs over JSON keys).</summary>
        private static readonly HashSet<string> ExcludedKeys = BuildExcludedKeys();

        private static HashSet<string> BuildExcludedKeys()
        {
            var keys = new HashSet<string>(ExcludedPropertyNames, StringComparer.OrdinalIgnoreCase);
            foreach (var p in SettingsProperties)
            {
                var json = p.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
                if (string.IsNullOrEmpty(json)) continue;
                if (keys.Contains(p.Name) || IsChasterName(p.Name) || IsSecretName(p.Name)) keys.Add(json!);
            }
            return keys;
        }

        private static bool IsChasterName(string name) => name.StartsWith("Chaster", StringComparison.OrdinalIgnoreCase);

        private static bool IsSecretName(string name) =>
            SecretMarkers.Any(m => name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>True when a backup must not carry this top-level key.</summary>
        public static bool IsExcludedFromBackup(string? name) =>
            !string.IsNullOrEmpty(name) && (ExcludedKeys.Contains(name!) || IsChasterName(name!) || IsSecretName(name!));

        /// <summary>The backup body before compression: the settings object minus the excluded keys.</summary>
        public static JObject BuildBackupObject(AppSettings settings)
        {
            var obj = JObject.Parse(JsonConvert.SerializeObject(settings, Formatting.None));
            foreach (var key in obj.Properties().Select(p => p.Name).ToList())
                if (IsExcludedFromBackup(key)) obj.Remove(key);
            return obj;
        }

        // ---- budget (WPF SettingsBackupBudget) ----

        public sealed class Encoded
        {
            public byte[] Compressed { get; init; } = Array.Empty<byte>();
            public string Base64 { get; init; } = "";
            public List<string> Trimmed { get; init; } = new();
            public int Budget { get; init; } = MaxEncodedBytes;
            public bool Fits => Base64.Length <= Budget;
        }

        /// <summary>Encode, trimming the bulky per-file path lists in place while over budget, then
        /// putting back whatever still fits.</summary>
        public static Encoded Encode(JObject settings, int budget = MaxEncodedBytes)
        {
            var trimmed = new List<string>();
            var removed = new List<JProperty>();
            var (gz, b64) = Pack(settings);
            foreach (var key in TrimOrder)
            {
                if (b64.Length <= budget) break;
                var prop = settings.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
                if (prop == null) continue;
                prop.Remove();
                removed.Add(prop);
                trimmed.Add(prop.Name);
                (gz, b64) = Pack(settings);
            }
            for (int i = removed.Count - 2; i >= 0 && b64.Length <= budget; i--)
            {
                settings.Add(removed[i]);
                var (g, b) = Pack(settings);
                if (b.Length <= budget) { (gz, b64) = (g, b); trimmed.Remove(removed[i].Name); }
                else removed[i].Remove();
            }
            return new Encoded { Compressed = gz, Base64 = b64, Trimmed = trimmed, Budget = budget };
        }

        private static (byte[] Gz, string B64) Pack(JObject settings)
        {
            var bytes = Encoding.UTF8.GetBytes(settings.ToString(Formatting.None));
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(bytes, 0, bytes.Length);
            var gz = output.ToArray();
            return (gz, Convert.ToBase64String(gz));
        }

        /// <summary>base64 -> gzip -> JSON -> settings. Excluded keys a hostile or old backup still
        /// carries are dropped before the object is built, so they land at their defaults.</summary>
        public static AppSettings? Decode(string base64)
        {
            string json;
            using (var input = new MemoryStream(Convert.FromBase64String(base64)))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
                json = reader.ReadToEnd();
            var obj = JObject.Parse(json);
            foreach (var key in obj.Properties().Select(p => p.Name).ToList())
                if (IsExcludedFromBackup(key)) obj.Remove(key);
            return obj.ToObject<AppSettings>(JsonSerializer.Create(new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace,
            }));
        }

        // ---- restore rules ----

        /// <summary>WPF ProfileSyncService.PreserveLocalOnlyFields: a backup never carries these, so the
        /// restored object arrives with them at their defaults; this machine's values win.</summary>
        public static void PreserveLocalOnlyFields(AppSettings? current, AppSettings? restored)
        {
            if (current == null || restored == null) return;
            restored.CustomAssetsPath = current.CustomAssetsPath;
            restored.DiscordWebhookUrl = current.DiscordWebhookUrl;
            restored.LastSeenUtc = current.LastSeenUtc;
            restored.FriendsPresenceShared = current.FriendsPresenceShared;
            restored.ModPersonalityPreset = current.ModPersonalityPreset; // the setter copies
            restored.KeywordTriggersOffByPanic = current.KeywordTriggersOffByPanic;
            // A backup over budget leaves out the per-file asset lists, so an empty list in the
            // restore may just mean "not carried": keep this PC's own.
            if (restored.DisabledAssetPaths.Count == 0 && current.DisabledAssetPaths.Count > 0)
                restored.DisabledAssetPaths = new HashSet<string>(current.DisabledAssetPaths);
            if (restored.ActiveAssetPaths.Count == 0 && current.ActiveAssetPaths.Count > 0)
                restored.ActiveAssetPaths = new HashSet<string>(current.ActiveAssetPaths);
            if (restored.AssetPresets.Count == 0 && current.AssetPresets.Count > 0)
                restored.AssetPresets = new List<AssetPreset>(current.AssetPresets);
            foreach (var p in ChasterLocalProperties)
            {
                var v = p.GetValue(current);
                if (v is List<string> list) v = new List<string>(list);
                p.SetValue(restored, v);
            }
        }

        /// <summary>
        /// A restore is never more permissive than the machine it lands on. It can not switch ON any
        /// Strict Lock flag, can not switch OFF the panic key, and can not bring the screen read back
        /// after a panic press switched it off here (the player switches that on again by hand).
        /// </summary>
        public static void HoldSafetyFloor(AppSettings? current, AppSettings? restored)
        {
            if (current == null || restored == null) return;
            foreach (var p in SettingsProperties)
            {
                if (p.PropertyType != typeof(bool) || !p.CanRead || !p.CanWrite) continue;
                if (p.Name.IndexOf("StrictLock", StringComparison.Ordinal) < 0) continue;
                if (!(bool)p.GetValue(current)! && (bool)p.GetValue(restored)!) p.SetValue(restored, false);
            }
            if (current.PanicKeyEnabled) restored.PanicKeyEnabled = true;
            if (current.KeywordTriggersOffByPanic)
            {
                restored.ScreenOcrEnabled = false;
                restored.KeywordTriggersOffByPanic = true;
            }
        }

        /// <summary>The whole local-wins pass of the WPF manual restore (MainWindow.CloudBackup.cs):
        /// identity, progression and entitlement windows, then the machine-local set, then the safety
        /// floor. Secrets (auth token, API key) live in the secret store and are never touched.</summary>
        public static void ApplyLocalWins(AppSettings? current, AppSettings? restored)
        {
            if (current == null || restored == null) return;
            restored.UnifiedId = current.UnifiedId;
            restored.PlayerLevel = current.PlayerLevel;
            restored.PlayerXP = current.PlayerXP;
            restored.SkillPoints = current.SkillPoints;
            restored.UnlockedSkills = current.UnlockedSkills;
            restored.HighestLevelEver = current.HighestLevelEver;
            restored.IsSeason0Og = current.IsSeason0Og;
            restored.CurrentSeason = current.CurrentSeason;
            restored.PendingSkillsResetAck = current.PendingSkillsResetAck;
            restored.UserDisplayName = current.UserDisplayName;
            restored.PatreonTier = current.PatreonTier;
            restored.PatreonPremiumValidUntil = current.PatreonPremiumValidUntil;
            restored.PatreonLabValidUntil = current.PatreonLabValidUntil;
            restored.InviteGrantUntil = current.InviteGrantUntil;
            restored.LastPatreonVerification = current.LastPatreonVerification;
            PreserveLocalOnlyFields(current, restored);
            HoldSafetyFloor(current, restored);
        }

        // ---- the client ----

        private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        private readonly HttpClient _http;
        private readonly Func<AppSettings?> _settings;
        private readonly Func<string?> _token;
        private int _refusedTooLarge;

        public CloudSettingsBackup(Func<AppSettings?>? settings = null, HttpMessageHandler? handler = null, Func<string?>? token = null)
        {
            _settings = settings ?? (() => CoreSettings.Current);
            _token = token ?? (() => _settings()?.AuthToken);
            _http = handler == null ? Shared.Value : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        /// <summary>Identity for every call: the account id AND its token. Either missing = signed out.</summary>
        public bool HasIdentity
        {
            get
            {
                var s = _settings();
                return s != null && !string.IsNullOrEmpty(s.UnifiedId) && !string.IsNullOrEmpty(_token());
            }
        }

        private HttpRequestMessage NewRequest(string path, object body, AppSettings s)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ServerUrl + path);
            request.Headers.Add("X-Auth-Token", _token());
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            return request;
        }

        /// <summary>Upload now (the button: WPF BackupSettingsAsync(force: true)). False when signed out,
        /// offline, over budget or refused. Never throws.</summary>
        public async Task<bool> BackupAsync()
        {
            var s = _settings();
            if (s == null || s.OfflineMode || !HasIdentity) return false;
            try
            {
                var obj = BuildBackupObject(s);
                var encoded = Encode(obj);
                if (!encoded.Fits)
                {
                    Log.Warning("Settings backup skipped: {Size} bytes encoded is over the {Max} budget even after trimming [{Trimmed}]",
                        encoded.Base64.Length, MaxEncodedBytes, string.Join(",", encoded.Trimmed));
                    return false;
                }
                using var request = NewRequest("/v2/user/backup-settings", new
                {
                    unified_id = s.UnifiedId,
                    settings_data = encoded.Base64,
                    app_version = CoreReleaseContent.AppVersion,
                }, s);
                using var response = await _http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (await MergedAccountRecovery.TryHandleAsync(response).ConfigureAwait(false)) return false;
                    if ((int)response.StatusCode == 413) Interlocked.Exchange(ref _refusedTooLarge, 1);
                    Log.Warning("Settings backup failed: {Status}", response.StatusCode);
                    return false;
                }
                Log.Information("Settings backed up to cloud ({Size} bytes compressed)", encoded.Compressed.Length);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("Settings backup did not finish: {ExType}: {Error}", ex.GetType().Name, ex.Message);
                return false;
            }
        }

        private async Task<JToken?> FetchAsync()
        {
            var s = _settings();
            if (s == null || !HasIdentity) return null;
            using var request = NewRequest("/v2/user/settings-backup", new { unified_id = s.UnifiedId }, s);
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await MergedAccountRecovery.TryHandleAsync(response).ConfigureAwait(false);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var backup = JObject.Parse(json)["backup"];
            return backup == null || backup.Type != JTokenType.Object ? null : backup;
        }

        /// <summary>The cloud copy's date and version, or null when there is none. THROWS when the
        /// check itself fails (the card shows "could not check"), as WPF's caller expects.</summary>
        public async Task<CloudBackupInfo?> GetInfoAsync()
        {
            var backup = await FetchAsync().ConfigureAwait(false);
            if (backup == null) return null;
            return new CloudBackupInfo
            {
                AppVersion = (string?)backup["app_version"],
                // Json.NET hands an ISO string back as a Date token: read it as one, or the string
                // cast re-formats it in the machine's culture (Italian desks read day and month swapped).
                BackedUpAt = backup["backed_up_at"] is { Type: JTokenType.Date } d ? (DateTime)d
                    : DateTime.TryParse((string?)backup["backed_up_at"], System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null,
                SizeBytes = (int?)backup["size_bytes"] ?? 0,
            };
        }

        /// <summary>Download and decode the cloud copy (excluded fields at their defaults), or null.
        /// The caller runs <see cref="ApplyLocalWins"/> before swapping it in. Never throws.</summary>
        public async Task<AppSettings?> DownloadAsync()
        {
            try
            {
                var backup = await FetchAsync().ConfigureAwait(false);
                var data = (string?)backup?["settings_data"];
                if (string.IsNullOrEmpty(data)) return null;
                var restored = Decode(data!);
                Log.Information("Settings downloaded from cloud (v{Version})", (string?)backup!["app_version"]);
                return restored;
            }
            catch (Exception ex)
            {
                Log.Error("Settings restore from cloud failed: {ExType}: {Error}", ex.GetType().Name, ex.Message);
                return null;
            }
        }
    }
}
