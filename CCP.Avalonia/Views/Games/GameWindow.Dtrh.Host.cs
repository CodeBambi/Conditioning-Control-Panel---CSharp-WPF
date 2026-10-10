using System;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games.Dtrh;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Down the Rabbit Hole host, the desktop half (WPF Services/Chaos/DtrhHostService.cs 7.1.5):
    /// init's volume / persona / mod content (:227), favorites and asset-stats (:254, :352), the native
    /// sfx bank silent while she speaks (:268), payload-state when a mandatory video covers the page
    /// (:866, :906), world freeze and dive mute over that video (:803, :831), the Loom (:357, :405),
    /// report-bug (:390) and the crash sentinel (:327).
    /// DtRH is a trance game: nothing here adds a sound, a flash or a shake WPF does not have.
    /// The haptic-state feed and the tap before each bark drive Core DtrhHapticDirector.
    /// Barks route through RouteDtrhBark (Director partial): a recorded line or nothing.
    /// not ported: the freeze's pause of a spoken companion line, the session stats store, the tray tuck.
    /// </summary>
    internal sealed partial class GameWindow
    {
        private bool _vnSpeaking, _worldFrozen, _diveMuted, _dtrhVideoHooked, _dtrhLoomHooked;
        private MandatoryVideoScheduler? _dtrhVideo;

        /// <summary>Test seams: the covering video's pause and mute (the real overlay by default).</summary>
        internal static Action<bool> DtrhVideoPause = on => MandatoryVideoOverlay.Instance.SetExternalPause(on);
        internal static Action<bool> DtrhVideoMute = on => MandatoryVideoOverlay.Instance.SetExternalMute(on);

        private void OpenDtrhHost()
        {
            try
            {
                var server = WebAssetServer.Shared;
                server.ModRoot ??= DtrhModContent.ModDtrhRoot;   // WPF :139: the ccp.mod virtual host
                server.Hosts.TryAdd(ArcSpiralsHost, () => DtrhLoomStore.SpiralsFolder);
            }
            catch (Exception ex) { Log.Debug("DtrhHost: hosts: {E}", ex.Message); }
            HookDtrhVideo(MandatoryVideoOverlay.Instance.Scheduler);
            // WPF Launch (:180): the descent's haptics live from here to CloseDtrhHost.
            try { global::ConditioningControlPanel.Services.Haptics.DtrhHapticDirector.OnLaunch(_testMode); } catch (Exception ex) { Log.Debug("DtrhHost haptics launch: {E}", ex.Message); }
            TuckShellForDtrh();
        }

        /// <summary>WPF HookVideoEvents(true): a mandatory video fully covers the game.</summary>
        internal void HookDtrhVideo(MandatoryVideoScheduler scheduler)
        {
            UnhookDtrhVideo();
            _dtrhVideo = scheduler;
            scheduler.VideoStarted += OnDtrhVideoStarted;
            scheduler.VideoEnded += OnDtrhVideoEnded;
            _dtrhVideoHooked = true;
        }

        private void UnhookDtrhVideo()
        {
            if (!_dtrhVideoHooked || _dtrhVideo == null) return;
            _dtrhVideo.VideoStarted -= OnDtrhVideoStarted;
            _dtrhVideo.VideoEnded -= OnDtrhVideoEnded;
            _dtrhVideoHooked = false;
            _dtrhVideo = null;
        }

        internal void OnDtrhVideoStartedForTest() => OnDtrhVideoStarted();
        internal void OnDtrhVideoEndedForTest() => OnDtrhVideoEnded();

        private void OnDtrhVideoStarted()
        {
            NoteDtrhVideoShown();   // session telemetry: a video was shown this run
            // WPF :890: the covering video's own haptics own the device until it closes.
            try { global::ConditioningControlPanel.Services.Haptics.DtrhHapticDirector.OnVideoCovering(true); } catch (Exception ex) { Log.Debug("DtrhHost haptics video: {E}", ex.Message); }
            Post(new { type = "payload-state", kind = "video", on = true });
        }

        private void OnDtrhVideoEnded()
        {
            Post(new { type = "payload-state", kind = "video", on = false });
            try { global::ConditioningControlPanel.Services.Haptics.DtrhHapticDirector.OnVideoCovering(false); } catch (Exception ex) { Log.Debug("DtrhHost haptics video: {E}", ex.Message); }   // WPF :929
            // the video window had the keyboard; hand it back to the game
            try { if (!IsClosedOrClosing) Dispatcher.UIThread.Post(() => { if (!IsClosedOrClosing) Web.Focus(); }); }
            catch (Exception ex) { Log.Debug("DtrhHost: focus back: {E}", ex.Message); }
        }

        /// <summary>WPF OnPageReady's init fields the shell init does not carry.</summary>
        private void DtrhInitExtras(JObject init)
        {
            int volume = 100;
            try { volume = CoreSettings.Current?.MasterVolume ?? 100; } catch { }
            init["settings"] = new JObject { ["masterVolume"] = volume };
            string modId = "builtin-sissyhypno";
            try { modId = CoreMods.ActiveModId ?? modId; } catch { }
            init["modId"] = modId;   // the page's VN portrait set + tint
            var mod = DtrhModContent.BuildInitPayload();
            init["modContent"] = mod == null ? JValue.CreateNull() : JToken.FromObject(mod);
            init["m2Test"] = _testMode;
        }

        /// <summary>WPF OnPageReady after the manifest: the Loom's saved spirals, then the favorites.</summary>
        private void PostDtrhAfterManifest()
        {
            PostDtrhLoomList();
            if (!_dtrhLoomHooked) { DtrhLoomStore.Changed += OnDtrhLoomChanged; _dtrhLoomHooked = true; }
            try
            {
                var favorites = DtrhAssetStatsStore.TopAssets(12);
                if (favorites.Count > 0) Post(new { type = "favorites", names = favorites });
            }
            catch (Exception ex) { Log.Debug("DtrhHost favorites post failed: {E}", ex.Message); }
        }

        private void OnDtrhLoomChanged()
        {
            try { Dispatcher.UIThread.Post(() => { if (!IsClosedOrClosing && IsReady) PostDtrhLoomList(); }); }
            catch (Exception ex) { _ = ex; }
        }

        private void PostDtrhLoomList()
        {
            try
            {
                Post(new
                {
                    type = "loom-list",
                    spirals = DtrhLoomStore.List().Select(s => new
                    {
                        slug = s.Slug,
                        url = WebAssetServer.Shared.HostUrl(ArcSpiralsHost, "loom_" + s.Slug + ".gif"),
                        @params = ParseLoomParams(s.ParamsJson),
                    }),
                });
            }
            catch (Exception ex) { Log.Debug("DtrhHost.PostLoomList: {E}", ex.Message); }
        }

        private static JObject? ParseLoomParams(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); } catch { return null; }
        }

        private void HandleDtrhHostFrame(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "vn-speaking":
                    _vnSpeaking = (bool?)o["on"] ?? false;
                    break;
                case "sfx":
                    if (_vnSpeaking) break;   // VN owns the mix: stingers stay silent while she speaks
                    ChaosSfx.PlayFrame((string?)o["name"], (float?)o["scale"]);
                    break;
                case "freeze-state":
                    ApplyWorldFreeze((bool?)o["on"] ?? false);
                    break;
                case "mute-state":
                    ApplyDiveMute((bool?)o["on"] ?? false);
                    break;
                case "asset-stats":
                    try { DtrhAssetStatsStore.Merge(o); } catch (Exception ex) { Log.Debug("DtrhHost asset-stats: {E}", ex.Message); }
                    break;
                case "loom-save":
                {
                    var (ok, slug, error) = DtrhLoomStore.Save(
                        (string?)o["name"], (string?)o["gifBase64"], o["params"], (bool?)o["overwrite"] ?? false);
                    Post(new { type = "loom-result", op = "save", ok, slug, error });
                    if (ok) PostDtrhLoomList();
                    break;
                }
                case "loom-delete":
                {
                    var slug = (string?)o["slug"];
                    var (ok, error) = DtrhLoomStore.Delete(slug);
                    Post(new { type = "loom-result", op = "delete", ok, slug, error });
                    if (ok) PostDtrhLoomList();
                    break;
                }
                case "loom-reveal":
                {
                    var path = DtrhLoomStore.GifPathFor((string?)o["slug"]);
                    if (path != null)
                    {
                        try { if (System.IO.Path.GetDirectoryName(path) is { Length: > 0 } dir) ExternalOpener.Open(dir); }
                        catch (Exception ex) { Log.Debug("DtrhHost: reveal failed: {E}", ex.Message); }
                    }
                    break;
                }
                case "report-bug":
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { if (!IsClosedOrClosing) _ = new Windows.BugReportWindow().ShowDialogSafe(this); }
                        catch (Exception ex) { Log.Warning("DtrhHost.report-bug: {E}", ex.Message); }
                    });
                    break;
                case "bark":
                    // Haptics tap FIRST (WPF :342): the moment happened whether or not a voice line
                    // plays over it; the vn-speaking gate inside RouteDtrhBark only guards the mix.
                    try { global::ConditioningControlPanel.Services.Haptics.DtrhHapticDirector.OnGameEvent(o); } catch (Exception ex) { Log.Debug("DtrhHost haptics bark: {E}", ex.Message); }
                    RouteDtrhBark(o);
                    break;
                case "haptic-state":
                    // The page's ~2 s depth / melt feed for the director's ambient floor (WPF :348).
                    try { global::ConditioningControlPanel.Services.Haptics.DtrhHapticDirector.OnHapticState(o); } catch (Exception ex) { Log.Debug("DtrhHost haptic-state: {E}", ex.Message); }
                    break;
            }
        }

        /// <summary>WPF run-started (:316): never carry a stale duck or freeze into a run; arm the sentinel.</summary>
        private void OnDtrhRunStartedExtras(string difficulty)
        {
            _vnSpeaking = false;
            ApplyWorldFreeze(false);
            ResetDtrhRunMetrics();
            // WPF :332, test mode included: the director's own Ready gate keeps a test run silent.
            try { global::ConditioningControlPanel.Services.Haptics.DtrhHapticDirector.OnRunStarted(); } catch (Exception ex) { Log.Debug("DtrhHost haptics run-started: {E}", ex.Message); }
            if (_testMode) return;
            DtrhBarkRunStarted(difficulty);
            try { ChaosCrashSentinel.Mark($"mode=dtrh-web diff={difficulty}"); } catch (Exception ex) { _ = ex; }
        }

        /// <summary>freeze-state {on}: the in-world Freeze bubble stops the field, so a covering video
        /// stops with it. Idempotent; force-released on run end and teardown.</summary>
        internal void ApplyWorldFreeze(bool on)
        {
            if (on == _worldFrozen) return;
            _worldFrozen = on;
            try { global::ConditioningControlPanel.Services.Haptics.DtrhHapticDirector.OnWorldFreeze(on); } catch (Exception ex) { Log.Debug("DtrhHost haptics freeze: {E}", ex.Message); }   // WPF :838
            try { DtrhVideoPause(on); } catch (Exception ex) { Log.Debug("DtrhHost.ApplyWorldFreeze: {E}", ex.Message); }
        }

        /// <summary>mute-state {on}: the dive's in-page master mute also silences a covering native video.</summary>
        internal void ApplyDiveMute(bool on)
        {
            if (on == _diveMuted) return;
            _diveMuted = on;
            try { DtrhVideoMute(on); } catch (Exception ex) { Log.Debug("DtrhHost.ApplyDiveMute: {E}", ex.Message); }
        }

        /// <summary>WPF DisposeAll: nothing native stays paused or silent after the window dies.</summary>
        private void CloseDtrhHost()
        {
            UnhookDtrhVideo();
            // WPF DisposeAll (:1091): our layer goes to zero the moment the window dies. Panic closes
            // this window too, after CoreHaptics.Service.PanicStop() has already silenced the device.
            try { global::ConditioningControlPanel.Services.Haptics.DtrhHapticDirector.OnClosed(); } catch (Exception ex) { Log.Debug("DtrhHost haptics closed: {E}", ex.Message); }
            RestoreShellAfterDtrh();
            if (_dtrhLoomHooked) { try { DtrhLoomStore.Changed -= OnDtrhLoomChanged; } catch { } _dtrhLoomHooked = false; }
            if (_worldFrozen) { _worldFrozen = false; try { DtrhVideoPause(false); } catch { } }
            // Unconditional (WPF #1103): the flag and the video's own mute can get out of step.
            _diveMuted = false;
            try { DtrhVideoMute(false); } catch { }
        }
    }
}
