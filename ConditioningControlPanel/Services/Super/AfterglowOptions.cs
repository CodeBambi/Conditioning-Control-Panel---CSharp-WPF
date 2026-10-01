using System;
using System.Globalization;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>One word of a burst: which row and column it sits in, where against the burst centre, its tilt and when it pops.</summary>
    public readonly struct AfterglowBurstSlot
    {
        /// <summary>Vertical place in lines (1 = one font size), 0 = the centre line. Includes a small jitter.</summary>
        public readonly double Line;
        /// <summary>Small sideways jitter in font sizes, so a row does not read as a table.</summary>
        public readonly double Nudge;
        /// <summary>Seconds after the first word.</summary>
        public readonly double DelayS;
        /// <summary>Row index (0 = top) and how many words share this row; Col is the place in the row, left to right.</summary>
        public readonly int Row, Col, RowSize;
        /// <summary>Slight tilt in radians, either way.</summary>
        public readonly double Tilt;

        public AfterglowBurstSlot(double line, double nudge, double delayS, int row = 0, int col = 0, int rowSize = 1, double tilt = 0)
        {
            Line = line; Nudge = nudge; DelayS = delayS; Row = row; Col = col; RowSize = rowSize; Tilt = tilt;
        }
    }

    /// <summary>
    /// Super Afterglow's own option box, the maths: frequency, burst size, word size and the two glow
    /// colours. Defaults (5, 1, 5, pink, mint) reproduce the effect as it shipped. WPF-free, App-free.
    /// The frequency is the Afterglow's own clock, never tied to the main subliminal's timer.
    /// </summary>
    public static class AfterglowOptions
    {
        public const int FrequencyMin = 1, FrequencyMax = 10, FrequencyDefault = 5;
        public const int CountMin = 1, CountMax = 4, CountDefault = 1;
        public const int SizeMin = 1, SizeMax = 10, SizeDefault = 5;
        public const int DurationMin = 1, DurationMax = 10, DurationDefault = 5;
        public const int OpacityMin = 10, OpacityMax = 100, OpacityDefault = 100;
        public const int TiltSliderMin = 0, TiltSliderMax = 10, TiltSliderDefault = 5;
        public const string ColorADefault = "#FF5FB0";   // pink (mockup palette)
        public const string ColorBDefault = "#5FFFD0";   // mint

        /// <summary>Row spacing of a burst, in font sizes: rows sit well apart so the words are spread out.</summary>
        public const double LineSpacing = 2.6;
        /// <summary>Gap between two words on one row, in font sizes.</summary>
        public const double GapFonts = 1.6;
        /// <summary>Sideways jitter, +- this many font sizes, and vertical jitter on top of the row.</summary>
        public const double NudgeSpan = 0.3, LineJitter = 0.35;
        /// <summary>Slight tilt of every burst word, +- this many radians (about 8 degrees).</summary>
        public const double TiltMax = 0.14;
        /// <summary>Up to two words share a row; more than that wraps onto a second line.</summary>
        public const int WordsPerRow = 2;
        public const double StaggerMinS = 0.05, StaggerMaxS = 0.09;

        public static int ClampFrequency(int v) => Math.Clamp(v, FrequencyMin, FrequencyMax);
        public static int ClampCount(int v) => Math.Clamp(v, CountMin, CountMax);
        public static int ClampSize(int v) => Math.Clamp(v, SizeMin, SizeMax);

        /// <summary>
        /// Interval multiplier: 1 at 5, halves every 2.5 steps up, doubles every 2.5 steps down.
        /// 10 = x0.25 (0.4 to 1.1 s), 1 = about x3 (4.5 to 13.6 s).
        /// </summary>
        public static double IntervalFactor(int frequency)
            => Math.Pow(2, (FrequencyDefault - ClampFrequency(frequency)) / 2.5);

        /// <summary>Seconds between pops: 1.5..4.5 at the default, scaled by <see cref="IntervalFactor"/>.</summary>
        public static void IntervalRange(int frequency, out double minS, out double maxS)
        {
            double f = IntervalFactor(frequency);
            minS = AfterglowField.IntervalMinS * f;
            maxS = AfterglowField.IntervalMaxS * f;
        }

        /// <summary>Seconds until the next pop. <paramref name="r01"/> in [0,1).</summary>
        public static double NextInterval(int frequency, double r01)
        {
            IntervalRange(frequency, out var lo, out var hi);
            return lo + Math.Clamp(r01, 0, 1) * (hi - lo);
        }

        /// <summary>Word font size in DIP: 1 = 20, 5 = 40 (as shipped), 10 = 90.</summary>
        public static double SizeDip(int size)
        {
            int s = ClampSize(size);
            return s <= SizeDefault ? 20 + (s - 1) * 5.0 : AfterglowField.FontDip + (s - SizeDefault) * 10.0;
        }

        /// <summary>Multiplier on everything that scales with the word (trail, sparks, offsets). 1 at 5.</summary>
        public static double SizeFactor(int size) => SizeDip(size) / AfterglowField.FontDip;

        public static int ClampDuration(int v) => Math.Clamp(v, DurationMin, DurationMax);
        public static int ClampOpacity(int v) => Math.Clamp(v, OpacityMin, OpacityMax);
        public static int ClampTilt(int v) => Math.Clamp(v, TiltSliderMin, TiltSliderMax);

        /// <summary>Tilt slider 0..10 as a multiplier on the burst words' tilt: 0 = straight, 5 = as shipped (about 8 degrees), 10 = double.</summary>
        public static double TiltFactor(int tilt) => ClampTilt(tilt) / (double)TiltSliderDefault;

        /// <summary>
        /// How long a word stays, as a multiple of the shipped 0.42 s: 1 = a third of it, 5 = as shipped,
        /// 10 = four times it (about 1.7 s). Every phase (in, hold, out) scales together.
        /// </summary>
        public static double DurationFactor(int duration) => Math.Pow(2, (ClampDuration(duration) - DurationDefault) * 0.4);

        /// <summary>Opacity slider 10..100 as a 0.1..1 multiplier on the words, echoes and sparks.</summary>
        public static double OpacityFactor(int opacity) => ClampOpacity(opacity) / 100.0;

        /// <summary>How many pops may be alive at once for this burst size.</summary>
        public static int MaxAlive(int count) => AfterglowField.MaxAlive * ClampCount(count);

        /// <summary>
        /// A burst of <paramref name="count"/> words, spread out: one word is the old single pop; two
        /// sit side by side; three or four wrap onto two lines (two per row). Each word gets a slight
        /// tilt and a little jitter, and pops 50..90 ms after the one before in a random order.
        /// </summary>
        public static AfterglowBurstSlot[] BurstPlan(int count, Func<double> rnd)
        {
            int n = ClampCount(count);
            if (n == 1) return new[] { new AfterglowBurstSlot(0, 0, 0, 0, 0, 1, 0) };

            int rows = RowsFor(n);
            var rowSize = new int[rows];
            for (int i = 0; i < n; i++) rowSize[i % rows]++;     // 3 -> 2+1, 4 -> 2+2, 2 -> 2

            // Slot places in reading order, then the pop order is a shuffle of them.
            var place = new (int Row, int Col)[n];
            int k0 = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < rowSize[r]; c++) place[k0++] = (r, c);

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = Math.Min(i, (int)(Math.Clamp(rnd(), 0, 1) * (i + 1)));
                (order[i], order[j]) = (order[j], order[i]);
            }

            var slots = new AfterglowBurstSlot[n];
            double t = 0;
            for (int k = 0; k < n; k++)
            {
                var (row, col) = place[order[k]];
                if (k > 0) t += StaggerMinS + Math.Clamp(rnd(), 0, 1) * (StaggerMaxS - StaggerMinS);
                double jitterY = (Math.Clamp(rnd(), 0, 1) * 2 - 1) * LineJitter;
                double nudge = (Math.Clamp(rnd(), 0, 1) * 2 - 1) * NudgeSpan;
                double tilt = (Math.Clamp(rnd(), 0, 1) * 2 - 1) * TiltMax;
                double line = (row - (rows - 1) / 2.0) * LineSpacing + jitterY;
                slots[k] = new AfterglowBurstSlot(line, nudge, t, row, col, rowSize[row], tilt);
            }
            return slots;
        }

        /// <summary>Rows a burst of <paramref name="count"/> words uses: 1 for one or two words, 2 beyond that.</summary>
        public static int RowsFor(int count) => ClampCount(count) <= WordsPerRow ? 1 : 2;

        /// <summary>
        /// <paramref name="count"/> indices into a pool of <paramref name="poolSize"/>: all different
        /// while the pool is big enough (partial shuffle), repeats only once it runs out. Empty pool = none.
        /// </summary>
        public static int[] PickIndices(int poolSize, int count, Func<double> rnd)
        {
            if (poolSize <= 0 || count <= 0) return Array.Empty<int>();
            var deck = new int[poolSize];
            for (int i = 0; i < poolSize; i++) deck[i] = i;
            var picks = new int[count];
            for (int k = 0; k < count; k++)
            {
                int left = poolSize - (k % poolSize);
                if (k > 0 && k % poolSize == 0)
                    for (int i = 0; i < poolSize; i++) deck[i] = i;   // pool exhausted: start a new deck
                int start = poolSize - left;
                int j = start + Math.Min(left - 1, (int)(Math.Clamp(rnd(), 0, 1) * left));
                (deck[start], deck[j]) = (deck[j], deck[start]);
                picks[k] = deck[start];
            }
            return picks;
        }

        /// <summary>Half the burst's height in font sizes (for clamping the stack on screen).</summary>
        public static double BurstHalfLines(int count) => ((RowsFor(count) - 1) * LineSpacing + 1) / 2.0 + LineJitter;

        /// <summary>
        /// "#RRGGBB" or "RRGGBB" (also "#AARRGGBB", alpha ignored) to RGB. Anything else gives
        /// <paramref name="fallbackHex"/>, which must itself parse.
        /// </summary>
        public static (byte R, byte G, byte B) ParseColor(string? hex, string fallbackHex)
        {
            if (TryParse(hex, out var c)) return c;
            TryParse(fallbackHex, out c);
            return c;
        }

        public static bool TryParse(string? hex, out (byte R, byte G, byte B) rgb)
        {
            rgb = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var s = hex.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length == 8) s = s.Substring(2);
            if (s.Length != 6) return false;
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
            rgb = ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        /// <summary>A normalised "#RRGGBB", or the default when <paramref name="hex"/> does not parse.</summary>
        public static string Normalise(string? hex, string fallbackHex)
        {
            var (r, g, b) = ParseColor(hex, fallbackHex);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        /// <summary>Spark tint: the glow colour 40% of the way to white.</summary>
        public static (byte R, byte G, byte B) SparkOf((byte R, byte G, byte B) glow)
        {
            static byte L(byte v) => (byte)Math.Round(v + (255 - v) * 0.4);
            return (L(glow.R), L(glow.G), L(glow.B));
        }
    }
}
