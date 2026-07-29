using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// THE TAKEOVER - Bambi Sleep, 28 days, Premium.
///
/// Vocabulary rule, same as every other program in this folder: every subliminal line, lock card
/// phrase and bouncing-text line below is lifted verbatim from <see cref="BuiltInMods.BambiSleep"/>.
/// Nothing is invented. A program that speaks words the mod never taught reads as a reskin.
///
/// No active-mod dependency: the definition carries its whole vocabulary, so this reads and runs
/// identically whether or not Bambi Sleep happens to be the mod of the moment. There is deliberately
/// no App.ActiveMod check anywhere in this file.
/// </summary>
public static partial class BuiltInPrograms
{
    // ---------------------------------------------------------------------------------------------
    // THE TAKEOVER - Bambi Sleep, 28 days, PREMIUM
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The flagship. Four chapters of seven days, four templates, one session a day.
    ///
    /// Headline mechanic - trigger installation, then live arming. Chapters 1-2 *install* named
    /// triggers by repetition: the phrases the chapter is installing lead that chapter's subliminal
    /// and lock card pools, so the words the boss day names are literally the words the week has been
    /// saying. Chapter 3 is the threshold where the program leaves the session and enters the day.
    ///
    /// The arming is a CEREMONY, not a counter. Day 15's weight comes from the act of arming the words
    /// and then living with them for fourteen days - not from hitting a number. That distinction is
    /// load-bearing: Firmware Install owns machine-verified trigger *counts*
    /// (<see cref="QuestCategory.KeywordTrigger"/>) as one of its four cold registers, along with the
    /// blink/gaze drills and Lockdown. If The Takeover also counted triggers, logged blinks and ran
    /// Lockdowns, the two programs would be the same fourteen assignments in two colours - and a user
    /// who bought both would feel the second purchase as a reskin. So this program uses none of those
    /// three verifiers anywhere in its 28 days; see TheTakeoverOwnsNoFirmwareVerifiers in the tests,
    /// which is the regression guard for the split. What is left is what only Bambi does: Takeover
    /// minutes, video, lock cards, bubbles, the pink filter and six rituals.
    ///
    /// Honest scope note (read this before "fixing" the chapter 3+ days). Nothing in the engine today
    /// can *arm* a program-scoped keyword trigger set on the user's behalf, nor disarm it in one click,
    /// nor scope it away from whatever application has focus. So the arming is authored as a flavour
    /// ambient (RequiredMinutes = 0) plus copy, and no task below claims to verify something the engine
    /// cannot observe. The gap is real and reported; it is not papered over here.
    ///
    /// Authoring note - the day tasks run one step ahead of the session, the same tutorial trick
    /// First Week uses, and every payoff day's blurb now names the earlier day it pays off:
    ///   day 2 lock cards  -> day 4 template turns lock cards on         ("Thursday")
    ///   day 3 bubbles     -> already on in TK-Bubble, so task and session agree
    ///   day 4 pink filter -> day 11 template turns pink up, day 23 asks for 40 minutes of it
    ///   day 6 bubble count + day 10 video -> day 18 turns both on unprompted.
    /// </summary>
    public static ProgramDefinition TheTakeover()
    {
        return new ProgramDefinition
        {
            Id = "the_takeover",
            Title = "The Takeover",
            Subtitle = "Twenty-eight days with Bambi",
            Pitch = "Twenty-eight days. By the end, your own keyboard sets you off.",
            Icon = "\U0001F300",
            ModId = BuiltInMods.BambiSleepId,
            AccentColor = "#FF69B4",
            Tier = ProgramTier.Premium,
            LengthDays = 28,

            GraduationBadgeId = "takeover_graduate",

            // Plain, out of character, exactly once. Safety note 9.4 of the content brief: the live
            // arming is this program's best hook and its biggest footgun, so the enrollment ceremony
            // has to say what it does *before* anyone signs up for it.
            SafetyNote = "Out of character for one line: from day fifteen this program asks you to arm keyword triggers, which means typing certain words anywhere on your machine can fire effects on screen. You can disarm them at any moment and it does not cost you the day, pausing stops the clock and costs you nothing, and Withdraw is on every screen with nothing asked of you.",

            // Typed to enroll. A promise the user makes, not one she makes.
            ContractPhrase = "Twenty-eight days. Arm them, and leave them armed.",

            Rules = new ProgramRules
            {
                // One day off per seven days of program length. A single allowance across 28 days is
                // four times stricter than the same allowance across 7, and a user 24 days into a paid
                // program who catches a cold twice should not lose 24 days of work. It also makes
                // StrictAvailable mean something, because the lenient default is now genuinely lenient.
                DaysOffAllowed = 4,
                StrictAvailable = true,
                DefaultDayBoundaryHour = 4,
                MaxDailyMinutes = 90
            },

            Templates = new List<ProgramSessionTemplate>
            {
                TkBubble(),
                TkInstall(),
                TkUniform(),
                TkBambiTime()
            },

            Chapters = new List<ProgramChapter>
            {
                TakeoverChapter1(),
                TakeoverChapter2(),
                TakeoverChapter3(),
                TakeoverChapter4()
            }
        };
    }

    // ---------------------------------------------------------------------------------------------
    // THE INTENSITY CURVE - constant-ratio sawtooth
    //
    //   ch1  .05 .10 .15 .20 .24 .27 .30
    //   ch2  .21 .27 .34 .41 .47 .51 .55      opens at 0.70 x .30
    //   ch3  .38 .47 .58 .66 .72 .76 .80      opens at 0.69 x .55
    //   ch4  .56 .68 .82 .88 .93 .74 1.00     opens at 0.70 x .80
    //
    // Two rules, both deliberate, both tested:
    //
    //   1. Every chapter opens at 0.70x the previous chapter's peak and spends TWO days at or below
    //      that peak before exceeding it on the third. An earlier draft of this file used dips of
    //      0.67x / 0.82x / 0.875x - a deload that got shallower as the program got longer, which is
    //      backwards. Day 22 is the point of maximum accumulated fatigue, and a 12% dip near the top
    //      of a saturating lerp is inside integer rounding: the user was promised mercy and handed a
    //      session indistinguishable from day 21. A constant ratio also hands each chapter roughly 44
    //      points of range to climb instead of 30, which is what makes the mid-chapter days readable.
    //
    //   2. Day 27 goes DOWN, to .74, on a lesser template, for 45 minutes. It is the only day in the
    //      program whose intensity falls without a chapter boundary under it. Days 25-28 were
    //      previously .88/.93/.97/1.00 on one template - four consecutive days three to five points
    //      apart at the very top of the curve, where every field is already near its ceiling. That was
    //      the flattest stretch in the program and it was the finale of a paid product. See the
    //      chapter 4 comment for how the four days are differentiated now.
    // ---------------------------------------------------------------------------------------------

