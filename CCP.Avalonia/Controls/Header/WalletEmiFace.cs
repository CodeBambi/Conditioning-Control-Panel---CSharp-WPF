using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// The wallet croupier's face: a header-owned port of the FLAT path of WPF
    /// Services/EmiDesk/EmiFace.cs (the renderer the desk and the Arcademy share). Same LOCKED
    /// numbers: a 152 x 137 virtual glass, the glyph fit to 95 % of it, a 5 px round pink stroke over
    /// a pink fill, raised 2 % of the box height, the ink box (not the advance box) centred, the same
    /// font chain. The wallet only ever draws "^_^", so the sideways / kaomoji / THINKING branches are
    /// left out.
    /// ponytail: the full EmiFace (side faces, kaomoji lift, Blink) belongs with the EMI desk port
    /// (studio lane, Views/Windows/EmiDesk); swap this for it once it exists.
    /// </summary>
    public sealed class WalletEmiFace : Control
    {
        public const double VirtualWidth = 152.0;
        public static readonly double VirtualHeight = Math.Round(VirtualWidth * 0.903);
        private const double FillFrac = 0.95;
        private const double Thick = 5.0;
        private const double LiftPercent = 2.0;

        private const string FallbackChain =
            "Noto Sans Mono, Cascadia Mono, Consolas, Noto Sans Symbols 2, Segoe UI Symbol, " +
            "Segoe UI Emoji, Nirmala UI, MS Gothic, Courier New, monospace";

        private static readonly IBrush PinkBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
        private static readonly Pen PinkPen = new(PinkBrush, Thick, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        private string? _face;

        public string? Face
        {
            get => _face;
            set { if (_face == value) return; _face = value; InvalidateVisual(); }
        }

        /// <summary>WPF EmiFace.Draw.</summary>
        public void Draw(string? text) => Face = text;

        public override void Render(DrawingContext dc)
        {
            try
            {
                double aw = Bounds.Width, ah = Bounds.Height;
                var t = _face;
                if (aw <= 0 || ah <= 0 || string.IsNullOrEmpty(t)) return;
                using (dc.PushClip(new Rect(0, 0, aw, ah)))
                using (dc.PushTransform(Matrix.CreateScale(aw / VirtualWidth, ah / VirtualHeight)))
                    Paint(dc, t!);
            }
            catch (Exception ex) { Log.Debug(ex, "[Wallet] face render failed"); }
        }

        /// <summary>The ink box of <paramref name="t"/> fit into the glass (test seam).</summary>
        internal static Rect FitInk(string t, out Geometry? geo)
        {
            double w = VirtualWidth, h = VirtualHeight;
            double boxW = w * FillFrac, boxH = h * FillFrac;
            double fs = Math.Max(6, Math.Floor(boxH));
            geo = Build(t, fs, out var ink);
            double fitW = boxW - Thick * 2, fitH = boxH - Thick * 2;
            double k = Math.Min(Math.Min(fitW / Math.Max(1, ink.Width), fitH / Math.Max(1, ink.Height)), 1.0);
            if (k < 1) { fs = Math.Max(4, Math.Floor(fs * k)); geo = Build(t, fs, out ink); }
            if (ink.Width > fitW || ink.Height > fitH)
            {
                fs = Math.Max(4, Math.Floor(fs * Math.Min(fitW / ink.Width, fitH / ink.Height)));
                geo = Build(t, fs, out ink);
            }
            return ink;
        }

        private static void Paint(DrawingContext dc, string t)
        {
            var ink = FitInk(t, out var geo);
            if (geo == null) return;
            double lift = -(LiftPercent / 100.0) * VirtualHeight;
            var m = Matrix.CreateTranslation(-(ink.X + ink.Width / 2.0), lift - (ink.Y + ink.Height / 2.0))
                    * Matrix.CreateTranslation(VirtualWidth / 2.0, VirtualHeight / 2.0);
            using (dc.PushTransform(m))
                dc.DrawGeometry(PinkBrush, PinkPen, geo);
        }

        private static Geometry? Build(string t, double fs, out Rect ink)
        {
            var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(FallbackChain)), Math.Max(1.0, fs), PinkBrush);
            var geo = ft.BuildGeometry(new Point(0, 0));
            var b = geo?.Bounds ?? default;
            ink = b.Width <= 0 || b.Height <= 0 ? new Rect(0, 0, 1, 1) : b;
            return geo;
        }
    }
}
