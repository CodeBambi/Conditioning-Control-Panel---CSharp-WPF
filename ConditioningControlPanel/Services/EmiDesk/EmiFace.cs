using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// EMI's face renderer: a direct WPF port of <c>Resources/web/arcademy/emi/face.js</c>.
///
/// The face is TEXT drawn on a virtual 152 px canvas (height = width * 0.903, the glass aspect
/// 41.68 : 37.63) in <c>#FF69B4</c>, fill + stroke, and the whole drawing is scaled to whatever
/// size the element is given. The web version upscales a 152 px bitmap with nearest-neighbour;
/// WPF has no such thing on a layered window, so we draw the glyph OUTLINE
/// (<see cref="FormattedText.BuildGeometry"/>) and let the vector pipeline keep it crisp at any
/// size. Every other number is verbatim from face.js and is LOCKED by EMI-DESIGN-LOCK.md:
/// 152 px wide, fill 95 % of the fit box, lift +2 % of box height (sideways faces too), stroke
/// thickness 5, kaomoji +10 % size / +10 % lift, THINKING dots 30 % size and -28 % lift.
///
/// Do not "improve" the fit maths. It fits against the real INK box (the geometry bounds), not
/// the advance width, because combining marks and fallback fonts lie about advance width, and it
/// pads by the stroke width because the stroke grows the glyph outside its ink box.
/// </summary>
public sealed class EmiFace : FrameworkElement
{
    /// <summary>Virtual canvas width in px. LOCKED.</summary>
    public const double VirtualWidth = 152.0;

    /// <summary>Glass aspect: height = width * 0.903 (41.68 : 37.63). LOCKED.</summary>
    public const double ScreenAspect = 0.903;

    /// <summary>Virtual canvas height in px (137). LOCKED.</summary>
    public static readonly double VirtualHeight = Math.Round(VirtualWidth * ScreenAspect);

