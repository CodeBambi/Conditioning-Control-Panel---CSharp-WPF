using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowEffects.cs (WPF 7.1.5): the private,
    /// disposable effect layers of the welcome show. Never registers a session, never reads or writes a
    /// setting beyond the motion level.
    ///
    /// <para>The timeline, the slots, the gather spiral and every number in <see cref="Tick"/> and
    /// <see cref="Animate"/> are WPF's. What differs is the paint: WPF stepped the production compositor
    /// layers (FlashLayer, BubbleLayer, PinkTintLayer, SpiralLayer, SubliminalLayer, ChaosFxLayer) by
    /// hand; none of those exist on this head, so each is drawn here directly in Skia on the stage's
    /// <c>FxSurface</c>. Brain drain: WPF blurred a live capture of the desktop (BrainDrainCapturePump);
    /// this head has no capture pump, so the 11-15 s beat is a soft milky veil with the same edges.</para>
    /// </summary>
    internal sealed class FirstShowEffects : IDisposable
    {
        private sealed record Media(List<SKImage> Frames, double Delay);
        private sealed class Actor
        {
            public int Id, MediaIndex = -1, Slot = -1;
            public double Born, Life, X, Y, Width, Height, Angle, Alpha, Scale = 1;
            public double HomeX, HomeY, GatherX, GatherY, NextTrail;
            public bool Gathering, Gone, Bubble;
            public Media? Media;
            public int Frame;
            public string? Text;
        }
        private struct Spark { public double X, Y, Vx, Vy, Born, Life, Size; public SKColor Color; public bool Ring; public double Radius; }

        private readonly List<Media> _media = new();
        private readonly List<Actor> _actors = new();
        private readonly List<Spark> _sparks = new();
        private readonly Action<string> _cue;
        private readonly SKPaint _paint = new() { IsAntialias = true };
        private readonly SKPaint _textPaint = new() { IsAntialias = true };
        private readonly SKFont _font;
        private readonly SKTypeface _face = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
        private readonly Random _random = new();
        private FirstShowDeck? _deck;
        private Media? _spiral;
        private SKImage? _bubbleSprite;
        private double _time, _nextFlash = 2, _nextBubble = 19, _nextSub = 15, _subAt = -10;
        private int _serial;
        private bool _disposed, _wordsSpawned, _revealed;

        /// <summary>The stage in DIPs; every position below is in this space.</summary>
        public double Width { get; }
        public double Height { get; }
        public int LoadedCount => _media.Count;
        public double Time => _time;

        /// <summary>Test seams: the motion level and the two show words (Loc on the desk).</summary>
        internal Func<MotionLevel> Motion = () => global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level;
        internal Func<bool> Particles = () => global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowParticles;
        internal Func<string, string> Words = key => ConditioningControlPanel.Localization.Loc.Get(key);

        public FirstShowEffects(Action<string> cue, double width, double height)
        {
            _cue = cue; Width = Math.Max(1, width); Height = Math.Max(1, height);
            _font = new SKFont(_face, 56);
            try
            {
                using var stream = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/bubble.png"));
                using var data = SKData.Create(stream);
                _bubbleSprite = SKImage.FromEncodedData(data);
            }
            catch (Exception ex) { Log.Debug("FirstShow bubble sprite: {Error}", ex.Message); }
        }

        public async Task LoadAsync(IReadOnlyList<string> paths)
        {
            var loaded = await Task.Run(() => paths.Take(FirstShowMediaLoader.TargetPictures)
                .Select(p => Decode(p, 420, 24, 600)).Where(m => m != null).Cast<Media>().ToList());
            if (_disposed) { foreach (var m in loaded) Free(m); return; }
            if (loaded.Count == 0) throw new InvalidOperationException("No preview images could be decoded.");
            foreach (var m in _media) Free(m);
            _media.Clear(); _media.AddRange(loaded); _deck = new FirstShowDeck(_media.Count);
            try
            {
                var path = ConditioningControlPanel.Services.ContentLocator.Resolve(Path.Combine("Resources", "spiral.gif"));
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var spiral = await Task.Run(() => Decode(path, 512, 45, 512));
                    if (_disposed) { if (spiral != null) Free(spiral); }
                    else _spiral = spiral;
                }
            }
            catch (Exception ex) { Log.Debug("FirstShow spiral: {Error}", ex.Message); }
        }

        private static void Free(Media m) { foreach (var f in m.Frames) f.Dispose(); m.Frames.Clear(); }

        /// <summary>Stills are capped at <paramref name="stillEdge"/>; animations at <paramref name="edge"/> and <paramref name="maxFrames"/> frames.</summary>
        private static Media? Decode(string path, int edge, int maxFrames, int stillEdge)
        {
            try
            {
                using var codec = SKCodec.Create(path);
                if (codec == null) return null;
                int count = Math.Max(1, codec.FrameCount);
                bool animated = count > 1;
                int cap = animated ? edge : stillEdge;
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                int longest = Math.Max(info.Width, info.Height);
                int w = Math.Max(1, info.Width * cap / Math.Max(cap, longest)), h = Math.Max(1, info.Height * cap / Math.Max(cap, longest));
                var frames = new List<SKImage>();
                double delay = .1;
                using var full = new SKBitmap(info);
                int step = Math.Max(1, (int)Math.Ceiling(count / (double)maxFrames));
                for (int i = 0; i < count; i++)
                {
                    // Every frame is decoded (a frame can need the one before it); only every step-th is kept.
                    var options = animated ? new SKCodecOptions(i, i > 0 ? i - 1 : -1) : SKCodecOptions.Default;
                    var result = codec.GetPixels(info, full.GetPixels(), options);
                    if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) break;
                    if (i % step != 0) continue;
                    using var small = full.Resize(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear));
                    var image = SKImage.FromBitmap(small ?? full);
                    if (image != null) frames.Add(image);
                    if (animated && i == 0 && codec.FrameInfo.Length > 0)
                        delay = Math.Max(.025, codec.FrameInfo[0].Duration / 1000.0 * step);
                    if (frames.Count >= maxFrames) break;
                }
                return frames.Count == 0 ? null : new Media(frames, delay);
            }
            catch (Exception ex) { Log.Debug("FirstShow media decode: {Error}", ex.Message); return null; }
        }

        public void Tick(double seconds, double delta)
        {
            if (_disposed) return;
            _time = seconds;
            bool moving = Motion() != MotionLevel.Off;
            if (seconds >= _nextBubble && seconds < FirstShowScript.GatherAt && _media.Count > 0)
            {
                Spawn(true, seconds); _nextBubble = seconds + .71;
            }
            if (seconds >= _nextFlash && seconds < FirstShowScript.GatherAt && _media.Count > 0)
            {
                Spawn(false, seconds);
                _nextFlash = seconds + (seconds < 4.5 ? .39 : .71);
            }
            if (seconds >= 15 && !_wordsSpawned)
            {
                _wordsSpawned = true;
                SpawnWord(Words("first_show_word1"), 0); SpawnWord(Words("first_show_word2"), 1);
            }
            if (seconds >= _nextSub && seconds < 27)
            {
                _subAt = seconds; _nextSub = seconds + 3.1;
            }
            foreach (var actor in _actors)
                if (!actor.Gone) Animate(actor, seconds, moving);
            _actors.RemoveAll(a => a.Gone);
            _sparks.RemoveAll(s => seconds - s.Born >= s.Life);
            if (seconds >= FirstShowScript.RevealAt && !_revealed)
            {
                _revealed = true;
                if (Particles())
                {
                    for (int i = 0; i < 6; i++) EmitBurst(Width * .5, Height * .56, i % 2 == 0 ? SKColors.HotPink : SKColors.Gold, 1.1 + i * .18);
                    EmitRipple(Width * .5, Height * .56, Width * .25, .9);
                }
                _cue("reveal");
            }
        }

        /// <summary>Live bubbles, for the stage's input region: only they take a click.</summary>
        internal IEnumerable<(double X, double Y, double Size)> Bubbles() =>
            _time >= FirstShowScript.GatherAt ? Enumerable.Empty<(double, double, double)>()
                : _actors.Where(a => a.Bubble && !a.Gone && a.Alpha > .2).Select(a => (a.X, a.Y, a.Width));

        internal int ActorCount => _actors.Count;

        private void Spawn(bool bubble, double born)
        {
            var visible = _actors.Where(a => a.MediaIndex >= 0 && !a.Gone && born - a.Born < a.Life)
                .Select(a => (a.MediaIndex, a.Slot)).ToArray();
            if (bubble && _actors.Count(a => a.Bubble && !a.Gone && born - a.Born < a.Life) >= 3) return;
            var pick = _deck?.Next(visible);
            if (pick == null) return;
            int id = _serial++, mediaIndex = pick.Value.Media, slot = pick.Value.Slot;
            var media = _media[mediaIndex];
            double[] xs = { .15, .82, .25, .74, .13, .86, .42, .60 };
            double[] ys = { .25, .66, .76, .23, .51, .42, .20, .79 };
            double jitterX = (_random.NextDouble() - .5) * .05;
            double jitterY = (_random.NextDouble() - .5) * .04;
            double w = Math.Min(Width * .19, 300), h = w * media.Frames[0].Height / media.Frames[0].Width;
            if (h > Height * .30) { w *= Height * .30 / h; h = Height * .30; }
            var a = new Actor { Id = id, MediaIndex = mediaIndex, Slot = slot, Born = born, Life = bubble ? 9 : 4.1 + id % 3 * .27, Media = media,
                HomeX = Width * (xs[slot] + jitterX), HomeY = Height * (ys[slot] + jitterY), Width = w, Height = h, Bubble = bubble };
            if (bubble) a.Width = a.Height = Math.Min(Width * .095, 155);
            _actors.Add(a);
            if (Particles()) EmitBurst(a.HomeX, a.HomeY, bubble ? SKColors.Cyan : SKColors.HotPink, .7);
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
            double motion = Motion() == MotionLevel.Full ? 1 : moving ? .4 : 0;
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
            if (time < FirstShowScript.GatherAt)
            {
                a.Alpha *= Math.Clamp((a.Life - age) / .7, 0, 1);
                if (age >= a.Life) a.Gone = true;
            }
            else
            {
                if (!a.Gathering) { a.Gathering = true; a.GatherX = a.X; a.GatherY = a.Y; }
                double delay = Fraction(a.Id * .618034) * 1.05;
                double duration = 1.7 + Fraction(a.Id * .414214) * .9;
                double p = Math.Clamp((time - FirstShowScript.GatherAt - delay) / duration, 0, 1);
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
                if (p >= 1) { a.Gone = true; if (Particles()) EmitBurst(a.X, a.Y, SKColors.Gold, .45); }
            }
            if (Particles() && time >= a.NextTrail && !a.Gone && (time >= FirstShowScript.GatherAt || a.Bubble))
            {
                EmitTrail(a.X, a.Y, time >= FirstShowScript.GatherAt ? .52 : .32, a.Id % 3 == 0, Width * .5 - a.X, Height * .56 - a.Y);
                a.NextTrail = time + (time >= FirstShowScript.GatherAt ? .045 : .16);
            }
            a.Frame = a.Media == null || !moving ? 0 : (int)(age / a.Media.Delay) % a.Media.Frames.Count;
        }

        // ---- particles: the three ChaosFxLayer emitters the show used, drawn plain ----

        private void EmitBurst(double x, double y, SKColor color, double power)
        {
            int n = (int)(14 * power) + 6;
            for (int i = 0; i < n && _sparks.Count < 900; i++)
            {
                double a = _random.NextDouble() * Math.PI * 2, v = (60 + _random.NextDouble() * 170) * power;
                _sparks.Add(new Spark { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Born = _time,
                    Life = .45 + _random.NextDouble() * .5, Size = 2 + _random.NextDouble() * 3 * power, Color = color });
            }
        }

        private void EmitRipple(double x, double y, double radius, double life) =>
            _sparks.Add(new Spark { X = x, Y = y, Born = _time, Life = life, Radius = radius, Ring = true, Color = SKColors.HotPink });

        private void EmitTrail(double x, double y, double life, bool gold, double towardX, double towardY)
        {
            if (_sparks.Count >= 900) return;
            double len = Math.Max(1, Math.Sqrt(towardX * towardX + towardY * towardY));
            _sparks.Add(new Spark { X = x + (_random.NextDouble() - .5) * 14, Y = y + (_random.NextDouble() - .5) * 14,
                Vx = towardX / len * 26, Vy = towardY / len * 26, Born = _time, Life = life, Size = 2.2 + _random.NextDouble() * 2,
                Color = gold ? SKColors.Gold : SKColors.HotPink });
        }

        public void Render(SKCanvas canvas, int width, int height)
        {
            if (_disposed) return;
            canvas.Save(); canvas.Scale(width / (float)Width, height / (float)Height);
            float W = (float)Width, H = (float)Height;
            double fade = Math.Clamp((_time - 7) / .8, 0, 1) * Math.Clamp((FirstShowScript.RevealAt - _time) / 3, 0, 1);

            // Brain drain 11-15 s: IN and OUT over .3 s at each phase edge, as WPF's blur did.
            if (_time >= 11 && _time < 15)
            {
                bool first = _time < 13;
                double edge = Math.Min(_time - (first ? 11 : 13), (first ? 13 : 15) - _time);
                _paint.Shader = null; _paint.Style = SKPaintStyle.Fill;
                _paint.Color = new SKColor(236, 228, 244, (byte)(70 * Math.Clamp(edge / .3, 0, 1)));
                canvas.DrawRect(0, 0, W, H, _paint);
            }
            if (_time >= 7 && _time < FirstShowScript.RevealAt)
            {
                _paint.Style = SKPaintStyle.Fill;
                _paint.Color = new SKColor(255, 65, 165, (byte)(255 * .13 * fade));
                canvas.DrawRect(0, 0, W, H, _paint);
                if (_spiral != null && fade > 0)
                {
                    bool moving = Motion() != MotionLevel.Off;
                    var frame = _spiral.Frames[moving ? (int)(_time / _spiral.Delay) % _spiral.Frames.Count : 0];
                    float side = Math.Max(W, H);
                    _paint.Color = SKColors.White.WithAlpha((byte)(255 * .16 * fade));
                    canvas.DrawImage(frame, new SKRect((W - side) / 2, (H - side) / 2, (W + side) / 2, (H + side) / 2), new SKSamplingOptions(SKFilterMode.Linear), _paint);
                }
            }
            foreach (var a in _actors)
            {
                if (a.Gone || a.Alpha < .002) continue;
                canvas.Save(); canvas.Translate((float)a.X, (float)a.Y); canvas.RotateDegrees((float)a.Angle);
                canvas.Scale((float)a.Scale);
                byte alpha = (byte)(Math.Clamp(a.Alpha, 0, 1) * 255);
                if (a.Text != null) DrawWord(canvas, a.Text, alpha);
                else if (a.Bubble) DrawBubble(canvas, a, alpha);
                else DrawPicture(canvas, a, alpha);
                canvas.Restore();
            }
            // The subliminal flash: in 100 ms, hold 70, out 180, at 70%.
            double sub = _time - _subAt;
            if (sub >= 0 && sub < .35)
            {
                double k = sub < .1 ? sub / .1 : sub < .17 ? 1 : 1 - (sub - .17) / .18;
                _font.Size = Math.Min(180, W * .11f);
                DrawOutlined(canvas, Words("first_show_word1"), W / 2, H / 2, (byte)(255 * .7 * Math.Clamp(k, 0, 1)), new SKColor(245, 50, 175), 8);
            }
            foreach (var s in _sparks)
            {
                double t = (_time - s.Born) / s.Life;
                if (t < 0 || t >= 1) continue;
                if (s.Ring)
                {
                    _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 3;
                    _paint.Color = s.Color.WithAlpha((byte)(170 * (1 - t)));
                    canvas.DrawCircle((float)s.X, (float)s.Y, (float)(s.Radius * (1 - Math.Pow(1 - t, 3))), _paint);
                }
                else
                {
                    double age = _time - s.Born;
                    _paint.Style = SKPaintStyle.Fill; _paint.Color = s.Color.WithAlpha((byte)(230 * (1 - t)));
                    canvas.DrawCircle((float)(s.X + s.Vx * age * (1 - t * .4)), (float)(s.Y + s.Vy * age * (1 - t * .4)), (float)(s.Size * (1 - t * .6)), _paint);
                }
            }
            _paint.Style = SKPaintStyle.Fill;
            canvas.Restore();
        }

        private void DrawPicture(SKCanvas canvas, Actor a, byte alpha)
        {
            var image = a.Media!.Frames[Math.Min(a.Frame, a.Media.Frames.Count - 1)];
            var rect = new SKRect(-(float)a.Width / 2, -(float)a.Height / 2, (float)a.Width / 2, (float)a.Height / 2);
            using var round = new SKRoundRect(rect, 14);
            // The glow is a wide soft stroke under the card, never a blur filter over the stage.
            _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 12;
            _paint.Color = SKColors.HotPink.WithAlpha((byte)(alpha * .65 * .35));
            canvas.DrawRoundRect(round, _paint);
            canvas.Save(); canvas.ClipRoundRect(round, SKClipOperation.Intersect, true);
            _paint.Style = SKPaintStyle.Fill; _paint.Color = SKColors.White.WithAlpha(alpha);
            canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear), _paint);
            canvas.Restore();
            _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 4; _paint.Color = SKColors.HotPink.WithAlpha(alpha);
            canvas.DrawRoundRect(round, _paint);
        }

        private void DrawBubble(SKCanvas canvas, Actor a, byte alpha)
        {
            float r = (float)a.Width / 2;
            _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 12;
            _paint.Color = SKColors.HotPink.WithAlpha((byte)(alpha * .45 * .4));
            canvas.DrawCircle(0, 0, r, _paint);
            var image = a.Media!.Frames[Math.Min(a.Frame, a.Media.Frames.Count - 1)];
            float inner = r * .73f;
            using var clip = new SKPath(); clip.AddCircle(0, 0, inner);
            canvas.Save(); canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            float scale = Math.Max(inner * 2 / image.Width, inner * 2 / image.Height);
            float iw = image.Width * scale / 2, ih = image.Height * scale / 2;
            _paint.Style = SKPaintStyle.Fill; _paint.Color = SKColors.White.WithAlpha(alpha);
            canvas.DrawImage(image, new SKRect(-iw, -ih, iw, ih), new SKSamplingOptions(SKFilterMode.Linear), _paint);
            canvas.Restore();
            if (_bubbleSprite != null) canvas.DrawImage(_bubbleSprite, new SKRect(-r, -r, r, r), new SKSamplingOptions(SKFilterMode.Linear), _paint);
            else { _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 3; _paint.Color = SKColors.White.WithAlpha((byte)(alpha * .7)); canvas.DrawCircle(0, 0, r, _paint); }
            // The tease shine at 28%.
            _paint.Style = SKPaintStyle.Fill; _paint.Color = SKColors.White.WithAlpha((byte)(alpha * .28));
            canvas.DrawOval(new SKRect(-r * .55f, -r * .72f, -r * .05f, -r * .38f), _paint);
        }

        private void DrawWord(SKCanvas canvas, string text, byte alpha)
        {
            _font.Size = Math.Min(56, (float)Width * .035f);
            DrawOutlined(canvas, text, 0, 0, alpha, SKColors.HotPink, 6);
        }

        private void DrawOutlined(SKCanvas canvas, string text, float x, float y, byte alpha, SKColor outline, float stroke)
        {
            _textPaint.Style = SKPaintStyle.Stroke; _textPaint.StrokeWidth = stroke;
            _textPaint.Color = outline.WithAlpha((byte)(alpha * 220 / 255));
            canvas.DrawText(text, x, y, SKTextAlign.Center, _font, _textPaint);
            _textPaint.Style = SKPaintStyle.Fill; _textPaint.Color = SKColors.White.WithAlpha(alpha);
            canvas.DrawText(text, x, y, SKTextAlign.Center, _font, _textPaint);
        }

        /// <summary>A click at a stage point pops the top bubble under it. True when one popped.</summary>
        public bool Pop(double x, double y)
        {
            if (_disposed || _time >= FirstShowScript.GatherAt) return false;
            var a = _actors.LastOrDefault(a => a.Bubble && !a.Gone && Math.Pow(a.X - x, 2) + Math.Pow(a.Y - y, 2) < Math.Pow(a.Width * .52, 2));
            if (a == null) return false;
            a.Gone = true;
            if (Particles())
            {
                EmitBurst(a.X, a.Y, SKColors.HotPink, 1.3);
                EmitRipple(a.X, a.Y, a.Width, .5);
            }
            _cue("pop");
            return true;
        }

        private static double Fraction(double value) => value - Math.Floor(value);
        private static double Triangle(double value) => 1 - Math.Abs(Fraction(value) * 2 - 1);

        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            _actors.Clear(); _sparks.Clear();
            foreach (var m in _media) Free(m);
            _media.Clear();
            if (_spiral != null) Free(_spiral);
            _spiral = null;
            _bubbleSprite?.Dispose(); _bubbleSprite = null;
            _paint.Dispose(); _textPaint.Dispose(); _font.Dispose();
        }
    }
}
