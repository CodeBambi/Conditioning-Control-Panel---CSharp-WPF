using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The phases of WPF SessionEngine the first runner pass left out (studio#31): spiral (randomised
    /// delayed start + opacity ramp), Brain Drain (start/end minutes + intensity ramp), bubbles
    /// (delayed start, +1 per 5 min ramp, intermittent bursts), mandatory videos, bubble count,
    /// the session's escalating Mind Wipe and audio ducking. Same numbers as SessionEngine.cs.
    /// Every write lands on a field <see cref="SessionSettingsSnapshot"/> restores at Stop.
    /// </summary>
    public sealed partial class SessionRunner
    {
        private double _randomizedSpiralStartMinute;
        private bool _brainDrainActive;
        private readonly List<double> _bubbleBursts = new();
        private int _bubbleBurstIndex;
        private bool _bubblesBurstActive;
        private double _bubbleBurstEndMinute;

        /// <summary>The ramped spiral opacity (0-100) while a ramp runs, else null: WPF drives the overlay
        /// with it (SetSustainedOverlayOpacity) and never writes SpiralOpacity. The head paints it.</summary>
        public double? SpiralOpacity { get; private set; }

        /// <summary>SessionEngine.cs:1580-1586 tail of StartSession: spiral start time, bursts, Mind Wipe.</summary>
        private void StartPhases(Session session)
        {
            var ss = session.Settings;
            SpiralOpacity = null;
            _randomizedSpiralStartMinute = RandomizedStart(ss.SpiralEnabled, ss.SpiralStartMinute, _random);   // SessionEngine.cs:988
            _bubbleBursts.Clear();
            _bubbleBurstIndex = 0;
            _bubblesBurstActive = false;
            if (ss.BubblesEnabled && ss.BubblesIntermittent)
                _bubbleBursts.AddRange(ScheduleBubbleBursts(ss, session.DurationMinutes, _random));

            // SessionEngine.cs:226: the session's own escalating Mind Wipe (the global one was stopped).
            if (ss.MindWipeEnabled)
            {
                var baseMul = ss.MindWipeBaseMultiplier;
                var volume = ss.MindWipeVolume / 100.0;
                if (ss.MindWipeStartMinute == 0) CoreMindWipe.StartSession(baseMul, volume);
                else _deferred.Defer("mind wipe", ss.MindWipeStartMinute, () => CoreMindWipe.StartSession(baseMul, volume));
            }
        }

        /// <summary>SessionEngine.ScheduleBubbleBursts (:1002): first burst after 2-4 min, then gaps of
        /// GapMin..GapMax minutes, never in the last 2 minutes.</summary>
        internal static List<double> ScheduleBubbleBursts(SessionSettings ss, double totalMinutes, Random random)
        {
            var bursts = new List<double>();
            double t = random.Next(2, 5);
            for (int i = 0; i < ss.BubblesBurstCount && t < totalMinutes - 2; i++)
            {
                bursts.Add(t);
                t += random.Next(ss.BubblesGapMin, ss.BubblesGapMax + 1);
            }
            Log.Information("Scheduled {Count} bubble bursts: {Times}", bursts.Count, string.Join(", ", bursts.Select(b => $"{b:F1}min")));
            return bursts;
        }

        /// <summary>ApplySessionSettings (SessionEngine.cs:1428-1660): ducking, spiral, brain drain,
        /// bubbles, mandatory videos, bubble count.</summary>
        private void ApplyPhases(SessionSettings ss, AppSettings s)
        {
            if (ss.AudioDuckLevel > 0)
            {
                s.AudioDuckingEnabled = true;
                s.DuckingLevel = ss.AudioDuckLevel;
            }

            if (ss.SpiralEnabled && ss.SpiralStartMinute == 0)
            {
                s.SpiralEnabled = true;
                s.SpiralOpacity = ss.SpiralOpacity;
            }
            else s.SpiralEnabled = false;

            _brainDrainActive = false;
            if (ss.BrainDrainEnabled && ss.BrainDrainStartMinute == 0)
            {
                s.BrainDrainEnabled = true;
                s.BrainDrainIntensity = ss.BrainDrainStartIntensity;
                _brainDrainActive = true;
                CoreBrainDrain.Start();
            }
            else
            {
                s.BrainDrainEnabled = false;
                CoreBrainDrain.Stop();
            }

            if (ss.BubblesEnabled)
            {
                s.BubblesFrequency = ss.BubblesFrequency;
                s.BubblesClickable = ss.BubblesClickable;
                s.BubblesEnabled = ss.BubblesStartMinute == 0 && !ss.BubblesIntermittent;
                if (s.BubblesEnabled)
                {
                    CoreBubbles.Start();
                    CoreBubbles.RefreshFrequency();
                }
                else CoreBubbles.Stop();
            }
            else
            {
                s.BubblesEnabled = false;
                CoreBubbles.Stop();
            }

            s.MandatoryVideosEnabled = ss.MandatoryVideosEnabled;
            if (ss.MandatoryVideosEnabled)
            {
                if (ss.VideosPerHour.HasValue) s.VideosPerHour = ss.VideosPerHour.Value;
                StartAt("mandatory videos", ss.MandatoryVideosStartMinute,
                    () => CoreEngine.Video?.Start(), () => CoreEngine.Video?.Stop());
            }
            else CoreEngine.Video?.Stop();

            s.BubbleCountEnabled = ss.BubbleCountEnabled;
            if (ss.BubbleCountEnabled)
            {
                if (ss.BubbleCountFrequency.HasValue) s.BubbleCountFrequency = ss.BubbleCountFrequency.Value;
                StartAt("bubble count", ss.BubbleCountStartMinute,
                    () => CoreEngine.BubbleCount?.Start(), () => CoreEngine.BubbleCount?.Stop());
            }
            else CoreEngine.BubbleCount?.Stop();
        }

        /// <summary>PauseSession (SessionEngine.cs:529-539): the phase services this partial drives.</summary>
        private void PausePhases()
        {
            CoreBubbles.Stop();
            CoreEngine.BubbleCount?.Stop();
            CoreMindWipe.Stop();
            CoreBrainDrain.Stop();
            CoreEngine.Video?.Stop();
        }

        /// <summary>ResumeSession (SessionEngine.cs:578-588). Mind Wipe comes back through the plain
        /// Start(base, volume), exactly as WPF (base read as plays per hour, not escalating).</summary>
        private void ResumePhases(SessionSettings ss)
        {
            if (ss.BubblesEnabled && CoreSettings.Current.BubblesEnabled) CoreBubbles.Start();
            if (ss.BubbleCountEnabled && !_deferred.IsPending("bubble count")) CoreEngine.BubbleCount?.Start();
            if (ss.MindWipeEnabled && !_deferred.IsPending("mind wipe"))
                CoreMindWipe.Start(ss.MindWipeBaseMultiplier, ss.MindWipeVolume / 100.0);
            if (ss.BrainDrainEnabled && _brainDrainActive) CoreBrainDrain.Start();
            if (ss.MandatoryVideosEnabled && !_deferred.IsPending("mandatory videos")) CoreEngine.Video?.Start();
        }

        /// <summary>MainTimer_Tick's phase work: HandleIntermittentBubbles (:1028), the spiral / bubble /
        /// brain drain ramps (UpdateRampingValues :761-815) and CheckDelayedFeatures (:867-965).</summary>
        private void TickPhases(Session session, double minutes)
        {
            var ss = session.Settings;
            var s = CoreSettings.Current;
            double total = session.DurationMinutes;

            // Intermittent bubbles. Burst end on the session clock (WPF: wall clock), so a pause
            // never eats a burst.
            if (ss.BubblesEnabled && ss.BubblesIntermittent)
            {
                if (_bubblesBurstActive && minutes >= _bubbleBurstEndMinute)
                {
                    _bubblesBurstActive = false;
                    SetBubblesActive(false, 0);
                    Log.Information("Bubble burst ended");
                }
                if (!_bubblesBurstActive && _bubbleBurstIndex < _bubbleBursts.Count && minutes >= _bubbleBursts[_bubbleBurstIndex])
                {
                    _bubblesBurstActive = true;
                    var duration = _random.Next(1, 3);   // 1-2 minutes
                    _bubbleBurstEndMinute = minutes + duration;
                    _bubbleBurstIndex++;
                    SetBubblesActive(true, ss.BubblesPerBurst);
                    Log.Information("Bubble burst started, duration: {Duration}min", duration);
                }
            }

            // Spiral ramp, after the randomised start.
            if (ss.SpiralEnabled && ss.SpiralOpacity != ss.SpiralOpacityEnd && minutes >= _randomizedSpiralStartMinute)
            {
                var progress = (minutes - _randomizedSpiralStartMinute) / (total - _randomizedSpiralStartMinute);
                SpiralOpacity = Lerp(ss.SpiralOpacity, ss.SpiralOpacityEnd, progress);
            }

            // Bubble frequency ramp: +1 every 5 minutes after a delayed start.
            if (ss.BubblesEnabled && !ss.BubblesIntermittent && ss.BubblesStartMinute > 0 && minutes >= ss.BubblesStartMinute)
            {
                var freq = ss.BubblesFrequency + (int)((minutes - ss.BubblesStartMinute) / 5);
                EmiRampStep(freq - ss.BubblesFrequency);   // WPF SessionEngine.cs:797
                if (s.BubblesFrequency != freq)
                {
                    s.BubblesFrequency = freq;
                    CoreBubbles.RefreshFrequency();
                }
            }

            // Brain drain intensity ramp: the service's live value, never the persisted setting.
            if (ss.BrainDrainEnabled && _brainDrainActive && ss.BrainDrainStartIntensity != ss.BrainDrainEndIntensity
                && minutes >= ss.BrainDrainStartMinute)
            {
                var duration = Math.Max(0.01, total - ss.BrainDrainStartMinute);
                var progress = Math.Clamp((minutes - ss.BrainDrainStartMinute) / duration, 0, 1);
                CoreBrainDrain.SetIntensity(Lerp(ss.BrainDrainStartIntensity, ss.BrainDrainEndIntensity, progress));
            }

            // Spiral delayed start at its randomised minute. The port always has a spiral (the
            // shipped one), so WPF's "no spiral files" skip never fires here.
            if (ss.SpiralEnabled && !s.SpiralEnabled && minutes >= _randomizedSpiralStartMinute)
            {
                if (ss.SpiralOpacity == ss.SpiralOpacityEnd) s.SpiralOpacity = ss.SpiralOpacity;
                s.SpiralEnabled = true;
                Log.Information("Spiral activated at {Minutes:F1} minutes (target was {Target:F1})", minutes, _randomizedSpiralStartMinute);
            }

            // Bubbles delayed start.
            if (ss.BubblesEnabled && !s.BubblesEnabled && ss.BubblesStartMinute > 0 && !ss.BubblesIntermittent
                && minutes >= ss.BubblesStartMinute)
            {
                s.BubblesEnabled = true;
                CoreBubbles.Start();
            }

            // Brain drain start / end minutes.
            if (ss.BrainDrainEnabled && !_brainDrainActive && ss.BrainDrainStartMinute > 0 && minutes >= ss.BrainDrainStartMinute)
            {
                _brainDrainActive = true;
                s.BrainDrainEnabled = true;
                s.BrainDrainIntensity = ss.BrainDrainStartIntensity;
                CoreBrainDrain.Start();
                Log.Information("Brain Drain activated at {Minutes:F1} minutes (target was {Target})", minutes, ss.BrainDrainStartMinute);
            }
            if (_brainDrainActive && ss.BrainDrainEndMinute > 0 && minutes >= ss.BrainDrainEndMinute)
            {
                _brainDrainActive = false;
                s.BrainDrainEnabled = false;
                CoreBrainDrain.Stop();
                Log.Information("Brain Drain deactivated at {Minutes:F1} minutes (end was {End})", minutes, ss.BrainDrainEndMinute);
            }
        }

        /// <summary>WPF MainWindow.SetBubblesActive (MainWindow.Presets.cs:2139): a burst runs at twice
        /// the per-burst count; its end stops the field.</summary>
        private static void SetBubblesActive(bool active, int bubblesPerBurst)
        {
            var s = CoreSettings.Current;
            if (active)
            {
                s.BubblesEnabled = true;
                s.BubblesFrequency = bubblesPerBurst * 2;
                CoreBubbles.Start();
                CoreBubbles.RefreshFrequency();
            }
            else
            {
                CoreBubbles.Stop();
                s.BubblesEnabled = false;
            }
        }

        private void StopPhases()
        {
            SpiralOpacity = null;
            _brainDrainActive = false;
            _bubblesBurstActive = false;
            _bubbleBursts.Clear();
        }
    }
}
