using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Services.Possession;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ConditioningControlPanel.Services.Banner
{
    /// <summary>
    /// One act on the marquee. The ticker is already paused and faded out when this runs, and the
    /// host is empty; the effect owns the strip it builds and leaves the host cleared behind it.
    ///
    /// <para>Every act is two beats: the body arrives, holds for about 1.2 s, and then the tag
    /// lands separately with a hit of its own. That shape is the point of the feature, so it lives
    /// in every implementation rather than in a base class that could be bypassed.</para>
    /// </summary>
    public interface IMarqueeReadEffect
    {
        /// <summary>Stable id. Used to keep the same act from playing twice in a row.</summary>
        string Id { get; }

        /// <summary>Whether this act suits the line. A no sends the picker to the next candidate.</summary>
        bool CanPlay(string body, string tag);

        Task PlayAsync(MarqueeReadStage stage, string body, string tag, CancellationToken ct);
    }

    // =================================================================================== the stage

    /// <summary>
    /// The strip the acts build on: the <c>MarqueeRead</c> host inside MarqueeFadeHost, so an act
    /// inherits the banner's clip and its edge fade for free.
    ///
    /// <para>Photosafe is read live off <c>AppSettings.LockdownPhotosafe</c>, the same switch
    /// <c>GlyphRotEffect</c> and the possession director read. Under it no act may produce a
    /// flicker or a jitter frame; fades and slides are all that is left.</para>
    /// </summary>
    public sealed class MarqueeReadStage
    {
        /// <param name="allowAmbient">
        /// False when the ticker itself is parked static (Performance tier or reduced motion). The
        /// interlude still plays, but as a plain crossfade: no glitch frames, no flicker, no jitter.
        /// </param>
        public MarqueeReadStage(Panel host, TranslateTransform slide, bool photosafe,
                                bool allowMotion, bool allowAmbient)
        {
            Host = host;
            Slide = slide;
            Photosafe = photosafe;
            AllowMotion = allowMotion;
            AllowAmbient = allowAmbient;
        }

        public Panel Host { get; }

        /// <summary>The whole strip's nudge transform. Only the teletype's tag hit uses it.</summary>
        public TranslateTransform Slide { get; }

        public bool Photosafe { get; }

        /// <summary>False when the user turned motion Off: acts degrade to plain fades.</summary>
        public bool AllowMotion { get; }

        /// <summary>False when the ticker is parked static: acts play as crossfades only.</summary>
        public bool AllowAmbient { get; }

        /// <summary>Everything an act may do with a jitter or a flicker hangs off this.</summary>
        public bool AllowFlicker => AllowMotion && AllowAmbient && !Photosafe;

        internal Brush Pink => Find("PinkBrush") ?? new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));

        internal Color GlowColor
        {
            get
            {
                if (Find("DarkPinkBrush") is SolidColorBrush b) return b.Color;
                return Color.FromRgb(0xC7, 0x1E, 0x7A);
            }
        }

        private static Brush? Find(string key)
        {
            try { return Application.Current?.TryFindResource(key) as Brush; }
            catch (Exception ex) { Diag.Swallowed(ex, "theme brush unavailable"); return null; }
        }

        /// <summary>Reset the host between acts. Also drops any animation the last act held.</summary>
        public void Clear()
        {
            try
            {
                Slide.BeginAnimation(TranslateTransform.XProperty, null);
                Slide.X = 0;
                foreach (var child in Host.Children)
                {
                    if (child is UIElement e) e.BeginAnimation(UIElement.OpacityProperty, null);
                }
                Host.Children.Clear();
            }
            catch (Exception ex) { Diag.Swallowed(ex, "stage teardown"); }
        }

        /// <summary>
        /// Build the word strip for a line. The tag's words are created up front and left
        /// invisible, so the strip is already at its final width when the body lands and the
        /// second beat does not shove the first one sideways.
        /// </summary>
        public ReadStrip BuildStrip(string body, string tag, FontFamily font, double fontSize,
                                    FontWeight weight, bool upper = false, Brush? brush = null)
        {
            var strip = new ReadStrip();
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            strip.Panel = panel;

            var ink = brush ?? Pink;
            double gap = Math.Max(3, fontSize * 0.34);

            void AddWords(string text, bool isTag)
            {
                foreach (var raw in (text ?? string.Empty).Split(' '))
                {
                    var word = raw.Trim();
                    if (word.Length == 0) continue;
                    if (upper) word = word.ToUpperInvariant();

                    var slide = new TranslateTransform();
                    var block = new TextBlock
                    {
                        Text = word,
                        FontFamily = font,
                        FontSize = fontSize,
                        FontWeight = weight,
                        Foreground = ink,
                        TextWrapping = TextWrapping.NoWrap,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    var cell = new Grid
                    {
                        Margin = new Thickness(0, 0, gap, 0),
                        RenderTransform = slide,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    cell.Children.Add(block);
                    panel.Children.Add(cell);

                    strip.Words.Add(new ReadWord
                    {
                        Cell = cell,
                        Block = block,
                        Slide = slide,
                        Word = word,
                        IsTag = isTag,
                    });
                    if (isTag && strip.TagStart < 0) strip.TagStart = strip.Words.Count - 1;
                }
            }

            AddWords(body, false);
            AddWords(tag, true);

            var box = new Viewbox
            {
                Child = panel,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 2, 14, 2),
                Effect = new DropShadowEffect
                {
                    Color = GlowColor,
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.8,
                },
            };
            strip.Box = box;
            Host.Children.Add(box);
            return strip;
        }
    }

    /// <summary>One word of a read, with the pieces the acts animate.</summary>
    public sealed class ReadWord
    {
        public Grid Cell { get; init; } = null!;
        public TextBlock Block { get; init; } = null!;
        public TranslateTransform Slide { get; init; } = null!;
        public string Word { get; init; } = "";
        public bool IsTag { get; init; }

        /// <summary>The redaction act's bar. Null for every other act.</summary>
        public Rectangle? Bar { get; set; }
    }

    /// <summary>A built strip: the Viewbox, its word panel, and the words in reading order.</summary>
    public sealed class ReadStrip
    {
        public Viewbox Box { get; set; } = null!;
        public StackPanel Panel { get; set; } = null!;
        public List<ReadWord> Words { get; } = new();

        /// <summary>Index of the first tag word, or -1 when the line had no separable tag.</summary>
        public int TagStart { get; set; } = -1;

        public IEnumerable<ReadWord> Body
        {
            get { foreach (var w in Words) if (!w.IsTag) yield return w; }
        }

        public IEnumerable<ReadWord> Tag
        {
            get { foreach (var w in Words) if (w.IsTag) yield return w; }
        }

        public void HideTag()
        {
            foreach (var w in Tag) w.Cell.Opacity = 0;
        }

        public void HideAll()
        {
            foreach (var w in Words) w.Cell.Opacity = 0;
        }
    }

    // ================================================================================ the registry

    /// <summary>
    /// The act table and the picker. Kept separate from the acts so the never-twice-in-a-row rule
    /// can be pinned by a test without a window behind it.
    /// </summary>
    public static class MarqueeReadEffects
    {
        public const string GlitchHeal = "glitch-heal";
        public const string NumberFirst = "number-first";
        public const string Teletype = "teletype";
        public const string NeonWarmUp = "neon-warmup";
        public const string Redaction = "redaction";

        /// <summary>A fresh act table. One instance per interlude; the acts hold no state.</summary>
        public static IReadOnlyList<IMarqueeReadEffect> Build() => new IMarqueeReadEffect[]
        {
            new GlitchHealEffect(),
            new NumberFirstEffect(),
            new TeletypeEffect(),
            new NeonWarmUpEffect(),
            new RedactionEffect(),
        };

        /// <summary>
        /// Pick an act. Never the one that just played, never one that says it cannot carry the
        /// line, and null only when nothing at all is eligible.
        ///
        /// <para>The roll is a plain index into whatever survived the filter, so a table that
        /// shrinks to one entry still plays that entry rather than falling over - the
        /// no-repeat rule yields to actually having something to show only when the table is down
        /// to a single usable act.</para>
        /// </summary>
        public static string? PickId(IReadOnlyList<string> ids, string? lastId,
                                     Func<string, bool> canPlay, Func<int, int> roll)
        {
            if (ids == null || ids.Count == 0) return null;

            var usable = new List<string>();
            foreach (var id in ids) if (canPlay(id)) usable.Add(id);
            if (usable.Count == 0) return null;

            var fresh = new List<string>();
            foreach (var id in usable) if (!string.Equals(id, lastId, StringComparison.Ordinal)) fresh.Add(id);

            var from = fresh.Count > 0 ? fresh : usable;
            return from[Math.Abs(roll(from.Count)) % from.Count];
        }
    }

    // ============================================================================= 1. glitch heal

    /// <summary>
    /// The line arrives as rot and heals left to right. Substitutions come from the same table the
    /// possession glyphrot uses (<see cref="MarqueeGlyphRot"/>), so a rotted marquee and a rotted
    /// label are visibly the same thing happening.
    ///
    /// <para>Two offset copies in cyan and magenta sit behind the real strip for the chromatic
    /// split, and a couple of horizontal jitter frames run while the body heals. Both the jitter
    /// and nothing else are dropped under photosafe: the split is a static offset, not a flicker,
    /// so it stays.</para>
    /// </summary>
    public sealed class GlitchHealEffect : IMarqueeReadEffect
    {
        public string Id => MarqueeReadEffects.GlitchHeal;

        public bool CanPlay(string body, string tag) => !string.IsNullOrWhiteSpace(body);

        private const double BodyHealMs = 2200;
        private const double HoldMs = 1200;
        private const double TagHealMs = 620;
        private const double TailMs = 900;

        public async Task PlayAsync(MarqueeReadStage stage, string body, string tag, CancellationToken ct)
        {
            var font = Helpers.FontPickerHelper.FredokaFamily;
            const double size = 21;

            var ghostA = MakeGhost(stage, body, tag, font, size, Color.FromRgb(0x3B, 0xE8, 0xFF), -2);
            var ghostB = MakeGhost(stage, body, tag, font, size, Color.FromRgb(0xFF, 0x3B, 0xC8), 2);
            var strip = stage.BuildStrip(body, tag, font, size, FontWeights.SemiBold);

            var layers = new[] { ghostA, ghostB, strip };
            foreach (var layer in layers) Scramble(layer, includeTag: true);
            strip.HideTag();
            ghostA.HideTag();
            ghostB.HideTag();

            if (stage.AllowFlicker) Jitter(stage);

            if (!await Heal(layers, tagWords: false, BodyHealMs, ct).ConfigureAwait(true)) return;
            PossAnim.Settle(stage.Slide, TranslateTransform.XProperty, 0);

            // The ghosts belong to the rot, so they leave with it.
            PossAnim.To(ghostA.Box, UIElement.OpacityProperty, 0, 420);
            PossAnim.To(ghostB.Box, UIElement.OpacityProperty, 0, 420);

            if (!await PossAnim.DelayAsync(HoldMs, ct).ConfigureAwait(true)) return;

            foreach (var w in strip.Tag) w.Cell.Opacity = 1;
            if (!await Heal(new[] { strip }, tagWords: true, TagHealMs, ct).ConfigureAwait(true)) return;

            await PossAnim.DelayAsync(TailMs, ct).ConfigureAwait(true);
        }

        private static ReadStrip MakeGhost(MarqueeReadStage stage, string body, string tag,
                                           FontFamily font, double size, Color tint, double dx)
        {
            var ghost = stage.BuildStrip(body, tag, font, size, FontWeights.SemiBold,
                                         brush: new SolidColorBrush(tint));
            ghost.Box.Opacity = 0.5;
            ghost.Box.Effect = null;
            ghost.Box.RenderTransform = new TranslateTransform(dx, 0);
            return ghost;
        }

        private static void Jitter(MarqueeReadStage stage)
        {
            var kf = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(BodyHealMs) };
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(-3, KeyTime.FromPercent(0.22)));
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.26)));
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(4, KeyTime.FromPercent(0.55)));
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.59)));
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(-2, KeyTime.FromPercent(0.81)));
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.85)));
            stage.Slide.BeginAnimation(TranslateTransform.XProperty, kf);
        }

        private static void Scramble(ReadStrip strip, bool includeTag)
        {
            foreach (var w in strip.Words)
            {
                if (!includeTag && w.IsTag) continue;
                w.Block.Text = MarqueeGlyphRot.Rot(w.Word);
            }
        }

        /// <summary>
        /// Heal one pass, left to right, a few characters per tick so a long line still lands
        /// inside its beat. Every layer is painted with the same prefix, which is what keeps the
        /// chromatic ghosts in step with the real strip.
        /// </summary>
        private static async Task<bool> Heal(IReadOnlyList<ReadStrip> layers, bool tagWords,
                                             double totalMs, CancellationToken ct)
        {
            var lead = layers[layers.Count - 1];
            var targets = new List<int>();
            int chars = 0;
            for (int i = 0; i < lead.Words.Count; i++)
            {
                if (lead.Words[i].IsTag != tagWords) continue;
                targets.Add(i);
                chars += lead.Words[i].Word.Length;
            }
            if (targets.Count == 0) return true;

            const double TickMs = 48;
            int ticks = Math.Max(1, (int)(totalMs / TickMs));
            int perTick = Math.Max(1, (int)Math.Ceiling(chars / (double)ticks));

            int budget = 0;
            foreach (var index in targets)
            {
                var word = lead.Words[index].Word;
                for (int c = 1; c <= word.Length; c++)
                {
                    var prefix = word.Substring(0, c) + MarqueeGlyphRot.Rot(word.Substring(c));
                    foreach (var layer in layers) layer.Words[index].Block.Text = prefix;

                    if (++budget < perTick) continue;
                    budget = 0;
                    if (!await PossAnim.DelayAsync(TickMs, ct).ConfigureAwait(true)) return false;
                }
                foreach (var layer in layers) layer.Words[index].Block.Text = word;
            }
            return true;
        }
    }

    /// <summary>
    /// The substitution table, lifted wholesale from
    /// <c>Services/Possession/Effects/GlyphRotEffect.cs</c> so the marquee's rot and the haunt's
    /// rot are the same alphabet.
    ///
    /// <para>INVARIANT, same as there: a rot is the same CHARACTER COUNT as the text it ate, and
    /// every substitute is a single base character (a combining mark rides on the one in front of
    /// it), so a healing line never re-flows and never shoves its neighbours around.</para>
    /// </summary>
    internal static class MarqueeGlyphRot
    {
        private static readonly char[] Boxes = { '▯', '▮', '□', '▪', '◻', '▉', '▒' };

        private static readonly Dictionary<char, char[]> LookAlikes = new()
        {
            ['a'] = new[] { 'а', '@' },
            ['e'] = new[] { 'е', '3' },
            ['o'] = new[] { 'о', '0' },
            ['i'] = new[] { 'і', '1' },
            ['s'] = new[] { 'ѕ', '5' },
            ['c'] = new[] { 'с' },
            ['n'] = new[] { 'п' },
            ['t'] = new[] { 'т', '7' },
            ['r'] = new[] { 'г' },
            ['y'] = new[] { 'у' },
            ['h'] = new[] { 'һ' },
            ['m'] = new[] { 'м' },
        };

        private static readonly Random Rng = new();

        public static string Rot(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text!.Length);
            foreach (var c in text) sb.Append(Rot(c));
            return sb.ToString();
        }

        public static string Rot(char c)
        {
            if (c == ' ') return " ";
            var lower = char.ToLowerInvariant(c);
            if (LookAlikes.TryGetValue(lower, out var picks) && Rng.Next(100) < 55)
                return picks[Rng.Next(picks.Length)].ToString();
            return Boxes[Rng.Next(Boxes.Length)].ToString();
        }
    }

    // ============================================================================ 2. number first

    /// <summary>
    /// For the lines built around a figure. The number arrives on its own, large and centred,
    /// rolling up on the odometer, then shrinks into the slot it belongs in while the rest of the
    /// sentence fades in around it.
    ///
    /// <para>Declines any line with no rendered integer in it; the picker then moves on. That is
    /// why this act is in the table rather than the only act for numbered lines: roughly half the
    /// pool draws its numbers from live ledger keys and the other half spells them out.</para>
    /// </summary>
    public sealed class NumberFirstEffect : IMarqueeReadEffect
    {
        public string Id => MarqueeReadEffects.NumberFirst;

        private static readonly Regex Integer = new(@"\d{1,7}", RegexOptions.Compiled);

        public bool CanPlay(string body, string tag) =>
            !string.IsNullOrWhiteSpace(body) && Integer.IsMatch(body);

        private const double RollMs = 900;
        private const double SettleMs = 620;
        private const double HoldMs = 1200;
        private const double TagMs = 460;
        private const double TailMs = 820;

        public async Task PlayAsync(MarqueeReadStage stage, string body, string tag, CancellationToken ct)
        {
            var font = Helpers.FontPickerHelper.FredokaFamily;
            const double size = 21;
            const double bigSize = 46;

            var strip = stage.BuildStrip(body, tag, font, size, FontWeights.SemiBold);
            strip.HideAll();

            var slot = FindNumberWord(strip);
            if (slot == null)
            {
                // Unreachable behind CanPlay, but a strip with no numeric word would leave the big
                // number nowhere to fly to. Play the two beats as a plain fade rather than a blank.
                foreach (var w in strip.Body)
                    PossAnim.To(w.Cell, UIElement.OpacityProperty, 1, SettleMs, PossAnim.EaseOut, 0);
                if (!await PossAnim.DelayAsync(SettleMs + HoldMs, ct).ConfigureAwait(true)) return;
                foreach (var w in strip.Tag)
                    PossAnim.To(w.Cell, UIElement.OpacityProperty, 1, TagMs, PossAnim.EaseOut, 0);
                await PossAnim.DelayAsync(TagMs + TailMs, ct).ConfigureAwait(true);
                return;
            }

            var match = Integer.Match(slot.Word);
            double value = double.TryParse(match.Value, NumberStyles.Integer,
                                           CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

            var scale = new ScaleTransform(1, 1);
            var drift = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(drift);

            var big = new TextBlock
            {
                Text = match.Value,
                FontFamily = font,
                FontSize = bigSize,
                FontWeight = FontWeights.Bold,
                Foreground = stage.Pink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = group,
                Effect = new DropShadowEffect { Color = stage.GlowColor, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 },
            };
            stage.Host.Children.Add(big);

            Services.MotionFx.Odometer(big, 0, value, "{0:0}", RollMs / 1000.0);
            if (!await PossAnim.DelayAsync(RollMs + 260, ct).ConfigureAwait(true)) return;

            // Where the real word sits, in host coordinates, and how much the Viewbox shrank it.
            var (offsetX, offsetY, viewScale) = Locate(stage, slot);
            double target = Math.Max(0.12, size * viewScale / bigSize);

            PossAnim.To(scale, ScaleTransform.ScaleXProperty, target, SettleMs, PossAnim.EaseInOut);
            PossAnim.To(scale, ScaleTransform.ScaleYProperty, target, SettleMs, PossAnim.EaseInOut);
            PossAnim.To(drift, TranslateTransform.XProperty, offsetX, SettleMs, PossAnim.EaseInOut);
            PossAnim.To(drift, TranslateTransform.YProperty, offsetY, SettleMs, PossAnim.EaseInOut);

            foreach (var w in strip.Body)
            {
                if (ReferenceEquals(w, slot)) continue;
                PossAnim.To(w.Cell, UIElement.OpacityProperty, 1, SettleMs, PossAnim.EaseOut, 0);
            }
            if (!await PossAnim.DelayAsync(SettleMs, ct).ConfigureAwait(true)) return;

            // Hand the slot back to the real word before the big one goes, so nothing blinks.
            slot.Cell.Opacity = 1;
            PossAnim.To(big, UIElement.OpacityProperty, 0, 180);

            if (!await PossAnim.DelayAsync(HoldMs, ct).ConfigureAwait(true)) return;

            foreach (var w in strip.Tag)
                PossAnim.To(w.Cell, UIElement.OpacityProperty, 1, TagMs, PossAnim.EaseOut, 0);

            await PossAnim.DelayAsync(TagMs + TailMs, ct).ConfigureAwait(true);
        }

        private static ReadWord? FindNumberWord(ReadStrip strip)
        {
            foreach (var w in strip.Body) if (Integer.IsMatch(w.Word)) return w;
            return null;
        }

        /// <summary>
        /// The slot's centre relative to the host's centre, plus the Viewbox's scale factor, read
        /// off the live visual tree rather than guessed: the strip may have been shrunk to fit.
        /// </summary>
        private static (double X, double Y, double Scale) Locate(MarqueeReadStage stage, ReadWord slot)
        {
            try
            {
                stage.Host.UpdateLayout();
                var t = slot.Block.TransformToAncestor(stage.Host);
                var centre = t.Transform(new Point(slot.Block.ActualWidth / 2, slot.Block.ActualHeight / 2));
                double scale = 1;
                if (t is MatrixTransform m && m.Matrix.M11 > 0) scale = m.Matrix.M11;
                return (centre.X - stage.Host.ActualWidth / 2,
                        centre.Y - stage.Host.ActualHeight / 2,
                        scale);
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "number slot not measurable");
                return (0, 0, 1);
            }
        }
    }

    // ================================================================================ 3. teletype

    /// <summary>
    /// Typed out in the pixel face, with a block cursor. The typing stops dead before the tag,
    /// holds while the cursor blinks, and then the tag snaps in whole with a one frame nudge of
    /// the strip. Under photosafe the cursor sits steady and the nudge is dropped.
    /// </summary>
    public sealed class TeletypeEffect : IMarqueeReadEffect
    {
        public string Id => MarqueeReadEffects.Teletype;

        public bool CanPlay(string body, string tag) => !string.IsNullOrWhiteSpace(body);

        private const double PerCharMs = 35;
        private const double TypeCapMs = 2300;
        private const double StopMs = 1200;
        private const double TailMs = 1000;

        public async Task PlayAsync(MarqueeReadStage stage, string body, string tag, CancellationToken ct)
        {
            var font = Services.EmiDesk.EmiFace.PixelFont;
            // Press Start 2P is an 8 x 8 cell with one weight: whole pixel sizes only, never bold.
            const double size = 12;

            var line = string.IsNullOrWhiteSpace(tag) ? body : body + " " + tag;

            var typed = new TextBlock
            {
                Text = "",
                FontFamily = font,
                FontSize = size,
                Foreground = stage.Pink,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var cursor = new TextBlock
            {
                Text = "█",
                FontFamily = font,
                FontSize = size,
                Foreground = stage.Pink,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(typed);
            row.Children.Add(cursor);

            // The strip is centred but the text is typed left to right, so the row is pinned to a
            // holder already at its finished width. Without that the whole line creeps sideways on
            // every character, which reads as a layout bug rather than a typewriter.
            var holder = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = Measure(line + "█", font, size),
            };
            holder.Children.Add(row);

            var box = new Viewbox
            {
                Child = holder,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Margin = new Thickness(14, 2, 14, 2),
                Effect = new DropShadowEffect { Color = stage.GlowColor, BlurRadius = 7, ShadowDepth = 0, Opacity = 0.75 },
            };
            stage.Host.Children.Add(box);

            if (stage.AllowFlicker) PossAnim.Oscillate(cursor, UIElement.OpacityProperty, 1, 0.05, 480);

            double perTick = Math.Max(1, Math.Ceiling(body.Length * PerCharMs / TypeCapMs));
            int step = (int)perTick;
            for (int i = 0; i < body.Length; i += step)
            {
                typed.Text = body.Substring(0, Math.Min(body.Length, i + step));
                if (!await PossAnim.DelayAsync(PerCharMs * step, ct).ConfigureAwait(true)) return;
            }
            typed.Text = body;

            if (!await PossAnim.DelayAsync(StopMs, ct).ConfigureAwait(true)) return;

            if (!string.IsNullOrWhiteSpace(tag))
            {
                typed.Text = body + " " + tag;
                if (stage.AllowFlicker) Nudge(stage);
            }

            await PossAnim.DelayAsync(TailMs, ct).ConfigureAwait(true);
            PossAnim.Settle(cursor, UIElement.OpacityProperty, 1);
        }

        /// <summary>One frame out and back. 3px, which is felt rather than seen.</summary>
        private static void Nudge(MarqueeReadStage stage)
        {
            var kf = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(180) };
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(3, KeyTime.FromPercent(0)));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), PossAnim.EaseOut));
            stage.Slide.BeginAnimation(TranslateTransform.XProperty, kf);
        }

        private static double Measure(string text, FontFamily font, double size)
        {
            try
            {
                var probe = new TextBlock { Text = text, FontFamily = font, FontSize = size };
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return Math.Max(40, probe.DesiredSize.Width);
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "teletype width probe");
                return Math.Max(40, text.Length * size * 0.62);
            }
        }
    }

    // ============================================================================ 4. neon warm-up

    /// <summary>
    /// The sign coming on. Impact caps for this act only (the reads otherwise keep the case they
    /// were written in), each word flickering up on its own random schedule, and the tag settling
    /// into a slow pink breath at the end.
    ///
    /// <para>Photosafe swaps the flicker for a plain sequential fade: same staging, no strobe.</para>
    /// </summary>
    public sealed class NeonWarmUpEffect : IMarqueeReadEffect
    {
        public string Id => MarqueeReadEffects.NeonWarmUp;

        public bool CanPlay(string body, string tag) => !string.IsNullOrWhiteSpace(body);

        private const double WarmMs = 1800;
        private const double HoldMs = 1200;
        private const double TagMs = 700;
        private const double TailMs = 1200;

        private static readonly Random Rng = new();

        public async Task PlayAsync(MarqueeReadStage stage, string body, string tag, CancellationToken ct)
        {
            var font = Services.UI.FontGuard.Family("Impact, Arial Black, Segoe UI");
            var strip = stage.BuildStrip(body, tag, font, 24, FontWeights.Bold, upper: true);
            strip.HideAll();

            int i = 0;
            int bodyCount = 0;
            foreach (var _ in strip.Body) bodyCount++;
            foreach (var w in strip.Body)
            {
                double at = bodyCount <= 1 ? 0 : WarmMs * 0.6 * i / (bodyCount - 1);
                WarmUp(stage, w.Cell, at, WarmMs * 0.4);
                i++;
            }

            if (!await PossAnim.DelayAsync(WarmMs + HoldMs, ct).ConfigureAwait(true)) return;

            ReadWord? last = null;
            foreach (var w in strip.Tag)
            {
                WarmUp(stage, w.Cell, 0, TagMs);
                last = w;
            }
            if (!await PossAnim.DelayAsync(TagMs, ct).ConfigureAwait(true)) return;

            if (last != null)
            {
                last.Block.Foreground = stage.Pink;
                Services.MotionFx.GlowBreath(last.Cell, 0.55, 1.0, 1.7);
            }

            await PossAnim.DelayAsync(TailMs, ct).ConfigureAwait(true);
            if (last != null) Services.MotionFx.Stop(last.Cell);
        }

        /// <summary>
        /// One word coming on. The flicker is a handful of discrete frames on a tube that cannot
        /// make up its mind; under photosafe (or motion Off) it is one ease instead.
        /// </summary>
        private static void WarmUp(MarqueeReadStage stage, UIElement cell, double delayMs, double ms)
        {
            if (!stage.AllowFlicker)
            {
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Max(160, ms)))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = PossAnim.EaseOut,
                    FillBehavior = FillBehavior.HoldEnd,
                };
                cell.BeginAnimation(UIElement.OpacityProperty, fade);
                return;
            }

            var kf = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                Duration = TimeSpan.FromMilliseconds(ms),
                FillBehavior = FillBehavior.HoldEnd,
            };
            kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            double at = 0.12;
            while (at < 0.82)
            {
                kf.KeyFrames.Add(new DiscreteDoubleKeyFrame(Rng.Next(100) < 55 ? 1 : 0.15, KeyTime.FromPercent(at)));
                at += 0.07 + Rng.NextDouble() * 0.1;
            }
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), PossAnim.EaseOut));
            cell.BeginAnimation(UIElement.OpacityProperty, kf);
        }
    }

    // =============================================================================== 5. redaction

    /// <summary>
    /// The line is there the whole time, under black bars. They slide off a word at a time, left to
    /// right, and the tag's bar is the last thing to go. No flicker anywhere in it, so it plays
    /// unchanged under photosafe.
    /// </summary>
    public sealed class RedactionEffect : IMarqueeReadEffect
    {
        public string Id => MarqueeReadEffects.Redaction;

        public bool CanPlay(string body, string tag) => !string.IsNullOrWhiteSpace(body);

        private const double UncoverMs = 2000;
        private const double HoldMs = 1200;
        private const double TagMs = 500;
        private const double TailMs = 900;

        public async Task PlayAsync(MarqueeReadStage stage, string body, string tag, CancellationToken ct)
        {
            var font = Helpers.FontPickerHelper.FredokaFamily;
            var strip = stage.BuildStrip(body, tag, font, 21, FontWeights.SemiBold);

            foreach (var w in strip.Words) AddBar(w);
            try { stage.Host.UpdateLayout(); }
            catch (Exception ex) { Diag.Swallowed(ex, "redaction measure"); }

            int bodyCount = 0;
            foreach (var _ in strip.Body) bodyCount++;
            double per = bodyCount > 0 ? UncoverMs / bodyCount : UncoverMs;

            foreach (var w in strip.Body)
            {
                Slide(w, Math.Max(140, per * 1.6));
                if (!await PossAnim.DelayAsync(per, ct).ConfigureAwait(true)) return;
            }

            if (!await PossAnim.DelayAsync(HoldMs, ct).ConfigureAwait(true)) return;

            foreach (var w in strip.Tag) Slide(w, TagMs);
            await PossAnim.DelayAsync(TagMs + TailMs, ct).ConfigureAwait(true);
        }

        private static void AddBar(ReadWord word)
        {
            var bar = new Rectangle
            {
                Fill = Brushes.Black,
                RadiusX = 2,
                RadiusY = 2,
                Margin = new Thickness(-2, -1, -2, -1),
                RenderTransform = new TranslateTransform(),
            };
            word.Cell.Children.Add(bar);
            word.Bar = bar;
        }

        private static void Slide(ReadWord word, double ms)
        {
            var bar = word.Bar;
            if (bar == null || bar.RenderTransform is not TranslateTransform t) return;
            double width = word.Cell.ActualWidth > 0 ? word.Cell.ActualWidth : word.Word.Length * 14;
            PossAnim.To(t, TranslateTransform.XProperty, width + 8, ms, PossAnim.EaseIn);
            PossAnim.To(bar, UIElement.OpacityProperty, 0, ms, PossAnim.EaseIn);
        }
    }
}
