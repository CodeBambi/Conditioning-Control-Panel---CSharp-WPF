using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// The programs that ship in the box.
///
/// Every definition is built fresh on each call. ProgramService hands day objects out to the UI and
/// the session builder, and a shared static graph would let one enrollment's mutation (or a day's
/// Overrides dictionary being edited) leak into the next - so nothing here is cached.
///
/// Vocabulary rule: every trigger, subliminal and lock card phrase below is lifted verbatim from the
/// mod manifest the program is themed on (<see cref="BuiltInMods.BambiSleep"/> for First Week).
/// Nothing is invented, because a program that speaks words the mod never taught reads as a reskin.
/// </summary>
public static partial class BuiltInPrograms
{
    /// <summary>
    /// Every built-in program, freshly constructed. This order is the browse order: the free funnel
    /// first, then shortest paid commitment to longest, so the 28-day mountains are not the first
    /// thing a browsing user is asked to weigh.
    ///
    /// Deliberately NOT filtered by the active mod. A program carries its entire vocabulary in its own
    /// definition, so Firmware Install reads identically whether or not Dronification happens to be the
    /// mod of the moment, and a user who likes the Drone framing should not have to go and switch mods
    /// to discover the program exists. Tier is the only gate - see ProgramService.CanEnroll.
    /// </summary>
    public static IReadOnlyList<ProgramDefinition> All() => new List<ProgramDefinition>
    {
        FirstWeek(),        // Bambi Sleep,   7d, free
        Presentation(),     // Sissy Hypno,  14d
        FirmwareInstall(),  // Dronification,14d
        TheTakeover(),      // Bambi Sleep,  28d
        Kept()              // Circe's Lock, 28d
    };

