// PORTED from WPF 7.1.5 ConditioningControlPanel/MainWindow/MainWindow.SectionEdge.cs (nav polish
// wave 9, fog band polish 11) plus GlowNavTarget from MainWindow.SectionChrome.cs.
//
// The window's 3 px frame and a faint 28 px band inside it wear the SECTION hue; the colour
// change crossfades with the page wash (SectionChromeRules.SectionWashMs, eased out). The
// travelling lift rides four 3 px strips (Controls/NavRail/SectionEdgeLift), never a transform on
// the full-window border. EdgeParticles runs the fog (and at Full the embers) on four strips;
// while the fog runs the flat band thins to SectionEdgeRules.FogBand at FogBandShare of its alpha.
// Every number is Core (ConditioningControlPanel.Nav.SectionEdgeRules / NavGlowRules,
// ConditioningControlPanel.Fx.EdgeParticleRules); this file only paints.
//
// Hooks, all from this file (no other partial changes):
//   * SectionEdgeHost_Loaded (MainShellWindow.axaml) collects the parts and paints the first hue.
//   * the section: PaintSectionWash (TabNavigation.cs) repaints SectionWashLine on every section
//     change; this file follows that repaint and reads _washSection. Seam: a direct
//     PaintSectionEdge call in PaintSectionWash would replace the observer.
//   * the motion level: AmbientFxCanvas.Env.MotionGateChanged, which Settings > Performance
//     raises on a level change (WPF CmbMotionLevel_SelectionChanged's edge refresh) and the OS
//     reduced-motion probe raises too.
//   * the window: Activated / Deactivated / WindowState / IsVisible re-check the lap.

