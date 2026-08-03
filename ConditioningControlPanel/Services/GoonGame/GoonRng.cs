using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// xoshiro256** — the deterministic PRNG both clients build from the same 64-bit match/round
    /// seed so a sudden-death round renders identically on both machines (same bubble spawns, same
    /// lock card, same flash order) with nothing but the seed crossing the wire.
    ///
    /// Why not <see cref="Random"/>: .NET's Random is explicitly not guaranteed stable across
    /// runtime versions, and .NET 6+ already changed its algorithm once. Determinism here is a
    /// correctness requirement, not a nicety — a drift means the two players see different rounds
    /// and the loser is right to complain. xoshiro256** is 20 lines, fixed forever.
    ///
    /// NOT thread-safe, and that is the point: a shared sequence consumed from two threads is
    /// non-deterministic by construction. Give each consumer its own instance from its own seed
    /// (see <see cref="Derive"/>).
    /// </summary>
    public sealed class GoonRng : IGoonRng
    {
        private ulong _s0, _s1, _s2, _s3;

        /// <summary>Seeds the four-word state by running the seed through splitmix64 (the author's
        /// recommended seeding routine — it decorrelates weak seeds like 0 or 1).</summary>
        public GoonRng(ulong seed)
        {
            Seed = seed;
            _s0 = SplitMix64(ref seed);
            _s1 = SplitMix64(ref seed);
            _s2 = SplitMix64(ref seed);
            _s3 = SplitMix64(ref seed);
        }

        /// <summary>The seed this instance was built from — handy in logs when a round desyncs.</summary>
        public ulong Seed { get; }

        public ulong NextULong()
        {
            var result = Rotl(_s1 * 5, 7) * 9;
            var t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);
            return result;
        }

        /// <summary>
        /// Uniform in [minInclusive, maxExclusive). Uses rejection sampling rather than a modulo,
        /// so the distribution is exactly flat AND the number of draws is a pure function of the
        /// stream — modulo bias would be invisible, but a biased draw count would desync the two
        /// clients the moment a range wasn't a power of two.
        /// </summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            var range = (ulong)((long)maxExclusive - minInclusive);
            var limit = ulong.MaxValue - (ulong.MaxValue % range);
            ulong draw;
            do { draw = NextULong(); } while (draw >= limit);
            return (int)((long)minInclusive + (long)(draw % range));
        }

        /// <summary>Uniform in [0,1). Top 53 bits, the standard double conversion.</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Fisher-Yates, in place. Deterministic given the same seed and the same list order.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Picks one element. Returns default for a null/empty list.</summary>
        public T? Pick<T>(IReadOnlyList<T> list) =>
            list == null || list.Count == 0 ? default : list[NextInt(0, list.Count)];

        // ------------------------------------------------------------------ seeds

        /// <summary>
        /// This client's contribution to a shared seed. Cryptographically random so the opponent
        /// can't predict it — the whole point of the XOR commit is that neither side gets to pick
        /// the round they're about to be graded on.
        /// </summary>
        public static ulong NewSeedContribution()
        {
            Span<byte> buf = stackalloc byte[8];
            RandomNumberGenerator.Fill(buf);
            return BitConverter.ToUInt64(buf);
        }

        /// <summary>
        /// The shared seed: XOR of the two contributions. Commutative on purpose — host and guest
        /// compute the same value without agreeing on an order first.
        ///
        /// Caveat the callers (Phases B and D) must respect: XOR only denies control to a player
        /// who reveals FIRST. Both sides send their contribution in the same message that schedules
        /// the round (MatchStartMsg / RoundScheduleMsg) and neither acts on the combined seed until
        /// the scheduled instant, so there is no window in which a player has seen the peer's word
        /// and can still change their own.
        /// </summary>
        public static ulong CombineSeeds(ulong a, ulong b) => a ^ b;

        /// <summary>
        /// A sub-stream for one purpose, derived from a parent seed. Lets Phase B/D hand each
        /// system (bubble layout, lock-card text, flash order) its own generator without any of
        /// them being able to disturb another's sequence by drawing a different number of values.
        /// </summary>
        public static GoonRng Derive(ulong parentSeed, string purpose)
        {
            ulong h = 0xcbf29ce484222325UL;                 // FNV-1a 64
            foreach (var c in purpose ?? string.Empty)
            {
                h ^= c;
                h *= 0x100000001b3UL;
            }
            var mixed = parentSeed ^ h;
            return new GoonRng(SplitMix64(ref mixed));
        }

        // ------------------------------------------------------------------ internals

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        private static ulong SplitMix64(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
