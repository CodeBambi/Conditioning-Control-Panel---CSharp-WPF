using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The For You feed's host (WPF 7.1.5 Services/Fyp/FypHostService.cs): the page is
    /// <c>fyp/index.html</c> on the app's own origin, the library comes off <c>ccp.assets</c> through
    /// the same gate every game uses, and the rules (init payload, settings frames, the remote gate,
    /// the XP cap, stats, the sub library) are Core <see cref="FypHostService"/>.
    ///
    /// <para>Frames: ready -> init; stats-save; asset-meta; media-error; clip-viewed (dwell + capped
    /// XP); need-remote -> assets-append + online-status; library-remove -> library; probe-sub ->
    /// sub-probe (+ library); attention-hit; settings-changed; file-menu; close (the shell).</para>
    ///
    /// <para>Eye control (blink / eyesClosed / gaze frames, eyeStatus, the calibrate frame) is
    /// GameWindow.Fyp.Eye.cs. NOT on this head: ghost mode (WPF's DWM live-thumbnail mirror over a
    /// parked window); it answers the page with WPF's own "unavailable" frame so its toggle snaps back
    /// with the WPF reason line.</para>
    ///
    /// <para>BRIGHT LINE: remote batches and sub probes go straight from this machine to the
    /// provider, and only when <see cref="FypHostService.RemoteAllowed"/> says so.</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string FypId = "fyp";

        private FypMetaStore? _fypMeta;
        private bool _fypHooked;
        private int _fypRemoteInFlight;   // 0/1 via Interlocked
        private readonly HashSet<string> _fypProbesInFlight = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Test seams: the remote batch and the sub probe (default: the Core coordinator).</summary>
        internal Func<CancellationToken, Task<FypOnlineCoordinator.FeedBatch>>? FypFetchOverride;
        internal Func<string, CancellationToken, Task<SubProbe>>? FypProbeOverride;

        /// <summary>The last file the page's right-click resolved (null = refused); tests read it.</summary>
        internal string? FypLastMenuPath { get; private set; }

        /// <summary>WPF OpenFypFeed: premium is enforced at the door, out loud (the 8 s refusal
        /// names the feature), with today's free day counted by the same key.</summary>
        private static bool FypGate() => Services.TierGate.DemandPremium(Loc.Get("tab_fyp"), "fyp");

        internal static bool IsFypOpen()
        {
            lock (Open) return Open.Any(w => w.Spec.Id == FypId);
        }

        internal static void CloseFyp()
        {
            GameWindow[] all;
            lock (Open) all = Open.Where(w => w.Spec.Id == FypId).ToArray();
            foreach (var w in all)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("[Game] fyp close: {E}", ex.Message); }
            }
        }

        /// <summary>WPF PostInit: one init payload on page-ready.</summary>
        private void PostFypInit()
        {
            try
            {
                _fypMeta ??= new FypMetaStore();
                if (!_fypHooked)
                {
                    _fypHooked = true;
                    // WPF Close: the page's probes and every channel's rotation are kept.
                    Closed += (_, _) =>
                    {
                        DisableFypEyeControl();   // WPF Close :137: the camera goes back
                        try { _fypMeta?.Save(); } catch (Exception ex) { Log.Debug("[Game] fyp meta save: {E}", ex.Message); }
                        try { FypOnlineCoordinator.SaveAll(); } catch (Exception ex) { Log.Debug("[Game] fyp channels save: {E}", ex.Message); }
                    };
                }
                Post(FypHostService.BuildInit(CoreSettings.Current, FypAssets(), eyeControl: true));
                // WPF :239: persisted eye control re-arms the camera on every launch (with the same consent
                // check); the page shows the toggle on from the payload and eyeStatus corrects it on a refusal.
                if (CoreSettings.Current.FypEyeControl) EnableFypEyeControl();
            }
            catch (Exception ex) { Log.Warning("[Game] fyp: init failed: {E}", ex.Message); }
        }

        /// <summary>The library manifest with each url on the transport this window has (the
        /// <c>https://ccp.assets/</c> host behind the gate, or its loopback twin).</summary>
        private List<FypAssetManifest.Entry> FypAssets()
        {
            var list = FypAssetManifest.Build(_fypMeta ??= new FypMetaStore());
            var server = Web.AppServer ?? WebAssetServer.Shared;
            return list.Select(e => new FypAssetManifest.Entry
            {
                Id = e.Id, Url = server.AssetUrl(e.Id), Type = e.Type, Filename = e.Filename, Folder = e.Folder,
                DurationMs = e.DurationMs, Width = e.Width, Height = e.Height,
                Origin = e.Origin, PosterUrl = e.PosterUrl, SmallUrl = e.SmallUrl,
            }).ToList();
        }

        /// <summary>WPF FypHostService.OnPageMessage. True = claimed.</summary>
        private bool HandleFyp(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "ready":
                    IsReady = true;
                    PostFypInit();
                    return true;
                case "stats-save":
                    // The page owns the stats shape; the blob is persisted verbatim.
                    if (o["stats"] is JObject stats) FypHostService.SaveStats(stats);
                    return true;
                case "asset-meta":
                {
                    // Remote pools churn forever: never let their probes grow fyp_meta.json.
                    var id = (string?)o["id"];
                    if (!FypHostService.IsRemoteId(id))
                        _fypMeta?.Update(id, (long?)o["durationMs"], (int?)o["width"], (int?)o["height"]);
                    return true;
                }
                case "media-error":
                {
                    // Warning so it lands in bug reports (the "black tiles" class, #562).
                    var id = (string?)o["id"];
                    Log.Warning("[FYP] media-error for {Id}: code={Code} {Message}", id, (int?)o["code"] ?? 0, (string?)o["message"] ?? "");
                    if (!FypHostService.IsRemoteId(id)) _fypMeta?.RecordFailure(id);
                    return true;
                }
                case "clip-viewed":
                {
                    // Remote clips feed the channel-taste average before the XP cap gets a say.
                    var segId = (string?)o["segId"];
                    if (FypHostService.IsRemoteId(segId))
                    {
                        try { FypOnlineCoordinator.Fyp.RecordDwell(segId!, (long?)o["dwellMs"] ?? 0); }
                        catch (Exception ex) { Log.Debug("[Game] fyp: dwell record failed: {E}", ex.Message); }
                    }
                    if (FypHostService.AllowClipXp()) CoreProgression.AddXP(FypHostService.ClipXp, "Fyp");
                    return true;
                }
                case "attention-hit":
                    CoreProgression.AddXP(FypHostService.AttentionXp, "Fyp");
                    return true;
                case "need-remote":
                    _ = ServeFypRemoteBatch();
                    return true;
                case "library-remove":
                    FypHostService.RemoveLibrarySub(CoreSettings.Current, (string?)o["name"] ?? (string?)o["sub"]);
                    Post(FypHostService.LibraryFrame(CoreSettings.Current));
                    return true;
                case "probe-sub":
                    _ = ProbeFypSub((string?)o["sub"]);
                    return true;
                case "settings-changed":
                    ApplyFypSetting((string?)o["key"], o["value"]);
                    return true;
                case "file-menu":
                    ShowFypFileMenu((string?)o["id"]);
                    return true;
                case "calibrate":
                    // WPF :326: off the web-message callback, then the gaze calibration over the feed.
                    Dispatcher.UIThread.Post(() => _ = RunFypGazeCalibration());
                    return true;
                default:
                    return false;   // close / log / boot-error: the shell's
            }
        }

        private void ApplyFypSetting(string? key, JToken? value)
        {
            var s = CoreSettings.Current;
            switch (FypHostService.ApplySetting(s, key, value))
            {
                case FypHostService.SettingEffect.GhostOn:
                    // not ported: ghost mode. WPF's own "could not compose" answer: the toggle snaps
                    // back and the page says the feed stays solid.
                    Post(new { type = "clickThrough", on = false });
                    Post(new { type = "ghost-unavailable", reason = "not on this build" });
                    break;
                case FypHostService.SettingEffect.EyeControlOn:
                    EnableFypEyeControl();   // WPF :395
                    break;
                case FypHostService.SettingEffect.EyeControlOff:
                    DisableFypEyeControl();   // WPF :396
                    PostFypEyeStatus(null);
                    break;
                case FypHostService.SettingEffect.EyeGazeChanged:
                    SyncFypGazeSubscription();   // WPF :402
                    PostFypEyeStatus(null);
                    break;
            }
        }

        /// <summary>
        /// WPF ServeRemoteBatch: one batch per ask, single-flight (the page re-asks after every
        /// append). Refused, quietly, unless consent is given and the source is not the library.
        /// </summary>
        internal async Task ServeFypRemoteBatch()
        {
            var s = CoreSettings.Current;
            if (!FypHostService.RemoteAllowed(s))
            {
                Log.Debug("[Game] fyp: need-remote refused (source {Fyp}/{App}, consent {Consent})", s.FypSource, s.MediaSource, s.HasRemoteMediaConsent);
                return;
            }
            if (Interlocked.CompareExchange(ref _fypRemoteInFlight, 1, 0) != 0) return;
            try
            {
                var fetch = FypFetchOverride ?? (ct => FypOnlineCoordinator.Fyp.FetchBatchDetailedAsync(ct));
                var batch = await Task.Run(() => fetch(CancellationToken.None)).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsClosedOrClosing) return;   // feed closed while fetching
                    foreach (var frame in FypHostService.BatchFrames(batch)) Post(frame);
                });
            }
            catch (Exception ex) { Log.Warning("[Game] fyp: remote batch failed: {E}", ex.Message); }
            finally { Interlocked.Exchange(ref _fypRemoteInFlight, 0); }
        }

        /// <summary>WPF ProbeCustomSub: is a typed subreddit real? One upstream request, then
        /// sub-probe back; a found sub is committed here, never by the page.</summary>
        internal async Task ProbeFypSub(string? rawSub)
        {
            var clean = FypOnlineCoordinator.SanitizeSub(rawSub);
            if (clean == null) { Post(FypHostService.InvalidProbeFrame(rawSub)); return; }
            // The same consent every remote fetch on this head asks for: a probe is a request to the provider.
            if (!CoreSettings.Current.HasRemoteMediaConsent)
            {
                Log.Debug("[Game] fyp: probe-sub refused (no remote media consent)");
                Post(new { type = "sub-probe", sub = clean, ok = false, videoCount = (int?)null, error = "consent" });
                return;
            }
            lock (_fypProbesInFlight) { if (!_fypProbesInFlight.Add(clean)) return; }
            try
            {
                var run = FypProbeOverride ?? ((sub, ct) => FypOnlineCoordinator.ProbeSubAsync(sub, ct));
                var probe = await Task.Run(() => run(clean, CancellationToken.None)).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsClosedOrClosing) return;   // feed closed while probing
                    Post(FypHostService.CommitProbe(CoreSettings.Current, clean, probe));
                    // The pill row is built from the library, so it has to hear about a new keeper.
                    if (probe.Ok) Post(FypHostService.LibraryFrame(CoreSettings.Current));
                });
            }
            catch (Exception ex) { Log.Warning("[Game] fyp: sub probe failed: {E}", ex.Message); }
            finally { lock (_fypProbesInFlight) _fypProbesInFlight.Remove(clean); }
        }

        /// <summary>WPF FypFileMenu.Show: right-click on a library item. The id is resolved and
        /// checked host-side; anything else gets no menu.</summary>
        private void ShowFypFileMenu(string? id)
        {
            var path = FypFileMenu.ResolveLocal(CorePaths.EffectiveAssets, id);
            if (path != null && !System.IO.File.Exists(path)) path = null;
            FypLastMenuPath = path;
            if (path == null) return;
            try
            {
                var fg = Brushes.White;
                var open = new MenuItem { Header = Loc.Get("fyp_menu_open_file"), Foreground = fg };
                open.Click += (_, _) => { if (System.IO.File.Exists(path)) ExternalOpener.Open(path); };
                var reveal = new MenuItem { Header = Loc.Get("fyp_menu_show_in_folder"), Foreground = fg };
                reveal.Click += (_, _) =>
                {
                    var dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) ExternalOpener.Open(dir);
                };
                var menu = new ContextMenu { Placement = PlacementMode.Pointer };
                menu.Items.Add(open);
                menu.Items.Add(reveal);
                menu.Open(Web);
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: file menu failed: {E}", ex.Message); }
        }
    }
}
