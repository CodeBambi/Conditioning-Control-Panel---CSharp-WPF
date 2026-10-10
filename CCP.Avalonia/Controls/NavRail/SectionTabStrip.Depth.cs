// PORTED from WPF 7.1.5 ConditioningControlPanel/Controls/NavRail/SectionTabStrip.Depth.cs
// (nav polish wave 10, the depth law on the page header). The tray is SUNKEN: a well floor under
// the track fill, a 9 px inner top band, a 6 px inner left band and a lit 1 px foot. Every pill is
// RAISED: a drop band in the section's shadow ink (DepthRules.ShadowColor) under the face, the
// face lifts on hover, drops on press, and the lit pill sits PRESSED IN its socket
// (DepthPressedShade + DepthPressedBevel ride the sliding fill, ActiveSinkPx down). Every number
// comes from Core DepthRules / HudPlankRules; every brush from Core DepthPalette. No Effect, no
// BoxShadow: bands are gradient Borders, as WPF.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Motion;

namespace ConditioningControlPanel.Avalonia.Controls.NavRail
{
    public sealed partial class SectionTabStrip
    {
        /// <summary>The pill drop band's inset from the pill's rounded ends.</summary>
        internal const double PillDropInset = 8;

        private readonly Border TrayFloor = new() { CornerRadius = new CornerRadius(21), IsHitTestVisible = false };
        private readonly Border TrayWellTop = new() { CornerRadius = new CornerRadius(21), IsHitTestVisible = false };
        private readonly Border TrayWellLeft = new() { CornerRadius = new CornerRadius(21), IsHitTestVisible = false };
        private readonly Border TrayWellFoot = new()
        {
            CornerRadius = new CornerRadius(21),
            IsHitTestVisible = false,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        private IBrush _pillDrop = Brushes.Transparent;
        private Border? _activePress;

        /// <summary>A Depth palette band mapped in pixels: <paramref name="px"/> long down (or
        /// across), so a 9 px well band stays 9 px on a 49 px tray (WPF PageDepthPaint.Band).</summary>
        internal static IBrush? Band(string key, double px, bool across = false)
        {
            if (DepthPalette.Find(key) is not { Kind: DepthBrushKind.Linear } b) return null;
            var start = new RelativePoint(0, 0, RelativeUnit.Absolute);
            var end = across ? new RelativePoint(px, 0, RelativeUnit.Absolute) : new RelativePoint(0, px, RelativeUnit.Absolute);
            return NavPaint.Linear(b.Stops, start, end);
        }

        /// <summary>The strip's depth paint for a section hue: the sunken tray and the hue-tinted
        /// drop band under every raised pill. Called from PaintFor, so it follows the section.</summary>
        internal void PaintDepthPages(uint hue)
        {
            TrayFloor.Background = NavPaint.Depth("DepthWellFloorBrush");
            TrayWellTop.Background = Band("DepthWellTop", DepthRules.WellPx);
            TrayWellLeft.Background = Band("DepthWellLeft", 6, across: true);
            TrayWellFoot.BorderBrush = NavPaint.Depth("DepthWellFoot");
            _pillDrop = global::ConditioningControlPanel.Avalonia.Controls.Depth.DepthPaint.ShadowBand(hue, DepthRules.ShadowAlpha);

            // The lit tab is pressed IN: the pressed shade and bevel ride the sliding fill, and the
            // fill sits ActiveSinkPx down with it.
            _activePress ??= new Border
            {
                CornerRadius = new CornerRadius(19),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            };
            _activePress.Background = NavPaint.Depth("DepthPressedShade");
            _activePress.BorderBrush = NavPaint.Depth("DepthPressedBevel");
            if (!ReferenceEquals(ActiveFill.Child, _activePress)) ActiveFill.Child = _activePress;
            ActiveFillShift.Y = DepthRules.ActiveSinkPx;

            foreach (var p in _pills) DepthSettle(p, animate: false);
        }

        /// <summary>
        /// Put a pill where the law says for its state: the face sits TravelFor(...) down and the
        /// drop band is ShadowFor(...) long, none while pressed or lit. Press goes down in PressMs;
        /// anything else springs back in ReleaseMs through a ReleaseOvershootPx overshoot
        /// (HudPlankRules.FaceTrack, the same spring). Motion Off jumps, so rest looks the same.
        /// </summary>
        private void DepthSettle(PillParts p, bool animate)
        {
            bool active = IsActive(p.Tab.Key);
            bool pressed = p.Pressed;
            bool hovered = p.Pill.IsPointerOver || p.Hovered;
            double y = DepthRules.TravelFor(true, pressed, active, hovered: false);
            double drop = DepthRules.ShadowFor(true, pressed, active, hovered);

            if (p.Drop != null)
            {
                p.Drop.Background = _pillDrop;
                p.Drop.Opacity = drop > 0 ? 1 : 0;
                p.Drop.Height = drop + 2;
                p.Drop.Margin = new Thickness(PillDropInset, 0, PillDropInset, -drop);
            }

            var shift = p.FaceShift;
            if (shift == null) return;
            int ms = DepthRules.Ms(pressed ? DepthRules.PressMs : DepthRules.ReleaseMs, Level);
            if (!animate || ms <= 0 || Math.Abs(shift.Y - y) < 0.01)
            {
                p.DepthTimer?.Stop();
                shift.Y = y;
                return;
            }
            p.DepthFrom = shift.Y;
            p.DepthMs = ms;
            p.DepthTrack = HudPlankRules.FaceTrack(y, releasing: !pressed, ms);
            p.DepthStarted = Environment.TickCount64;
            p.DepthTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => DepthStep(p));
            p.DepthTimer.Start();
            DepthStep(p);
        }

