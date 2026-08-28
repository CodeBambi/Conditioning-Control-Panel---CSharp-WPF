using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// THE SHARED WALLET, host side (wallet CONTRACT v1, wire contract
/// <c>proxy/docs/arcademy-wallet-api.md</c>). One wallet per ACCOUNT, held by the proxy, mutated
/// only by the server. This class is the desktop's half of that: it reads the account's wallet at
/// launch, imports this machine's wallet into it once, and from then on asks the server to mint
/// every graded finish and to settle every purchase.
///
/// <para>THE SAME POSTURE AS <see cref="ArcademySyncService"/>, AND FOR THE SAME REASON. The
/// Arcademy runs offline inside the WebView2 and a player with no account never touches this file
/// at all: with the door shut, the local wallet stays the authority and the money path is byte for
/// byte what it was. Nothing here blocks, nothing here opens a dialog, and a failure is one log
/// line plus a frame parked on disk. Offline forever is still a working feature.</para>
///
/// <para>WHAT THE SERVER OWNS vs WHAT THIS MACHINE OWNS. The server owns the MONEY: balances, the
/// token ledger, the replay counters the decay ladder reads, the shelf inventory and the two lever
/// rungs. This machine keeps owning ATTENDANCE - the streak, perfect attendance, the punch cards -
/// because those are local-day facts about a person sitting at this desk, and they already have
/// their own mirror. The local <c>wallet</c> meta key becomes a CACHE of the last answer, which is
/// what lets the Prize Counter, the Locker and the chips render with the network down.</para>
///
/// <para>NEVER SUBTRACT, NEVER DOUBLE-PAY. Two rules carry that. First, every mint frame carries a
/// <c>mintId</c> and the server is idempotent on it, so a frame that timed out on the wire and got
/// replayed tomorrow pays exactly once. Second, a frame is only ever QUEUED once this account has
/// imported (<see cref="ArcademyMetaStore.WalletImportedKey"/>): before that the local wallet is
/// still the record and the import itself is what carries the night's earnings up, so queueing as
/// well would bank the same tickets twice.</para>
///
/// <para>THE LOCAL MINT IS A PREVIEW. When a class ends and the server cannot be reached, the host
/// still mints locally so the debrief has a number to show, then parks the frame. The server's
/// answer REPLACES the local wallet when the queue drains, so the preview is never added to
/// anything - it is only ever the page's benefit.</para>
///
/// <para>THE TIER GATE IS PARKED, NOT DROPPED, AND THAT IS A DESKTOP RULE. The server reads the
/// live tier on every call; this app grants a 14-day cached-entitlement grace it does not, so
/// inside that grace an account playing here is answered <c>403 arcademy_locked</c> there. The
/// import can only ever carry what was on this machine when it ran, never a night earned after it,
/// so a dropped frame is the one way the grace could cost somebody money they watched themselves
/// earn. Parked frames wait for the pledge, and the server pays a queued one even when its stamp
/// falls outside the attendance wall. A 403 that names no tier is the identity door, which is a
/// different refusal and keeps its own ending.</para>
/// </summary>
internal static class ArcademyWalletSyncService
{
    /// <summary>The proxy, spelled the way every other service here spells it (there is no shared
    /// constant in this codebase - see <see cref="ArcademySyncService"/>).</summary>
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    private const string WalletPath = "/v2/arcademy/wallet";
    private const string ImportPath = "/v2/arcademy/wallet/import";
    private const string ClassEndedPath = "/v2/arcademy/class-ended";
    private const string PrizeBuyPath = "/v2/arcademy/prize-buy";

    /// <summary>How long a call the PAGE is waiting on may take. The Prize Counter puts its own
    /// watchdog down at 6s (<c>shell/prizecounter.js</c> ECHO_WAIT_MS) and answers "the counter
    /// went quiet" when it fires, so a request that has not landed by 5s has to be turned into a
    /// refusal here while there is still a second left to say so. The report card has no watchdog
    /// - a late <c>payout-result</c> simply repaints it - but it reads as a stall all the same, so
    /// the mint is timed the same way.</summary>
    private static readonly TimeSpan PageWait = TimeSpan.FromSeconds(5);

