using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>What the bubble-count game needs from a head. Called on the head's UI thread.</summary>
    public interface IBubbleCountHost
    {
        /// <summary>Open the game (clip, count bubbles, then the answer window) and call
        /// <paramref name="onComplete"/> once with the answer's outcome (WPF BubbleCountWindow.ShowOnAllMonitors).</summary>
        void Show(string path, int difficulty, bool strict, Action<bool> onComplete);
        /// <summary>Full-screen retry/mercy message for <paramref name="ms"/>, then <paramref name="then"/>
        /// (WPF ShowFullscreenMessage).</summary>
        void ShowMessage(string text, int ms, Action then);
        /// <summary>Close game, answer and message windows without completing (WPF ForceCloseAll x2).</summary>
        void CloseAll();
        /// <summary>Length of the clip the last game played (WPF BubbleCountWindow.LastVideoDurationSeconds).</summary>
        double LastVideoDurationSeconds { get; }
    }

    /// <summary>
    /// The portable half of WPF <c>BubbleCountService</c> (ConditioningControlPanel/Services/BubbleCountService.cs):
    /// the schedule (ScheduleNextGame :91), the trigger (TriggerGame :266), completion XP with its 3-minute
    /// cooldown and duration scaling (OnGameComplete :450, ScaleXpByDuration :442), strict retry with
    /// mercy after 3 (:476-512, RetryGame :524) and ForceCleanup (:831). WPF delegates its rules here.
    /// The Bambi Freeze lead-in (:353) goes through <see cref="CoreSubliminal.TriggerBambiFreeze"/>.
    /// ponytail: local clips only - pack clips, the For You defer and the interaction queue are
    /// WPF-head services; add each when it reaches Core.
    /// </summary>
    public sealed class BubbleCountScheduler
    {
        public enum Difficulty { Easy, Medium, Hard }

        public static readonly TimeSpan XpCooldown = TimeSpan.FromMinutes(3);
        public const double FullXpVideoDurationSeconds = 60.0;
        public const int MercyAfterRetries = 3;
        /// <summary>WPF's Task.Delay(800) that lets the Bambi Freeze land before the game.</summary>
        public static readonly TimeSpan LeadIn = TimeSpan.FromMilliseconds(800);

        private readonly IBubbleCountHost _host;
        private readonly TimeProvider _time;
        private readonly Func<IReadOnlyList<string>> _library;
        private readonly Random _random = new();
        private Queue<string> _queue = new();
        private ITimer? _scheduler, _leadIn;
        private DateTimeOffset _lastXp = DateTimeOffset.MinValue;
        private int _retryCount;

        public BubbleCountScheduler(IBubbleCountHost host, TimeProvider? time = null, Func<IReadOnlyList<string>>? library = null)
        {
            _host = host;
            _time = time ?? TimeProvider.System;
            _library = library ?? MandatoryVideoScheduler.LocalLibrary;
        }

        public bool IsRunning { get; private set; }
        /// <summary>A game (lead-in, clip, answer or retry message) is in progress.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>WPF ScheduleNextGame: 3600/perHour (clamped 1-10) jittered +/-20%, never under 60 s.</summary>
        public static double NextIntervalSeconds(int perHour, double roll)
        {
            var b = 3600.0 / Math.Clamp(perHour, 1, 10);
            return Math.Max(60, b + (roll * b * 0.4 - b * 0.2));
        }

        /// <summary>WPF ScaleXpByDuration: full XP from 60 s, proportional below, floor 10% and 1.</summary>
        public static int ScaleXpByDuration(int baseXp, double durationSeconds)
        {
            if (durationSeconds >= FullXpVideoDurationSeconds) return baseXp;
            var scale = Math.Max(0.1, durationSeconds / FullXpVideoDurationSeconds);
            return Math.Max(1, (int)(baseXp * scale));
        }

        /// <summary>WPF BubbleCountWindow.CalculateTargetBubbles: 3/5/8 per 30 s, +/-20%, at least 3.</summary>
        public static int TargetBubbles(Difficulty difficulty, double durationSeconds, double roll)
        {
            double rate = difficulty switch { Difficulty.Easy => 3, Difficulty.Hard => 8, _ => 5 };
            var scaled = rate / 30.0 * durationSeconds;
            var variance = scaled * 0.2;
            return Math.Max(3, (int)Math.Round(scaled + (roll * variance * 2 - variance)));
        }

        public void Start()
        {
            if (IsRunning) return;
            if (!CoreSettings.Current.BubbleCountEnabled) { Log.Information("BubbleCountService: Disabled in settings"); return; }
            IsRunning = true;
            ScheduleNext();
            Log.Information("BubbleCountService started - {PerHour}/hour", CoreSettings.Current.BubbleCountFrequency);
        }

        /// <summary>WPF Stop: the schedule only. A game already on screen plays out, as in WPF; the
        /// engine stop (and so panic) follows with <see cref="ForceCleanup"/>.</summary>
        public void Stop()
        {
            IsRunning = false;
            Dispose(ref _scheduler);
        }

        /// <summary>WPF RefreshSchedule: a live schedule picks up a new frequency.</summary>
        public void RefreshSchedule() { if (IsRunning) ScheduleNext(); }

        /// <summary>WPF ForceCleanup / ResetBusyState: close everything, no completion, no XP.</summary>
        public void ForceCleanup()
        {
            Dispose(ref _leadIn);
            var wasBusy = IsBusy;
            Idle();   // WPF ForceCleanup: App.Bubbles.Resume()
            _retryCount = 0;
            try { _host.CloseAll(); } catch (Exception ex) { Log.Warning(ex, "BubbleCountService: close failed"); }
            if (wasBusy) Log.Information("BubbleCountService: ForceCleanup called");
        }

        private void ScheduleNext()
        {
            var s = CoreSettings.Current;
            if (!IsRunning || !s.BubbleCountEnabled) return;
            var secs = NextIntervalSeconds(s.BubbleCountFrequency, _random.NextDouble());
            Dispose(ref _scheduler);
            _scheduler = After(TimeSpan.FromSeconds(secs), mine =>
            {
                if (!ReferenceEquals(mine, _scheduler)) return;
                Dispose(ref _scheduler);
                if (IsRunning && !IsBusy) Trigger();
                ScheduleNext();
            });
            Log.Debug("Next bubble count game in {Interval:F1} seconds", secs);
        }

        /// <summary>WPF TriggerGame. <paramref name="forceTest"/> is the card's Test button: it runs
        /// with the engine stopped and the feature off.</summary>
        public void Trigger(bool forceTest = false)
        {
            if (IsBusy || (!forceTest && !IsRunning)) return;
            if (!forceTest && !CoreSettings.Current.BubbleCountEnabled)
            {
                Log.Information("BubbleCountService: game dropped - bubble count is disabled in settings");
                return;
            }
            IsBusy = true;
            _retryCount = 0;
            CoreBubbles.Pause();   // WPF App.Bubbles.PauseAndClear: nothing else to count
            CoreSubliminal.TriggerBambiFreeze();   // WPF :353, before the 800 ms lead-in
            Dispose(ref _leadIn);
            _leadIn = After(LeadIn, mine =>
            {
                if (!ReferenceEquals(mine, _leadIn) || !IsBusy) return;
                Dispose(ref _leadIn);
                if (!Play(CoreSettings.Current.BubbleCountStrictLock))
                {
                    Log.Warning("BubbleCountService: No videos found");
                    Idle();
                }
            });
        }

        private bool Play(bool strict)
        {
            var path = PickNext();
            if (path == null) return false;
            CoreProgression.TrackBubbleCountGameStarted();
            try { _host.Show(path, CoreSettings.Current.BubbleCountDifficulty, strict, OnComplete); }
            catch (Exception ex) { Log.Error(ex, "Failed to start bubble count game"); Idle(); _retryCount = 0; }
            return true;
        }

        /// <summary>WPF OnGameComplete.</summary>
        internal void OnComplete(bool success)
        {
            if (!IsBusy) return;   // panic got there first
            if (success)
            {
                _retryCount = 0;
                Idle();
                var now = _time.GetUtcNow();
                if (now - _lastXp >= XpCooldown)
                {
                    var xp = ScaleXpByDuration(100, _host.LastVideoDurationSeconds);
                    CoreProgression.AddXP(xp, "BubbleCount");
                    _lastXp = now;
                    Log.Information("Bubble count game completed! +{Xp} XP", xp);
                }
                CoreProgression.TrackBubbleCountCompleted();
                return;
            }
            var s = CoreSettings.Current;
            if (!s.BubbleCountStrictLock)
            {
                _retryCount = 0;
                Idle();
                Log.Information("Bubble count game failed");
                return;
            }
            _retryCount++;
            if (_retryCount >= MercyAfterRetries && s.MercySystemEnabled)
            {
                Log.Information("Bubble count mercy after {Retries} retries", _retryCount);
                _host.ShowMessage(CoreMods.AttentionCheckMercyMessage ?? "BAMBI GETS MERCY", 2500, () => { _retryCount = 0; Idle(); });
                return;
            }
            Log.Information("Bubble count retry {Count} (mercy at 3)", _retryCount);
            _host.ShowMessage(CoreMods.BubbleCountRetryMessage ?? "WRONG!\nWATCH AGAIN", 2000, () =>
            {
                if (!IsBusy) return;   // panic during the message
                if (!Play(true)) { Log.Warning("BubbleCountService: No videos for retry, granting mercy"); _retryCount = 0; Idle(); }
            });
        }

        private void Idle()
        {
            IsBusy = false;
            CoreBubbles.Resume();
        }

        internal string? PickNext()
        {
            if (_queue.Count == 0) _queue = new Queue<string>(_library().OrderBy(_ => _random.Next()));
            return _queue.Count > 0 ? _queue.Dequeue() : null;
        }

        private ITimer After(TimeSpan due, Action<ITimer?> tick)
        {
            ITimer? created = null;
            created = _time.CreateTimer(_ => { var mine = created; CoreDispatch.Post(() => tick(mine)); },
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            created.Change(due, Timeout.InfiniteTimeSpan);
            return created;
        }

        private static void Dispose(ref ITimer? timer)
        {
            var t = timer;
            timer = null;
            t?.Dispose();
        }
    }
}