        private static void DepthStep(PillParts p)
        {
            double t = Environment.TickCount64 - p.DepthStarted;
            if (p.FaceShift != null) p.FaceShift.Y = Keyframes.Sample(p.DepthTrack, p.DepthFrom, t);
            if (t >= p.DepthMs)
            {
                p.DepthTimer?.Stop();
                if (p.FaceShift != null && p.DepthTrack.Length > 0) p.FaceShift.Y = p.DepthTrack[^1].Value;
            }
        }

        private void DepthPress(PillParts p, bool down)
        {
            if (p.Pressed == down) return;
            p.Pressed = down;
            DepthSettle(p, animate: true);
        }

        private void DepthHover(PillParts p, bool on)
        {
            p.Hovered = on;
            DepthSettle(p, animate: true);
        }

        // ---- test seams ---------------------------------------------------------------------

        /// <summary>Test seam: how far a pill's face sits down now (px, + = down).</summary>
        internal double PillFaceTravel(string key) => Part(key)?.FaceShift?.Y ?? double.NaN;

        /// <summary>Test seam: a pill's drop band length now (0 = no band: pressed or lit).</summary>
        internal double PillDropLength(string key) =>
            Part(key)?.Drop is { } d && d.Opacity > 0 ? d.Height - 2 : 0;

        /// <summary>Test seam: a pill's drop band brush.</summary>
        internal IBrush? PillDropBrush(string key) => Part(key)?.Drop?.Background;

        /// <summary>Test seam: press (or release) a pill without a pointer.</summary>
        internal void DepthPressForTests(string key, bool down)
        {
            if (Part(key) is { } p) { p.Pressed = down; DepthSettle(p, animate: false); }
        }

        /// <summary>Test seam: hover (or leave) a pill without a pointer, for the depth state only.</summary>
        internal void DepthHoverForTests(string key, bool on)
        {
            if (Part(key) is { } p) { p.Hovered = on; DepthSettle(p, animate: false); }
        }

        internal IBrush? TrayFloorBrush => TrayFloor.Background;
        internal IBrush? TrayWellTopBrush => TrayWellTop.Background;
        internal IBrush? TrayWellLeftBrush => TrayWellLeft.Background;
        internal IBrush? TrayWellFootBrush => TrayWellFoot.BorderBrush;
        internal Border? ActivePressLayer => _activePress;
        internal double ActiveFillSink => ActiveFillShift.Y;
    }
}
