using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media.Immutable;
using AvDc = Avalonia.Media.DrawingContext;
using AvMedia = Avalonia.Media;

// The board's scene art was written against WPF's retained drawing API (DrawingContext, mutable
// Rect / Matrix, StreamGeometry with per-segment flags, gradient brushes taking Points). These
// shims give the scene files in this namespace the same surface over Avalonia's immediate
// drawing, so ~5,000 lines of 7.1.5 scene code port with only mechanical edits and stay
// diffable against the WPF original. They are scoped to this namespace on purpose: nothing
// outside Controls/Billboard/Scenes sees them, and the card host uses real Avalonia types.
namespace ConditioningControlPanel.Avalonia.Controls.Billboard.Scenes
{
    // ---- geometry values (WPF: mutable structs) ------------------------------------------------

    public struct Vector
    {
        public Vector(double x, double y) { X = x; Y = y; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Length => Math.Sqrt(X * X + Y * Y);
        public double LengthSquared => X * X + Y * Y;
        public void Normalize() { double l = Length; if (l > 0) { X /= l; Y /= l; } }
        public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y);
        public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y);
        public static Vector operator -(Vector a) => new(-a.X, -a.Y);
        public static Vector operator *(Vector a, double k) => new(a.X * k, a.Y * k);
        public static Vector operator *(double k, Vector a) => new(a.X * k, a.Y * k);
        public static Vector operator /(Vector a, double k) => new(a.X / k, a.Y / k);
    }

    public struct Point : IEquatable<Point>
    {
        public Point(double x, double y) { X = x; Y = y; }
        public double X { get; set; }
        public double Y { get; set; }
        public void Offset(double dx, double dy) { X += dx; Y += dy; }
        public static Point operator +(Point p, Vector v) => new(p.X + v.X, p.Y + v.Y);
        public static Point operator -(Point p, Vector v) => new(p.X - v.X, p.Y - v.Y);
        public static Vector operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static bool operator ==(Point a, Point b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Point a, Point b) => !(a == b);
        public bool Equals(Point o) => this == o;
        public override bool Equals(object? obj) => obj is Point p && this == p;
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static implicit operator global::Avalonia.Point(Point p) => new(p.X, p.Y);
        public static implicit operator Point(global::Avalonia.Point p) => new(p.X, p.Y);
        public static explicit operator Vector(Point p) => new(p.X, p.Y);
        public override string ToString() => X.ToString(CultureInfo.InvariantCulture) + "," + Y.ToString(CultureInfo.InvariantCulture);
    }

    public struct Size
    {
        public Size(double w, double h) { Width = w; Height = h; }
        public double Width { get; set; }
        public double Height { get; set; }
        public static bool operator ==(Size a, Size b) => a.Width == b.Width && a.Height == b.Height;
        public static bool operator !=(Size a, Size b) => !(a == b);
        public override bool Equals(object? o) => o is Size x && x == this;
        public override int GetHashCode() => HashCode.Combine(Width, Height);
        public static implicit operator global::Avalonia.Size(Size s) => new(Math.Max(0, s.Width), Math.Max(0, s.Height));
    }

    public struct Rect
    {
        public Rect(double x, double y, double w, double h) { X = x; Y = y; Width = Math.Max(0, w); Height = Math.Max(0, h); }
        public Rect(Point a, Point b) : this(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) { }
        public Rect(Point p, Size s) : this(p.X, p.Y, s.Width, s.Height) { }
        public Rect(Size s) : this(0, 0, s.Width, s.Height) { }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Left => X;
        public double Top => Y;
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public Point TopLeft => new(X, Y);
        public Point TopRight => new(Right, Y);
        public Point BottomLeft => new(X, Bottom);
        public Point BottomRight => new(Right, Bottom);
        public Point Location => new(X, Y);
        public Size Size => new(Width, Height);
        public bool IsEmpty => Width <= 0 && Height <= 0;
        public void Offset(double dx, double dy) { X += dx; Y += dy; }
        public void Offset(Vector v) => Offset(v.X, v.Y);
        public void Inflate(double dx, double dy) { X -= dx; Y -= dy; Width = Math.Max(0, Width + 2 * dx); Height = Math.Max(0, Height + 2 * dy); }
        public bool Contains(Point p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
        public void Union(Rect o)
        {
            double l = Math.Min(X, o.X), t = Math.Min(Y, o.Y), r = Math.Max(Right, o.Right), b = Math.Max(Bottom, o.Bottom);
            X = l; Y = t; Width = r - l; Height = b - t;
        }
        public static bool operator ==(Rect a, Rect b) => a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;
        public static bool operator !=(Rect a, Rect b) => !(a == b);
        public override bool Equals(object? o) => o is Rect x && x == this;
        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
        public static implicit operator global::Avalonia.Rect(Rect r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));
        public static implicit operator Rect(global::Avalonia.Rect r) => new(r.X, r.Y, r.Width, r.Height);
    }

    /// <summary>WPF's mutable affine matrix: Scale/Rotate/Translate APPEND (row vectors), as WPF does.</summary>
    public struct Matrix
    {
        public Matrix(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
        { M11 = m11; M12 = m12; M21 = m21; M22 = m22; OffsetX = offsetX; OffsetY = offsetY; }
        public double M11 { get; set; }
        public double M12 { get; set; }
        public double M21 { get; set; }
        public double M22 { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);
        public bool IsIdentity => M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && OffsetX == 0 && OffsetY == 0;

        public static Matrix Multiply(Matrix a, Matrix b) => new(
            a.M11 * b.M11 + a.M12 * b.M21, a.M11 * b.M12 + a.M12 * b.M22,
            a.M21 * b.M11 + a.M22 * b.M21, a.M21 * b.M12 + a.M22 * b.M22,
            a.OffsetX * b.M11 + a.OffsetY * b.M21 + b.OffsetX, a.OffsetX * b.M12 + a.OffsetY * b.M22 + b.OffsetY);
        public static Matrix operator *(Matrix a, Matrix b) => Multiply(a, b);

        public void Append(Matrix m) => this = Multiply(this, m);
        public void Prepend(Matrix m) => this = Multiply(m, this);
        public void Scale(double sx, double sy) => Append(new Matrix(sx, 0, 0, sy, 0, 0));
        public void ScaleAt(double sx, double sy, double cx, double cy) => Append(new Matrix(sx, 0, 0, sy, cx - sx * cx, cy - sy * cy));
        public void Translate(double dx, double dy) { OffsetX += dx; OffsetY += dy; }
        public void ScalePrepend(double sx, double sy) => Prepend(new Matrix(sx, 0, 0, sy, 0, 0));
        public void ScaleAtPrepend(double sx, double sy, double cx, double cy) => Prepend(new Matrix(sx, 0, 0, sy, cx - sx * cx, cy - sy * cy));
        public void TranslatePrepend(double dx, double dy) => Prepend(new Matrix(1, 0, 0, 1, dx, dy));
        public void RotatePrepend(double degrees) => Prepend(Rot(degrees, 0, 0));
        public void RotateAtPrepend(double degrees, double cx, double cy) => Prepend(Rot(degrees, cx, cy));
        public void Rotate(double degrees) => Append(Rot(degrees, 0, 0));
        public void RotateAt(double degrees, double cx, double cy) => Append(Rot(degrees, cx, cy));
        public void Skew(double skewXDeg, double skewYDeg) =>
            Append(new Matrix(1, Math.Tan(skewYDeg * Math.PI / 180), Math.Tan(skewXDeg * Math.PI / 180), 1, 0, 0));

        private static Matrix Rot(double deg, double cx, double cy)
        {
            double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            return new Matrix(c, s, -s, c, cx * (1 - c) + cy * s, cy * (1 - c) - cx * s);
        }

        public double Determinant => M11 * M22 - M12 * M21;
        public bool HasInverse => Math.Abs(Determinant) > 1e-12;

        public void Invert()
        {
            double d = Determinant;
            if (Math.Abs(d) < 1e-12) throw new InvalidOperationException("Matrix is not invertible.");
            var m = new Matrix(M22 / d, -M12 / d, -M21 / d, M11 / d,
                (M21 * OffsetY - M22 * OffsetX) / d, (M12 * OffsetX - M11 * OffsetY) / d);
            this = m;
        }

        public Point Transform(Point p) => new(p.X * M11 + p.Y * M21 + OffsetX, p.X * M12 + p.Y * M22 + OffsetY);
        public Vector Transform(Vector v) => new(v.X * M11 + v.Y * M21, v.X * M12 + v.Y * M22);

        public static implicit operator global::Avalonia.Matrix(Matrix m) => new(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);
    }

    // ---- transforms ---------------------------------------------------------------------------------

    public abstract class Transform
    {
        public abstract Matrix Value { get; }
        public void Freeze() { }
        public static Transform Identity { get; } = new MatrixTransform(Matrix.Identity);
    }

    public sealed class MatrixTransform : Transform
    {
        public MatrixTransform() { }
        public MatrixTransform(Matrix m) => Matrix = m;
        public MatrixTransform(double m11, double m12, double m21, double m22, double ox, double oy) => Matrix = new Matrix(m11, m12, m21, m22, ox, oy);
        public Matrix Matrix { get; set; } = Matrix.Identity;
        public override Matrix Value => Matrix;
    }

    public sealed class TranslateTransform : Transform
    {
        public TranslateTransform() { }
        public TranslateTransform(double x, double y) { X = x; Y = y; }
        public double X { get; set; }
        public double Y { get; set; }
        public override Matrix Value => new(1, 0, 0, 1, X, Y);
    }

    public sealed class ScaleTransform : Transform
    {
        public ScaleTransform() { }
        public ScaleTransform(double sx, double sy) { ScaleX = sx; ScaleY = sy; }
        public ScaleTransform(double sx, double sy, double cx, double cy) { ScaleX = sx; ScaleY = sy; CenterX = cx; CenterY = cy; }
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public override Matrix Value { get { var m = Matrix.Identity; m.ScaleAt(ScaleX, ScaleY, CenterX, CenterY); return m; } }
    }

    public sealed class RotateTransform : Transform
    {
        public RotateTransform() { }
        public RotateTransform(double angle) => Angle = angle;
        public RotateTransform(double angle, double cx, double cy) { Angle = angle; CenterX = cx; CenterY = cy; }
        public double Angle { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public override Matrix Value { get { var m = Matrix.Identity; m.RotateAt(Angle, CenterX, CenterY); return m; } }
    }

    public sealed class TransformGroup : Transform
    {
        public List<Transform> Children { get; } = new();
        public override Matrix Value
        {
            get
            {
                var m = Matrix.Identity;
                foreach (var t in Children) m.Append(t.Value);
                return m;
            }
        }
    }

    // ---- brushes and pens -----------------------------------------------------------------------------

    public enum BrushMappingMode { Absolute, RelativeToBoundingBox }

    public abstract class Brush
    {
        private AvMedia.IImmutableBrush? _native;
        private double _opacity = 1;
        public double Opacity { get => _opacity; set { _opacity = value; _native = null; } }
        public void Freeze() { }
        protected void Changed() => _native = null;
        internal AvMedia.IImmutableBrush Native => _native ??= Build();
        protected abstract AvMedia.IImmutableBrush Build();
    }

    public sealed class SolidColorBrush : Brush
    {
        private AvMedia.Color _color;
        public SolidColorBrush() { }
        public SolidColorBrush(AvMedia.Color c) => _color = c;
        public AvMedia.Color Color { get => _color; set { _color = value; Changed(); } }
        protected override AvMedia.IImmutableBrush Build() => new ImmutableSolidColorBrush(_color, Opacity);
    }

    public sealed class GradientStop
    {
        public GradientStop() { }
        public GradientStop(AvMedia.Color c, double offset) { Color = c; Offset = offset; }
        public AvMedia.Color Color { get; set; }
        public double Offset { get; set; }
    }

    public abstract class GradientBrush : Brush
    {
        public List<GradientStop> GradientStops { get; } = new();
        public BrushMappingMode MappingMode { get; set; } = BrushMappingMode.RelativeToBoundingBox;
        public AvMedia.GradientSpreadMethod SpreadMethod { get; set; } = AvMedia.GradientSpreadMethod.Pad;
        public Transform? Transform { get; set; }
        public Transform? RelativeTransform { get; set; }

        protected global::Avalonia.RelativeUnit Unit =>
            MappingMode == BrushMappingMode.Absolute ? global::Avalonia.RelativeUnit.Absolute : global::Avalonia.RelativeUnit.Relative;

        protected void Fill(AvMedia.GradientBrush b)
        {
            b.Opacity = Opacity;
            b.SpreadMethod = SpreadMethod;
            foreach (var s in GradientStops) b.GradientStops.Add(new AvMedia.GradientStop(s.Color, s.Offset));
            if (Transform != null) b.Transform = new AvMedia.MatrixTransform(Transform.Value);
        }
    }

    public sealed class LinearGradientBrush : GradientBrush
    {
        public LinearGradientBrush() { }
        public LinearGradientBrush(AvMedia.Color a, AvMedia.Color b, double angleDegrees)
        {
            GradientStops.Add(new GradientStop(a, 0));
            GradientStops.Add(new GradientStop(b, 1));
            double r = angleDegrees * Math.PI / 180;
            EndPoint = new Point(Math.Cos(r), Math.Sin(r));
        }
        public LinearGradientBrush(AvMedia.Color a, AvMedia.Color b, Point start, Point end)
        {
            GradientStops.Add(new GradientStop(a, 0));
            GradientStops.Add(new GradientStop(b, 1));
            StartPoint = start;
            EndPoint = end;
        }
        public Point StartPoint { get; set; } = new(0, 0);
        public Point EndPoint { get; set; } = new(1, 1);

        protected override AvMedia.IImmutableBrush Build()
        {
            var b = new AvMedia.LinearGradientBrush
            {
                StartPoint = new global::Avalonia.RelativePoint(StartPoint.X, StartPoint.Y, Unit),
                EndPoint = new global::Avalonia.RelativePoint(EndPoint.X, EndPoint.Y, Unit),
            };
            Fill(b);
            return (AvMedia.IImmutableBrush)b.ToImmutable();
        }
    }

    public sealed class RadialGradientBrush : GradientBrush
    {
        public RadialGradientBrush() { }
        public RadialGradientBrush(AvMedia.Color a, AvMedia.Color b)
        {
            GradientStops.Add(new GradientStop(a, 0));
            GradientStops.Add(new GradientStop(b, 1));
        }
        public RadialGradientBrush(GradientStopCollection stops) { foreach (var s in stops) GradientStops.Add(s); }
        public Point Center { get; set; } = new(0.5, 0.5);
        public Point GradientOrigin { get; set; } = new(0.5, 0.5);
        public double RadiusX { get; set; } = 0.5;
        public double RadiusY { get; set; } = 0.5;

        protected override AvMedia.IImmutableBrush Build()
        {
            var b = new AvMedia.RadialGradientBrush
            {
                Center = new global::Avalonia.RelativePoint(Center.X, Center.Y, Unit),
                GradientOrigin = new global::Avalonia.RelativePoint(GradientOrigin.X, GradientOrigin.Y, Unit),
                RadiusX = new global::Avalonia.RelativeScalar(RadiusX, Unit),
                RadiusY = new global::Avalonia.RelativeScalar(RadiusY, Unit),
            };
            Fill(b);
            return (AvMedia.IImmutableBrush)b.ToImmutable();
        }
    }

    public sealed class GradientStopCollection : List<GradientStop> { }

    public static class Brushes
    {
        public static SolidColorBrush White { get; } = new(AvMedia.Colors.White);
        public static SolidColorBrush Black { get; } = new(AvMedia.Colors.Black);
        public static SolidColorBrush Transparent { get; } = new(AvMedia.Colors.Transparent);
    }

    public sealed class DashStyle
    {
        public DashStyle() { }
        public DashStyle(IEnumerable<double> dashes, double offset) { Dashes = new List<double>(dashes); Offset = offset; }
        public List<double> Dashes { get; set; } = new();
        public double Offset { get; set; }
        public void Freeze() { }
    }

    public sealed class Pen
    {
        private ImmutablePen? _native;
        public Pen() { }
        public Pen(Brush? brush, double thickness) { Brush = brush; Thickness = thickness; }
        public Brush? Brush { get; set; }
        public double Thickness { get; set; } = 1;
        public AvMedia.PenLineCap StartLineCap { get; set; } = AvMedia.PenLineCap.Flat;
        public AvMedia.PenLineCap EndLineCap { get; set; } = AvMedia.PenLineCap.Flat;
        public AvMedia.PenLineCap DashCap { get; set; } = AvMedia.PenLineCap.Square;
        public AvMedia.PenLineJoin LineJoin { get; set; } = AvMedia.PenLineJoin.Miter;
        public double MiterLimit { get; set; } = 10;
        public DashStyle? DashStyle { get; set; }
        public void Freeze() { }

        /// <summary>Avalonia has one cap for both ends (and the dashes): WPF's start cap wins, a
        /// round dash cap rounds the whole line.</summary>
        internal ImmutablePen Native => _native ??= new ImmutablePen(
            Brush?.Native, Thickness,
            DashStyle == null ? null : new ImmutableDashStyle(DashStyle.Dashes, DashStyle.Offset),
            DashStyle != null && DashCap == AvMedia.PenLineCap.Round ? AvMedia.PenLineCap.Round : StartLineCap,
            LineJoin, MiterLimit);
    }

    // ---- geometry -----------------------------------------------------------------------------------------

    public enum FillRule { EvenOdd, Nonzero }

    public enum ToleranceType { Absolute, Relative }

    public enum SweepDirection { Counterclockwise, Clockwise }

    public abstract class Geometry
    {
        private AvMedia.Geometry? _native;
        private Transform? _transform;

        public Transform? Transform { get => _transform; set { _transform = value; _native = null; } }
        public void Freeze() { }

        /// <summary>WPF strokes only segments flagged isStroked; Avalonia strokes every segment.
        /// A figure built with no stroked segment at all is drawn fill-only, which is what those
        /// WPF shapes looked like.</summary>
        internal virtual bool Unstroked => false;

        internal AvMedia.Geometry Native
        {
            get
            {
                if (_native != null) return _native;
                var g = Build();
                if (_transform != null && !_transform.Value.IsIdentity)
                {
                    g = g.Clone();
                    g.Transform = new AvMedia.MatrixTransform(_transform.Value);
                }
                return _native = g;
            }
        }

        protected abstract AvMedia.Geometry Build();

        public Rect Bounds => Native.Bounds;

        public Geometry Clone() => new WrappedGeometry(Native.Clone(), Unstroked);

        public Geometry GetFlattenedPathGeometry(double tolerance, ToleranceType type) => Clone();

        public bool FillContains(Point p) => Native.FillContains(p);

        public static Geometry Combine(Geometry a, Geometry b, AvMedia.GeometryCombineMode mode, Transform? transform)
        {
            var g = new CombinedGeometry(mode, a, b);
            if (transform != null) g.Transform = transform;
            return g;
        }
    }

    internal sealed class WrappedGeometry : Geometry
    {
        private readonly AvMedia.Geometry _g;
        private readonly bool _unstroked;
        public WrappedGeometry(AvMedia.Geometry g, bool unstroked) { _g = g; _unstroked = unstroked; }
        internal override bool Unstroked => _unstroked;
        protected override AvMedia.Geometry Build() => _g;
    }

    public sealed class PathGeometry : Geometry
    {
        private readonly AvMedia.Geometry _g;
        private PathGeometry(AvMedia.Geometry g) => _g = g;
        public static PathGeometry CreateFromGeometry(Geometry g) => new(g.Native);
        protected override AvMedia.Geometry Build() => _g;

        public void GetPointAtFractionLength(double fraction, out Point point, out Point tangent)
        {
            double len = _g.ContourLength;
            point = default;
            tangent = default;
            if (_g.TryGetPointAndTangentAtDistance(Math.Clamp(fraction, 0, 1) * len, out var p, out var t))
            {
                point = p;
                tangent = t;
            }
        }
    }

    public sealed class RectangleGeometry : Geometry
    {
        public RectangleGeometry(Rect r) : this(r, 0, 0) { }
        public RectangleGeometry(Rect r, double rx, double ry) { Rect = r; RadiusX = rx; RadiusY = ry; }
        public Rect Rect { get; }
        public double RadiusX { get; }
        public double RadiusY { get; }

        protected override AvMedia.Geometry Build()
        {
            var r = Rect;
            double rx = Math.Min(Math.Abs(RadiusX), r.Width / 2), ry = Math.Min(Math.Abs(RadiusY), r.Height / 2);
            if (rx <= 0 || ry <= 0) return new AvMedia.RectangleGeometry(r);
            var g = new AvMedia.StreamGeometry();
            using (var c = g.Open())
            {
                c.SetFillRule(AvMedia.FillRule.NonZero);
                var arc = new global::Avalonia.Size(rx, ry);
                c.BeginFigure(new global::Avalonia.Point(r.X + rx, r.Y), true);
                c.LineTo(new global::Avalonia.Point(r.Right - rx, r.Y));
                c.ArcTo(new global::Avalonia.Point(r.Right, r.Y + ry), arc, 0, false, AvMedia.SweepDirection.Clockwise);
                c.LineTo(new global::Avalonia.Point(r.Right, r.Bottom - ry));
                c.ArcTo(new global::Avalonia.Point(r.Right - rx, r.Bottom), arc, 0, false, AvMedia.SweepDirection.Clockwise);
                c.LineTo(new global::Avalonia.Point(r.X + rx, r.Bottom));
                c.ArcTo(new global::Avalonia.Point(r.X, r.Bottom - ry), arc, 0, false, AvMedia.SweepDirection.Clockwise);
                c.LineTo(new global::Avalonia.Point(r.X, r.Y + ry));
                c.ArcTo(new global::Avalonia.Point(r.X + rx, r.Y), arc, 0, false, AvMedia.SweepDirection.Clockwise);
                c.EndFigure(true);
            }
            return g;
        }
    }

    public sealed class EllipseGeometry : Geometry
    {
        public EllipseGeometry(Point center, double rx, double ry) { Center = center; RadiusX = rx; RadiusY = ry; }
        public EllipseGeometry(Rect r) : this(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2) { }
        public Point Center { get; }
        public double RadiusX { get; }
        public double RadiusY { get; }
        protected override AvMedia.Geometry Build() =>
            new AvMedia.EllipseGeometry(new global::Avalonia.Rect(Center.X - RadiusX, Center.Y - RadiusY, Math.Max(0, RadiusX * 2), Math.Max(0, RadiusY * 2)));
    }

    public sealed class CombinedGeometry : Geometry
    {
        public CombinedGeometry(AvMedia.GeometryCombineMode mode, Geometry a, Geometry b) { Mode = mode; A = a; B = b; }
        public AvMedia.GeometryCombineMode Mode { get; }
        public Geometry A { get; }
        public Geometry B { get; }
        protected override AvMedia.Geometry Build() => new AvMedia.CombinedGeometry(Mode, A.Native, B.Native);
    }

    public sealed class GeometryGroup : Geometry
    {
        public List<Geometry> Children { get; } = new();
        public FillRule FillRule { get; set; } = FillRule.EvenOdd;
        protected override AvMedia.Geometry Build()
        {
            var g = new AvMedia.GeometryGroup { FillRule = FillRule == FillRule.Nonzero ? AvMedia.FillRule.NonZero : AvMedia.FillRule.EvenOdd };
            foreach (var c in Children) g.Children.Add(c.Native);
            return g;
        }
    }

    /// <summary>WPF StreamGeometry: figures recorded through <see cref="Open"/>; FillRule defaults to EvenOdd as in WPF.</summary>
    public sealed class StreamGeometry : Geometry
    {
        private readonly List<Action<AvMedia.StreamGeometryContext>> _ops = new();
        private bool _anyStroked;
        public FillRule FillRule { get; set; } = FillRule.EvenOdd;
        internal override bool Unstroked => !_anyStroked && _ops.Count > 0;
        public StreamGeometryContext Open() => new(this);

        internal void Add(Action<AvMedia.StreamGeometryContext> op, bool stroked)
        {
            _ops.Add(op);
            if (stroked) _anyStroked = true;
        }

        protected override AvMedia.Geometry Build()
        {
            var g = new AvMedia.StreamGeometry();
            using (var c = g.Open())
            {
                c.SetFillRule(FillRule == FillRule.Nonzero ? AvMedia.FillRule.NonZero : AvMedia.FillRule.EvenOdd);
                foreach (var op in _ops) op(c);
            }
            return g;
        }
    }

    public sealed class StreamGeometryContext : IDisposable
    {
        private readonly StreamGeometry _g;
        private bool _open, _closed;
        internal StreamGeometryContext(StreamGeometry g) => _g = g;

        public void BeginFigure(Point start, bool isFilled, bool isClosed)
        {
            EndOpen();
            _open = true;
            _closed = isClosed;
            _g.Add(c => c.BeginFigure(start, isFilled), false);
        }

        public void LineTo(Point p, bool isStroked, bool isSmoothJoin) => _g.Add(c => c.LineTo(p), isStroked);

        public void QuadraticBezierTo(Point c1, Point p, bool isStroked, bool isSmoothJoin) =>
            _g.Add(c => c.QuadraticBezierTo(c1, p), isStroked);

        public void BezierTo(Point c1, Point c2, Point p, bool isStroked, bool isSmoothJoin) =>
            _g.Add(c => c.CubicBezierTo(c1, c2, p), isStroked);

        public void ArcTo(Point p, Size size, double rotationAngle, bool isLargeArc, SweepDirection sweep, bool isStroked, bool isSmoothJoin) =>
            _g.Add(c => c.ArcTo(p, size, rotationAngle, isLargeArc,
                sweep == SweepDirection.Clockwise ? AvMedia.SweepDirection.Clockwise : AvMedia.SweepDirection.CounterClockwise), isStroked);

        public void PolyLineTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
        {
            foreach (var p in points) LineTo(p, isStroked, isSmoothJoin);
        }

        private void EndOpen()
        {
            if (!_open) return;
            bool closed = _closed;
            _g.Add(c => c.EndFigure(closed), false);
            _open = false;
        }

        public void Close() => Dispose();

        public void Dispose() => EndOpen();
    }

    // ---- text ---------------------------------------------------------------------------------------------

    public static class FontStyles
    {
        public static AvMedia.FontStyle Normal => AvMedia.FontStyle.Normal;
        public static AvMedia.FontStyle Italic => AvMedia.FontStyle.Italic;
    }

    public static class FontWeights
    {
        public static AvMedia.FontWeight Normal => AvMedia.FontWeight.Normal;
        public static AvMedia.FontWeight Medium => AvMedia.FontWeight.Medium;
        public static AvMedia.FontWeight SemiBold => AvMedia.FontWeight.SemiBold;
        public static AvMedia.FontWeight Bold => AvMedia.FontWeight.Bold;
        public static AvMedia.FontWeight ExtraBold => AvMedia.FontWeight.ExtraBold;
        public static AvMedia.FontWeight Black => AvMedia.FontWeight.Black;
    }

    public static class FontStretches
    {
        public static AvMedia.FontStretch Normal => AvMedia.FontStretch.Normal;
    }

    public sealed class Typeface
    {
        public Typeface(AvMedia.FontFamily family, AvMedia.FontStyle style, AvMedia.FontWeight weight, AvMedia.FontStretch stretch) =>
            Native = new AvMedia.Typeface(family, style, weight, stretch);
        public Typeface(string family) => Native = new AvMedia.Typeface(family);
        internal AvMedia.Typeface Native { get; }
    }

    /// <summary>WPF FormattedText over Avalonia's: same constructor shape (the pixelsPerDip is unused).</summary>
    public sealed class FormattedText
    {
        private readonly AvMedia.FormattedText _t;

        public FormattedText(string text, CultureInfo culture, AvMedia.FlowDirection flow, Typeface face, double emSize, Brush foreground, double pixelsPerDip)
            => _t = new AvMedia.FormattedText(text ?? string.Empty, culture, flow, face.Native, Math.Max(0.5, emSize), foreground.Native);

        public double MaxTextWidth { get => _t.MaxTextWidth; set => _t.MaxTextWidth = value; }
        public int MaxLineCount { get => _t.MaxLineCount; set => _t.MaxLineCount = value; }
        public AvMedia.TextTrimming Trimming { get => _t.Trimming; set => _t.Trimming = value; }
        public AvMedia.TextAlignment TextAlignment { get => _t.TextAlignment; set => _t.TextAlignment = value; }
        public double Width => _t.Width;
        public double WidthIncludingTrailingWhitespace => _t.WidthIncludingTrailingWhitespace;
        public double Height => _t.Height;
        public double Baseline => _t.Baseline;
        internal AvMedia.FormattedText Native => _t;
    }

    // ---- drawing ----------------------------------------------------------------------------------------------

    /// <summary>A recorded picture (WPF DrawingGroup): ops captured once through <see cref="Open"/>, replayed per frame.</summary>
    public abstract class Drawing
    {
        internal abstract void Replay(DrawingContext dc);
        public void Freeze() { }
    }

    public sealed class DrawingGroup : Drawing
    {
        private List<Action<DrawingContext>> _ops = new();
        public DrawingContext Open()
        {
            _ops = new List<Action<DrawingContext>>();
            return new DrawingContext(_ops);
        }
        internal override void Replay(DrawingContext dc)
        {
            foreach (var op in _ops) op(dc);
        }
    }

    /// <summary>
    /// WPF's DrawingContext over Avalonia's: Push*/Pop as a stack (Avalonia hands back a pushed
    /// state to dispose), brushes and pens converted at the call, rounded rectangles native. A
    /// context opened on a <see cref="DrawingGroup"/> records instead of drawing.
    /// </summary>
    public sealed class DrawingContext : IDisposable
    {
        private readonly AvDc? _dc;
        private readonly List<Action<DrawingContext>>? _rec;
        private readonly Stack<AvDc.PushedState> _stack = new();

        public DrawingContext(AvDc dc) => _dc = dc;

        internal DrawingContext(List<Action<DrawingContext>> rec) => _rec = rec;

        private bool Rec(Action<DrawingContext> op)
        {
            if (_rec == null) return false;
            _rec.Add(op);
            return true;
        }

        public void DrawRectangle(Brush? brush, Pen? pen, Rect r)
        {
            if (Rec(d => d.DrawRectangle(brush, pen, r))) return;
            if (brush == null && pen == null) return;
            _dc!.DrawRectangle(brush?.Native, pen?.Native, r);
        }

        public void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect r, double rx, double ry)
        {
            if (Rec(d => d.DrawRoundedRectangle(brush, pen, r, rx, ry))) return;
            if (brush == null && pen == null) return;
            double mx = Math.Min(Math.Abs(rx), r.Width / 2), my = Math.Min(Math.Abs(ry), r.Height / 2);
            _dc!.DrawRectangle(brush?.Native, pen?.Native, r, mx, my);
        }

        public void DrawEllipse(Brush? brush, Pen? pen, Point c, double rx, double ry)
        {
            if (Rec(d => d.DrawEllipse(brush, pen, c, rx, ry))) return;
            if (brush == null && pen == null) return;
            _dc!.DrawEllipse(brush?.Native, pen?.Native, c, Math.Abs(rx), Math.Abs(ry));
        }

        public void DrawLine(Pen pen, Point a, Point b)
        {
            if (Rec(d => d.DrawLine(pen, a, b))) return;
            if (pen == null) return;
            _dc!.DrawLine(pen.Native, a, b);
        }

        public void DrawGeometry(Brush? brush, Pen? pen, Geometry g)
        {
            if (Rec(d => d.DrawGeometry(brush, pen, g))) return;
            if (g.Unstroked) pen = null;
            if (brush == null && pen == null) return;
            _dc!.DrawGeometry(brush?.Native, pen?.Native, g.Native);
        }

        public void DrawDrawing(Drawing drawing)
        {
            if (Rec(d => d.DrawDrawing(drawing))) return;
            drawing.Replay(this);
        }

        public void DrawText(FormattedText text, Point at)
        {
            if (Rec(d => d.DrawText(text, at))) return;
            _dc!.DrawText(text.Native, at);
        }

        public void DrawImage(AvMedia.IImage image, Rect dest)
        {
            if (Rec(d => d.DrawImage(image, dest))) return;
            _dc!.DrawImage(image, dest);
        }

        public void PushTransform(Transform t)
        {
            var m = t.Value;
            if (Rec(d => d.PushTransform(new MatrixTransform(m)))) return;
            _stack.Push(_dc!.PushTransform(m));
        }

        public void PushOpacity(double opacity)
        {
            if (Rec(d => d.PushOpacity(opacity))) return;
            _stack.Push(_dc!.PushOpacity(Math.Clamp(opacity, 0, 1)));
        }

        public void PushClip(Geometry clip)
        {
            if (Rec(d => d.PushClip(clip))) return;
            _stack.Push(_dc!.PushGeometryClip(clip.Native));
        }

        public void Pop()
        {
            if (Rec(d => d.Pop())) return;
            if (_stack.Count > 0) _stack.Pop().Dispose();
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            while (_stack.Count > 0) _stack.Pop().Dispose();
        }
    }
}
