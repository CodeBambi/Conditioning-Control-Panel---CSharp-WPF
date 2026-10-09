// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Windowing.cs: attach/detach,
// the attached dock beside the shell, the detached drag and the saved placement.
//
// Coordinates: Avalonia's Window.Position is PHYSICAL px (WPF Left/Top were DIPs). Docking works in
// px throughout, as WPF's physical route did (GetWindowRect/SetWindowPos). The saved
// AvatarTubeLeft/Top stay in WPF's DIPs, so a WPF-written file reads unchanged: px = dip * DesktopScaling.
//
// The mirrored art on a right dock and the Win32 owner pairing live in AvatarTubeWindow.Flip.cs.
//
// ponytail: skipped from WPF - make-room (moving main so she fits), measured mod-art insets (stock
// 239/353 are used), Ctrl+scroll zoom and the floating bob. Add each when a user misses it.
// The detached menu's Shrink / Grow / Dismiss are ported at the foot of this file.

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
        // WPF Windowing.cs:106-108: the user's own size for the free tube, 50%..150% in 25% steps.
        internal const double MinScale = 0.5, MaxScale = 1.5, ScaleStep = 0.25;
        private double _currentScale = 1.0;
        internal double CurrentScale => _currentScale;
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
            if (this.FindControl<MenuItem>("MenuItemShrink") is { } shrink) shrink.Click += (_, _) => StepScale(-ScaleStep);
            if (this.FindControl<MenuItem>("MenuItemGrow") is { } grow) grow.Click += (_, _) => StepScale(+ScaleStep);
            if (this.FindControl<MenuItem>("MenuItemDismiss") is { } dismiss) dismiss.Click += (_, _) => DismissFromMenu();
            // WPF Windowing.cs:2716: the saved scale comes back with her (#669).
            var saved = CoreSettings.Current.AvatarTubeScale;
            if (!double.IsNaN(saved) && saved > 0) _currentScale = Math.Clamp(saved, MinScale, MaxScale);
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
            ApplyTubeSize();   // the user's scale applies to the free tube only; docked she is stock size
            if (!attached) ApplyTubeArtFlip(false);   // WPF Detach: the free tube reads unmirrored
            UpdatePosition();
            ApplyNativeOwner(attached);
            if (attached && _parentWindow != null) Platform.X11Overlay.RestackAbove(this, _parentWindow);
            if (!attached) Platform.X11Overlay.SetInputRect(this, null);
            Log.Information("Avatar tube {Mode}", attached ? "attached" : "detached");
            if (_restoringPlacement) return;
            CoreSettings.Current.AvatarTubeDetached = !attached;
            CoreSettings.Save();
        }

        /// <summary>Topmost, drag cursors, frame hit-testing and the menu, per WPF's mode switch and
        /// UpdateContextMenuForState: Shrink, Grow and Dismiss belong to the free tube only.</summary>
        private void ApplyModeChrome()
        {
            Topmost = !_isAttached;
            var cursor = new Cursor(_isAttached ? StandardCursorType.Arrow : StandardCursorType.SizeAll);
            foreach (var c in DragSurfaces()) c.Cursor = cursor;
            _imgTubeFrame.IsHitTestVisible = !_isAttached;
            if (this.FindControl<MenuItem>("MenuItemDetach") is { } d) d.IsVisible = _isAttached;
            if (this.FindControl<MenuItem>("MenuItemAttach") is { } a) a.IsVisible = !_isAttached;
            if (this.FindControl<MenuItem>("MenuItemShrink") is { } sh) sh.IsVisible = !_isAttached;
            if (this.FindControl<MenuItem>("MenuItemGrow") is { } gr) gr.IsVisible = !_isAttached;
            if (this.FindControl<MenuItem>("MenuItemDismiss") is { } di) di.IsVisible = !_isAttached;
            UpdateResizeMenuState();
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
            ApplyTubeSize(force: true);
        }

        private double _appliedUserScale = 1.0;

        /// <summary>The window is the art: stock fit when docked, times the user's scale when free.
        /// A mode change with nothing to change leaves the fitted size alone.</summary>
        private void ApplyTubeSize(bool force = false)
        {
            var user = _isAttached ? 1.0 : _currentScale;
            if (!force && Math.Abs(user - _appliedUserScale) < 0.001) return;
            _appliedUserScale = user;
            Width = DesignWidth * _scaleFactor * user;
            Height = DesignHeight * _scaleFactor * user;
        }

        /// <summary>WPF UpdateResizeMenuState (Windowing.cs:2513): the end stops grey out and say so.</summary>
        private void UpdateResizeMenuState()
        {
            if (this.FindControl<MenuItem>("MenuItemShrink") is { } shrink)
            {
                shrink.IsEnabled = _currentScale > MinScale;
                shrink.Header = global::ConditioningControlPanel.Localization.Loc.Get(shrink.IsEnabled ? "menu_shrink" : "menu_shrink_min");
                shrink.Foreground = shrink.IsEnabled ? global::Avalonia.Media.Brushes.White : global::Avalonia.Media.Brushes.Gray;
            }
            if (this.FindControl<MenuItem>("MenuItemGrow") is { } grow)
            {
                grow.IsEnabled = _currentScale < MaxScale;
                grow.Header = global::ConditioningControlPanel.Localization.Loc.Get(grow.IsEnabled ? "menu_grow" : "menu_grow_max");
                grow.Foreground = grow.IsEnabled ? global::Avalonia.Media.Brushes.White : global::Avalonia.Media.Brushes.Gray;
            }
        }

        /// <summary>WPF MenuItemShrink_Click / MenuItemGrow_Click (ChatInput.cs:1021, :1040): one step,
        /// free tube only, saved (#669), and she is nudged back on screen if the new size left it.</summary>
        internal void StepScale(double delta)
        {
            try
            {
                if (_isAttached) return;
                var next = Math.Clamp(_currentScale + delta, MinScale, MaxScale);
                if (Math.Abs(next - _currentScale) < 0.001) return;
                _currentScale = next;
                ApplyTubeSize();
                UpdateResizeMenuState();
                CoreSettings.Current.AvatarTubeScale = _currentScale;
                CoreSettings.Save();
                KeepOnScreenAfterResize();
            }
            catch (Exception ex) { Log.Warning(ex, "AvatarTubeWindow: resize from the menu failed"); }
        }

        private void KeepOnScreenAfterResize()
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null) return;
            var wa = screen.WorkingArea;
            var size = TubePixelSize;
            int x = Math.Max(wa.X, Math.Min(Position.X, wa.Right - size.Width));
            int y = Math.Max(wa.Y, Math.Min(Position.Y, wa.Bottom - size.Height));
            if (x != Position.X || y != Position.Y) Position = new PixelPoint(x, y);
        }

        /// <summary>WPF MenuItemDismiss_Click (ChatInput.cs:400): dismissing is a decision, so
        /// AvatarEnabled is saved off (#888), she re-attaches and hides, and the Companion room's
        /// switch re-reads. The shell's SetAvatarEnabled does the save, the hide and the sync.</summary>
        internal void DismissFromMenu()
        {
            try
            {
                Log.Information("User dismissed avatar - hiding and reattaching");
                CoreSettings.Current.AvatarEnabled = false;
                CoreSettings.Save();
                if (!_isAttached) SetAttached(true);
                var shell = _parentWindow as Windows.MainShellWindow ?? Windows.MainShellWindow.Current;
                if (shell != null) { shell.HideAvatarTube(); shell.SyncHero(); }
                else Hide();
            }
            catch (Exception ex) { Log.Warning(ex, "AvatarTubeWindow: dismiss failed"); }
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
            // Docked right the art is mirrored about her centre (WPF Windowing.cs:866-871).
            int mirroredLeftInset = (int)Math.Round(Math.Max(0, MirroredLeftInsetUnits() * art - day));
            var plan = TubeDockPlacement.Place(ToBox(pr), size.Width, size.Height,
                leftInset, rightInset, (int)Math.Round(VerticalOffset * art), ToBox(work), mirroredLeftInset);

            // WPF sanity check: reject positions far off the virtual desktop (transitional garbage
            // during minimise churn, e.g. Windows parks a minimised window at -32000,-32000).
            var vs = VirtualDesktop();
            if (plan.Left < vs.X - 2000 || plan.Left > vs.Right + 2000 || plan.Top < vs.Y - 1000 || plan.Top > vs.Bottom + 1000)
            {
                Log.Information("AvatarTube dock rejected off-desktop: tube=({L},{T}) main=({ML},{MT} {MW}x{MH}) desktop=({DL},{DT},{DR},{DB})",
                    plan.Left, plan.Top, pr.X, pr.Y, pr.Width, pr.Height, vs.X, vs.Y, vs.Right, vs.Bottom);
                return;
            }

            ApplyTubeArtFlip(plan.Side == DockSide.Right);
            // Side or size changes only: a drag moves main every frame and must not flood the log.
            string decision = $"{plan.Side}|{size}";
            if (decision != _lastDockDecision)
            {
                _lastDockDecision = decision;
                Log.Information("AvatarTube dock: side={Side} tube=({L},{T} {W}x{H}) main=({ML},{MT} {MW}x{MH}) work=({WL},{WT},{WR},{WB}) insets={IL}/{IR}/{IM}",
                    plan.Side, plan.Left, plan.Top, size.Width, size.Height, pr.X, pr.Y, pr.Width, pr.Height,
                    work.X, work.Y, work.Right, work.Bottom, leftInset, rightInset, mirroredLeftInset);
            }
            Position = new PixelPoint(plan.Left, plan.Top);
            // WPF's transparent margin was click-through (layered window); an X11 window takes
            // clicks on every pixel, so cut her input down to her side of the seam or the shell
            // under that margin goes dead: left of it on a left dock, right of the mirrored art's
            // left edge on a right dock; floating over the shell cuts both margins (audit #1847).
            // Detach gives the whole window back.
            InputRect = plan.Side switch
            {
                DockSide.Left => new PixelRect(0, 0, size.Width - rightInset, size.Height),
                DockSide.Right => new PixelRect(mirroredLeftInset, 0, Math.Max(0, size.Width - mirroredLeftInset), size.Height),
                _ => new PixelRect(leftInset, 0, Math.Max(0, size.Width - leftInset - rightInset), size.Height),
            };
            Platform.X11Overlay.SetInputRect(this, InputRect);
        }

        private string? _lastDockDecision;

        /// <summary>The union of every screen's bounds (px); a huge box when there is no screen answer.</summary>
        private PixelRect VirtualDesktop()
        {
            PixelRect? u = null;
            foreach (var sc in Screens.All) u = u is { } r ? r.Union(sc.Bounds) : sc.Bounds;
            return u ?? new PixelRect(-100000, -100000, 200000, 200000);
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
