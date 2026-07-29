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
public static class BuiltInPrograms
{
    /// <summary>Every built-in program, freshly constructed.</summary>
    public static IReadOnlyList<ProgramDefinition> All() => new List<ProgramDefinition>
    {
        FirstWeek()
    };

    // ---------------------------------------------------------------------------------------------
    // FIRST WEEK - Bambi Sleep, 7 days, FREE
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The free funnel. Seven days, one chapter, four templates, one session a day.
    ///
    /// Authoring note - the day tasks deliberately run one step *ahead* of the session:
    ///   day 1 bubbles  -> day 3 template turns bubbles on
    ///   day 3 pink     -> day 5 template turns pink on
    ///   day 2 lock cards + day 5 video + day 4 bubble count -> day 7 turns all three on.
    /// The user meets each feature by hand on the dashboard first, then finds it already running for
    /// them. That is the tutorial, and it is why the tasks do not match the day's own template.
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
                        new ProgramDay
                        {
                            DayIndex = 1,
                            Title = "Just Sit Still",
                            Blurb = "Barely anything happens today, and that's on purpose. Put it on, leave it on, pop a few bubbles while you're there. Easy, Bambi~",
                            SessionTemplateId = "BW-Drift",
                            SessionMinutes = 30,
                            Intensity = 0.05,
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d1_bubbles",
                                    Kind = ProgramTaskKind.AutoVerified,
                                    Description = "Pop 20 bubbles",
                                    Verifier = QuestCategory.Bubbles,
                                    TargetValue = 20
                                }
                            }
                        },

                        // ---- Day 2 ---------------------------------------------------------------
                        new ProgramDay
                        {
                            DayIndex = 2,
                            Title = "Say It Back",
                            Blurb = "Today Bambi wants something small in return. Type it back exactly, good girl - it only takes a second, and it counts~",
                            SessionTemplateId = "BW-Drift",
                            SessionMinutes = 30,
                            Intensity = 0.12,
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d2_lockcards",
                                    Kind = ProgramTaskKind.AutoVerified,
                                    Description = "Complete 2 lock cards",
                                    Verifier = QuestCategory.LockCard,
                                    TargetValue = 2
                                }
                            }
                        },

                        // ---- Day 3 ---------------------------------------------------------------
                        new ProgramDay
                        {
                            DayIndex = 3,
                            Title = "Pink Thoughts",
                            Blurb = "Something pretty turns up today. Let it sit over everything you do and try not to think about it too hard~",
                            SessionTemplateId = "BW-Focus",
                            SessionMinutes = 30,
                            Intensity = 0.20,
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d3_pink",
                                    Kind = ProgramTaskKind.AutoVerified,
                                    // PinkFilter accumulates in whole minutes.
                                    Description = "Keep the pink filter on for 10 minutes",
                                    Verifier = QuestCategory.PinkFilter,
                                    TargetValue = 10
                                }
                            }
                        },

                        // ---- Day 4 ---------------------------------------------------------------
                        new ProgramDay
                        {
                            DayIndex = 4,
                            Title = "Pay Attention",
                            Blurb = "A little longer today, and Bambi's going to check you're still in there. *giggles* She's nice about it. Mostly.",
                            SessionTemplateId = "BW-Focus",
                            SessionMinutes = 45,
                            Intensity = 0.30,
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d4_bubblecount",
                                    Kind = ProgramTaskKind.AutoVerified,
                                    Description = "Finish 1 bubble count game",
                                    Verifier = QuestCategory.BubbleCount,
                                    TargetValue = 1
                                }
                            }
                        },

                        // ---- Day 5 ---------------------------------------------------------------
                        new ProgramDay
                        {
                            DayIndex = 5,
                            Title = "Watch, Don't Think",
                            Blurb = "Screens today. Sit close, keep your eyes soft, and let the pretty stuff do the work for you~",
                            SessionTemplateId = "BW-Pink",
                            SessionMinutes = 45,
                            Intensity = 0.42,
                            Tasks = new List<ProgramTask>
                            {
                                new ProgramTask
                                {
                                    Id = "d5_video",
                                    Kind = ProgramTaskKind.AutoVerified,
                                    // Video accumulates in whole minutes.
                                    Description = "Watch 12 minutes of video",
                                    Verifier = QuestCategory.Video,
                                    TargetValue = 12
                                }
                            }
                        },

                        // ---- Day 6 ---------------------------------------------------------------
                        new ProgramDay
                        {
                            DayIndex = 6,
                            Title = "A Secret, Just Yours",
                            Blurb = "Same as yesterday on the inside. Outside is where today actually happens - one little thing nobody else gets to know about~",
                            SessionTemplateId = "BW-Pink",
                            SessionMinutes = 45,
                            Intensity = 0.55,
                            Tasks = new List<ProgramTask>
                            {
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
                        new ProgramDay
                        {
                            DayIndex = 7,
                            Title = "Giggletime",
                            Blurb = "Everything at once, for a whole hour. You already know how to do every part of it, Bambi. All that's left is saying yes~",
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
    //   - the floor/ceiling pairs are chosen for the intensity band each template is actually used
    //     at, not for a theoretical 0..1 sweep. BW-Drift only ever runs at .05/.12, so its floor is
    //     literally the day-1 experience and its ceiling sits far out to make a 7-point step visible.
    //     BW-Deep only ever runs at .75, so its ceiling is deliberately beyond what day 7 reaches.
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
    //   - FlashOpacity/FlashOpacityEnd, FlashPerHour/FlashPerHourEnd, PinkFilterStart/EndOpacity and
    //     SpiralOpacity/SpiralOpacityEnd are ramps the engine walks *within* one session. They are
    //     authored as real ramps first, and the program lerp then moves both ends across the week.
    //
    // LockCardPhrases are set on every template even where lock cards are off, because
    // SessionEngine.ApplySessionSettings only reads the pool when LockCardEnabled is true - the list
    // is inert there and costs nothing, and it is right if a future day flips the flag.
    // ---------------------------------------------------------------------------------------------

    /// <summary>All 21 entries of the Bambi Sleep SubliminalPool are enabled; these are subsets of it.</summary>
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
        "DROP FOR COCK"
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
    /// Bouncing text. The Bambi Sleep manifest carries no BouncingTextPool, so these are taken from
    /// its RandomFloating / Idle phrase lists verbatim - still the mod's own words, sentence-cased
    /// the way bouncing text reads best.
    /// </summary>
    private static List<string> FocusBouncingText() => new()
    {
        "Empty head, happy girl!",
        "Pink is my favorite color!",
        "Bambi is a good girl~",
        "Bubbles pop thoughts away~",
        "So pink and empty...",
        "Good girl! *giggles*",
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
    /// BW-Drift - passive. Flash, subliminals, whispers. Nothing asks anything of you.
    /// Used on days 1 (i .05) and 2 (i .12), so the floor IS day one: eleven pale flashes an hour and
    /// one subliminal a minute. If a first-time user notices it working, it is authored wrong.
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
            FlashOpacity = 15,
            FlashOpacityEnd = 25,
            FlashScale = 85,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 5,
            SubliminalPerMin = 1,
            SubliminalFrames = 2,
            SubliminalOpacity = 30,
            SubliminalPhrases = DriftSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 3,
            WhisperVolume = 8,
            AudioDuckLevel = 25,

            BouncingTextEnabled = false,
            PinkFilterEnabled = false,
            SpiralEnabled = false,
            BubblesEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            LockCardPhrases = EarlyLockCards(),
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 60,
            FlashPerHourEnd = 100,
            FlashImages = 3,
            FlashOpacity = 60,
            FlashOpacityEnd = 85,
            FlashScale = 110,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 6,
            SubliminalFrames = 4,
            SubliminalOpacity = 75,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 30,
            AudioDuckLevel = 60,

            BouncingTextEnabled = false,
            PinkFilterEnabled = false,
            SpiralEnabled = false,
            BubblesEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// BW-Focus - adds bouncing text and bubbles. The first template that puts something on screen
    /// you can look at on purpose. Used on days 3 (i .20) and 4 (i .30).
    /// </summary>
    private static ProgramSessionTemplate BwFocus() => new()
    {
        Id = "BW-Focus",
        Name = "Focus",
        Description = "Something to look at now. Words that drift past, bubbles that ask to be popped.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 20,
            FlashPerHourEnd = 35,
            FlashImages = 1,
            FlashOpacity = 25,
            FlashOpacityEnd = 40,
            FlashScale = 90,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 3,
            SubliminalPerMin = 2,
            SubliminalFrames = 2,
            SubliminalOpacity = 40,
            SubliminalPhrases = FocusSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 2,
            WhisperVolume = 12,
            AudioDuckLevel = 30,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 4,
            BouncingTextSpeed = 2,
            BouncingTextSize = 60,
            BouncingTextOpacity = 55,
            BouncingTextPhrases = FocusBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 8,
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
            FlashPerHour = 90,
            FlashPerHourEnd = 140,
            FlashImages = 3,
            FlashOpacity = 70,
            FlashOpacityEnd = 90,
            FlashScale = 115,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 7,
            SubliminalFrames = 4,
            SubliminalOpacity = 80,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 35,
            AudioDuckLevel = 65,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 6,
            BouncingTextSize = 110,
            BouncingTextOpacity = 95,

            BubblesEnabled = true,
            BubblesStartMinute = 2,
            BubblesFrequency = 4,
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
    /// BW-Pink - adds the pink filter and the spiral. Used on days 5 (i .42) and 6 (i .55).
    /// The spiral floor/ceiling pair is tuned so day 5 lands its start on minute 20, which is the
    /// beat the content brief calls for ("spiral arrives at minute 20"), before the engine's +/-3.
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
            FlashHydra = false,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 2,
            SubliminalPerMin = 3,
            SubliminalFrames = 3,
            SubliminalOpacity = 45,
            SubliminalPhrases = PinkSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 1,
            WhisperVolume = 15,
            AudioDuckLevel = 35,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 2,
            BouncingTextSpeed = 3,
            BouncingTextSize = 70,
            BouncingTextOpacity = 65,
            BouncingTextPhrases = PinkBouncingText(),

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
            PinkFilterStartMinute = 12,
            PinkFilterStartOpacity = 5,
            PinkFilterEndOpacity = 25,

            SpiralEnabled = true,
            SpiralStartMinute = 26,
            SpiralOpacity = 3,
            SpiralOpacityEnd = 12,

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
            FlashPerHourEnd = 240,
            FlashImages = 3,
            FlashOpacity = 75,
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
            SubliminalOpacity = 85,

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
            PinkFilterStartMinute = 3,
            PinkFilterStartOpacity = 20,
            PinkFilterEndOpacity = 60,

            SpiralEnabled = true,
            SpiralStartMinute = 12,
            SpiralOpacity = 15,
            SpiralOpacityEnd = 40,

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
            BubblesStartMinute = 4,
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
            LockCardFrequency = 1,
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
            BubblesStartMinute = 2,
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
            LockCardFrequency = 4,

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
