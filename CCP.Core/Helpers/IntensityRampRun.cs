using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// The Intensity Ramp runtime: WPF MainWindow.StartStop.cs StartRampTimer (:575), StopRampTimer
    /// (:601) and RampTimer_Tick (:649), minus the timer. The head ticks it every 2 s while the
    /// engine runs; the arithmetic, the caps and the session rule are WPF's exactly.
    /// Start captures the base values; every tick writes base x factor (clamped to the feature's own
    /// cap); Stop writes the base values back. The visual links sit still while a preset session runs
    /// (sessions ramp themselves), and the ramp never ends the engine under a session (#444).
    /// </summary>
    public sealed class IntensityRampRun
    {
        /// <summary>WPF DispatcherTimer interval: "Update every 2 seconds".</summary>
        public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

        private readonly Dictionary<string, double> _base = new();
        private DateTime _startTime;

        public bool IsActive => _base.Count > 0;

        public void Start(AppSettings settings, DateTime now)
        {
            _base["FlashOpacity"] = settings.FlashOpacity;
            _base["SpiralOpacity"] = settings.SpiralOpacity;
            _base["PinkFilterOpacity"] = settings.PinkFilterOpacity;
            _base["MasterVolume"] = settings.MasterVolume;
            _base["SubAudioVolume"] = settings.SubAudioVolume;
            _base["BrainDrainBlurStrength"] = settings.BrainDrainBlurStrength;
            _startTime = now;
        }

        /// <summary>Restores the base values (WPF StopRampTimer). Safe to call when never started.</summary>
        public void Stop(AppSettings settings)
        {
            if (_base.Count == 0) return;
            if (_base.TryGetValue("FlashOpacity", out var flashOp)) settings.FlashOpacity = (int)flashOp;
            if (_base.TryGetValue("SpiralOpacity", out var spiralOp)) settings.SpiralOpacity = (int)spiralOp;
            if (_base.TryGetValue("PinkFilterOpacity", out var pinkOp)) settings.PinkFilterOpacity = (int)pinkOp;
            if (_base.TryGetValue("MasterVolume", out var masterVol)) settings.MasterVolume = (int)masterVol;
            if (_base.TryGetValue("SubAudioVolume", out var subVol)) settings.SubAudioVolume = (int)subVol;
            if (_base.TryGetValue("BrainDrainBlurStrength", out var bdBlur)) settings.BrainDrainBlurStrength = (int)bdBlur;
            _base.Clear();
        }

        /// <summary>One WPF RampTimer_Tick. Returns true when the ramp is complete and the engine
        /// should stop (EndSessionOnRampComplete, never while a session runs).</summary>
        public bool Tick(AppSettings settings, DateTime now, bool sessionActive)
        {
            if (_base.Count == 0) return false;
            var elapsed = (now - _startTime).TotalMinutes;
            var duration = settings.RampDurationMinutes;
            var progress = Math.Min(elapsed / duration, 1.0);
            var mult = RampMath.ResolveFactor(settings, progress);

            if (!sessionActive && settings.RampLinkFlashOpacity && _base.TryGetValue("FlashOpacity", out var flashBase))
                settings.FlashOpacity = (int)Math.Clamp(flashBase * mult, 0, 100);
            if (!sessionActive && settings.RampLinkSpiralOpacity && _base.TryGetValue("SpiralOpacity", out var spiralBase))
                settings.SpiralOpacity = (int)Math.Clamp(spiralBase * mult, 0, 100);
            if (!sessionActive && settings.RampLinkPinkFilterOpacity && _base.TryGetValue("PinkFilterOpacity", out var pinkBase))
                settings.PinkFilterOpacity = (int)Math.Clamp(pinkBase * mult, 0, 50);
            if (settings.RampLinkMasterAudio && _base.TryGetValue("MasterVolume", out var masterBase))
                settings.MasterVolume = (int)Math.Clamp(masterBase * mult, 0, 100);
            if (settings.RampLinkSubliminalAudio && _base.TryGetValue("SubAudioVolume", out var subBase))
                settings.SubAudioVolume = (int)Math.Clamp(subBase * mult, 0, 100);
            if (!sessionActive && settings.RampLinkBrainDrain && _base.TryGetValue("BrainDrainBlurStrength", out var bdBase))
                settings.BrainDrainBlurStrength = (int)Math.Clamp(bdBase * mult, 0, 100);

            return progress >= 1.0 && settings.EndSessionOnRampComplete && !sessionActive;
        }
    }
}
