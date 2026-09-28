// PORTED from ConditioningControlPanel/MainWindow/MainWindow.WindowChrome.cs (478 lines).
//
// The four title-bar handlers are the ONLY members of this partial that touch nothing but the
// window, so they are ported for real; everything else in the WPF file reaches App.Lockdown, the
// avatar tube window or Win32 and is listed below rather than faked. The two bark pings the
// minimize and close buttons fire DO cross now, through CoreBark.
//
// Win32 -> Avalonia, per the port's mapping table:
//   DragMove()                         -> BeginMoveDrag(PointerPressedEventArgs)
//   WindowState = Minimized/Maximized  -> unchanged; Avalonia has the same enum
//   e.ClickCount == 2                  -> e.ClickCount == 2 on PointerPressedEventArgs
//   PointToScreen + Left/Top fixup     -> dropped. BeginMoveDrag restores from maximized itself
//                                         on every backend, so the manual re-anchor is not needed.
//   OnDpiChanged / OnStateChanged      -> no such overrides on Avalonia's Window. Scaling changes
//                                         arrive as ScalingChanged; WindowState is an
//                                         AvaloniaProperty you observe. See the WorkAreaFit
//                                         partial, which is where the DPI re-fit actually lives.
//
// Members dropped (App.*/avatar-tube/Win32 only):
//   private void EnsureSessionRestoredForExit(…)
//   protected override void OnClosing(…)          // tray half ported in MainShellWindow.Tray.cs;
//                                                  // session save, Bark, Lockdown still missing
//   protected override void OnDpiChanged(…)       // queues the work-area re-fit; see WorkAreaFit
//   protected override void OnStateChanged(…)     // avatar re-attach, Bark, taskbar thumbnail
//   private void HideAvatarTube(…)                // called by BtnMinimize_Click below

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF: WindowChrome ResizeBorderThickness="5" + ResizeMode=CanResizeWithGrip gave the
        // undecorated window OS resize edges. SystemDecorations="None" on X11 gives none, so the
        // edges are hit-tested here and handed to the window manager via BeginResizeDrag.
        internal const double ResizeBorder = 5;
        private const double ResizeCorner = 12;   // the lost grip, folded into a wider corner

        /// <summary>The edge under <paramref name="p"/> in a window of size <paramref name="s"/>, or null.</summary>
        internal static WindowEdge? EdgeAt(Point p, Size s)
        {
            bool l = p.X < ResizeBorder, r = p.X >= s.Width - ResizeBorder;
            bool t = p.Y < ResizeBorder, b = p.Y >= s.Height - ResizeBorder;
            bool lc = p.X < ResizeCorner, rc = p.X >= s.Width - ResizeCorner;
            bool tc = p.Y < ResizeCorner, bc = p.Y >= s.Height - ResizeCorner;
            if ((t && lc) || (l && tc)) return WindowEdge.NorthWest;
            if ((t && rc) || (r && tc)) return WindowEdge.NorthEast;
            if ((b && lc) || (l && bc)) return WindowEdge.SouthWest;
            if ((b && rc) || (r && bc)) return WindowEdge.SouthEast;
            if (l) return WindowEdge.West;
            if (r) return WindowEdge.East;
            if (t) return WindowEdge.North;
            if (b) return WindowEdge.South;
            return null;
        }

        internal static StandardCursorType CursorFor(WindowEdge e) => e switch
        {
            WindowEdge.West => StandardCursorType.LeftSide,
            WindowEdge.East => StandardCursorType.RightSide,
            WindowEdge.North => StandardCursorType.TopSide,
            WindowEdge.South => StandardCursorType.BottomSide,
            WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
            WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
            WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
            _ => StandardCursorType.BottomRightCorner,
        };

        /// <summary>The last edge handed to BeginResizeDrag. Test seam.</summary>
        internal WindowEdge? LastResizeEdge { get; private set; }

        private WindowEdge? _cursorEdge;

        private WindowEdge? ResizeEdgeFor(PointerEventArgs e)
            => WindowState == WindowState.Normal && CanResize ? EdgeAt(e.GetPosition(this), Bounds.Size) : null;

        private void HookResizeEdges()
        {
            // Tunnel, so the edge wins over whatever card sits under the 5px band.
            AddHandler(PointerMovedEvent, (_, e) =>
            {
                var edge = ResizeEdgeFor(e);
                if (edge == _cursorEdge) return;
                _cursorEdge = edge;
                Cursor = edge is { } x ? new Cursor(CursorFor(x)) : null;
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (ResizeEdgeFor(e) is not { } edge || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                LastResizeEdge = edge;
                e.Handled = true;
                BeginResizeDrag(edge, e);
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private void TitleBar_MouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                BtnMaximize_Click(sender, new RoutedEventArgs());
                return;
            }

            // BeginMoveDrag hands the drag to the window manager, which is what makes an
            // undecorated window draggable on X11 the way DragMove() did on Win32. It also
            // un-maximizes on the way, so the WPF PointToScreen re-anchor above it is gone.
            BeginMoveDrag(e);
        }

        private void BtnMinimize_Click(object? sender, RoutedEventArgs e)
        {
            CoreBark.NotifyUiAction("minimize");
            // ponytail: still needs App.Lockdown (NotifyEscapeAttempt - minimizing during a
            // lockdown stays ALLOWED, it just gets noticed) and HideAvatarTube.
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs the avatar tube window (detach before maximizing, re-attach after);
            // wired when AvatarTubeWindow's host service moves to Core.
            // The glyph is set through Named<T>, not through the generated BtnMaximize field.
            // MainShellWindow loads with AvaloniaXamlLoader.Load, which never assigns those fields,
            // so `BtnMaximize.Content = …` threw a NullReferenceException on the first click of the
            // maximize button - a crash, and one neither --render-view nor --nav-check can see,
            // because neither clicks the chrome. See the header of MainShellWindow.TabNavigation.cs.
            var maximize = Named<Button>("BtnMaximize");
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                if (maximize is not null) maximize.Content = "☐";
            }
            else
            {
                WindowState = WindowState.Maximized;
                if (maximize is not null) maximize.Content = "❐";
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            CoreBark.NotifyUiAction("close");
            // Close() goes to the tray via OnClosing (MainShellWindow.Tray.cs) when a tray host exists.
            // ponytail: the WPF handler also runs EnsureSessionRestoredForExit (session service).
            Close();
        }
    }
}
