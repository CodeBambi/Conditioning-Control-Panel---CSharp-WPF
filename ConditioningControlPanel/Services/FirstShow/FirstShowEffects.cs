using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;

namespace ConditioningControlPanel.Services.FirstShow;

/// <summary>Private, disposable production effect layers. Never registers a session or changes settings.</summary>
internal sealed class FirstShowEffects : IDisposable
{
    private sealed record Media(List<BitmapSource> Frames, double Delay);
    private sealed class Actor
    {
        public int Id, MediaIndex = -1, Slot = -1;
        public double Born, Life, X, Y, Width, Height, Angle, Alpha, Scale = 1;
        public double HomeX, HomeY, GatherX, GatherY, NextTrail;
        public bool Gathering, Gone;
        public Media? Media;
        public FlashLayer? Flash;
        public FlashLayer.FlashItem? Picture;
        public BubbleLayer? Bubbles;
        public BubbleLayer.BubbleItem? Bubble;
        public string? Text;
    }
    private readonly CompositorEngine _engine = new();
    private readonly PinkTintLayer _pink;
    private readonly SpiralLayer _spiral;
    private readonly SubliminalLayer _subliminal;
    private readonly ChaosFxLayer _particles;
    private readonly List<Media> _media = new();
    private FirstShowDeck? _deck;
    private readonly List<Actor> _actors = new();
    private readonly Action<string> _cue;
    private readonly System.Drawing.Rectangle _screen;
    private readonly SKPaint _paint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
    private readonly SKPaint _textPaint = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
    private readonly SKTypeface _font = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
    private SKImage? _bubbleSprite, _blurFrame;
    private BrainDrainCapturePump? _capture;
    private double _time, _nextFlash = 2, _nextBubble = 19, _nextSub = 15;
    private int _serial, _drainPhase;
    private bool _disposed, _wordsSpawned, _revealed;
    public int Width => _screen.Width;
    public int Height => _screen.Height;
    public int LoadedCount => _media.Count;

    public FirstShowEffects(Action<string> cue)
    {
        _cue = cue;
        _screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        // These draw lists are stepped by the show host, never by a second compositor host.
        _engine.Dispose();
        _pink = new(_engine); _spiral = new(_engine); _subliminal = new(_engine); _particles = new(_engine);
        try
        {
            var bitmap = new BitmapImage(new Uri("pack://application:,,,/Resources/bubble.png"));
            bitmap.Freeze();
            _bubbleSprite = SkiaWpfInterop.ToSKImage(bitmap);
        }
        catch (Exception ex) { App.Logger?.Debug("FirstShow bubble sprite: {Error}", ex.Message); }
    }

    public async Task LoadAsync(IReadOnlyList<string> paths)
    {
        var loaded = await Task.Run(() => paths.Take(FirstShowMediaLoader.TargetPictures).Select(Decode).Where(m => m != null).Cast<Media>().ToList());
        if (_disposed) return;
        if (loaded.Count == 0) throw new InvalidOperationException("No preview images could be decoded.");
        _media.Clear(); _media.AddRange(loaded); _deck = new FirstShowDeck(_media.Count);
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/spiral.gif"))?.Stream;
            if (stream != null)
            {
                using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                var buffer = bytes.ToArray();
                var decoded = await Task.Run(() => { using var source = new MemoryStream(buffer); return AnimatedWebp.DecodeFrames(source, 512, 45, 16); });
                if (!_disposed && decoded.HasValue) _spiral.ShowFrames(decoded.Value.Frames, decoded.Value.FrameDelay, 0);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("FirstShow spiral: {Error}", ex.Message); }
    }

    private static Media? Decode(string path)
    {
        try
        {
            var animated = AnimatedWebp.DecodeFrames(path, 420, 24, 8);
            if (animated.HasValue) return new(animated.Value.Frames, Math.Max(.025, animated.Value.FrameDelay.TotalSeconds));
            return AnimatedWebp.RunGatedDecode(() =>
            {
                using var bitmap = SKBitmap.Decode(path);
                if (bitmap == null) return null;
                int edge = Math.Max(bitmap.Width, bitmap.Height);
                using var resized = bitmap.Resize(new SKImageInfo(Math.Max(1, bitmap.Width * 600 / Math.Max(600, edge)), Math.Max(1, bitmap.Height * 600 / Math.Max(600, edge))), SKFilterQuality.Medium);
                var source = resized ?? bitmap;
                using var image = SKImage.FromBitmap(source);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = data.AsStream();
                var frame = new BitmapImage(); frame.BeginInit(); frame.CacheOption = BitmapCacheOption.OnLoad;
                frame.StreamSource = stream; frame.EndInit(); frame.Freeze();
                return new Media(new List<BitmapSource> { frame }, .1);
            });
        }
        catch (Exception ex) { App.Logger?.Debug("FirstShow media decode: {Error}", ex.Message); return null; }
    }