    /// <summary>Bodies are truncated in the log the way <see cref="ArcademySyncService"/> does.</summary>
    private const int MaxLoggedBody = 200;

    /// <summary>The wire's id shape for both <c>deviceId</c> and <c>mintId</c>. A GUID in "N" form
    /// is 32 hex characters, comfortably inside it.</summary>
    private static readonly Regex IdShape = new("^[A-Za-z0-9_-]{8,64}$", RegexOptions.Compiled);

    /// <summary>The launch pull can take its time (nobody is watching it); the two page-facing
    /// calls carry their own shorter cancellation instead.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static ArcademyWalletSyncService()
    {
        try
        {
            Http.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            Http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
        }
        catch { /* a header we could not set is not worth failing a static ctor over */ }
    }

    private static readonly object Gate = new();
    private static ArcademyMetaStore? _store;
    private static Action? _onWalletChanged;

    /// <summary>Bumped by <see cref="Detach"/>, read by every continuation - same generation guard
    /// <see cref="ArcademySyncService"/> and <see cref="ArcademyHostService"/> use, so a reply that
    /// lands after the window closed cannot write into the next session's store.</summary>
    private static int _generation;

    /// <summary>Tonight's payday as the SERVER drew it, or null when this launch never got an
    /// answer. <c>init.economy.payday</c> prefers it and falls back to the local draw; the two
    /// agree whenever the mirrored roster and the enrolled roster do.</summary>
    private static ArcademyEconomy.Payday? _serverPayday;

    /// <summary>One hard refusal is worth a line; a hundred are worth none. Covers the tier gate and
    /// the credential door alike - either way it is the same launch-long condition. Reset per launch.</summary>
    private static bool _refusalLogged;

    /// <summary>Set the first time the tier gate turns a mint away this launch. It changes nothing
    /// about what happens to the frame (it parks, like any other unanswered one); it only stops the
    /// replay reporting a wall as "the wire is down", which is the wrong thing to go looking for.</summary>
    private static bool _tierWalled;

    /// <summary>One request at a time. The server takes a 5s account lock on every write, so two
    /// of ours racing would only 409 each other.</summary>
    private static readonly SemaphoreSlim InFlight = new(1, 1);

    // ============================ lifecycle ============================

    /// <summary>
    /// Bind to the store the Arcademy just opened and go and fetch the account's wallet. Called
    /// from <see cref="ArcademyHostService"/>'s launch beside the punch-card mirror's own Attach,
    /// and like that one it is not awaited: the reply usually lands while the WebView2 is still
    /// booting and rides out in <c>init</c>, and when it is slower
    /// <paramref name="onWalletChanged"/> pushes the ordinary whole-blob meta snapshot instead.
    /// </summary>
    public static void Attach(ArcademyMetaStore store, Action onWalletChanged)
    {
        lock (Gate)
        {
            _store = store;
            _onWalletChanged = onWalletChanged;
            _serverPayday = null;
            _refusalLogged = false;
            _tierWalled = false;
        }
        int generation = Volatile.Read(ref _generation);
        Run(() => LaunchAsync(generation), "launch");
    }

    /// <summary>Unbind at teardown. Nothing is flushed because nothing is buffered: a frame that
    /// did not get out lives in <c>pendingMints</c> on disk and the next launch replays it.</summary>
    public static void Detach()
    {
        lock (Gate)
        {
            _store = null;
            _onWalletChanged = null;
            _serverPayday = null;
        }
        Interlocked.Increment(ref _generation);
    }

    /// <summary>Is there an account to bank into? The same door <see cref="ArcademySyncService"/>
    /// pushes cards through, asked the same way and just as quietly.</summary>
    public static bool DoorOpen => Identity(out _, out _);

    /// <inheritdoc cref="_serverPayday"/>
    public static ArcademyEconomy.Payday? ServerPayday
    {
        get { lock (Gate) return _serverPayday; }
    }

    // ============================ what the host calls ============================

    /// <summary>What became of one graded finish.</summary>
    internal enum MintVerdict
    {
        /// <summary>The server minted it. <c>Economy</c> is its answer and the wallet is adopted.</summary>
        Banked,

        /// <summary>Nobody answered, or nobody may answer YET (network, 5xx, 409, 429, a timeout,
        /// and the tier gate). Mint locally for the page and park the frame.</summary>
        Queue,