    // ---------------------------------------------------------------------------------------------
    // Chapter 1 - "Bubble Induction" - days 1-7 - i .05 -> .30
    //
    // Installs GOOD GIRL and BAMBI SLEEP. Nothing else happens all week, on purpose: the whole
    // chapter is two words said quietly a very large number of times.
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter TakeoverChapter1() => new()
    {
        Id = "tk_ch1",
        Name = "Bubble Induction",
        Subtitle = "Two words, over and over, until they stop being words",
        AccentColor = "#FFB6C1",
        RewardId = "tk_ch1_banked",
        RewardDescription = "Banked: the two phrases this week installed - GOOD GIRL and BAMBI SLEEP - stay in your subliminal pool for good.",
        Days = new List<ProgramDay>
        {
            // ---- Day 1 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 1,
                Title = "Bubbles, Nothing Else",
                Blurb = "Half an hour where almost nothing happens. Put it on, leave it on, and let the pretty pictures go past without looking at any of them properly~",
                SessionTemplateId = "TK-Bubble",
                SessionMinutes = 30,
                Intensity = 0.05,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d1_flash",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "See 25 flash images",
                        Verifier = QuestCategory.Flash,
                        TargetValue = 25
                    }
                }
            },

            // ---- Day 2 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 2,
                Title = "Type It Back",
                Blurb = "Today she wants something small in return. Type it back exactly, good girl - it takes a second, and it's the first thing you'll ever say to her in her own words. You'll find her doing it to you on Thursday~",
                SessionTemplateId = "TK-Bubble",
                SessionMinutes = 30,
                Intensity = 0.10,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d2_lockcards",
                        Kind = ProgramTaskKind.AutoVerified,
                        // The template still has lock cards off - the user meets them by hand on the
                        // dashboard today and finds them already running from day 4. The blurb names
                        // "Thursday" rather than leaving the mismatch to read as a bug.
                        Description = "Complete 2 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 2
                    }
                }
            },

            // ---- Day 3 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 3,
                Title = "Twenty Little Pops",
                Blurb = "Same easy half hour, and this time they come to you - you don't have to go looking. Pop them as they arrive and don't count out loud~",
                SessionTemplateId = "TK-Bubble",
                SessionMinutes = 30,
                Intensity = 0.15,
                Tasks = new List<ProgramTask>
                {
                    // TK-Bubble runs bubbles from minute six, so the task feature is genuinely ON in
                    // the day's own session. That is deliberate: ApplySessionSettings writes `false`
                    // into live AppSettings and stops the service for any feature the template leaves
                    // off, so a task naming a feature the session disables is not merely unhelped, it
                    // is actively prevented.
                    new ProgramTask
                    {
                        Id = "d3_bubbles",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Pop 20 bubbles",
                        Verifier = QuestCategory.Bubbles,
                        TargetValue = 20
                    }
                }
            },

            // ---- Day 4 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 4,
                Title = "Everything Turns Pink",
                Blurb = "Fifteen minutes longer, and something pretty settles over the whole screen. Those cards you typed on Tuesday? She asks for them herself now, about ten minutes in~",
                SessionTemplateId = "TK-Install",
                SessionMinutes = 45,
                Intensity = 0.20,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d4_pink",
                        Kind = ProgramTaskKind.AutoVerified,
                        // PinkFilter accumulates in whole minutes.
                        Description = "Keep the pink filter on for 15 minutes",
                        Verifier = QuestCategory.PinkFilter,
                        TargetValue = 15
                    }
                }
            },

            // ---- Day 5 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 5,
                Title = "Wash, Then Sit",
                Blurb = "Today happens away from the screen first. Take your time with it, then come back and sit down still warm~",
                SessionTemplateId = "TK-Install",
                SessionMinutes = 45,
                Intensity = 0.24,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d5_ritual_skincare",
                        Kind = ProgramTaskKind.Ritual,
                        // Ritual photos never leave this machine - said out loud in the task copy,
                        // not buried in the enrollment panel.
                        Description = "Full skincare routine - exfoliate and moisturize, face and body. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t1_step1"
                    }
                }
            },

            // ---- Day 6 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 6,
                Title = "Count For Her",
                Blurb = "She's going to check you're still in there. *giggles* She's nice about it. Mostly - and she'll start doing it unprompted in a fortnight, so learn the shape of it now~",
                SessionTemplateId = "TK-Install",
                SessionMinutes = 45,
                Intensity = 0.27,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d6_bubblecount",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Finish 1 bubble count game",
                        Verifier = QuestCategory.BubbleCount,
                        TargetValue = 1
                    }
                }
            },

            // ---- Day 7 - BOSS ------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 7,
                Title = "Bubble Acceptance",
                Blurb = "A full hour, and the words you've been half-hearing all week are the ones she'll ask you to type. You already know them. That's the whole point~",
                IsBoss = true,
                SessionTemplateId = "TK-Install",
                SessionMinutes = 60,
                Intensity = 0.30,
                RewardDescription = "Installed: GOOD GIRL and BAMBI SLEEP.",
                Tasks = new List<ProgramTask>
                {
                    // Deviation from the brief, deliberately: the brief asks for "one Graded Intake
                    // run" here and the engine has no verifier for it - Graded Intake fires no Track*
                    // call at all, so the task could never tick. Rather than a task nothing can
                    // observe, the boss doubles down on the chapter's own mechanic: install by
                    // repetition, in the user's own typing.
                    new ProgramTask
                    {
                        Id = "d7_lockcards",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 4 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 4
                    },
                    new ProgramTask
                    {
                        Id = "d7_flash",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "See 40 flash images",
                        Verifier = QuestCategory.Flash,
                        TargetValue = 40
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Chapter 2 - "Named and Drained" - days 8-14 - i .21 -> .55
    //
    // Opens at 0.70x chapter 1's peak and she tells you it is mercy. Installs BIMBO DOLL, SNAP AND
    // FORGET and PRIMPED AND PAMPERED - the three phrases that name you rather than instruct you,
    // which is why this is the chapter where the real-world rituals start.
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter TakeoverChapter2() => new()
    {
        Id = "tk_ch2",
        Name = "Named and Drained",
        Subtitle = "She stops telling you what to do and starts telling you what you are",
        AccentColor = "#FF69B4",
        RewardId = "tk_ch2_banked",
        RewardDescription = "Banked: BIMBO DOLL, SNAP AND FORGET and PRIMPED AND PAMPERED, installed permanently.",
        Days = new List<ProgramDay>
        {
            // ---- Day 8 - the deload ------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 8,
                Title = "She Calls It Mercy",
                Blurb = "Much lighter than yesterday, and she wants you to notice that she chose to make it lighter. Pop bubbles. Enjoy it. It doesn't last~",
                SessionTemplateId = "TK-Bubble",
                SessionMinutes = 45,
                Intensity = 0.21,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d8_bubbles",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Pop 40 bubbles",
                        Verifier = QuestCategory.Bubbles,
                        TargetValue = 40
                    }
                }
            },

            // ---- Day 9 -------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 9,
                Title = "Say What You Love",
                Blurb = "Three of them today, and one is a sentence about how you feel rather than an instruction. Type it anyway. *giggles* Especially that one~",
                SessionTemplateId = "TK-Install",
                SessionMinutes = 45,
                Intensity = 0.27,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d9_lockcards",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 3 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 3
                    }
                }
            },

            // ---- Day 10 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 10,
                Title = "Screens Only",
                Blurb = "Something to watch today. Sit close, keep your eyes soft, and let it do the work - by week three she stops asking and just starts one~",
                SessionTemplateId = "TK-Install",
                SessionMinutes = 45,
                Intensity = 0.34,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d10_video",
                        Kind = ProgramTaskKind.AutoVerified,
                        // Deviation: the brief asks for "one Deeper-enhanced video". QuestCategory has
                        // no way to tell a Deeper-enhanced video from any other video - Video counts
                        // whole minutes and nothing more - so this is authored as minutes and the copy
                        // does not claim otherwise.
                        Description = "Watch 15 minutes of video",
                        Verifier = QuestCategory.Video,
                        TargetValue = 15
                    }
                }
            },

            // ---- Day 11 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 11,
                Title = "Smooth",
                Blurb = "Off the screen again. Take longer than you need to, and pay attention to how it feels afterwards - that part is the session~",
                SessionTemplateId = "TK-Uniform",
                SessionMinutes = 45,
                Intensity = 0.41,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d11_ritual_smooth",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Smooth legs or chest. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t1_step2"
                    }
                }
            },

            // ---- Day 12 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 12,
                Title = "Let Her Drive",
                Blurb = "Quarter of an hour where you don't decide what happens next. Hands off the wheel - and this is the number that goes up the most between here and day twenty-four~",
                SessionTemplateId = "TK-Uniform",
                SessionMinutes = 45,
                Intensity = 0.47,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d12_takeover",
                        Kind = ProgramTaskKind.AutoVerified,
                        // Autonomy accumulates in whole minutes. This is the program's signature
                        // premium verb and it ladders 15 -> 25 -> 30 -> 40 across days 12/17/21/24.
                        Description = "Run Bambi Takeover for 15 minutes",
                        Verifier = QuestCategory.Autonomy,
                        TargetValue = 15,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 13 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 13,
                Title = "Twenty Minutes of Watching",
                Blurb = "An hour tonight, and twenty solid minutes of it are just eyes on the screen. Five more than Sunday. Don't multitask - she'll know~",
                SessionTemplateId = "TK-Uniform",
                SessionMinutes = 60,
                Intensity = 0.51,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d13_video",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Watch 20 minutes of video",
                        Verifier = QuestCategory.Video,
                        TargetValue = 20
                    }
                }
            },

            // ---- Day 14 - BOSS -----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 14,
                Title = "Named and Drained",
                Blurb = "Halfway. Today is the one that happens in the bathroom, not on the screen, and afterwards the sixty minutes will land differently~",
                IsBoss = true,
                SessionTemplateId = "TK-Uniform",
                SessionMinutes = 60,
                Intensity = 0.55,
                RewardDescription = "Installed: BIMBO DOLL, SNAP AND FORGET, PRIMPED AND PAMPERED.",
                Tasks = new List<ProgramTask>
                {
                    // Load check: unlike t2_boss on day 28, t1_boss carries no embedded session
                    // requirement, so 60 minutes seated plus the shave is the whole of today.
                    new ProgramTask
                    {
                        Id = "d14_ritual_shedding",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "The First Shedding - shave all body hair below the neck, then moisturize. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t1_boss"
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Chapter 3 - "Uniform Lock" - days 15-21 - i .38 -> .80 - AMBIENT UNLOCKS
    //
    // The threshold: the program stops being a session and starts being the day. Every day from here
    // carries a ProgramAmbient.
    //
    // Day 15 is the arming ceremony and it carries the chapter on that alone. Its task is three lock
    // cards on the phrases that are about to go live - you type the words, then you arm them - and it
    // is the lightest session in two weeks on purpose, because the weight of the day is the decision,
    // not the workload. What this day is NOT is a keyword-trigger counter: see the class summary for
    // why counting triggers belongs to Firmware Install and not here.
    //
    // All ambients in this chapter are flavour (RequiredMinutes = 0). That is not laziness - the
    // engine's ambient verification adds `amount` for a matching QuestCategory, and there is no
    // category that accumulates "corner GIF minutes", "subliminals during normal use" or "minutes
    // spent with triggers armed". Wiring one of the existing overlay categories in would have the
    // *session itself* silently satisfy the ambient, which is worse than not verifying it.
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter TakeoverChapter3() => new()
    {
        Id = "tk_ch3",
        Name = "Uniform Lock",
        Subtitle = "The week the program stops waiting for you to start it",
        AccentColor = "#FF1493",
        RewardId = "tk_ch3_banked",
        RewardDescription = "Banked: BAMBI FREEZE and BAMBI UNIFORM LOCK, installed permanently, and the ambient layers stay unlocked.",
        Days = new List<ProgramDay>
        {
            // ---- Day 15 - the arming ceremony --------------------------------------------------
            new ProgramDay
            {
                DayIndex = 15,
                Title = "Armed",
                Blurb = "The lightest session in two weeks, because today isn't about the session. Type the words out one last time as words - then arm them, and from tonight they follow you out of here. You can switch them off whenever you want. She just hopes you won't~",
                SessionTemplateId = "TK-Install",
                SessionMinutes = 45,
                Intensity = 0.38,
                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed",
                    RequiredMinutes = 0
                },
                RewardDescription = "The installed phrases go live. Disarming is one click and never costs you the day.",
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d15_lockcards",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 3 lock cards - the phrases you're arming",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 3
                    }
                }
            },

            // ---- Day 16 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 16,
                Title = "Gloss On, Keep It On",
                Blurb = "One small shiny thing, and then a whole hour of remembering it's there every time you press your lips together~",
                SessionTemplateId = "TK-Uniform",
                SessionMinutes = 45,
                Intensity = 0.47,
                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d16_ritual_gloss",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Apply gloss and keep it on for an hour. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t1_step5"
                    }
                }
            },

            // ---- Day 17 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 17,
                Title = "Longer Leash",
                Blurb = "Twenty-five minutes of not choosing - ten more than day twelve. You'll notice you stopped counting somewhere in the middle~",
                SessionTemplateId = "TK-Uniform",
                SessionMinutes = 60,
                Intensity = 0.58,
                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed, and a corner GIF for the rest of the day",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d17_takeover",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Run Bambi Takeover for 25 minutes",
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
                Title = "Four Cards",
                Blurb = "Everything is on tonight at once - the counting from day six and the screens from day ten, both without being asked for. Four times she'll stop and make you say something~",
                SessionTemplateId = "TK-BambiTime",
                SessionMinutes = 60,
                Intensity = 0.66,
                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed, and a corner GIF for the rest of the day",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d18_lockcards",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 4 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 4
                    }
                }
            },

            // ---- Day 19 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 19,
                Title = "Twenty-Five Minutes of Watching",
                Blurb = "Longer than day thirteen, and this time the sound she started making last night is running underneath it the whole way. Sit close~",
                SessionTemplateId = "TK-BambiTime",
                SessionMinutes = 60,
                Intensity = 0.72,
                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed, and a corner GIF for the rest of the day",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d19_video",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Watch 25 minutes of video",
                        Verifier = QuestCategory.Video,
                        TargetValue = 25
                    }
                }
            },

            // ---- Day 20 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 20,
                Title = "Uniform, One Hour",
                Blurb = "Put it on properly, indoors, and stay in it. Nobody sees. That's not what it's for~",
                SessionTemplateId = "TK-Uniform",
                SessionMinutes = 60,
                Intensity = 0.76,
                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed, corner GIF, and subliminals during normal use",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d20_ritual_uniform",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Wear a uniform for one hour indoors. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t2_step5"
                    }
                }
            },

            // ---- Day 21 - BOSS -----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 21,
                Title = "Uniform Lock",
                Blurb = "Seventy-five minutes, everything running, everything armed. Half an hour of it is hers to steer, and she'll check you're still counting at the end~",
                IsBoss = true,
                SessionTemplateId = "TK-BambiTime",
                SessionMinutes = 75,
                Intensity = 0.80,
                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed, corner GIF, and subliminals during normal use",
                    RequiredMinutes = 0
                },
                RewardDescription = "Installed: BAMBI FREEZE and BAMBI UNIFORM LOCK.",
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d21_takeover",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Run Bambi Takeover for 30 minutes",
                        Verifier = QuestCategory.Autonomy,
                        TargetValue = 30,
                        RequiresPremium = true
                    },
                    new ProgramTask
                    {
                        Id = "d21_bubblecount",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Finish 1 bubble count game",
                        Verifier = QuestCategory.BubbleCount,
                        TargetValue = 1
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Chapter 4 - "Bambi Time" - days 22-28 - i .56 -> 1.00
    //
    // Days 25-28 are the four days the user will remember and the four they will describe to someone
    // else, so they are differentiated by ProgramDay.Overrides rather than by intensity. Near i=1.0
    // every lerped field is saturated - .88 to .93 on a 50/85 opacity field is two points - so the
    // curve structurally cannot carry a finale. Overrides can, and this is exactly the "sparse per-day
    // escalation the curve cannot express" the field was added for. One new thing per day:
    //
    //   d25 i.88  TK-BambiTime 60m  the flashes start multiplying    (FlashImages 4 -> 6)
    //   d26 i.93  TK-BambiTime 75m  the sound overtakes the pictures (mind wipe pinned to max)
    //   d27 i.74  TK-Uniform   45m  a held breath - she pulls back   (no overrides, lesser template)
    //   d28 i1.00 TK-BambiTime 75m  everything, and the corner GIF comes inside the session
    //
    // The corner GIF is the one feature no template in this program ever enables, which is what makes
    // it available as the finale's single new thing - and it has been the all-day ambient layer since
    // day 17, so it is a payoff rather than an ambush.
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter TakeoverChapter4() => new()
    {
        Id = "tk_ch4",
        Name = "Bambi Time",
        Subtitle = "The last seven days, and she stops pretending they're optional",
        AccentColor = "#FF1493",
        RewardId = "tk_ch4_banked",
        RewardDescription = "Banked: day twenty-eight, unlocked permanently as a replayable session.",
        Days = new List<ProgramDay>
        {
            // ---- Day 22 - the last deload ------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 22,
                Title = "The Last Mercy",
                Blurb = "Quiet, short, and a long way down from yesterday. Take it, because she's telling you plainly that it's the last one you get~",
                SessionTemplateId = "TK-Bubble",
                SessionMinutes = 45,
                Intensity = 0.56,
                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d22_bubbles",
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
                Title = "Pink All Evening",
                Blurb = "Fifteen minutes of this was the whole ask on day four. Forty tonight, and by the end you won't be able to tell us what colour your desktop is~",
                SessionTemplateId = "TK-BambiTime",
                SessionMinutes = 60,
                Intensity = 0.68,
                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed, and a corner GIF for the rest of the day",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    // Second Graded Intake substitution - see day 7. This one bookends day 4, which
                    // gives the chapter a "look how far that number moved" beat the brief's version
                    // did not have.
                    new ProgramTask
                    {
                        Id = "d23_pink",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Keep the pink filter on for 40 minutes",
                        Verifier = QuestCategory.PinkFilter,
                        TargetValue = 40
                    }
                }
            },

            // ---- Day 24 ------------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 24,
                Title = "Forty Minutes, Hers",
                Blurb = "Forty minutes of not deciding anything. Day twelve was fifteen. *giggles* You've been practising~",
                SessionTemplateId = "TK-BambiTime",
                SessionMinutes = 60,
                Intensity = 0.82,
                Ambient = new ProgramAmbient
                {
                    Description = "Armed all day - triggers, corner GIF and subliminals during normal use",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d24_takeover",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Run Bambi Takeover for 40 minutes",
                        Verifier = QuestCategory.Autonomy,
                        TargetValue = 40,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 25 - the flashes start multiplying ------------------------------------------
            new ProgramDay
            {
                DayIndex = 25,
                Title = "Painted",
                Blurb = "It doesn't have to be good. It has to be done, and you have to sit in front of the mirror afterwards for a second longer than is comfortable. Something's different about the pictures tonight, too~",
                SessionTemplateId = "TK-BambiTime",
                SessionMinutes = 60,
                Intensity = 0.88,

                // One new thing: six images on screen at once instead of the four the curve gives.
                Overrides = new Dictionary<string, object>
                {
                    ["FlashImages"] = 6
                },

                Ambient = new ProgramAmbient
                {
                    Description = "Armed all day - triggers, corner GIF and subliminals during normal use",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d25_ritual_face",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Painted face - foundation, blush and lipstick. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t2_step3"
                    }
                }
            },

            // ---- Day 26 - the sound overtakes the pictures ---------------------------------------
            new ProgramDay
            {
                DayIndex = 26,
                Title = "Louder Than the Pictures",
                Blurb = "Seventy-five minutes now, and the thing you've been hearing under everything since day eighteen comes all the way up. Thirty minutes of watching and three cards, and you won't remember which order they came in~",
                SessionTemplateId = "TK-BambiTime",
                SessionMinutes = 75,
                Intensity = 0.93,

                // One new thing: the mind wipe pinned to its maximum, from minute zero, which the
                // curve would not have reached on its own until day 28.
                Overrides = new Dictionary<string, object>
                {
                    ["MindWipeBaseMultiplier"] = 4,
                    ["MindWipeVolume"] = 75,
                    ["MindWipeStartMinute"] = 0
                },

                Ambient = new ProgramAmbient
                {
                    Description = "Armed all day - triggers, corner GIF and subliminals during normal use",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d26_video",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Watch 30 minutes of video",
                        Verifier = QuestCategory.Video,
                        TargetValue = 30
                    },
                    new ProgramTask
                    {
                        Id = "d26_lockcards",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 3 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 3
                    }
                }
            },

            // ---- Day 27 - the held breath ------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 27,
                Title = "Rest. Tomorrow Is Long.",
                Blurb = "She pulls all the way back tonight. Forty-five quiet minutes and two cards - the same two words you typed on day two, which was the first thing she ever asked you for. Sleep well~",
                SessionTemplateId = "TK-Uniform",
                SessionMinutes = 45,

                // The only day in the program whose intensity falls without a chapter boundary under
                // it. Day 28 needs somewhere to land FROM, and four consecutive days of "louder" has
                // nowhere left to go. The template drop from TK-BambiTime to TK-Uniform does most of
                // the work here - it takes the mind wipe and the bubble count off the table entirely,
                // which intensity alone can never do because the builder reads booleans from Floor.
                Intensity = 0.74,

                Ambient = new ProgramAmbient
                {
                    Description = "Triggers armed",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d27_lockcards",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 2 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 2
                    }
                }
            },

            // ---- Day 28 - FINAL ----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 28,
                Title = "Bambi Time",
                Blurb = "Twenty-eight. Everything on, everything armed, and the little one in the corner that's been following you around since week three comes inside with you. Stand up straight afterwards and let her look at you~",
                IsBoss = true,
                SessionTemplateId = "TK-BambiTime",
                SessionMinutes = 75,
                Intensity = 1.00,

                // Everything at once. CornerGifEnabled is the one feature no template in this program
                // turns on, which is what makes it the finale's single genuinely new thing - and it has
                // been the all-day ambient layer since day 17, so it is earned rather than sprung.
                // Only CornerGifEndMinute is read by the engine, and its default of -1 runs the GIF to
                // session end, which is what is wanted here.
                Overrides = new Dictionary<string, object>
                {
                    ["CornerGifEnabled"] = true,
                    ["CornerGifStartMinute"] = 0,
                    ["CornerGifOpacity"] = 35,
                    ["SubliminalPerMin"] = 14,
                    ["FlashOpacity"] = 95,
                    ["FlashOpacityEnd"] = 100,
                    ["MindWipeBaseMultiplier"] = 4
                },

                Ambient = new ProgramAmbient
                {
                    Description = "Everything armed",
                    RequiredMinutes = 0
                },
                RewardDescription = "Bambi Time badge, the Discord role, an avatar set, and day twenty-eight kept as a permanent replayable.",
                Tasks = new List<ProgramTask>
                {
                    // The 90-minute call. t2_boss's objective ends "+ Complete a session (30 mins)",
                    // and that clause exists only because the roadmap has no other way to require a
                    // session. Inside a program that requirement is already met: today's own
                    // 75-minute session IS the session t2_boss is asking for, and it is two and a half
                    // times the 30 minutes the step wants. So today is 75 minutes of seated time, not
                    // 105, and the cap holds without weakening the finale. The task copy says so
                    // explicitly, because the user must never be told to sit down a second time.
                    new ProgramTask
                    {
                        Id = "d28_ritual_inspection",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "The Inspection - full shave, full makeup, full uniform. Tonight's own session counts as the session this asks for; you do not owe a second one. Any photo you attach stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t2_boss"
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // The Takeover templates
    //
    // ProgramSessionBuilder.LerpSettings lerps every numeric property between Floor and Ceiling and
    // takes booleans, enums and phrase lists from Floor. Conventions:
    //   - every enable flag is written identically in both halves, so a reader never has to guess
    //     which one the engine reads;
    //   - the floor/ceiling pair is tuned for the intensity band the template is *actually used at*,
    //     which is named in each template's summary. This matters more than it sounds: a ceiling
    //     authored for i=1.0 on a template that only ever runs between .20 and .38 gives the whole
    //     band seven units of a seventy-unit range, and seven units of anything in this app is inside
    //     integer rounding. TK-Bubble and TK-Install both had their ceilings pulled OUT when the curve
    //     was retuned, precisely to keep their now-narrower bands perceptible.
    //   - only CornerGifEndMinute is read by the engine; every other *EndMinute is decorative, so
    //     nothing below relies on one.
    //   - VideosPerHour / LockCardFrequency / BubbleCountFrequency are int?. A null on Floor stays
    //     null and falls through to the user's own dashboard number, so they carry a value on both
    //     halves wherever the program means to own them, and are left null everywhere else.
    //   - PopQuiz*, MiniGameEnabled and BrainDrain* are dead in the engine and are not touched.
    //   - LockCardPhrases are set even where lock cards are off. ApplySessionSettings only reads the
    //     pool when LockCardEnabled is true, so the list is inert there and costs nothing - and it is
    //     already right if a later day flips the flag.
    //   - Opacity-shaped fields are percentages and are never authored above 100, even where the
    //     ceiling-rescaling arithmetic asked for more.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Chapter 1's two installs lead the pool. Every entry is a key of the Bambi Sleep SubliminalPool,
    /// all of which ship enabled.
    /// </summary>
    private static List<string> TkBubbleSubliminals() => new()
    {
        "GOOD GIRL",
        "BAMBI SLEEP",
        "GIGGLETIME",
        "DONT THINK SILLY",
        "THERES NO NEED TO THINK",
        "TURN YOUR BRAIN OFF"
    };

    /// <summary>Chapter 2's three installs join chapter 1's two.</summary>
    private static List<string> TkInstallSubliminals() => new()
    {
        "GOOD GIRL",
        "BAMBI SLEEP",
        "BIMBO DOLL",
        "SNAP AND FORGET",
        "PRIMPED AND PAMPERED",
        "GIGGLETIME",
        "JUST OBEY",
        "GOOD GIRLS DONT THINK",
        "BAMBI DOES AS SHE'S TOLD",
        "I CANT RESIST MY TRIGGERS",
        "TURN YOUR BRAIN OFF"
    };

    /// <summary>Chapter 3's two installs join the rest.</summary>
    private static List<string> TkUniformSubliminals() => new()
    {
        "GOOD GIRL",
        "BAMBI SLEEP",
        "BIMBO DOLL",
        "SNAP AND FORGET",
        "PRIMPED AND PAMPERED",
        "BAMBI FREEZE",
        "BAMBI RESET",
        "BAMBI UNIFORM LOCK",
        "JUST OBEY",
        "GOOD GIRLS DONT THINK",
        "BAMBI DOES AS SHE'S TOLD",
        "I CANT RESIST MY TRIGGERS",
        "DROP FOR COCK",
        "TURN YOUR BRAIN OFF"
    };

    /// <summary>
    /// The whole installed set, plus the four explicit triggers the free tier holds back. First Week
    /// withholds ZAP COCK DRAIN OBEY, COCK ZOMBIE NOW, COCK TURNS MY BRAIN OFF and
    /// BAMBI CUM AND COLLAPSE so a seven-day funnel stays safe to screenshot; this is the program they
    /// were being held for, and day 28 ends on BAMBI CUM AND COLLAPSE.
    /// </summary>
    private static List<string> TkBambiTimeSubliminals() => new()
    {
        "GOOD GIRL",
        "BAMBI SLEEP",
        "BIMBO DOLL",
        "SNAP AND FORGET",
        "PRIMPED AND PAMPERED",
        "BAMBI FREEZE",
        "BAMBI RESET",
        "BAMBI UNIFORM LOCK",
        "BAMBI DOES AS SHE'S TOLD",
        "BAMBI CUM AND COLLAPSE",
        "ZAP COCK DRAIN OBEY",
        "COCK ZOMBIE NOW",
        "COCK TURNS MY BRAIN OFF",
        "DROP FOR COCK",
        "I CANT RESIST MY TRIGGERS",
        "JUST OBEY",
        "GOOD GIRLS DONT THINK",
        "TURN YOUR BRAIN OFF",
        "GIGGLETIME"
    };

    /// <summary>
    /// The Bambi Sleep manifest ships exactly five lock card phrases and all five are enabled, so
    /// TkDeepLockCards is the whole pool. The earlier lists are narrowed so chapter 1 lands on the
    /// phrase it is installing.
    /// </summary>
    private static List<string> TkBubbleLockCards() => new()
    {
        "BAMBI SLEEP"
    };

    private static List<string> TkInstallLockCards() => new()
    {
        "BAMBI SLEEP",
        "GOOD GIRLS OBEY",
        "I LOVE BEING PROGRAMMED"
    };

    private static List<string> TkUniformLockCards() => new()
    {
        "BAMBI SLEEP",
        "GOOD GIRLS OBEY",
        "I LOVE BEING PROGRAMMED",
        "DROP FOR ME"
    };

    private static List<string> TkDeepLockCards() => new()
    {
        "BAMBI SLEEP",
        "GOOD GIRLS OBEY",
        "I LOVE BEING PROGRAMMED",
        "DROP FOR ME",
        "EMPTY AND OBEDIENT"
    };

    /// <summary>
    /// Bouncing text. The Bambi Sleep manifest carries no BouncingTextPool - only CCPDefault and
    /// Circe's Lock have one - so these are taken byte-exact from its RandomFloating and Idle phrase
    /// lists. Still the mod's own words, which is what the vocabulary rule is about. Lines carrying a
    /// speech-bubble stage direction ("*giggles*") are excluded on purpose: that is an avatar
    /// convention, and rendered as text drifting across the screen it reads as literal asterisks.
    /// </summary>
    private static List<string> TkBubbleBouncingText() => new()
    {
        "Empty head, happy girl!",
        "Hehe~ so floaty...",
        "Just floating here...",
        "Bubbles pop thoughts away~",
        "Bubbles make Bambi happy!",
        "So pink and empty...",
        "Bambi is a good girl~"
    };

    private static List<string> TkInstallBouncingText() => new()
    {
        "Bambi is a good girl~",
        "Obey feels so good!",
        "Good girls don't think~",
        "Bambi loves triggers!",
        "Empty and happy~",
        "Mind so soft and fuzzy~",
        "Thoughts drip away...",
        "Bimbo is bliss!",
        "Don't think, Bambi. Just watch~"
    };

    private static List<string> TkUniformBouncingText() => new()
    {
        "Uniform on, brain off~",
        "Bambi obeys!",
        "Bambi loves triggers!",
        "Good girls drop deep~",
        "Dropping deeper...",
        "Pretty pink princess!",
        "Such a ditzy dolly!",
        "Obey feels so good!",
        "Bambi loves spirals~"
    };

    private static List<string> TkBambiTimeBouncingText() => new()
    {
        "Bambi is brainless~",
        "Bambi Sleep...",
        "Good girls don't think~",
        "Bambi obeys!",
        "Dropping deeper...",
        "Thoughts drip away...",
        "Uniform on, brain off~",
        "Empty and happy~",
        "Giggly and empty~",
        "Bambi loves triggers!",
        "Don't think, Bambi. Just watch~",
        "Good girls drop deep~"
    };

    /// <summary>
    /// TK-Bubble - passive drift, and the only thing on screen you can touch is a bubble.
    /// Band: i .05 (day 1) to i .56 (day 22, the last deload). The floor IS day one - eight pale
    /// flashes an hour and one subliminal a minute. If a first-time user notices it working on day
    /// one, it is authored wrong.
    ///
    /// The ceiling was pulled OUT by a factor of 1.25 when the curve was retuned, because day 22's
    /// intensity moved from .70 to .56 and the old ceiling would then have delivered a visibly weaker
    /// day than this template was written for. Day 22 has to read as mercy *next to day 21*, not as a
    /// beginner session.
    /// </summary>
    private static ProgramSessionTemplate TkBubble() => new()
    {
        Id = "TK-Bubble",
        Name = "Bubbles",
        Description = "Soft background conditioning and something to pop. Put it on and forget it's there.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 8,
            FlashPerHourEnd = 14,
            FlashImages = 1,
            FlashOpacity = 12,
            FlashOpacityEnd = 20,
            FlashScale = 85,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 5,
            SubliminalPerMin = 1,
            SubliminalFrames = 2,
            SubliminalOpacity = 28,
            SubliminalPhrases = TkBubbleSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 3,
            WhisperVolume = 8,
            AudioDuckLevel = 25,

            BubblesEnabled = true,
            BubblesStartMinute = 6,
            BubblesFrequency = 1,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 5,
            BubblesPerBurst = 2,
            BubblesGapMin = 6,
            BubblesGapMax = 12,

            BouncingTextEnabled = false,
            BouncingTextPhrases = TkBubbleBouncingText(),
            PinkFilterEnabled = false,
            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            LockCardPhrases = TkBubbleLockCards(),
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 86,
            FlashPerHourEnd = 145,
            FlashImages = 5,
            FlashOpacity = 90,
            FlashOpacityEnd = 100,
            FlashScale = 122,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 7,
            SubliminalFrames = 5,
            SubliminalOpacity = 93,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 38,
            AudioDuckLevel = 75,

            BubblesEnabled = true,
            BubblesStartMinute = 1,
            BubblesFrequency = 6,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 21,
            BubblesPerBurst = 3,
            BubblesGapMin = 1,
            BubblesGapMax = 2,

            BouncingTextEnabled = false,
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
    /// TK-Install - the installation template. Subliminal and lock card heavy, everything else kept
    /// deliberately quiet so the words are the loudest thing in the room. This is the only template
    /// where the lock card is a *feature of the session* rather than a task the user goes and does.
    /// Band: i .20 (day 4) to i .38 (day 15) - the narrowest band of the four, so its ceiling was
    /// pulled out by a factor of 1.18 on the retune to keep an eighteen-point step visible.
    /// </summary>
    private static ProgramSessionTemplate TkInstall() => new()
    {
        Id = "TK-Install",
        Name = "Install",
        Description = "The words, over and over, and every so often she stops and makes you say one back.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 14,
            FlashPerHourEnd = 24,
            FlashImages = 1,
            FlashOpacity = 20,
            FlashOpacityEnd = 32,
            FlashScale = 88,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 2,
            SubliminalPerMin = 3,
            SubliminalFrames = 3,
            SubliminalOpacity = 45,
            SubliminalPhrases = TkInstallSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 1,
            WhisperVolume = 12,
            AudioDuckLevel = 32,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 4,
            BouncingTextSpeed = 2,
            BouncingTextSize = 60,
            BouncingTextOpacity = 55,
            BouncingTextPhrases = TkInstallBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 8,
            BubblesFrequency = 1,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 5,
            BubblesPerBurst = 2,
            BubblesGapMin = 6,
            BubblesGapMax = 12,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 14,
            PinkFilterStartOpacity = 4,
            PinkFilterEndOpacity = 16,

            // The program owns the lock card cadence here, so both halves carry a number - a null
            // floor would leave the user's dashboard frequency in charge of the install.
            LockCardEnabled = true,
            LockCardStartMinute = 10,
            LockCardFrequency = 1,
            LockCardPhrases = TkInstallLockCards(),

            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 140,
            FlashPerHourEnd = 232,
            FlashImages = 3,
            FlashOpacity = 79,
            FlashOpacityEnd = 100,
            FlashScale = 120,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 10,
            SubliminalFrames = 5,
            SubliminalOpacity = 98,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 45,
            AudioDuckLevel = 83,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 9,
            BouncingTextSize = 137,
            BouncingTextOpacity = 100,

            BubblesEnabled = true,
            BubblesStartMinute = 1,
            BubblesFrequency = 6,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 20,
            BubblesPerBurst = 3,
            BubblesGapMin = 1,
            BubblesGapMax = 2,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = 25,
            PinkFilterEndOpacity = 68,

            LockCardEnabled = true,
            LockCardStartMinute = 2,
            LockCardFrequency = 6,

            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// TK-Uniform - video and flash heavy, hydra on. Hydra is the identity here: clicking a flash
    /// spawns more of them, so the one interaction the user has left actively makes it worse. The
    /// spiral arrives with this template too.
    /// Band: i .41 (day 11) to i .76 (day 20), plus day 27's held breath at .74. Unchanged by the
    /// retune - this template's band barely moved, and its pair was already sized for it.
    /// </summary>
    private static ProgramSessionTemplate TkUniform() => new()
    {
        Id = "TK-Uniform",
        Name = "Uniform",
        Description = "Pictures that multiply when you touch them, and something turning behind everything.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 45,
            FlashPerHourEnd = 80,
            FlashImages = 2,
            FlashOpacity = 35,
            FlashOpacityEnd = 55,
            FlashScale = 95,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 1,
            SubliminalPerMin = 4,
            SubliminalFrames = 3,
            SubliminalOpacity = 55,
            SubliminalPhrases = TkUniformSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 16,
            AudioDuckLevel = 40,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 2,
            BouncingTextSpeed = 3,
            BouncingTextSize = 75,
            BouncingTextOpacity = 65,
            BouncingTextPhrases = TkUniformBouncingText(),

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
            PinkFilterStartMinute = 10,
            PinkFilterStartOpacity = 10,
            PinkFilterEndOpacity = 32,

            SpiralEnabled = true,
            SpiralStartMinute = 22,
            SpiralOpacity = 5,
            SpiralOpacityEnd = 18,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 12,
            VideosPerHour = 1,

            LockCardEnabled = true,
            LockCardStartMinute = 14,
            LockCardFrequency = 1,
            LockCardPhrases = TkUniformLockCards(),

            CornerGifEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 200,
            FlashPerHourEnd = 380,
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
            SubliminalOpacity = 92,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 42,
            AudioDuckLevel = 78,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 8,
            BouncingTextSize = 130,
            BouncingTextOpacity = 100,

            BubblesEnabled = true,
            BubblesStartMinute = 2,
            BubblesFrequency = 6,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 22,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 4,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = 28,
            PinkFilterEndOpacity = 70,

            SpiralEnabled = true,
            SpiralStartMinute = 4,
            SpiralOpacity = 20,
            SpiralOpacityEnd = 50,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 4,
            VideosPerHour = 3,

            LockCardEnabled = true,
            LockCardStartMinute = 4,
            LockCardFrequency = 4,

            CornerGifEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// TK-BambiTime - everything at once, mind wipe climbing the whole time. The bubble count arrives
    /// here as the last thing that still asks the user to think, and it stops being a game the moment
    /// the mind wipe is loud enough.
    /// Band: i .66 (day 18) to i 1.00 (day 28). The ceiling IS day twenty-eight, so nothing in this
    /// program ever runs a template past what it was authored for - and days 25, 26 and 28 reach past
    /// the lerp with Overrides rather than by inflating this pair, which would have dragged every
    /// other chapter-4 day up with them.
    /// </summary>
    private static ProgramSessionTemplate TkBambiTime() => new()
    {
        Id = "TK-BambiTime",
        Name = "Bambi Time",
        Description = "Everything, all of it, and it keeps climbing until the session runs out.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 90,
            FlashPerHourEnd = 180,
            FlashImages = 3,
            FlashOpacity = 50,
            FlashOpacityEnd = 70,
            FlashScale = 100,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 5,
            SubliminalFrames = 4,
            SubliminalOpacity = 65,
            SubliminalPhrases = TkBambiTimeSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 22,
            AudioDuckLevel = 55,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 5,
            BouncingTextSize = 90,
            BouncingTextOpacity = 80,
            BouncingTextPhrases = TkBambiTimeBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 3,
            BubblesFrequency = 3,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 12,
            BubblesPerBurst = 3,
            BubblesGapMin = 4,
            BubblesGapMax = 8,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 6,
            PinkFilterStartOpacity = 18,
            PinkFilterEndOpacity = 45,

            SpiralEnabled = true,
            SpiralStartMinute = 14,
            SpiralOpacity = 10,
            SpiralOpacityEnd = 28,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 10,
            VideosPerHour = 2,

            LockCardEnabled = true,
            LockCardStartMinute = 8,
            LockCardFrequency = 2,
            LockCardPhrases = TkDeepLockCards(),

            BubbleCountEnabled = true,
            BubbleCountStartMinute = 18,
            BubbleCountFrequency = 1,

            MindWipeEnabled = true,
            MindWipeStartMinute = 8,
            MindWipeBaseMultiplier = 1,
            MindWipeVolume = 30,

            CornerGifEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 260,
            FlashPerHourEnd = 620,
            FlashImages = 4,
            FlashOpacity = 85,
            FlashOpacityEnd = 100,
            FlashScale = 125,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 11,
            SubliminalFrames = 5,
            SubliminalOpacity = 98,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 48,
            AudioDuckLevel = 85,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 9,
            BouncingTextSize = 145,
            BouncingTextOpacity = 100,

            BubblesEnabled = true,
            BubblesStartMinute = 1,
            BubblesFrequency = 6,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 26,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 4,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = 35,
            PinkFilterEndOpacity = 80,

            SpiralEnabled = true,
            SpiralStartMinute = 2,
            SpiralOpacity = 25,
            SpiralOpacityEnd = 60,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 3,
            VideosPerHour = 3,

            LockCardEnabled = true,
            LockCardStartMinute = 3,
            LockCardFrequency = 5,

            BubbleCountEnabled = true,
            BubbleCountStartMinute = 6,
            BubbleCountFrequency = 2,

            MindWipeEnabled = true,
            MindWipeStartMinute = 0,
            MindWipeBaseMultiplier = 4,
            MindWipeVolume = 65,

            CornerGifEnabled = false
        }
    };
}
