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
    /// ponytail: fx, media deals and the word voice run on WPF's null seams (BackRoomStubs: the four
    /// bundled loops, effects acked as skipped, words silent); haptics, Circe's tab slot lines and
    /// the race handoff are not wired yet.
    /// </summary>
    internal sealed partial class GameWindow
    {
        private BackRoomBridge? _backRoom;
        private AppSettings? _backRoomSettings;

        private void OpenBackRoom()
        {
            var roomAccount = BackRoomApi.AppIdentity()?.UnifiedId;
            (string UnifiedId, string Token)? RoomIdentity()
            {
                var current = BackRoomApi.AppIdentity();
                return current?.UnifiedId == roomAccount ? current : null;
            }
            BackRoomBridge? bridge = null;
            var api = new BackRoomApi(null, RoomIdentity, sp => bridge?.AdoptSp(sp, () => RoomIdentity() != null));
            _backRoom = bridge = new BackRoomBridge(new BackRoomBridge.Deps
            {
                Post = Post,
                Relay = api,
                Media = new NullBackRoomMedia(Loc.Get),
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

        private void CloseBackRoom()
        {
            if (_backRoomSettings != null) _backRoomSettings.PropertyChanged -= OnBackRoomSetting;
            _backRoomSettings = null;
            var b = _backRoom;
            _backRoom = null;
            b?.CloseNow();
        }

        private void OnBackRoomSetting(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.SkillPoints) && sender is AppSettings s)
                _backRoom?.OnSpChanged(s.SkillPoints, "earn");
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
                    // ponytail: the race handoff (WPF OnRoomMessage) - refused until Racing has a host.
                    if ((string?)o["game"] == "race") Post(new { type = "game-open-result", game = "race", ok = false, reason = "locked" });
                    return true;
                default:
                    _backRoom!.Handle(o);
                    return true;
            }
        }

        /// <summary>WPF BackRoomHostService.BuildInit, the fields this head has.</summary>
        private object BackRoomInit()
        {
            var s = CoreSettings.Current;
            var motion = s.MotionLevel;
            bool breakoutPage = Spec.Id is "breakout" or "breakoutdemo";
            return new
            {
                type = "init",
                protocol = BackRoomBridge.Protocol,
                racingTracks = Array.Empty<string>(),
                breakout = BreakoutEntitlementFor(Spec.Id),
                breakoutStandalone = breakoutPage,
                sp = s.SkillPoints,
                reduced = motion != MotionLevel.Full,
                invertLook = s.BackRoomInvertLook,
                welcomeSeen = s.BackRoomWelcomeSeen,
                motion = motion.ToString().ToLowerInvariant(),
                intensity = motion != MotionLevel.Full ? "calm" : "normal",
                lang = s.Language,
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