        /// <summary>The server said no and will say no to this frame for ever (a malformed body, a
        /// credential that names no account at all). Mint locally and park nothing - a queue of
        /// frames that can never land is just a leak.</summary>
        Refused,
    }

    internal readonly record struct MintOutcome(MintVerdict Verdict, JObject? Economy);

    /// <summary>What became of one press of Buy. <c>Answered</c> false is the offline case: the
    /// counter refuses and the local wallet is left exactly as it was.</summary>
    internal readonly record struct BuyOutcome(bool Answered, bool Ok, string? Reason, JObject? Wallet);

    /// <summary>
    /// Bank one graded finish. Fire-and-forget with a guaranteed single callback on a background
    /// thread - the caller owns marshalling it to the dispatcher, exactly like the mirror's
    /// <c>onCardsChanged</c>.
    /// </summary>
    public static void Bank(JObject frame, Action<MintOutcome> settled)
    {
        int generation = Volatile.Read(ref _generation);
        Run(async () =>
        {
            MintOutcome outcome;
            try { outcome = await PostMintAsync(frame, generation).ConfigureAwait(false); }
            catch (Exception ex)
            {
                App.Logger?.Information("[ArcademyWallet] mint failed: {E}", ex.Message);
                outcome = new MintOutcome(MintVerdict.Queue, null);
            }
            try { settled(outcome); }
            catch (Exception ex) { App.Logger?.Debug("ArcademyWallet.Bank callback: {E}", ex.Message); }

            // A mint that landed is proof the wire is up: anything parked from an earlier night
            // can go out now rather than waiting for the next launch.
            if (outcome.Verdict == MintVerdict.Banked) await ReplayAsync(generation).ConfigureAwait(false);
        }, "mint");
    }

    /// <summary>Settle one press of Buy at the counter. Same shape as <see cref="Bank"/>.</summary>
    public static void Buy(string sku, Action<BuyOutcome> settled)
    {
        int generation = Volatile.Read(ref _generation);
        Run(async () =>
        {
            BuyOutcome outcome;
            try { outcome = await PostBuyAsync(sku, generation).ConfigureAwait(false); }
            catch (Exception ex)
            {
                App.Logger?.Information("[ArcademyWallet] buy failed: {E}", ex.Message);
                outcome = new BuyOutcome(false, false, "offline", null);
            }
            try { settled(outcome); }
            catch (Exception ex) { App.Logger?.Debug("ArcademyWallet.Buy callback: {E}", ex.Message); }
        }, "buy");
    }

    // ============================ launch ============================