    /// <summary>EMI pink. LOCKED.</summary>
    public static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);

    private const double FillFrac = 0.95;   // glyph fills 95% of the fit box
    private const double Thick = 5.0;       // stroke line width, same pink
    private const double LiftPercent = 2.0; // raise +2% of box height

    private static readonly SolidColorBrush PinkBrush;
    private static readonly Pen PinkPen;

    static EmiFace()
    {
        PinkBrush = new SolidColorBrush(Pink);
        PinkBrush.Freeze();
        PinkPen = new Pen(PinkBrush, Thick)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round
        };
        PinkPen.Freeze();
    }

    // ---------------------------------------------------------------- canon sets

    // Duplicated here (not read from EmiChains) so the renderer stays standalone: IsKao has to
    // answer for arbitrary caller text, not just chain frames. Same lists as face.js.
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

    // Short classic ASCII faces rotate 90 degrees; everything else stays flat.
    private static readonly Regex SideRe =
        new(@"^[>]?[:;=8xXB][-'^]?[)(DPOop|/\\3]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NonAsciiRe =
        new(@"[^\x00-\x7F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when the text is a short classic ASCII face that must be rotated 90 degrees.</summary>
    public static bool IsSide(string? t) =>
        !string.IsNullOrEmpty(t) && t!.Length <= 4 && SideRe.IsMatch(t);

    /// <summary>
    /// Anything outside the FLAT / SIDE / SPECIAL canon that carries exotic glyphs is a kaomoji:
    /// +10 % size and +10 % lift. The promoted round-eye faces live in the flat set, so they count
    /// as flat. Verbatim from face.js.
    /// </summary>
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

    // ---------------------------------------------------------------- the font

    private static FontFamily? _faceFont;
    private static bool _fontLogged;
    private static readonly object FontLock = new();

    /// <summary>
    /// The face's typeface. face.js uses the bundled <c>NotoSansMono-latin.woff2</c>; WPF cannot
    /// load woff2 (its font loader takes ttf / otf / ttc / compositefont only), so this prefers a
    /// real ttf/otf dropped beside the woff2 and otherwise falls back to a comma-separated family
    /// list. The exotic kaomoji glyphs come from the same Windows system faces face.js names in
    /// its FALLBACK chain, so the rendered face matches the web one glyph for glyph on a normal
    /// Windows install.
    /// </summary>
    public static FontFamily FaceFont
    {
        get
        {
            if (_faceFont != null) return _faceFont;
            lock (FontLock)
            {
                if (_faceFont != null) return _faceFont;
                _faceFont = ResolveFaceFont();
                return _faceFont;
            }
        }
    }

    private const string FallbackChain =
        "Noto Sans Mono, Cascadia Mono, Consolas, Noto Sans Symbols 2, Segoe UI Symbol, " +
        "Segoe UI Emoji, Nirmala UI, MS Gothic, Courier New, Global Monospace";

    /// <summary>
    /// The desk's OWN font folder, shipped as Content beside the exe: real ttf files, because WPF
    /// cannot load the campus woff2. Probed first, ahead of the web folder, so the desk keeps
    /// working when the arcademy assets are trimmed out of a build.
    /// </summary>
    private const string DeskFontDir = "Resources/emi/fonts";

    /// <summary>The campus font folder, kept as a second probe so a dropped ttf there still wins.</summary>
    private const string WebFontDir = "Resources/web/arcademy/emi/fonts";

    private static readonly string[] FontDirs = { DeskFontDir, WebFontDir };

    private static FontFamily ResolveFaceFont()
    {
        try
        {
            var hit = FindFontDir("NotoSansMono");
            if (hit != null)
            {
                var ff = new FontFamily(hit.Value.BaseUri, "./#Noto Sans Mono, " + FallbackChain);
                if (!_fontLogged)
                {
                    _fontLogged = true;
                    Log.Information("[EmiDesk] face font from {File}", hit.Value.File);
                }
                return ff;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] face font probe failed, using the system chain");
        }

        if (!_fontLogged)
        {
            _fontLogged = true;
            Log.Information("[EmiDesk] face font: system chain (no ttf/otf in the font folders)");
        }
        return new FontFamily(FallbackChain);
    }

    // ---------------------------------------------------------------- the pixel font

    private static FontFamily? _pixelFont;
    private static bool _pixelLogged;

    private const string PixelFallbackChain =
        "Press Start 2P, Noto Sans Mono, Cascadia Mono, Consolas, Courier New, Global Monospace";

    /// <summary>
    /// The pixel typeface for chrome that is NOT the face: the speech bubble, the offer chips, the
    /// dock pill. Press Start 2P from <c>Resources/emi/fonts</c>, with the same system fallback
    /// chain behind it so a trimmed build renders in a monospace rather than in nothing.
    ///
    /// Press Start 2P has ONE weight and an 8 x 8 cell: use it at whole pixel sizes (8 for the
    /// bubble, 7 for a chip) and never ask WPF to synthesise bold, or the cells land between
    /// device pixels and the whole strip smears.
    /// </summary>
    public static FontFamily PixelFont
    {
        get
        {
            if (_pixelFont != null) return _pixelFont;
            lock (FontLock)
            {
                if (_pixelFont != null) return _pixelFont;
                _pixelFont = ResolvePixelFont();
                return _pixelFont;
            }
        }
    }

    private static FontFamily ResolvePixelFont()
    {
        try
        {
            var hit = FindFontDir("PressStart2P");
            if (hit != null)
            {
                var ff = new FontFamily(hit.Value.BaseUri, "./#Press Start 2P, " + PixelFallbackChain);
                if (!_pixelLogged)
                {
                    _pixelLogged = true;
                    Log.Information("[EmiDesk] pixel font from {File}", hit.Value.File);
                }
                return ff;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] pixel font probe failed, using the system chain");
        }

        if (!_pixelLogged)
        {
            _pixelLogged = true;
            Log.Information("[EmiDesk] pixel font: system chain (no PressStart2P ttf shipped)");
        }
        return new FontFamily(PixelFallbackChain);
    }

    /// <summary>
    /// Find the first shipped font folder that carries a ttf/otf whose file name starts with
    /// <paramref name="prefix"/>. Returns the folder as a base URI (WPF wants a trailing
    /// separator) plus the file name, for the log line.
    /// </summary>
    private static (Uri BaseUri, string File)? FindFontDir(string prefix)
    {
        foreach (var rel in FontDirs)
        {
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                var file = Directory.EnumerateFiles(dir, "*.*")
                    .FirstOrDefault(f =>
                        (f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                         f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                        && Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (file == null) continue;
                var baseUri = new Uri(dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar);
                return (baseUri, Path.GetFileName(file));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] font folder probe failed for {Dir}", rel);
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- properties

    /// <summary>The kaomoji currently on the glass. Null or empty clears it.</summary>
    public static readonly DependencyProperty FaceProperty = DependencyProperty.Register(
        nameof(Face), typeof(string), typeof(EmiFace),
        new FrameworkPropertyMetadata("0_0", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The kaomoji currently on the glass. Null or empty clears it.</summary>
    public string? Face
    {
        get => (string?)GetValue(FaceProperty);
        set => SetValue(FaceProperty, value);
    }

    /// <summary>THINKING dots mode: fixed 30 % size, -28 % lift, no fit pass.</summary>
    public static readonly DependencyProperty SmallProperty = DependencyProperty.Register(
        nameof(Small), typeof(bool), typeof(EmiFace),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>THINKING dots mode: fixed 30 % size, -28 % lift, no fit pass.</summary>
    public bool Small
    {
        get => (bool)GetValue(SmallProperty);
        set => SetValue(SmallProperty, value);
    }

    /// <summary>Suppress the 90 degree rotation of classic sideways faces (chain-level `flat`).</summary>
    public static readonly DependencyProperty FlatProperty = DependencyProperty.Register(
        nameof(Flat), typeof(bool), typeof(EmiFace),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Suppress the 90 degree rotation of classic sideways faces (chain-level `flat`).</summary>
    public bool Flat
    {
        get => (bool)GetValue(FlatProperty);
        set => SetValue(FlatProperty, value);
    }

    // ---------------------------------------------------------------- api

    /// <summary>
    /// One frame, the way chains draw: text plus the frame's small/flat options. Equivalent to
    /// face.js <c>draw(text, frameOpts)</c>.
    /// </summary>
    public void Draw(string? text, bool small = false, bool flat = false)
    {
        Small = small;
        Flat = flat;
        Face = text;
    }

    /// <summary>Blank the glass.</summary>
    public void Clear() => Face = null;

    private DispatcherTimer? _blinkTimer;

    /// <summary>
    /// A single eye-blink on whatever face is up: 110 ms of <c>-_-</c> then back, the middle frame
    /// of the canon <c>blink</c> chain. Safe to call while a chain runs (the chain's next frame
    /// simply wins); a second call restarts the timer rather than stacking.
    /// </summary>
    public void Blink()
    {
        try
        {
            var restore = Face;
            if (string.IsNullOrEmpty(restore) || restore == "-_-") restore = "0_0";
            _blinkTimer?.Stop();
            Face = "-_-";
            _blinkTimer ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(110)
            };
            _blinkTimer.Tick -= OnBlinkTick;
            _blinkTimer.Tick += OnBlinkTick;
            _blinkRestore = restore;
            _blinkTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] Blink failed");
        }
    }

    private string? _blinkRestore;

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        try
        {
            _blinkTimer?.Stop();
            if (Face == "-_-") Face = _blinkRestore ?? "0_0";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] Blink tick failed");
        }
    }

    // ---------------------------------------------------------------- layout + render

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? VirtualWidth : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? VirtualHeight : availableSize.Height;
        return new Size(w, h);
    }

    private double PixelsPerDip
    {
        get
        {
            try { return VisualTreeHelper.GetDpi(this).PixelsPerDip; }
            catch { return 1.0; }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        try
        {
            double aw = ActualWidth, ah = ActualHeight;
            if (aw <= 0 || ah <= 0) return;

            var t = Face;
            if (string.IsNullOrEmpty(t)) return;

            // Everything below is in VIRTUAL space (152 x 137); one transform scales it to the
            // element. The clip is the canvas's implicit one: a face that somehow overruns the
            // bezel is cut, not smeared across the body art.
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, aw, ah)));
            dc.PushTransform(new ScaleTransform(aw / VirtualWidth, ah / VirtualHeight));
            try
            {
                Paint(dc, t!, Small, Flat);
            }
            finally
            {
                dc.Pop();   // scale
                dc.Pop();   // clip
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] face render failed");
        }
    }

    private void Paint(DrawingContext dc, string t, bool small, bool flat)
    {
        const double W = VirtualWidth;
        double H = VirtualHeight;

        bool side = !flat && IsSide(t);
        bool kao = IsKao(t);
        double fill = FillFrac * (kao ? 1.10 : 1.0);

        // Available box in TEXT space (pre-rotation): a sideways face is measured against the
        // swapped axes because the canvas is rotated under it.
        double boxW = (side ? H : W) * fill;
        double boxH = (side ? W : H) * fill;

        double ppd = PixelsPerDip;

        double fs = Math.Max(6, Math.Floor(boxH));
        if (small) fs = Math.Max(6, Math.Floor(boxH * 0.30));   // THINKING dots: fixed 30 %, no fit

        var geo = BuildGeometry(t, fs, ppd, out var ink);

        double pad = Thick;                       // the stroke grows the glyph past its ink box
        double fitW = boxW - pad * 2, fitH = boxH - pad * 2;
        double k = small
            ? 1.0
            : Math.Min(Math.Min(fitW / Math.Max(1, ink.Width), fitH / Math.Max(1, ink.Height)), 1.0);
        if (k < 1)
        {
            fs = Math.Max(4, Math.Floor(fs * k));
            geo = BuildGeometry(t, fs, ppd, out ink);
        }
        // One guard pass: glyph metrics are not perfectly linear in font size.
        if (!small && (ink.Width > fitW || ink.Height > fitH))
        {
            fs = Math.Max(4, Math.Floor(fs * Math.Min(fitW / ink.Width, fitH / ink.Height)));
            geo = BuildGeometry(t, fs, ppd, out ink);
        }

        // Vertical lift as a fraction of box height. Negative = down the glass. liftSide is true
        // in face.js DEFAULTS, so a sideways face gets the raise as well.
        double liftPct = small ? -0.28 : (LiftPercent + (kao ? 10 : 0)) / 100.0;
        double lift = -liftPct * (side ? W : H);

        // face.js centres the ACTUAL ink box (not the advance box) at (0, lift) after translating
        // to the canvas centre. Doing it as one translate on the geometry is the same arithmetic
        // with none of the baseline bookkeeping.
        geo.Transform = new TranslateTransform(
            -(ink.X + ink.Width / 2.0),
            lift - (ink.Y + ink.Height / 2.0));

        dc.PushTransform(new TranslateTransform(W / 2.0, H / 2.0));
        if (side) dc.PushTransform(new RotateTransform(90));
        try
        {
            dc.DrawGeometry(PinkBrush, Thick > 0 ? PinkPen : null, geo);
        }
        finally
        {
            if (side) dc.Pop();
            dc.Pop();
        }
    }

    private static Geometry BuildGeometry(string t, double fs, double pixelsPerDip, out Rect ink)
    {
        var ft = new FormattedText(
            t,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FaceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            Math.Max(1.0, fs),
            PinkBrush,
            pixelsPerDip <= 0 ? 1.0 : pixelsPerDip);

        var geo = ft.BuildGeometry(new Point(0, 0));
        var b = geo.Bounds;
        if (b.IsEmpty || b.Width <= 0 || b.Height <= 0)
        {
            // No ink (whitespace, or a font with nothing for this codepoint). Give the fit maths a
            // non-degenerate box so it cannot divide by zero; the draw is then a no-op anyway.
            b = new Rect(0, 0, 1, 1);
        }
        ink = b;
        return geo;
    }
}
