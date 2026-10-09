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
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.AvatarTubeLayout;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private const double DesignHeight = 1020;   // WPF Windowing.cs:42 (the 780x1080 canvas is fitted into it)
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
            if (this.FindControl<MenuItem>("MenuItemDetach") is { } detach) detach.Click += (_, _) => Detach();
            else Log.Warning("AvatarTubeWindow: MenuItemDetach not found");
            if (this.FindControl<MenuItem>("MenuItemAttach") is { } attach) attach.Click += (_, _) => AttachFromMenu();
            else Log.Warning("AvatarTubeWindow: MenuItemAttach not found");
            foreach (var c in DragSurfaces()) c.PointerPressed += OnDragPointerPressed;
            // The WM owns the drag, so the release may never reach us: the first pointer event
            // after it (release, or the next enter/move) saves where she ended up.
            PointerReleased += (_, _) => PersistTubePlacement();
            PointerEntered += (_, _) => PersistTubePlacement();
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

        /// <summary>WPF MenuItemAttach_Click (ChatInput.cs:999-1016): bring main back first.</summary>
        internal void AttachFromMenu()
        {
            if (_parentWindow is { } p)
            {
                p.Show();
                p.WindowState = WindowState.Normal;
                p.Activate();
            }
            Attach();
        }

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
            if (!attached) Platform.X11Overlay.SetInputRect(this, null);
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
            _restorePending = false;   // she is being moved by hand now; the saved spot is stale
            BeginMoveDrag(e);
        }

        /// <summary>WPF PersistTubePlacement (on mouse-up). DIPs of the screen she is on, so a
        /// WPF-written file and a mixed-DPI desk both read back right.</summary>
        internal void PersistTubePlacement()
        {
            if (_isAttached || _restoringPlacement || _restorePending || !IsVisible) return;
            var k = Screens.ScreenFromWindow(this)?.Scaling ?? DesktopScaling;
            var s = CoreSettings.Current;
            double left = Position.X / k, top = Position.Y / k;
            if (Math.Abs(left - s.AvatarTubeLeft) < 0.5 && Math.Abs(top - s.AvatarTubeTop) < 0.5) return;
            s.AvatarTubeLeft = left;
            s.AvatarTubeTop = top;
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
            int rightInset = (int)Math.Round(Math.Max(0, (TubeArtRightPadding - SeamOverlapOverMain) * art - day));
            int leftInset = (int)Math.Round(Math.Max(0, TubeArtLeftPadding * art - day));
            var plan = TubeDockPlacement.Place(ToBox(pr), size.Width, size.Height, leftInset, rightInset,
                (int)Math.Round(VerticalOffset * art), ToBox(work));
            Position = new PixelPoint(plan.Left, plan.Top);
            // WPF's transparent margin was click-through (layered window); an X11 window takes
            // clicks on every pixel, so cut her input down to everything left of the seam or the
            // shell's rail under that margin goes dead. The cut follows the side she docked to: on a
            // right dock her LEFT margin is the one over the shell. Detach gives the whole window back.
            InputRect = plan.Side switch
            {
                DockSide.Left => new PixelRect(0, 0, size.Width - rightInset, size.Height),
                DockSide.Right => new PixelRect(leftInset, 0, size.Width - leftInset, size.Height),
                _ => new PixelRect(leftInset, 0, Math.Max(0, size.Width - leftInset - rightInset), size.Height),   // floating over the shell: both margins
            };
            Platform.X11Overlay.SetInputRect(this, InputRect);
        }

        /// <summary>The attached tube's input region (window px) as last set by <see cref="UpdatePosition"/>.</summary>
        internal PixelRect? InputRect { get; private set; }

        /// <summary>WPF RestoreSavedPlacement: a detached tube comes back where it was left, with at
        /// least half of it on a connected screen.</summary>
        private void RestoreSavedPlacement(bool force = false)
        {
            var s = CoreSettings.Current;
            var here = Screens.ScreenFromWindow(this);
            if (!_restorePending || (!force && here != null && Math.Abs(here.Scaling - DesktopScaling) > 0.01)) return;
            _restorePending = false;   // once per open: a later monitor change must not yank her back
            if (_isAttached || double.IsNaN(s.AvatarTubeLeft) || double.IsNaN(s.AvatarTubeTop)) return;
            _restoringPlacement = true;
            try
            {
                var screens = Screens.All.Select(sc => (ToBox(sc.Bounds), sc.Scaling, ToBox(sc.WorkingArea))).ToList();
                var size = TubePixelSize;
                var (cx, cy) = TubeWindowMath.RestoreDetached(s.AvatarTubeLeft, s.AvatarTubeTop,
                    size.Width, size.Height, screens, DesktopScaling);
                Position = new PixelPoint(cx, cy);
            }
            finally { _restoringPlacement = false; }
        }
    }
}
