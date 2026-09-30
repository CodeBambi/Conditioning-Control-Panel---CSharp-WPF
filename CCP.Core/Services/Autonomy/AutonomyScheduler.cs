using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>Types of autonomous actions the companion can take.</summary>
    public enum AutonomyActionType
    {
        Flash, Video, Subliminal, BrainDrainPulse, StartBubbles, Comment, MindWipe, LockCard,
        SpiralPulse, PinkFilterPulse, BouncingText, BubbleCount, WebVideo, WallpaperShuffle, SpokenMantra
    }

    /// <summary>What triggered the autonomous action.</summary>
    public enum AutonomyTriggerSource { Idle, Random, Context, TimeOfDay }

    /// <summary>Time-of-day mood affecting behavior style.</summary>
    public enum AutonomyMood { Gentle, Attentive, Playful, Mischievous }

    public class AutonomyActionEventArgs : EventArgs
    {
        public AutonomyActionType ActionType { get; }
        public AutonomyTriggerSource Source { get; }
        public string? Context { get; }

        public AutonomyActionEventArgs(AutonomyActionType actionType, AutonomyTriggerSource source, string? context = null)
        {
            ActionType = actionType;
            Source = source;
            Context = context;
        }
    }

    /// <summary>
    /// Takeover's decision logic, lifted out of WPF <c>AutonomyService</c>
    /// (ConditioningControlPanel/Services/AutonomyService.cs): the entitlement gate, cooldown, the
    /// random/idle cadence, mood and intensity weighting, the announce roll and its 2 s delay, and
    /// the generation that drops a queued action once Takeover stops. WPF keeps its own
    /// DispatcherTimers and delegates the pure rules below; a head without them drives an instance
    /// through <see cref="Start"/> / <see cref="Tick"/>, which only reads <see cref="Clock"/>, so a
    /// fake clock tests the whole schedule.
    ///
    /// <para>Safety: nothing arms without the user's enable + consent + entitlement
    /// (<see cref="CanStart"/>), entitlement is re-checked before every action, and
    /// <see cref="Stop"/> bumps the generation so an announced action never lands afterwards.</para>
    /// </summary>
    public sealed class AutonomyScheduler
    {
        // ---- pure rules (WPF delegates) ----

        /// <summary>WPF CanStart / CanTakeAction: premium, or the ? box's takeover free day.</summary>
        public static bool HasEntitlement => CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("takeover");

        public static bool CanStart(AppSettings s) => s.AutonomyModeEnabled && s.AutonomyConsentGiven && HasEntitlement;

        /// <summary>A pulse that started the overlay service stops it, once no sibling holds it (#1180).</summary>
        public static bool ShouldStopOverlayAfterPulse(bool overlayWasRunningBeforePulse, bool otherPulseActive)
            => !overlayWasRunningBeforePulse && !otherPulseActive;

        /// <summary>An announced action still fires only if Takeover is on and not restarted since (#1153).</summary>
        public static bool ShouldRunDelayedAction(bool enabled, int generationAtSchedule, int generationNow)
            => enabled && generationAtSchedule == generationNow;

        public static AutonomyMood MoodAt(int hour) => hour switch
        {
            >= 22 or < 6 => AutonomyMood.Mischievous,
            >= 18 => AutonomyMood.Playful,
            >= 12 => AutonomyMood.Attentive,
            _ => AutonomyMood.Gentle
        };

        public static double TimeMultiplier(AppSettings s, int hour)
        {
            if (!s.AutonomyTimeAwareEnabled) return 1.0;
            return hour switch
            {
                >= 22 or < 6 => s.AutonomyNightMultiplier,
                >= 18 => s.AutonomyEveningMultiplier,
                >= 12 => s.AutonomyAfternoonMultiplier,
                _ => s.AutonomyMorningMultiplier
            };
        }

        public const double RetryIntervalSeconds = 12;

        /// <summary>WPF ScheduleNextRandomTick: retry 12-24 s; test mode 30 s; else ±33 % around
        /// the slider, divided by the time-of-day multiplier, clamped 15-900 s.</summary>
        public static double NextRandomSeconds(AppSettings s, bool retry, bool forceTest, int hour, double roll)
        {
            if (retry) return RetryIntervalSeconds + roll * RetryIntervalSeconds;
            if (forceTest) return 30;
            var seconds = s.AutonomyRandomIntervalSeconds * ((2.0 / 3.0) + roll * (2.0 / 3.0));
            var timeMult = TimeMultiplier(s, hour);
            if (timeMult > 0) seconds /= timeMult;
            return Math.Clamp(seconds, 15, 900);
        }

        /// <summary>WPF SelectAction's candidate list, in its order and weights. The head says
        /// whether its extra conditions allow a web video / spoken mantra right now.</summary>
        public static List<(AutonomyActionType type, int weight)> Candidates(AppSettings s, bool webVideoOk, bool mantraOk)
        {
            var c = new List<(AutonomyActionType, int)>();
            if (s.AutonomyCanTriggerFlash) c.Add((AutonomyActionType.Flash, 30));
            if (s.AutonomyCanTriggerVideo) c.Add((AutonomyActionType.Video, 15));
            if (s.AutonomyCanTriggerSubliminal) c.Add((AutonomyActionType.Subliminal, 25));
            // BrainDrainPulse, SpiralPulse and BubbleCount are deliberately never candidates (WPF).
            if (s.AutonomyCanTriggerBubbles) c.Add((AutonomyActionType.StartBubbles, 15));
            if (s.AutonomyCanComment) c.Add((AutonomyActionType.Comment, 20));
            if (s.AutonomyCanTriggerMindWipe) c.Add((AutonomyActionType.MindWipe, 15));
            if (s.AutonomyCanTriggerLockCard) c.Add((AutonomyActionType.LockCard, 10));
            if (s.AutonomyCanTriggerPinkFilter) c.Add((AutonomyActionType.PinkFilterPulse, 20));
            if (s.AutonomyCanTriggerBouncingText) c.Add((AutonomyActionType.BouncingText, 15));
            if (s.AutonomyCanTriggerWebVideo && webVideoOk) c.Add((AutonomyActionType.WebVideo, 20));
            if (s.AutonomyCanTriggerWallpaper) c.Add((AutonomyActionType.WallpaperShuffle, 10));
            // Surprise mantras only while the user is not driving the mic (wake word / PTT).
            if (s.AutonomyCanTriggerVoiceCommand && s.MicConsentGiven && !s.SpeechWakeWordEnabled
                && !s.SpeechPushToTalkEnabled && mantraOk)
                c.Add((AutonomyActionType.SpokenMantra, 18));
            return c;
        }

        /// <summary>WPF ApplyMoodWeights + ApplyIntensityScaling + the weighted roll.</summary>
        public static AutonomyActionType? Pick(List<(AutonomyActionType type, int weight)> candidates,
            AutonomyMood mood, int intensity, Random rng)
        {
            if (candidates.Count == 0) return null;
            var disruptiveBonus = (intensity - 5) * 0.1;
            var weighted = candidates.Select(c =>
            {
                var m = mood switch
                {
                    AutonomyMood.Gentle => c.type switch
                    {
                        AutonomyActionType.Comment => 1.5,
                        AutonomyActionType.Video or AutonomyActionType.BrainDrainPulse => 0.5,
                        _ => 1.0
                    },
                    AutonomyMood.Playful => c.type switch
                    {
                        AutonomyActionType.StartBubbles => 1.5,
                        AutonomyActionType.Flash => 1.3,
                        _ => 1.0
                    },
                    AutonomyMood.Mischievous => c.type switch
                    {
                        AutonomyActionType.Video or AutonomyActionType.BrainDrainPulse => 1.5,
                        AutonomyActionType.Subliminal => 1.3,
                        _ => 1.0
                    },
                    _ => 1.0
                };
                var w = (int)(c.weight * m);
                if (c.type is AutonomyActionType.Video or AutonomyActionType.BrainDrainPulse)
                    w = Math.Max(1, (int)(w * (1.0 + disruptiveBonus)));
                return (c.type, w);
            }).ToList();

            var total = weighted.Sum(c => c.w);
            if (total <= 0) return null;
            var roll = rng.Next(total);
            var cumulative = 0;
            foreach (var (type, w) in weighted)
            {
                cumulative += w;
                if (roll < cumulative) return type;
            }
            return weighted[0].type;
        }

        /// <summary>The on-screen takeover cue; null suppresses it (Comment is just a giggle).</summary>
        public static string? TakeoverEffectLabel(AutonomyActionType t) => t switch
        {
            AutonomyActionType.Flash => "FLASH",
            AutonomyActionType.Video => "VIDEO",
            AutonomyActionType.Subliminal => "SUBLIMINAL",
            AutonomyActionType.BrainDrainPulse => "BRAIN DRAIN",
            AutonomyActionType.StartBubbles => "BUBBLES",
            AutonomyActionType.MindWipe => "MIND WIPE",
            AutonomyActionType.LockCard => "LOCK CARD",
            AutonomyActionType.SpiralPulse => "SPIRAL",
            AutonomyActionType.PinkFilterPulse => "PINK FILTER",
            AutonomyActionType.BouncingText => "BOUNCING TEXT",
            AutonomyActionType.BubbleCount => "BUBBLE COUNT",
            AutonomyActionType.WebVideo => "WEB VIDEO",
            AutonomyActionType.WallpaperShuffle => "WALLPAPER",
            AutonomyActionType.SpokenMantra => "MANTRA",
            _ => null,
        };

        public static readonly IReadOnlyDictionary<AutonomyActionType, string[]> AnnouncementPhrases =
            new Dictionary<AutonomyActionType, string[]>
            {
                [AutonomyActionType.Flash] = new[] { "Time for a little surprise~", "Here comes something pretty!", "Look at the screen for me~", "Ooh, I want to show you something~", "Pretty picture time~" },
                [AutonomyActionType.Video] = new[] { "Video time! Get comfy~", "I have something to show you...", "Time to watch and absorb~", "Sit back and watch~", "Let's watch something together~" },
                [AutonomyActionType.Subliminal] = new[] { "Just a little message for you~", "*giggles* Did you see that?", "Shhh, just let it sink in~", "A little reminder~", "Don't think, just absorb~" },
                [AutonomyActionType.BrainDrainPulse] = new[] { "Let me blur your thoughts~", "Time to get fuzzy~", "Thinking is overrated~", "Let it all go blurry~" },
                [AutonomyActionType.StartBubbles] = new[] { "Pop pop pop!", "Let's play~", "Bubble time!", "Click the bubbles~" },
                [AutonomyActionType.Comment] = new[] { "*giggles*", "Teehee~", "Just thinking about you~" },
                [AutonomyActionType.MindWipe] = new[] { "Let me wipe your thoughts~", "Shhh... empty mind~", "No more thinking~", "Time to forget~" },
                [AutonomyActionType.LockCard] = new[] { "Time to earn a reward~", "Complete this for me~", "Show me how good you are~", "Task time~" },
                [AutonomyActionType.SpiralPulse] = new[] { "Watch the pretty spiral~", "Spirals are so pretty...", "Look at the swirls~", "Round and round~" },
                [AutonomyActionType.PinkFilterPulse] = new[] { "Everything looks better in pink~", "Pink is your color~", "So pretty and pink~", "Pink thoughts~" },
                [AutonomyActionType.BouncingText] = new[] { "Read the pretty words~", "Follow the bouncing text~", "Words to remember~", "Let them sink in~" },
                [AutonomyActionType.BubbleCount] = new[] { "Count with me~", "How many bubbles?", "Test your focus~", "Counting game time~" },
                [AutonomyActionType.WebVideo] = new[] { "Time to watch something special~", "I picked a video just for you~", "Sit back and let it sink in~", "Watch and absorb~", "Fullscreen time~" },
                [AutonomyActionType.WallpaperShuffle] = new[] { "New scenery for you~", "Let me redecorate~", "A little change of view~", "How about this one?~" },
            };

        // ---- the scheduler a head without DispatcherTimers drives ----

        public Func<DateTime> Clock { get; set; } = () => DateTime.Now;
        public Random Rng { get; set; } = new();

        /// <summary>Head: can this head perform the action at all (and right now)? Unported
        /// actions answer false, so they are never picked.</summary>
        public Func<AutonomyActionType, bool> CanPerform { get; set; } = _ => false;

        /// <summary>Head: a fullscreen interaction / browser video is up, so wait (WPF InteractionQueue, BrowserMedia).</summary>
        public Func<bool> IsBusy { get; set; } = () => false;

        /// <summary>Head: run the action (source, announced).</summary>
        public Action<AutonomyActionType, bool>? Perform { get; set; }

        public event EventHandler<bool>? EnabledChanged;
        public event EventHandler<AutonomyActionEventArgs>? ActionTriggered;
        public event EventHandler<string>? AnnouncementMade;

        private bool _enabled;
        private int _generation;
        private DateTime _lastAction = DateTime.MinValue, _nextRandom = DateTime.MaxValue, _nextIdle = DateTime.MaxValue;
        private (AutonomyActionType action, DateTime due, int gen)? _pending;
        private Timer? _timer;

        public bool IsEnabled => _enabled;
        public DateTime NextRandomFire => _nextRandom;

        /// <summary>WPF Start: refuses without enable + consent + entitlement; idempotent.</summary>
        public bool Start(bool withTimer = true)
        {
            if (_enabled || !CanStart(CoreSettings.Current)) return _enabled;
            _enabled = true;
            _lastAction = DateTime.MinValue;
            _pending = null;
            RefreshIdleTimer();
            RefreshRandomTimer();
            if (withTimer) _timer = new Timer(_ => CoreDispatch.Post(Tick), null, 1000, 1000);
            Log.Information("Autonomy: started");
            try { EnabledChanged?.Invoke(this, true); } catch { }
            return true;
        }

        /// <summary>WPF Stop: timers off, any announced action dropped. The saved enable flag is not touched.</summary>
        public void Stop()
        {
            var was = _enabled;
            _enabled = false;
            _generation++;
            _pending = null;
            _timer?.Dispose();
            _timer = null;
            _nextRandom = _nextIdle = DateTime.MaxValue;
            if (!was) return;
            Log.Information("Autonomy: stopped");
            try { EnabledChanged?.Invoke(this, false); } catch { }
        }

        public void ReportUserActivity()
        {
            if (_nextIdle != DateTime.MaxValue) RefreshIdleTimer();
        }

        public void RefreshIdleTimer()
        {
            var s = CoreSettings.Current;
            _nextIdle = _enabled && s.AutonomyIdleTriggerEnabled
                ? Clock().AddMinutes(s.AutonomyIdleTimeoutMinutes) : DateTime.MaxValue;
        }

        public void RefreshRandomTimer() => ScheduleRandom(retry: false);

        private void ScheduleRandom(bool retry)
        {
            var s = CoreSettings.Current;
            var now = Clock();
            _nextRandom = _enabled && s.AutonomyRandomTriggerEnabled
                ? now.AddSeconds(NextRandomSeconds(s, retry, false, now.Hour, Rng.NextDouble()))
                : DateTime.MaxValue;
        }

        /// <summary>WPF CanTakeAction: on, entitled (a lapse mid-run stops her acting), not busy, past the cooldown.</summary>
        public bool CanTakeAction() =>
            _enabled && HasEntitlement && !IsBusy()
            && (Clock() - _lastAction).TotalSeconds >= CoreSettings.Current.AutonomyCooldownSeconds;

        /// <summary>One pass of the schedule: the announced action, then the idle and random triggers.</summary>
        public void Tick()
        {
            if (!_enabled) return;
            var now = Clock();

            if (_pending is { } p && now >= p.due)
            {
                _pending = null;
                if (ShouldRunDelayedAction(_enabled, p.gen, _generation)) Run(p.action, announced: true);
            }

            if (now >= _nextIdle)
            {
                _nextIdle = now.AddMinutes(CoreSettings.Current.AutonomyIdleTimeoutMinutes);   // DispatcherTimer repeats
                if (CanTakeAction()) Execute(AutonomyTriggerSource.Idle);
            }

            if (now >= _nextRandom)
            {
                if (!CanTakeAction()) { ScheduleRandom(retry: true); return; }
                ScheduleRandom(retry: false);
                Execute(AutonomyTriggerSource.Random);
            }
        }

        /// <summary>The Test button: act now, ignoring cooldown (WPF TestTrigger still needs Takeover on).</summary>
        public void TestTrigger()
        {
            if (_enabled && HasEntitlement) Execute(AutonomyTriggerSource.Random);
        }

        private void Execute(AutonomyTriggerSource source)
        {
            var s = CoreSettings.Current;
            var now = Clock();
            var candidates = Candidates(s, webVideoOk: true, mantraOk: true).Where(c => CanPerform(c.type)).ToList();
            var action = Pick(candidates, MoodAt(now.Hour), s.AutonomyIntensity, Rng);
            if (action is not { } a) { Log.Warning("Autonomy: no action this head can perform is enabled"); return; }

            if (Rng.Next(100) < s.AutonomyAnnouncementChance && AnnouncementPhrases.TryGetValue(a, out var phrases))
            {
                var phrase = phrases[Rng.Next(phrases.Length)];
                try { AnnouncementMade?.Invoke(this, phrase); } catch { }
                _pending = (a, now.AddSeconds(2), _generation);
            }
            else Run(a, announced: false);

            _lastAction = now;
            try { ActionTriggered?.Invoke(this, new AutonomyActionEventArgs(a, source)); } catch { }
        }

        private void Run(AutonomyActionType a, bool announced)
        {
            Log.Information("Autonomy: performing {Action}", a);
            try { Perform?.Invoke(a, announced); }
            catch (Exception ex) { Log.Warning(ex, "Autonomy: {Action} failed", a); }
        }
    }
}
