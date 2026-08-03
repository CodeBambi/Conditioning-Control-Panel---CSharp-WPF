using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Dumps the C# engine's deterministic output as JSON so the browser client's test suite can
    /// prove byte-for-byte parity against it. The C# side is the REFERENCE implementation: if the
    /// page and this file disagree, the page is wrong.
    ///
    /// Covers the three things a desync can hide in:
    ///   1. the PRNG itself      (<see cref="GoonRng"/> raw stream, int ranges, doubles, Derive)
    ///   2. the per-element salt (<c>GoonDraft.SaltSeed</c>, reproduced here — it is private)
    ///   3. everything built on top (all four round specs; the whole Live-phase ramp)
    ///
    /// Seed choice is deliberate: 0 and 1 catch a missing splitmix64 seeding step, the golden ratio
    /// constant and ulong.MaxValue catch 32-bit truncation, and 2^53+1 catches a JS client that
    /// carried a seed through a Number instead of a BigInt (the only value here that a double
    /// cannot represent). Every ulong is emitted as a DECIMAL STRING for the same reason.
    ///
    /// Run with <c>--goon-vectors</c>. Field names in the emitted JSON are a contract with the
    /// page's test — snake_case, and stable.
    /// </summary>
    internal static class GoonVectorDumper
    {
        /// <summary>Seeds every RNG section is generated from. See the class doc for why these.</summary>
        private static readonly ulong[] Seeds =
        {
            0UL,
            1UL,
            2UL,
            0x9E3779B97F4A7C15UL,   // 11400714819323198485 — the splitmix64 increment itself
            ulong.MaxValue,         // 18446744073709551615
            6364136223846793005UL,  // the classic LCG multiplier; also the "match seed" below
            2685821657736338717UL,
            9007199254740993UL,     // 2^53+1 — unrepresentable as a JS Number
        };

        /// <summary>Purposes fed to <see cref="GoonRng.Derive"/> — including the empty string,
        /// which a naive FNV-1a port often special-cases by accident.</summary>
        private static readonly string[] DerivePurposes = { "", "bubble", "flash", "lockcard" };

        /// <summary>The seed both the salt table and the round/ramp sections build from.</summary>
        private const ulong MatchSeed = 6364136223846793005UL;

        /// <summary>Live phase used for the ramp vector: 12 minutes, the engine's default.</summary>
        private const int RampLiveDurationSec = 720;

        /// <summary>Writes the vectors and returns the path written.</summary>
        public static string Run(string? outPath = null)
        {
            var path = outPath ?? Path.Combine(
                AppContext.BaseDirectory, "Resources", "web", "goon", "test", "rng-vectors.json");

            var root = new JObject
            {
                ["generated_utc"] = DateTime.UtcNow.ToString("o"),
                ["app_version"] = SafeAppVersion(),
                ["seeds"] = BuildSeedVectors(),
                ["derive"] = BuildDeriveVectors(),
                ["salt_seed"] = BuildSaltSeedVectors(),
                ["round_specs"] = BuildRoundSpecVectors(),
                ["ramp"] = BuildRampVector(BaselineDraft),
                ["ramps"] = BuildRampVectors(),
            };

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, root.ToString(Formatting.Indented));
            App.Logger?.Information("GoonVectorDumper: wrote parity vectors -> {Path}", path);
            return path;
        }

        private static string SafeAppVersion()
        {
            try { return UpdateService.AppVersion; }
            catch { return "unknown"; }
        }

        // ------------------------------------------------------------------ rng

        /// <summary>Every list starts from a FRESH generator: a shared instance would make each
        /// section's values depend on how many draws the previous section happened to take, which
        /// is exactly the coupling a parity test must not have.</summary>
        private static JArray BuildSeedVectors()
        {
            var arr = new JArray();
            foreach (var seed in Seeds)
            {
                var nextULong = new JArray();
                var rngA = new GoonRng(seed);
                for (int i = 0; i < 16; i++) nextULong.Add(rngA.NextULong().ToString());

                var nextInt016 = new JArray();
                var rngB = new GoonRng(seed);
                for (int i = 0; i < 8; i++) nextInt016.Add(rngB.NextInt(0, 16));

                // A non-power-of-two range: the rejection-sampling loop (and therefore the DRAW
                // COUNT) has to match, not just the values.
                var nextInt2000 = new JArray();
                var rngC = new GoonRng(seed);
                for (int i = 0; i < 8; i++) nextInt2000.Add(rngC.NextInt(2000, 6001));

                var nextDouble = new JArray();
                var rngD = new GoonRng(seed);
                for (int i = 0; i < 8; i++) nextDouble.Add(rngD.NextDouble());

                arr.Add(new JObject
                {
                    ["seed"] = seed.ToString(),
                    ["next_ulong"] = nextULong,
                    ["next_int_0_16"] = nextInt016,
                    ["next_int_2000_6001"] = nextInt2000,
                    ["next_double"] = nextDouble,
                });
            }
            return arr;
        }

        private static JArray BuildDeriveVectors()
        {
            var arr = new JArray();
            const ulong parent = 1UL;   // weak parent seed: the derive mix must still decorrelate it
            foreach (var purpose in DerivePurposes)
            {
                var rng = GoonRng.Derive(parent, purpose);
                var draws = new JArray();
                for (int i = 0; i < 4; i++) draws.Add(rng.NextULong().ToString());
                arr.Add(new JObject
                {
                    ["seed"] = parent.ToString(),
                    ["purpose"] = purpose,
                    ["first_draws"] = draws,
                });
            }
            return arr;
        }

        /// <summary>
        /// The per-element salt from <c>GoonDraft.BuildRamp</c>. Reproduced here because
        /// <c>GoonDraft.SaltSeed</c> is private — if that ever changes, this MUST change with it or
        /// the vectors quietly stop testing the real thing. (Kept private there on purpose: nothing
        /// but the ramp planner should be salting seeds.)
        /// </summary>
        private static ulong SaltSeed(ulong matchSeed, GoonElement element)
        {
            unchecked
            {
                ulong salt = (ulong)((int)element + 1) * 0x9E3779B97F4A7C15UL;
                return matchSeed ^ salt;
            }
        }

        private static JObject BuildSaltSeedVectors()
        {
            var elements = new JObject();
            foreach (GoonElement e in Enum.GetValues(typeof(GoonElement)))
                elements[((int)e).ToString()] = SaltSeed(MatchSeed, e).ToString();

            return new JObject
            {
                ["match_seed"] = MatchSeed.ToString(),
                ["elements"] = elements,
            };
        }

        // ------------------------------------------------------------ round specs

        /// <summary>All four round kinds x difficulty 1..4 x two seeds. Each spec is built from a
        /// fresh context, so the draw order inside a single BuildSpec is what is under test.</summary>
        private static JArray BuildRoundSpecVectors()
        {
            var seeds = new[] { 1UL, MatchSeed };
            var kinds = new[]
            {
                GoonRoundKind.QuickDrawLockCard,
                GoonRoundKind.StaringContest,
                GoonRoundKind.ReactionDuel,
                GoonRoundKind.BubbleRace,
            };

            var arr = new JArray();
            foreach (var kind in kinds)
                foreach (var difficulty in new[] { 1, 2, 3, 4 })
                    foreach (var seed in seeds)
                    {
                        var ctx = new GoonRoundContext
                        {
                            RoundNo = 1,
                            Difficulty = difficulty,
                            Seed = seed,
                            Rng = new GoonRng(seed),
                        };
                        arr.Add(new JObject
                        {
                            ["kind"] = (int)kind,
                            ["difficulty"] = difficulty,
                            ["seed"] = seed.ToString(),
                            ["spec"] = BuildSpecJson(kind, ctx),
                        });
                    }
            return arr;
        }

        private static JObject BuildSpecJson(GoonRoundKind kind, GoonRoundContext ctx) => kind switch
        {
            GoonRoundKind.QuickDrawLockCard => QuickDrawJson(QuickDrawRound.BuildSpec(ctx)),
            GoonRoundKind.StaringContest => StaringJson(StaringContestRound.BuildSpec(ctx)),
            GoonRoundKind.ReactionDuel => ReactionJson(ReactionDuelRound.BuildSpec(ctx)),
            GoonRoundKind.BubbleRace => BubbleRaceJson(BubbleRaceRound.BuildSpec(ctx)),
            _ => new JObject(),
        };

        private static JObject QuickDrawJson(GoonLockCardSpec s) => new()
        {
            // phrase_index is not carried on the spec — it is the draw the page must reproduce, so
            // it is recovered from the (duplicate-free) pool rather than re-rolled here.
            ["phrase_index"] = Array.IndexOf(QuickDrawRound.PhrasePool, s.Phrase),
            ["phrase"] = s.Phrase,
            ["repeats"] = s.Repeats,
            ["strict"] = s.Strict,
            ["voice"] = s.Voice,
            ["time_limit_ms"] = s.TimeLimitMs,
        };

        private static JObject StaringJson(GoonStaringContestSpec s)
        {
            var beats = new JArray();
            foreach (var b in s.Beats)
            {
                beats.Add(new JObject
                {
                    ["offset_ms"] = b.OffsetMs,
                    ["duration_ms"] = b.DurationMs,
                    ["intensity"] = b.Intensity,
                    ["norm_x"] = b.NormX,
                    ["norm_y"] = b.NormY,
                    ["scale"] = b.Scale,
                });
            }
            return new JObject
            {
                ["duration_ms"] = s.DurationMs,
                ["difficulty"] = s.Difficulty,
                ["beats"] = beats,
            };
        }

        private static JObject ReactionJson(GoonReactionDuelSpec s) => new()
        {
            ["delay_ms"] = s.DelayMs,
            ["max_response_ms"] = s.MaxResponseMs,
            ["difficulty"] = s.Difficulty,
            ["decoy_offsets_ms"] = new JArray(s.DecoyOffsetsMs.Select(o => (object)o).ToArray()),
        };

        private static JObject BubbleRaceJson(GoonBubbleRaceSpec s)
        {
            var bubbles = new JArray();
            foreach (var b in s.Bubbles)
            {
                bubbles.Add(new JObject
                {
                    ["index"] = b.Index,
                    ["norm_x"] = b.NormX,
                    ["norm_y"] = b.NormY,
                    ["scale"] = b.Scale,
                    ["spawn_offset_ms"] = b.SpawnOffsetMs,
                    ["drift_angle_deg"] = b.DriftAngleDeg,
                    ["drift_speed"] = b.DriftSpeed,
                });
            }
            return new JObject
            {
                ["count"] = s.Count,
                ["timeout_ms"] = s.TimeoutMs,
                ["difficulty"] = s.Difficulty,
                ["bubbles"] = bubbles,
            };
        }

        // ------------------------------------------------------------------ ramp

        /// <summary>One three-element draft (a sustained element, a burst element, and a second
        /// burst element with a different profile) planned over a full 12-minute live phase — the
        /// widest single check in the file: entry fractions, the sustained ramp cadence, burst
        /// duration/gap jitter, the end clamp, and the Stop-before-Start sort order.
        /// Emitted as the top-level <c>ramp</c> section, which predates <c>ramps</c> and is kept
        /// verbatim so an older gate keeps passing.</summary>
        private static readonly List<GoonElement> BaselineDraft = new()
        {
            GoonElement.Flashes,     // 0 — sustained
            GoonElement.Bubbles,     // 3 — burst
            GoonElement.LockCards,   // 4 — burst, later entry
        };

        /// <summary>
        /// Named ramp cases. Every SUSTAINED element with a distinct profile has to appear in at
        /// least one draft here or its entry fraction / intensity band is untested — that is how a
        /// new element (Spiral, 2026-08-03) gets covered without disturbing the baseline vector.
        /// The spiral case deliberately drafts Spiral ALONGSIDE BrainDrain: the two share a shape
        /// and differ only in onset and ceiling, which is exactly the pair a transcription slip
        /// would swap.
        /// </summary>
        private static readonly (string Name, List<GoonElement> Draft)[] RampCases =
        {
            ("baseline", BaselineDraft),
            ("spiral", new List<GoonElement>
            {
                GoonElement.Spiral,      // 8 — sustained, early onset, gentle ceiling
                GoonElement.BrainDrain,  // 6 — sustained, late onset, hard ceiling
                GoonElement.Videos,      // 1 — burst, so the sort order is still exercised
            }),
        };

        private static JArray BuildRampVectors()
        {
            var arr = new JArray();
            foreach (var (name, draft) in RampCases)
            {
                var o = BuildRampVector(draft);
                o["name"] = name;
                arr.Add(o);
            }
            return arr;
        }

        private static JObject BuildRampVector(List<GoonElement> draft)
        {
            var cues = GoonDraft.BuildRamp(
                draft, MatchSeed, RampLiveDurationSec, seed => new GoonRng(seed));

            var arr = new JArray();
            foreach (var c in cues)
            {
                arr.Add(new JObject
                {
                    ["element"] = (int)c.Element,
                    ["offset_ms"] = c.OffsetMs,
                    ["type"] = c.Action switch
                    {
                        GoonCueAction.Start => "start",
                        GoonCueAction.Intensity => "intensity",
                        _ => "stop",
                    },
                    ["intensity"] = c.Intensity,
                    ["duration_ms"] = c.DurationMs,
                });
            }

            return new JObject
            {
                ["seed"] = MatchSeed.ToString(),
                ["live_duration_ms"] = (long)RampLiveDurationSec * 1000L,
                ["elements"] = new JArray(draft.Select(e => (object)(int)e).ToArray()),
                ["cues"] = arr,
            };
        }
    }
}
