using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Services.UI;               // MonitorTarget
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Owns the Pink Filter tint: one <see cref="TintOverlayWindow"/> per targeted monitor, kept
    /// in step with <see cref="Models.AppSettings"/>. The Avalonia counterpart of
    /// <c>OverlayService</c>'s Pink region (StartPinkFilter / StopPinkFilter /
    /// UpdatePinkFilterOpacity / RefreshFilterColor), minus everything that belongs to the session
    /// engine - the timed and sustained holds, the Deeper ramp override and the 500ms poll all
    /// live in <c>OverlayService</c> and have nothing on this head to drive them.
    ///
    /// <para><b>Static, and not a service, deliberately.</b> There is exactly one desktop and one
    /// tint on it; an instance would need an owner, and the only candidate owner is the shell
    /// window this thing has to outlive between tab switches.</para>
    ///
    /// <para><b>Two refusals, and both matter more than showing the tint:</b> a full-screen
    /// topmost window that is not click-through locks the user out of their own desktop, and a
    /// window whose transparency request was denied paints an opaque screen-sized block instead of
    /// a wash. Either one is caught in <see cref="Accept"/> and the window is closed rather than
    /// left up. That is why this feature does NOT work through the Avalonia head on Windows or
    /// under a native Wayland backend: <see cref="X11Overlay"/> is X11-only and answers false
    /// there, so no tint is shown at all rather than an un-dismissable one.</para>
    /// </summary>
    internal static class PinkFilterOverlay
    {
        private static readonly List<TintOverlayWindow> Windows = new();
        private static int[] _shownOn = Array.Empty<int>();
        private static Window? _hookedOwner;

        /// <summary>The colour the tint renders: the user's pick if set, else the active mod's
        /// filter colour, which unseeded is the built-in default manifest's. Same order as
        /// <c>OverlayService.GetFilterRgb</c>.</summary>
        internal static (byte R, byte G, byte B) EffectiveColor() =>
            CoreMods.TryParseHexColor(CoreSettings.Current.PinkFilterColor, out var rgb)
                ? rgb
                : CoreMods.GetFilterColorRgb();

        /// <summary>
        /// Bring the tint in line with the current settings: show it, hide it, move it or repaint
        /// it as those say. Idempotent, so every card handler can just call this.
        /// </summary>
        /// <param name="host">Any attached visual - it is only used to reach <c>Screens</c> and
        /// the owning window. A detached one enumerates no screens and hides the tint.</param>
        public static void Refresh(Visual host)
        {
            var s = CoreSettings.Current;
            if (!s.PinkFilterEnabled || !ShouldShow()) { CloseAll(); return; }

            var screens = ScreenList.Enumerate(host);
            var primary = -1;
            for (var i = 0; i < screens.Count; i++) if (screens[i].IsPrimary) { primary = i; break; }

            var want = ResolveScreenIndices(s.PinkFilterTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
            var (r, g, b) = EffectiveColor();
            var opacity = s.PinkFilterOpacity / 100.0;

            // Same monitors as last time: repaint the brushes and leave the windows alone. This is
            // the whole reason the slider does not tear down a full-screen window per tick.
            if (Windows.Count > 0 && want.SequenceEqual(_shownOn))
            {
                foreach (var w in Windows) w.SetTint(r, g, b, opacity);
                return;
            }

            CloseAll();

            // Checked BEFORE anything is created, not only inside Accept: without this, a slider
            // drag on a platform that cannot do click-through flashes a topmost full-screen window
            // once per tick and logs a warning each time. Accept still runs - IsAvailable being
            // true does not promise THIS window is an X11 one, nor that the compositor granted
            // transparency.
            if (!X11Overlay.IsAvailable)
            {
                Log.Debug("Pink filter: no X11 display, so no tint on this platform");
                return;
            }

            HookOwner(TopLevel.GetTopLevel(host) as Window);

            foreach (var i in want)
            {
                var w = new TintOverlayWindow();
                w.PlaceOn(screens[i]);
                w.SetTint(r, g, b, opacity);
                w.Show();                       // Position -> Show -> click-through, the order
                                                // BubbleCountWindow uses; there is no XID before Show
                // A refusal is about the platform, not about this monitor, so drop the whole set
                // rather than tinting some screens and not others.
                if (!Accept(w)) { CloseAll(); return; }
                Windows.Add(w);
            }
            _shownOn = want;
            Log.Debug("Pink filter showing on {Count} screen(s) at {Opacity}%", Windows.Count, s.PinkFilterOpacity);
        }

        public static void CloseAll()
        {
            foreach (var w in Windows)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("Pink filter: failed to close a tint window: {E}", ex.Message); }
            }
            Windows.Clear();
            _shownOn = Array.Empty<int>();
            if (_hookedOwner is not null) { _hookedOwner.Closed -= OnOwnerClosed; _hookedOwner = null; }
        }

        /// <summary>
        /// Which entries of the screen list an effect targets, ported case for case from
        /// <c>App.ResolveScreens</c> (ConditioningControlPanel/App.ScreenResolver.cs). The sentinel
        /// numbers are PERSISTED, so this has to agree with the WPF head exactly:
        /// <see cref="MonitorTarget.All"/> is every monitor, <see cref="MonitorTarget.FollowGlobal"/>
        /// is every monitor when <c>DualMonitorEnabled</c> and the primary otherwise, and 0..N is
        /// that one index. An index that no longer exists (unplugged monitor) falls back to the
        /// FollowGlobal behaviour WITHOUT rewriting the setting, so the target survives a reconnect.
        /// </summary>
        internal static int[] ResolveScreenIndices(int target, bool dualMonitorEnabled, int screenCount, int primaryIndex)
        {
            if (screenCount <= 0) return Array.Empty<int>();
            if (primaryIndex < 0 || primaryIndex >= screenCount) primaryIndex = 0;

            if (target == MonitorTarget.All) return Enumerable.Range(0, screenCount).ToArray();
            if (target >= 0 && target < screenCount) return new[] { target };
            return dualMonitorEnabled ? Enumerable.Range(0, screenCount).ToArray() : new[] { primaryIndex };
        }

        /// <summary>
        /// WPF gates the persistent tint on the session engine: <c>RefreshOverlays</c> returns
        /// early when <c>!_isRunning</c>, so the checkbox there arms the effect and
        /// <c>OverlayService.Start()</c> is what puts it on screen.
        ///
        /// <para>That gate is mirrored here <b>except when there is no engine at all</b>, which is
        /// this head today - <see cref="CoreSession"/> is unseeded, so the provider is null and
        /// "not running" is a statement about the head rather than about the session. Mirroring it
        /// literally would mean the checkbox could never draw anything on Linux, ever. With no
        /// engine to own the tint's lifecycle, the card owns it; the moment a head seeds
        /// <c>CoreSession</c> this falls back to the WPF rule on its own.</para>
        /// </summary>
        private static bool ShouldShow()
            => CoreSession.IsEngineRunningProvider is null || CoreSession.IsEngineRunning;

        // ponytail: nothing calls Refresh at startup, so a tint left enabled in settings does not
        // come back until the user touches the card. Restoring it belongs to whatever owns the
        // app's start-up sequence - OverlayService.Start() on the WPF side - and putting it in
        // App.OnFrameworkInitializationCompleted here would show a full-screen overlay before the
        // shell window the user needs in order to switch it off is even up.

        /// <summary>The two safety refusals. False means the window has already been closed.</summary>
        private static bool Accept(TintOverlayWindow w)
        {
            if (!X11Overlay.SetClickThrough(w, true))
            {
                Log.Warning("Pink filter: this platform cannot make the tint click-through, so it is not shown - a full-screen topmost window that swallows clicks would lock the desktop");
                Close(w);
                return false;
            }

            if (w.ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                Log.Warning("Pink filter: the window manager refused per-pixel transparency (no compositor), so the tint is not shown - it would paint an opaque block, not a wash");
                Close(w);
                return false;
            }

            return true;
        }

        private static void Close(Window w)
        {
            try { w.Close(); } catch (Exception ex) { Log.Debug("Pink filter: close failed: {E}", ex.Message); }
        }

        /// <summary>
        /// Avalonia's default <c>ShutdownMode</c> is <c>OnLastWindowClose</c>, and these windows
        /// are windows. Without this, closing the shell while the tint is up leaves a live process
        /// behind a pink sheet with no UI to turn it off.
        /// </summary>
        private static void HookOwner(Window? owner)
        {
            if (owner is null || ReferenceEquals(owner, _hookedOwner)) return;
            if (_hookedOwner is not null) _hookedOwner.Closed -= OnOwnerClosed;
            _hookedOwner = owner;
            owner.Closed += OnOwnerClosed;
        }

        private static void OnOwnerClosed(object? sender, EventArgs e) => CloseAll();
    }
}
