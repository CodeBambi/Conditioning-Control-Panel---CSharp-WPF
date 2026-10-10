using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>Contract D on the wire: <c>409 {"error":"merged","canonical_unified_id":"u_..."}</c>
    /// (WPF Services/Account/MergedAccountRecovery.cs, moved whole).</summary>
    internal static class MergedAccountResponse
    {
        public const int ConflictStatus = 409;
        public const string MergedError = "merged";

        private static readonly Regex UnifiedIdPattern = new("^u_[a-z0-9]{6,64}$", RegexOptions.Compiled);

        public static bool IsValidUnifiedId(string? id) =>
            !string.IsNullOrEmpty(id) && UnifiedIdPattern.IsMatch(id);

        public static bool TryParse(int statusCode, string? body, out string? canonicalUnifiedId)
        {
            canonicalUnifiedId = null;
            if (statusCode != ConflictStatus || string.IsNullOrWhiteSpace(body)) return false;

            JObject obj;
            try { obj = JObject.Parse(body); }
            catch { return false; }

            if (!string.Equals(obj["error"]?.Type == JTokenType.String ? obj["error"]!.Value<string>() : null,
                    MergedError, StringComparison.Ordinal))
                return false;

            var canonical = obj["canonical_unified_id"]?.Type == JTokenType.String
                ? obj["canonical_unified_id"]!.Value<string>()
                : null;
            if (!IsValidUnifiedId(canonical)) return false;

            canonicalUnifiedId = canonical;
            return true;
        }
    }

    internal enum MergedSwapDecision { Swap, IgnoreInvalidId, IgnoreSameId, IgnoreAlreadySwapped }

    /// <summary>Each canonical id is swapped to at most once per process, whoever asks first.</summary>
    internal sealed class MergedSwapPolicy
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _swappedTo = new(StringComparer.Ordinal);

        public MergedSwapDecision Decide(string? currentUnifiedId, string? canonicalUnifiedId)
        {
            if (!MergedAccountResponse.IsValidUnifiedId(canonicalUnifiedId)) return MergedSwapDecision.IgnoreInvalidId;
            if (string.Equals(currentUnifiedId, canonicalUnifiedId, StringComparison.Ordinal)) return MergedSwapDecision.IgnoreSameId;
            lock (_gate)
            {
                if (_swappedTo.Contains(canonicalUnifiedId!)) return MergedSwapDecision.IgnoreAlreadySwapped;
            }
            return MergedSwapDecision.Swap;
        }

        public bool MarkSwapped(string canonicalUnifiedId)
        {
            lock (_gate) return _swappedTo.Add(canonicalUnifiedId);
        }

        public int SwapCount { get { lock (_gate) return _swappedTo.Count; } }
    }

    /// <summary>
    /// CONTRACT D. The account this install is signed into was merged into another one: every door
    /// answers 409 "merged" with the canonical id. The recovery adopts that id, drops the tombstone's
    /// token, re-runs the provider sign-in and reloads the profile, once per canonical. The caller's
    /// own request is NOT retried here: it tells the user to try again (account_merged_retry_hint).
    ///
    /// <para>The identity step is Core's; what only a head can do (stop its sync, re-run its
    /// providers, reload, repaint, offer the sign-in) comes in through the seams below. Unseeded
    /// seams are skipped, so the identity still moves and the app reads as signed out of the
    /// tombstone, which is the state a lapsed session leaves.</para>
    /// </summary>
    public static class MergedAccountRecovery
    {
        private static MergedSwapPolicy _policy = new();
        private static readonly SemaphoreSlim SwapGate = new(1, 1);

        /// <summary>Stop anything that writes to the old id (heartbeat, push) and forget the loaded profile.</summary>
        public static volatile Action? StopSync;
        /// <summary>Re-stamp the local quest file for the canonical id (same person, merged account).</summary>
        public static volatile Action<string>? StampQuests;
        /// <summary>Re-run the provider sign-in; true when an auth token for the canonical is in hand.</summary>
        public static volatile Func<Task<bool>>? Reauthenticate;
        /// <summary>Read the canonical's profile and restart the heartbeat.</summary>
        public static volatile Func<Task>? ReloadProfile;
        /// <summary>The swap ended: (canonical, reauthed). The head repaints, and offers the sign-in when false.</summary>
        public static volatile Action<string, bool>? Finished;

        /// <summary>Tests: the running swap, and a clean claim ledger.</summary>
        internal static Task LastSwap { get; private set; } = Task.CompletedTask;
        internal static void ResetForTest() { _policy = new MergedSwapPolicy(); SettingsForTest = null; }
        internal static Func<Models.AppSettings>? SettingsForTest;
        private static Models.AppSettings Settings => SettingsForTest?.Invoke() ?? CoreSettings.Current;

        public static async Task<bool> TryHandleAsync(HttpResponseMessage? response, string? body = null)
        {
            if (response == null) return false;
            var status = (int)response.StatusCode;
            if (status != MergedAccountResponse.ConflictStatus) return false;

            if (body == null)
            {
                try { body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
                catch { return false; }
            }
            return TryHandle(status, body);
        }

        /// <summary>True when the reply was a merge tombstone (handled or already being handled).</summary>
        public static bool TryHandle(int statusCode, string? body)
        {
            if (!MergedAccountResponse.TryParse(statusCode, body, out var canonical)) return false;

            var current = Settings.UnifiedId ?? CoreAccount.UnifiedUserId;
            var decision = _policy.Decide(current, canonical);
            if (decision == MergedSwapDecision.Swap && !_policy.MarkSwapped(canonical!))
                decision = MergedSwapDecision.IgnoreAlreadySwapped;   // lost the claim to a concurrent caller

            if (decision != MergedSwapDecision.Swap)
            {
                Log.Debug("[MergedAccount] 409 merged for {Current} -> {Canonical} ignored ({Decision})", current, canonical, decision);
                return true;
            }

            LastSwap = Task.Run(() => RunSwapAsync(current, canonical!));
            return true;
        }

        private static async Task RunSwapAsync(string? fromUnifiedId, string canonical)
        {
            await SwapGate.WaitAsync().ConfigureAwait(false);
            try { await SwapAsync(fromUnifiedId, canonical).ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "[MergedAccount] swap to {Canonical} failed", canonical); }
            finally { SwapGate.Release(); }
        }

        private static async Task SwapAsync(string? fromUnifiedId, string canonical)
        {
            Log.Information("[MergedAccount] account {From} was merged into {Canonical}; adopting the canonical id and re-signing in",
                fromUnifiedId ?? "(none)", canonical);

            // 1. Timers first, so nothing else writes to the tombstone while we swap; the sync must
            //    READ the canonical before it pushes again.
            try { StopSync?.Invoke(); }
            catch (Exception ex) { Log.Debug("[MergedAccount] stopping sync failed: {Error}", ex.Message); }

            // 2. Identity: canonical id in, tombstone's token out. No cloud backup (that door refused us too).
            CoreAccount.UnifiedUserId = canonical;
            var settings = Settings;
            settings.UnifiedId = canonical;
            settings.AuthToken = null;
            if (SettingsForTest == null) CoreSettings.Save(suppressCloudBackup: true);

            try { StampQuests?.Invoke(canonical); }
            catch (Exception ex) { Log.Debug("[MergedAccount] quest owner restamp failed: {Error}", ex.Message); }

            // 3. Re-run the provider sign-in. The OAuth doors land on the canonical and mint its token.
            bool reauthed = false;
            try { if (Reauthenticate is { } reauth) reauthed = await reauth().ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "[MergedAccount] provider re-auth threw"); }

            if (reauthed)
            {
                var landed = Settings.UnifiedId;
                if (!string.Equals(landed, canonical, StringComparison.Ordinal))
                    Log.Warning("[MergedAccount] provider re-auth answered {Landed}, expected canonical {Canonical}; keeping the server's answer", landed, canonical);
                else
                    Log.Information("[MergedAccount] re-signed in on {Canonical}", canonical);

                try { if (ReloadProfile is { } reload) await reload().ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning(ex, "[MergedAccount] profile reload after swap failed"); }
            }
            else
            {
                // 4. No provider credential: signed out of the tombstone, holding the canonical id with no token.
                Log.Information("[MergedAccount] no provider credential to re-sign in with; offering the sign-in prompt for {Canonical}", canonical);
            }

            try { Finished?.Invoke(canonical, reauthed); }
            catch (Exception ex) { Log.Debug("[MergedAccount] finish hook failed: {Error}", ex.Message); }
        }
    }
}
