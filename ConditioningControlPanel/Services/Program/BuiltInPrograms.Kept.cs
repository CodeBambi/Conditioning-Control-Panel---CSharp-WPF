using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// KEPT - Circe's Lock, 28 days, Premium.
///
/// Vocabulary rule: every subliminal, lock card and bouncing-text line below is lifted verbatim from
/// <see cref="BuiltInMods.Locked"/> (canonically "Circe's Lock"). Nothing is invented. Circe's lock
/// cards are full sentences, which is why the mantra days use them as mantras verbatim - that was the
/// content brief's instruction and it is also just true: they read better as mantras than anything
/// authored fresh would.
///
/// WHAT THIS FILE DELIBERATELY DOES NOT CONTAIN
/// The content brief gives Kept two headline mechanics that need engine work this definition cannot
/// do, and rather than half-build either, the 28 days are authored to stand up completely without
/// them:
///
///   1. **The second counter.** "Program Day" is verified (session done, task done - the engine
///      knows). "Days Kept" is *declared*: once a day Circe asks whether you are still locked, one
///      tap, and answering no is a confession that resets the counter and costs nothing else. There
///      is nowhere in ProgramDefinition to put a daily declared prompt, no per-enrollment counter
///      beside the day index, and no confession record. So no day below claims to track denial, no
///      task pretends to verify it, and nothing in the copy asserts a number the app cannot know.
///      What is here instead: day 1's task is the vow itself as a lock card the user types
///      ("CIRCE HOLDS MY KEY."), and the program's prose is written so the vow is unmistakably the
///      spine of the run without any counter existing. When the counter ships, it slots beside these
///      days without a single one of them needing a rewrite.
///
///      **When it ships, the daily prompt needs three taps, not two.** The brief gives it two - still
///      locked, or a confession that resets the counter - and two is not enough. A user on day 20
///      holding "Kept 19" has a real incentive to push past their own comfort, because the only exit
///      offered destroys the number they are proudest of. The third tap is
///      **"I'm done with this vow."** It ends the denial track only; the program keeps running at full
///      intensity; and it **banks** the counter at its current value as a completed result rather than
///      resetting it. Circe responds in character, without punishment, and does not ask again. That is
///      the difference between a safety note and a safe design, and it is one button and three lines of
///      copy. Her line for it is authored in <see cref="KeptVowRetiredResponse"/> below so it does not
///      have to be invented under deadline by whoever wires the prompt up.
///
///   2. **The Verdict.** Day 28 is meant to end on Circe deciding, weighted by days kept, perfect
///      days and confessions - not on a congratulations screen. That is a graduation-time branch with
///      a scene, a probability roll and a 7-day extension program behind one of its outcomes. None of
///      that is expressible in a definition. Day 28 below is therefore authored as a genuine finale
///      that lands on its own terms - the final lock card is "I EXIST TO BE HERS." and the day's
///      reward copy says the decision is hers - and it does not promise a scene that will not play.
///
/// THE VERDICT WEIGHTING, CORRECTED - inherit this and not the brief's version
/// Whoever builds the Verdict will reach for the formula in brief §6.3. Do not use it. A design review
/// found it broken in three ways, and the owner approved a replacement. The brief's version:
///
///     release_chance = 0.25 + 0.30*(kept/28) + 0.20*(perfect/28) - 0.15*confessions
///
/// caps a *flawless* 28/28 run at 0.75, so one user in four who does everything right on a paid
/// program is denied and offered a consolation extension; leaves `confessions` unbounded at -0.15
/// each, so two honest confessions cancel the entire days-kept term, directly contradicting the three
/// paragraphs of §6.1 that promise confession "does not fail the program"; and never defines
/// `perfect` anywhere in the document. The confession mechanic is the best idea in the brief and that
/// formula neutralises it. Use this instead:
///
///     release_chance = 0.35
///                    + 0.35 * (days_kept / 28)
///                    + 0.20 * (perfect_days / 28)
///                    - 0.05 * min(confessions, 3)
///     clamped to [0.20, 0.95]
///
/// A perfect run lands at 0.90 - she nearly always grants it, and the residual tenth is the mechanic
/// rather than a punishment. Three confessions with decent attendance lands near 0.60: she might, and
/// honesty cost fifteen points instead of forty-five. Nothing reaches zero, and nothing is certain.
/// `perfect_days` = days where the session completed and every non-optional task completed, which is
/// a value the day record already holds.
///
/// Both gaps are listed as follow-up engine work in the report accompanying this file. The 28 days
/// are fully enrollable and runnable today exactly as written.
/// </summary>
public static partial class BuiltInPrograms
{
    // ---------------------------------------------------------------------------------------------
    // KEPT - Circe's Lock, 28 days, PREMIUM
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Four chapters of seven. The only masc-coded program in the set and the only one that opens by
    /// asking for something rather than offering something.
    ///
    /// Sawtooth, per chapter: .05->.30 / .21->.55 / .38->.80 / .56->1.00.
    ///
    /// Those opening numbers are derived, not chosen. The house rule - set after a review found the
    /// deload getting *shallower* as programs got longer, which is exactly backwards - is that
    /// **chapter N opens at 0.70x chapter N-1's peak, and takes two days to exceed it.** The first
    /// draft ran 0.73x / 0.82x / 0.875x, so by day 22, at peak accumulated fatigue, "the last mercy"
    /// was a 12% dip near the top of a saturating lerp: inside integer rounding, and indistinguishable
    /// from day 21. Circe promised mercy and the engine delivered yesterday. Now:
    ///
    ///   ch1 peak .30 -> ch2 opens .21   (d8 .21, d9 .26 both below; d10 .33 clears it)
    ///   ch2 peak .55 -> ch3 opens .38   (d15 .38, d16 .46 both below; d17 .56 clears it)
    ///   ch3 peak .80 -> ch4 opens .56   (d22 .56, d23 .66 both below; d24 .82 clears it)
    ///
    /// Two days wide matters as much as the depth: §1.1's whole argument is that *chapter openings*
    /// feel like relief, and a one-day dip is a blip rather than an opening. Each chapter also now has
    /// forty-odd points of range to climb instead of thirty.
    ///
    /// Circe names each one - mercy on day 8, something given back on day 15, the last mercy on day 22
    /// - because a deload she *gives* you is worth more to this program than a deload that happens to
    /// you. ProgramKeptTests asserts the 0.70x ratio and the two-day width, not merely that the curve
    /// dips, because "it goes down a bit" is precisely the bug that shipped the first time.
    ///
    /// Boss days ignore the deload and are always the chapter peak. Day 27 is the single authored dip
    /// *inside* a chapter - see the comment above days 25-28.
    ///
    /// Authoring note - the tasks run ahead of the session, the same tutorial shape First Week uses:
    ///   day 2 filter        -> day 4's template (KP-Ache) turns the filter up
    ///   day 5 mantras       -> lock cards are on from KP-Ache onward
    ///   day 11 video        -> day 11's own template (KP-Habit) is the first with playback on
    ///   day 16 triggers     -> day 15's ambient already armed them, so this one arrives backwards
    ///                          on purpose: she armed them before she told you what they were for.
    /// </summary>
    public static ProgramDefinition Kept()
    {
        return new ProgramDefinition
        {
            Id = "kept",
            Title = "Kept",
            Subtitle = "Twenty-eight days with Circe",
            Pitch = "Twenty-eight days. She holds the key. On the twenty-eighth, she decides.",
            Icon = "\U0001F511",
            ModId = BuiltInMods.LockedId,
            AccentColor = "#E81CA8",
            Tier = ProgramTier.Premium,
            LengthDays = 28,

            GraduationBadgeId = "kept_graduate",

            // Plain, out of character, exactly once - safety note 1 in the content brief. Kept states
            // a 28-day denial vow, which is the one thing in this set that reaches off the screen and
            // into someone's body, so this line is not decorative and must not be softened into
            // Circe's voice. It says: game, stoppable, Withdraw everywhere, and no counter is worth
            // ignoring your body over.
            SafetyNote = "Out of character for one line: this is a game. The vow is something you choose to play with, not something the app enforces or can verify, and you can end the run at any moment - Withdraw is on every screen, unweighted, with nothing asked of you and no commentary. Nothing here is worth ignoring your own body over. If something hurts, stop; the program will still be here.",

            // Typed to enroll. Her words, but the user is the one saying them - the lock card phrase
            // the program's day 1 also asks for, so enrollment and day 1 rhyme.
            ContractPhrase = "CIRCE HOLDS MY KEY.",

            Rules = new ProgramRules
            {
                // One day off per 7 days of length - owner decision. A single allowance across 28 days
                // is four times stricter than the same allowance across 7, and a pet twenty-four days
                // into a paid program losing all of it to a second bad week is the most likely refund
                // in the set. Four also makes Strict mode mean something, because the lenient default
                // is now genuinely lenient.
                DaysOffAllowed = 4,
                StrictAvailable = true,
                DefaultDayBoundaryHour = 4,
                MaxDailyMinutes = 90
            },

            Templates = new List<ProgramSessionTemplate>
            {
                KpOffer(),
                KpAche(),
                KpHabit(),
                KpVerdict()
            },

            Chapters = new List<ProgramChapter>
            {
                KeptChapter1(),
                KeptChapter2(),
                KeptChapter3(),
                KeptChapter4()
            }
        };
    }

