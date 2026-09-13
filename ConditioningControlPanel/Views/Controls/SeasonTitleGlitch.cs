using System;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls;

/// <summary>
/// The Quests header's one-shot "the season is dead" glitch: the old title ("Airhead August")
/// tears apart through RGB split, sliced bands, scrambled glyphs and a couple of dead season
/// names, then snaps clean into the permanent title.
///
/// <para><b>How it draws.</b> On first use the title TextBlock is lifted out of its Border into a
/// Grid alongside two colour ghosts (pink, cyan) and a stack of band copies, each clipped to a
/// horizontal strip. During glitch frames the real title is hidden and the bands carry the text,
/// so shifting a band sideways tears that strip of the word. Everything shares one cell and one
/// font, and the Grid's height is pinned to the font's natural line height, so nothing below the
/// header reflows while the string length morphs. The noise alphabet is plain ASCII for the same
/// reason: a fallback-font glyph could grow the line.</para>
///
/// <para><b>Timing.</b> Every frame is a pure function of elapsed time (<see cref="RenderFrame"/>)
/// quantized to 30 steps per second, which gives the stepped digital look and lets a test render
/// any instant offscreen.</para>
/// </summary>
internal sealed class SeasonTitleGlitch
{
    internal const double DurationSeconds = 1.6;
    internal const string DeadTitle = "Airhead August";

    private const int BandCount = 6;
    private const string Noise = "!<>-_/\\[]{}=+*^?#%&@$01XZ|~";

    private static readonly Brush PinkGhost = Frozen(Color.FromRgb(0xFF, 0x14, 0x93));
    private static readonly Brush CyanGhost = Frozen(Color.FromRgb(0x00, 0xE5, 0xFF));
    private static readonly Brush HotWhite = Frozen(Color.FromRgb(0xFF, 0xF0, 0xFA));

    private readonly TextBlock _title;
    private readonly Stopwatch _clock = new();
    private readonly TextBlock[] _bands = new TextBlock[BandCount];
    private readonly RectangleGeometry[] _bandClips = new RectangleGeometry[BandCount];
    private Grid? _stage;
    private TextBlock? _ghostPink, _ghostCyan;
    private Grid? _bandHost;
    private DispatcherTimer? _timer;
    private Action? _onDone;
    private string _from = DeadTitle, _to = DeadTitle;
    private uint _seed = 0x5EED;

    internal SeasonTitleGlitch(TextBlock title) => _title = title;

    internal bool IsPlaying => _timer != null;