    public void Tick(double seconds, double delta)
    {
        if (_disposed) return;
        _time = seconds;
        var dt = TimeSpan.FromSeconds(Math.Clamp(delta, 0, .1));
        bool moving = MotionFx.Level != MotionLevel.Off;
        double fade = Math.Clamp((seconds - 7) / .8, 0, 1) * Math.Clamp((32 - seconds) / 3, 0, 1);
        _pink.Set(255, 65, 165, .13 * fade);
        _spiral.SetOpacity(.16 * fade);
        if (moving) _spiral.Update(dt);
        _subliminal.Update(dt); _particles.Update(dt);
        int drain = seconds >= 11 && seconds < 13 ? 1 : seconds >= 13 && seconds < 15 ? 2 : 0;
        if (drain != _drainPhase)
        {
            _capture?.Shutdown(); _capture = null; _blurFrame?.Dispose(); _blurFrame = null;
            _drainPhase = drain;
            if (drain != 0)
                _capture = new BrainDrainCapturePump(4, TimeSpan.FromMilliseconds(40), drain == 2 && moving, new[] { _screen }, 70 * .14f / 4 / 3, (1 + 70 * .045f));
        }
        if (seconds >= _nextBubble && seconds < 28 && _media.Count > 0)
        {
            Spawn(true, seconds); _nextBubble = seconds + .71;
        }
        if (seconds >= _nextFlash && seconds < 28 && _media.Count > 0)
        {
            Spawn(false, seconds);
            _nextFlash = seconds + (seconds < 4.5 ? .39 : .71);
        }
        if (seconds >= 15 && !_wordsSpawned)
        {
            _wordsSpawned = true;
            SpawnWord(Loc.Get("first_show_word1"), 0); SpawnWord(Loc.Get("first_show_word2"), 1);
        }
        if (seconds >= _nextSub && seconds < 27)
        {
            _subliminal.Flash(new[] { new SubliminalLayer.Placement(new SKRectI(0, 0, Width, Height), Math.Min(1f, Width / 1500f)) },
                Loc.Get("first_show_word1"), SKColors.Transparent, SKColors.White, new SKColor(245, 50, 175), true, .7, 100, 70, 180);
            _nextSub = seconds + 3.1;
        }
        foreach (var actor in _actors)
        {
            if (actor.Gone) continue;
            Animate(actor, seconds, moving);
            actor.Flash?.Update(dt);
        }
        foreach (var actor in _actors.Where(a => a.Gone).ToArray())
        {
            actor.Flash?.Clear(); actor.Bubbles?.Clear(); _actors.Remove(actor);
        }
        if (seconds >= 32 && !_revealed)
        {
            _revealed = true; _subliminal.Clear();
            if (MotionFx.AllowParticles)
            {
                for (int i = 0; i < 6; i++) _particles.EmitBurst(new Point(Width * .5, Height * .56), i % 2 == 0 ? SKColors.HotPink : SKColors.Gold, 1.1f + i * .18f);
                _particles.EmitRipple(new Point(Width * .5, Height * .56), Width * .25, 900, true);
            }
            _cue("reveal");
        }
    }

    private void Spawn(bool bubble, double born)
    {
        var visible = _actors.Where(a => a.MediaIndex >= 0 && !a.Gone && born-a.Born < a.Life)
            .Select(a => (a.MediaIndex,a.Slot)).ToArray();
        if (bubble && _actors.Count(a => a.Bubble != null && !a.Gone && born-a.Born < a.Life) >= 3) return;
        var pick = _deck?.Next(visible);
        if (pick == null) return;
        int id = _serial++, mediaIndex = pick.Value.Media, slot = pick.Value.Slot;
        var media = _media[mediaIndex];
        double[] xs = { .15,.82,.25,.74,.13,.86,.42,.60 };
        double[] ys = { .25,.66,.76,.23,.51,.42,.20,.79 };
        double jitterX = (Random.Shared.NextDouble()-.5)*.05;
        double jitterY = (Random.Shared.NextDouble()-.5)*.04;
        double w = Math.Min(Width * .19, 300), h = w * media.Frames[0].PixelHeight / media.Frames[0].PixelWidth;
        if (h > Height * .30) { w *= Height * .30 / h; h = Height * .30; }
        var a = new Actor { Id = id, MediaIndex = mediaIndex, Slot = slot, Born = born, Life = bubble ? 9 : 4.1 + id % 3 * .27, Media = media,
            HomeX = Width * (xs[slot]+jitterX), HomeY = Height * (ys[slot]+jitterY), Width = w, Height = h };
        if (bubble)
        {
            a.Width = a.Height = Math.Min(Width * .095, 155);
            a.Bubbles = new BubbleLayer(_engine);
            a.Bubble = a.Bubbles.Add(new BubbleLayer.BubbleItem { DpiScale = 1, SizeDip = (float)a.Width,
                HitSizeDip = (float)a.Width, Sprite = _bubbleSprite, HasFace = true, FaceSource = media.Frames[0],
                TeaseInnerDip = (float)a.Width * .73f, HasShine = true, HasGlow = true,
                GlowColor = SKColors.HotPink, GlowBlurDip = 12, GlowOpacity = .45f });
        }
        else
        {
            a.Flash = new FlashLayer(_engine);
            a.Picture = a.Flash.Spawn(media.Frames.Select(SkiaWpfInterop.ToSKImage).ToArray(), 0, 0, (float)w, (float)h, 4, 14, SKColors.HotPink, 6, .65, false);
        }
        _actors.Add(a);
        if (MotionFx.AllowParticles) _particles.EmitBurst(new Point(a.HomeX, a.HomeY), bubble ? SKColors.Cyan : SKColors.HotPink, .7f);
        _cue(bubble ? "bubbles" : "flash");
    }

