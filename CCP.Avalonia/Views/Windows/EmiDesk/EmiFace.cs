using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// EMI's face renderer: the Avalonia port of WPF Services/EmiDesk/EmiFace.cs (itself a port of
    /// <c>Resources/web/arcademy/emi/face.js</c>). Text drawn on a virtual 152 x 137 canvas in
    /// <c>#FF69B4</c>, fill + 5 px round stroke, scaled to the element. Every number is LOCKED by
    /// EMI-DESIGN-LOCK.md: glyph fills 95 % of the fit box, lift +2 % of box height (sideways faces
    /// too), kaomoji +10 % size / +10 % lift, THINKING dots 30 % size and -28 % lift, classic
    /// sideways faces rotate 90 degrees. Fit is against the INK box (geometry bounds), padded by
    /// the stroke width.
    /// </summary>
    public sealed class EmiFace : Control
    {
        public const double VirtualWidth = 152.0;
        public const double ScreenAspect = 0.903;
        public static readonly double VirtualHeight = Math.Round(VirtualWidth * ScreenAspect);
        public static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);

        private const double FillFrac = 0.95;
        private const double Thick = 5.0;
        private const double LiftPercent = 2.0;

        private static readonly IBrush PinkBrush = new SolidColorBrush(Pink).ToImmutable();
        private static readonly IPen PinkPen = new ImmutablePen(new SolidColorBrush(Pink).ToImmutable(), Thick,
            lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        // Same lists as face.js; duplicated so the renderer answers for arbitrary caller text.
        private static readonly string[] FlatSet =
        {
            "._.", "^_^", "^_~", ">.<", "@_@", "-_-", "o_o", "T_T", ">_<", "=_=", "\u00AC_\u00AC",
            "^___^", "x_x", "*_*", "0_0", ";_;", "(\u25C9_\u25C9)", "(\u2299_\u2299)", "(\u25D4_\u25D4)"
        };

        private static readonly string[] SideSet =
        {
            ":)", ":D", ";)", ":'(", ">:(", ":O", ":P", ":|", "<3", "XD", ":3", ">:)", ":/", "B)"
        };

        private static readonly string[] KaoSet =
        {
            "( \u0361\u00B0 \u035C\u0296 \u0361\u00B0)", "(\u00AC\u203F\u00AC)", "(\u25E0\u203F\u25E0)",
            "(\u2310\u25A0_\u25A0)", "(\u0CA0\u203F\u0CA0)", "(\u2716\u256D\u256E\u2716)",
            "(\u273F\u25E1\u203F\u25E1)", "(\u25D5\u203F\u25D5)", "(\u0CA5_\u0CA5)",
            "(\uFF61\u2665\u203F\u2665\uFF61)", "(\u2267\u25E1\u2266)"
        };

        private static readonly string[] SpecialSet =
        {
            "\\o/", "GG", "#ERR", "ZzZ", "!!!", "???", "LV UP", "6.7", "\u2665\u2665\u2665",
            "\u2605\u2605\u2605", "404", "brb"
        };

        private static readonly Regex SideRe =
            new(@"^[>]?[:;=8xXB][-'^]?[)(DPOop|/\\3]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex NonAsciiRe =
            new(@"[^\x00-\x7F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>True for a short classic ASCII face that rotates 90 degrees.</summary>
        public static bool IsSide(string? t) => !string.IsNullOrEmpty(t) && t!.Length <= 4 && SideRe.IsMatch(t);

        /// <summary>True for a kaomoji (the picked list, or any long non-ASCII face).</summary>
        public static bool IsKao(string? t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            if (Array.IndexOf(KaoSet, t) >= 0) return true;
            return Array.IndexOf(FlatSet, t) < 0
                && Array.IndexOf(SideSet, t) < 0
                && Array.IndexOf(SpecialSet, t) < 0
                && NonAsciiRe.IsMatch(t!)
                && t!.Replace("\u00AC", string.Empty).Length >= 5;
        }

        /// <summary>The shipped Noto Sans Mono first (Assets/emi/fonts, an AvaloniaResource), then the WPF system chain.</summary>
        public static readonly FontFamily FaceFont = new(
            "avares://CCP.Avalonia/Resources/emi/fonts#Noto Sans Mono, Noto Sans Mono, Cascadia Mono, Consolas, " +
            "Noto Sans Symbols 2, Segoe UI Symbol, Segoe UI Emoji, Nirmala UI, MS Gothic, Courier New, monospace");

        private string? _face = "0_0";
        private bool _small;
        private bool _flat;

        public string? Face
        {
            get => _face;
            set { if (_face == value) return; _face = value; InvalidateVisual(); }
        }

        public bool Small
        {
            get => _small;
            set { if (_small == value) return; _small = value; InvalidateVisual(); }
        }

        public bool Flat
        {
            get => _flat;
            set { if (_flat == value) return; _flat = value; InvalidateVisual(); }
        }

        /// <summary>Paint one face frame (the chain player's Draw hook).</summary>
        public void Draw(string? text, bool small = false, bool flat = false)
        {
            _small = small;
            _flat = flat;
            _face = text;
            InvalidateVisual();
        }

        public void Clear() => Face = null;

        private DispatcherTimer? _blinkTimer;
        private string? _blinkRestore;

        /// <summary>A 110 ms "-_-" blink, then back to whatever was up.</summary>
        public void Blink()
        {
            try
            {
                var restore = Face;
                if (string.IsNullOrEmpty(restore) || restore == "-_-") restore = "0_0";
                _blinkTimer?.Stop();
                Face = "-_-";
                if (_blinkTimer == null)
                {
                    _blinkTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(110) };
                    _blinkTimer.Tick += (_, _) =>
                    {
                        _blinkTimer?.Stop();
                        if (Face == "-_-") Face = _blinkRestore ?? "0_0";
                    };
                }
                _blinkRestore = restore;
                _blinkTimer.Start();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] Blink failed"); }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) ? VirtualWidth : availableSize.Width;
            double h = double.IsInfinity(availableSize.Height) ? VirtualHeight : availableSize.Height;
            return new Size(w, h);
        }

        public override void Render(DrawingContext dc)
        {
            try
            {
                double aw = Bounds.Width, ah = Bounds.Height;
                var t = _face;
                if (aw <= 0 || ah <= 0 || string.IsNullOrEmpty(t)) return;
                using (dc.PushClip(new Rect(0, 0, aw, ah)))
                using (dc.PushTransform(Matrix.CreateScale(aw / VirtualWidth, ah / VirtualHeight)))
                    Paint(dc, t!, _small, _flat);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] face render failed"); }
        }

        /// <summary>The fitted geometry and the transform that places it (test seam).</summary>
        internal static (Geometry? Geo, Rect Ink, Matrix Place, double FontSize) Layout(string t, bool small, bool flat)
        {
            const double W = VirtualWidth;
            double H = VirtualHeight;

            bool side = !flat && IsSide(t);
            bool kao = IsKao(t);
            double fill = FillFrac * (kao ? 1.10 : 1.0);

            double boxW = (side ? H : W) * fill;
            double boxH = (side ? W : H) * fill;

            double fs = Math.Max(6, Math.Floor(boxH));
            if (small) fs = Math.Max(6, Math.Floor(boxH * 0.30));

            var geo = Build(t, fs, out var ink);
            double pad = Thick;
            double fitW = boxW - pad * 2, fitH = boxH - pad * 2;
            double k = small ? 1.0 : Math.Min(Math.Min(fitW / Math.Max(1, ink.Width), fitH / Math.Max(1, ink.Height)), 1.0);
            if (k < 1)
            {
                fs = Math.Max(4, Math.Floor(fs * k));
                geo = Build(t, fs, out ink);
            }
            if (!small && (ink.Width > fitW || ink.Height > fitH))
            {
                fs = Math.Max(4, Math.Floor(fs * Math.Min(fitW / ink.Width, fitH / ink.Height)));
                geo = Build(t, fs, out ink);
            }

            double liftPct = small ? -0.28 : (LiftPercent + (kao ? 10 : 0)) / 100.0;
            double lift = -liftPct * (side ? W : H);

            var m = Matrix.CreateTranslation(-(ink.X + ink.Width / 2.0), lift - (ink.Y + ink.Height / 2.0));
            if (side) m *= Matrix.CreateRotation(Math.PI / 2);
            m *= Matrix.CreateTranslation(W / 2.0, H / 2.0);
            return (geo, ink, m, fs);
        }

        private static void Paint(DrawingContext dc, string t, bool small, bool flat)
        {
            var (geo, _, m, _) = Layout(t, small, flat);
            if (geo == null) return;
            using (dc.PushTransform(m))
                dc.DrawGeometry(PinkBrush, PinkPen, geo);
        }

        private static Geometry? Build(string t, double fs, out Rect ink)
        {
            var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FaceFont), Math.Max(1.0, fs), PinkBrush);
            var geo = ft.BuildGeometry(new Point(0, 0));
            var b = geo?.Bounds ?? default;
            ink = b.Width <= 0 || b.Height <= 0 ? new Rect(0, 0, 1, 1) : b;
            return geo;
        }
    }
}
