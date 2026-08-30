using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// CHANNEL RERUNS. The one channel on the glass that is ABOUT THE PLAYER: she digs the best class
/// you ever graded out of the Arcademy's own day ledger and airs it back at you as a tape.
///
/// <para>Ported from the campus saver (<c>Resources/web/arcademy/emi/channels.js</c>, CH4) together
/// with its two helpers, <c>bestRerun</c> and <c>flapName</c>. The campus reads the region straight
/// out of page memory; the desk has no page, so the same region is read off the same file the page
/// writes it to (<c>arcademy_meta.json</c>) - which is why this is the only channel here with a
/// data dependency, and the only one that can refuse for want of MATERIAL rather than for want of a
/// library.</para>
/// </summary>
public static partial class EmiChannels
{
    /// <summary>
    /// The rerun tape. Static paint, four moving things: the OSD word, its blink, the timecode and
    /// the tracking band rolling up the frame.
    ///
    /// <para>SCALE. Every campus number below is quoted at the 152 x 137 virtual glass
    /// (<c>GLASS_W</c>/<c>GLASS_H</c> in channels.js) and is scaled by <c>w / 152</c> across and
    /// <c>h / 137</c> down, the same law <see cref="PongPainter"/> applies with its own reference.
    /// Pong references 60 because the numbers IT was ported from are the pitch demo's; these are the
    /// campus painter's, so 152 is the only reference that keeps the card centred - at <c>w / 60</c>
    /// the date line would land two glass-heights below the glass.</para>
    ///
    /// <para>SHE IS NOT EMBARRASSED HERE (campus <c>caught</c> table). The desk's caught beat is the
    /// orchestrator's business, but the paint has to agree with it: this is a tape she keeps on
    /// purpose, so nothing here reads as a channel she got stuck on.</para>
    /// </summary>
    internal sealed class RerunsPainter : EmiChannelPainter
    {
        private const double RefW = 152.0;   // CAMPUS GLASS_W - what every number here is quoted at
        private const double RefH = 137.0;   // CAMPUS GLASS_H

        /// <summary>The tape black. Deeper than <see cref="ScreenBrush"/> on purpose: the campus
        /// clears this one channel to #07070f so the band and the scanlines have somewhere to be.</summary>
        private static readonly SolidColorBrush TapeBlack =
            Freeze(new SolidColorBrush(Color.FromRgb(0x07, 0x07, 0x0F)));

