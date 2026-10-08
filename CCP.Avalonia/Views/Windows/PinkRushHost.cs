// PORTED from ConditioningControlPanel/Services/Progression/SkillTreeService.cs (#region Pink Rush, Start :866,
// Stop :895) and MainWindow.Enhancements.cs OnPinkRushStarted/OnPinkRushEnded (:2121, :2186). The rules
// (roll, window, 3x) are Core PinkRushRules; this owns the two timers, the wash and the popup.
// Lockdown: WPF refuses nothing here (the popup is a dismissable toast, not a door).

using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    internal static class PinkRushHost
    {
        /// <summary>Clock and roll seams: tests step them (P08).</summary>
        internal static Func<DateTime> Now = () => DateTime.Now;
        internal static Func<double> Roll = Random.Shared.NextDouble;

        private static DispatcherTimer? _check;
        private static DispatcherTimer? _expiry;

        /// <summary>The open popup, if any (WPF MainWindow._pinkRushPopup).</summary>
        internal static PinkRushPopup? Popup { get; private set; }

        internal static bool IsChecking => _check?.IsEnabled == true;

        /// <summary>WPF SkillTreeService.Start, called by StartEngine: arm the check when the skill is owned.</summary>
        internal static void Start()
        {
            if (!SkillTreeRules.HasSkill(CoreSettings.Current, PinkRushRules.SkillId)) return;
            if (_check is null)
            {
                _check = new DispatcherTimer { Interval = PinkRushRules.CheckInterval };
                _check.Tick += (_, _) => CheckTick();
            }
            _check.Start();
        }

        /// <summary>WPF PinkRushCheckTimer_Tick.</summary>
        internal static void CheckTick()
        {
            if (PinkRushRules.ShouldStart(CoreSettings.Current, Roll())) Begin();
        }

        /// <summary>WPF StartPinkRush + OnPinkRushStarted.</summary>
        internal static void Begin()
        {
            var s = CoreSettings.Current;
            PinkRushRules.Begin(s, Now());
            if (_expiry is null)
            {
                _expiry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _expiry.Tick += (_, _) => ExpiryTick();
            }
            _expiry.Start();
            Log.Information("Pink Rush activated! 60 seconds of 3x XP");

            Wash();
            ClosePopup();
            // Perk-announcement opt-out: only the popup goes; the 3x and the wash stay (WPF :2160).
            if (s.SuppressPerkNotifications)
            {
                Log.Information("Pink Rush activated! Popup suppressed by SuppressPerkNotifications.");
                return;
            }
            Popup = new PinkRushPopup(s.PinkRushEndTime!.Value);
            Popup.Closed += (sender, _) => { if (ReferenceEquals(sender, Popup)) Popup = null; };
            Popup.Show();
        }

        /// <summary>WPF PinkRushTimer_Tick.</summary>
        internal static void ExpiryTick()
        {
            if (PinkRushRules.IsDue(CoreSettings.Current, Now())) End();
        }

        /// <summary>WPF EndPinkRush + OnPinkRushEnded: the 3x stops and the popup closes.</summary>
        internal static void End()
        {
            PinkRushRules.Clear(CoreSettings.Current);
            _expiry?.Stop();
            ClosePopup();
            Log.Information("Pink Rush ended");
        }

        /// <summary>WPF SkillTreeService.Stop, called on every engine stop (so panic and exit too).</summary>
        internal static void Stop()
        {
            _check?.Stop();
            _expiry?.Stop();
            if (CoreSettings.Current.PinkRushActive) End();
            else ClosePopup();
        }

        private static void ClosePopup()
        {
            var p = Popup;
            Popup = null;
            try { p?.Close(); } catch { /* already closing */ }
        }

        /// <summary>WPF's half-second pink wash (FF1493 at alpha 100, window opacity 0.6 -> 0 over 500 ms).
        /// Only where a click-through tint can reach the screen, as PinkFilterOverlay refuses otherwise.</summary>
        private static void Wash()
        {
            try
            {
                var host = MainShellWindow.Current;
                if (host is null || !PinkFilterOverlay.CanShowTint(host)) return;
                foreach (var screen in ScreenList.Enumerate(host))
                {
                    var w = new TintOverlayWindow { Opacity = 0.6 };
                    w.PlaceOn(screen);
                    w.SetTint(0xFF, 0x14, 0x93, 100 / 255.0);
                    w.Show();
                    if (!X11Overlay.SetClickThrough(w, true)) { w.Close(); return; }
                    w.Transitions = new Transitions
                    {
                        new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(500) }
                    };
                    w.Opacity = 0;
                    DispatcherTimer.RunOnce(() => { try { w.Close(); } catch { } }, TimeSpan.FromMilliseconds(500));
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Pink Rush flash effect failed: {Error}", ex.Message);
            }
        }
    }
}
