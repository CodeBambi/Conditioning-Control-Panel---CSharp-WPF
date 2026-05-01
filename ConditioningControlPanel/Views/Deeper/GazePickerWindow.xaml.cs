using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Deeper
{
    /// <summary>
    /// Transparent borderless window positioned over the editor's video preview
    /// area for picking a gaze-target rect. Uses a separate window because
    /// LibVLC's VideoView renders in a native child HWND that wins WPF airspace
    /// (an inline overlay would render behind the video and be invisible).
    ///
    /// Owner is the Deeper editor; the window is sized + positioned to match
    /// the screen rect of the editor's PreviewHost. Coordinates fed back to
    /// the caller are normalized [x, y, w, h] in [0, 1] relative to that host.
    /// </summary>
    public partial class GazePickerWindow : Window
    {
        private double[] _rect = new[] { 0.25, 0.25, 0.5, 0.5 };
        private DragMode _drag = DragMode.None;
        private Point _dragStart;          // canvas coords at mouse-down
        private double[] _dragStartRect = new double[4];

        /// <summary>
        /// Set true if the user clicked Done (rect should be applied).
        /// </summary>
        public bool Committed { get; private set; }

        /// <summary>
        /// Final normalized rect after user committed; meaningful only if
        /// <see cref="Committed"/> is true.
        /// </summary>
        public double[] ResultRect => (double[])_rect.Clone();

        public GazePickerWindow(double[] initial)
        {
            InitializeComponent();
            if (initial != null && initial.Length >= 4)
                _rect = (double[])initial.Clone();
            ClampRect();
            TxtHint.Text = Loc.Get("deeper_editor_gaze_pick_hint");
            Loaded += (_, _) => RenderRect();
        }

        private enum DragMode { None, NewRect, Move, ResizeNW, ResizeN, ResizeNE, ResizeE, ResizeSE, ResizeS, ResizeSW, ResizeW }

        // -- Mouse on the picker canvas (background) --------------------------

        private void PickCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(PickCanvas);
            // If the click is inside the existing rect, it's a move. Otherwise
            // start a fresh rectangle (overwriting the existing one).
            if (_rect[2] > 0 && _rect[3] > 0 && IsInsideRect(p))
            {
                BeginDrag(DragMode.Move, p);
            }
            else
            {
                BeginDrag(DragMode.NewRect, p);
                _rect[0] = p.X / Math.Max(1, PickCanvas.ActualWidth);
                _rect[1] = p.Y / Math.Max(1, PickCanvas.ActualHeight);
                _rect[2] = 0.001;
                _rect[3] = 0.001;
                RenderRect();
            }
            PickCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void PickCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drag == DragMode.None || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(PickCanvas);
            ApplyDrag(p);
        }

        private void PickCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag == DragMode.None) return;
            PickCanvas.ReleaseMouseCapture();
            _drag = DragMode.None;
            // Drop sub-pixel rects from accidental clicks so the rect picker
            // doesn't end up with a 0.001 x 0.001 ghost.
            if (_rect[2] < 0.01 || _rect[3] < 0.01)
            {
                _rect[2] = Math.Max(_rect[2], 0.05);
                _rect[3] = Math.Max(_rect[3], 0.05);
                ClampRect();
                RenderRect();
            }
        }

        // -- Mouse on a resize handle -----------------------------------------

        private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Rectangle r || r.Tag is not string tag) return;
            var mode = tag switch
            {
                "NW" => DragMode.ResizeNW,
                "N"  => DragMode.ResizeN,
                "NE" => DragMode.ResizeNE,
                "E"  => DragMode.ResizeE,
                "SE" => DragMode.ResizeSE,
                "S"  => DragMode.ResizeS,
                "SW" => DragMode.ResizeSW,
                "W"  => DragMode.ResizeW,
                _ => DragMode.None
            };
            if (mode == DragMode.None) return;
            BeginDrag(mode, e.GetPosition(PickCanvas));
            PickCanvas.CaptureMouse();
            e.Handled = true;
        }

        // -- Drag mechanics ----------------------------------------------------

        private void BeginDrag(DragMode mode, Point p)
        {
            _drag = mode;
            _dragStart = p;
            Array.Copy(_rect, _dragStartRect, 4);
        }

        private void ApplyDrag(Point p)
        {
            var w = Math.Max(1, PickCanvas.ActualWidth);
            var h = Math.Max(1, PickCanvas.ActualHeight);
            var dx = (p.X - _dragStart.X) / w;
            var dy = (p.Y - _dragStart.Y) / h;
            var px = p.X / w;
            var py = p.Y / h;
            var sx = _dragStartRect[0];
            var sy = _dragStartRect[1];
            var sw = _dragStartRect[2];
            var sh = _dragStartRect[3];

            switch (_drag)
            {
                case DragMode.NewRect:
                    _rect[0] = Math.Min(sx, px);
                    _rect[1] = Math.Min(sy, py);
                    _rect[2] = Math.Abs(px - sx);
                    _rect[3] = Math.Abs(py - sy);
                    break;
                case DragMode.Move:
                    _rect[0] = Math.Clamp(sx + dx, 0, 1 - sw);
                    _rect[1] = Math.Clamp(sy + dy, 0, 1 - sh);
                    break;
                case DragMode.ResizeNW:
                    ResizeFromAnchor(sx + sw, sy + sh, px, py);
                    break;
                case DragMode.ResizeN:
                    _rect[1] = Math.Min(sy + sh - 0.005, py);
                    _rect[3] = (sy + sh) - _rect[1];
                    break;
                case DragMode.ResizeNE:
                    ResizeFromAnchor(sx, sy + sh, px, py);
                    break;
                case DragMode.ResizeE:
                    _rect[2] = Math.Max(0.005, px - sx);
                    break;
                case DragMode.ResizeSE:
                    ResizeFromAnchor(sx, sy, px, py);
                    break;
                case DragMode.ResizeS:
                    _rect[3] = Math.Max(0.005, py - sy);
                    break;
                case DragMode.ResizeSW:
                    ResizeFromAnchor(sx + sw, sy, px, py);
                    break;
                case DragMode.ResizeW:
                    _rect[0] = Math.Min(sx + sw - 0.005, px);
                    _rect[2] = (sx + sw) - _rect[0];
                    break;
            }
            ClampRect();
            RenderRect();
        }

        private void ResizeFromAnchor(double anchorX, double anchorY, double px, double py)
        {
            _rect[0] = Math.Min(anchorX, px);
            _rect[1] = Math.Min(anchorY, py);
            _rect[2] = Math.Abs(anchorX - px);
            _rect[3] = Math.Abs(anchorY - py);
        }

        private void ClampRect()
        {
            _rect[0] = Math.Clamp(_rect[0], 0, 1);
            _rect[1] = Math.Clamp(_rect[1], 0, 1);
            _rect[2] = Math.Max(0.005, Math.Min(_rect[2], 1 - _rect[0]));
            _rect[3] = Math.Max(0.005, Math.Min(_rect[3], 1 - _rect[1]));
        }

        private bool IsInsideRect(Point p)
        {
            var w = PickCanvas.ActualWidth;
            var h = PickCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return false;
            var rx = _rect[0] * w;
            var ry = _rect[1] * h;
            var rw = _rect[2] * w;
            var rh = _rect[3] * h;
            // Inset slightly so click-on-handle is preferred over click-inside-edge.
            const double inset = 8;
            return p.X > rx + inset && p.X < rx + rw - inset
                && p.Y > ry + inset && p.Y < ry + rh - inset;
        }

        // -- Render ------------------------------------------------------------

        private void PickCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderRect();

        private void RenderRect()
        {
            if (PickCanvas == null) return;
            var w = PickCanvas.ActualWidth;
            var h = PickCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var rx = _rect[0] * w;
            var ry = _rect[1] * h;
            var rw = Math.Max(2, _rect[2] * w);
            var rh = Math.Max(2, _rect[3] * h);

            RectShape.Visibility = Visibility.Visible;
            RectShape.Width = rw;
            RectShape.Height = rh;
            Canvas.SetLeft(RectShape, rx);
            Canvas.SetTop(RectShape, ry);

            PositionHandle(HandleNW, rx, ry);
            PositionHandle(HandleN,  rx + rw / 2, ry);
            PositionHandle(HandleNE, rx + rw, ry);
            PositionHandle(HandleE,  rx + rw, ry + rh / 2);
            PositionHandle(HandleSE, rx + rw, ry + rh);
            PositionHandle(HandleS,  rx + rw / 2, ry + rh);
            PositionHandle(HandleSW, rx, ry + rh);
            PositionHandle(HandleW,  rx, ry + rh / 2);

            TxtCoords.Text = string.Format(CultureInfo.InvariantCulture,
                "x={0:0.000}  y={1:0.000}  w={2:0.000}  h={3:0.000}",
                _rect[0], _rect[1], _rect[2], _rect[3]);
        }

        private static void PositionHandle(Rectangle h, double cx, double cy)
        {
            h.Visibility = Visibility.Visible;
            Canvas.SetLeft(h, cx - h.Width / 2);
            Canvas.SetTop(h, cy - h.Height / 2);
        }

        // -- Commit / cancel ---------------------------------------------------

        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            Committed = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Committed = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Committed = false; Close(); }
            else if (e.Key == Key.Enter) { Committed = true; Close(); }
        }
    }
}
