using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The wire shape of a merged-account refusal (split-accounts contract, item B/D):
    /// <c>HTTP 409 {"error":"merged","canonical_unified_id":"u_..."}</c>. Pure parsing, no app
    /// state, so it is unit-testable on its own.
    /// </summary>
    internal static class MergedAccountResponse
    {
        public const int ConflictStatus = 409;
        public const string MergedError = "merged";

        /// <summary>
        /// Conservative shape check for a unified id: the server mints <c>u_</c> plus a base36
        /// timestamp plus 12 hex chars (server.js generateUnifiedId), so lower-case alphanumerics
        /// after the prefix. Bounded so a hostile body cannot hand us a megabyte "id".
        /// </summary>
        private static readonly Regex UnifiedIdPattern = new("^u_[a-z0-9]{6,64}$", RegexOptions.Compiled);

        public static bool IsValidUnifiedId(string? id) =>
            !string.IsNullOrEmpty(id) && UnifiedIdPattern.IsMatch(id);

        /// <summary>
        /// True when <paramref name="statusCode"/> + <paramref name="body"/> is a merged-account
        /// refusal carrying a well-formed canonical id. Any other 409 (and every non-409) is false,
        /// so callers keep their existing handling for those.
        /// </summary>
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

    internal enum MergedSwapDecision
    {
        /// <summary>Adopt the canonical id.</summary>
        Swap,
        /// <summary>The canonical id is missing or malformed.</summary>
        IgnoreInvalidId,
        /// <summary>The app already holds the canonical id; nothing to do.</summary>
        IgnoreSameId,
        /// <summary>This session already swapped to that canonical once; never loop.</summary>
        IgnoreAlreadySwapped,
    }

    /// <summary>
    /// Loop guard for the merged swap: at most one swap per app session per canonical id, and
    /// never a swap onto the id already held. Pure state, no app references, thread-safe.
    /// </summary>
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

        /// <summary>Records the swap. Returns false if it was already recorded (a concurrent winner).</summary>
        public bool MarkSwapped(string canonicalUnifiedId)
        {
            lock (_gate) return _swappedTo.Add(canonicalUnifiedId);
        }

        public int SwapCount { get { lock (_gate) return _swappedTo.Count; } }
    }

    /// <summary>
    /// Contract D, desktop side. One handler for every authenticated V2 request path that can be
    /// answered with <c>409 merged</c> after the account this desktop was signed into has been
    /// merged into another (the record it keeps writing to is now a tombstone).
    ///
    /// <para>On a merged refusal: adopt <c>canonical_unified_id</c> as the stored unified id, drop
    /// the auth token (it belonged to the tombstone and the canonical never issued it), persist,
    /// stop the sync/heartbeat timers, then re-run the existing provider sign-in
    /// (<see cref="App.ReauthenticateAfterMergedSwapAsync"/>, the NeedsAuthTokenUpgrade flow
    /// through /v2/auth/patreon or /v2/auth/discord) so the canonical mints a fresh token. With no
    /// provider credential the app lands in the ordinary signed-out state and a sign-in prompt is
    /// offered through the startup presenter.</para>
    ///
    /// <para>Single-flight: several timers can receive the same 409 in the same second. The first
    /// caller claims the canonical id in <see cref="MergedSwapPolicy"/> (an atomic add) and the
    /// swap runs ONCE, on a detached task; the rest see
    /// <see cref="MergedSwapDecision.IgnoreAlreadySwapped"/> (or
    /// <see cref="MergedSwapDecision.IgnoreSameId"/> once the swap has landed) and return. The
    /// loop guard is per app session per canonical id, so a canonical that is itself merged later
    /// can still be followed once, but the same hop can never repeat.</para>
    ///
    /// <para>Detached on purpose: some of the doors this is wired into are called from inside
    /// <c>ProfileSyncService.HandleUnauthorizedAsync</c>, which holds a non-reentrant recovery
    /// gate. The swap reloads the profile, and that reload can 401 and want the same gate. Running
    /// the swap inline there would deadlock; running it on its own task cannot.</para>
    /// </summary>
    public static class MergedAccountRecovery
    {
        private static readonly MergedSwapPolicy Policy = new();
        private static readonly SemaphoreSlim SwapGate = new(1, 1);

        /// <summary>
        /// Checks <paramref name="response"/> for a merged refusal and starts the swap if it is one.
        /// Returns true when the response WAS a merged 409 (whether the swap was started or the
        /// guard ignored it): the caller should stop treating the request as an ordinary failure
        /// and not retry it with the old id. Returns false for every other response, untouched.
        ///
        /// <para>Pass <paramref name="body"/> when the caller has already read it; otherwise the
        /// body is read here (only on a 409, so the common path costs nothing).</para>
        /// </summary>
        public static async Task<bool> TryHandleAsync(HttpResponseMessage? response, string? body = null)
        {
            if (response == null) return false;
            var status = (int)response.StatusCode;
            if (status != MergedAccountResponse.ConflictStatus) return false;

            if (body == null)
            {
                try { body = await response.Content.ReadAsStringAsync(); }
                catch { return false; }
            }
            return TryHandle(status, body);
        }

        /// <summary>
        /// Status + body form for callers that read the body themselves. Synchronous: it parses,
        /// decides, claims the canonical id and schedules the swap; nothing here waits on the
        /// network.
        /// </summary>
        public static bool TryHandle(int statusCode, string? body)
        {
            if (!MergedAccountResponse.TryParse(statusCode, body, out var canonical)) return false;

            var current = App.Settings?.Current?.UnifiedId ?? App.UnifiedUserId;
            var decision = Policy.Decide(current, canonical);
            if (decision == MergedSwapDecision.Swap && !Policy.MarkSwapped(canonical!))
                decision = MergedSwapDecision.IgnoreAlreadySwapped;   // lost the claim to a concurrent caller

            if (decision != MergedSwapDecision.Swap)
            {
                App.Logger?.Debug("[MergedAccount] 409 merged for {Current} -> {Canonical} ignored ({Decision})",
                    current, canonical, decision);
                return true;
            }

            _ = Task.Run(() => RunSwapAsync(current, canonical!));
            return true;
        }

        private static async Task RunSwapAsync(string? fromUnifiedId, string canonical)
        {
            // Serialises successive swaps (a canonical that is itself merged later); the claim in
            // TryHandle already guarantees each canonical runs at most once.
            await SwapGate.WaitAsync();
            try
            {
                await SwapAsync(fromUnifiedId, canonical);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[MergedAccount] swap to {Canonical} failed", canonical);
            }
            finally
            {
                SwapGate.Release();
            }
        }

        private static async Task SwapAsync(string? fromUnifiedId, string canonical)
        {
            App.Logger?.Information("[MergedAccount] account {From} was merged into {Canonical}; adopting the canonical id and re-signing in",
                fromUnifiedId ?? "(none)", canonical);

            // 1. Timers first, so nothing else writes to the tombstone while we swap.
            try { App.ProfileSync?.StopTimersForAccountSwap(); }
            catch (Exception ex) { App.Logger?.Debug("[MergedAccount] stopping timers failed: {Error}", ex.Message); }

            // 2. Identity: canonical id in, tombstone's token out. Persisted without a cloud backup
            //    (the backup door is one of the ones that just refused us).
            App.UnifiedUserId = canonical;
            var settings = App.Settings?.Current;
            if (settings != null)
            {
                settings.UnifiedId = canonical;
                settings.AuthToken = null;
                App.Settings?.Save(suppressCloudBackup: true);
            }

            // The local quest file is stamped with the owning id and is RESET by EnsureOwnedBy on
            // the next profile load when the stamp differs. Same human, merged account: carry it.
            try { App.Quests?.StampOwner(canonical); }
            catch (Exception ex) { App.Logger?.Debug("[MergedAccount] quest owner restamp failed: {Error}", ex.Message); }

            // Same reason as a logout: the sync service must READ the canonical before it PUSHes.
            try { App.ProfileSync?.ResetLoadedProfileState(); }
            catch (Exception ex) { App.Logger?.Debug("[MergedAccount] sync state reset failed: {Error}", ex.Message); }

            // 3. Re-run the provider sign-in. The OAuth doors land on the canonical and mint its token.
            bool reauthed = false;
            try { reauthed = await App.ReauthenticateAfterMergedSwapAsync(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[MergedAccount] provider re-auth threw"); }

            if (reauthed)
            {
                var landed = App.Settings?.Current?.UnifiedId;
                if (!string.Equals(landed, canonical, StringComparison.Ordinal))
                    App.Logger?.Warning("[MergedAccount] provider re-auth answered {Landed}, expected canonical {Canonical}; keeping the server's answer",
                        landed, canonical);
                else
                    App.Logger?.Information("[MergedAccount] re-signed in on {Canonical}", canonical);

                try
                {
                    if (App.ProfileSync != null)
                    {
                        await App.ProfileSync.LoadProfileAsync();
                        App.ProfileSync.StartHeartbeat();
                    }
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "[MergedAccount] profile reload after swap failed"); }

                RefreshAccountUi();
                return;
            }

            // 4. No provider credential (device-code / email session, or the provider's own token
            //    has lapsed): the app is now signed out of the tombstone and holds the canonical id
            //    with no token, exactly the state a lapsed session leaves. Offer the sign-in.
            App.Logger?.Information("[MergedAccount] no provider credential to re-sign in with; offering the sign-in prompt for {Canonical}", canonical);
            RefreshAccountUi();
            OfferSignIn(canonical);
        }

        private static void RefreshAccountUi()
        {
            try
            {
                var main = App.MainWindowRef;
                if (main == null) return;
                main.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { main.RefreshAccountUiAfterMergedSwap(); }
                    catch (Exception ex) { App.Logger?.Debug("[MergedAccount] UI refresh failed: {Error}", ex.Message); }
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[MergedAccount] UI refresh dispatch failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// The passive surface for the no-credential case: opens at once when nothing owns the
        /// user, otherwise becomes an Inbox row. Keyed per canonical so a second refusal in the
        /// same session cannot post it twice.
        /// </summary>
        private static void OfferSignIn(string canonical)
        {
            var ladder = App.StartupLadder;
            var item = new Startup.InboxItem
            {
                Key = "merged-account-signin:" + canonical,
                Glyph = "🔗",
                Title = Localization.Loc.Get("account_merged_signin_title"),
                Summary = Localization.Loc.Get("account_merged_signin_summary"),
                Open = () => App.MainWindowRef?.OpenLoginAfterMergedSwap(),
            };

            if (ladder != null)
            {
                ladder.PresentOrInbox(item);
                return;
            }

            // No presenter yet (very early boot): the login button is still there, and the next
            // launch validates the restored session and lands in the same place.
            App.Logger?.Debug("[MergedAccount] no startup presenter; leaving the sign-in to the account button");
        }

        /// <summary>Test seam: the decision the live policy would make right now.</summary>
        internal static MergedSwapDecision PeekDecision(string? currentUnifiedId, string? canonicalUnifiedId)
            => Policy.Decide(currentUnifiedId, canonicalUnifiedId);
    }
}
