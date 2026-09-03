using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// Transparent borderless window positioned over the editor's video preview
    /// area for picking a gaze-target rect. Coordinates fed back to the caller are
    /// normalized [x, y, w, h] in [0, 1] relative to the preview host.
    ///
    /// PORTED from ConditioningControlPanel/Views/Deeper/GazePickerWindow.xaml.cs. Deviations:
    ///  - Mouse* handlers become Pointer* handlers; capture goes through e.Pointer.
    ///  - ActualWidth/Height become Bounds.Width/Height.
    ///  - Handlers are wired in the constructor, per the porting convention.
    /// </summary>
    public partial class GazePickerWindow : Window
    {
        private double[] _rect = new[] { 0.25, 0.25, 0.5, 0.5 };
        private DragMode _drag = DragMode.None;
        private Point _dragStart;          // canvas coords at pointer-down
        private readonly double[] _dragStartRect = new double[4];

        private readonly Canvas _canvas;
        private readonly Rectangle _rectShape;
        private readonly TextBlock _txtCoords;
        private readonly Rectangle _nw, _n, _ne, _e, _se, _s, _sw, _w;

        /// <summary>Set true if the user clicked Done (rect should be applied).</summary>
        public bool Committed { get; private set; }

        /// <summary>Final normalized rect; meaningful only if <see cref="Committed"/> is true.</summary>
        public double[] ResultRect => (double[])_rect.Clone();

        /// <summary>Render constructor: the default quarter-inset rect, so --render-all can draw it.</summary>
        public GazePickerWindow() : this(null) { }

        public GazePickerWindow(double[]? initial)
        {
            AvaloniaXamlLoader.Load(this);
            if (initial != null && initial.Length >= 4)
                _rect = (double[])initial.Clone();
            ClampRect();

            _canvas = this.FindControl<Canvas>("PickCanvas")!;
            _rectShape = this.FindControl<Rectangle>("RectShape")!;
            _txtCoords = this.FindControl<TextBlock>("TxtCoords")!;
            _nw = this.FindControl<Rectangle>("HandleNW")!;
            _n = this.FindControl<Rectangle>("HandleN")!;
            _ne = this.FindControl<Rectangle>("HandleNE")!;
            _e = this.FindControl<Rectangle>("HandleE")!;
            _se = this.FindControl<Rectangle>("HandleSE")!;
            _s = this.FindControl<Rectangle>("HandleS")!;
            _sw = this.FindControl<Rectangle>("HandleSW")!;
            _w = this.FindControl<Rectangle>("HandleW")!;

            foreach (var h in new[] { _nw, _n, _ne, _e, _se, _s, _sw, _w })
                h.PointerPressed += Handle_PointerPressed;

            _canvas.PointerPressed += PickCanvas_PointerPressed;
            _canvas.PointerMoved += PickCanvas_PointerMoved;
            _canvas.PointerReleased += PickCanvas_PointerReleased;
            _canvas.SizeChanged += (_, _) => RenderRect();

            this.FindControl<Button>("BtnDone")!.Click += (_, _) => { Committed = true; Close(); };
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => { Committed = false; Close(); };
            KeyDown += Window_KeyDown;
        }

        private enum DragMode { None, NewRect, Move, ResizeNW, ResizeN, ResizeNE, ResizeE, ResizeSE, ResizeS, ResizeSW, ResizeW }

        // -- Pointer on the picker canvas (background) ------------------------

        private void PickCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(_canvas);
            // If the click is inside the existing rect, it's a move. Otherwise
            // start a fresh rectangle (overwriting the existing one).
            if (_rect[2] > 0 && _rect[3] > 0 && IsInsideRect(p))
            {
                BeginDrag(DragMode.Move, p);
            }
            else
            {
                BeginDrag(DragMode.NewRect, p);
                _rect[0] = p.X / Math.Max(1, _canvas.Bounds.Width);
                _rect[1] = p.Y / Math.Max(1, _canvas.Bounds.Height);
                _rect[2] = 0.001;
                _rect[3] = 0.001;
                RenderRect();
            }
            e.Pointer.Capture(_canvas);
            e.Handled = true;
        }

        private void PickCanvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_drag == DragMode.None || !e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
            ApplyDrag(e.GetPosition(_canvas));
        }

        private void PickCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_drag == DragMode.None) return;
            e.Pointer.Capture(null);
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

        // -- Pointer on a resize handle ---------------------------------------

        private void Handle_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Rectangle r || r.Tag is not string tag) return;
            if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
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
            BeginDrag(mode, e.GetPosition(_canvas));
            e.Pointer.Capture(_canvas);
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
            var w = Math.Max(1, _canvas.Bounds.Width);
            var h = Math.Max(1, _canvas.Bounds.Height);
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
            var w = _canvas.Bounds.Width;
            var h = _canvas.Bounds.Height;
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

        private void RenderRect()
        {
            var w = _canvas.Bounds.Width;
            var h = _canvas.Bounds.Height;
            if (w <= 0 || h <= 0) return;

            var rx = _rect[0] * w;
            var ry = _rect[1] * h;
            var rw = Math.Max(2, _rect[2] * w);
            var rh = Math.Max(2, _rect[3] * h);

            _rectShape.IsVisible = true;
            _rectShape.Width = rw;
            _rectShape.Height = rh;
            Canvas.SetLeft(_rectShape, rx);
            Canvas.SetTop(_rectShape, ry);

            PositionHandle(_nw, rx, ry);
            PositionHandle(_n,  rx + rw / 2, ry);
            PositionHandle(_ne, rx + rw, ry);
            PositionHandle(_e,  rx + rw, ry + rh / 2);
            PositionHandle(_se, rx + rw, ry + rh);
            PositionHandle(_s,  rx + rw / 2, ry + rh);
            PositionHandle(_sw, rx, ry + rh);
            PositionHandle(_w,  rx, ry + rh / 2);

            _txtCoords.Text = string.Format(CultureInfo.InvariantCulture,
                "x={0:0.000}  y={1:0.000}  w={2:0.000}  h={3:0.000}",
                _rect[0], _rect[1], _rect[2], _rect[3]);
        }

        private static void PositionHandle(Rectangle h, double cx, double cy)
        {
            h.IsVisible = true;
            Canvas.SetLeft(h, cx - h.Width / 2);
            Canvas.SetTop(h, cy - h.Height / 2);
        }

        // -- Commit / cancel ---------------------------------------------------

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Committed = false; Close(); }
            else if (e.Key == Key.Enter) { Committed = true; Close(); }
        }
    }
}
