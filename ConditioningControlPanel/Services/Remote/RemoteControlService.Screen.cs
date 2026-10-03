using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Remote;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Remote Control v2: the live preview (<c>screen</c> + <c>caps</c> in the status push), the
    /// event ring that feeds it, and "hot mode" cadence (poll every second while the controller
    /// is busy). Wire contract: the v2 brief, sections 4 and 5.
    /// </summary>
    public partial class RemoteControlService
    {
        private const double HotPollSeconds = 1.0;
        private const double HotWindowSeconds = 60.0;
        private const double ScreenPushMinGapSeconds = 1.0;
        private const double ConnectedHeartbeatSeconds = 5.0;
        private const double CountResultShownSeconds = 20.0;

        private readonly RemoteEventRing _screenEvents = new();
        private DateTime _lastControllerCommandUtc = DateTime.MinValue;
        private string? _lastPushedScreenKey;
        private bool _pollBackedOff;
        private bool _feedsAttached;

        // A word the controller sent, waiting for the subliminal service to show it, so that one
        // event (and only that one) carries its text.
        private string? _pendingControllerWord;
        private DateTime _pendingControllerWordUtc;

        private DateTime _countEndedUtc = DateTime.MinValue;
        private bool? _countRight;
        private int? _countN, _countAnswer;

        private string? _videoKindSeen;
        private DateTime _videoSeenSinceUtc;

        /// <summary>The poll interval to run at when nothing is backing off: 1 s while a
        /// controller is connected and either sent a command in the last minute or has a haptic
        /// loop running, else the normal 5 s.</summary>
        private double PollBaseSeconds =>
            ControllerConnected
            && ((DateTime.UtcNow - _lastControllerCommandUtc).TotalSeconds < HotWindowSeconds
                || _remoteHaptics?.IsLooping == true)
                ? HotPollSeconds
                : PollIntervalSeconds;

        /// <summary>Status heartbeat: 5 s while a controller watches, 15 s otherwise.</summary>
        private double StatusHeartbeatSeconds => ControllerConnected ? ConnectedHeartbeatSeconds : StatusPushIntervalSeconds;

        private static long UnixMsNow => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>End of a poll: move the timer to the cadence the moment calls for, unless a
        /// 429 backoff is in charge (it always wins).</summary>
        private void ApplyPollCadence()
        {
            if (_pollBackedOff || _pollTimer == null) return;
            var want = PollBaseSeconds;
            if (Math.Abs(_currentPollInterval - want) < 0.01) return;
            _currentPollInterval = want;
            _pollTimer.Interval = TimeSpan.FromSeconds(want);
        }

        /// <summary>True when the preview moved since the last push and the 1 s gap has passed.</summary>
        private bool ScreenWantsPush(JObject? screen)
            => screen != null
               && RemoteScreenState.ChangeKey(screen) != _lastPushedScreenKey
               && (DateTime.UtcNow - _lastStatusPushUtc).TotalSeconds >= ScreenPushMinGapSeconds;

        // ------------------------------------------------------------------ the event ring

        private void AttachScreenFeeds()
        {
            if (_feedsAttached) return;
            _feedsAttached = true;
            if (App.Flash != null) App.Flash.FlashDisplayed += OnScreenFlash;
            if (App.Subliminal != null) App.Subliminal.SubliminalDisplayed += OnScreenWord;
            if (App.Bubbles != null) App.Bubbles.OnBubblePopped += OnScreenPop;
            if (App.LockCard != null) App.LockCard.LockCardCompleted += OnScreenLockDone;
            if (App.BubbleCount != null)
            {
                App.BubbleCount.GameCompleted += OnScreenCountRight;
                App.BubbleCount.GameFailed += OnScreenCountWrong;
            }
            LockCardWindow.TypoMade += OnScreenTypo;
        }

        private void DetachScreenFeeds()
        {
            if (!_feedsAttached) return;
            _feedsAttached = false;
            if (App.Flash != null) App.Flash.FlashDisplayed -= OnScreenFlash;
            if (App.Subliminal != null) App.Subliminal.SubliminalDisplayed -= OnScreenWord;
            if (App.Bubbles != null) App.Bubbles.OnBubblePopped -= OnScreenPop;
            if (App.LockCard != null) App.LockCard.LockCardCompleted -= OnScreenLockDone;
            if (App.BubbleCount != null)
            {
                App.BubbleCount.GameCompleted -= OnScreenCountRight;
                App.BubbleCount.GameFailed -= OnScreenCountWrong;
            }
            LockCardWindow.TypoMade -= OnScreenTypo;
            _screenEvents.Clear();
            _pendingControllerWord = null;
            _countRight = null;
            _countN = _countAnswer = null;
            _videoKindSeen = null;
            _lastPushedScreenKey = null;
        }

        private void OnScreenFlash(object? sender, EventArgs e)
        {
            var paths = App.Flash?.LastDisplayedImagePaths;
            var online = paths != null && paths.Any(FlashService.IsRemotePath);
            _screenEvents.Add(new RemoteScreenEvent("flash", UnixMsNow, Src: online ? "online" : "local"));
        }

        private void OnScreenWord(object? sender, EventArgs e)
        {
            // Only a word the controller sent travels as text; Sam's own pool never does.
            string? text = null;
            if (_pendingControllerWord != null && (DateTime.UtcNow - _pendingControllerWordUtc).TotalSeconds < 5)
            {
                text = _pendingControllerWord;
                _pendingControllerWord = null;
            }
            _screenEvents.Add(new RemoteScreenEvent("word", UnixMsNow, Text: text));
        }

        private void OnScreenPop() => _screenEvents.Add(new RemoteScreenEvent("pop", UnixMsNow));
        private void OnScreenTypo() => _screenEvents.Add(new RemoteScreenEvent("typo", UnixMsNow));
        private void OnScreenLockDone(object? sender, LockCardCompletedEventArgs e)
            => _screenEvents.Add(new RemoteScreenEvent("done", UnixMsNow));
        private void OnScreenCountRight(object? sender, EventArgs e) => NoteCountEnded(true);
        private void OnScreenCountWrong(object? sender, EventArgs e) => NoteCountEnded(false);

        private void NoteCountEnded(bool right)
        {
            _countRight = right;
            _countN = BubbleCountResultWindow.LastCorrectAnswer;
            _countAnswer = BubbleCountResultWindow.LastAnswer;
            _countEndedUtc = DateTime.UtcNow;
            _screenEvents.Add(new RemoteScreenEvent("count", UnixMsNow));
        }

        /// <summary><c>trigger_custom_subliminal</c>: the next word shown is the controller's.</summary>
        private void NoteControllerWord(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _pendingControllerWord = text.Trim();
            _pendingControllerWordUtc = DateTime.UtcNow;
        }

        // ------------------------------------------------------------------ the preview

        /// <summary>The live preview, read off the app. Null if anything in it throws: the
        /// status push still goes, the page falls back to <c>active_services</c>. UI thread.</summary>
        private JObject? BuildScreenSafe()
        {
            try { return RemoteScreenState.Build(ReadScreenInputs()); }
            catch (Exception ex)
            {
                App.Logger?.Debug("[RemoteControl] Screen preview skipped: {Error}", ex.Message);
                return null;
            }
        }

        private RemoteScreenInputs ReadScreenInputs()
        {
            var s = App.Settings?.Current;
            var i = new RemoteScreenInputs
            {
                Spiral = s?.SpiralEnabled == true,
                Pink = s?.PinkFilterEnabled == true,
                Flash = App.Flash?.IsRunning == true,
                Subliminal = App.Subliminal?.IsRunning == true,
                Bubbles = App.Bubbles?.IsRunning == true,
                Bounce = App.BouncingText?.IsRunning == true,
                BrainDrain = App.Overlay?.BrainDrainVisualUp == true || App.BrainDrain?.IsRunning == true,
                MindWipe = App.MindWipe?.IsRunning == true,
                Duck = App.Audio?.IsDucked == true,
                Autonomy = App.Autonomy?.IsEnabled == true,
                LockCards = App.LockCard?.IsRunning == true,
                SpiralOpacity = s?.SpiralOpacity ?? 0,
                PinkOpacity = s?.PinkFilterOpacity ?? 0,
                Easy = EasyFactor,
                IdleSeconds = ReadIdleSeconds(),
                Events = _screenEvents.Recent(UnixMsNow),
            };

            // Video: how long it has been up is counted here, from the first preview that saw it.
            string? kind = App.Video?.IsPlaying == true ? "local"
                : MainWindowRef?.IsRemoteBrowserVideoActive == true ? "web" : null;
            if (kind != _videoKindSeen) { _videoKindSeen = kind; _videoSeenSinceUtc = DateTime.UtcNow; }
            if (kind != null)
            {
                i.VideoKind = kind;
                i.VideoElapsedMs = (long)(DateTime.UtcNow - _videoSeenSinceUtc).TotalMilliseconds;
                if (kind == "local")
                {
                    var dur = App.Video?.GetPrimaryDurationSeconds() ?? 0;
                    if (dur > 0) i.VideoDurationMs = (long)(dur * 1000);
                }
            }

            if (LockCardWindow.RemoteSnapshot is { } card)
            {
                i.LockText = card.Text;
                i.LockPos = card.Pos;
                i.LockTypos = card.Typos;
                i.LockDone = card.Done;
            }

            if (App.BubbleCount?.IsBusy == true) i.CountActive = true;
            else if (_countRight != null && (DateTime.UtcNow - _countEndedUtc).TotalSeconds < CountResultShownSeconds)
            {
                i.CountRight = _countRight;
                i.CountN = _countN;
                i.CountAnswer = _countAnswer;
            }

            var haptics = _remoteHaptics;
            if (haptics?.IsPlaying == true)
            {
                i.HapticLevel = (int)Math.Round(haptics.CurrentLevel * EasyFactor);
                i.HapticPattern = haptics.Plan?.IsHold == true ? null : haptics.Plan?.Name;
                i.HapticLoop = haptics.IsLooping;
            }
            i.HapticDevice = App.Haptics?.IsConnected == true;

            if (s != null && s.MediaSource != "local" && s.HasRemoteMediaConsent)
                i.OnlineNames = FypOnlineCoordinator.ResolveChannels(s.FypOnlineNiches, s.FypOnlineCustomSubs);
            i.ShareOnlineNames = s?.RemoteShareMediaSources != false;

            RefreshMediaCountsIfStale();
            i.Pictures = _mediaPictures;
            i.Videos = _mediaVideos;
            return i;
        }

        // ------------------------------------------------------------------ media counts

        private int _mediaPictures, _mediaVideos;
        private DateTime _mediaCountedUtc = DateTime.MinValue;
        private int _mediaCounting;

        private static readonly HashSet<string> PictureExts = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v", ".wmv" };

        /// <summary>Counts the assets folder's pictures and videos off the UI thread, at most every
        /// two minutes. Counts only: no name ever leaves this method.</summary>
        private void RefreshMediaCountsIfStale()
        {
            if ((DateTime.UtcNow - _mediaCountedUtc).TotalMinutes < 2) return;
            if (System.Threading.Interlocked.Exchange(ref _mediaCounting, 1) == 1) return;
            string root;
            try { root = App.EffectiveAssetsPath; }
            catch { _mediaCounting = 0; return; }
            _ = Task.Run(() =>
            {
                try
                {
                    _mediaPictures = CountFiles(Path.Combine(root, "images"), PictureExts);
                    _mediaVideos = CountFiles(Path.Combine(root, "videos"), VideoExts);
                }
                catch { }
                finally
                {
                    _mediaCountedUtc = DateTime.UtcNow;
                    _mediaCounting = 0;
                }
            });
        }

        private static int CountFiles(string dir, HashSet<string> exts)
        {
            if (!Directory.Exists(dir)) return 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            var n = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", options))
            {
                if (exts.Contains(Path.GetExtension(f))) n++;
                if (n >= 100_000) break;
            }
            return n;
        }

        // ------------------------------------------------------------------ attention

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo info);

        /// <summary>Seconds since the subject last touched the keyboard or mouse.</summary>
        private static int ReadIdleSeconds()
        {
            try
            {
                var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
                if (!GetLastInputInfo(ref info)) return 0;
                long millis = (long)Environment.TickCount - info.dwTime;
                if (millis < 0) millis += (long)uint.MaxValue + 1;
                return (int)(millis / 1000);
            }
            catch { return 0; }
        }
    }
}