    // ---------------------------------------------------------------------------------------------
    // FIRST WEEK - Bambi Sleep, 7 days, FREE
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The free funnel. Seven days, one chapter, four templates, one session a day.
    ///
    /// Authoring note 1 - the day tasks deliberately run one step *ahead* of the session:
    ///   day 1 bubbles     -> day 1 session already runs them, day 3 doubles the rate
    ///   day 2 lock cards  -> day 6 override fires the first one in-session
    ///   day 3 pink filter -> day 5 template turns pink on for you at minute 12
    ///   day 4 bubble count-> day 7 turns it on
    ///   day 5 video       -> day 7 turns it on
    /// The user meets each feature by hand first, then finds it already running for them. That is the
    /// tutorial. Every blurb below now *says* which earlier day it is paying off or which later day it
    /// is setting up, because a mechanic the user cannot see reads as "the task doesn't match the
    /// session", i.e. as a bug.
    ///
    /// Authoring note 2 - bubbles are the deliberate exception to "the task is one day ahead", and the
    /// reason is not aesthetic. SessionEngine.ApplySessionSettings does not merely omit a disabled
    /// feature, it writes false into live AppSettings and stops the service (bubbles at :1061-1065,
    /// lock cards :1129, video :1087, bubble count :1161, pink filter :1033). So a day whose blurb asks
    /// for a feature its template disables is not "unassisted", it is *actively prevented* for the
    /// length of the session. On day 1 of a free 7-day funnel that reads as a broken app, so BW-Drift
    /// runs bubbles from minute zero and day 1's task is completable by pressing Start. Where a task
    /// genuinely has to happen outside the session (lock cards day 2, pink day 3, counting day 4,
    /// video day 5) the blurb says so out loud - "while she's running the screen belongs to her".
    ///
    /// Authoring note 3 - the intensity curve is a sawtooth, not a ramp:
    ///   .05 / .12 / .22 / .32 / .45 / .38 / .75
    /// Day 6 is a real deload (30 minutes at .38, below day 5's .45) and the copy names it as mercy,
    /// per content brief 1.1. A 7-day program is *not* exempt from the deload rule - if anything it
    /// needs it more, because day 6 is the day before the boss and the highest-churn day in the week,
    /// and the version of this program without a deload had day 6 as an accidental flat spot: the cost
    /// of a deload (a day with no progress) with none of the benefit (framed relief from a character
    /// who is choosing to be kind). The three longer programs authored from the same brief should copy
    /// the shape, not the length.
    ///
    /// Authoring note 4 - LerpSettings takes every boolean from Floor, so intensity can never turn a
    /// feature on. "What is new today" therefore has exactly three possible sources: a new template, a
    /// new duration, or a day Overrides entry. This program uses all three:
    ///   day 2  Overrides BouncingTextEnabled  - her words start showing up, late and dim
    ///   day 4  Overrides FlashAudioEnabled    - the pictures start making a noise (rehearses day 5)
    ///   day 6  Overrides LockCard + MindWipe + FlashHydra - the deload is also the reveal
    /// Day 6's override is what makes day 7's "you already know how to do every part of it" true: mind
    /// wipe and hydra both used to appear for the first time on the boss.
    /// </summary>
    public static ProgramDefinition FirstWeek()
    {
        return new ProgramDefinition
        {
            Id = "first_week",
            Title = "First Week",
            Subtitle = "Seven days with Bambi",
            Pitch = "Seven days. One session a day. You'll know by Friday.",
            Icon = "\U0001F380",
            ModId = BuiltInMods.BambiSleepId,
            AccentColor = "#FF69B4",
            Tier = ProgramTier.Free,
            LengthDays = 7,

            GraduationBadgeId = "first_week_graduate",

            // Plain, out of character, exactly once - the enrollment ceremony shows this verbatim.
            SafetyNote = "Out of character for one line: this is a game, you can stop at any time, and Withdraw is on every screen with nothing asked of you.",

            // Typed to enroll. Bambi's cadence, but a promise the user is making, not one she makes.
            ContractPhrase = "Seven days, and Bambi doesn't miss one.",

            Rules = new ProgramRules
            {
                DaysOffAllowed = 1,
                StrictAvailable = true,
                DefaultDayBoundaryHour = 4,
                MaxDailyMinutes = 90
            },

            Templates = new List<ProgramSessionTemplate>
            {
                BwDrift(),
                BwFocus(),
                BwPink(),
                BwDeep()
            },

            Chapters = new List<ProgramChapter>
            {
                new ProgramChapter
                {
                    Id = "fw_ch1",
                    Name = "First Week",
                    Subtitle = "Six easy days and one that isn't",
                    AccentColor = "#FF69B4",
                    RewardId = "first_week_preset",
                    RewardDescription = "The \"First Week\" preset - day seven, saved permanently, replayable whenever you want it back.",
                    Days = new List<ProgramDay>
                    {
                        // ---- Day 1 ---------------------------------------------------------------
                        // The session runs bubbles from minute zero (see BwDrift) so the day's own task
                        // is completable by pressing Start. First minute of the funnel - nothing the
                        // blurb asks for may be something the session takes away.
                        new ProgramDay
                        {
                            DayIndex = 1,
                            Title = "Just Sit Still",
                            Blurb = "Barely anything happens today, and that's on purpose. Put it on, leave it on, and pop twenty bubbles while you're in there. Easy, Bambi~",
                            SessionTemplateId = "BW-Drift",
                            SessionMinutes = 30,
                            Intensity = 0.05,
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d1_bubbles",
                                    Kind = ProgramTaskKind.AutoVerified,
                                    // 30 minutes at BW-Drift's day-1 rate of 1 bubble/min from minute 0
                                    // offers ~30, so 20 leaves room for the ones that float away.
                                    Description = "Pop 20 bubbles",
                                    Verifier = QuestCategory.Bubbles,
                                    TargetValue = 20
                                }
                            }
                        },

                        // ---- Day 2 ---------------------------------------------------------------
                        // Same template as day 1, so the day needs an Overrides entry or it is a
                        // repeat: intensity alone can only buy +1 subliminal a minute and six flashes
                        // an hour. The override turns her floating text on for the last third of the
                        // session, which gives the title "Say It Back" something in-session to point
                        // at and makes day 3's full bouncing text an escalation, not an introduction.
                        new ProgramDay
                        {
                            DayIndex = 2,
                            Title = "Say It Back",
                            Blurb = "Something small in return today. She'll ask twice while you're just using your computer, and you type it back exactly - good girl. Then sit down: near the end of the session her words start turning up on their own~",
                            SessionTemplateId = "BW-Drift",
                            SessionMinutes = 30,
                            Intensity = 0.12,
                            // Numbers and phrases for this live inert on BW-Drift's Floor (same
                            // convention as LockCardPhrases-with-the-flag-off), so the day only has to
                            // flip the switch. At i .12 that lands minute 23 of 30, speed 1, opacity 34.
                            Overrides = new Dictionary<string, object>
                            {
                                ["BouncingTextEnabled"] = true
                            },
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d2_lockcards",
                                    OutsideSession = true,
                                    Kind = ProgramTaskKind.AutoVerified,
                                    Description = "Complete 2 lock cards",
                                    Verifier = QuestCategory.LockCard,
                                    TargetValue = 2
                                }
                            }
                        },

                        // ---- Day 3 ---------------------------------------------------------------
                        // New template, so no override needed. The blurb is careful *not* to promise
                        // pink in-session: BW-Focus has PinkFilterEnabled false and the engine writes
                        // that false into live settings, so a user who left pink on would watch it
                        // switch off. Naming the rule ("while she's running the screen belongs to her")
                        // turns a would-be bug report into the tutorial's next lesson.
                        new ProgramDay
                        {
                            DayIndex = 3,
                            Title = "Pink Thoughts",
                            Blurb = "Her words are on screen the whole time now, and the bubbles come to you instead of you going to find them. The pink is still yours to switch on - ten minutes of it, before or after, because while she's running the screen belongs to her. Day five she takes it over for you~",
                            SessionTemplateId = "BW-Focus",
                            SessionMinutes = 30,
                            Intensity = 0.22,
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d3_pink",
                                    OutsideSession = true,
                                    Kind = ProgramTaskKind.AutoVerified,
                                    // PinkFilter accumulates in whole minutes.
                                    Description = "Keep the pink filter on for 10 minutes",
                                    Verifier = QuestCategory.PinkFilter,
                                    TargetValue = 10
                                }
                            }
                        },

                        // ---- Day 4 ---------------------------------------------------------------
                        // Reuses BW-Focus, so it gets the same treatment as day 2. Flash audio is the
                        // one new thing: it is the loudest feature the week used to introduce without
                        // warning (BW-Pink switched it on for the first time on day 5), so day 4
                        // rehearses it a day early and the blurb names it.
                        new ProgramDay
                        {
                            DayIndex = 4,
                            Title = "Pay Attention",
                            Blurb = "A little longer today, and one round of counting to prove you're still in there. *giggles* You start that one yourself - on day seven she starts it for you. Turn your speakers up too: the pictures have started making a noise~",
                            SessionTemplateId = "BW-Focus",
                            SessionMinutes = 45,
                            Intensity = 0.32,
                            Overrides = new Dictionary<string, object>
                            {
                                ["FlashAudioEnabled"] = true
                            },
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d4_bubblecount",
                                    OutsideSession = true,
                                    Kind = ProgramTaskKind.AutoVerified,
                                    Description = "Finish 1 bubble count game",
                                    Verifier = QuestCategory.BubbleCount,
                                    TargetValue = 1
                                }
                            }
                        },

                        // ---- Day 5 ---------------------------------------------------------------
                        // Chapter peak before the deload. New template: pink at minute 12 (paying off
                        // day 3 by hand) and the spiral at minute 20, which is the beat the content
                        // brief asks for. Both numbers are authored backwards from those minutes - see
                        // BwPink.
                        new ProgramDay
                        {
                            DayIndex = 5,
                            Title = "Watch, Don't Think",
                            Blurb = "Twelve minutes of screens today, and you press play yourself - day seven she picks them. That pink you kept on by hand on day three? She does it for you now, twelve minutes in, and something starts turning in the middle of the screen not long after~",
                            SessionTemplateId = "BW-Pink",
                            SessionMinutes = 45,
                            Intensity = 0.45,
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d5_video",
                                    OutsideSession = true,
                                    Kind = ProgramTaskKind.AutoVerified,
                                    // Video accumulates in whole minutes.
                                    Description = "Watch 12 minutes of video",
                                    Verifier = QuestCategory.Video,
                                    TargetValue = 12
                                }
                            }
                        },

                        // ---- Day 6 - DELOAD ------------------------------------------------------
                        // The deload and the reveal in one day. Shorter (30m), genuinely gentler
                        // (i .38, below day 5's .45, so every lerped number drops), and it is the only
                        // day that adds features by override rather than by template:
                        //   - the first lock card the *program* has ever fired, paying off day 2 at
                        //     last and pulling that pairing in from five days to four;
                        //   - six minutes of mind wipe and clickable hydra flashes, both of which used
                        //     to arrive for the first time on the boss.
                        // LockCardFrequency is per *hour* (LockCardService.cs:69), so frequency 1 would
                        // have scheduled the first card 42-78 minutes out - i.e. never, in a 30-minute
                        // session. 6/hour from minute 14 lands it at minute 21-27: exactly one card,
                        // near the end, which is what the blurb promises and what d6_lockcards needs.
                        // Mind wipe in session mode plays (multiplier + elapsed/5min) times per five
                        // minutes (MindWipeService.cs:463-471), so multiplier 1 over the last six
                        // minutes is a coin flip on whether the user hears anything at all. 2 from
                        // minute 22 expects ~3 plays: quiet, but certain.
                        new ProgramDay
                        {
                            DayIndex = 6,
                            Title = "Softer, And A Secret",
                            Blurb = "Shorter today, and gentler - she's being kind on purpose. Near the end she'll ask you to say it back the way you learned on day two, there's a sound in here you haven't heard before, and the pictures split if you poke them. Outside is where today really happens, though: one little thing nobody else gets to know about~",
                            SessionTemplateId = "BW-Pink",
                            SessionMinutes = 30,
                            Intensity = 0.38,
                            Overrides = new Dictionary<string, object>
                            {
                                ["LockCardEnabled"] = true,
                                ["LockCardStartMinute"] = 14,
                                ["LockCardFrequency"] = 6,
                                ["MindWipeEnabled"] = true,
                                ["MindWipeStartMinute"] = 22,
                                ["MindWipeBaseMultiplier"] = 2,
                                ["MindWipeVolume"] = 15,
                                ["FlashHydra"] = true
                            },
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    // Not optional, and satisfied by the session itself. Day 6 used to
                                    // be completable by pressing Start and sitting through a repeat of
                                    // day 5, because its only task was the optional ritual. Now the
                                    // one thing it requires is the one thing that is new about it.
                                    Id = "d6_lockcards",
                                    OutsideSession = true,
                                    Kind = ProgramTaskKind.AutoVerified,
                                    Description = "Complete 1 lock card",
                                    Verifier = QuestCategory.LockCard,
                                    TargetValue = 1
                                },
                                new ProgramTask
                                {
                                    Id = "d6_ritual_pink",
                                    Kind = ProgramTaskKind.Ritual,
                                    Description = "Wear one hidden pink item for the whole day",
                                    RoadmapStepId = "t1_step3",
                                    // Free tier never gates on a real-world act. Skipping this is not a miss.
                                    Optional = true
                                }
                            }
                        },

                        // ---- Day 7 - BOSS --------------------------------------------------------
                        // The claim in the blurb is now true. Every feature in BW-Deep has appeared
                        // before: bubbles day 1, bouncing text day 2, flash audio day 4, pink and
                        // spiral day 5, lock cards / mind wipe / hydra day 6. Lock cards, the bubble
                        // count and video are the three the user only ever did by hand.
                        new ProgramDay
                        {
                            DayIndex = 7,
                            Title = "Giggletime",
                            Blurb = "Everything at once, for a whole hour. Cards, counting, screens, that pink, the sound you met yesterday - you've done every piece of this by hand already, Bambi. All that's left is saying yes~",
                            IsBoss = true,
                            SessionTemplateId = "BW-Deep",
                            SessionMinutes = 60,
                            Intensity = 0.75,
                            RewardDescription = "Good Girl badge, and day seven saved as a preset you keep.",
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d7_lockcards",
                                    OutsideSession = true,
                                    Kind = ProgramTaskKind.AutoVerified,
                                    Description = "Complete 3 lock cards",
                                    Verifier = QuestCategory.LockCard,
                                    TargetValue = 3
                                },
                                new ProgramTask
                                {
                                    Id = "d7_bubbles",
                                    Kind = ProgramTaskKind.AutoVerified,
                                    Description = "Pop 30 bubbles",
                                    Verifier = QuestCategory.Bubbles,
                                    TargetValue = 30
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    // ---------------------------------------------------------------------------------------------
    // First Week templates
    //
    // ProgramSessionBuilder.LerpSettings lerps every numeric property between Floor and Ceiling and
    // takes booleans, enums and phrase lists from Floor. So:
    //   - every enable flag is written identically in both halves, so a reader never has to guess
    //     which one the engine reads;
    //   - the floor is literally the lowest day the template is used on. The *ceiling* is then chosen
    //     by arithmetic, not by taste, and the arithmetic runs the opposite way to intuition. A
    //     template used across two days t1 and t2 moves a field by (ceiling - floor) x (t2 - t1), so a
    //     *narrower* range makes the day-to-day step smaller, not larger. BW-Focus spans .22 to .32 -
    //     ten points - so a knob needs ~50 units of range to move 5, and small integer knobs
    //     (FlashImages, BouncingTextSpeed, BubblesFrequency, SubliminalPerMin) only step at all if the
    //     floor/ceiling pair is picked so a rounding boundary falls *between* the two intensities.
    //     Every ceiling below was checked that way; the values each day actually lands on are written
    //     in the per-template comment so a later edit cannot quietly flatten two days into one.
    //     BW-Deep only ever runs at .75, so its ceiling is deliberately beyond what day 7 reaches.
    //   - FlashImages is the one knob deliberately laddered across the whole week rather than within a
    //     template: 1 / 1 / 2 / 2 / 3 / 3 / 4. Changing any FlashImages floor or ceiling below breaks
    //     that ladder, and a template whose day-5 value came out *below* day 4's would read as the
    //     program going backwards.
    //
    // Gotchas honoured here (see Models/Session.cs):
    //   - only CornerGifEndMinute is read by the engine; every other *EndMinute is decorative, so
    //     nothing below relies on one. All are left at -1 (= run to session end).
    //   - VideosPerHour / LockCardFrequency / BubbleCountFrequency are int?. A null on Floor stays
    //     null and falls through to the user's own dashboard number, so they are set on both halves
    //     wherever the program means to own the value, and left null everywhere else.
    //   - Pink and spiral StartMinute are randomised +/-3 min at session start; the numbers below are
    //     the centre of that window, not a promise.
    //   - PopQuiz*, MiniGameEnabled and BrainDrain* are dead in the engine and are not touched.
    //   - BubblesBurstCount / BubblesPerBurst / BubblesGapMin / BubblesGapMax are read *only* on the
    //     intermittent path (SessionEngine.ScheduleBubbleBursts :743-767 and :791, both gated on
    //     BubblesIntermittent). Every template here runs BubblesIntermittent = false, so those four
    //     are inert and only BubblesFrequency (bubbles per minute, BubbleService.cs:187) and
    //     BubblesStartMinute do anything. They are still authored, for the same reason LockCardPhrases
    //     are, but do not reach for them when a day needs to feel different.
    //   - BubblesStartMinute is 0 in all four templates on purpose. A non-zero value takes the
    //     delayed-start path, which sets live BubblesEnabled = false at t=0 (SessionEngine.cs:1053) -
    //     so a user who had bubbles running watches them stop for that many minutes when the session
    //     begins. On the free tier's first days that is indistinguishable from a crash.
    //   - LockCardFrequency and VideosPerHour are per *hour*. In a 30-minute session a frequency of 1
    //     means "never" - LockCardService schedules the first card at 60/frequency minutes with a
    //     +/-30% jitter (LockCardService.cs:68-78).
    //   - FlashOpacity/FlashOpacityEnd, FlashPerHour/FlashPerHourEnd, PinkFilterStart/EndOpacity and
    //     SpiralOpacity/SpiralOpacityEnd are ramps the engine walks *within* one session. They are
    //     authored as real ramps first, and the program lerp then moves both ends across the week.
    //
    // LockCardPhrases are set on every template even where lock cards are off, because
    // SessionEngine.ApplySessionSettings only reads the pool when LockCardEnabled is true - the list
    // is inert there and costs nothing, and it is right if a future day flips the flag.
    // ---------------------------------------------------------------------------------------------

    // ---------------------------------------------------------------------------------------------
    // Subliminals. The rule, stated once and honoured below:
    //
    //   First Week has to be safe to screenshot. The five explicit entries of the Bambi Sleep
    //   SubliminalPool - DROP FOR COCK, ZAP COCK DRAIN OBEY, COCK ZOMBIE NOW,
    //   COCK TURNS MY BRAIN OFF and BAMBI CUM AND COLLAPSE - are held back for the premium 28-day
    //   program, which is where the content brief has the arc *end* on BAMBI CUM AND COLLAPSE. The
    //   other 16 are all used, and DeepSubliminals() is exactly those 16.
    //
    // This is escalation across programs, not squeamishness: the free week is the one a user is most
    // likely to record their reaction to and post, and it was previously shipping one explicit trigger
    // (DROP FOR COCK, on day 7) while withholding four - a rule that was neither screenshot-safe nor
    // the mod's full voice. Pick one. This picks safe.
    // ---------------------------------------------------------------------------------------------

    private static List<string> DriftSubliminals() => new()
    {
        "GOOD GIRL",
        "BAMBI SLEEP",
        "BIMBO DOLL",
        "PRIMPED AND PAMPERED",
        "GIGGLETIME",
        "DONT THINK SILLY",
        "THERES NO NEED TO THINK",
        "TURN YOUR BRAIN OFF"
    };

    private static List<string> FocusSubliminals() => new()
    {
        "GOOD GIRL",
        "BAMBI SLEEP",
        "BIMBO DOLL",
        "PRIMPED AND PAMPERED",
        "GIGGLETIME",
        "DONT THINK SILLY",
        "THERES NO NEED TO THINK",
        "TURN YOUR BRAIN OFF",
        "GOOD GIRLS DONT THINK",
        "JUST OBEY",
        "BAMBI DOES AS SHE'S TOLD"
    };

    private static List<string> PinkSubliminals() => new()
    {
        "GOOD GIRL",
        "BAMBI SLEEP",
        "BIMBO DOLL",
        "PRIMPED AND PAMPERED",
        "GIGGLETIME",
        "DONT THINK SILLY",
        "THERES NO NEED TO THINK",
        "TURN YOUR BRAIN OFF",
        "GOOD GIRLS DONT THINK",
        "JUST OBEY",
        "BAMBI DOES AS SHE'S TOLD",
        "SNAP AND FORGET",
        "BAMBI FREEZE"
    };

    /// <summary>
    /// Day 7 gets the whole screenshot-safe pool: all 21 SubliminalPool entries minus the five held
    /// back above. ProgramFirstWeekTests asserts that equality both ways, because "the boss uses
    /// everything except the explicit five" is the rule and a hand-maintained subset drifts out of a
    /// rule silently.
    /// </summary>
    private static List<string> DeepSubliminals() => new()
    {
        "GOOD GIRL",
        "BAMBI SLEEP",
        "BIMBO DOLL",
        "GIGGLETIME",
        "BAMBI FREEZE",
        "BAMBI RESET",
        "SNAP AND FORGET",
        "JUST OBEY",
        "TURN YOUR BRAIN OFF",
        "GOOD GIRLS DONT THINK",
        "BAMBI DOES AS SHE'S TOLD",
        "I CANT RESIST MY TRIGGERS",
        "BAMBI UNIFORM LOCK",
        "PRIMPED AND PAMPERED",
        "DONT THINK SILLY",
        "THERES NO NEED TO THINK"
    };

    /// <summary>
    /// The Bambi Sleep manifest ships exactly five lock card phrases and all five are enabled, so
    /// these lists are the whole pool - narrowed on the early templates so day 2 lands on the phrase
    /// the content brief names.
    /// </summary>
    private static List<string> EarlyLockCards() => new()
    {
        "GOOD GIRLS OBEY"
    };

    private static List<string> MidLockCards() => new()
    {
        "GOOD GIRLS OBEY",
        "I LOVE BEING PROGRAMMED",
        "BAMBI SLEEP"
    };

    private static List<string> DeepLockCards() => new()
    {
        "GOOD GIRLS OBEY",
        "I LOVE BEING PROGRAMMED",
        "BAMBI SLEEP",
        "DROP FOR ME",
        "EMPTY AND OBEDIENT"
    };

    /// <summary>
    /// Bouncing text. The Bambi Sleep manifest carries no BouncingTextPool (only CCPDefault and Circe's
    /// Lock have one), so these are taken from its RandomFloating / Idle phrase lists **byte-exact** -
    /// not re-cased, not re-punctuated, not trimmed. The test compares them to the manifest with
    /// ordinal string equality, so any "improvement" to the casing will fail the build.
    ///
    /// One line from RandomFloating is deliberately *not* used: "Good girl! *giggles*". Asterisk
    /// actions are an avatar speech-bubble convention; drifting across the screen as text they render
    /// as literal asterisks. "Pretty pink princess!" carries the same beat and bounces clean.
    /// </summary>
    private static List<string> DriftBouncingText() => new()
    {
        "Empty head, happy girl!",
        "Hehe~ so floaty...",
        "Just floating here..."
    };

    private static List<string> FocusBouncingText() => new()
    {
        "Empty head, happy girl!",
        "Pink is my favorite color!",
        "Bambi is a good girl~",
        "Bubbles pop thoughts away~",
        "So pink and empty...",
        "Pretty pink princess!",
        "Mind so soft and fuzzy~",
        "Giggly and empty~"
    };

    private static List<string> PinkBouncingText() => new()
    {
        "Empty head, happy girl!",
        "Bambi is a good girl~",
        "Good girls don't think~",
        "Dropping deeper...",
        "Thoughts drip away...",
        "Obey feels so good!",
        "Bimbo is bliss!",
        "Pink spirals are pretty...",
        "So pink and empty...",
        "Bambi loves spirals~"
    };

    private static List<string> DeepBouncingText() => new()
    {
        "Good girls don't think~",
        "Bambi is brainless~",
        "Obey feels so good!",
        "Dropping deeper...",
        "Thoughts drip away...",
        "Uniform on, brain off~",
        "Bambi obeys!",
        "Bimbo is bliss!",
        "Empty and happy~",
        "Bambi Sleep...",
        "Don't think, Bambi. Just watch~",
        "Good girls drop deep~"
    };

    /// <summary>
    /// BW-Drift - almost passive. Flash, subliminals, whispers, and one bubble a minute.
    /// Used on days 1 (i .05) and 2 (i .12), so the floor IS day one: twelve pale flashes an hour and
    /// one subliminal a minute. If a first-time user notices it working, it is authored wrong.
    ///
    /// Deviation from the content brief's template gloss, on purpose: the brief has BW-Focus be the
    /// template that "adds bubbles". It cannot be, because day 1's *task* is to pop twenty of them and
    /// SessionEngine writes BubblesEnabled = false into live settings and calls App.Bubbles.Stop() for
    /// any session that does not run them (:1061-1065). Day 1 with bubbles off is not a day where the
    /// user has to find bubbles themselves, it is a day where the app takes them away for thirty
    /// minutes while the counter sits at zero. So BW-Drift runs them at the gentlest rate in the file
    /// and BW-Focus escalates the rate instead of introducing the feature.
    ///
    /// Lands on:                       day 1 (.05)   day 2 (.12)
    ///   FlashPerHour / ...End              12 -> 21     18 -> 30
    ///   FlashOpacity / ...End              15 -> 26     19 -> 31
    ///   FlashImages                          1            1
    ///   SubliminalPerMin                     1            2
    ///   SubliminalStartMinute                6            5
    ///   WhisperVolume                       10           12
    ///   BubblesFrequency (per min)           1            1
    ///   BouncingText                       off        on via override, minute 23, speed 1, opacity 34
    /// </summary>
    private static ProgramSessionTemplate BwDrift() => new()
    {
        Id = "BW-Drift",
        Name = "Drift",
        Description = "Soft background conditioning. Put it on and forget it's there.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 8,
            FlashPerHourEnd = 14,
            FlashImages = 1,
            FlashOpacity = 12,
            FlashOpacityEnd = 22,
            FlashScale = 85,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 6,
            SubliminalPerMin = 1,
            SubliminalFrames = 2,
            SubliminalOpacity = 28,
            SubliminalPhrases = DriftSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 5,
            WhisperVolume = 8,
            AudioDuckLevel = 25,

            // Off here, flipped on by day 2's Overrides. The numbers and the phrase list are authored
            // anyway - inert while the flag is false, exactly like LockCardPhrases below - so the day
            // only has to carry the one key that makes it a different day.
            BouncingTextEnabled = false,
            BouncingTextStartMinute = 24,
            BouncingTextSpeed = 1,
            BouncingTextSize = 45,
            BouncingTextOpacity = 30,
            BouncingTextPhrases = DriftBouncingText(),

            // Start minute 0 is load-bearing, not lazy - see the BubblesStartMinute note above.
            BubblesEnabled = true,
            BubblesStartMinute = 0,
            BubblesFrequency = 1,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 8,
            BubblesPerBurst = 2,
            BubblesGapMin = 6,
            BubblesGapMax = 12,

            PinkFilterEnabled = false,
            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            LockCardPhrases = EarlyLockCards(),
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            // Deliberately far out. This template only ever runs across a seven-point band, so the
            // ranges have to be wide or day 2 is day 1 - the old ceiling of 60 flashes/hour moved the
            // day-1-to-day-2 step by three flashes across a whole thirty-minute session.
            FlashEnabled = true,
            FlashPerHour = 90,
            FlashPerHourEnd = 150,
            FlashImages = 3,
            FlashOpacity = 70,
            FlashOpacityEnd = 95,
            FlashScale = 110,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 8,
            SubliminalFrames = 5,
            SubliminalOpacity = 80,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 40,
            AudioDuckLevel = 65,

            BouncingTextEnabled = false,
            BouncingTextStartMinute = 12,
            BouncingTextSpeed = 4,
            BouncingTextSize = 75,
            BouncingTextOpacity = 65,

            BubblesEnabled = true,
            BubblesStartMinute = 0,
            BubblesFrequency = 3,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 20,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 5,

            PinkFilterEnabled = false,
            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// BW-Focus - her words on screen for the whole session instead of the last eight minutes, and the
    /// bubbles come to you. Used on days 3 (i .22) and 4 (i .32).
    ///
    /// Every ceiling here was re-picked so the ten-point step between the two days crosses a rounding
    /// boundary on the knobs a user can actually perceive. Narrowing the ceilings toward the top of the
    /// band - the intuitive move - does the opposite: it shrinks the step, because the step is
    /// (ceiling - floor) x 0.10. Six of this template's knobs used to round to the same integer on both
    /// days.
    ///
    /// Lands on:                       day 3 (.22)   day 4 (.32)
    ///   FlashPerHour / ...End              45 -> 72     57 -> 91
    ///   FlashOpacity / ...End              36 -> 52     42 -> 58
    ///   FlashImages                          2            2
    ///   SubliminalPerMin                     4            5
    ///   BouncingTextSpeed                    3            4
    ///   BouncingTextStartMinute              6            5
    ///   BubblesFrequency (per min)           2            3
    ///   WhisperVolume                       19           23
    ///   FlashAudio                         off        on via override
    ///   duration                            30           45
    /// </summary>
    private static ProgramSessionTemplate BwFocus() => new()
    {
        Id = "BW-Focus",
        Name = "Focus",
        Description = "Something to look at now. Words that drift past, bubbles that ask to be popped.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 18,
            FlashPerHourEnd = 30,
            FlashImages = 1,
            FlashOpacity = 24,
            FlashOpacityEnd = 40,
            FlashScale = 90,
            FlashClickable = true,
            FlashHydra = false,
            // Off here, on via day 4's Overrides. BW-Pink switches it on permanently from day 5, so
            // day 4 is the rehearsal that keeps the day-7 blurb honest.
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 5,
            SubliminalPerMin = 2,
            SubliminalFrames = 2,
            SubliminalOpacity = 38,
            SubliminalPhrases = FocusSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 5,
            WhisperVolume = 12,
            AudioDuckLevel = 30,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 8,
            BouncingTextSpeed = 2,
            BouncingTextSize = 55,
            BouncingTextOpacity = 50,
            BouncingTextPhrases = FocusBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 0,
            BubblesFrequency = 1,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 5,
            BubblesPerBurst = 2,
            BubblesGapMin = 6,
            BubblesGapMax = 12,

            PinkFilterEnabled = false,
            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            LockCardPhrases = MidLockCards(),
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 140,
            FlashPerHourEnd = 220,
            FlashImages = 4,
            FlashOpacity = 80,
            FlashOpacityEnd = 95,
            FlashScale = 120,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 10,
            SubliminalFrames = 6,
            SubliminalOpacity = 88,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 46,
            AudioDuckLevel = 70,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 7,
            BouncingTextSize = 115,
            BouncingTextOpacity = 95,

            BubblesEnabled = true,
            BubblesStartMinute = 0,
            BubblesFrequency = 6,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 14,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 5,

            PinkFilterEnabled = false,
            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// BW-Pink - adds the pink filter and the spiral. Used on days 5 (i .45) and 6 (i .38).
    ///
    /// Note the band runs *downwards*: day 6 is the week's deload, so it sits below day 5 and every
    /// lerped number on it is smaller. That is the whole reason day 6 needs an Overrides entry - a
    /// deload day still has to contain something new, and intensity by definition cannot give it to it.
    ///
    /// Two start minutes here are authored backwards from the beat they are supposed to land on rather
    /// than picked to look tidy, which is the standard the rest of the file is held to:
    ///   SpiralStartMinute     26 -> 12  lands minute 20 on day 5 (content brief: "spiral arrives at
    ///                                   minute 20"), minute 21 on day 6.
    ///   PinkFilterStartMinute 19 ->  3  lands minute 12 on day 5, which is what day 5's blurb
    ///                                   promises out loud, and minute 13 on day 6.
    /// Both are then jittered +/-3 by the engine at session start. Change either floor and the copy
    /// that names the minute goes stale.
    ///
    /// Lands on:                       day 5 (.45)   day 6 (.38)
    ///   FlashPerHour / ...End             80 -> 141    72 -> 128
    ///   FlashOpacity / ...End              52 -> 71     48 -> 67
    ///   FlashImages                          3            3
    ///   SubliminalPerMin                     7            6
    ///   PinkFilter start/end opacity   13 -> 42      11 -> 39
    ///   Spiral opacity start/end         9 -> 26       8 -> 23
    ///   duration                            45           30
    ///   plus, day 6 only, one lock card and six minutes of mind wipe by override
    /// </summary>
    private static ProgramSessionTemplate BwPink() => new()
    {
        Id = "BW-Pink",
        Name = "Pink",
        Description = "The screen stops being neutral. Pink over everything, and something turning in the middle of it.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 30,
            FlashPerHourEnd = 60,
            FlashImages = 2,
            FlashOpacity = 30,
            FlashOpacityEnd = 50,
            FlashScale = 95,
            FlashClickable = true,
            // False here; day 6's Overrides turns it on, which is the only place in the week hydra
            // appears before the boss.
            FlashHydra = false,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 3,
            SubliminalPerMin = 4,
            SubliminalFrames = 3,
            SubliminalOpacity = 45,
            SubliminalPhrases = PinkSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 2,
            WhisperVolume = 16,
            AudioDuckLevel = 36,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 4,
            BouncingTextSpeed = 3,
            BouncingTextSize = 68,
            BouncingTextOpacity = 62,
            BouncingTextPhrases = PinkBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 0,
            BubblesFrequency = 2,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 8,
            BubblesPerBurst = 2,
            BubblesGapMin = 5,
            BubblesGapMax = 10,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 19,
            PinkFilterStartOpacity = 5,
            PinkFilterEndOpacity = 25,

            SpiralEnabled = true,
            SpiralStartMinute = 26,
            SpiralOpacity = 3,
            SpiralOpacityEnd = 12,

            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            // Off here; day 6's Overrides turns it on. Narrowed to the single phrase the content brief
            // names for day 2's hand-typed task, so the first card the program itself ever fires is the
            // one the user learned four days earlier.
            LockCardEnabled = false,
            LockCardPhrases = EarlyLockCards(),
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 140,
            FlashPerHourEnd = 240,
            FlashImages = 5,
            FlashOpacity = 78,
            FlashOpacityEnd = 96,
            FlashScale = 122,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 10,
            SubliminalFrames = 5,
            SubliminalOpacity = 90,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 46,
            AudioDuckLevel = 74,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 8,
            BouncingTextSize = 122,
            BouncingTextOpacity = 100,

            BubblesEnabled = true,
            BubblesStartMinute = 0,
            BubblesFrequency = 7,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 18,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 4,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 3,
            PinkFilterStartOpacity = 22,
            PinkFilterEndOpacity = 62,

            SpiralEnabled = true,
            SpiralStartMinute = 12,
            SpiralOpacity = 16,
            SpiralOpacityEnd = 42,

            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// BW-Deep - everything on. Lock cards, mandatory videos, the bubble count, hydra flashes and an
    /// escalating mind wipe. Used once, on the day 7 boss at i .75, so the ceiling sits above what
    /// the week ever reaches - the graduation preset can be pushed further later, this cannot.
    ///
    /// Nothing here is a first encounter any more. Bubbles day 1, bouncing text day 2, flash audio day
    /// 4, pink and spiral day 5, and lock cards, mind wipe and hydra all rehearsed on day 6 - which is
    /// what lets the day-7 blurb say "you've done every piece of this by hand already" without lying.
    ///
    /// Lands on, at i .75:
    ///   FlashPerHour        180 -> 480      FlashImages            4     FlashOpacity     70 -> 90
    ///   SubliminalPerMin      9  (5 frames) LockCardStartMinute    8     LockCardFreq  5/hour
    ///   MindWipe          mult 3, vol 51    BubbleCountStartMinute 11    VideosPerHour    2
    /// LockCardFrequency is deliberately 2 -> 6 rather than 1 -> 4: the old pair landed 3/hour, which
    /// over the 52 minutes after the first card is scheduled expects ~2.6 cards for a task that asks
    /// for 3. 5/hour expects ~4, so the boss's own task is completable inside the boss.
    /// </summary>
    private static ProgramSessionTemplate BwDeep() => new()
    {
        Id = "BW-Deep",
        Name = "Deep",
        Description = "Everything at once, and it keeps climbing for the whole hour.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 60,
            FlashPerHourEnd = 120,
            FlashImages = 2,
            FlashOpacity = 40,
            FlashOpacityEnd = 60,
            FlashScale = 100,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 4,
            SubliminalFrames = 3,
            SubliminalOpacity = 55,
            SubliminalPhrases = DeepSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 18,
            AudioDuckLevel = 45,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 4,
            BouncingTextSize = 80,
            BouncingTextOpacity = 75,
            BouncingTextPhrases = DeepBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 0,
            BubblesFrequency = 3,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 10,
            BubblesPerBurst = 2,
            BubblesGapMin = 4,
            BubblesGapMax = 9,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 8,
            PinkFilterStartOpacity = 12,
            PinkFilterEndOpacity = 40,

            SpiralEnabled = true,
            SpiralStartMinute = 20,
            SpiralOpacity = 6,
            SpiralOpacityEnd = 20,

            // The program owns these three numbers, so both halves carry a value - a null floor would
            // leave the user's dashboard frequency in charge of the boss.
            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 12,
            VideosPerHour = 1,

            LockCardEnabled = true,
            LockCardStartMinute = 15,
            LockCardFrequency = 2,
            LockCardPhrases = DeepLockCards(),

            BubbleCountEnabled = true,
            BubbleCountStartMinute = 20,
            BubbleCountFrequency = 1,

            MindWipeEnabled = true,
            MindWipeStartMinute = 10,
            MindWipeBaseMultiplier = 1,
            MindWipeVolume = 25,

            CornerGifEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 220,
            FlashPerHourEnd = 600,
            FlashImages = 4,
            FlashOpacity = 80,
            FlashOpacityEnd = 100,
            FlashScale = 120,
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
            WhisperVolume = 45,
            AudioDuckLevel = 80,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 9,
            BouncingTextSize = 140,
            BouncingTextOpacity = 100,

            BubblesEnabled = true,
            BubblesStartMinute = 0,
            BubblesFrequency = 6,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 24,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 4,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = 30,
            PinkFilterEndOpacity = 75,

            SpiralEnabled = true,
            SpiralStartMinute = 4,
            SpiralOpacity = 20,
            SpiralOpacityEnd = 55,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 4,
            VideosPerHour = 2,

            LockCardEnabled = true,
            LockCardStartMinute = 5,
            LockCardFrequency = 6,

            BubbleCountEnabled = true,
            BubbleCountStartMinute = 8,
            BubbleCountFrequency = 2,

            MindWipeEnabled = true,
            MindWipeStartMinute = 0,
            MindWipeBaseMultiplier = 4,
            MindWipeVolume = 60,

            CornerGifEnabled = false
        }
    };
}
