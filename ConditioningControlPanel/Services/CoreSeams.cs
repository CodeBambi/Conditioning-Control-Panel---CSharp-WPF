using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Billboard.Board;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.Stakes;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The WPF answers to the seams of rules that live once in CCP.Core (Lobby, PvP stakes, Goon
    /// online media, the Racing Thoughts door, the Tonight Board picture). Each seam gets exactly what the WPF 7.1.5 copy of the class read
    /// directly (BackRoomApi, App.Chaster, the dispatcher, WPF imaging), so moving the class to
    /// Core changes nothing a player can see. Idempotent; called first thing in App startup and
    /// once by the test assembly.
    /// </summary>
    internal static class CoreSeams
    {
        private static bool _wired;

        public static void Wire()
        {
            if (_wired) return;
            _wired = true;

            // The account door and the proxy.
            LobbyWire.DefaultIdentity = () => BackRoomApi.AppIdentity();
            LobbyWire.DefaultBaseUrl = () => BackRoomApi.BaseUrl;
            StakeApi.DefaultIdentity = () => BackRoomApi.AppIdentity();
            StakeApi.DefaultBaseUrl = () => BackRoomApi.BaseUrl;

            // PvP stakes: Circe's Tab and the UI thread.
            StakeBridge.ChasterState = () => App.Chaster is { } c ? (c.IsLinked, c.SafetyHoldRemaining) : null;
            StakeBridge.OnUi = apply =>
            {
                var d = App.Current?.Dispatcher;
                if (d == null || d.CheckAccess()) apply(); else d.Invoke(apply);
            };
            StakeSettlement.BookTime = (row, seconds) => App.Chaster?.NoteSeconds(row, seconds).AppliedSeconds ?? 0;
            StakeSettlement.Account = () => BackRoomApi.AppIdentity()?.UnifiedId;
            StakeSettlement.OnUi = write => App.Current?.Dispatcher?.BeginInvoke(write);

            // Goon online media: the app's downloader into {assets}/.temp.
            GoonGame.GoonOnlineMedia.Materialize = (url, ct) => Fyp.Online.RemoteMediaCache.MaterializeAsync(url, ct, ownerReleases: true);
            GoonGame.GoonOnlineMedia.ReleaseTempFile = Fyp.Online.RemoteMediaCache.ReleaseTempFile;

            // Racing Thoughts: which original tracks this account owns.
            Race.RacingAccess.IsGrantedProvider = Prizes.PrizeGrants.IsGranted;

            // The Tonight Board picture: WPF imaging, any thread.
            BoardPicture.PngDecoder = DecodePng;

            WireAwareness();
        }

        /// <summary>
        /// Awareness (observer, ledger, routing, arbiter) lives once in Core. These are the probes,
        /// the timer and the App statics the WPF 7.1.5 copies named directly; every lambda reads
        /// its static lazily, since most are null until late in startup.
        /// </summary>
        private static void WireAwareness()
        {
            Awareness.AwarenessPlatform.ForegroundProbeFactory = () => new Awareness.Win32ForegroundProbe();
            Awareness.AwarenessPlatform.InputProbeFactory = () => new Awareness.Win32InputProbe();
            Awareness.AwarenessPlatform.MicrophoneProbeFactory = () => new Awareness.WasapiMicrophoneProbe();
            Awareness.AwarenessPlatform.MediaWatcherFactory = () => new Awareness.SmtcMediaWatcher();
            Awareness.AwarenessPlatform.AppStateProbeFactory = () => new Awareness.AppStateProbe();
            Awareness.AwarenessPlatform.PollTimerFactory = (interval, tick) => new AwarenessPollTimer(interval, tick);

            Awareness.AwarenessHost.Ai = () => App.Ai;
            Awareness.AwarenessHost.CurrentServiceName = () => App.WindowAwareness?.CurrentServiceName;
            Awareness.AwarenessHost.RecentForegroundApps = () => App.KeywordTriggers?.GetRecentForegroundApps();
            Awareness.AwarenessHost.MuteKeywordEcho = (line, ms) => App.KeywordTriggers?.MuteKeywordEcho(line, ms);
            Awareness.AwarenessHost.RaiseAwarenessBark = frame => App.Bark?.RaiseAwarenessBark(frame) ?? false;
            Awareness.AwarenessHost.NotifyExternalLineSpoken = () => App.Bark?.NotifyExternalLineSpoken();
            Awareness.AwarenessHost.HasAvatar = () => App.AvatarWindow != null;
            Awareness.AwarenessHost.IsCompanionBusy = ms => App.AvatarWindow?.IsCompanionBusy(ms) ?? false;
            Awareness.AwarenessHost.ForegroundTitle = ForegroundTitle;
            Awareness.AwarenessHost.SpeakAwarenessLine = (line, doubleBounce) =>
            {
                // The WPF copy refused with no dispatcher or a closing one, then with no avatar.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return false;
                var avatar = App.AvatarWindow;
                if (avatar == null) return false;
                avatar.SpeakAwarenessLine(line, doubleBounce);
                return true;
            };
        }

        private static string? ForegroundTitle()
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return null;
            var sb = new System.Text.StringBuilder(512);
            return GetWindowText(handle, sb, sb.Capacity) <= 0 ? null : sb.ToString();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        /// <summary>The observer's poll: a DispatcherTimer at Normal priority, as the WPF copy
        /// built it. With no dispatcher there is no polling (the ledger stays live), and a tick
        /// that lands after the dispatcher began shutting down is dropped, as it was.</summary>
        private sealed class AwarenessPollTimer : IDisposable
        {
            private readonly System.Windows.Threading.DispatcherTimer? _timer;
            private readonly Action _tick;

            public AwarenessPollTimer(TimeSpan interval, Action tick)
            {
                _tick = tick;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    App.Logger?.Warning("AwarenessObserver: no dispatcher - ledger is live, polling is not");
                    return;
                }
                _timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, dispatcher) { Interval = interval };
                _timer.Tick += OnTick;
                _timer.Start();
            }

            private void OnTick(object? sender, EventArgs e)
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                _tick();
            }

            public void Dispose()
            {
                if (_timer == null) return;
                _timer.Stop();
                _timer.Tick -= OnTick;
            }
        }

        /// <summary>PNG bytes to straight Bgra32 pixels (0xAARRGGBB ints, row-major). The size
        /// gate runs before the format conversion, as it did when this lived in BoardPicture.</summary>
        private static (int[] Pixels, int Width, int Height)? DecodePng(byte[] png)
        {
            using var ms = new MemoryStream(png, writable: false);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            BitmapSource src = decoder.Frames[0];
            if (src.PixelWidth <= 0 || src.PixelHeight <= 0) return null;
            if (src.PixelWidth > BoardWire.MaxFrameWidth * BoardWire.MaxFrames || src.PixelHeight > 512) return null;
            if (src.Format != PixelFormats.Bgra32)
                src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var px = new int[w * h];
            src.CopyPixels(px, w * 4, 0);
            return (px, w, h);
        }
    }
}