    private void SpawnWord(string text, int slot)
    {
        _actors.Add(new Actor { Id = _serial++, Born = 15 + slot * .6, Life = 30, Text = text,
            Width = Width * .24, Height = 70, HomeX = Width * (.25 + slot * .5), HomeY = Height * (.23 + slot * .5) });
    }

    private void Animate(Actor a, double time, bool moving)
    {
        double age = time - a.Born;
        if (age < 0) { a.Alpha = 0; return; }
        double motion = MotionFx.Level == MotionLevel.Full ? 1 : moving ? .4 : 0;
        a.X = a.HomeX + Math.Sin(age * (.9 + a.Id % 4 * .19) + a.Id) * Width * .023 * motion;
        a.Y = a.HomeY + Math.Cos(age * (1.1 + a.Id % 3 * .17) + a.Id * .7) * Height * .025 * motion;
        if (a.Text != null && moving)
        {
            a.X = Width * (.15 + Triangle(age * (.085 + a.Id % 3 * .018) + a.Id * .37) * .70);
            a.Y = Height * (.12 + Triangle(age * (.11 + a.Id % 2 * .03) + a.Id * .29) * .66);
        }
        a.Angle = Math.Sin(age * (2.1 + a.Id % 4 * .27) + a.Id) * 5 * motion;
        a.Alpha = Math.Clamp(age / .3, 0, 1);
        a.Scale = moving ? 1 + Math.Sin(Math.Min(1, age / .45) * Math.PI) * .12 : 1;
        if (time < 28)
        {
            a.Alpha *= Math.Clamp((a.Life - age) / .7, 0, 1);
            if (age >= a.Life) a.Gone = true;
        }
        else
        {
            if (!a.Gathering) { a.Gathering = true; a.GatherX = a.X; a.GatherY = a.Y; }
            double delay = Fraction(a.Id * .618034) * 1.05;
            double duration = 1.7 + Fraction(a.Id * .414214) * .9;
            double p = Math.Clamp((time - 28 - delay) / duration, 0, 1);
            double remain = Math.Pow(1 - p, 1.22);
            double dx = a.GatherX - Width * .5, dy = a.GatherY - Height * .56;
            double turn = p * (4.8 + Fraction(a.Id * .732051) * 5.2) * motion;
            a.X = Width * .5 + (dx * Math.Cos(turn) - dy * Math.Sin(turn)) * remain;
            a.Y = Height * .56 + (dx * Math.Sin(turn) + dy * Math.Cos(turn)) * remain;
            a.X += Math.Sin(time * (13 + a.Id % 5) + a.Id) * 10 * remain * motion;
            a.Y += Math.Cos(time * (11 + a.Id % 7) + a.Id) * 8 * remain * motion;
            a.Angle += p * (a.Id % 2 == 0 ? 140 : -190) * motion;
            a.Scale = moving ? Math.Max(.01, 1 - Math.Pow(p, 1.5)) : 1; a.Alpha *= 1 - Math.Pow(p, 5);
            if (!moving) { a.X = a.GatherX; a.Y = a.GatherY; }
            if (p >= 1) { a.Gone = true; if (MotionFx.AllowParticles) _particles.EmitBurst(new Point(a.X, a.Y), SKColors.Gold, .45f); }
        }
        if (MotionFx.AllowParticles && time >= a.NextTrail && !a.Gone && (time >= 28 || a.Bubble != null))
        {
            _particles.EmitTrail(new Point(a.X, a.Y), time >= 28 ? .52 : .32, a.Id % 3 == 0, Width * .5 - a.X, Height * .56 - a.Y);
            a.NextTrail = time + (time >= 28 ? .045 : .16);
        }
        int frame = a.Media == null || !moving ? 0 : (int)(age / a.Media.Delay) % a.Media.Frames.Count;
        if (a.Picture != null)
        {
            a.Picture.X = (float)(a.X - a.Width / 2); a.Picture.Y = (float)(a.Y - a.Height / 2);
            a.Picture.Opacity = a.Alpha; a.Picture.FrameIndex = frame;
        }
        if (a.Bubble != null)
        {
            a.Bubble.CenterXPx = a.X; a.Bubble.CenterYPx = a.Y; a.Bubble.Opacity = (float)a.Alpha;
            a.Bubble.FaceSource = a.Media!.Frames[frame]; a.Bubble.TeaseShineOpacity = .28f;
        }
    }