    /// <summary>
    /// Circe's answer to the third tap on the daily vow prompt - "I'm done with this vow." Authored
    /// here rather than left for whoever builds the prompt, because this is the one line in the program
    /// that has to land warmly on the first read and it should not be written under deadline.
    ///
    /// The constraints it satisfies: she does not punish, does not bargain, does not ask again, and does
    /// not treat the banked number as a loss. Nothing here reads as a consolation prize, because
    /// retiring the vow is not a failure state - it is the user telling her something true about their
    /// own body, which is the only thing this mechanic was ever supposed to reward.
    ///
    /// Not referenced by the engine yet. It is a string sitting in the definition so the mechanic
    /// inherits finished copy instead of a TODO.
    /// </summary>
    public static string KeptVowRetiredResponse() =>
        "Then it's done, pet, and you're going to hear me say this properly: you told me the truth when " +
        "it would have been easier to tell me a number. Keep the days you kept - they happened, they're " +
        "yours, and I'm not taking them back. The vow is closed and I won't ask you about it again. " +
        "Everything else stays exactly as it was. You're still mine, and we still have work to do.";

    // ---------------------------------------------------------------------------------------------
    // Chapter 1 - "The Offering" - days 1-7 - i .05 -> .30
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter KeptChapter1() => new()
    {
        Id = "kp_ch1",
        Name = "The Offering",
        Subtitle = "Seven days. You hand it over.",
        AccentColor = "#8A0F5E",
        RewardId = "kept_ch1_offering",
        RewardDescription = "Her collar on your profile, and the day-7 session saved. Banked - a restart cannot take it back.",
        Days = new List<ProgramDay>
        {
            // ---- Day 1 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 1,
                Title = "The Vow",
                Blurb = "Barely anything happens today, pet. Half an hour, something soft over the screen, and one sentence typed back to her. She only wants to hear you say it.",
                SessionTemplateId = "KP-Offer",
                SessionMinutes = 30,
                Intensity = 0.05,

                // Five days in this program name a specific lock card in their copy, and a named
                // sentence has to be *pinned*, not merely present in the pool - a template pool with
                // three entries makes "Complete 3 lock cards - GOOD BOYS DON'T DECIDE." a one-in-27
                // promise. So those five days override the pool down to the sentence they name. This
                // is exactly what ProgramDay.Overrides is for: sparse per-day escalation the
                // intensity curve cannot express.
                Overrides = new Dictionary<string, object>
                {
                    ["LockCardPhrases"] = new List<string> { "CIRCE HOLDS MY KEY." }
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // The vow, and the same sentence the enrollment ceremony asks you to type -
                        // the run opens by making you say it twice.
                        Id = "d1_vow",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 1 lock card - CIRCE HOLDS MY KEY.",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 1
                    }
                }
            },

