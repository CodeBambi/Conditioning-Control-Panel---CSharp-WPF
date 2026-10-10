using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Arcademy host (WPF 7.1.5 Services/Arcademy/ArcademyHostService.cs), the run + save half:
    /// the meta store (arcademy_meta.json, same file and shape as WPF), class-started / class-ended
    /// with the XP payout, attendance, punch cards and the till (tickets, tokens, the lever), the
    /// prize counter, enrollment, the card + wallet mirrors, campus presence, the settings frames
    /// (GameWindow.Arcademy.Init.cs) and the registry link. Bodies copied from WPF :550-650,
    /// :3689-4290, :5374-5500. Frame ledger and what is left: C:/wt-par/progress/g2-frames.md.
    /// </summary>
    internal sealed partial class GameWindow
    {
        /// <summary>Tests name the save file (CorePaths.UserData is fixed for the process).</summary>
        internal static string? ArcademyMetaPathOverride;

        private ArcademyMetaStore? _arcMeta;
        private bool _arcAttached, _arcClassActive, _arcSuppressSettingEcho;
        private int _arcGeneration;
        private readonly Dictionary<string, string> _arcPendingLever = new(StringComparer.Ordinal);

        internal ArcademyMetaStore? ArcademyMeta => _arcMeta;

        private bool HandleArcademy(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "ready":
                    // WPF OnPageReady: exactly one init per boot, then the fullscreen state.
                    IsReady = true;
                    NoteHeartbeat();
                    EnsureArcademy();
                    Post(BuildArcademyInit());
                    Post(new { type = "fullscreen", on = IsHostFullscreen });
                    SeedArcademyNativeState();
                    Log.Information("[Game] arcademy: sent init (protocol 1)");
                    return true;
                case "fullscreen-request":
                    SetHostFullscreen((bool?)o["on"] ?? false);
                    return true;
                case "set-setting":
                    OnArcademySetSetting(o);
                    return true;
                case "meta-command":
                    EnsureArcademy();
                    _arcMeta?.Handle(o);
                    // SEAM(emi-desk): WPF pushes a lockerOutfit write to the desk mascot here (PushEmiOutfitToDesk).
                    return true;
                case "class-started":
                    _arcClassActive = true;
                    InRun = true;
                    NotePendingLever((string?)o["gameKey"], (string?)o["lever"]);
                    Log.Information("[Game] arcademy: class started ({Game}, tier {Tier})", (string?)o["gameKey"], ArcInt(o, "gradeTier", 0));
                    try { ArcademyPresenceService.NoteRoomEnter((string?)o["gameKey"]); } catch (Exception ex) { Log.Debug("[Game] arcademy presence: {E}", ex.Message); }
                    return true;
                case "class-ended":
                    InRun = false;
                    OnArcademyClassEnded(o);
                    return true;
                case "class-left":
                    _arcClassActive = false;
                    InRun = false;
                    return true;
                case "enrollment-done":
                    OnArcademyEnrollmentDone(o);
                    return true;
                case "prize-buy":
                    OnArcademyPrizeBuy(o);
                    return true;
                case "library-remove":
                    OnArcademyLibraryRemove(o);
                    return true;
                case "annex-stats":
                    OnArcademyAnnexStats();
                    return true;
                case "assets-request":
                case "local-sample-request":
                    // Not ported (remote + local media batches). WPF answers a closed gate with an empty
                    // batch rather than silence, because silence leaves the page spinning: same here.
                    Log.Information("[Game] arcademy: '{T}' not ported yet - answered empty", (string?)o["type"]);
                    Post(new { type = "assets", reqId = (string?)o["reqId"] ?? "", urls = Array.Empty<object>(), done = true });
                    return true;
                case "probe-sub":
                    Log.Information("[Game] arcademy: 'probe-sub' not ported yet - answered offline");
                    Post(new { type = "sub-probe", reqId = (string?)o["reqId"] ?? "", name = (string?)o["name"] ?? "", ok = false, videoCount = (int?)null, stillOnly = false, error = "offline" });
                    return true;
                case "share-image":
                    Log.Information("[Game] arcademy: 'share-image' not ported yet - answered not copied");
                    Post(new { type = "share-image-result", ok = false });
                    return true;
                case "link-discord":
                    Log.Information("[Game] arcademy: 'link-discord' not ported yet");
                    PushArcademyProfile("failed");
                    return true;
                case "resume-request":
                    // The panic ladder's rung 1 (suspend) is not ported: panic closes every game window at once.
                    Log.Debug("[Game] arcademy: resume-request with no panic suspend outstanding - ignored");
                    return true;
                default:
                    return false;   // heartbeat, pong, boot-error, log, exit, exit-done: the shared shell
            }
        }

        /// <summary>WPF Launch: mint the store and attach the three mirrors. Lazy (first ready), and torn
        /// down on the window's own Closed, so the shared open/close hooks stay untouched.</summary>
        private void EnsureArcademy()
        {
            if (_arcMeta == null)
            {
                _arcMeta = ArcademyMetaPathOverride is { } path
                    ? new ArcademyMetaStore(Post, path)
                    : new ArcademyMetaStore(Post);
                var unlocked = _arcMeta.UnlockedGameKeys();
                if (unlocked.Count > 0)
                    Log.Information("[Game] arcademy: punch cards unlock {N} room(s): {Keys}", unlocked.Count, string.Join(", ", unlocked));
                Closed += (_, _) => CloseArcademy();
            }
            if (_arcAttached) return;
            _arcAttached = true;
            try { ArcademySyncService.Attach(_arcMeta, PostArcademyMetaIfLive); } catch (Exception ex) { Log.Debug("[Game] arcademy sync attach: {E}", ex.Message); }
            try { ArcademyWalletSyncService.Attach(_arcMeta, PostArcademyMetaIfLive); } catch (Exception ex) { Log.Debug("[Game] arcademy wallet attach: {E}", ex.Message); }
            try { ArcademyPresenceService.Attach(OnArcademyPresence); } catch (Exception ex) { Log.Debug("[Game] arcademy presence attach: {E}", ex.Message); }
            HookArcademySettings(true);
        }

        /// <summary>WPF DisposeAll: stop the mirrors, flush the save, drop the store.</summary>
        private void CloseArcademy()
        {
            Interlocked.Increment(ref _arcGeneration);
            HookArcademySettings(false);
            if (_arcAttached)
            {
                _arcAttached = false;
                try { ArcademyPresenceService.Detach(); } catch (Exception ex) { Log.Debug("[Game] arcademy presence detach: {E}", ex.Message); }
                try { ArcademyWalletSyncService.Detach(); } catch (Exception ex) { Log.Debug("[Game] arcademy wallet detach: {E}", ex.Message); }
                try { ArcademySyncService.Detach(); } catch (Exception ex) { Log.Debug("[Game] arcademy sync detach: {E}", ex.Message); }
            }
            try { _arcMeta?.FlushSave(); } catch (Exception ex) { Log.Warning("[Game] arcademy meta flush: {E}", ex.Message); }
            _arcMeta = null;
            _arcClassActive = false;
        }

        /// <summary>A mirror moved the store on a background thread (cards or wallet): re-push the blob.</summary>
        private void PostArcademyMetaIfLive()
        {
            var meta = _arcMeta;
            if (meta == null || IsClosedOrClosing) return;
            try { Post(meta.SnapshotMessage()); } catch (Exception ex) { Log.Debug("[Game] arcademy meta push: {E}", ex.Message); }
        }

        private void OnArcademyPresence(string? self, JObject? snapshot)
        {
            if (_arcMeta == null || IsClosedOrClosing) return;
            try { Post(new { type = "presence", self, snapshot }); } catch (Exception ex) { Log.Debug("[Game] arcademy presence post: {E}", ex.Message); }
        }

        // ============================ class-ended: XP + attendance ============================

        private static readonly Dictionary<int, double> ArcXpBase = new() { [1] = 40, [2] = 60, [3] = 85, [4] = 110 };

        private static readonly Dictionary<string, double> ArcXpGradeMult = new(StringComparer.OrdinalIgnoreCase)
        {
            ["S+"] = 1.6, ["S"] = 1.5, ["A"] = 1.25, ["B"] = 1.0, ["C"] = 0.6, ["pass"] = 1.0,
        };

        private const double ArcFlavorXpCap = 15;
        private const double ArcDareBonusXp = 15;
        private static readonly HashSet<string> ArcDareKinds = new(StringComparer.Ordinal) { "S", "streak", "fast" };

        /// <summary>The Extra Credit lever, remembered rather than believed: clamped against what is
        /// unlocked at class-started and held until class-ended asks. The page never echoes a multiplier.</summary>
        private void NotePendingLever(string? gameKey, string? lever)
        {
            try
            {
                var key = (gameKey ?? "").Trim();
                if (key.Length == 0 || key.Length > 64) return;
                var (extra, honors) = _arcMeta?.LeverUnlocks() ?? (false, false);
                var clamped = ArcademyEconomy.ClampLever(lever?.Trim(), extra, honors);
                if (clamped == "standard") _arcPendingLever.Remove(key);
                else if (_arcPendingLever.Count < 64 || _arcPendingLever.ContainsKey(key)) _arcPendingLever[key] = clamped;
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy lever: {E}", ex.Message); }
        }

        private string TakePendingLever(string gameKey)
        {
            if (!_arcPendingLever.TryGetValue(gameKey, out var lever)) return "standard";
            _arcPendingLever.Remove(gameKey);
            return lever;
        }

        private readonly record struct ArcTill(int Tickets, int Base, double Mult, bool TokenMinted, int Payday, string Lever, JObject Wallet);

        private readonly record struct ArcPayout(string GameKey, int Tier, double Xp, bool LevelUp, int Streak, int Perfect,
            int ClassesToday, bool Retake, double DareXp, string Grade, string DareWon, string DayUtc);

        private void OnArcademyClassEnded(JObject o)
        {
            _arcClassActive = false;
            try
            {
                EnsureArcademy();
                // Every field read defensively and separately: one garbled field must never cost the
                // player the day's attendance (a garbled grade degrades to C, a flavour bonus to 0).
                var gameKey = (ArcString(o, "gameKey") ?? "").Trim();
                if (gameKey.Length > 64) gameKey = gameKey[..64];
                int tier = Math.Clamp(ArcInt(o, "gradeTier", 1), 1, 4);
                bool zen = ArcBool(o, "zen", false);
                var grade = (ArcString(o, "grade") ?? "").Trim();
                if (zen || !ArcXpGradeMult.ContainsKey(grade)) grade = zen ? "pass" : "C";

                var lever = TakePendingLever(gameKey);
                // S+ is unreachable outside Honors: a claimed one is graded S, never trusted.
                if (string.Equals(grade, "S+", StringComparison.OrdinalIgnoreCase) && lever != "honors")
                {
                    Log.Warning("[Game] arcademy: S+ claimed for {Game} without the Honors lever - graded S", gameKey);
                    grade = "S";
                }
                double flavor = Math.Clamp(ArcDouble(o, "flavorXp", 0), 0, ArcFlavorXpCap);

                var dayUtc = (ArcString(o, "dayUtc") ?? "").Trim();
                if (!DateTime.TryParseExact(dayUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    dayUtc = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                bool firstToday = _arcMeta?.TryClaimXpDay(gameKey, dayUtc) ?? true;

                double xp = firstToday ? ArcXpBase[tier] * ArcXpGradeMult[grade] + flavor : 0;

                int levelBefore = CoreSettings.Current?.PlayerLevel ?? 0;
                if (xp > 0)
                {
                    try { CoreProgression.AddXP(xp, "Other"); }
                    catch (Exception ex) { Log.Debug("[Game] arcademy payout AddXP: {E}", ex.Message); }
                }
                // EMI's dare: gated on firstToday for the same reason the payout is (a retake is free).
                var dareWon = (ArcString(o, "dareWon") ?? "").Trim();
                if (dareWon.Length > 16) dareWon = dareWon[..16];
                bool darePaid = firstToday && ArcDareKinds.Contains(dareWon);
                if (darePaid)
                {
                    try { CoreProgression.AddXP(ArcDareBonusXp, "Quest"); }
                    catch (Exception ex) { Log.Debug("[Game] arcademy dare AddXP: {E}", ex.Message); }
                }
                int levelAfter = CoreSettings.Current?.PlayerLevel ?? levelBefore;

                var localDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var (streak, perfect, classesToday, slipsSpent) = _arcMeta?.RecordAttendance(localDate, gameKey) ?? (0, 0, 0, 0);
                var lateSlipUsed = slipsSpent > 0;

                // Signed in: the server banks the till and the local mint is the fallback. Signed out: local.
                var mintFrame = ArcademyWalletSyncService.DoorOpen
                    ? ArcademyWalletSyncService.BuildMintFrame(gameKey, grade, zen, streak, localDate, lever, slipsSpent, dayUtc)
                    : null;
                var till = mintFrame == null ? MintArcademyCurrency(gameKey, grade, zen, streak, localDate, lever) : default;

                ArcademyPunchCards.PunchMint? punch = null;
                try
                {
                    bool gradedS = ArcademyEconomy.IsTokenGrade(grade, zen: false);
                    punch = _arcMeta?.StampPunchCard(gameKey, localDate, gradedS);
                    if (punch is { Minted: true }) ArcademySyncService.NotifyMutation();
                }
                catch (Exception ex) { Log.Warning("[Game] arcademy punch card: {E}", ex.Message); }

                try { ArcademyPresenceService.NoteClassEnd(gameKey, grade); } catch (Exception ex) { Log.Debug("[Game] arcademy presence: {E}", ex.Message); }

                var report = new ArcPayout(gameKey, tier, xp, levelAfter > levelBefore, streak, perfect, classesToday,
                    !firstToday, darePaid ? ArcDareBonusXp : 0, grade, darePaid ? dareWon : "", dayUtc);

                if (_arcMeta != null) Post(_arcMeta.SnapshotMessage());
                if (mintFrame == null)
                {
                    PostArcademyPayout(report, till, lateSlipUsed);
                    PostArcademyPunchCard(gameKey, "daily", punch);
                    return;
                }
                PostArcademyPunchCard(gameKey, "daily", punch);
                int epoch = Volatile.Read(ref _arcGeneration);
                ArcademyWalletSyncService.Bank(mintFrame, outcome =>
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        SettleArcademyMint(epoch, report, mintFrame, lever, lateSlipUsed, zen, localDate, outcome)));
            }
            catch (Exception ex) { Log.Warning("[Game] arcademy class-ended: {E}", ex.Message); }
        }

        private void SettleArcademyMint(int epoch, ArcPayout report, JObject frame, string lever, bool lateSlipUsed,
            bool zen, string localDate, ArcademyWalletSyncService.MintOutcome outcome)
        {
            try
            {
                if (_arcMeta == null || Volatile.Read(ref _arcGeneration) != epoch) return;
                ArcTill till;
                bool slip = lateSlipUsed;
                if (outcome.Verdict == ArcademyWalletSyncService.MintVerdict.Banked && outcome.Economy != null)
                {
                    till = ArcTillFromEconomy(outcome.Economy, lever);
                    slip = (bool?)outcome.Economy["lateSlipUsed"] ?? lateSlipUsed;
                }
                else
                {
                    till = MintArcademyCurrency(report.GameKey, report.Grade, zen, report.Streak, localDate, lever);
                    if (outcome.Verdict == ArcademyWalletSyncService.MintVerdict.Queue) ArcademyWalletSyncService.Park(frame);
                }
                Post(_arcMeta.SnapshotMessage());
                PostArcademyPayout(report, till, slip);
            }
            catch (Exception ex) { Log.Warning("[Game] arcademy settle mint: {E}", ex.Message); }
        }

        private static ArcTill ArcTillFromEconomy(JObject economy, string fallbackLever)
        {
            int tickets = Math.Max(0, (int?)economy["tickets"] ?? 0);
            int b = Math.Max(0, (int?)economy["ticketBase"] ?? 0);
            double mult = (double?)economy["ticketMult"] ?? 1.0;
            if (!(mult > 0)) mult = 1.0;
            bool token = (bool?)economy["tokenMinted"] ?? false;
            int payday = Math.Max(1, (int?)economy["payday"]?["mult"] ?? 1);
            var lever = (string?)economy["lever"];
            if (string.IsNullOrWhiteSpace(lever)) lever = fallbackLever;
            return new ArcTill(tickets, b, mult, token, payday, lever,
                ArcademyEconomy.BalanceJson(ArcademyEconomy.EnsureShape(economy["wallet"] as JObject)));
        }

        /// <summary>The local till (WPF MintCurrency): tickets by grade, plays, streak, payday and the lever;
        /// one token for the first S of the day. Sparkles are not involved: this wallet is tickets and tokens.</summary>
        private ArcTill MintArcademyCurrency(string gameKey, string grade, bool zen, int streak, string localDate, string lever)
        {
            var empty = new JObject { ["t"] = 0, ["k"] = 0 };
            if (_arcMeta == null || gameKey.Length == 0) return new ArcTill(0, 0, 1.0, false, 1, lever, empty);
            try
            {
                _arcMeta.TryUnlockExtraCredit(grade);
                var prior = _arcMeta.NoteWalletPlay(localDate, gameKey);
                var payday = ArcademyEconomy.PickPayday(DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _arcMeta.EnrolledGameKeys());
                var paydayMult = ArcademyEconomy.PaydayMultFor(payday, gameKey);
                var sum = ArcademyEconomy.ComputeTickets(grade, prior, streak, paydayMult, lever);
                _arcMeta.EarnTickets(sum.Tickets);
                bool token = ArcademyEconomy.IsTokenGrade(grade, zen) && _arcMeta.TryClaimTokenDay(localDate);
                return new ArcTill(sum.Tickets, sum.Base, sum.Mult, token, paydayMult, lever, ArcademyEconomy.BalanceJson(_arcMeta.WalletSnapshot()));
            }
            catch (Exception ex)
            {
                Log.Warning("[Game] arcademy mint: {E}", ex.Message);
                return new ArcTill(0, 0, 1.0, false, 1, lever, empty);
            }
        }

        private void PostArcademyPayout(in ArcPayout r, in ArcTill till, bool lateSlipUsed)
        {
            Post(new
            {
                type = "payout-result",
                gameKey = r.GameKey,
                xp = r.Xp,
                levelUp = r.LevelUp,
                streak = r.Streak,
                perfectAttendance = r.Perfect,
                classesToday = r.ClassesToday,
                retake = r.Retake,
                dareXp = r.DareXp,
                grade = r.Grade,
                tickets = till.Tickets,
                ticketBase = till.Base,
                ticketMult = till.Mult,
                tokenMinted = till.TokenMinted,
                payday = till.Payday,
                lever = till.Lever,
                lateSlipUsed,
                wallet = till.Wallet ?? new JObject { ["t"] = 0, ["k"] = 0 },
            });
            Log.Information("[Game] arcademy: class complete ({Game}, tier {Tier}, grade {Grade}) = {Xp:0} XP, {Tickets} tickets, streak {Streak}, {Today}/4 today",
                r.GameKey, r.Tier, r.Grade, r.Xp, till.Tickets, r.Streak, r.ClassesToday);
        }

        private void PostArcademyPunchCard(string gameKey, string reason, ArcademyPunchCards.PunchMint? mint)
        {
            if (mint is not { } m) return;
            Post(new
            {
                type = "punchcard-result",
                gameKey,
                reason,
                minted = m.Punches,
                justUnlocked = m.JustUnlocked,
                holes = ArcademyPunchCards.Holes,
                card = m.Card,
            });
        }

        // ============================ the prize counter ============================

        private void OnArcademyPrizeBuy(JObject o)
        {
            try
            {
                var sku = (ArcString(o, "sku") ?? "").Trim();
                if (sku.Length > 64) sku = sku[..64];
                EnsureArcademy();
                if (_arcMeta == null) { PostArcademyWalletResult(sku, false, "unknown", null); return; }

                if (ArcademyWalletSyncService.DoorOpen)
                {
                    int epoch = Volatile.Read(ref _arcGeneration);
                    ArcademyWalletSyncService.Buy(sku, outcome => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (_arcMeta == null || Volatile.Read(ref _arcGeneration) != epoch) return;
                            if (!outcome.Answered) { PostArcademyWalletResult(sku, false, "offline", _arcMeta.WalletSnapshot()); return; }
                            if (outcome.Ok) Post(_arcMeta.SnapshotMessage());
                            PostArcademyWalletResult(sku, outcome.Ok, outcome.Reason, _arcMeta.WalletSnapshot());
                        }
                        catch (Exception ex) { Log.Warning("[Game] arcademy settle buy: {E}", ex.Message); }
                    }));
                    return;
                }

                var localDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var result = _arcMeta.Buy(sku, localDate);
                if (result.Ok) Post(_arcMeta.SnapshotMessage());
                PostArcademyWalletResult(sku, result.Ok, result.Reason, _arcMeta.WalletSnapshot());
            }
            catch (Exception ex) { Log.Warning("[Game] arcademy prize-buy: {E}", ex.Message); }
        }

        private void PostArcademyWalletResult(string sku, bool ok, string? reason, JObject? wallet)
        {
            try
            {
                var w = ArcademyEconomy.EnsureShape(wallet);
                Post(new
                {
                    type = "wallet-result",
                    ok,
                    sku,
                    reason,
                    wallet = ArcademyEconomy.BalanceJson(w),
                    inv = ArcademyEconomy.InvJson(w),
                    unlocks = ArcademyEconomy.UnlocksJson(w),
                });
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy wallet-result: {E}", ex.Message); }
        }

        // ============================ enrollment, library, registry ============================

        private void OnArcademyEnrollmentDone(JObject o)
        {
            try
            {
                var gameKey = (ArcString(o, "gameKey") ?? "").Trim();
                if (gameKey.Length > 64) gameKey = gameKey[..64];
                if (gameKey.Length == 0) return;
                EnsureArcademy();
                var localDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var mint = _arcMeta?.EnrollPunchCard(gameKey, localDate);
                if (mint is { Minted: true })
                {
                    if (_arcMeta != null) Post(_arcMeta.SnapshotMessage());
                    ArcademySyncService.NotifyMutation();
                }
                PostArcademyPunchCard(gameKey, "enrollment", mint);
            }
            catch (Exception ex) { Log.Warning("[Game] arcademy enrollment-done: {E}", ex.Message); }
        }

        private void OnArcademyLibraryRemove(JObject o)
        {
            try
            {
                var name = (string?)o["name"] ?? (string?)o["sub"] ?? "";
                var s = CoreSettings.Current;
                if (s != null && !string.IsNullOrWhiteSpace(name) && s.RemoveLibrarySub(name))
                {
                    CoreSettings.Save();
                    FypOnlineCoordinator.ResetAllChannels();
                    Log.Information("[Game] arcademy: r/{Sub} removed from the library", name.Trim());
                }
                PushArcademyLibrary();
            }
            catch (Exception ex) { Log.Warning("[Game] arcademy library remove: {E}", ex.Message); }
        }

        private void PushArcademyLibrary()
        {
            try { Post(new { type = "library", subLibrary = BuildArcademySubLibrary() }); }
            catch (Exception ex) { Log.Debug("[Game] arcademy library push: {E}", ex.Message); }
        }

        private const string ArcAnnexStatsUrl = ProviderSubscription.ProxyBaseUrl + "/v2/arcademy/annex/stats";
        private const int ArcMaxAnnexStatsChars = 400_000;
        private static readonly HttpClient ArcAnnexHttp = new() { Timeout = TimeSpan.FromSeconds(6) };

        /// <summary>The registry link downstairs: exactly one reply always comes back, a failure is body = null.</summary>
        private async void OnArcademyAnnexStats()
        {
            int epoch = Volatile.Read(ref _arcGeneration);
            JObject? body = null;
            if (CoreSettings.Current?.OfflineMode != true)
            {
                try
                {
                    using var response = await ArcAnnexHttp.GetAsync(ArcAnnexStatsUrl).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (text.Length <= ArcMaxAnnexStatsChars) body = JObject.Parse(text);
                    }
                    else Log.Debug("[Game] arcademy: annex-stats {Status}", (int)response.StatusCode);
                }
                catch (Exception ex) { Log.Debug("[Game] arcademy: annex-stats failed: {E}", ex.Message); body = null; }
            }
            if (IsClosedOrClosing || Volatile.Read(ref _arcGeneration) != epoch) return;
            try { Post(new { type = "annex-stats", body }); } catch (Exception ex) { Log.Debug("[Game] arcademy annex post: {E}", ex.Message); }
        }

        // ============================ field readers that degrade instead of throwing ============================

        private static string? ArcString(JObject o, string name) =>
            o[name] is JValue { Type: JTokenType.String } v ? (string?)v.Value : null;

        private static int ArcInt(JObject o, string name, int fallback)
        {
            try
            {
                return o[name] switch
                {
                    JValue { Type: JTokenType.Integer or JTokenType.Float } v => Convert.ToInt32(v.Value, CultureInfo.InvariantCulture),
                    JValue { Type: JTokenType.String } s when int.TryParse((string?)s.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                    _ => fallback,
                };
            }
            catch { return fallback; }
        }

        private static double ArcDouble(JObject o, string name, double fallback)
        {
            try
            {
                double d = o[name] switch
                {
                    JValue { Type: JTokenType.Integer or JTokenType.Float } v => Convert.ToDouble(v.Value, CultureInfo.InvariantCulture),
                    JValue { Type: JTokenType.String } s when double.TryParse((string?)s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                    _ => fallback,
                };
                return double.IsFinite(d) ? d : fallback;
            }
            catch { return fallback; }
        }

        private static bool ArcBool(JObject o, string name, bool fallback) => o[name] switch
        {
            JValue { Type: JTokenType.Boolean } v => (bool)(v.Value ?? fallback),
            _ => fallback,
        };
    }
}