        private static readonly SolidColorBrush FlapInk =
            Freeze(new SolidColorBrush(Color.FromRgb(0x8D, 0x8D, 0xB0)));   // CAMPUS #8d8db0
        private static readonly SolidColorBrush DateInk =
            Freeze(new SolidColorBrush(Color.FromRgb(0x4D, 0x4D, 0x6A)));   // CAMPUS #4d4d6a
        private static readonly SolidColorBrush DimInk =
            Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x7A)));   // CAMPUS #5a5a7a, the REW off-beat
        private static readonly SolidColorBrush BandInk =
            Freeze(new SolidColorBrush(Color.FromArgb(0x29, 0xE6, 0xE6, 0xFF)));  // rgba(230,230,255,0.16)
        private static readonly SolidColorBrush BandUnderInk =
            Freeze(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0x69, 0xB4)));  // rgba(255,105,180,0.10)

        private readonly double _w, _h, _k, _ky;
        private readonly Tape? _tape;

        private readonly TextBlock _osd = new();
        private readonly TextBlock _time = new();
        private readonly Rectangle _band = new();
        private readonly Rectangle _bandUnder = new();

        private double _bandH, _bandUnderH, _bandSpan;
        private int _lastOsd = -1;      // 0 = REW dim, 1 = REW lit, 2 = PLAY; -1 = nothing written yet
        private int _lastSecond = -1;   // the timecode only ever changes once a second

        internal RerunsPainter(double w, double h)
        {
            _w = w; _h = h;
            _k = Math.Max(0.25, w / RefW);
            _ky = Math.Max(0.25, h / RefH);

            // The tape is chosen HERE, not in Tick and not in Attach's hot path: one cached read at
            // build time, so the ten seconds this channel lives never touch the disk.
            _tape = HasMaterial() ? CachedTape() : null;
        }

        /// <summary>
        /// The house <c>TryBuild</c> shape, for whichever door the orchestrator uses. NO TAPE, NO
        /// CHANNEL - the campus's plan() returns null rather than airing a stub, and so does this.
        /// </summary>
        internal static RerunsPainter? TryBuild(double w, double h)
        {
            if (!HasMaterial()) return null;
            return new RerunsPainter(w, h);
        }

        public override string Id => "reruns";

        public override void Attach(Panel host)
        {
            // Not AddBackdrop: that one paints the shared dead-screen navy, and this channel is a
            // tape rather than a dead screen.
            host.Children.Add(Box(0, 0, _w, _h, TapeBlack, 1.0));

            var tape = _tape;

            // THE CARD. Grade huge, class under it, date under that - the campus's three lines at
            // 40 / 7 / 7 px, top-anchored at y 36 / 88 / 102.
            if (tape != null)
            {
                host.Children.Add(Centred(tape.Grade, 40, 36, PinkBrush));
                host.Children.Add(Centred(FlapName(tape.GameKey), 7, 88, FlapInk));
                host.Children.Add(Centred(tape.Date, 7, 102, DateInk));
            }
            // A null tape can only happen when the blob changed between the feasibility check and
            // the build. The tape still rolls, blank: a dead ten seconds is recoverable, a throw
            // into the glass is not.

            // THE OSD, top left. Text and colour are the only things Tick writes here.
            _osd.FontFamily = EmiFace.PixelFont;
            _osd.FontSize = Px(8);
            _osd.Foreground = PinkBrush;
            _osd.Text = "<< REW";
            _osd.IsHitTestVisible = false;
            Canvas.SetLeft(_osd, 5 * _k);
            Canvas.SetTop(_osd, 5 * _ky);
            host.Children.Add(_osd);

            // The fake timecode, bottom right. Right-aligned by giving the block the glass's width
            // less the campus's 5 px margin: WPF has no draw-from-the-right anchor, and measuring a
            // pixel-font string per frame to place it by hand would be the one allocation this
            // painter cannot afford.
            _time.FontFamily = EmiFace.PixelFont;
            _time.FontSize = Px(8);
            _time.Foreground = PinkBrush;
            _time.Text = "0:00";
            _time.TextAlignment = TextAlignment.Right;
            _time.Width = Math.Max(1, _w - 5 * _k);
            _time.IsHitTestVisible = false;
            Canvas.SetLeft(_time, 0);
            Canvas.SetTop(_time, Math.Max(0, _h - 14 * _ky));
            host.Children.Add(_time);

            // THE TRACKING BAND, over the card and under the scanlines - the campus's paint order.
            _bandH = Math.Max(1.0, 9 * _ky);
            _bandUnderH = Math.Max(0.5, 3 * _ky);
            _bandSpan = _h + 18 * _ky;
            Dress(_band, _bandH, BandInk);
            Dress(_bandUnder, _bandUnderH, BandUnderInk);
            host.Children.Add(_band);
            host.Children.Add(_bandUnder);

            // The scanline mask every channel wears. ONE tiled brush rather than sixty-eight
            // rectangles: at the small end of the glass a 2 px campus row is under half a DIP, and
            // sixty-eight sub-pixel bars antialias into a flat grey wash instead of a rhythm.
            host.Children.Add(new Rectangle
            {
                Width = _w,
                Height = _h,
                Fill = Scanlines(_w, _ky),
                IsHitTestVisible = false
            });
        }

        public override void Tick(double tMs)
        {
            // THE FIRST SECOND IS A REWIND. She is not showing you the tape yet, she is finding it.
            bool rew = tMs < 1000;
            int state = !rew ? 2 : ((int)Math.Floor(tMs / 160.0) % 2 == 0 ? 0 : 1);
            if (state != _lastOsd)
            {
                _lastOsd = state;
                _osd.Text = state == 2 ? "> PLAY" : "<< REW";
                _osd.Foreground = state == 0 ? DimInk : PinkBrush;
            }

            // One string a second, never one a frame.
            int seconds = (int)Math.Max(0, Math.Floor(tMs / 1000.0));
            if (seconds != _lastSecond)
            {
                _lastSecond = seconds;
                _time.Text = Timecode(seconds);
            }

            // The band rolls UP, once every two seconds, and runs off the top before it comes back
            // on at the bottom - hence the span overshoot.
            double y = _h - (tMs % 2000.0) / 2000.0 * _bandSpan;
            Canvas.SetTop(_band, y);
            Canvas.SetTop(_bandUnder, y + _bandH);
        }

        // ------------------------------------------------------------ paint helpers

        /// <summary>A campus font size in glass px, at whole DIPs: Press Start 2P has an 8 x 8 cell
        /// and smears when it is asked to land between device pixels (see <see cref="EmiFace"/>).
        /// The floor of 4 breaks the scaling law on purpose at the narrow end of the glass: strictly
        /// proportional, the class name would round to three DIPs and read as texture, not a word.</summary>
        private double Px(double campusSize) => Math.Max(4, Math.Round(campusSize * _k));

        /// <summary>One centred line of the card. Centred by spanning the glass rather than by
        /// measuring: the block is built once and never moves again.</summary>
        private TextBlock Centred(string s, double campusSize, double campusTop, Brush ink)
        {
            var tb = new TextBlock
            {
                Text = s,
                FontFamily = EmiFace.PixelFont,
                FontSize = Px(campusSize),
                Foreground = ink,
                Width = _w,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(tb, 0);
            Canvas.SetTop(tb, campusTop * _ky);
            return tb;
        }

        private void Dress(Rectangle r, double height, Brush fill)
        {
            r.Width = _w;
            r.Height = height;
            r.Fill = fill;
            r.IsHitTestVisible = false;
            Canvas.SetLeft(r, 0);
            Canvas.SetTop(r, -height);   // parked off the top until the first tick places it
        }

        /// <summary>The campus's <c>scanlines(g, 0.2)</c> as a frozen tile: one dark row every two
        /// glass px, at least two DIPs apart so the rhythm survives the small glass.</summary>
        private static Brush Scanlines(double w, double ky)
        {
            double step = Math.Max(2.0, Math.Round(2 * ky));
            double tileW = Math.Max(1.0, w);
            var drawing = new GeometryDrawing(
                Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0))),   // rgba(0,0,0,0.2)
                null,
                new RectangleGeometry(new Rect(0, 0, tileW, step / 2.0)));
            // The tile is the full width of the glass, so this repeats DOWN and never across: a one
            // DIP wide tile would have the compositor stamping the same column a hundred and fifty
            // times per frame for an identical result.
            var brush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, tileW, step),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, tileW, step),
                Stretch = Stretch.Fill
            };
            return Freeze(brush);
        }

        /// <summary><c>0:07</c> - the fake timecode every playback channel wears.</summary>
        private static string Timecode(int seconds)
        {
            int m = seconds / 60, s = seconds % 60;
            return m.ToString(CultureInfo.InvariantCulture) + ":" +
                   s.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The split-flap short name for a class key, a straight port of the campus's
        /// <c>flapName</c>. NO LEXICON REACHES EMI: she reads the raw key off the ledger and shouts
        /// it, which is why a key nobody named still airs as something.</summary>
        internal static string FlapName(string? gameKey)
        {
            var raw = gameKey ?? string.Empty;
            // /[_-]+/g -> ' ': a RUN of underscores and hyphens collapses to one space, and nothing
            // else does. Spaces the key already had are left alone, exactly as the regex leaves them.
            var sb = new System.Text.StringBuilder(raw.Length);
            bool inRun = false;
            foreach (var c in raw)
            {
                bool sep = c == '_' || c == '-';
                if (sep) { if (!inRun) sb.Append(' '); }
                else sb.Append(c);
                inRun = sep;
            }
            var s = sb.ToString().ToUpperInvariant().Trim();
            // Faithful to the campus, cut first and only THEN fall back: a 14 char cut can end on a
            // space, and the empty string is the only thing that becomes CLASS.
            return s.Length > 14 ? s.Substring(0, 14) : (s.Length == 0 ? "CLASS" : s);
        }

        // ------------------------------------------------------------ the material

        private sealed record Tape(string Date, string GameKey, string Grade);

        /// <summary>The grades a rerun is worth airing. CAMPUS <c>GRADE_OK</c>, exactly: S or A, and
        /// the comparison is done lowered so a ledger that ever wrote "a" still counts.</summary>
        private static readonly HashSet<string> GradeOk =
            new(StringComparer.Ordinal) { "s", "a" };

        /// <summary>An <c>arcademy_meta.json</c> bigger than this is a corrupt save, not a ledger -
        /// the store's own write cap is half a megabyte.</summary>
        private const long MaxBlobBytes = 2 * 1024 * 1024;

        private static readonly object MatLock = new();
        private static long _matStamp = -1;   // ticks ^ length, the house stamp; -1 = never read
        private static Tape? _matTape;

        /// <summary>
        /// IS THERE A TAPE? True when the Arcademy's day ledger holds a class this channel would
        /// air. Read at most once per change to the file (the same <c>LastWriteTimeUtc.Ticks ^
        /// Length</c> stamp <see cref="Services.Arcademy.ArcademyHostService"/> caches its wallet
        /// against), so the wheel asking this every ten seconds costs one <c>FileInfo</c>.
        ///
        /// <para>NEVER THROWS AND ALWAYS FAILS CLOSED. A missing file, a half-written blob, a
        /// <c>days</c> region that is an array this week - every one of them is "no tape", because
        /// the alternative is a broken save reaching the mascot's face.</para>
        /// </summary>
        internal static bool HasMaterial()
        {
            try
            {
                var path = System.IO.Path.Combine(App.UserDataPath, "arcademy_meta.json");
                long stamp;
                long length;
                try
                {
                    var info = new FileInfo(path);
                    length = info.Exists ? info.Length : 0L;
                    stamp = info.Exists ? (info.LastWriteTimeUtc.Ticks ^ length) : 0L;
                }
                catch { stamp = 0L; length = 0L; }

                lock (MatLock)
                {
                    if (_matStamp != stamp)
                    {
                        _matStamp = stamp;
                        _matTape = (stamp == 0L || length > MaxBlobBytes) ? null : ReadTape(path);
                    }
                    return _matTape != null;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] rerun material check failed");
                return false;
            }
        }

        /// <summary>The cached tape, without re-reading. Null until <see cref="HasMaterial"/> has
        /// said yes at least once for the current stamp.</summary>
        private static Tape? CachedTape()
        {
            lock (MatLock) { return _matTape; }
        }

        /// <summary>One read of the ledger. Called only when the stamp moved.</summary>
        private static Tape? ReadTape(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var blob = JObject.Parse(File.ReadAllText(path));
                return BestRerun(blob["days"] as JObject);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] rerun ledger read failed");
                return null;
            }
        }

        /// <summary>
        /// THE MOST RECENT S-OR-BETTER DAY ON RECORD, or null. A straight port of the campus's
        /// <c>bestRerun</c> against the same region: <c>days</c> is keyed by LOCAL date
        /// (<c>yyyy-MM-dd</c>, regression #978), so ordinal order IS chronological order and
        /// newest-first is a reversed ordinal sort - no date parsing, and a key that is not a date
        /// simply sorts somewhere and never throws.
        ///
        /// <para>Inside a day the classes are walked in the order the page wrote them, which is the
        /// order they were sat, and the FIRST graded one wins. That is the campus's behaviour and
        /// not an accident to be tidied: the rule is "the newest day that went well", not "the best
        /// class of that day".</para>
        /// </summary>
        private static Tape? BestRerun(JObject? days)
        {
            if (days == null) return null;
            foreach (var date in days.Properties().Select(p => p.Name)
                         .OrderByDescending(n => n, StringComparer.Ordinal))
            {
                if (days[date] is not JObject row) continue;
                if (row["classes"] is not JObject classes) continue;
                foreach (var cls in classes.Properties())
                {
                    if (cls.Value is not JObject entry) continue;
                    var grade = (entry["grade"]?.Type == JTokenType.String
                        ? (string?)entry["grade"]
                        : entry["grade"]?.ToString()) ?? string.Empty;
                    grade = grade.ToLowerInvariant();
                    if (GradeOk.Contains(grade))
                    {
                        return new Tape(date, cls.Name, grade.ToUpperInvariant());
                    }
                }
            }
            return null;
        }
    }
}