using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Nav;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private Rectangle[] _edgeBands = Array.Empty<Rectangle>();
        private Border? _edgeLine;
        private EdgeParticles? _edgeParticles;
        private SectionEdgeLift? _edgeLift;
        private uint _edgeHue = NavStripRules.Lilac;
        private bool _edgeReady, _edgeFogLive;

        // The crossfade: what is painted now, where it is going, and the clock moving it.
        private uint[] _edgeGlowNow = Array.Empty<uint>(), _edgeLineNow = Array.Empty<uint>(), _edgeLiftNow = Array.Empty<uint>();
        private uint[] _edgeGlowFrom = Array.Empty<uint>(), _edgeLineFrom = Array.Empty<uint>(), _edgeLiftFrom = Array.Empty<uint>();
        private uint[] _edgeGlowTo = Array.Empty<uint>(), _edgeLineTo = Array.Empty<uint>(), _edgeLiftTo = Array.Empty<uint>();
        private FrameClock? _edgeFadeClock;
        private readonly Stopwatch _edgeFadeWatch = new();
        private int _edgeFadeMs;

        /// <summary>The hue the edge wears (or is fading to), ARGB (tests, NavCheck).</summary>
        internal uint SectionEdgeHue => _edgeHue;

        /// <summary>The three line, glow and lift colours painted right now (tests).</summary>
        internal (uint[] Glow, uint[] Line, uint[] Lift) SectionEdgePainted => (_edgeGlowNow, _edgeLineNow, _edgeLiftNow);

        /// <summary>The section edge's parts (tests).</summary>
        internal EdgeParticles? SectionEdgeStrips => _edgeParticles;
        internal SectionEdgeLift? SectionEdgeLiftStrips => _edgeLift;
        internal double SectionEdgeBandDepth => _edgeBands.Length > 0 ? _edgeBands[0].Height : 0;

        private void SectionEdgeHost_Loaded(object? sender, RoutedEventArgs e) => InitializeSectionEdge();

        /// <summary>Collects the edge's parts, wires the hooks, paints the current section at once.</summary>
        internal void InitializeSectionEdge()
        {
            if (_edgeReady) return;
            try
            {
                _edgeBands = new[] { "SectionEdgeBandTop", "SectionEdgeBandBottom", "SectionEdgeBandLeft", "SectionEdgeBandRight" }
                    .Select(n => Named<Rectangle>(n)).Where(r => r != null).Select(r => r!).ToArray();
                _edgeLine = Named<Border>("GlassWindowEdge");
                _edgeParticles = Named<EdgeParticles>("SectionEdgeParticles");
                _edgeLift = Named<SectionEdgeLift>("SectionEdgeLift");
                if (Named<Grid>("SectionEdgeHost") is { } host)
                {
                    _edgeFadeClock = new FrameClock(host) { Interval = TimeSpan.FromMilliseconds(16) };
                    _edgeFadeClock.Tick += (_, _) => StepEdgeFade();
                }
                _edgeReady = true;

                // The section: follow the page wash's repaint (see the header).
                if (Named<Border>("SectionWashLine") is { } washLine)
                    washLine.PropertyChanged += (_, a) =>
                    {
                        if (a.Property == Border.BackgroundProperty) OnSectionWashRepainted();
                    };

                // The motion-level fan-out: a level change repaints the edge and re-gates the strips.
                AmbientFxCanvas.Env.MotionGateChanged += RefreshSectionEdge;
                Closed += (_, _) => AmbientFxCanvas.Env.MotionGateChanged -= RefreshSectionEdge;

                Activated += (_, _) => UpdateSectionEdgeMotion();
                Deactivated += (_, _) => UpdateSectionEdgeMotion();
                PropertyChanged += (_, a) =>
                {
                    if (a.Property == WindowStateProperty || a.Property == IsVisibleProperty) UpdateSectionEdgeMotion();
                };

                _edgeHue = NavStripRules.Accent(_washSection ?? NavSections.Home);
                PaintSectionEdge(_edgeHue, 0);
            }
            catch (Exception ex) { Log.Debug("InitializeSectionEdge failed: {E}", ex.Message); }
        }

        private void OnSectionWashRepainted()
        {
            if (_washSection == null) return;
            var hue = NavStripRules.Accent(_washSection);
            if (hue == _edgeHue && _edgeReady) return;
            PaintSectionEdge(hue, NavRailRules.Ms(SectionChromeRules.SectionWashMs, AmbientFxCanvas.Env.Level));
        }

        /// <summary>WPF CmbMotionLevel_SelectionChanged's edge refresh: the lift, the band and the
        /// strips follow the new level now, not at the next section change.</summary>
        private void RefreshSectionEdge()
        {
            try { PaintSectionEdge(_edgeHue, 0); _edgeParticles?.Refresh(); }
            catch (Exception ex) { Log.Debug("Edge motion refresh failed: {E}", ex.Message); }
        }

        /// <summary>Retints the frame line, the glow band and the lift to <paramref name="hue"/>
        /// over <paramref name="ms"/> (0 = at once), then re-checks the travelling lift.</summary>
        internal void PaintSectionEdge(uint hue, int ms)
        {
            _edgeHue = hue;
            if (!_edgeReady) return;   // InitializeSectionEdge paints _edgeHue once loaded
            try
            {
                var level = AmbientFxCanvas.Env.Level;

                // The strips first: whether the fog runs decides how strong the band is.
                _edgeParticles?.Mount(hue, ms);
                bool fogLive = _edgeParticles?.FogLive == true;
                if (fogLive != _edgeFogLive || ms <= 0) SetEdgeBandDepth(SectionEdgeRules.BandDepth(fogLive));
                _edgeFogLive = fogLive;

                StartEdgeFade(SectionEdgeRules.GlowStops(hue, fogLive),
                              SectionEdgeRules.LineStops(hue, level),
                              SectionEdgeRules.LiftStops(hue, level), ms);
                UpdateSectionEdgeMotion();
            }
            catch (Exception ex) { Log.Debug("PaintSectionEdge failed: {E}", ex.Message); }
        }

        /// <summary>The four bands' depth into the window (top and bottom a height, sides a width).</summary>
        private void SetEdgeBandDepth(double depth)
        {
            foreach (var band in _edgeBands)
            {
                if (band.VerticalAlignment is VerticalAlignment.Top or VerticalAlignment.Bottom
                    && band.HorizontalAlignment == HorizontalAlignment.Stretch)
                    band.Height = depth;
                else
                    band.Width = depth;
            }
        }

        // ---- the crossfade (WPF ColorAnimation per stop, QuadraticEase EaseOut) -------------------

        private void StartEdgeFade(uint[] glow, uint[] line, uint[] lift, int ms)
        {
            _edgeGlowTo = glow; _edgeLineTo = line; _edgeLiftTo = lift;
            if (ms <= 0 || _edgeGlowNow.Length == 0 || _edgeFadeClock == null)
            {
                _edgeFadeClock?.Stop();
                PaintEdgeStops(glow, line, lift);
                return;
            }
            _edgeGlowFrom = _edgeGlowNow; _edgeLineFrom = _edgeLineNow; _edgeLiftFrom = _edgeLiftNow;
            _edgeFadeMs = ms;
            _edgeFadeWatch.Restart();
            _edgeFadeClock.Start();
        }

        private void StepEdgeFade() => StepEdgeFade(_edgeFadeWatch.Elapsed.TotalMilliseconds);

        /// <summary>Paints the crossfade <paramref name="elapsedMs"/> in (tests drive it directly).</summary>
        internal void StepEdgeFade(double elapsedMs)
        {
            double t = _edgeFadeMs <= 0 ? 1 : Math.Clamp(elapsedMs / _edgeFadeMs, 0, 1);
            double eased = 1 - (1 - t) * (1 - t);
            PaintEdgeStops(Lerp(_edgeGlowFrom, _edgeGlowTo, eased), Lerp(_edgeLineFrom, _edgeLineTo, eased),
                           Lerp(_edgeLiftFrom, _edgeLiftTo, eased));
            if (t >= 1) _edgeFadeClock?.Stop();
        }

        private static uint[] Lerp(uint[] from, uint[] to, double t)
        {
            if (from.Length != to.Length) return to;
            var r = new uint[to.Length];
            for (int i = 0; i < to.Length; i++) r[i] = LerpArgb(from[i], to[i], t);
            return r;
        }

        /// <summary>A straight per-channel ARGB mix (WPF ColorAnimation interpolates the same way).</summary>
        internal static uint LerpArgb(uint a, uint b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            uint Ch(int shift)
            {
                double x = (a >> shift) & 0xFF, y = (b >> shift) & 0xFF;
                return (uint)Math.Round(x + (y - x) * t) << shift;
            }
            return Ch(24) | Ch(16) | Ch(8) | Ch(0);
        }

        private void PaintEdgeStops(uint[] glow, uint[] line, uint[] lift)
        {
            _edgeGlowNow = glow; _edgeLineNow = line; _edgeLiftNow = lift;
            var offsets = SectionEdgeRules.GlowOffsets;
            var stops = glow.Select((c, i) => (c, offsets[Math.Min(i, offsets.Length - 1)])).ToArray();
            foreach (var band in _edgeBands)
            {
                // Each band fades from its window edge inward.
                var (start, end) = band.Name switch
                {
                    "SectionEdgeBandBottom" => (new RelativePoint(0, 1, RelativeUnit.Relative), new RelativePoint(0, 0, RelativeUnit.Relative)),
                    "SectionEdgeBandLeft" => (new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative)),
                    "SectionEdgeBandRight" => (new RelativePoint(1, 0, RelativeUnit.Relative), new RelativePoint(0, 0, RelativeUnit.Relative)),
                    _ => (new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative)),
                };
                band.Fill = NavPaint.Linear(stops, start, end);
            }
            if (_edgeLine != null && line.Length >= 3)
                _edgeLine.BorderBrush = NavPaint.Horizontal(new[] { (line[0], 0.0), (line[1], 0.5), (line[2], 1.0) });
            _edgeLift?.SetStops(lift);
        }

        // ---- the travelling lift ------------------------------------------------------------------

        /// <summary>Starts, pauses, resumes or parks the travelling lift for the motion level and
        /// window state. Full runs the lap only while the window is active, shown and not
        /// minimised; Reduced holds the lift still at the top centre; Off hides the strips.</summary>
        private void UpdateSectionEdgeMotion()
        {
            if (!_edgeReady || _edgeLift == null) return;
            try
            {
                var motion = SectionEdgeRules.MotionFor(AmbientFxCanvas.Env.Level,
                    AmbientFxCanvas.Env.AllowAmbientLoops && !FxBisect.Off("edgeglow"));
                bool run = SectionEdgeRules.SpinShouldRun(motion, IsActive || FxBisect.Off("forceactive"), IsVisible,
                                                          WindowState == WindowState.Minimized);
                _edgeLift.Apply(motion, run);
            }
            catch (Exception ex) { Log.Debug("UpdateSectionEdgeMotion failed: {E}", ex.Message); }
        }

        // ---- the "moved here" glow (WPF MainWindow.SectionChrome.cs GlowNavTarget) ----------------

        /// <summary>Palette door: a Ctrl+K row that lands inside a page rings its target the way
        /// Show me does.</summary>
        internal void GlowNavKey(string key) => GlowNavTarget(key);

        /// <summary>
        /// Glow the place something moved to: a Settings section key ("account", "monitors")
        /// lights that section's pill; a tab key lights its strip pill and its rail row (the row
        /// the tab lights). Full = 600 ms sheen + 2 s glow, Reduced = the glow alone, Off =
        /// nothing (NavGlow). Every silent return writes a Debug line. True when anything glowed.
        /// </summary>
        internal bool GlowNavTarget(string tabKey)
        {
            try
            {
                var level = AmbientFxCanvas.Env.Level;
                if (string.IsNullOrEmpty(tabKey)) { Log.Debug("GlowNavTarget: empty key"); return false; }
                if (level == MotionLevel.Off) { Log.Debug("GlowNavTarget({Key}): motion off", tabKey); return false; }

                if (SettingsSectionLabelKey(tabKey) != null && tabKey != "appsettings")
                {
                    var pillName = "SectionPill" + char.ToUpperInvariant(tabKey[0]) + tabKey.Substring(1);
                    if (AppSettingsPage?.FindControl<Control>(pillName) is { } settingsPill)
                        return NavGlow.Once(settingsPill, NavStripRules.Accent(NavSections.Settings), level, "settings." + tabKey);
                    Log.Debug("GlowNavTarget({Key}): no Settings pill {Name}", tabKey, pillName);
                    return false;
                }

                var section = NavSections.SectionForTab(tabKey);
                var accent = NavStripRules.Accent(section);
                bool any = false;
                if (NavStrip?.PillFor(NavStripRules.ActivePill(tabKey) ?? tabKey) is { } pill)
                    any |= NavGlow.Once(pill, accent, level, "pill." + tabKey);
                if (_navSectionRows.FirstOrDefault(r => r.Section == section)?.Button is { } row)
                    any |= NavGlow.Once(row, accent, level, "rail." + tabKey);
                if (!any) Log.Debug("GlowNavTarget({Key}): nothing to glow (no pill, no rail row)", tabKey);
                return any;
            }
            catch (Exception ex) { Log.Debug("GlowNavTarget({Key}) failed: {E}", tabKey, ex.Message); return false; }
        }
    }
}
