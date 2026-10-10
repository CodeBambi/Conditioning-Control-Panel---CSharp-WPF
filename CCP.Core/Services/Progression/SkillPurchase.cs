using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Buying a skill, server-authoritative: WPF <c>SkillTreeService.PurchaseSkillAsync</c> (the local gate, the
    /// login requirement, the local-only effects, <see cref="SkillUnlocked"/>) over
    /// <c>ProfileSyncService.PurchaseSkillAsync</c> (<c>POST /v2/user/purchase-skill</c>).
    ///
    /// <para>Wallet rule: only the server's debited answer lowers <see cref="AppSettings.SkillPoints"/>. A failed
    /// request, an error status and a refusal leave the wallet alone; the ONE exception is a balance refusal that
    /// survives a sync, which adopts the server's number (<see cref="SparklePoints.AfterBalanceRefusal"/>, #1268
    /// #1269 #1300).</para>
    ///
    /// <para>ponytail: no 401 auth recovery (WPF HandleUnauthorizedAsync retries once on a recovered token; this
    /// head has no recovery, so a 401 goes straight to the "session has expired" message); no Pink Rush check
    /// timer on buying pink_rush (the timer is SkillTreeService's, not ported).</para>
    /// </summary>
    public sealed class SkillPurchase
    {
        private const string ServerUrl = "https://codebambi-proxy.vercel.app";

        /// <summary>The app's one instance (WPF App.SkillTree). Unseeded: the view builds a bare one, which still
        /// answers the signed-out and cannot-purchase refusals.</summary>
        public static volatile SkillPurchase? Current;

        private readonly HttpClient _http;
        private readonly Func<AppSettings?> _settings;

        /// <summary>One real sync before a balance refusal is asked again (WPF SyncBeforeRetryAsync). Null or
        /// false: no sync reached the server, the wallet is kept.</summary>
        public Func<Task<bool>>? SyncBeforeRetry { get; set; }
        /// <summary>WPF App.Achievements.TrackSkillPointsSpent.</summary>
        public Action<int>? PointsSpent { get; set; }
        /// <summary>WPF App.Achievements.ReconcileLifetimePointsSpent.</summary>
        public Action<long>? LifetimeSpentReconciled { get; set; }
        /// <summary>WPF App.Settings.Save. Default: the Core settings save.</summary>
        public Action Save { get; set; } = () => { if (CoreSettings.HasProvider) CoreSettings.Save(); };
        /// <summary>The 401 path (WPF HandleUnauthorizedAsync): true only when the session was recovered.</summary>
        public Func<HttpResponseMessage, Task<bool>> Unauthorized { get; set; } = AuthRecovery.HandleUnauthorizedAsync;

        /// <summary>A skill was bought (WPF SkillTreeService.SkillUnlocked).</summary>
        public event EventHandler<string>? SkillUnlocked;

        /// <param name="settings">Default: the head's settings.</param>
        /// <param name="handler">Test seam; null is the real network.</param>
        public SkillPurchase(Func<AppSettings?>? settings = null, HttpMessageHandler? handler = null)
        {
            _settings = settings ?? (() => CoreSettings.HasProvider ? CoreSettings.Service?.Current : null);
            _http = V2AuthService.Configure(new HttpClient(handler ?? new ServerClockHandler()));
            // A test transport means no real network: the recovery is the test's to hand in.
            if (handler != null) Unauthorized = _ => Task.FromResult(false);
        }

        /// <summary>WPF SkillTreeService.PurchaseSkillAsync.</summary>
        public async Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId)
        {
            var settings = _settings();
            if (settings == null) return (false, Loc.Get("skill_err_settings_unavailable"));
            if (!SkillTreeRules.CanPurchaseSkill(settings, skillId)) return (false, Loc.Get("skill_err_cannot_purchase"));

            var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
            if (skill == null) return (false, Loc.Get("skill_err_unknown"));

            // Require login: purchases are server-authoritative.
            if (string.IsNullOrEmpty(settings.UnifiedId)) return (false, Loc.Get("skill_err_login_required"));

            var (success, error) = await RequestAsync(settings, skillId, afterSync: false).ConfigureAwait(true);
            if (!success) return (false, error);

            ApplySkillEffects(settings, skillId);
            Log.Information("Skill purchased via server: {SkillId} for {Cost} points, {Remaining} remaining",
                skillId, skill.Cost, settings.SkillPoints);
            try { SkillUnlocked?.Invoke(this, skillId); }
            catch (Exception ex) { Log.Debug("SkillUnlocked observer failed: {E}", ex.Message); }
            return (true, null);
        }

        /// <summary>WPF SkillTreeService.ApplySkillEffects, minus the Pink Rush timer.</summary>
        internal void ApplySkillEffects(AppSettings settings, string skillId)
        {
            switch (skillId)
            {
                case "good_girl_streak":
                    settings.StreakShieldsRemaining = 1;
                    settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
                    break;
                case "oopsie_insurance":
                    // The server grants the same charge and is authoritative on the next sync. Saved here
                    // because nothing else on this path persists it.
                    settings.StreakFixCharges++;
                    Save();
                    break;
            }
        }

        /// <summary>WPF ProfileSyncService.ApplyPurchaseResult: the server's balance, and the UNION of skills.</summary>
        internal static void ApplyPurchaseResult(AppSettings settings, int? serverSkillPoints, List<string>? serverSkills)
        {
            if (serverSkillPoints.HasValue) settings.SkillPoints = serverSkillPoints.Value;
            if (serverSkills != null)
            {
                var merged = new HashSet<string>(settings.UnlockedSkills ?? new List<string>());
                foreach (var skill in serverSkills) merged.Add(skill);
                settings.UnlockedSkills = merged.ToList();
            }
        }

        /// <summary>WPF ProfileSyncService.PurchaseSkillAsync(skillId, afterSync).</summary>
        private async Task<(bool success, string? error)> RequestAsync(AppSettings settings, string skillId, bool afterSync)
        {
            var unifiedId = settings.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
                return (false, "Purchasing enhancements requires a cloud account. Please log in first.");

            try
            {
                var requestBody = JsonConvert.SerializeObject(new
                {
                    unified_id = unifiedId,
                    skill_id = skillId,
                    // Local points, so the server can reconcile (bubble pop points may not be synced yet).
                    skill_points = settings.SkillPoints,
                });
                async Task<HttpResponseMessage> SendOnce()
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/user/purchase-skill");
                    var token = settings.AuthToken;   // read per send: the retry carries the recovered token
                    if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                    request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                    return await _http.SendAsync(request).ConfigureAwait(true);
                }

                using var first = await SendOnce().ConfigureAwait(true);
                var response = first;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

                // Signed out (or into another account) while this was in flight: the answer is not ours to apply.
                if (!string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal))
                    return (false, Loc.Get("skill_err_login_required"));

                if (!response.IsSuccessStatusCode)
                {
                    if (V2AuthService.MergedRecovery is { } merged && await merged(response, json).ConfigureAwait(true))
                        return (false, Loc.Get("account_merged_retry_hint"));

                    // On 401, attempt auth recovery and retry once, but ONLY if the session was genuinely
                    // recovered: the same POST with the same dead token only burns a round trip (#879).
                    if (await Unauthorized(response).ConfigureAwait(true) && !string.IsNullOrEmpty(settings.AuthToken)
                        && string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal))
                    {
                        Log.Information("Skill purchase: retrying after auth token recovery");
                        response = await SendOnce().ConfigureAwait(true);
                        json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                        if (!string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal))
                            return (false, Loc.Get("skill_err_login_required"));
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Log.Warning("Skill purchase failed: auth token invalid/missing after recovery attempt");
                        return (false, $"Your session has expired. Open ⚙️ {Loc.Get("nav_door_settings")} → Account and sign in again to purchase skills.");
                    }

                    // Never the wallet from an error body: the server may answer 0 for an account whose
                    // points were not backfilled. Sync reconciles.
                    string errorMsg;
                    try { errorMsg = JsonConvert.DeserializeObject<PurchaseSkillResponse>(json)?.Error ?? $"Server error: {response.StatusCode}"; }
                    catch { errorMsg = $"Server error: {response.StatusCode}"; }
                    Log.Warning("Skill purchase failed: {Error}", errorMsg);
                    return (false, errorMsg);
                }

                var result = JsonConvert.DeserializeObject<PurchaseSkillResponse>(json);
                if (result == null) return (false, "Invalid server response");

                if (!result.Success)
                {
                    Log.Warning("Skill purchase rejected: {Error}, server says {Points} points", result.Error, result.SkillPoints);
                    var refused = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
                    if (refused != null)
                    {
                        // First balance refusal: sync once so the server credits what it has not seen, then
                        // ask again (#1300). Only a refusal that survives the sync lowers the wallet.
                        var step = SparklePoints.AfterBalanceRefusal(settings.SkillPoints, result.SkillPoints, refused.Cost, afterSync);
                        if (step == SparklePoints.RefusalStep.SyncAndRetry)
                        {
                            if (SyncBeforeRetry is { } sync && await sync().ConfigureAwait(true))
                            {
                                Log.Information("Skill purchase: balance refusal at {Server} vs local {Local}, synced, asking again",
                                    result.SkillPoints, settings.SkillPoints);
                                return await RequestAsync(settings, skillId, afterSync: true).ConfigureAwait(true);
                            }
                            // No sync reached the server: keep the wallet, the next sync settles it.
                            return (false, result.Error ?? "Purchase failed");
                        }
                        var adopted = SparklePoints.AdoptAfterRefusal(settings.SkillPoints, result.SkillPoints, refused.Cost);
                        if (adopted.HasValue)
                        {
                            Log.Information("Skill purchase: adopting server balance {Server} over local {Local} after a balance refusal",
                                adopted.Value, settings.SkillPoints);
                            settings.SkillPoints = adopted.Value;
                            Save();
                        }
                    }
                    return (false, result.Error ?? "Purchase failed");
                }

                // The debited receipt: the server's authoritative values.
                ApplyPurchaseResult(settings, result.SkillPoints, result.UnlockedSkills);

                // Prestige: count the spend locally, then adopt the server total when it is ahead (it already
                // includes this purchase and reconcile only raises, so this never double-counts).
                var bought = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
                if (bought != null)
                {
                    PointsSpent?.Invoke(bought.Cost);
                    settings.SeasonPointsSpent += bought.Cost;
                }
                if (result.LifetimePointsSpent.HasValue) LifetimeSpentReconciled?.Invoke(result.LifetimePointsSpent.Value);

                Save();
                Log.Information("Skill purchased via server: {SkillId}, {Points} points remaining", skillId, settings.SkillPoints);
                return (true, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Skill purchase request failed");
                return (false, "Connection failed. Please check your internet connection.");
            }
        }

        private sealed class PurchaseSkillResponse
        {
            [JsonProperty("success")] public bool Success { get; set; }
            [JsonProperty("error")] public string? Error { get; set; }
            [JsonProperty("skill_points")] public int? SkillPoints { get; set; }
            [JsonProperty("unlocked_skills")] public List<string>? UnlockedSkills { get; set; }
            [JsonProperty("lifetime_points_spent")] public long? LifetimePointsSpent { get; set; }
        }
    }
}
