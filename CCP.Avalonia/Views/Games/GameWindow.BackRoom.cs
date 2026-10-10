using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games.BackRoom;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Back Room host (WPF Services/BackRoom/BackRoomHostService.CreateBridge): the ported
    /// <see cref="BackRoomBridge"/> (CONTRACT section 2: every station-request gets exactly one
    /// station-result, idem keys, reply guard, close ladder) over <see cref="BackRoomApi"/> (the
    /// whitelisted /v2/backroom relay, identity pinned to the account that opened the room). THE
    /// BANK: the server's settled <c>sp</c> is the debited receipt, adopted into SkillPoints exactly
    /// as WPF does (BackRoomApi.Read -> AdoptSp -> SetSp); a balance change from elsewhere goes to
    /// the page as <c>balance</c>. The Breakout doors ride the same bridge (they are Back Room pages).
    /// media deals are WPF BackRoomMedia (local folders + the warm Scrolller pool, urls on loopback); fx run on
    /// BackRoomFxHead (the port's overlays; a primitive with none is acked skipped), words on BackRoomVoice
    /// (recorded clips only), haptics on BackRoomHapticDirector, slot lines on Circe's tab, the feature day
    /// log through NoteEvent. The race handoff still opens its own window.
    /// </summary>
    internal sealed partial class GameWindow
    {
        private BackRoomBridge? _backRoom;
        private bool _openingRace;
        private AppSettings? _backRoomSettings;

        /// <summary>WPF BackRoomHostService.Media: one live deal for every room and Breakout window (the
        /// warm Scrolller pool outlives a window, as in WPF), its urls moved onto the loopback server.</summary>
        private static IBackRoomMedia? _roomMedia;
        internal static IBackRoomMedia RoomMedia => _roomMedia ??= new LoopbackBackRoomMedia(
            new BackRoomMedia((key, fallback) => { var s = Loc.Get(key); return string.IsNullOrWhiteSpace(s) || s == key ? fallback : s; }),
            rel => Platform.WebAssetServer.Shared.Url(rel), rel => Platform.WebAssetServer.Shared.AssetUrl(rel));

        private void OpenBackRoom()
        {
            // WPF LaunchCore: start filling the remote picture pool now (a no-op for a local-only room).
            try { RoomMedia.WarmForRoomOpen(); } catch (Exception ex) { Log.Debug("[BackRoom] warm: {E}", ex.Message); }
            var roomAccount = BackRoomApi.AppIdentity()?.UnifiedId;
            (string UnifiedId, string Token)? RoomIdentity()
            {
                var current = BackRoomApi.AppIdentity();
                return current?.UnifiedId == roomAccount ? current : null;
            }
            // IB6: this window's own door onto the shared fx head (the room and Breakout can be open together).
            var fx = BackRoomFxHead.Attach(this);
            BackRoomBridge? bridge = null;
            var api = new BackRoomApi(null, RoomIdentity, sp => bridge?.AdoptSp(sp, () => RoomIdentity() != null));
            _backRoom = bridge = new BackRoomBridge(new BackRoomBridge.Deps
            {
                Post = Post,
                Relay = api,
                Media = RoomMedia,
                // WPF CreateBridge :421-431. Recorded clips only (no synthetic speech): a word with no clip stays silent.
                Fx = fx,
                Voice = RoomVoiceFactory(() => IsBreakoutPage(Spec.Id)),
                Haptic = BackRoomHapticDirector.OnHaptic,
                NoteEvent = key => global::ConditioningControlPanel.Services.FeatureDayLogService.Current?.Note(key),   // WPF CreateBridge :427
                SlotLanded = SlotLanded,
                SetOption = option => Dispatcher.UIThread.Post(() =>
                {
                    if (_backRoom != bridge) return;
                    try { BackRoomWire.ApplyRoomOption(CoreSettings.Current, option, IsBreakoutPage(Spec.Id)); CoreSettings.Save(); }
                    catch (Exception ex) { Log.Debug("[BackRoom] room-option: {E}", ex.Message); }
                }),
                BuildInit = BackRoomInit,
                CloseWindow = () => Dispatcher.UIThread.Post(() => { if (!IsClosedOrClosing) Close(); }),
                Schedule = ScheduleOnUi,
                SetSp = sp => { if (_backRoom == bridge) { CoreSettings.Current.SkillPoints = sp; CoreSettings.Save(); } },
                OnUi = a => Dispatcher.UIThread.Post(() => { try { a(); } catch (Exception ex) { Log.Debug("[BackRoom] OnUi: {E}", ex.Message); } }),
                OffUi = work => System.Threading.Tasks.Task.Run(work),
                Log = msg => Log.Debug("[BackRoom] {Msg}", msg),
            });
            _backRoomSettings = CoreSettings.Current;
            _backRoomSettings.PropertyChanged += OnBackRoomSetting;
        }

        /// <summary>The spoken word (10.21). Tests swap it for the null object.</summary>
        internal static Func<Func<bool>, IBackRoomVoice> RoomVoiceFactory = breakout => new BackRoomVoice(breakout);

        /// <summary>WPF ChasterHooks.SlotLanded (10.24): the server's own line for an outcome that really
        /// landed. The melt line books the melt row, the jackpot line wipes the tab; inert until the tab is
        /// on, linked and the row is switched on (the Core service's own gates). No new price rule here.</summary>
        internal static void SlotLanded(string? line)
        {
            if (Platform.ChasterHead.Service is not { } chaster) return;
            try
            {
                switch (BackRoomWire.SlotLineRow(line))
                {
                    case "melt": chaster.Note("melt"); break;
                    case ConditioningControlPanel.Services.Chaster.CircesTab.JackpotEventId: chaster.Wipe(); break;
                }
            }
            catch (Exception ex) { Log.Debug("[BackRoom] slot landed: {E}", ex.Message); }
        }

        private void CloseBackRoom()
        {
            if (_backRoomSettings != null) _backRoomSettings.PropertyChanged -= OnBackRoomSetting;
            _backRoomSettings = null;
            var b = _backRoom;
            _backRoom = null;
            b?.CloseNow();   // Finish -> CancelFx: every effect, word and pulse the room started stops with the window
            BackRoomFxHead.Detach(this);   // after the cancel: the last window out stops everything, an earlier one only its own holds
        }

        private void OnBackRoomSetting(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.SkillPoints) && sender is AppSettings s)
                _backRoom?.OnSpChanged(s.SkillPoints, "earn");
            // WPF OnSettingChanged :694: a motion, intensity, invert look, media or audio change while the
            // room is open repaints the page from one full settings frame.
            if (e.PropertyName != null && BackRoomWire.SettingsFrameProperties.Contains(e.PropertyName))
                _backRoom?.PushSettings(BackRoomSettingsMessage());
            // WPF OnSettingsChanged: a niche / source / consent change makes the warm pool stale.
            if (e.PropertyName is nameof(AppSettings.BackRoomMediaSubs) or nameof(AppSettings.BackRoomMediaSubsOff)
                or nameof(AppSettings.BackRoomMediaSource) or nameof(AppSettings.MediaSource)
                or nameof(AppSettings.RemoteMediaConsented) or nameof(AppSettings.FypOnlineConsented))
            {
                try { RoomMedia.ReleaseWarmPool(); RoomMedia.WarmForRoomOpen(); }
                catch (Exception ex) { Log.Debug("[BackRoom] repool: {E}", ex.Message); }
            }
        }

        /// <summary>The page frames the room owns. Shell frames (log, boot-error) fall through.</summary>
        private bool HandleBackRoom(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "log":
                case "boot-error":
                case "heartbeat":
                case "fullscreen-set":
                    return false;
                case "ready":
                    IsReady = true;
                    _backRoom!.OnReady();
                    return true;
                case "game-open":
                    // WPF BackRoomHostService.OnRoomMessage: the door answers with the real ownership
                    // result ("locked" without a racing purchase, "busy" while a race is up), then the
                    // room winds down and the race opens once it has closed.
                    if ((string?)o["game"] != "race" || _openingRace) return true;
                    var refusal = RaceWindow.Refusal();
                    Post(new { type = "game-open-result", game = "race", ok = refusal == null, reason = refusal });
                    if (refusal != null) return true;
                    _openingRace = true;
                    // ponytail: WPF hands the race the room's own window and returns to the room after
                    // (?raceReturn=1); this head opens the race in its own window and ends there.
                    Closed += (_, _) => { if (RaceWindow.Refusal() == null) RaceWindow.Launch(); };
                    _backRoom!.RequestClose("race");
                    return true;
                default:
                    _backRoom!.Handle(o);
                    return true;
            }
        }

        /// <summary>WPF BackRoomHostService.SettingsMessage.</summary>
        internal object BackRoomSettingsMessage()
            => BackRoomWire.SettingsMessage(CoreSettings.Current, IsBreakoutPage(Spec.Id), BreakoutEntitlementFor(Spec.Id));

        /// <summary>WPF BackRoomHostService.BuildInit.</summary>
        internal object BackRoomInit()
        {
            var s = CoreSettings.Current;
            var motion = s.MotionLevel;
            bool breakoutPage = IsBreakoutPage(Spec.Id);
            return new
            {
                type = "init",
                protocol = BackRoomBridge.Protocol,
                racingTracks = RaceWindow.OwnedTracks(),
                breakout = BreakoutEntitlementFor(Spec.Id),
                breakoutStandalone = breakoutPage,
                sp = s.SkillPoints,
                reduced = motion != MotionLevel.Full,
                invertLook = s.BackRoomInvertLook,
                welcomeSeen = s.BackRoomWelcomeSeen,
                motion = BackRoomWire.MotionWire(motion),
                intensity = BackRoomWire.IntensityWire(s, motion),
                lang = s.Language,
                gates = BackRoomWire.GatesWire(s), media = BackRoomWire.MediaWire(s), audio = BackRoomWire.AudioWire(s, breakoutPage),
                intensityChoice = BackRoomWire.IntensityChoiceWire(s),
                lex = BackRoomWire.Lex(LocalizationManager.Instance.KeysWithPrefix(BackRoomWire.LexPrefix), Loc.Get),
                stations = BackRoomApi.Ops.Keys.ToArray(),
                open = (bool?)null,
                identity = new
                {
                    unifiedId = CoreAccount.UnifiedUserId ?? "",
                    displayName = CoreAccount.DisplayName ?? "",
                    appVersion = CoreReleaseContent.AppVersion,
                },
            };
        }

        private static Action ScheduleOnUi(TimeSpan delay, Action fn)
        {
            DispatcherTimer? t = null;
            bool cancelled = false;
            Dispatcher.UIThread.Post(() =>
            {
                if (cancelled) return;
                t = new DispatcherTimer { Interval = delay };
                t.Tick += (_, _) => { t?.Stop(); fn(); };
                t.Start();
            });
            return () => Dispatcher.UIThread.Post(() => { cancelled = true; t?.Stop(); });
        }
    }
}