    public void Render(SKCanvas canvas, int width, int height)
    {
        if (_disposed) return;
        canvas.Save(); canvas.Scale(width / (float)Width, height / (float)Height);
        var bounds = new SKRectI(0, 0, Width, Height); var elapsed = TimeSpan.FromSeconds(_time);
        if (_capture != null)
        {
            if (_capture.TryTakeFrame(_screen.X + Width / 2, _screen.Y + Height / 2, out var fresh, out _) && fresh != null)
            { _blurFrame?.Dispose(); _blurFrame = fresh; }
            if (_blurFrame != null)
            {
                double edge = Math.Min(_time - (_drainPhase == 1 ? 11 : 13), (_drainPhase == 1 ? 13 : 15) - _time);
                _paint.Color = SKColors.White.WithAlpha((byte)(BrainDrainLayer.AlphaFor(70) * Math.Clamp(edge / .3, 0, 1)));
                canvas.DrawImage(_blurFrame, new SKRect(0, 0, Width, Height), _paint);
            }
        }
        if (_time >= 7 && _time < 32) { _pink.Render(canvas, bounds, 1, elapsed); _spiral.Render(canvas, bounds, 1, elapsed); }
        foreach (var a in _actors)
        {
            if (a.Gone || a.Alpha < .002) continue;
            canvas.Save(); canvas.Translate((float)a.X, (float)a.Y); canvas.RotateDegrees((float)a.Angle);
            canvas.Scale((float)a.Scale); canvas.Translate(-(float)a.X, -(float)a.Y);
            a.Flash?.Render(canvas, bounds, 1, elapsed); a.Bubbles?.Render(canvas, bounds, 1, elapsed);
            if (a.Text != null)
            {
                _textPaint.Typeface = _font; _textPaint.TextSize = Math.Min(56, Width * .035f);
                _textPaint.Style = SKPaintStyle.Stroke; _textPaint.StrokeWidth = 6;
                _textPaint.Color = SKColors.HotPink.WithAlpha((byte)(a.Alpha * 220));
                canvas.DrawText(a.Text, (float)a.X, (float)a.Y, _textPaint);
                _textPaint.Style = SKPaintStyle.Fill; _textPaint.Color = SKColors.White.WithAlpha((byte)(a.Alpha * 255));
                canvas.DrawText(a.Text, (float)a.X, (float)a.Y, _textPaint);
            }
            canvas.Restore();
        }
        _subliminal.Render(canvas, bounds, 1, elapsed); _particles.Render(canvas, bounds, 1, elapsed);
        canvas.Restore();
    }

    public void Pop(double x, double y)
    {
        if (_disposed || _time >= 28) return;
        var a = _actors.LastOrDefault(a => a.Bubble != null && !a.Gone && Math.Pow(a.X - x, 2) + Math.Pow(a.Y - y, 2) < Math.Pow(a.Width * .52, 2));
        if (a == null) return;
        a.Gone = true;
        if (MotionFx.AllowParticles)
        {
            _particles.EmitBurst(new Point(a.X, a.Y), SKColors.HotPink, 1.3f);
            _particles.EmitRipple(new Point(a.X, a.Y), a.Width, 500, true);
        }
        _cue("pop");
    }

    private static double Fraction(double value) => value - Math.Floor(value);
    private static double Triangle(double value) => 1 - Math.Abs(Fraction(value) * 2 - 1);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _capture?.Shutdown(); _capture = null; _blurFrame?.Dispose(); _blurFrame = null;
        foreach (var a in _actors) { a.Flash?.Clear(); a.Bubbles?.Clear(); }
        _actors.Clear(); _media.Clear(); _spiral.Hide(); _spiral.OnDeactivated();
        _pink.Hide(); _subliminal.Clear(); _particles.Clear(); _bubbleSprite?.Dispose();
        _paint.Dispose(); _textPaint.Dispose(); _font.Dispose(); _engine.Dispose();
    }
}