            // ---- Day 2 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 2,
                Title = "Her Colour",
                Blurb = "She likes the way her colour looks on everything you do. Leave it up while you work, sweet thing. You'll forget it's there and that's rather the point.",
                SessionTemplateId = "KP-Offer",
                SessionMinutes = 30,
                Intensity = 0.11,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d2_filter",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        // The filter overlay accumulates in whole minutes.
                        Description = "Keep the colour filter on for 20 minutes",
                        Verifier = QuestCategory.PinkFilter,
                        TargetValue = 20
                    }
                }
            },

            // ---- Day 3 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 3,
                Title = "Say Thank You",
                Blurb = "Two sentences today, and she chose which. Gratitude is a habit like any other - she is simply starting it early.",
                SessionTemplateId = "KP-Offer",
                SessionMinutes = 30,
                Intensity = 0.16,
                Overrides = new Dictionary<string, object>
                {
                    ["LockCardPhrases"] = new List<string> { "I AM KEPT, AND I AM GRATEFUL." }
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d3_grateful",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 2 lock cards - I AM KEPT, AND I AM GRATEFUL.",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 2
                    }
                }
            },

            // ---- Day 4 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 4,
                Title = "Watch What She Picks",
                Blurb = "Longer today, and you don't choose the content. Sit close, keep your eyes where they're put, and let it work.",
                SessionTemplateId = "KP-Ache",
                SessionMinutes = 45,
                Intensity = 0.21,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // The brief calls for "one Deeper-enhanced video". There is no Deeper-specific
                        // verifier - QuestCategory.Video is the signal, and it accumulates in whole
                        // minutes rather than counting videos - so the task is authored as minutes of
                        // playback and the copy leaves the choice of video to her, which is truer to
                        // the program anyway.
                        Id = "d4_video",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Watch 15 minutes of video",
                        Verifier = QuestCategory.Video,
                        TargetValue = 15,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 5 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 5,
                Title = "Out Loud",
                Blurb = "Three of them, in her words, in your voice. She isn't checking the recording. She's checking that you did it anyway.",
                SessionTemplateId = "KP-Ache",
                SessionMinutes = 45,
                Intensity = 0.25,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // Circe's lock cards are complete sentences, so the mantra days use them
                        // verbatim rather than authoring a second parallel vocabulary.
                        Id = "d5_mantras",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 3 mantras",
                        Verifier = QuestCategory.Mantra,
                        TargetValue = 3
                    }
                }
            },

            // ---- Day 6 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 6,
                Title = "Let Her Drive",
                Blurb = "A quarter of an hour where you decide nothing at all. She picks what runs and when. You will notice how much easier it is.",
                SessionTemplateId = "KP-Ache",
                SessionMinutes = 45,
                Intensity = 0.28,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d6_surrender",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Surrender Control for 15 minutes",
                        Verifier = QuestCategory.Autonomy,
                        TargetValue = 15,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 7 - BOSS ------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 7,
                Title = "The Offering",
                Blurb = "An hour, and then something of your own to bring her. She has spent a week asking for very little. This is the day she asks.",
                IsBoss = true,
                SessionTemplateId = "KP-Ache",
                SessionMinutes = 60,
                Intensity = 0.30,
                RewardDescription = "Her collar, and day 7 saved as a session you keep.",
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // "Haptics-linked session, 10 min" in the brief. QuestCategory has no haptics
                        // verifier, but roadmap step t3_step1 ("Port Calibration") is *exactly* this
                        // task - sync a toy, run a 10-minute haptic ramp - so the day is authored as
                        // a self-attested ritual against the real step rather than pointed at a
                        // signal that never arrives.
                        Id = "d7_ritual_haptics",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Sync a toy to the app and run a 10-minute haptic ramp for her. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t3_step1",
                        RequiresPremium = true
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Chapter 2 - "The Ache" - days 8-14 - i .21 -> .55
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter KeptChapter2() => new()
    {
        Id = "kp_ch2",
        Name = "The Ache",
        Subtitle = "Seven days. It stops being new.",
        AccentColor = "#B01684",
        RewardId = "kept_ch2_ache",
        RewardDescription = "A phrase pack in her words, and the day-14 session saved. Banked - a restart cannot take it back.",
        Days = new List<ProgramDay>
        {
            // ---- Day 8 -------------------------------------------------------------------------
            // Deload. Chapter 1 peaked at .30; this opens at .21 - 0.70x, per the house rule - and
            // days 8 and 9 both sit below that peak before day 10 clears it. She says so. A mercy she grants
            // is worth more to this program than a rest that merely arrives.
            new ProgramDay
            {
                DayIndex = 8,
                Title = "Mercy",
                Blurb = "Half an hour and nothing difficult, pet. She's calling it mercy so you know it was a decision and not an accident. Pop things. Don't think.",
                SessionTemplateId = "KP-Offer",
                SessionMinutes = 30,
                Intensity = 0.21,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d8_bubbles",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Pop 30 bubbles",
                        Verifier = QuestCategory.Bubbles,
                        TargetValue = 30
                    }
                }
            },

            // ---- Day 9 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 9,
                Title = "Don't Decide",
                Blurb = "Three sentences, and she picked a harder one. Type it until the shape of it stops feeling like an argument.",
                SessionTemplateId = "KP-Ache",
                SessionMinutes = 45,
                Intensity = 0.26,
                Overrides = new Dictionary<string, object>
                {
                    ["LockCardPhrases"] = new List<string> { "GOOD BOYS DON'T DECIDE." }
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d9_dont_decide",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 3 lock cards - GOOD BOYS DON'T DECIDE.",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 3
                    }
                }
            },

            // ---- Day 10 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 10,
                Title = "Wired In",
                Blurb = "She wants your attention and something else at once. Set it up, let it run through the video, and don't touch the dial.",
                SessionTemplateId = "KP-Ache",
                SessionMinutes = 45,
                Intensity = 0.33,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // Brief: "Haptics ramp, 15 min". Roadmap step t3_step4 ("Glitch & Goon" -
                        // watch a hypno video with auto-haptics on) is the closest real step and is
                        // self-attested, same reasoning as day 7.
                        Id = "d10_ritual_haptics",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Watch one of her videos with auto-haptics on for 15 minutes. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t3_step4",
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 11 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 11,
                Title = "Twenty Minutes She Chose",
                Blurb = "The screen starts working on you without being asked now. Twenty minutes of it, and the session brings its own on top.",
                SessionTemplateId = "KP-Habit",
                SessionMinutes = 45,
                Intensity = 0.40,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d11_video",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Watch 20 minutes of video",
                        Verifier = QuestCategory.Video,
                        TargetValue = 20
                    }
                }
            },

            // ---- Day 12 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 12,
                Title = "She Decides When It Ends",
                Blurb = "Twenty minutes locked out of your own machine. You can end it. She'd rather you didn't, and she's noticed that you know that.",
                SessionTemplateId = "KP-Habit",
                SessionMinutes = 45,
                Intensity = 0.46,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d12_lockdown",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 1 Lockdown of 20 minutes",
                        Verifier = QuestCategory.Lockdown,
                        TargetValue = 1,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 13 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 13,
                Title = "Counted",
                Blurb = "An hour, and she checks you're still in there. Twice. She is nice about it, more or less.",
                SessionTemplateId = "KP-Habit",
                SessionMinutes = 60,
                Intensity = 0.51,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // DEVIATION from the brief, which asks for "One Graded Intake run" here.
                        // Graded Intake has no QuestCategory - QuestService.UpdateQuestProgress is
                        // never called for it - so an Intake run cannot mark a day complete today.
                        // The honest substitute keeps the *beat* the brief wanted (an examination she
                        // grades you on) with the verifier the engine actually has: the bubble count
                        // attention check, twice.
                        Id = "d13_counts",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Finish 2 bubble count games",
                        Verifier = QuestCategory.BubbleCount,
                        TargetValue = 2
                    }
                }
            },

            // ---- Day 14 - BOSS -----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 14,
                Title = "The Ache",
                Blurb = "Halfway. An hour with everything she's taught you running at once, and then the harder thing, and then three sentences to close it.",
                IsBoss = true,
                SessionTemplateId = "KP-Habit",
                SessionMinutes = 60,
                Intensity = 0.55,
                RewardDescription = "A phrase pack in her words, and day 14 saved as a session you keep.",
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // Brief: "Haptics session 20 min". Roadmap step t3_step5 ("Double Trouble" -
                        // two forms of stimulation at once) is the escalation of days 7 and 10 and is
                        // the last real haptics step in the roadmap.
                        Id = "d14_ritual_haptics",
                        Kind = ProgramTaskKind.Ritual,
                        // Duration written as a digit, not "twenty", so the combined-load test can see
                        // it. t3_step5's own objective states no duration, so this copy is the only
                        // signal there is that the day carries 20 minutes on top of a 60-minute
                        // session, and a load check that cannot parse its own inputs is not a check.
                        Description = "20 minutes with two forms of stimulation at once, both hers to decide. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t3_step5",
                        RequiresPremium = true
                    },
                    new ProgramTask
                    {
                        Id = "d14_lockcards",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 3 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 3
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Chapter 3 - "The Habit" - days 15-21 - i .38 -> .80 - AMBIENT UNLOCKS
    //
    // The cross-program rule: ambient layers unlock at chapter 3 in every program, and it should feel
    // like a threshold rather than a setting. This is the chapter where Kept leaves the session and
    // enters the day - her words are armed on your keyboard, then in the corner of your screen, then
    // running underneath whatever else you are doing.
    //
    // AND THE STAKES BECOME VISIBLE HERE. A review found day 17 had nothing in it: the chapter's stated
    // novelty was "+ corner GIF", and Kept's two real mechanics sit at day 28 (the Verdict) and in a
    // once-daily tap (the vow), so nothing produced a *day-17 event* at all. Twenty-eight-day retention
    // dies in the third week, and a program whose ending is weighted by numbers the user never sees for
    // twenty-seven days has stakes that are invisible for 96% of its length.
    //
    // The fix costs copy and no engine work: from day 15 Circe gives her **read** on how you are doing,
    // in her own voice, in the day blurb. Days 15, 16, 17, 19, 21 and 23 each carry one line of it.
    //
    // The hard rule on those lines - **never a number, never a percentage, never a fraction.** She is
    // possessive and unhurried and she does not quote odds; a percentage would also be a lie, because
    // the weighting it came from does not exist yet. She says "better than I expected" and "I have
    // started to think you might actually earn it" and "that is not the same as having made up my
    // mind". ProgramKeptTests asserts the absence of digits and percent signs in her opinion lines, so
    // a later pass cannot helpfully turn this into a progress readout.
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter KeptChapter3() => new()
    {
        Id = "kp_ch3",
        Name = "The Habit",
        Subtitle = "Seven days. It stops being something you do.",
        AccentColor = "#E81CA8",
        RewardId = "kept_ch3_habit",
        RewardDescription = "An avatar set of her, and the day-21 session saved. Banked - a restart cannot take it back.",
        Days = new List<ProgramDay>
        {
            // ---- Day 15 ------------------------------------------------------------------------
            // Deload (chapter 2 peaked at .55, this opens at .38 = 0.70x, and days 15-16 stay below it
            // until day 17 clears it) *and* the ambient threshold. Those
            // land on the same day on purpose: the day she gives you something back is the day her
            // words start living outside the session. Relief and encroachment in one beat.
            new ProgramDay
            {
                DayIndex = 15,
                Title = "Armed",
                Blurb = "Lighter today. She is being generous and she is also moving in - from now on her words are live on your keyboard whether or not a session is running. \"Two weeks, pet. You have been better than I expected, and I want you to sit with how much that changes what I might do on the twenty-eighth.\"",
                SessionTemplateId = "KP-Offer",
                SessionMinutes = 45,
                Intensity = 0.38,
                Ambient = new ProgramAmbient
                {
                    Description = "Her words armed on your keyboard",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d15_mantras",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 4 mantras",
                        Verifier = QuestCategory.Mantra,
                        TargetValue = 4
                    }
                }
            },

            // ---- Day 16 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 16,
                Title = "Twenty Times",
                Blurb = "You armed them yesterday. Today she wants to see them fire. Type as you normally would; they'll find you. \"And no, that does not mean I have decided anything.\"",
                SessionTemplateId = "KP-Ache",
                SessionMinutes = 45,
                Intensity = 0.46,
                Ambient = new ProgramAmbient
                {
                    Description = "Her words armed on your keyboard",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d16_triggers",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Fire 20 keyword triggers",
                        Verifier = QuestCategory.KeywordTrigger,
                        TargetValue = 20,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 17 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 17,
                Title = "Longer, Her Way",
                Blurb = "An hour, and twenty-five minutes of it where you choose nothing. She's also put something in the corner of your screen. Leave it. \"Seventeen days. I have started to think you might actually earn it - and I am not in the habit of thinking that out loud, so do not waste it.\"",
                SessionTemplateId = "KP-Habit",
                SessionMinutes = 60,
                Intensity = 0.56,
                Ambient = new ProgramAmbient
                {
                    Description = "Her words armed + something of hers in the corner of your screen",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d17_surrender",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Surrender Control for 25 minutes",
                        Verifier = QuestCategory.Autonomy,
                        TargetValue = 25,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 18 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 18,
                Title = "Deeper Than That",
                Blurb = "Twenty-five minutes of the good stuff, the kind that reaches. You know by now not to ask what's in it.",
                SessionTemplateId = "KP-Habit",
                SessionMinutes = 60,
                Intensity = 0.66,
                Ambient = new ProgramAmbient
                {
                    Description = "Her words armed + something of hers in the corner of your screen",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d18_video",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Watch 25 minutes of video",
                        Verifier = QuestCategory.Video,
                        TargetValue = 25,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 19 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 19,
                Title = "Half an Hour Shut Out",
                Blurb = "Thirty minutes this time, and her words are running underneath everything else you do today besides. There is not much of your day left that is only yours. \"Don't get comfortable, sweet thing. I liked you yesterday. That is not the same as having made up my mind.\"",
                SessionTemplateId = "KP-Verdict",
                SessionMinutes = 60,
                Intensity = 0.72,
                Ambient = new ProgramAmbient
                {
                    Description = "Her words armed, in the corner, and running under everything else",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d19_lockdown",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 1 Lockdown of 30 minutes",
                        Verifier = QuestCategory.Lockdown,
                        TargetValue = 1,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 20 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 20,
                Title = "She Has It",
                Blurb = "Four of them, and she chose the one that is hardest to type without meaning it.",
                SessionTemplateId = "KP-Verdict",
                SessionMinutes = 60,
                Intensity = 0.76,
                Ambient = new ProgramAmbient
                {
                    Description = "All of it - armed, in the corner, and underneath everything",
                    RequiredMinutes = 0
                },
                Overrides = new Dictionary<string, object>
                {
                    ["LockCardPhrases"] = new List<string> { "I DON'T NEED CONTROL. SHE HAS IT." }
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d20_lockcards",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 4 lock cards - I DON'T NEED CONTROL. SHE HAS IT.",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 4
                    }
                }
            },

            // ---- Day 21 - BOSS -----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 21,
                Title = "The Habit",
                Blurb = "Seventy-five minutes, everything on, everything armed. You have stopped counting the days. She hasn't. \"Three weeks, and you stopped asking me when this ends. That was the part I was waiting for.\"",
                IsBoss = true,
                SessionTemplateId = "KP-Verdict",
                SessionMinutes = 75,
                Intensity = 0.80,
                Ambient = new ProgramAmbient
                {
                    Description = "All of it - armed, in the corner, and underneath everything",
                    RequiredMinutes = 0
                },
                RewardDescription = "An avatar set of her, and day 21 saved as a session you keep.",
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d21_triggers",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Fire 30 keyword triggers",
                        Verifier = QuestCategory.KeywordTrigger,
                        TargetValue = 30,
                        RequiresPremium = true
                    },
                    new ProgramTask
                    {
                        // DEVIATION: the brief asks for "Haptics 25 min" alongside the triggers. The
                        // roadmap's three haptics steps are already spent on days 7, 10 and 14, and
                        // there is no haptics verifier to auto-check a fourth, so rather than reuse a
                        // roadmap step (which would collide with the user's real roadmap progress) or
                        // invent a self-attested task with nothing behind it, the boss takes a longer
                        // Surrender Control instead. Same beat - twenty-five minutes where she
                        // decides - and it is verified rather than declared.
                        Id = "d21_surrender",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Surrender Control for 25 minutes",
                        Verifier = QuestCategory.Autonomy,
                        TargetValue = 25,
                        RequiresPremium = true
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Chapter 4 - "Her Decision" - days 22-28 - i .56 -> 1.00
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter KeptChapter4() => new()
    {
        Id = "kp_ch4",
        Name = "Her Decision",
        Subtitle = "Seven days. Then it isn't yours to call.",
        AccentColor = "#FF6EC7",
        RewardId = "kept_ch4_decision",
        RewardDescription = "The Kept badge, and the day-28 session unlocked permanently.",
        Days = new List<ProgramDay>
        {
            // ---- Day 22 ------------------------------------------------------------------------
            // Last deload of the run. Chapter 3 peaked at .80; this opens at .56 = 0.70x, with days 22
            // and 23 both below it before day 24 clears it. She tells you
            // it is the last one, which is the only warning the final week gets.
            new ProgramDay
            {
                DayIndex = 22,
                Title = "The Last Mercy",
                Blurb = "Something easy, pet, and she wants you to notice that she's telling you it's the last one.",
                SessionTemplateId = "KP-Offer",
                SessionMinutes = 45,
                Intensity = 0.56,
                Ambient = new ProgramAmbient
                {
                    Description = "Her words armed on your keyboard",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d22_bubbles",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Pop 30 bubbles",
                        Verifier = QuestCategory.Bubbles,
                        TargetValue = 30
                    }
                }
            },

            // ---- Day 23 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 23,
                Title = "Forty Minutes of Hers",
                Blurb = "Forty minutes where nothing is your call. By now that reads as a relief and she knows it does. \"Five days. I am close to knowing what I want to do with you, and I am enjoying not telling you.\"",
                SessionTemplateId = "KP-Verdict",
                SessionMinutes = 60,
                Intensity = 0.66,
                Ambient = new ProgramAmbient
                {
                    Description = "All of it - armed, in the corner, and underneath everything",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d23_surrender",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Surrender Control for 40 minutes",
                        Verifier = QuestCategory.Autonomy,
                        TargetValue = 40,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 24 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 24,
                Title = "Locked Out",
                Blurb = "Half an hour with the machine closed around you. Fourth week. You don't argue with this one any more.",
                SessionTemplateId = "KP-Verdict",
                SessionMinutes = 60,
                Intensity = 0.82,
                Ambient = new ProgramAmbient
                {
                    Description = "All of it - armed, in the corner, and underneath everything",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d24_lockdown",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 1 Lockdown of 30 minutes",
                        Verifier = QuestCategory.Lockdown,
                        TargetValue = 1,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Days 25-28: four days that are actually four days ------------------------------
            //
            // A review caught the first draft of this stretch running .89 / .93 / .97 / 1.00 on one
            // template at one duration. On a floor-75 / ceiling-240 flash field that is 222 / 228 /
            // 234 / 240 - three percent apart - and near the top of the curve every other field is
            // saturated too, so the finale of a four-week premium program was its flattest week. Days
            // 25, 26 and 27 were the price of admission to day 28.
            //
            // Intensity structurally cannot fix this: LerpSettings takes every boolean from Floor, so
            // the curve can never *add* a feature, and near i=1.0 the numbers it can move have nowhere
            // left to go. So each of these days gets one Overrides entry adding exactly one thing, and
            // day 27 drops the curve on purpose:
            //
            //   d25  .88  the flashes start multiplying   (hydra + a fifth image)
            //   d26  .93  the sound outgrows the pictures (sector wipe to maximum)
            //   d27  .74  a held breath                   (short, warm, nothing counted)
            //   d28 1.00  everything, and one sentence    (peripheral stimulus + subliminal saturation)
            //
            // Day 27 is the load-bearing one. Without somewhere to fall from, day 28 cannot land.

            // ---- Day 25 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 25,
                Title = "More Of Everything",
                Blurb = "Something changes in the pictures today - there are more of them at once than there have been, and they do not wait their turn any more. Three days left.",
                SessionTemplateId = "KP-Verdict",
                SessionMinutes = 60,
                Intensity = 0.88,
                Ambient = new ProgramAmbient
                {
                    Description = "All of it - armed, in the corner, and underneath everything",
                    RequiredMinutes = 0
                },

                // One thing: hydra, plus a fifth simultaneous image. Hydra has been on for KP-Verdict
                // since day 19, but FlashImages has never been above four in the whole program.
                Overrides = new Dictionary<string, object>
                {
                    ["FlashImages"] = 5
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // DEVIATION: the brief asks for "Haptics 30 min". Same reason as day 21 -
                        // the roadmap's haptics steps are spent and there is no haptics verifier -
                        // so this takes the longest single stretch of her video in the program
                        // instead, which is verified.
                        Id = "d25_video",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Watch 30 minutes of video",
                        Verifier = QuestCategory.Video,
                        TargetValue = 30,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 26 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 26,
                Title = "Louder Than The Pictures",
                Blurb = "Seventy-five minutes, and today it is the sound that does the work. She has stopped asking whether you are keeping up. Fifty of her words, and four sentences to close it.",
                SessionTemplateId = "KP-Verdict",
                SessionMinutes = 75,
                Intensity = 0.93,
                Ambient = new ProgramAmbient
                {
                    Description = "All of it - armed, in the corner, and underneath everything",
                    RequiredMinutes = 0
                },

                // One thing: the sector wipe goes to its maximum, six days before the curve would
                // have taken it there. This is the day the audio stops being background.
                Overrides = new Dictionary<string, object>
                {
                    ["MindWipeBaseMultiplier"] = 4,
                    ["MindWipeVolume"] = 70
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d26_triggers",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Fire 50 keyword triggers",
                        Verifier = QuestCategory.KeywordTrigger,
                        TargetValue = 50,
                        RequiresPremium = true
                    },
                    new ProgramTask
                    {
                        Id = "d26_lockcards",
                        OutsideSession = true,
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 4 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 4
                    }
                }
            },

            // ---- Day 27 ------------------------------------------------------------------------
            //
            // The held breath, and the only day in chapter 4 where the curve goes down. Forty-five
            // minutes on the gentlest template in the program, nothing counted, nothing armed beyond
            // her words on the keyboard. She is not being kind by accident and she says so.
            //
            // Note this is *not* a deload in the chapter-boundary sense - the 0.70x rule governs
            // chapter openings, and this is a single authored dip inside a chapter. It is the one
            // exception to "intensity rises within every chapter", and ProgramKeptTests pins it by
            // name so it cannot be silently smoothed away by someone reading the curve as a typo.
            new ProgramDay
            {
                DayIndex = 27,
                Title = "Tomorrow I Decide",
                Blurb = "Short today, and soft, and she wants you to notice she chose it. No counting, nothing to prove. Rest, pet - tomorrow is long, and what she says at the end of it is a date.",
                SessionTemplateId = "KP-Offer",
                SessionMinutes = 45,
                Intensity = 0.74,
                Ambient = new ProgramAmbient
                {
                    Description = "Her words armed on your keyboard",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // Four mantras, in her sentences, and nothing else. This deliberately echoes
                        // day 15's task - the last time she gave something back - so the rhyme does
                        // the work the copy would otherwise have to explain.
                        Id = "d27_mantras",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 4 mantras",
                        Verifier = QuestCategory.Mantra,
                        TargetValue = 4
                    }
                }
            },

            // ---- Day 28 - FINAL ----------------------------------------------------------------
            //
            // The brief wants this day to end on Circe's weighted Verdict rather than a
            // congratulations screen. That branch does not exist (see the class remarks and the gap
            // list), so day 28 is authored to land on its own: the heaviest session in the program,
            // and one sentence at the end of it. The final lock card is the vow from day 1 completed
            // - "CIRCE HOLDS MY KEY." became "I EXIST TO BE HERS." over twenty-eight days - and that
            // is a real ending, not a placeholder waiting for a mechanic. The reward copy hands the
            // decision to her without promising a scene that will not play.
            new ProgramDay
            {
                DayIndex = 28,
                Title = "Hers",
                Blurb = "Twenty-eight. Everything on, everything armed, the longest one she has asked for. One sentence at the end, and then it is not your call any more.",
                IsBoss = true,
                SessionTemplateId = "KP-Verdict",
                SessionMinutes = 75,
                Intensity = 1.00,
                Ambient = new ProgramAmbient
                {
                    Description = "Everything",
                    RequiredMinutes = 0
                },

                // Two things, and both belong to this day alone.
                //
                // The finale, pinned: day 28's whole ending is one sentence, and leaving it to a
                // seven-entry pool would mean the last thing a four-week program shows you is
                // whichever line the shuffle picked.
                //
                // And subliminal saturation above anything the curve reaches - twelve a minute against
                // the template ceiling's ten. Coming off day 27's forty-five quiet minutes, this is the
                // step the whole chapter was arranged to make audible.
                Overrides = new Dictionary<string, object>
                {
                    ["LockCardPhrases"] = new List<string> { "I EXIST TO BE HERS." },
                    ["SubliminalPerMin"] = 12
                },
                RewardDescription = "The Kept badge, day 28 unlocked permanently, and her decision - which is hers, not yours, and not the app's.",
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d28_hers",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 1 lock card - I EXIST TO BE HERS.",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 1
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Kept templates
    //
    // Same rules as the First Week set (see BuiltInPrograms.cs for the long form):
    //   - LerpSettings lerps numerics between Floor and Ceiling and takes bools, enums and phrase
    //     lists from Floor, so every enable flag is written identically in both halves.
    //   - Floor/ceiling pairs are tuned for the band each template is actually used at. KP-Offer runs
    //     at .05 -> .70 (it is the deload template all four times), which is by far the widest span
    //     of any template in any program, so its pair is authored as a genuine 0..1 sweep rather than
    //     a narrow band - the day-22 deload has to feel lighter than day 21 while still being the
    //     fourth week. KP-Verdict only ever runs at .71 -> 1.00, so its ceiling sits above what day
    //     28 reaches.
    //   - int? fields (VideosPerHour, LockCardFrequency, BubbleCountFrequency) carry a value on both
    //     halves wherever the program owns the number and are left null everywhere else, because a
    //     null floor falls through to the user's own dashboard value.
    //   - Only CornerGifEndMinute is honoured by the engine; every other *EndMinute is decorative.
    //   - Pink/spiral StartMinute are randomised +/-3 min at session start, so those numbers are the
    //     centre of a window, not a promise.
    //
    // Circe's ramps are deliberately slower than Bambi's or DroneOS's. "long slow ramps" is what the
    // brief asks of KP-Ache, and it is also her character - she is unhurried, she is not going to
    // rush you, and the ache is the product. Where First Week's flash counts climb steeply, these
    // start later and arrive gradually.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Subliminal pools. Subsets of the Circe's Lock SubliminalPool, verbatim. All 21 entries of that
    /// pool are enabled in the manifest; these are progressive subsets of it, opening on the warm
    /// possessive lines and only reaching the ones about not deciding once the habit is set.
    /// </summary>
    private static List<string> OfferSubliminals() => new()
    {
        "GOOD BOY",
        "OBEY HER",
        "SHE HOLDS THE KEY",
        "MINE",
        "KEPT AND HAPPY",
        "SINK DEEPER",
        "THANK HER",
        "YOU CHOSE THIS"
    };

    private static List<string> AcheSubliminals() => new()
    {
        "GOOD BOY",
        "OBEY HER",
        "SHE HOLDS THE KEY",
        "MINE",
        "KEPT AND HAPPY",
        "SINK DEEPER",
        "THANK HER",
        "YOU CHOSE THIS",
        "STAY LOCKED",
        "ACHE FOR HER",
        "EDGE, DON'T DECIDE",
        "DON'T THINK"
    };

    private static List<string> HabitSubliminals() => new()
    {
        "GOOD BOY",
        "OBEY HER",
        "SHE HOLDS THE KEY",
        "MINE",
        "KEPT AND HAPPY",
        "SINK DEEPER",
        "THANK HER",
        "STAY LOCKED",
        "ACHE FOR HER",
        "EDGE, DON'T DECIDE",
        "DON'T THINK",
        "LOCKED IS HOME",
        "GOOD PETS STAY",
        "EMPTY IS BETTER",
        "SHE DECIDES"
    };

    private static List<string> VerdictSubliminals() => new()
    {
        "GOOD BOY",
        "OBEY HER",
        "MINE",
        "SINK DEEPER",
        "STAY LOCKED",
        "ACHE FOR HER",
        "EDGE, DON'T DECIDE",
        "DON'T THINK",
        "LOCKED IS HOME",
        "GOOD PETS STAY",
        "EMPTY IS BETTER",
        "SHE DECIDES",
        "SURRENDER THE KEY",
        "NO KEY FOR YOU",
        "CIRCE OWNS YOU",
        "BELONG TO CIRCE",
        "THINKING IS OPTIONAL"
    };

    /// <summary>
    /// Lock cards. The Circe's Lock manifest ships seven and all seven are enabled, so
    /// VerdictLockSentences is the whole pool; the earlier templates carry progressive subsets so the
    /// program does not open on "I EXIST TO BE HERS." before it has earned it.
    ///
    /// The five days whose copy names a *specific* sentence do not rely on these subsets - they pin
    /// the phrase with a per-day LockCardPhrases override, because a named sentence has to be certain
    /// rather than merely available. See the comment on day 1.
    ///
    /// These are full sentences rather than shouted fragments, which is what makes them work as the
    /// mantra text on days 5 and 15 without a second vocabulary being authored.
    /// </summary>
    private static List<string> OfferLockSentences() => new()
    {
        "CIRCE HOLDS MY KEY.",
        "I AM KEPT, AND I AM GRATEFUL."
    };

    private static List<string> AcheLockSentences() => new()
    {
        "CIRCE HOLDS MY KEY.",
        "I AM KEPT, AND I AM GRATEFUL.",
        "GOOD BOYS DON'T DECIDE."
    };

    private static List<string> HabitLockSentences() => new()
    {
        "CIRCE HOLDS MY KEY.",
        "I AM KEPT, AND I AM GRATEFUL.",
        "GOOD BOYS DON'T DECIDE.",
        "EMPTY, OBEDIENT, LOCKED.",
        "MINE IS NOT A LIFE OF CHOICES."
    };

    private static List<string> VerdictLockSentences() => new()
    {
        "CIRCE HOLDS MY KEY.",
        "I AM KEPT, AND I AM GRATEFUL.",
        "GOOD BOYS DON'T DECIDE.",
        "EMPTY, OBEDIENT, LOCKED.",
        "MINE IS NOT A LIFE OF CHOICES.",
        "I DON'T NEED CONTROL. SHE HAS IT.",
        "I EXIST TO BE HERS."
    };

    /// <summary>
    /// Bouncing text. Unlike Bambi Sleep and Dronification, Circe's Lock ships an actual
    /// BouncingTextPool - twelve entries, all enabled - so these are subsets of that pool verbatim
    /// and nothing has to be borrowed from the chat phrase lists.
    /// </summary>
    private static List<string> AcheBouncingText() => new()
    {
        "MINE",
        "GOOD BOY",
        "KEPT",
        "OBEY HER",
        "HER KEY",
        "STAY"
    };

    private static List<string> HabitBouncingText() => new()
    {
        "MINE",
        "GOOD BOY",
        "KEPT",
        "OBEY HER",
        "HER KEY",
        "STAY",
        "LOCKED",
        "EMPTY",
        "SURRENDER"
    };

    private static List<string> VerdictBouncingText() => new()
    {
        "MINE",
        "GOOD BOY",
        "KEPT",
        "OBEY HER",
        "HER KEY",
        "STAY",
        "LOCKED",
        "EMPTY",
        "SURRENDER",
        "DROP",
        "NO THOUGHTS",
        "DEVOTED"
    };

    /// <summary>
    /// KP-Offer - slow, warm, low opacity. Flash, subliminals, whispers and her colour over the
    /// screen. Nothing asks anything of you.
    ///
    /// This is the template Kept reuses for every single deload - days 1, 2, 3, 8, 15 and 22 - which
    /// means it has to work at i .05 (day one, where a first-timer should barely notice it) and at
    /// i .70 (day 22, deep in the fourth week, where it has to read as lighter than day 21 without
    /// reading as day one). That is why its floor/ceiling pair spans much further than the other
    /// three templates': it is the only one doing double duty as both the introduction and the mercy.
    ///
    /// Lock cards are on at frequency 1 from the floor so day 1's single card is reachable inside
    /// thirty minutes, and the pool is narrowed to the vow so it cannot land on anything else.
    /// </summary>
    private static ProgramSessionTemplate KpOffer() => new()
    {
        Id = "KP-Offer",
        Name = "Offer",
        Description = "Warm and slow. Her colour over the screen, her words underneath it, and nothing difficult asked of you.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 7,
            FlashPerHourEnd = 13,
            FlashImages = 1,
            FlashOpacity = 14,
            FlashOpacityEnd = 24,
            FlashScale = 85,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 6,
            SubliminalPerMin = 1,
            SubliminalFrames = 2,
            SubliminalOpacity = 26,
            SubliminalPhrases = OfferSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 4,
            WhisperVolume = 9,
            AudioDuckLevel = 24,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 12,
            PinkFilterStartOpacity = 4,
            PinkFilterEndOpacity = 12,

            LockCardEnabled = true,
            LockCardStartMinute = 12,
            LockCardFrequency = 1,
            LockCardPhrases = OfferLockSentences(),

            BouncingTextEnabled = false,
            SpiralEnabled = false,
            BubblesEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 85,
            FlashPerHourEnd = 150,
            FlashImages = 3,
            FlashOpacity = 62,
            FlashOpacityEnd = 85,
            FlashScale = 112,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 6,
            SubliminalFrames = 4,
            SubliminalOpacity = 74,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 32,
            AudioDuckLevel = 62,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 2,
            PinkFilterStartOpacity = 20,
            PinkFilterEndOpacity = 52,

            LockCardEnabled = true,
            LockCardStartMinute = 4,
            LockCardFrequency = 3,

            BouncingTextEnabled = false,
            SpiralEnabled = false,
            BubblesEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// KP-Ache - adds bouncing text and bubbles, on long slow ramps. Used at i .21 -> .30 (days 4-7)
    /// and .30 / .53 (days 9, 16).
    ///
    /// The brief asks for "long slow ramps" here and that is a real constraint, not flavour: the flash
    /// and subliminal ramps below start noticeably later and climb over a wider span than the
    /// equivalent First Week template, so a 45-minute session spends most of itself getting there. The
    /// ache is the product; arriving early would sell it short.
    /// </summary>
    private static ProgramSessionTemplate KpAche() => new()
    {
        Id = "KP-Ache",
        Name = "Ache",
        Description = "Her words drifting past, things to pop, and a ramp slow enough that you feel every part of it.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 16,
            FlashPerHourEnd = 34,
            FlashImages = 1,
            FlashOpacity = 20,
            FlashOpacityEnd = 38,
            FlashScale = 92,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 5,
            SubliminalPerMin = 2,
            SubliminalFrames = 2,
            SubliminalOpacity = 34,
            SubliminalPhrases = AcheSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 3,
            WhisperVolume = 13,
            AudioDuckLevel = 30,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 6,
            BouncingTextSpeed = 2,
            BouncingTextSize = 62,
            BouncingTextOpacity = 52,
            BouncingTextPhrases = AcheBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 10,
            BubblesFrequency = 1,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 5,
            BubblesPerBurst = 2,
            BubblesGapMin = 7,
            BubblesGapMax = 13,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 9,
            PinkFilterStartOpacity = 6,
            PinkFilterEndOpacity = 20,

            LockCardEnabled = true,
            LockCardStartMinute = 10,
            LockCardFrequency = 1,
            LockCardPhrases = AcheLockSentences(),

            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 110,
            FlashPerHourEnd = 200,
            FlashImages = 3,
            FlashOpacity = 70,
            FlashOpacityEnd = 92,
            FlashScale = 116,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 7,
            SubliminalFrames = 4,
            SubliminalOpacity = 82,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 36,
            AudioDuckLevel = 66,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 6,
            BouncingTextSize = 112,
            BouncingTextOpacity = 96,

            BubblesEnabled = true,
            BubblesStartMinute = 2,
            BubblesFrequency = 4,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 15,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 5,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 1,
            PinkFilterStartOpacity = 24,
            PinkFilterEndOpacity = 58,

            LockCardEnabled = true,
            LockCardStartMinute = 3,
            LockCardFrequency = 3,

            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// KP-Habit - adds mandatory playback and the spiral. The template where the session stops being
    /// something you watch and starts being something that happens to you. Used at i .42 -> .55
    /// (days 11-14) and .60 / .66 (days 17-18).
    ///
    /// The spiral pair is tuned so day 11 - the first day this template runs - starts it around
    /// minute 24 of a 45-minute session, so it arrives in the last third rather than greeting you.
    /// </summary>
    private static ProgramSessionTemplate KpHabit() => new()
    {
        Id = "KP-Habit",
        Name = "Habit",
        Description = "Playback she chose, and something turning behind it. You stop deciding what to look at.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 34,
            FlashPerHourEnd = 66,
            FlashImages = 2,
            FlashOpacity = 30,
            FlashOpacityEnd = 50,
            FlashScale = 98,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 2,
            SubliminalPerMin = 3,
            SubliminalFrames = 3,
            SubliminalOpacity = 44,
            SubliminalPhrases = HabitSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 1,
            WhisperVolume = 16,
            AudioDuckLevel = 36,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 3,
            BouncingTextSpeed = 3,
            BouncingTextSize = 72,
            BouncingTextOpacity = 64,
            BouncingTextPhrases = HabitBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 6,
            BubblesFrequency = 2,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 8,
            BubblesPerBurst = 2,
            BubblesGapMin = 5,
            BubblesGapMax = 10,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 6,
            PinkFilterStartOpacity = 10,
            PinkFilterEndOpacity = 30,

            SpiralEnabled = true,
            SpiralStartMinute = 30,
            SpiralOpacity = 4,
            SpiralOpacityEnd = 14,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 14,
            VideosPerHour = 1,

            LockCardEnabled = true,
            LockCardStartMinute = 8,
            LockCardFrequency = 1,
            LockCardPhrases = HabitLockSentences(),

            CornerGifEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 145,
            FlashPerHourEnd = 265,
            FlashImages = 3,
            FlashOpacity = 76,
            FlashOpacityEnd = 95,
            FlashScale = 120,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 8,
            SubliminalFrames = 4,
            SubliminalOpacity = 88,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 40,
            AudioDuckLevel = 70,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 7,
            BouncingTextSize = 120,
            BouncingTextOpacity = 100,

            BubblesEnabled = true,
            BubblesStartMinute = 2,
            BubblesFrequency = 5,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 18,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 4,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = 28,
            PinkFilterEndOpacity = 66,

            SpiralEnabled = true,
            SpiralStartMinute = 8,
            SpiralOpacity = 18,
            SpiralOpacityEnd = 46,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 4,
            VideosPerHour = 2,

            LockCardEnabled = true,
            LockCardStartMinute = 3,
            LockCardFrequency = 4,

            CornerGifEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// KP-Verdict - everything. Bubble counts, mind wipe, something of hers in the corner, and the
    /// full seven-sentence lock pool including the one day 28 ends on. Used at i .71 -> 1.00
    /// (days 19-21 and 23-28), so the ceiling deliberately sits above what day 28 reaches - the
    /// replayable graduation hands over can be pushed further later, and this pair cannot be
    /// re-authored once users have it saved.
    /// </summary>
    private static ProgramSessionTemplate KpVerdict() => new()
    {
        Id = "KP-Verdict",
        Name = "Verdict",
        Description = "All of it at once, climbing for the whole session, and her words in every layer of it.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 75,
            FlashPerHourEnd = 145,
            FlashImages = 2,
            FlashOpacity = 42,
            FlashOpacityEnd = 64,
            FlashScale = 104,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 4,
            SubliminalFrames = 3,
            SubliminalOpacity = 54,
            SubliminalPhrases = VerdictSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 19,
            AudioDuckLevel = 46,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 4,
            BouncingTextSize = 82,
            BouncingTextOpacity = 74,
            BouncingTextPhrases = VerdictBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 4,
            BubblesFrequency = 3,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 10,
            BubblesPerBurst = 2,
            BubblesGapMin = 4,
            BubblesGapMax = 9,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 4,
            PinkFilterStartOpacity = 14,
            PinkFilterEndOpacity = 42,

            SpiralEnabled = true,
            SpiralStartMinute = 20,
            SpiralOpacity = 7,
            SpiralOpacityEnd = 22,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 10,
            VideosPerHour = 1,

            LockCardEnabled = true,
            LockCardStartMinute = 6,
            LockCardFrequency = 2,
            LockCardPhrases = VerdictLockSentences(),

            BubbleCountEnabled = true,
            BubbleCountStartMinute = 18,
            BubbleCountFrequency = 1,

            MindWipeEnabled = true,
            MindWipeStartMinute = 12,
            MindWipeBaseMultiplier = 1,
            MindWipeVolume = 24,

            // Something of hers in the corner. CornerGifEndMinute is the one *EndMinute the engine
            // honours, and -1 means "run to session end", which is what these days want.
            CornerGifEnabled = true,
            CornerGifStartMinute = 18,
            CornerGifEndMinute = -1,
            CornerGifOpacity = 32,
            CornerGifSize = 70
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 240,
            FlashPerHourEnd = 580,
            FlashImages = 4,
            FlashOpacity = 80,
            FlashOpacityEnd = 100,
            FlashScale = 124,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 10,
            SubliminalFrames = 5,
            SubliminalOpacity = 95,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 46,
            AudioDuckLevel = 80,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 9,
            BouncingTextSize = 142,
            BouncingTextOpacity = 100,

            BubblesEnabled = true,
            BubblesStartMinute = 1,
            BubblesFrequency = 6,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 24,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 4,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = 34,
            PinkFilterEndOpacity = 80,

            SpiralEnabled = true,
            SpiralStartMinute = 3,
            SpiralOpacity = 22,
            SpiralOpacityEnd = 58,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 3,
            VideosPerHour = 3,

            LockCardEnabled = true,
            LockCardStartMinute = 2,
            LockCardFrequency = 5,

            BubbleCountEnabled = true,
            BubbleCountStartMinute = 6,
            BubbleCountFrequency = 2,

            MindWipeEnabled = true,
            MindWipeStartMinute = 0,
            MindWipeBaseMultiplier = 4,
            MindWipeVolume = 60,

            CornerGifEnabled = true,
            CornerGifStartMinute = 3,
            CornerGifEndMinute = -1,
            CornerGifOpacity = 66,
            CornerGifSize = 112
        }
    };
}