    /// <summary>Plays from <paramref name="from"/> into <paramref name="to"/>; <paramref name="onDone"/> fires only on a natural finish.</summary>
    internal void Play(string from, string to, Action? onDone)
    {
        Cancel();
        _from = from;
        _to = to;
        _onDone = onDone;
        if (!EnsureStage())
        {
            _title.Text = to;
            onDone?.Invoke();
            return;
        }
        _seed = (uint)Environment.TickCount;
        RenderAt(0);
        _clock.Restart();
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Stops a running glitch and leaves the final title clean. Does not fire onDone.</summary>
    internal void Stop()
    {
        if (!IsPlaying) return;
        Cancel();
        RenderClean(_to);
    }

    /// <summary>Renders one instant of the glitch with a fixed seed; for offscreen previews and tests.</summary>
    internal void RenderFrame(string from, string to, double seconds)
    {
        _from = from;
        _to = to;
        _seed = 0x5EED;
        if (EnsureStage()) RenderAt(seconds);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current?.Dispatcher is not { HasShutdownStarted: false })
            {
                Cancel();
                return;
            }
            var t = _clock.Elapsed.TotalSeconds;
            if (t < DurationSeconds)
            {
                RenderAt(t);
                return;
            }
            var done = _onDone;
            Cancel();
            RenderClean(_to);
            done?.Invoke();
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "season title glitch tick");
            Cancel();
            try { RenderClean(_to); } catch (Exception inner) { Diag.Swallowed(inner, "season title glitch reset"); }
        }
    }

    private void Cancel()
    {
        _onDone = null;
        if (_timer == null) return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
        _clock.Reset();
    }

    private bool EnsureStage()
    {
        if (_stage != null) return true;
        if (_title.Parent is not Border border || !ReferenceEquals(border.Child, _title)) return false;

        border.Child = null;
        // Pinned to the font's natural line height: the header can never grow or shrink mid-glitch.
        _stage = new Grid
        {
            Height = _title.ActualHeight > 0 ? _title.ActualHeight : _title.FontFamily.LineSpacing * _title.FontSize
        };
        _ghostCyan = Layer(CyanGhost);
        _ghostPink = Layer(PinkGhost);
        _bandHost = new Grid
        {
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x69, 0xB4), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.7 }
        };
        for (int i = 0; i < BandCount; i++)
        {
            _bandClips[i] = new RectangleGeometry();
            _bands[i] = Layer(_title.Foreground);
            _bands[i].Clip = _bandClips[i];
            _bandHost.Children.Add(_bands[i]);
        }
        _stage.Children.Add(_ghostCyan);
        _stage.Children.Add(_ghostPink);
        _stage.Children.Add(_title);
        _stage.Children.Add(_bandHost);
        border.Child = _stage;
        return true;
    }

    private TextBlock Layer(Brush foreground) => new()
    {
        FontFamily = _title.FontFamily,
        FontSize = _title.FontSize,
        FontWeight = _title.FontWeight,
        FontStyle = _title.FontStyle,
        HorizontalAlignment = _title.HorizontalAlignment,
        VerticalAlignment = _title.VerticalAlignment,
        TextAlignment = _title.TextAlignment,
        Foreground = foreground,
        IsHitTestVisible = false,
        Opacity = 0,
        RenderTransform = new TranslateTransform()
    };

    private void RenderClean(string text)
    {
        _title.Text = text;
        _title.Opacity = 1;
        if (_stage == null) return;
        _ghostPink!.Opacity = 0;
        _ghostCyan!.Opacity = 0;
        _bandHost!.Opacity = 0;
    }

    // ---- the timeline -----------------------------------------------------------------------
    //
    //   0.00-0.30  "Airhead August" holds, one 2-frame twitch at 0.20
    //   0.30-0.55  onset: RGB split opens, bands start slipping, letters begin to rot
    //   0.55-0.60  hard flicker: blank
    //   0.60-0.95  full glitch: scrambled old title, "Sissygasm Sep", pure noise, "Juicy J", noise
    //   0.95-0.98  hard flicker: blank
    //   0.98-1.02  white-hot frame, the new title fully scrambled
    //   1.02-1.38  decode: "Deeper Every Month" locks in left to right while the tearing calms
    //   1.38-1.42  one last slice kick
    //   1.42-1.60  clean final title, then the shimmer takes over
    private void RenderAt(double t)
    {
        int frame = (int)(t * 30);
        string text;
        double intensity;
        bool blank = false, white = false;

        if (t < 0.30)
        {
            text = _from;
            intensity = t >= 0.20 && t < 0.27 ? 0.25 : 0;
        }
        else if (t < 0.55)
        {
            double p = (t - 0.30) / 0.25;
            intensity = 0.35 + 0.35 * p;
            text = Morph(Scramble(_from, 0.12 + 0.35 * p, frame, 1), frame, p);
        }
        else if (t < 0.60) { text = _from; intensity = 0; blank = true; }
        else if (t < 0.95)
        {
            // The dead names tear a little less than the noise around them, so they can be read.
            bool deadName = t is >= 0.66 and < 0.76 or >= 0.84 and < 0.91;
            intensity = deadName ? 0.55 : 1;
            text = t switch
            {
                < 0.66 => Scramble(_from, 0.6, frame, 2),
                < 0.76 => Scramble("Sissygasm Sep", 0.08, frame, 3),
                < 0.84 => NoiseOfLength(13 + (int)((t - 0.76) / 0.08 * 5), frame),
                < 0.91 => Scramble("Juicy J", 0.06, frame, 4),
                _ => NoiseOfLength(_to.Length, frame)
            };
        }
        else if (t < 0.98) { text = _to; intensity = 0; blank = true; }
        else if (t < 1.02) { text = Scramble(_to, 1, frame, 5); intensity = 1; white = true; }
        else if (t < 1.38)
        {
            double p = (t - 1.02) / 0.36;
            intensity = 0.8 * (1 - p) + 0.1;
            text = Decode(_to, 1 - Math.Pow(1 - p, 2), frame);
        }
        else if (t < 1.42) { text = _to; intensity = 0.6; }
        else { text = _to; intensity = 0; }

        if (blank)
        {
            _title.Text = text;
            _title.Opacity = 0;
            _ghostPink!.Opacity = 0;
            _ghostCyan!.Opacity = 0;
            _bandHost!.Opacity = 0;
            return;
        }
        if (intensity <= 0)
        {
            RenderClean(text);
            return;
        }

        _title.Text = text;
        _title.Opacity = 0;
        _bandHost!.Opacity = 1;

        double split = 2 + 8 * intensity * Rnd(frame, 10) + (white ? 6 : 0);
        Ghost(_ghostPink!, text, -split, (Rnd(frame, 11) - 0.5) * 3 * intensity, 0.55 + 0.35 * intensity);
        Ghost(_ghostCyan!, text, split * (0.7 + 0.6 * Rnd(frame, 12)), (Rnd(frame, 13) - 0.5) * 3 * intensity, 0.5 + 0.35 * intensity);

        // Random cut points down the line, sorted, so the bands tile the full height every frame.
        double height = _stage!.Height;
        Span<double> cuts = stackalloc double[BandCount + 1];
        cuts[0] = -height;
        cuts[BandCount] = height * 2;
        for (int i = 1; i < BandCount; i++) cuts[i] = height * (i + (Rnd(frame, 20 + i) - 0.5) * 0.9) / BandCount;
        cuts.Slice(1, BandCount - 1).Sort();

        int tinted = Rnd(frame, 30) < intensity * 0.35 ? (int)(Rnd(frame, 31) * BandCount) : -1;
        for (int i = 0; i < BandCount; i++)
        {
            var band = _bands[i];
            band.Text = text;
            band.Opacity = 1;
            _bandClips[i].Rect = new Rect(-4000, cuts[i], 8000, Math.Max(0, cuts[i + 1] - cuts[i]));
            double shift = Rnd(frame, 40 + i) < 0.3 + 0.55 * intensity
                ? (Rnd(frame, 50 + i) - 0.5) * 2 * (4 + 26 * intensity)
                : 0;
            ((TranslateTransform)band.RenderTransform).X = shift;
            band.Foreground = white ? HotWhite : i == tinted ? (Rnd(frame, 32) < 0.5 ? CyanGhost : HotWhite) : _title.Foreground;
        }
    }

    private static void Ghost(TextBlock ghost, string text, double dx, double dy, double opacity)
    {
        ghost.Text = text;
        ghost.Opacity = opacity;
        var move = (TranslateTransform)ghost.RenderTransform;
        move.X = dx;
        move.Y = dy;
    }

    private string Scramble(string source, double fraction, int frame, int salt)
    {
        var sb = new StringBuilder(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            sb.Append(c != ' ' && Rnd(frame, salt * 100 + i) < fraction ? NoiseChar(frame, salt * 100 + i + 57) : c);
        }
        return sb.ToString();
    }

    /// <summary>The string length breathes during onset: a character or two dropped or tacked on.</summary>
    private string Morph(string text, int frame, double progress)
    {
        double roll = Rnd(frame, 60);
        if (roll < 0.25 * progress && text.Length > 4) return text.Remove((int)(Rnd(frame, 61) * text.Length), 1);
        if (roll > 1 - 0.3 * progress) return text + NoiseChar(frame, 62) + (Rnd(frame, 63) < 0.5 ? NoiseChar(frame, 64).ToString() : "");
        return text;
    }

    private string NoiseOfLength(int length, int frame)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) sb.Append(Rnd(frame, 70 + i) < 0.12 ? ' ' : NoiseChar(frame, 90 + i));
        return sb.ToString();
    }

    private string Decode(string target, double locked, int frame)
    {
        int lockedCount = (int)(target.Length * locked);
        var sb = new StringBuilder(target.Length);
        for (int i = 0; i < target.Length; i++)
        {
            char c = target[i];
            sb.Append(c == ' ' || i < lockedCount ? c : NoiseChar(frame, 200 + i));
        }
        return sb.ToString();
    }

    private char NoiseChar(int frame, int salt) => Noise[(int)(Rnd(frame, salt) * Noise.Length)];

    private double Rnd(int frame, int salt)
    {
        unchecked
        {
            uint h = (uint)frame * 73856093u ^ (uint)salt * 19349663u ^ _seed;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            h *= 0x27d4eb2du;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216.0;
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
