// PORTED from AvatarTube/AvatarTubeWindow.Speech.cs (WPF 7.1.5): :1040 CreateBubbleWindow, :890
// ApplySpeechBubblePlacement (the window half), :982 SpeechPopupScale, :1000 SyncSpeechPopupOpen,
// :1130 HookSpeechBubblePlacement. Ledger row tube#T17 (bubble window half).
//
// The bubble leaves the tube's scaled design canvas for its own small borderless window: text at its
// real size, placed by the shared Core rule (AvatarTubeLayout.SpeechBubblePlacement) inside the work
// area of her monitor, ABOVE her head, and never over main's window while attached - in the canvas it
// sat over the chat box and the pipes. Not activating, not in the taskbar, owned by the tube so it
// stays directly above it. The same Border moves across, so every speech path keeps working on it.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.AvatarTubeLayout;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private const double BubbleWindowPad = 16;   // WPF: the popup is the bubble plus a 16 DIP margin
        private Window? _bubbleWindow;
        private bool _placingBubbleWindow;

        internal bool HasBubbleWindow => _bubbleWindow != null;

        /// <summary>From OnOpened, live app only (a headless test keeps the in-canvas bubble).</summary>
        private void CreateBubbleWindow()
        {
            if (_bubbleWindow != null || SpeechInstant) return;
            try
            {
                if (_speechBubble.Parent is Panel host) host.Children.Remove(_speechBubble);
                else return;
                _speechBubble.PointerPressed -= OnDragPointerPressed;   // the bubble no longer drags the tube
                _speechBubble.Margin = new Thickness(BubbleWindowPad);
                _speechBubble.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
                _speechBubble.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
                _speechBubble.MaxWidth = SpeechBubbleMaxWidth;

                _bubbleWindow = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    Background = Brushes.Transparent,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    CanResize = false,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Focusable = false,
                    Topmost = Topmost,
                    Title = "",
                    FontFamily = FontFamily,
                    Content = _speechBubble,
                };
                _speechBubble.PropertyChanged += (_, e) =>
                {
                    if (e.Property == IsVisibleProperty) SyncBubbleWindow();
                    else if (e.Property == BoundsProperty && _speechBubble.IsVisible) PlaceBubbleWindow();
                };
                PropertyChanged += (_, e) =>
                {
                    if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) SyncBubbleWindow();
                    else if (e.Property == TopmostProperty && _bubbleWindow != null) _bubbleWindow.Topmost = Topmost;
                };
                PositionChanged += (_, _) => { if (_speechBubble.IsVisible) PlaceBubbleWindow(); };
                if (_parentWindow != null)
                {
                    _parentWindow.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) SyncBubbleWindow();
                    };
                    _parentWindow.PositionChanged += (_, _) => { if (_speechBubble.IsVisible) PlaceBubbleWindow(); };
                }
                Closed += (_, _) => { try { _bubbleWindow?.Close(); } catch { } _bubbleWindow = null; };
                SyncBubbleWindow();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Speech bubble window could not be built; the bubble stays in the tube");
                _bubbleWindow = null;
            }
        }

        /// <summary>WPF SyncSpeechPopupOpen: up exactly while the bubble is meant to show and she (and,
        /// attached, main) is on screen. Rule in Core <see cref="SpeechBubbleVisibility"/>.</summary>
        private void SyncBubbleWindow()
        {
            if (_bubbleWindow == null) return;
            bool want = SpeechBubbleVisibility.ShouldShow(_speechBubble.IsVisible, IsVisible,
                WindowState == WindowState.Minimized, _isAttached,
                _parentWindow?.IsVisible ?? true, _parentWindow?.WindowState == WindowState.Minimized);
            if (want == _bubbleWindow.IsVisible) return;
            try
            {
                if (want)
                {
                    PlaceBubbleWindow();   // position the hidden window first: no flash at 0,0
                    _bubbleWindow.Show(this);
                    PlaceBubbleWindow();
                }
                else _bubbleWindow.Hide();
            }
            catch (Exception ex) { Log.Debug(ex, "Speech bubble window show/hide failed"); }
        }

        /// <summary>WPF ApplySpeechBubblePlacement, window half: everything in physical px.</summary>
        private void PlaceBubbleWindow()
        {
            if (_bubbleWindow == null || _placingBubbleWindow || _avatarBorder.Bounds.Width <= 0) return;
            _placingBubbleWindow = true;
            try
            {
                var p0 = _avatarBorder.PointToScreen(new Point(0, 0));
                var p1 = _avatarBorder.PointToScreen(new Point(_avatarBorder.Bounds.Width, _avatarBorder.Bounds.Height));
                var avatar = Box.FromEdges(Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y), Math.Max(p0.X, p1.X), Math.Max(p0.Y, p1.Y));
                var screen = Screens.ScreenFromPoint(new PixelPoint((int)(avatar.X + avatar.W / 2), (int)(avatar.Y + avatar.H / 2)))
                             ?? Screens.ScreenFromWindow(this);
                if (screen == null) return;
                var wa = screen.WorkingArea;
                var work = Box.FromEdges(wa.X, wa.Y, wa.Right, wa.Bottom);
                double s = screen.Scaling > 0 ? screen.Scaling : 1.0;

                Box? obstacle = null;
                if (_isAttached && _parentWindow is { IsVisible: true } main)
                {
                    var ps = main.RenderScaling;
                    obstacle = new Box(main.Position.X, main.Position.Y, main.Bounds.Width * ps, main.Bounds.Height * ps);
                }

                var b = _speechBubble;
                double chromeW = b.Padding.Left + b.Padding.Right + b.BorderThickness.Left + b.BorderThickness.Right;
                double configured = double.IsFinite(b.MaxWidth) && b.MaxWidth > 0 ? b.MaxWidth : SpeechBubbleMaxWidth;
                var plan = SpeechBubblePlacement.Place(work, work, obstacle, avatar, configured * s, chromeW * s, maxPx =>
                {
                    double outer = maxPx / s;
                    b.Width = double.NaN;   // measure free; the plan's width is set below
                    b.Measure(new Size(outer, double.PositiveInfinity));
                    return (Math.Min(outer, b.DesiredSize.Width - b.Margin.Left - b.Margin.Right) * s,
                            (b.DesiredSize.Height - b.Margin.Top - b.Margin.Bottom) * s);
                }, edgeGap: Math.Max(SpeechBubblePlacement.Gap, BubbleWindowPad * s + 2));

                b.Width = plan.Bubble.W / s;   // the plan's width, so the window wraps text as measured
                _bubbleWindow.Position = new PixelPoint((int)Math.Round(plan.Bubble.X - BubbleWindowPad * s),
                                                        (int)Math.Round(plan.Bubble.Y - BubbleWindowPad * s));
            }
            catch (Exception ex) { Log.Debug("Speech bubble placement failed: {Error}", ex.Message); }
            finally { _placingBubbleWindow = false; }
        }
    }
}
