// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Windowing.cs: attach/detach,
// the attached dock beside the shell, the detached drag and the saved placement.
//
// Coordinates: Avalonia's Window.Position is PHYSICAL px (WPF Left/Top were DIPs). Docking works in
// px throughout, as WPF's physical route did (GetWindowRect/SetWindowPos). The saved
// AvatarTubeLeft/Top stay in WPF's DIPs, so a WPF-written file reads unchanged: px = dip * DesktopScaling.
//
// ponytail: skipped from WPF - make-room (moving main so she fits), the mirrored art on a right
// dock, measured mod-art insets (stock 239/353 are used), Ctrl+scroll zoom (so AvatarTubeScale is
// neither applied nor written) and the floating bob. Add each when a user misses it.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.AvatarTubeLayout;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private const double DesignHeight = 1080;
        private const double TubeArtLeftPadding = 239;   // WPF Windowing.cs:88
        private const double VerticalOffset = 20;        // WPF Windowing.cs:91
        private const double DockDaylight = 3;           // WPF Windowing.cs:1079

        private double _scaleFactor = 1.0;
        private bool _restoringPlacement;
        private bool _restorePending = true;

        public bool IsDetached => !_isAttached;

        /// <summary>The hero card's Detach chip (WPF MainWindow.Patreon.cs:1276). One sink, owned
        /// by the open tube, same shape as <see cref="OpenChatSink"/>.</summary>
        internal static volatile Action? ToggleDetachedSink;

        private Control[] DragSurfaces() => new Control[]
        {
            _avatarBorder, _speechBubble, this.FindControl<Border>("TitleBox")!, _imgTubeFrame,
        };

        private void InitWindowing()
        {
            foreach (var name in new[] { "MenuItemDetach", "MenuItemAttach" })
            {
                var item = this.FindControl<MenuItem>(name);
                if (item != null) item.Click += (_, _) => ToggleDetached();
                else Log.Warning("AvatarTubeWindow: {Name} not found", name);
            }
            foreach (var c in DragSurfaces()) c.PointerPressed += OnDragPointerPressed;
            PositionChanged += OnTubePositionChanged;
            // A window mapped on a scaled screen first reports DesktopScaling 1 (seen live on KWin);
            // the placement is redone once the real scaling arrives.
            ScalingChanged += (_, _) => { FitToScreen(); UpdatePosition(); RestoreSavedPlacement(); };
            if (_parentWindow != null)
            {
                _parentWindow.PositionChanged += OnParentPositionChanged;
                _parentWindow.PropertyChanged += OnParentPropertyChanged;
            }
            ApplyModeChrome();
        }

        private void ReleaseWindowing()
        {
            if (ToggleDetachedSink == (Action)ToggleDetached) ToggleDetachedSink = null;
            if (_parentWindow == null) return;
            _parentWindow.PositionChanged -= OnParentPositionChanged;
            _parentWindow.PropertyChanged -= OnParentPropertyChanged;
        }

        private void OnParentPositionChanged(object? s, PixelPointEventArgs e) => UpdatePosition();

        private void OnParentPropertyChanged(object? s, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ClientSizeProperty || e.Property == WindowStateProperty) UpdatePosition();
        }

        public void ToggleDetached() { if (_isAttached) Detach(); else Attach(); }

        public void Detach() => RunOnAvatar(() => SetAttached(false));

        public void Attach() => RunOnAvatar(() => SetAttached(true));

        /// <summary>WPF Detach/Attach (Windowing.cs:2182-2319): detach frees her where she is and makes
        /// her topmost; attach drops topmost and snaps back beside main. Both persist the mode.</summary>
        private void SetAttached(bool attached)
        {
            if (_isAttached == attached) return;
            _isAttached = attached;
            _restorePending = false;   // the user's own mode change wins over the saved one
            RefreshTubeGlass();
            RefreshTubeLayout();
            ApplyModeChrome();
            UpdatePosition();
            if (attached && _parentWindow != null) Platform.X11Overlay.RestackAbove(this, _parentWindow);
            Log.Information("Avatar tube {Mode}", attached ? "attached" : "detached");
            if (_restoringPlacement) return;
            CoreSettings.Current.AvatarTubeDetached = !attached;
            CoreSettings.Save();
        }

        /// <summary>Topmost, drag cursors, frame hit-testing and the menu, per WPF's mode switch and
        /// UpdateContextMenuForState. Shrink/Grow/Dismiss stay hidden: zoom and dismiss did not port.</summary>
        private void ApplyModeChrome()
        {
            Topmost = !_isAttached;
            var cursor = new Cursor(_isAttached ? StandardCursorType.Arrow : StandardCursorType.SizeAll);
            foreach (var c in DragSurfaces()) c.Cursor = cursor;
            _imgTubeFrame.IsHitTestVisible = !_isAttached;
            if (this.FindControl<MenuItem>("MenuItemDetach") is { } d) d.IsVisible = _isAttached;
            if (this.FindControl<MenuItem>("MenuItemAttach") is { } a) a.IsVisible = !_isAttached;
        }

        /// <summary>WPF starts a manual drag only on her visible parts; BeginMoveDrag hands it to the WM.</summary>
        private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_isAttached || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
        }

        /// <summary>WPF saved on mouse-up; the WM owns this drag, so save on every move and let
        /// CoreSettings' debounce land the last one.</summary>
        private void OnTubePositionChanged(object? sender, PixelPointEventArgs e)
        {
            if (_isAttached || _restoringPlacement || _restorePending || !IsVisible) return;
            var s = CoreSettings.Current;
            s.AvatarTubeLeft = e.Point.X / DesktopScaling;
            s.AvatarTubeTop = e.Point.Y / DesktopScaling;
            CoreSettings.Save();
        }

        /// <summary>WPF CalculateScaleFactor: size the canvas to the work area she opens on.</summary>
        private void FitToScreen()
        {
            var screen = Screens.ScreenFromWindow(_parentWindow ?? this) ?? Screens.Primary;
            if (screen == null) return;
            var wa = screen.WorkingArea;
            _scaleFactor = TubeWindowMath.FitScale(wa.Width / screen.Scaling, wa.Height / screen.Scaling);
            Width = DesignWidth * _scaleFactor;
            Height = DesignHeight * _scaleFactor;
        }

        private PixelSize TubePixelSize => PixelSize.FromSize(new Size(Width, Height), DesktopScaling);

        private static Box ToBox(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);

        /// <summary>WPF UpdatePosition's physical route: dock beside main per TubeDockPlacement.</summary>
        internal void UpdatePosition()
        {
            if (!_isAttached || _parentWindow is not { } p || p.WindowState == WindowState.Minimized) return;
            var pr = new PixelRect(p.Position, PixelSize.FromSize(p.ClientSize, p.DesktopScaling));
            if (pr.Width <= 0 || pr.Height <= 0) return;
            var work = p.Screens.ScreenFromWindow(p)?.WorkingArea ?? new PixelRect(-100000, -100000, 200000, 200000);
            double art = _scaleFactor * DesktopScaling, day = DockDaylight * DesktopScaling;
            var size = TubePixelSize;
            var plan = TubeDockPlacement.Place(ToBox(pr), size.Width, size.Height,
                (int)Math.Round(Math.Max(0, TubeArtLeftPadding * art - day)),
                (int)Math.Round(Math.Max(0, (TubeArtRightPadding - SeamOverlapOverMain) * art - day)),
                (int)Math.Round(VerticalOffset * art), ToBox(work));
            Position = new PixelPoint(plan.Left, plan.Top);
        }

        /// <summary>WPF RestoreSavedPlacement: a detached tube comes back where it was left, with at
        /// least half of it on a connected screen.</summary>
        private void RestoreSavedPlacement()
        {
            var s = CoreSettings.Current;
            var here = Screens.ScreenFromWindow(this);
            if (!_restorePending || (here != null && Math.Abs(here.Scaling - DesktopScaling) > 0.01)) return;
            _restorePending = false;   // once per open: a later monitor change must not yank her back
            if (_isAttached || double.IsNaN(s.AvatarTubeLeft) || double.IsNaN(s.AvatarTubeTop)) return;
            _restoringPlacement = true;
            try
            {
                double k = DesktopScaling, x = s.AvatarTubeLeft * k, y = s.AvatarTubeTop * k;
                var size = TubePixelSize;
                var screen = Screens.ScreenFromPoint(new PixelPoint((int)(x + size.Width / 2.0), (int)(y + size.Height / 2.0)))
                             ?? Screens.Primary;
                if (screen == null) return;
                var (cx, cy) = TubeWindowMath.ClampDetached(x, y, size.Width, size.Height, ToBox(screen.WorkingArea));
                Position = new PixelPoint(cx, cy);
            }
            finally { _restoringPlacement = false; }
        }
    }
}
