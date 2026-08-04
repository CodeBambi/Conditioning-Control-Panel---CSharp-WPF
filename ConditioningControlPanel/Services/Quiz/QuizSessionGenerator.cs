using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    public class SessionTextContent
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> SubliminalPhrases { get; set; } = new();
        public List<string> BouncingTextPhrases { get; set; } = new();
        public List<string> LockCardPhrases { get; set; } = new();
    }

    public static class QuizSessionGenerator
    {
        public static Session GenerateSession(int totalScore, int maxScore, string categoryId, string categoryName, SessionTextContent textContent)
        {
            var scorePercent = maxScore > 0 ? (double)totalScore / maxScore * 100 : 0;
            var difficulty = scorePercent switch
            {
                <= 25 => SessionDifficulty.Easy,
                <= 50 => SessionDifficulty.Medium,
                <= 75 => SessionDifficulty.Hard,
                _ => SessionDifficulty.Extreme
            };

            var settings = BuildSettings(difficulty, textContent);
            var phases = BuildPhases(difficulty);
            var icon = GetCategoryIcon(categoryId);

            var session = new Session
            {
                Id = Guid.NewGuid().ToString(),
                Name = !string.IsNullOrWhiteSpace(textContent.Name) ? textContent.Name : $"{categoryName} Session",
                Icon = icon,
                DurationMinutes = 60,
                IsAvailable = true,
                Difficulty = difficulty,
                BonusXP = Session.GetDifficultyXP(difficulty),
                Source = SessionSource.Custom,
                Description = !string.IsNullOrWhiteSpace(textContent.Description) ? textContent.Description : $"A personalized {difficulty} session generated from your {categoryName} quiz results.",
                Settings = settings,
                Phases = phases
            };

            return session;
        }

        // ====================================================================
        // "Graded Intake" web-core path (Agent I).
        // The decoupled web quiz emits a QuizRunResult (Resources/web/intake) that
        // carries far more than a raw score: a banded-descent peakDepth, a revealed
        // archetype route, a reward-decoupling profile, per-tag tallies, and — the
        // load-bearing bit — the VERBATIM mantras the user affirmed. This overload
        // drafts a themed CCP session from all of that. The classic score-bucket
        // GenerateSession above stays the fallback for the legacy AI quiz.
        // ====================================================================

        /// <summary>
        /// Draft a session from a completed "Graded Intake" run.
        ///   peakDepth      -&gt; base intensity / difficulty
        ///   route          -&gt; niche + archetype content theming
        ///   rewardProfile  -&gt; variable (intermittent/hydra) vs steady effects
        ///   tagTallies     -&gt; subsystem emphasis (lock cards / mind wipe / subliminals ...)
        ///   affirmedMantras-&gt; seeded VERBATIM into SubliminalPhrases + BouncingTextPhrases
        /// The channel intensities the web core already clamped in-page are independent of
        /// this draft; the values below stay inside the same normal ranges the classic quiz
        /// uses, so the drafted session still respects the user's regular session caps.
        /// </summary>
        public static Session GenerateSession(QuizRunResult run, SessionTextContent? textContent = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var niche = string.IsNullOrWhiteSpace(run.Niche) ? "bambi" : run.Niche.Trim().ToLowerInvariant();

            // Five-axis read of WHAT THE USER ACTUALLY CHOSE (Slice 1). Deterministic.
            var profile = IntakeProfiler.Profile(run);

            var difficulty = DifficultyFromDepth(run.PeakDepth);
            // A5 AUTONOMY — an inverse GATE, not a knob. A run that kept refusing the
            // compliant answer on the heat-3+ confession prompts is telling us the descent
            // outran the consent, whatever peakDepth says. Step the tier down one notch and
            // (below, in ApplyRunShaping) force lock cards off. Ignored when the axis is
            // under-sampled: bambi and drone have no confession prompts at all, so the gate
            // simply never fires on those niches rather than firing on noise.
            if (!profile.Autonomy.UnderSampled && profile.Autonomy.Value >= AutonomyClampThreshold)
                difficulty = StepDownTier(difficulty);

            var scorePercent = run.MaxScore > 0
                ? run.TotalScore / run.MaxScore * 100.0
                : Math.Clamp(run.PeakDepth, 0, 1) * 100.0;

            // Base text content: caller-supplied (AI synthesis) or deterministic per-niche.
            var content = textContent ?? GetFallbackContent(niche, scorePercent);

            // The whole point of the rework: the mantras the user affirmed become the
            // session's own words, verbatim. Merge (mantras first) into the phrase pools.
            SeedAffirmedMantras(content, run.AffirmedMantras);

            // Route-aware naming: lead with the revealed primary archetype when present.
            var nicheDisplay = NicheDisplay(niche);
            var diffPrefix = scorePercent switch
            {
                <= 25 => "Gentle",
                <= 50 => "Guided",
                <= 75 => "Intense",
                _ => "Deep"
            };
            var archetype = run.Route?.PrimaryArchetypeId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content.Name))
                content.Name = $"{diffPrefix} {nicheDisplay} Intake";

            // BUG #614: the comment above has always CLAIMED the naming was route-aware, but
            // `archetype` was only ever spent on the description. The name came from
            // GetFallbackContent, which is nothing but a score-derived prefix plus a fixed
            // per-niche noun - so every circe run that peaked in Climax was called, verbatim,
            // "Extreme Keyholding". Two runs in a row producing byte-identical names is
            // indistinguishable from no session having been drafted at all, which is exactly
            // how it was reported.
            //
            // The revealed primary archetype is the one thing that genuinely varies between
            // two runs at the same tier (five per bank: unclaimed -> hers-entirely), and it is
            // the most meaningful thing the run learned, so it goes in the name. The niche noun
            // stays in front of it so the name still says what the session IS, and the
            // difficulty prefix stays where it was. Result: "Extreme Keyholding - Hers
            // Entirely" - short enough that GetExportFileName still produces a sane filename,
            // and plain " - " rather than a colon/em-dash so nothing has to be sanitised away.
            //
            // No route revealed (or an AI-supplied name that already names the route) leaves
            // the old name untouched. Collisions are still possible - same tier AND same route,
            // or repeated runs by a settled user - which is precisely why
            // IntakeHostService.UniqueSessionPath keeps its -2/-3 suffixing.
            if (!string.IsNullOrWhiteSpace(archetype) && !string.IsNullOrWhiteSpace(content.Name))
            {
                var routeName = PrettyId(archetype);
                if (!string.IsNullOrWhiteSpace(routeName)
                    && content.Name.IndexOf(routeName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    content.Name = $"{content.Name} - {routeName}";
                }
            }

            if (string.IsNullOrWhiteSpace(content.Description))
                content.Description = $"A {difficulty} session drafted from your {nicheDisplay} Intake"
                    + (string.IsNullOrWhiteSpace(archetype) ? "." : $" — {PrettyId(archetype)} route.");

            var settings = BuildSettings(difficulty, content);
            ApplyRunShaping(settings, run, profile);

            var phases = BuildPhases(difficulty);
            var icon = GetCategoryIcon(niche);

            return new Session
            {
                Id = Guid.NewGuid().ToString(),
                Name = content.Name,
                Icon = icon,
                DurationMinutes = 60,
                IsAvailable = true,
                Difficulty = difficulty,
                BonusXP = Session.GetDifficultyXP(difficulty),
                Source = SessionSource.Custom,
                Description = content.Description,
                Settings = settings,
                Phases = phases
            };
        }

        /// <summary>A5 autonomy at or above this refuses the drafted tier one step down.
        /// Calibrated against headless runs: an all-compliant player scores 0.00, an
        /// all-refusing player 1.00, and a coin-flip player 0.62-0.66 — so the gate sits above
        /// the coin-flip band and only trips on sustained, deliberate refusal.</summary>
        private const double AutonomyClampThreshold = 0.70;

        /// <summary>One notch gentler. Easy is already the floor.</summary>
        private static SessionDifficulty StepDownTier(SessionDifficulty d) => d switch
        {
            SessionDifficulty.Extreme => SessionDifficulty.Hard,
            SessionDifficulty.Hard => SessionDifficulty.Medium,
            SessionDifficulty.Medium => SessionDifficulty.Easy,
            _ => SessionDifficulty.Easy,
        };

        /// <summary>peakDepth (0..1) -&gt; difficulty, aligned to the web core's band depth floors
        /// (Establishing 0.18, Deepening 0.42, Climax 0.72).</summary>
        private static SessionDifficulty DifficultyFromDepth(double peakDepth)
        {
            var d = Math.Clamp(peakDepth, 0, 1);
            return d switch
            {
                <= 0.20 => SessionDifficulty.Easy,
                <= 0.45 => SessionDifficulty.Medium,
                <= 0.72 => SessionDifficulty.Hard,
                _ => SessionDifficulty.Extreme
            };
        }

        private static string NicheDisplay(string niche) => niche switch
        {
            "bambi" => "Bambi",
            "drone" => "Drone",
            "sissy" => "Sissy",
            "circe" => "Circe",
            _ => string.IsNullOrEmpty(niche) ? "Intake" : char.ToUpperInvariant(niche[0]) + niche[1..]
        };

        private static string PrettyId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;
            var words = id.Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
            return string.Join(' ', words);
        }

        /// <summary>Merge the affirmed mantras VERBATIM into the subliminal, bouncing-text and
        /// lock-card pools (mantras first, case-insensitive de-dupe). These are the user's own
        /// committed words, so they are never uppercased or reworded here.</summary>
        private static void SeedAffirmedMantras(SessionTextContent content, List<string>? mantras)
        {
            if (mantras == null || mantras.Count == 0) return;

            var clean = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in mantras)
            {
                var t = m?.Trim();
                if (string.IsNullOrEmpty(t) || !seen.Add(t)) continue;
                clean.Add(t);
            }
            if (clean.Count == 0) return;

            content.SubliminalPhrases = MergeVerbatim(clean, content.SubliminalPhrases);
            content.BouncingTextPhrases = MergeVerbatim(clean, content.BouncingTextPhrases);
            // Affirmations read naturally as lock-card lines too (first-person commitments).
            content.LockCardPhrases = MergeVerbatim(clean, content.LockCardPhrases);
        }

        private static List<string> MergeVerbatim(List<string> lead, List<string> rest)
        {
            var outList = new List<string>(lead);
            var seen = new HashSet<string>(lead, StringComparer.OrdinalIgnoreCase);
            foreach (var s in rest ?? new List<string>())
            {
                var t = s?.Trim();
                if (string.IsNullOrEmpty(t) || !seen.Add(t)) continue;
                outList.Add(t);
            }
            return outList;
        }

        /// <summary>Shape the base difficulty settings by the run's reward profile and its
        /// five-axis <see cref="IntakeProfile"/>.
        ///
        /// This REPLACES the old tag-substring block, which asked <c>HasTag(tags,"obey",...)</c>
        /// — a boolean over <see cref="QuizRunResult.TagTallies"/>. That was the wrong question
        /// twice over: tallies are credited whichever way the user answered (exposure, not
        /// preference), and one sighting of a tag moved a knob exactly as far as forty. The axes
        /// answer "how hard did they lean into this, weighted by how hot the asking was".
        ///
        /// DETERMINISTIC: no dice anywhere in the draft path. Same profile in =&gt; byte-identical
        /// SessionSettings out.</summary>
        private static void ApplyRunShaping(SessionSettings s, QuizRunResult run, IntakeProfile profile)
        {
            var chase = Math.Clamp(run.RewardProfile?.ChaseMagnitude ?? 0, 0, 1);
            var chased = run.RewardProfile?.ChasedReward == true;

            if (chased && chase > 0.15)
            {
                // Variable-ratio feel: bursty, unpredictable bubbles + self-multiplying flashes.
                s.BubblesEnabled = true;
                s.BubblesIntermittent = true;
                s.FlashHydra = true;
                // Lean the flash frequency up a touch with how hard they chased. BOTH halves of
                // the ramp scale together — raising only the start is precisely how the ramp got
                // inverted in the first place (see the RAMP PAIRS note in BuildSettings).
                var chaseMul = 1.0 + 0.25 * chase;
                s.FlashPerHour = Math.Clamp((int)(s.FlashPerHour * chaseMul), 1, FlashPerHourMax);
                s.FlashPerHourEnd = Math.Clamp((int)(s.FlashPerHourEnd * chaseMul), 1, FlashPerHourMax);
            }
            else
            {
                // Steady, predictable reinforcement.
                s.BubblesIntermittent = false;
            }

            // ================================================================
            // AXIS -> KNOB TABLE.
            //
            // Every row is:  knob = clamp(tier base + round(SPAN * lean(axis)), lo, hi)
            // where lean(axis) = axis.Value - 0.5, i.e. -0.5 (total refusal) .. +0.5 (total
            // endorsement) and EXACTLY 0 when the axis is under-sampled — an under-sampled axis
            // moves nothing and the tier's own value stands. SPAN is therefore the full
            // peak-to-peak travel the axis can produce on that knob.
            //
            // The (lo,hi) clamps are the knobs' REAL ranges, taken from the AppSettings
            // property setters those values are eventually written into, not invented here:
            //   FlashFrequency      1..180   (AppSettings.FlashFrequency)
            //   FlashOpacity       10..100   (session convention; 0 would disable it)
            //   SubliminalFrequency 1..30    (AppSettings.SubliminalFrequency; sessions use 1..6)
            //   SubliminalOpacity  20..100
            //   SpiralOpacity       0..50    (AppSettings.SpiralOpacity)
            //   PinkFilter opacity  0..100
            //   LockCardFrequency   1..10    (AppSettings.LockCardFrequency; sessions use 1..4)
            //   MindWipeBaseMultiplier 1..4
            //
            //   axis            knob                       span   clamp
            //   -----------------------------------------------------------------
            //   A1 blankness    MindWipeBaseMultiplier      +-2    1..4
            //   A1 blankness    SubliminalOpacity          +-20   20..100
            //   A1 blankness    SpiralOpacity / ...End     +-8/16  0..50   (spiral on only)
            //   A2 service      LockCardEnabled            on at >=0.75 (the one ON/OFF row)
            //   A2 service      LockCardFrequency           +-2    1..4
            //   A2 service      MandatoryVideos VideosPerHour +-2  1..4    (videos on only)
            //   A3 arousal      FlashPerHour / ...End      +-30/60 1..180
            //   A3 arousal      FlashOpacity / ...End      +-15/25 10..100
            //   A4 presentation SubliminalPerMin            +-2    1..6
            //   A4 presentation PinkFilterEndOpacity       +-20    0..100
            // ================================================================
            var a1 = Lean(profile.Blankness);
            var a2 = Lean(profile.Service);
            var a3 = Lean(profile.Arousal);
            var a4 = Lean(profile.Presentation);

            // --- A1 blankness -------------------------------------------------
            s.MindWipeBaseMultiplier = Nudge(s.MindWipeBaseMultiplier, 2 * a1, 1, 4);
            s.SubliminalOpacity      = Nudge(s.SubliminalOpacity, 20 * a1, 20, 100);
            if (s.SpiralEnabled)
            {
                s.SpiralOpacity    = Nudge(s.SpiralOpacity, 8 * a1, 0, 50);
                s.SpiralOpacityEnd = Nudge(s.SpiralOpacityEnd, 16 * a1, 0, 50);
            }

            // --- A2 service ---------------------------------------------------
            // The one place an axis may switch a subsystem ON rather than scale it. The old
            // tag block did this off a single sighting of "obey"; it now takes a well-sampled
            // A2 at 0.75+, i.e. the user took the compliant option on three quarters of the
            // heat-weighted service asks. Easy/Medium ship with lock cards off, so without
            // this a service-forward shallow run could never earn them. (MindWipe, Subliminals
            // and BouncingText are already enabled on every tier, so they need no equivalent.)
            const double lockCardEnableAt = 0.75;
            if (!s.LockCardEnabled && !profile.Service.UnderSampled && profile.Service.Value >= lockCardEnableAt)
            {
                s.LockCardEnabled = true;
                s.LockCardStartMinute = 10;
                s.LockCardFrequency = 1;
            }
            if (s.LockCardEnabled)
                s.LockCardFrequency = Nudge(s.LockCardFrequency ?? 1, 2 * a2, 1, 4);
            if (s.MandatoryVideosEnabled)
                s.VideosPerHour = Nudge(s.VideosPerHour ?? 1, 2 * a2, 1, 4);

            // --- A3 arousal-forward -------------------------------------------
            s.FlashPerHour    = Nudge(s.FlashPerHour, 30 * a3, 1, FlashPerHourMax);
            s.FlashPerHourEnd = Nudge(s.FlashPerHourEnd, 60 * a3, 1, FlashPerHourMax);
            s.FlashOpacity    = Nudge(s.FlashOpacity, 15 * a3, 10, 100);
            s.FlashOpacityEnd = Nudge(s.FlashOpacityEnd, 25 * a3, 10, 100);

            // --- A4 presentation ----------------------------------------------
            s.SubliminalPerMin      = Nudge(s.SubliminalPerMin, 2 * a4, 1, 6);
            s.PinkFilterEndOpacity  = Nudge(s.PinkFilterEndOpacity, 20 * a4, 0, 100);

            // --- A5 autonomy: the second half of the inverse gate --------------
            // The tier was already stepped down in GenerateSession. Lock cards are the one
            // subsystem that physically holds the user in place, so a refusing run loses them
            // outright rather than merely getting fewer.
            if (!profile.Autonomy.UnderSampled && profile.Autonomy.Value >= AutonomyClampThreshold)
            {
                s.LockCardEnabled = false;
                s.LockCardFrequency = null;
            }

            // Ramp monotonicity is an INVARIANT of a drafted session, not a hope: every pair
            // above must still end at or above where it started after all the nudging.
            EnsureRising(s);
        }

        /// <summary>Real ceiling of the flash-frequency knob — AppSettings.FlashFrequency clamps
        /// to 1..180, so anything the draft writes above 180 is silently truncated at runtime.</summary>
        private const int FlashPerHourMax = 180;

        /// <summary>Axis -&gt; signed lean in [-0.5, +0.5]. An under-sampled axis leans 0, so the
        /// tier's base value stands untouched (fall back, never act on noise).</summary>
        private static double Lean(IntakeAxis axis) => axis.UnderSampled ? 0.0 : axis.Value - 0.5;

        /// <summary>base + round(delta), clamped. Round-half-away-from-zero so the mapping is
        /// symmetric about the neutral point and fully deterministic.</summary>
        private static int Nudge(int baseValue, double delta, int lo, int hi) =>
            Math.Clamp(baseValue + (int)Math.Round(delta, MidpointRounding.AwayFromZero), lo, hi);

        /// <summary>Guarantee every ramp pair still RISES after shaping. SessionEngine lerps
        /// start -&gt; end across the hour, so an end below its start makes the session fade out.</summary>
        private static void EnsureRising(SessionSettings s)
        {
            if (s.FlashPerHourEnd < s.FlashPerHour) s.FlashPerHourEnd = s.FlashPerHour;
            if (s.FlashOpacityEnd < s.FlashOpacity) s.FlashOpacityEnd = s.FlashOpacity;
            if (s.SpiralOpacityEnd < s.SpiralOpacity) s.SpiralOpacityEnd = s.SpiralOpacity;
            if (s.PinkFilterEndOpacity < s.PinkFilterStartOpacity) s.PinkFilterEndOpacity = s.PinkFilterStartOpacity;
            if (s.BrainDrainEndIntensity < s.BrainDrainStartIntensity) s.BrainDrainEndIntensity = s.BrainDrainStartIntensity;
        }

        /// <summary>
        /// The tier baseline. Two things changed here and both are load-bearing:
        ///
        /// NO DICE. Every numeric knob used to be passed through a Randomize() that applied
        /// +-15% at DRAFT time, so the same quiz answers produced a different session every
        /// time and nothing about a draft was reproducible or reviewable. It is gone. (Runtime
        /// jitter is a separate, deliberate thing and is untouched: SessionEngine.RandomizeStartTimes
        /// still scatters the per-feature start minutes when the session actually runs.)
        ///
        /// RAMP PAIRS. SessionEngine.UpdateRampingValues lerps start -&gt; end across the hour for
        /// FlashPerHour/FlashPerHourEnd, FlashOpacity/FlashOpacityEnd, SpiralOpacity/SpiralOpacityEnd,
        /// PinkFilterStartOpacity/PinkFilterEndOpacity and BrainDrainStartIntensity/EndIntensity.
        /// Only the pink and spiral pairs were ever set here; the flash pair was not, so it kept
        /// the SessionSettings defaults (FlashPerHourEnd = 10) and every drafted session ramped
        /// its flashes DOWN — Extreme went 450/hr to 10/hr over the hour. Both halves of every
        /// pair are now written explicitly for every tier, and every pair RISES.
        ///
        /// Flash frequencies were also re-based into the knob's real range. AppSettings.FlashFrequency
        /// clamps to 1..180, so the old Hard/Extreme starts (180 / 450) sat at or past the ceiling
        /// and the ramp could not express anything: Extreme was a flat 180/hr wall from minute
        /// zero. The tiers now climb toward that ceiling instead of starting pinned to it, which
        /// is the escalation the ramp exists to deliver.
        /// </summary>
        private static SessionSettings BuildSettings(SessionDifficulty difficulty, SessionTextContent textContent)
        {
            var s = new SessionSettings();

            switch (difficulty)
            {
                case SessionDifficulty.Easy:
                    // Flash — a barely-there trickle that roughly doubles by the end.
                    s.FlashEnabled = true;
                    s.FlashPerHour = 12;
                    s.FlashPerHourEnd = 30;
                    s.FlashOpacity = 25;
                    s.FlashOpacityEnd = 45;
                    s.FlashHydra = false;
                    s.FlashClickable = true;
                    s.FlashAudioEnabled = true;
                    // Subliminal
                    s.SubliminalEnabled = true;
                    s.SubliminalPerMin = 2;
                    s.SubliminalOpacity = 40;
                    s.SubliminalFrames = 2;
                    // Bouncing text
                    s.BouncingTextEnabled = true;
                    s.BouncingTextSpeed = 2;
                    s.BouncingTextSize = 50;
                    s.BouncingTextOpacity = 80;
                    // Pink filter
                    s.PinkFilterEnabled = true;
                    s.PinkFilterStartMinute = 15;
                    s.PinkFilterStartOpacity = 0;
                    s.PinkFilterEndOpacity = 15;
                    // Spiral
                    s.SpiralEnabled = false;
                    // Lock card
                    s.LockCardEnabled = false;
                    // Mandatory videos
                    s.MandatoryVideosEnabled = false;
                    // Bubble count
                    s.BubbleCountEnabled = false;
                    // Mind wipe
                    s.MindWipeEnabled = true;
                    s.MindWipeBaseMultiplier = 1;
                    s.MindWipeVolume = 30;
                    // Brain drain
                    s.BrainDrainEnabled = false;
                    // Bubbles
                    s.BubblesEnabled = true;
                    s.BubblesFrequency = 4;
                    s.BubblesClickable = true;
                    break;

                case SessionDifficulty.Medium:
                    // Flash — starts light, roughly doubles by the end of the hour.
                    s.FlashEnabled = true;
                    s.FlashPerHour = 38;
                    s.FlashPerHourEnd = 80;
                    s.FlashOpacity = 48;
                    s.FlashOpacityEnd = 70;
                    s.FlashHydra = false;
                    s.FlashClickable = true;
                    s.FlashAudioEnabled = true;
                    // Subliminal
                    s.SubliminalEnabled = true;
                    s.SubliminalPerMin = 3;
                    s.SubliminalOpacity = 55;
                    s.SubliminalFrames = 2;
                    // Bouncing text
                    s.BouncingTextEnabled = true;
                    s.BouncingTextSpeed = 3;
                    s.BouncingTextSize = 75;
                    s.BouncingTextOpacity = 90;
                    // Pink filter
                    s.PinkFilterEnabled = true;
                    s.PinkFilterStartMinute = 10;
                    s.PinkFilterStartOpacity = 5;
                    s.PinkFilterEndOpacity = 30;
                    // Spiral
                    s.SpiralEnabled = true;
                    s.SpiralStartMinute = 20;
                    s.SpiralOpacity = 5;
                    s.SpiralOpacityEnd = 15;
                    // Lock card
                    s.LockCardEnabled = false;
                    // Mandatory videos
                    s.MandatoryVideosEnabled = false;
                    // Bubble count
                    s.BubbleCountEnabled = false;
                    // Mind wipe
                    s.MindWipeEnabled = true;
                    s.MindWipeBaseMultiplier = 2;
                    s.MindWipeVolume = 40;
                    // Brain drain
                    s.BrainDrainEnabled = false;
                    // Bubbles
                    s.BubblesEnabled = true;
                    s.BubblesFrequency = 5;
                    s.BubblesClickable = true;
                    break;

                case SessionDifficulty.Hard:
                    // Flash — re-based off the old flat 180 (the AppSettings ceiling, which left
                    // the ramp nothing to say) to a climb that reaches it in the last stretch.
                    s.FlashEnabled = true;
                    s.FlashPerHour = 70;
                    s.FlashPerHourEnd = 150;
                    s.FlashOpacity = 70;
                    s.FlashOpacityEnd = 90;
                    s.FlashHydra = true;
                    s.FlashClickable = true;
                    s.FlashAudioEnabled = true;
                    // Subliminal
                    s.SubliminalEnabled = true;
                    s.SubliminalPerMin = 4;
                    s.SubliminalOpacity = 70;
                    s.SubliminalFrames = 3;
                    // Bouncing text
                    s.BouncingTextEnabled = true;
                    s.BouncingTextSpeed = 5;
                    s.BouncingTextSize = 100;
                    s.BouncingTextOpacity = 100;
                    // Pink filter
                    s.PinkFilterEnabled = true;
                    s.PinkFilterStartMinute = 5;
                    s.PinkFilterStartOpacity = 10;
                    s.PinkFilterEndOpacity = 45;
                    // Spiral
                    s.SpiralEnabled = true;
                    s.SpiralStartMinute = 10;
                    s.SpiralOpacity = 10;
                    s.SpiralOpacityEnd = 25;
                    // Lock card
                    s.LockCardEnabled = true;
                    s.LockCardStartMinute = 10;
                    s.LockCardFrequency = 2;
                    // Mandatory videos
                    s.MandatoryVideosEnabled = true;
                    s.MandatoryVideosStartMinute = 10;
                    s.VideosPerHour = 2;
                    // Bubble count
                    s.BubbleCountEnabled = true;
                    s.BubbleCountStartMinute = 15;
                    s.BubbleCountFrequency = 3;
                    // Mind wipe
                    s.MindWipeEnabled = true;
                    s.MindWipeBaseMultiplier = 3;
                    s.MindWipeVolume = 50;
                    // Brain drain
                    s.BrainDrainEnabled = false;
                    // Bubbles
                    s.BubblesEnabled = true;
                    s.BubblesFrequency = 6;
                    s.BubblesClickable = true;
                    break;

                case SessionDifficulty.Extreme:
                    // Flash — the old 450 was ~2.5x the 180/hr ceiling AppSettings enforces, so
                    // Extreme was a flat wall from minute zero AND ramped "down" to 10. It now
                    // opens above every other tier's peak and climbs to the true ceiling.
                    s.FlashEnabled = true;
                    s.FlashPerHour = 110;
                    s.FlashPerHourEnd = 180;
                    s.FlashOpacity = 68;
                    s.FlashOpacityEnd = 100;
                    s.FlashHydra = true;
                    s.FlashClickable = true;
                    s.FlashAudioEnabled = true;
                    // Subliminal
                    s.SubliminalEnabled = true;
                    s.SubliminalPerMin = 5;
                    s.SubliminalOpacity = 85;
                    s.SubliminalFrames = 3;
                    // Bouncing text
                    s.BouncingTextEnabled = true;
                    s.BouncingTextSpeed = 7;
                    s.BouncingTextSize = 100;
                    s.BouncingTextOpacity = 100;
                    // Pink filter
                    s.PinkFilterEnabled = true;
                    s.PinkFilterStartMinute = 0;
                    s.PinkFilterStartOpacity = 15;
                    s.PinkFilterEndOpacity = 55;
                    // Spiral
                    s.SpiralEnabled = true;
                    s.SpiralStartMinute = 0;
                    s.SpiralOpacity = 10;
                    s.SpiralOpacityEnd = 35;
                    // Lock card
                    s.LockCardEnabled = true;
                    s.LockCardStartMinute = 5;
                    s.LockCardFrequency = 3;
                    // Mandatory videos
                    s.MandatoryVideosEnabled = true;
                    s.MandatoryVideosStartMinute = 5;
                    s.VideosPerHour = 3;
                    // Bubble count
                    s.BubbleCountEnabled = true;
                    s.BubbleCountStartMinute = 5;
                    s.BubbleCountFrequency = 3;
                    // Mind wipe
                    s.MindWipeEnabled = true;
                    s.MindWipeBaseMultiplier = 3;
                    s.MindWipeVolume = 60;
                    // Brain drain
                    s.BrainDrainEnabled = true;
                    s.BrainDrainStartMinute = 40;
                    s.BrainDrainStartIntensity = 5;
                    s.BrainDrainEndIntensity = 20;
                    // Bubbles
                    s.BubblesEnabled = true;
                    s.BubblesFrequency = 8;
                    s.BubblesClickable = true;
                    break;
            }

            // Apply text content
            if (textContent.SubliminalPhrases.Count > 0)
                s.SubliminalPhrases = new List<string>(textContent.SubliminalPhrases);
            if (textContent.BouncingTextPhrases.Count > 0)
                s.BouncingTextPhrases = new List<string>(textContent.BouncingTextPhrases);
            if (textContent.LockCardPhrases.Count > 0)
                s.LockCardPhrases = new List<string>(textContent.LockCardPhrases);

            return s;
        }

        private static List<SessionPhase> BuildPhases(SessionDifficulty difficulty)
        {
            return difficulty switch
            {
                SessionDifficulty.Easy => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "Settling In", Description = "Easing into it gently." },
                    new() { StartMinute = 20, Name = "Pink Glow", Description = "The pink filter warms up." },
                    new() { StartMinute = 50, Name = "Winding Down", Description = "Gentle cool-down." }
                },
                SessionDifficulty.Medium => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "Warm Up", Description = "Getting started." },
                    new() { StartMinute = 15, Name = "Building", Description = "Intensity is growing." },
                    new() { StartMinute = 35, Name = "Intensifying", Description = "Things are getting serious." },
                    new() { StartMinute = 50, Name = "Peak", Description = "Maximum for this difficulty." }
                },
                SessionDifficulty.Hard => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "Introduction", Description = "Brace yourself." },
                    new() { StartMinute = 10, Name = "Escalation", Description = "Rapid increase in stimuli." },
                    new() { StartMinute = 25, Name = "Deep", Description = "Full immersion." },
                    new() { StartMinute = 40, Name = "Overwhelming", Description = "Relentless conditioning." },
                    new() { StartMinute = 55, Name = "Final Push", Description = "Last stretch of intensity." }
                },
                SessionDifficulty.Extreme => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "No Mercy", Description = "Maximum from the start." },
                    new() { StartMinute = 10, Name = "Spiral Down", Description = "Deeper and deeper." },
                    new() { StartMinute = 20, Name = "Breaking Point", Description = "Resistance is futile." },
                    new() { StartMinute = 35, Name = "Total Override", Description = "Complete sensory overload." },
                    new() { StartMinute = 50, Name = "Maximum Overload", Description = "Everything at once." }
                },
                _ => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "Start", Description = "Session begins." }
                }
            };
        }

        private static string GetCategoryIcon(string categoryId)
        {
            return categoryId.ToLowerInvariant() switch
            {
                "sissy" => "\uD83C\uDF80",       // ribbon
                "bambi" => "\uD83E\uDDE0",       // brain
                "circe" => "\uD83D\uDD11",       // key (keyholder / Locked)
                "obedience" => "\uD83D\uDD12",   // lock
                "mindlessness" => "\uD83C\uDF00", // cyclone
                "submission" => "\uD83D\uDC96",   // sparkling heart
                _ => "\u2728"                      // sparkles
            };
        }

        public static SessionTextContent GetFallbackContent(string categoryId, double scorePercentage)
        {
            var content = new SessionTextContent();
            var catKey = categoryId.ToLowerInvariant();

            // Difficulty-based name prefix
            var diffPrefix = scorePercentage switch
            {
                <= 25 => "Gentle",
                <= 50 => "Guided",
                <= 75 => "Intense",
                _ => "Extreme"
            };

            switch (catKey)
            {
                case "sissy":
                    content.Name = $"{diffPrefix} Feminization";
                    content.Description = "A session focused on feminine transformation and acceptance.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Good girl", "So pretty", "Embrace your femininity", "You love being girly",
                        "Pink is your color", "Such a good girl", "Feminine and obedient",
                        "You are beautiful", "Accept who you are", "Soft and pretty"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "GOOD GIRL", "SO PRETTY", "EMBRACE IT", "PINK", "FEMININE", "BEAUTIFUL"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "I am a good girl", "Pink is my favorite color", "I love being pretty",
                        "I embrace my femininity", "Good girls obey", "I am soft and feminine",
                        "Being girly makes me happy", "I accept who I am"
                    };
                    break;

                case "bambi":
                    content.Name = $"{diffPrefix} Bambi Training";
                    content.Description = "A deep conditioning session for Bambi transformation.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Bambi Sleep", "Good girl Bambi", "Bambi loves to obey", "Empty and happy",
                        "Bambi drops deeper", "No thoughts", "Bambi is a good girl",
                        "Drop for Bambi", "Bambi takes over", "Deeper and deeper"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "BAMBI SLEEP", "DROP DEEPER", "GOOD GIRL", "OBEY", "EMPTY", "NO THOUGHTS"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "Bambi is a good girl", "I love to drop deeper", "Bambi obeys",
                        "Empty and happy", "Good girls drop", "Bambi takes over",
                        "No thoughts just Bambi", "I love being Bambi"
                    };
                    break;

                // The Locked / Circe niche (banks/circe.json). Its voice is the odd one out:
                // formal, quiet, second-person, and always about HER - permission, denial, the
                // key, being kept. No "good girl", no giggle. Matches the bank's own archetypes
                // (unclaimed / held-at-the-edge / locked-beta / kept-property / hers-entirely)
                // and the icon GetCategoryIcon already assigns circe (the key).
                case "circe":
                    content.Name = $"{diffPrefix} Keyholding";
                    content.Description = "A session about permission, denial, and belonging to her.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "She holds the key", "Ask, then accept", "Denial is devotion", "You are kept",
                        "Her word is the only quiet", "Wait to be told", "It was never yours",
                        "Locked and grateful", "Serve without asking twice", "Hers entirely"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "HERS", "ASK FIRST", "DENIED", "KEPT", "LOCKED", "WAIT"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "The key was never mine", "I ask, and I accept the answer",
                        "Denial sharpens my devotion", "I am kept, not borrowed",
                        "I wait to be told", "Her permission comes before my wanting",
                        "I serve without asking twice", "I set no term. She does"
                    };
                    break;

                case "obedience":
                    content.Name = $"{diffPrefix} Obedience Training";
                    content.Description = "A session designed to reinforce obedience and compliance.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Obey", "Submit", "Good girls listen", "Follow instructions",
                        "Compliance is bliss", "Do as you're told", "Obedience is pleasure",
                        "Listen and obey", "You love to comply", "Surrender control"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "OBEY", "SUBMIT", "COMPLY", "LISTEN", "FOLLOW", "SURRENDER"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "I love to obey", "Good girls follow instructions", "Obedience is pleasure",
                        "I submit willingly", "Compliance makes me happy", "I do as I am told",
                        "Listening is easy", "I surrender control"
                    };
                    break;

                case "mindlessness":
                    content.Name = $"{diffPrefix} Mindless Bliss";
                    content.Description = "A session focused on emptying the mind and embracing bliss.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Empty mind", "No thoughts", "Blank and happy", "Don't think",
                        "Mindless bliss", "Let go", "Drift away", "So empty",
                        "Thinking is hard", "Just float"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "EMPTY", "BLANK", "NO THOUGHTS", "DRIFT", "FLOAT", "MINDLESS"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "My mind is empty", "Thinking is so hard", "I love being blank",
                        "No thoughts just bliss", "Empty heads are happy heads", "I drift away so easily",
                        "Mindless is happy", "I let my thoughts go"
                    };
                    break;

                case "submission":
                    content.Name = $"{diffPrefix} Submission";
                    content.Description = "A session to deepen submission and surrender.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Submit", "Surrender", "Give in", "You are owned",
                        "Good girls submit", "Let go of control", "Deeper submission",
                        "You belong", "Surrender completely", "Submit and feel bliss"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "SUBMIT", "SURRENDER", "GIVE IN", "OWNED", "DEEPER", "BELONG"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "I submit willingly", "Surrender feels so good", "I give in completely",
                        "I belong", "Good girls surrender", "I let go of control",
                        "Submission is bliss", "I am deeply submissive"
                    };
                    break;

                default:
                    content.Name = $"{diffPrefix} Conditioning";
                    content.Description = "A personalized conditioning session.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Good girl", "Obey", "Submit", "Let go", "Deeper",
                        "Surrender", "Empty mind", "So pretty", "Accept it", "Drift away"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "OBEY", "SUBMIT", "DEEPER", "GOOD GIRL", "LET GO", "SURRENDER"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "I am a good girl", "I love to obey", "Surrender feels good",
                        "I submit willingly", "Empty and happy", "Good girls listen",
                        "I let go of control", "Deeper and deeper"
                    };
                    break;
            }

            return content;
        }
    }
}