    /// <summary>
    /// THE LAUNCH PULL, and the only place the import rule runs. Read the account's wallet, adopt
    /// it, carry this machine's wallet up if it has never been carried, then drain anything that
    /// was parked while the wire was down.
    /// </summary>
    private static async Task LaunchAsync(int generation)
    {
        if (!Identity(out var unifiedId, out var token)) return;

        // Wait our turn rather than give up. Sign out and back in, or close the Arcademy and open
        // it again, and this runs while the previous pull is still on the wire; dropping the new
        // one would leave the fresh window reading a stale cache until the next launch. Bounded so
        // a wedged request cannot hold a launch here for ever, and the epoch is asked again on the
        // way in because the wait is long enough for the window to have moved on.
        if (!await InFlight.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false)) return;
        try
        {
            if (Retired(generation)) return;

            var url = $"{ProxyBaseUrl}{WalletPath}?unified_id={Uri.EscapeDataString(unifiedId)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Auth-Token", token);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                App.Logger?.Information("[ArcademyWallet] pull failed: {Status} {Body}",
                    (int)response.StatusCode, Truncate(body));
                return;
            }

            var reply = Parse(body);
            if (reply == null || Retired(generation)) return;
            var store = Store(generation);
            if (store == null) return;

            NotePayday(reply["payday"] as JObject);
            NoteCatalogWave(reply["catalogWave"]);

            bool exists = (bool?)reply["exists"] ?? false;
            var serverWallet = reply["wallet"] as JObject;
            bool imported = store.WalletImportedAt() != null;

            // THE IMPORT RULE (contract, "Client behaviour"). Two doors into the same call: a
            // server with nothing on this account takes whatever this machine has earned, and a
            // server that already holds a wallet MERGES a machine that has never handed its own
            // over. Both are once-per-device forever after - the server remembers the deviceId, and
            // so do we, so a lost answer costs a wasted round trip rather than a second import.
            if (!imported && (exists || store.WalletHasEarnings()))
            {
                var answer = await PostImportAsync(store, unifiedId, token, generation).ConfigureAwait(false);
                if (answer != null) serverWallet = answer;
            }
            else if (!imported)
            {
                // Nothing here and nothing there. Not an import, and deliberately not marked as one
                // either: the day this machine does earn something is the day it should carry it up.
                App.Logger?.Information("[ArcademyWallet] no wallet on either side yet - nothing to import");
            }

            if (Retired(generation)) return;
            if (serverWallet != null)
            {
                store.AdoptServerWallet(serverWallet);
                App.Logger?.Information("[ArcademyWallet] adopted the account wallet: {T} tickets, {K} tokens (rev {Rev})",
                    (int?)serverWallet["t"] ?? 0, (int?)serverWallet["k"] ?? 0, (int?)serverWallet["rev"] ?? 0);
            }

            await ReplayAsync(generation).ConfigureAwait(false);
            RaiseChanged(generation);
        }
        catch (Exception ex)
        {
            App.Logger?.Information("[ArcademyWallet] pull failed: {E}", ex.Message);
        }
        finally { try { InFlight.Release(); } catch { } }
    }

    /// <summary>
    /// Hand this machine's wallet over, once. The answer is the merged wallet and it is what gets
    /// adopted; the flag is written on any answer at all, including the server's own no-op, because
    /// what it records is "this device has been offered", not "this device paid in".
    /// </summary>
    private static async Task<JObject?> PostImportAsync(ArcademyMetaStore store, string unifiedId,
        string token, int generation)
    {
        try
        {
            var deviceId = store.WalletDeviceId();
            if (!IdShape.IsMatch(deviceId))
            {
                App.Logger?.Warning("[ArcademyWallet] refusing to import with a malformed device id");
                return null;
            }

            var payload = JsonConvert.SerializeObject(new
            {
                unified_id = unifiedId,
                deviceId,
                wallet = store.WalletSnapshot(),
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}{ImportPath}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Auth-Token", token);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                App.Logger?.Information("[ArcademyWallet] import refused: {Status} {Body} - trying again next launch",
                    (int)response.StatusCode, Truncate(text));
                return null;
            }

            var reply = Parse(text);
            if (Retired(generation)) return null;

            // A clamp is the server refusing an impossible delta, not a failed request - the same
            // reading the punch-card mirror's clamps get. Worth a line, worth nothing else.
            if (reply?["clamps"] is JArray clamps && clamps.Count > 0)
            {
                App.Logger?.Information("[ArcademyWallet] the import was clamped: {Clamps}",
                    string.Join(", ", clamps.Select(c => (string?)c ?? "?")));
            }

            store.MarkWalletImported();
            App.Logger?.Information("[ArcademyWallet] this machine's wallet has been carried up ({Imported})",
                (bool?)reply?["imported"] == true ? "merged" : "already known");
            return reply?["wallet"] as JObject;
        }
        catch (Exception ex)
        {
            App.Logger?.Information("[ArcademyWallet] import failed: {E}", ex.Message);
            return null;
        }
    }

    // ============================ the mint ============================

    /// <summary>
    /// POST one class-ended frame. Timed at <see cref="PageWait"/> because the debrief is sitting
    /// there waiting for the number.
    /// </summary>
    private static async Task<MintOutcome> PostMintAsync(JObject frame, int generation)
    {
        if (!Identity(out var unifiedId, out var token)) return new MintOutcome(MintVerdict.Refused, null);

        using var cts = new CancellationTokenSource(PageWait);
        try
        {
            var body = (JObject)frame.DeepClone();
            body["unified_id"] = unifiedId;

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}{ClassEndedPath}")
            {
                Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Auth-Token", token);

            using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;

                if (status == 403)
                {
                    var refusal = Parse(text);

                    // THE TIER GATE IS THE ONE REFUSAL THIS LANE PARKS, and it is worth saying why
                    // at length because the server's own contract advises the opposite.
                    //
                    // The desktop grants a 14-day CACHED-ENTITLEMENT GRACE that the server does not
                    // keep: the proxy reads the live tier on every call, so inside that grace an
                    // account that is legitimately playing here is answered 403 there. Dropping the
                    // frame is the one way that grace could cost somebody real money, because the
                    // once-per-device import can only ever carry what was on the machine when it
                    // ran - never a night earned after it. The queue CAN carry that night, and the
                    // server pays a queued frame even when its stamp falls outside the attendance
                    // wall (it answers `<game>:queued_stamp_walled` and pays anyway).
                    //
                    // Replayed on the NEXT LAUNCH and no sooner, which is also the only cadence
                    // that means anything: a pledge does not come back mid-session.
                    if (IsTierRefusal(refusal))
                    {
                        lock (Gate)
                        {
                            _tierWalled = true;
                            if (!_refusalLogged)
                            {
                                _refusalLogged = true;
                                App.Logger?.Information(
                                    "[ArcademyWallet] the server will not bank for this account yet: {Body}. Frames are being parked until it does.",
                                    Truncate(text));
                            }
                        }
                        return new MintOutcome(MintVerdict.Queue, null);
                    }

                    // THE OTHER 403 IS `arcademy_identity_unknown`, and it is a different animal:
                    // this credential resolves to no account at all, so there is no wallet for a
                    // queue to drain into and a parked frame would be waiting on something that is
                    // not coming. The money stays on this machine, where it already is.
                    lock (Gate)
                    {
                        if (!_refusalLogged)
                        {
                            _refusalLogged = true;
                            App.Logger?.Information(
                                "[ArcademyWallet] the server cannot name an account for this credential: {Body}. Tickets stay on this machine.",
                                Truncate(text));
                        }
                    }
                    return new MintOutcome(MintVerdict.Refused, null);
                }

                // A 400 is this client speaking a contract the server has not learned (or a frame
                // built out of something unreadable). Replaying it forever would never fix it.
                //
                // ONE REASON IS DIFFERENT AND IT IS WORTH THE EXTRA BRANCH. `local_day_out_of_range`
                // on a frame that did NOT go up marked `queued` is the ATTENDANCE wall, which is
                // deliberately the tighter of the server's two: it asks whether somebody really was
                // at this desk on that day. The queue's wall is fourteen days wide, so the very same
                // frame replayed as a queued one is a frame the server will pay. Parking it is
                // therefore not a retry of a hopeless request, it is asking a different question.
                if (status == 400)
                {
                    var refusal = Parse(text);
                    if ((string?)refusal?["reason"] == "local_day_out_of_range"
                        && (bool?)frame["queued"] != true)
                    {
                        App.Logger?.Information(
                            "[ArcademyWallet] the attendance wall would not take {Day} - parking it for the queue's wider window",
                            (string?)frame["localDay"]);
                        return new MintOutcome(MintVerdict.Queue, null);
                    }

                    App.Logger?.Warning("[ArcademyWallet] the server refused the frame: {Body}", Truncate(text));
                    return new MintOutcome(MintVerdict.Refused, null);
                }

                // A 401 IS PARKED, NOT DROPPED, and the distinction is worth a line of its own.
                // It is what a token caught mid-refresh looks like, and it is also what this route
                // would answer if it had not yet learned the desktop's own door - and in both cases
                // the money is real and the frame will pay the moment the credential works. Parking
                // is the side that cannot lose a night; the queue's own cap is what bounds it.
                if (status == 401)
                {
                    lock (Gate)
                    {
                        if (!_refusalLogged)
                        {
                            _refusalLogged = true;
                            App.Logger?.Warning(
                                "[ArcademyWallet] the mint would not take this account's credential: {Body}. Frames are being parked until it does.",
                                Truncate(text));
                        }
                    }
                    return new MintOutcome(MintVerdict.Queue, null);
                }

                App.Logger?.Information("[ArcademyWallet] mint refused: {Status} {Body} - parking the frame",
                    status, Truncate(text));
                return new MintOutcome(MintVerdict.Queue, null);
            }

            var reply = Parse(text);
            var economy = reply?["economy"] as JObject;
            if (economy == null)
            {
                // A 200 with no economy block is an older server that only stamped the card. The
                // money never left this machine, so mint locally and park it for the day the route
                // learns to pay.
                App.Logger?.Information("[ArcademyWallet] the server stamped the card but paid nothing - parking the frame");
                return new MintOutcome(MintVerdict.Queue, null);
            }

            if ((bool?)economy["duplicate"] == true)
            {
                App.Logger?.Information("[ArcademyWallet] mint {Mint} was already banked - the server paid it once",
                    (string?)frame["mintId"]);
            }

            if (!Retired(generation) && economy["wallet"] is JObject wallet)
            {
                Store(generation)?.AdoptServerWallet(wallet);
            }
            return new MintOutcome(MintVerdict.Banked, economy);
        }
        catch (OperationCanceledException)
        {
            App.Logger?.Information("[ArcademyWallet] the mint did not answer inside {S}s - parking the frame",
                PageWait.TotalSeconds);
            return new MintOutcome(MintVerdict.Queue, null);
        }
        catch (Exception ex)
        {
            App.Logger?.Information("[ArcademyWallet] mint failed: {E} - parking the frame", ex.Message);
            return new MintOutcome(MintVerdict.Queue, null);
        }
    }

    /// <summary>
    /// Drain <c>pendingMints</c>, oldest first. Each frame goes up carrying the SAME mintId it was
    /// parked with, so a frame that in fact reached the server the first time (and only lost its
    /// answer on the way back) is recognised and paid once.
    ///
    /// <para>Stops at the first frame nobody answers rather than working down the list: the wire is
    /// down, and the rest of the queue would only be a row of timeouts. THE TIER GATE STOPS IT THE
    /// SAME WAY and for a better reason - it is one condition for the whole launch, so the second
    /// frame would only be told what the first one already was. A frame the server refuses outright
    /// is dropped instead, because a queue that can never drain is just a leak.</para>
    ///
    /// <para>Called at launch, and again after any mint that landed (a mint landing is proof the
    /// wire and the gate are both open). Nothing here retries inside a session on its own.</para>
    /// </summary>
    private static async Task ReplayAsync(int generation)
    {
        var store = Store(generation);
        if (store == null) return;
        JArray pending;
        try { pending = store.PendingMints(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyWallet.Replay read: {E}", ex.Message); return; }
        if (pending.Count == 0) return;

        App.Logger?.Information("[ArcademyWallet] {N} parked mint(s) to replay", pending.Count);
        foreach (var token in pending.ToList())
        {
            if (Retired(generation)) return;
            // PendingMints() hands back only frames that still carry a mintId, so there is nothing
            // malformed to step over here.
            if (token is not JObject frame) continue;
            var mintId = (string?)frame["mintId"] ?? "";

            frame["queued"] = true;
            var outcome = await PostMintAsync(frame, generation).ConfigureAwait(false);
            if (outcome.Verdict == MintVerdict.Queue)
            {
                bool walled;
                lock (Gate) walled = _tierWalled;
                App.Logger?.Information(
                    walled
                        ? "[ArcademyWallet] the account cannot bank yet - {N} frame(s) stay parked"
                        : "[ArcademyWallet] the wire is still down - {N} frame(s) stay parked",
                    store.PendingMintCount());
                return;
            }

            store.DropMint(mintId);
            if (outcome.Verdict == MintVerdict.Banked)
            {
                App.Logger?.Information("[ArcademyWallet] banked a parked mint for {Game} on {Day}",
                    (string?)frame["game"], (string?)frame["localDay"]);
            }
        }
    }

    // ============================ the counter ============================

    /// <summary>
    /// POST one purchase. ONLINE ONLY by contract: the wallet lives on the server, so a press of
    /// Buy that cannot reach it is a refusal rather than a local spend that would be overwritten by
    /// the next pull anyway.
    /// </summary>
    private static async Task<BuyOutcome> PostBuyAsync(string sku, int generation)
    {
        if (!Identity(out var unifiedId, out var token)) return new BuyOutcome(false, false, "offline", null);

        using var cts = new CancellationTokenSource(PageWait);
        try
        {
            var payload = JsonConvert.SerializeObject(new { unified_id = unifiedId, sku });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}{PrizeBuyPath}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Auth-Token", token);

            using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var reply = Parse(text);

            if (!response.IsSuccessStatusCode)
            {
                // THE ACCOUNT LOCK. Another device is mid-write on this same wallet - a phone that
                // finished a class a second ago, most likely. It is the one status code here that
                // is genuinely momentary, and the counter has its own line for it so it never reads
                // as "you cannot afford that".
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    App.Logger?.Information("[ArcademyWallet] the wallet was busy - the counter says so");
                    return new BuyOutcome(true, false, "busy", null);
                }

                // Every refusal the counter has a line for arrives as a 200 with `ok:false`. A
                // status code instead means the account, not the purchase, was the problem, and the
                // room says the same thing it says when nobody answers.
                App.Logger?.Information("[ArcademyWallet] buy refused: {Status} {Body}",
                    (int)response.StatusCode, Truncate(text));
                return new BuyOutcome(false, false, "offline", null);
            }

            var wallet = reply?["wallet"] as JObject;
            bool ok = (bool?)reply?["ok"] == true;
            if (ok && wallet != null && !Retired(generation)) Store(generation)?.AdoptServerWallet(wallet);

            var reason = (string?)reply?["reason"];
            App.Logger?.Information("[ArcademyWallet] the counter answered '{Sku}': {Verdict}",
                sku, ok ? "sold" : reason ?? "refused");
            return new BuyOutcome(true, ok, reason, wallet);
        }
        catch (OperationCanceledException)
        {
            App.Logger?.Information("[ArcademyWallet] the counter did not answer inside {S}s", PageWait.TotalSeconds);
            return new BuyOutcome(false, false, "offline", null);
        }
        catch (Exception ex)
        {
            App.Logger?.Information("[ArcademyWallet] buy failed: {E}", ex.Message);
            return new BuyOutcome(false, false, "offline", null);
        }
    }

    // ============================ small guards ============================

    /// <summary>
    /// Is this 403 the TIER gate rather than the identity door? The two send a player to two
    /// different places and this lane treats them differently, so it reads the machine-readable
    /// fields rather than the status code: the tier body is <c>code:'arcademy_locked'</c> and
    /// carries <c>min_tier</c>, the identity body is <c>code:'arcademy_identity_unknown'</c> and
    /// carries neither. An unreadable body is treated as the identity refusal, which is the side
    /// that parks nothing - a queue is the thing to be careful about growing.
    /// </summary>
    private static bool IsTierRefusal(JObject? refusal)
    {
        if (refusal == null) return false;
        if (string.Equals((string?)refusal["code"], "arcademy_locked", StringComparison.Ordinal)) return true;
        return refusal["min_tier"] != null && refusal["min_tier"]!.Type != JTokenType.Null;
    }

    /// <summary>Take the server's draw for tonight, when it sent one worth having.</summary>
    private static void NotePayday(JObject? payday)
    {
        if (payday == null) return;
        var gameKey = (string?)payday["gameKey"];
        int mult = (int?)payday["mult"] ?? 1;
        if (mult < 1) mult = 1;
        lock (Gate) _serverPayday = new ArcademyEconomy.Payday(gameKey, mult);
    }

    /// <summary>The server ships its own copy of the shelf, generated from
    /// <see cref="ArcademyEconomy"/>. A wave that has drifted from this build's is not something
    /// this class can fix, but it IS the first thing to look at when a sku starts coming back
    /// unknown, so it goes in the log where the owner will find it.</summary>
    private static void NoteCatalogWave(JToken? wave)
    {
        int w = (int?)wave ?? 0;
        if (w == 0 || w == ArcademyEconomy.CurrentWave) return;
        App.Logger?.Information("[ArcademyWallet] the server's shelf is wave {Server}, this build is wave {Local}",
            w, ArcademyEconomy.CurrentWave);
    }

    /// <summary>
    /// The token door: <c>X-Auth-Token</c> plus this account's own <c>unified_id</c>, exactly the
    /// door <see cref="ArcademySyncService"/> pushes cards through. No identity, no traffic - and
    /// silently so: a player who has never logged in is not a problem to report, they are simply
    /// someone whose wallet lives on their own machine.
    /// </summary>
    private static bool Identity(out string unifiedId, out string token)
    {
        unifiedId = string.Empty;
        token = string.Empty;
        try
        {
            if (App.Settings?.Current?.OfflineMode == true) return false;
            var id = App.Settings?.Current?.UnifiedId;
            var auth = App.Settings?.Current?.AuthToken;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(auth)) return false;
            unifiedId = id;
            token = auth;
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyWallet.Identity: {E}", ex.Message);
            return false;
        }
    }

    private static ArcademyMetaStore? Store(int generation)
    {
        if (Retired(generation)) return null;
        lock (Gate) return _store;
    }

    private static bool Retired(int generation) =>
        generation >= 0 && Volatile.Read(ref _generation) != generation;

    private static void RaiseChanged(int generation)
    {
        Action? cb;
        lock (Gate) cb = _onWalletChanged;
        if (cb == null || Retired(generation)) return;
        try { cb(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyWallet.RaiseChanged: {E}", ex.Message); }
    }

    /// <summary>Fire-and-forget onto the thread pool with a catch-all: every entry point here is
    /// called from a UI-thread handler and none of them may pay for the network or die of it
    /// (CLAUDE.md async rules 6-8).</summary>
    private static void Run(Func<Task> work, string what)
    {
        try
        {
            _ = Task.Run(async () =>
            {
                try { await work().ConfigureAwait(false); }
                catch (Exception ex) { App.Logger?.Information("[ArcademyWallet] {What} failed: {E}", what, ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyWallet.Run({What}): {E}", what, ex.Message); }
    }

    private static JObject? Parse(string body)
    {
        try { return string.IsNullOrWhiteSpace(body) ? null : JObject.Parse(body); }
        catch (Exception ex)
        {
            App.Logger?.Information("[ArcademyWallet] unreadable reply: {E}", ex.Message);
            return null;
        }
    }

    private static string Truncate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= MaxLoggedBody ? s : s[..MaxLoggedBody] + "...";
    }

    // ============================ the frame ============================

    /// <summary>
    /// Build the mint frame for one graded finish. Everything on it is host-decided: the page
    /// reports what happened and every number here has already been clamped by the caller, so the
    /// server is being told the same story the local mint would have been told.
    ///
    /// <para><c>tzOffsetMinutes</c> is the wire's convention, not .NET's - minutes to ADD to local
    /// to reach UTC, so a player at UTC+2 sends -120.</para>
    /// </summary>
    public static JObject BuildMintFrame(string gameKey, string grade, bool zen, int streak,
        string localDay, string lever, bool lateSlipUsed, string seed)
    {
        int tz;
        try { tz = -(int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes); }
        catch { tz = 0; }

        return new JObject
        {
            ["kind"] = "class-ended",
            ["game"] = gameKey,
            ["grade"] = grade,
            ["zen"] = zen,
            ["localDay"] = localDay,
            ["tzOffsetMinutes"] = Math.Clamp(tz, -840, 840),
            ["streak"] = Math.Clamp(streak, 0, 3650),
            ["lateSlipUsed"] = lateSlipUsed,
            ["lever"] = lever,
            ["mintId"] = Guid.NewGuid().ToString("N"),
            ["seed"] = seed.Length <= 64 ? seed : seed[..64],
        };
    }

    /// <summary>
    /// Park a frame the server never took, so the next launch can carry it up.
    ///
    /// <para>ONLY ONCE THIS ACCOUNT HAS IMPORTED, and that condition is the whole double-pay guard.
    /// Before the import, the local wallet still IS the record and the import is what carries the
    /// night's earnings to the server; parking the frame as well would bank the same tickets twice,
    /// once as a balance and once as a mint. After the import the local wallet is only a cache, so
    /// the frame is the only copy of that night's money and it has to be kept.</para>
    /// </summary>
    public static void Park(JObject frame)
    {
        try
        {
            var store = Store(Volatile.Read(ref _generation));
            if (store == null) return;
            if (store.WalletImportedAt() == null)
            {
                App.Logger?.Information(
                    "[ArcademyWallet] this machine has not carried its wallet up yet - the local mint is the record, nothing parked");
                return;
            }
            store.QueueMint(frame);
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyWallet.Park: {E}", ex.Message); }
    }
}
