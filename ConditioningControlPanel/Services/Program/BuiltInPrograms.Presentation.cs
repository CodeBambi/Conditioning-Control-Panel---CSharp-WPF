using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// PRESENTATION - Sissy Hypno, 14 days, Premium.
///
/// Vocabulary rule, same as every other program in this folder: every subliminal line, lock card
/// phrase and bouncing-text line below is lifted verbatim from <see cref="BuiltInMods.SissyHypno"/>.
/// Nothing is invented.
///
/// No active-mod dependency: the definition carries its whole vocabulary, so this reads and runs
/// identically whether or not Sissy Hypno happens to be the mod of the moment. There is deliberately
/// no App.ActiveMod check anywhere in this file.
/// </summary>
public static partial class BuiltInPrograms
{
    // ---------------------------------------------------------------------------------------------
    // PRESENTATION - Sissy Hypno, 14 days, PREMIUM
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Two chapters of seven days. The inverse of every other program in the box: here the *task*
    /// leads and the session supports.
    ///
    /// SEVEN rituals across fourteen days, alternating with seven lighter screen-only days. An earlier
    /// draft ran eleven rituals in fourteen days, borrowing nearly the whole roadmap step library, and
    /// that was the single most dangerous thing about the program. The steps it borrowed are authored
    /// on a month-scale horizon - "Shave legs *for the first time*", "Shave all body hair", "Full
    /// Shave" - and stacking those on days 2, 7 and 14 asked for three full-body grooming events in
    /// twelve days. Roadmap tracks 1 and 2 are gated on each other by design ("Unlock: Track 1 Boss
    /// Photo Uploaded") and collapsing both into a fortnight dissolves that gate. This program's
    /// failure mode was never boredom; it was a user quitting on day nine because the app had asked
    /// for eleven photographs and a second full-body shave. Seven rituals with a light day between
    /// each reads as generous rather than relentless, and it gives the session/task inversion room to
    /// actually breathe.
    ///
    /// The ladder is skin -> lips -> posture -> (the one shedding) -> face -> eyes -> the full look.
    /// Two full-body grooming events, seven days apart, which is a plausible maintenance interval
    /// rather than an escalation.
    ///
    /// Headline mechanic - the photo ledger, ending in a day 1 / day 14 diptych. Days 1 and 14 borrow
    /// different roadmap steps (the objectives genuinely differ - "before" is bare skin, "after" is the
    /// full look) but their task copy asks for the SAME SHOT in the same framing, spelled out
    /// identically in both descriptions. A diptych of two different poses compares nothing.
    ///
    /// Honest scope note: today's engine has ProgramTaskKind.Ritual and the roadmap diary, which is
    /// exactly enough to *collect* seven photographs in order and keep them local. It has nothing that
    /// composes two of them into a diptych at graduation, and ProgramService.SubmitRitualTask only
    /// files a photo when the borrowed step is the user's currently-active roadmap step. Both gaps are
    /// engine work, both are reported, and neither is faked anywhere in this file.
    ///
    /// Photo safety (content brief section 9.2): ritual photos never leave this machine. Every ritual
    /// task below says so in its own copy, not only in the enrollment ceremony, because for this
    /// audience that sentence is the difference between a feature and a liability.
    /// </summary>
    public static ProgramDefinition Presentation()
    {
        return new ProgramDefinition
        {
            Id = "presentation",
            Title = "Presentation",
            Subtitle = "Fourteen days, seven photographs",
            Pitch = "Two weeks. Seven photographs. Look at day one when you're done.",
            Icon = "\U0001F48B",
            ModId = BuiltInMods.SissyHypnoId,
            AccentColor = "#9B59B6",
            Tier = ProgramTier.Premium,
            LengthDays = 14,

            GraduationBadgeId = "presentation_graduate",

            // Plain, out of character, exactly once. Content brief section 9.2: stated at enrollment,
            // never buried in settings.
            SafetyNote = "Out of character for one line: this program asks you for photographs, and they never leave this machine. They are saved locally in your own diary, never uploaded, never synced to any account, never rendered onto a share card, and you can delete any of them at any time. Withdraw is on every screen with nothing asked of you.",

            // Typed to enroll. A promise the user makes, not one she makes.
            ContractPhrase = "Fourteen days, and I don't skip the mirror.",

            Rules = new ProgramRules
            {
                // One day off per seven days of program length, so a fortnight gets two. The same
                // single allowance that suits a 7-day program is twice as strict across 14 days, and a
                // user eleven days into a paid program who has a bad week should not lose eleven days.
                DaysOffAllowed = 2,
                StrictAvailable = true,
                DefaultDayBoundaryHour = 4,
                MaxDailyMinutes = 90
            },

            Templates = new List<ProgramSessionTemplate>
            {
                PrSoft(),
                PrMirror(),
                PrDress(),
                PrShow()
            },

            Chapters = new List<ProgramChapter>
            {
                PresentationChapter1(),
                PresentationChapter2()
            }
        };
    }

    // ---------------------------------------------------------------------------------------------
    // THE INTENSITY CURVE - constant-ratio sawtooth
    //
    //   ch1  .05 .12 .18 .24 .28 .32 .35
    //   ch2  .25 .31 .40 .49 .57 .64 .70     opens at 0.71 x .35
    //
    // Chapter 2 opens at 0.70x chapter 1's peak and spends two days at or below that peak before
    // exceeding it on the third (day 10, .40). An earlier draft opened at .28 - a 0.80x dip one day
    // wide - which is inside integer rounding on a saturating lerp, so the "deload" was a promise the
    // numbers did not keep.
    //
    // The program tops out at .70 and NOT at 1.00, deliberately. A fortnight should not claim the same
    // terminal intensity as the 28-day flagship, and this program's last week is spent in front of a
    // mirror rather than in front of a mind wipe. There is a test pinning that so nobody "fixes" it
    // upward.
    // ---------------------------------------------------------------------------------------------

    // ---------------------------------------------------------------------------------------------
    // Chapter 1 - "Softening" - days 1-7 - i .05 -> .35
    //
    // Four rituals (days 1, 3, 5, 7) alternating with three screen days (2, 4, 6). The whole week is
    // skin and small shiny things, and the sessions are quiet enough that the user's memory of the week
    // will be of the bathroom, not the monitor. That is the design, not a shortfall.
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter PresentationChapter1() => new()
    {
        Id = "pr_ch1",
        Name = "Softening",
        Subtitle = "A week of skin, and one photograph you'll want back later",
        AccentColor = "#BB8FCE",
        RewardId = "pr_ch1_banked",
        RewardDescription = "Banked: the first four pages of your ledger, kept locally, yours to delete whenever you like.",
        Days = new List<ProgramDay>
        {
            // ---- Day 1 - the day-one photograph ------------------------------------------------
            new ProgramDay
            {
                DayIndex = 1,
                Title = "The Blank Slate",
                Blurb = "Today's photograph is the one the whole two weeks gets measured against, so take it before you change a single thing. Stand square to the mirror, arms loose, and don't pose. Then a very quiet half hour~",
                SessionTemplateId = "PR-Soft",
                SessionMinutes = 30,
                Intensity = 0.05,
                RewardDescription = "Page one of the ledger.",
                Tasks = new List<ProgramTask>
                {
                    // The "before" half of the diptych. The framing sentence here is repeated
                    // word-for-word on day 14 - a diptych of two different poses compares nothing, so
                    // the instruction has to be identical at both ends and it has to be explicit.
                    new ProgramTask
                    {
                        Id = "d1_ritual_blank_slate",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "The Blank Slate - full exfoliate and moisturize, face and body. This is your day-one photograph: stand square to the mirror, feet together, arms loose at your sides, camera at chest height. It stays on this machine: never uploaded, never synced, never on a share card, and you can delete it whenever you want.",
                        RoadmapStepId = "t1_step1"
                    }
                }
            },

            // ---- Day 2 - light -----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 2,
                Title = "Say It Back",
                Blurb = "Nothing to do off-screen today - yesterday was enough. Twice tonight she'll stop everything and wait for you to type it back exactly~",
                SessionTemplateId = "PR-Soft",
                SessionMinutes = 30,
                Intensity = 0.12,

                // PR-Soft leaves lock cards off, and SessionEngine.ApplySessionSettings does not merely
                // fail to provide a feature the template omits - it writes `false` into live AppSettings
                // and stops the service. So without this, a user who typed their two cards on the
                // dashboard and then pressed Start would watch the feature switch itself off and the
                // counter stall for thirty minutes.
                //
                // It is also what gives day 2 a perceptible change at all: on PR-Soft, .05 to .12 is a
                // handful of flashes and one extra subliminal a minute. Two quiet cards near the end is
                // something the user will actually notice, and it is the day's own title.
                Overrides = new Dictionary<string, object>
                {
                    ["LockCardEnabled"] = true,
                    ["LockCardStartMinute"] = 18,
                    ["LockCardFrequency"] = 1
                },

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

            // ---- Day 3 - ritual ----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 3,
                Title = "Glossy",
                Blurb = "One small shiny thing, and then an hour of being reminded it's there every time you press your lips together. Photograph the mouth only~",
                SessionTemplateId = "PR-Mirror",
                SessionMinutes = 30,
                Intensity = 0.18,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d3_ritual_gloss",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Glossy Lips - apply gloss and keep it on for one hour. Photograph the mouth only, close up. Any photo stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t1_step5"
                    }
                }
            },

            // ---- Day 4 - light -----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 4,
                Title = "Fifteen Pink Minutes",
                Blurb = "A screen day. Put the pink on, leave it on while you do whatever you were going to do anyway, and let it stop being a colour you notice~",
                SessionTemplateId = "PR-Mirror",
                SessionMinutes = 45,
                Intensity = 0.24,
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

            // ---- Day 5 - ritual ----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 5,
                Title = "Sit Like a Doll",
                Blurb = "Fifteen minutes of not moving. Knees together, back straight, hands on your lap. It's the easiest thing on the whole list and the hardest to actually do~",
                SessionTemplateId = "PR-Mirror",
                SessionMinutes = 45,
                Intensity = 0.28,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d5_ritual_posture",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Doll Posture - hold the pose for 15 minutes, then photograph your hands resting on your lap. Any photo stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t1_step6"
                    }
                }
            },

            // ---- Day 6 - light -----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 6,
                Title = "Three Times, Exactly",
                Blurb = "Screens again, and longer words tonight than on Monday. Three of them, typed exactly, and one of them is about being empty~",
                SessionTemplateId = "PR-Dress",
                SessionMinutes = 45,
                Intensity = 0.32,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d6_lockcards",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 3 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 3
                    }
                }
            },

            // ---- Day 7 - BOSS ------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 7,
                Title = "The First Shedding",
                Blurb = "The big one, and the only one of its kind this week. Everything below the neck, then moisturize, then look at yourself properly. Halfway, babe~",
                IsBoss = true,
                SessionTemplateId = "PR-Dress",
                SessionMinutes = 45,
                Intensity = 0.35,
                RewardDescription = "Half the ledger, and the ambient layers unlock the day after tomorrow.",
                Tasks = new List<ProgramTask>
                {
                    // Load check: t1_boss carries no embedded session requirement, unlike t2_boss on
                    // day 14, so 45 minutes seated plus the shave is the whole of today.
                    new ProgramTask
                    {
                        Id = "d7_ritual_shedding",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "The First Shedding - shave all body hair below the neck, then moisturize. Any photo stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t1_boss"
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Chapter 2 - "Presentation" - days 8-14 - i .25 -> .70 - AMBIENT UNLOCKS
    //
    // Three rituals (days 10, 12, 14) and four lighter days. The rituals move from grooming to
    // *presenting*: face, then eyes, then the full look.
    //
    // Deviation from the brief, stated plainly: section 1.4 says "ambient layers unlock at chapter 3
    // in every program", but section 4 - the section that actually authors this program - puts the
    // unlock in chapter 2, and a 14-day program has no chapter 3 to put it in. Section 4 wins; the
    // general rule is really "ambient unlocks at the halfway threshold". Day 8 is the deload and gets
    // nothing: the unlock is supposed to feel like a threshold, and handing it out on the rest day
    // spends the beat for free.
    //
    // All ambients here are flavour (RequiredMinutes = 0), for the same reason as The Takeover's:
    // there is no QuestCategory that accumulates corner-GIF minutes, out-of-session subliminal minutes
    // or minutes-with-triggers-armed, and borrowing one of the overlay categories would let the
    // *session* silently satisfy the ambient. Not verifying is more honest than verifying the wrong
    // thing.
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter PresentationChapter2() => new()
    {
        Id = "pr_ch2",
        Name = "Presentation",
        Subtitle = "The week you stop preparing and start being looked at",
        AccentColor = "#9B59B6",
        RewardId = "pr_ch2_banked",
        RewardDescription = "Banked: the full seven-page ledger and a phrase pack, kept locally.",
        Days = new List<ProgramDay>
        {
            // ---- Day 8 - the deload ------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 8,
                Title = "Breathe, Babe",
                Blurb = "A long way down from yesterday, and nothing at all to do off-screen. Put the pink on, leave it on, and let last night catch up with you~",
                SessionTemplateId = "PR-Soft",
                SessionMinutes = 30,
                Intensity = 0.25,

                // Same reason as day 2: PR-Soft has no pink filter of its own, so the day's task would
                // be switched off by the day's own session. Pink over everything is also exactly the
                // right texture for a deload - it is the least demanding thing the app can put on a
                // screen, and it is what the blurb promises.
                Overrides = new Dictionary<string, object>
                {
                    ["PinkFilterEnabled"] = true,
                    ["PinkFilterStartMinute"] = 2,
                    ["PinkFilterStartOpacity"] = 12,
                    ["PinkFilterEndOpacity"] = 26
                },

                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d8_pink",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Keep the pink filter on for 20 minutes",
                        Verifier = QuestCategory.PinkFilter,
                        TargetValue = 20
                    }
                }
            },

            // ---- Day 9 - light, ambient unlocks ------------------------------------------------
            new ProgramDay
            {
                DayIndex = 9,
                Title = "It Follows You Out",
                Blurb = "Fifteen minutes of watching tonight, and from today the words don't stop when the session does. They'll turn up while you're doing something else entirely~",
                SessionTemplateId = "PR-Mirror",
                SessionMinutes = 45,
                Intensity = 0.31,

                // PR-Mirror is the talking template and runs no video of its own, so the day's fifteen
                // minutes of watching had to be switched on here or the session would have switched it
                // off. It also makes day 9 the first day in the program with something to watch, which
                // is the right shape for the day the ambient layer unlocks: today is when the program
                // stops being only words.
                Overrides = new Dictionary<string, object>
                {
                    ["MandatoryVideosEnabled"] = true,
                    ["MandatoryVideosStartMinute"] = 6,
                    ["VideosPerHour"] = 1
                },

                Ambient = new ProgramAmbient
                {
                    Description = "Subliminals during normal use",
                    RequiredMinutes = 0
                },
                RewardDescription = "The ambient layer unlocks. It has an off switch that never costs you the day.",
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d9_video",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Watch 15 minutes of video",
                        Verifier = QuestCategory.Video,
                        TargetValue = 15
                    }
                }
            },

            // ---- Day 10 - ritual ---------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 10,
                Title = "Painted",
                Blurb = "It doesn't have to be good, babe. It has to be on your face, and you have to look at it for longer than is comfortable before you take the picture~",
                SessionTemplateId = "PR-Dress",
                SessionMinutes = 45,
                Intensity = 0.40,
                Ambient = new ProgramAmbient
                {
                    Description = "Subliminals during normal use, and a corner GIF for the rest of the day",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d10_ritual_face",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Painted Face - foundation, blush and lipstick, then a face photograph. Any photo stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t2_step3"
                    }
                }
            },

            // ---- Day 11 - light ----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 11,
                Title = "Four Times Now",
                Blurb = "Two on Monday of week one, three on Saturday, four tonight. She's noticed you don't hesitate before typing any more~",
                SessionTemplateId = "PR-Dress",
                SessionMinutes = 45,
                Intensity = 0.49,
                Ambient = new ProgramAmbient
                {
                    Description = "Subliminals during normal use, and a corner GIF for the rest of the day",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d11_lockcards",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 4 lock cards",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 4
                    }
                }
            },

            // ---- Day 12 - ritual ---------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 12,
                Title = "Look Up Slowly",
                Blurb = "Lashes on top of Thursday's face, then five minutes of slow blinking at your own reflection. You'll feel silly for about ninety seconds~",
                SessionTemplateId = "PR-Show",
                SessionMinutes = 45,
                Intensity = 0.57,
                Ambient = new ProgramAmbient
                {
                    Description = "Subliminals during normal use, corner GIF, and triggers armed",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d12_ritual_lashes",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "Lash Out - false lashes or heavy mascara, then blink slowly for five minutes. Photograph the eyes, close up. Any photo stays on this machine: never uploaded, never synced, never on a share card.",
                        RoadmapStepId = "t2_step4"
                    }
                }
            },

            // ---- Day 13 - light ----------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 13,
                Title = "Let Her Steer",
                Blurb = "The last easy evening. Quarter of an hour where you don't decide what happens next, and something underneath the session got louder tonight. Lay tomorrow's clothes out before bed~",
                SessionTemplateId = "PR-Show",
                SessionMinutes = 45,
                Intensity = 0.64,

                // PR-Show only spans .57 to .70, which is far too narrow for the lerp to distinguish
                // three consecutive days - on a 95/150 flash field that whole band is eight units. So
                // the last two days each take one Overrides entry instead. This one is "the sound got
                // louder": the mind wipe jumps to multiplier 3 from minute four, six points of
                // intensity ahead of where the curve would have put it.
                Overrides = new Dictionary<string, object>
                {
                    ["MindWipeStartMinute"] = 4,
                    ["MindWipeBaseMultiplier"] = 3,
                    ["MindWipeVolume"] = 55
                },

                Ambient = new ProgramAmbient
                {
                    Description = "Subliminals during normal use, corner GIF, and triggers armed",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "d13_takeover",
                        Kind = ProgramTaskKind.AutoVerified,
                        // Autonomy accumulates in whole minutes.
                        Description = "Run Bimbo Takeover for 15 minutes",
                        Verifier = QuestCategory.Autonomy,
                        TargetValue = 15,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Day 14 - FINAL, the day-fourteen photograph -----------------------------------
            new ProgramDay
            {
                DayIndex = 14,
                Title = "The Inspection",
                Blurb = "All of it at once - shaved, painted, lashed, dressed - and one full hour. Take the photograph last, in exactly the pose you used on day one, and then go and look at day one~",
                IsBoss = true,
                SessionTemplateId = "PR-Show",
                SessionMinutes = 60,
                Intensity = 0.70,

                // The finale's one new thing: the corner GIF comes off the all-day ambient layer and
                // into the session itself. No template in this program ever enables it, which is what
                // makes it available here - and it has been following the user around since day 10, so
                // it is a payoff rather than an ambush. Only CornerGifEndMinute is read by the engine
                // and its default of -1 runs to session end, which is what is wanted.
                Overrides = new Dictionary<string, object>
                {
                    ["CornerGifEnabled"] = true,
                    ["CornerGifStartMinute"] = 0,
                    ["CornerGifOpacity"] = 30,
                    ["SubliminalPerMin"] = 12,
                    ["MindWipeBaseMultiplier"] = 4,
                    ["MindWipeVolume"] = 62
                },

                Ambient = new ProgramAmbient
                {
                    Description = "Everything on for the whole day",
                    RequiredMinutes = 0
                },
                RewardDescription = "Presented badge, the Discord role, a phrase pack, and the seventh page of your ledger beside the first.",
                Tasks = new List<ProgramTask>
                {
                    // The 90-minute call. t2_boss's objective ends "+ Complete a session (30 mins)",
                    // and that clause exists only because the roadmap has no other way to require a
                    // session. Inside a program that requirement is already met: tonight's own
                    // 60-minute session IS the session t2_boss is asking for, and it is double what the
                    // step wants. So today is 60 minutes of seated time, not 90, and the cap holds
                    // without trimming the finale. The task copy says so explicitly, because the user
                    // must never be told to sit down a second time - least of all on the day they also
                    // have to shave, paint, dress and photograph themselves.
                    //
                    // The framing sentence is word-for-word day 1's. That is the diptych.
                    new ProgramTask
                    {
                        Id = "d14_ritual_inspection",
                        Kind = ProgramTaskKind.Ritual,
                        Description = "The Inspection - full shave, full makeup, full uniform. Tonight's own session counts as the session this asks for; you do not owe a second one. This is your day-fourteen photograph: stand square to the mirror, feet together, arms loose at your sides, camera at chest height. It stays on this machine: never uploaded, never synced, never on a share card, and you can delete it whenever you want.",
                        RoadmapStepId = "t2_boss"
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Presentation templates
    //
    // Same conventions as the First Week and Takeover templates - see the block comment above the
    // Takeover templates for the full list. Three things are specific to this program:
    //
    //   1. Every floor is softer than the Takeover template you would reach for at the same intensity.
    //      A day here spends most of its effort off-screen, and a session that fights the ritual for
    //      the user's remaining attention loses the ritual, which is the thing this program is about.
    //
    //   2. Ceilings are placed for a .70 arrival, not a 1.00 one. PR-Mirror's and PR-Dress's ceilings
    //      were pulled OUT on the retune (x1.23 and x1.10) because the new curve narrowed their bands;
    //      a ceiling the program can never approach is a ceiling that makes the whole band feel flat.
    //
    //   3. PR-Show spans only .57 to .70 - too narrow for any floor/ceiling pair to distinguish three
    //      days, since solving for a visible step across thirteen intensity points wants a negative
    //      floor. Days 13 and 14 therefore carry one Overrides entry each. That is the field's whole
    //      purpose and it is the only honest way to give a finale somewhere to go.
    //
    // Five days in total carry Overrides, and they fall into two groups:
    //   - days 2, 8 and 9 switch ON the feature their own task names. This is not decoration: the
    //     engine writes `false` into live AppSettings and stops the service for anything the template
    //     leaves off, so a task naming a feature the session omits is actively prevented rather than
    //     merely unassisted, and its counter stalls for the whole session.
    //   - days 13 and 14 add the one new thing the saturated top of the curve cannot express.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Subliminals for the passive templates. Every entry is a key of the Sissy Hypno SubliminalPool,
    /// all 21 of which ship enabled. Note the Sissy pool is a near-clone of the Bambi one with the
    /// Bambi-prefixed entries renamed - BAMBI SLEEP became DEEP SLEEP, BAMBI FREEZE became FREEZE - so
    /// a phrase list copy-pasted from the Takeover file would compile, run, and be wrong.
    /// </summary>
    private static List<string> PrSoftSubliminals() => new()
    {
        "GOOD GIRL",
        "DEEP SLEEP",
        "PRIMPED AND PAMPERED",
        "DONT THINK SILLY",
        "THERES NO NEED TO THINK",
        "TURN YOUR BRAIN OFF"
    };

    /// <summary>
    /// The mirror pool. The manifest has no vocabulary that literally names looking or reflections, so
    /// this is the pool's set of *being looked at* phrases rather than invented ones - BIMBO DOLL,
    /// PRIMPED AND PAMPERED, GOOD GIRL. Nothing here is authored; every line is a manifest key.
    /// </summary>
    private static List<string> PrMirrorSubliminals() => new()
    {
        "GOOD GIRL",
        "BIMBO DOLL",
        "PRIMPED AND PAMPERED",
        "DEEP SLEEP",
        "GIGGLETIME",
        "JUST OBEY",
        "GOOD GIRLS DONT THINK",
        "DONT THINK SILLY",
        "TURN YOUR BRAIN OFF",
        "THERES NO NEED TO THINK"
    };

    private static List<string> PrDressSubliminals() => new()
    {
        "GOOD GIRL",
        "BIMBO DOLL",
        "PRIMPED AND PAMPERED",
        "UNIFORM LOCK",
        "OBEY",
        "JUST OBEY",
        "FREEZE",
        "SNAP AND FORGET",
        "GIGGLETIME",
        "GOOD GIRLS DONT THINK",
        "TURN YOUR BRAIN OFF",
        "I CANT RESIST MY TRIGGERS"
    };

    private static List<string> PrShowSubliminals() => new()
    {
        "GOOD GIRL",
        "BIMBO DOLL",
        "PRIMPED AND PAMPERED",
        "UNIFORM LOCK",
        "FREEZE",
        "RESET",
        "DEEP SLEEP",
        "OBEY",
        "JUST OBEY",
        "SNAP AND FORGET",
        "DROP FOR COCK",
        "COCK ZOMBIE NOW",
        "CUM AND COLLAPSE",
        "ZAP COCK DRAIN OBEY",
        "I CANT RESIST MY TRIGGERS",
        "GOOD GIRLS DONT THINK",
        "TURN YOUR BRAIN OFF",
        "GIGGLETIME"
    };

    /// <summary>
    /// The Sissy Hypno manifest ships exactly five lock card phrases and all five are enabled, so
    /// PrShowLockCards is the whole pool. The earlier lists are narrowed so days 4 and 6 land on the
    /// phrase the content brief names - EMPTY AND OBEDIENT.
    /// </summary>
    private static List<string> PrEarlyLockCards() => new()
    {
        "EMPTY AND OBEDIENT"
    };

    private static List<string> PrMidLockCards() => new()
    {
        "EMPTY AND OBEDIENT",
        "GOOD GIRLS OBEY",
        "DEEP SLEEP"
    };

    private static List<string> PrShowLockCards() => new()
    {
        "EMPTY AND OBEDIENT",
        "GOOD GIRLS OBEY",
        "I LOVE BEING PROGRAMMED",
        "DEEP SLEEP",
        "DROP FOR ME"
    };

    /// <summary>
    /// Bouncing text. The Sissy Hypno manifest carries no BouncingTextPool - only CCPDefault and
    /// Circe's Lock have one - so these are taken byte-exact from its RandomFloating and Idle phrase
    /// lists. Lines carrying a speech-bubble stage direction ("*giggles*") are excluded: that is an
    /// avatar convention and renders as literal asterisks when it drifts across the screen.
    /// </summary>
    private static List<string> PrSoftBouncingText() => new()
    {
        "Empty head, happy girl!",
        "Hehe~ so floaty...",
        "Just floating here...",
        "Sissy bliss...",
        "So sleepy and cute...",
        "Mind so soft and fuzzy~"
    };

    /// <summary>The looking pool - the manifest's own phrases about pretty things and watching.</summary>
    private static List<string> PrMirrorBouncingText() => new()
    {
        "Pretty pink princess!",
        "Pink is my favorite color!",
        "Pink spirals are pretty...",
        "You love spirals~",
        "Don't think. Just watch~",
        "Such a ditzy dolly!",
        "Feminized and happy~",
        "Good girl~"
    };

    private static List<string> PrDressBouncingText() => new()
    {
        "Feminized and happy~",
        "Mindless and girly~",
        "Sissy obeys!",
        "Obey feels so good!",
        "Dropping deeper...",
        "Thoughts drip away...",
        "Bimbo is bliss!",
        "Pretty pink princess!"
    };

    private static List<string> PrShowBouncingText() => new()
    {
        "Good girls don't think~",
        "Good girls drop deep~",
        "Sissy obeys!",
        "Empty and happy~",
        "Dropping deeper...",
        "Triggers feel amazing!",
        "Obey feels so good!",
        "So pink and empty...",
        "Mindless and girly~",
        "Thoughts drip away..."
    };

    /// <summary>
    /// PR-Soft - passive drift, low opacity, and nothing that asks for a click. Used on days 1 and 2
    /// (the day with the most physical work attached, and the day after it) and day 8 (the deload).
    /// Band: i .05 to i .25. The floor is genuinely almost nothing, because day 1's session runs after
    /// a full exfoliate-and-moisturize routine and a photograph the user has to steel themselves for.
    /// </summary>
    private static ProgramSessionTemplate PrSoft() => new()
    {
        Id = "PR-Soft",
        Name = "Soft",
        Description = "Barely there. Something to sit inside while your skin settles.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 6,
            FlashPerHourEnd = 11,
            FlashImages = 1,
            FlashOpacity = 10,
            FlashOpacityEnd = 18,
            FlashScale = 85,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 6,
            SubliminalPerMin = 1,
            SubliminalFrames = 2,
            SubliminalOpacity = 25,
            SubliminalPhrases = PrSoftSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 4,
            WhisperVolume = 7,
            AudioDuckLevel = 22,

            BouncingTextEnabled = false,
            BouncingTextPhrases = PrSoftBouncingText(),
            PinkFilterEnabled = false,
            SpiralEnabled = false,
            BubblesEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            LockCardPhrases = PrEarlyLockCards(),
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 78,
            FlashPerHourEnd = 145,
            FlashImages = 3,
            FlashOpacity = 68,
            FlashOpacityEnd = 96,
            FlashScale = 112,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 8,
            SubliminalFrames = 4,
            SubliminalOpacity = 86,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 37,
            AudioDuckLevel = 72,

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
    /// PR-Mirror - bouncing text and subliminals, and the first template with the pink filter. This is
    /// the template that talks: the bouncing pool is the manifest's own phrases about pretty things,
    /// and it is the only feature turned up here.
    /// Band: i .18 (day 3) to i .31 (day 9). Its ceiling was pulled out by a factor of 1.23 on the
    /// retune - a thirteen-point band against a ceiling authored for .38 would have been invisible.
    /// </summary>
    private static ProgramSessionTemplate PrMirror() => new()
    {
        Id = "PR-Mirror",
        Name = "Mirror",
        Description = "Words drifting past on their own, and a colour over everything you look at.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 12,
            FlashPerHourEnd = 22,
            FlashImages = 1,
            FlashOpacity = 18,
            FlashOpacityEnd = 30,
            FlashScale = 88,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 3,
            SubliminalPerMin = 2,
            SubliminalFrames = 2,
            SubliminalOpacity = 38,
            SubliminalPhrases = PrMirrorSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 2,
            WhisperVolume = 10,
            AudioDuckLevel = 28,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 3,
            BouncingTextSpeed = 2,
            BouncingTextSize = 65,
            BouncingTextOpacity = 60,
            BouncingTextPhrases = PrMirrorBouncingText(),

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 12,
            PinkFilterStartOpacity = 5,
            PinkFilterEndOpacity = 18,

            // The program owns the cadence on days 4 and 9, so both halves carry a number.
            LockCardEnabled = true,
            LockCardStartMinute = 8,
            LockCardFrequency = 1,
            LockCardPhrases = PrMidLockCards(),

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
            FlashPerHour = 132,
            FlashPerHourEnd = 228,
            FlashImages = 3,
            FlashOpacity = 79,
            FlashOpacityEnd = 100,
            FlashScale = 121,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 9,
            SubliminalFrames = 5,
            SubliminalOpacity = 99,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 44,
            AudioDuckLevel = 82,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 9,
            BouncingTextSize = 145,
            BouncingTextOpacity = 100,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = 28,
            PinkFilterEndOpacity = 72,

            LockCardEnabled = true,
            LockCardStartMinute = 2,
            LockCardFrequency = 6,

            SpiralEnabled = false,
            BubblesEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// PR-Dress - video and flash. The spiral, the bubbles and the first mandatory video arrive here:
    /// this is the template for the days where the ritual is makeup rather than grooming and the user
    /// is sitting in front of a mirror anyway.
    /// Band: i .32 (day 6) to i .49 (day 11). Ceiling pulled out x1.10 on the retune.
    /// </summary>
    private static ProgramSessionTemplate PrDress() => new()
    {
        Id = "PR-Dress",
        Name = "Dress",
        Description = "Screens now. Things to watch, things to pop, and something turning behind it all.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 30,
            FlashPerHourEnd = 55,
            FlashImages = 2,
            FlashOpacity = 28,
            FlashOpacityEnd = 45,
            FlashScale = 92,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 2,
            SubliminalPerMin = 3,
            SubliminalFrames = 3,
            SubliminalOpacity = 48,
            SubliminalPhrases = PrDressSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 1,
            WhisperVolume = 14,
            AudioDuckLevel = 36,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 2,
            BouncingTextSpeed = 3,
            BouncingTextSize = 72,
            BouncingTextOpacity = 68,
            BouncingTextPhrases = PrDressBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 7,
            BubblesFrequency = 2,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 7,
            BubblesPerBurst = 2,
            BubblesGapMin = 5,
            BubblesGapMax = 10,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 9,
            PinkFilterStartOpacity = 9,
            PinkFilterEndOpacity = 28,

            SpiralEnabled = true,
            SpiralStartMinute = 20,
            SpiralOpacity = 4,
            SpiralOpacityEnd = 15,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 14,
            VideosPerHour = 1,

            LockCardEnabled = true,
            LockCardStartMinute = 12,
            LockCardFrequency = 1,
            LockCardPhrases = PrMidLockCards(),

            CornerGifEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 168,
            FlashPerHourEnd = 303,
            FlashImages = 4,
            FlashOpacity = 79,
            FlashOpacityEnd = 100,
            FlashScale = 121,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 10,
            SubliminalFrames = 5,
            SubliminalOpacity = 97,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 43,
            AudioDuckLevel = 80,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 9,
            BouncingTextSize = 138,
            BouncingTextOpacity = 100,

            BubblesEnabled = true,
            BubblesStartMinute = 2,
            BubblesFrequency = 5,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 21,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 4,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = 28,
            PinkFilterEndOpacity = 70,

            SpiralEnabled = true,
            SpiralStartMinute = 3,
            SpiralOpacity = 19,
            SpiralOpacityEnd = 47,

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
    /// PR-Show - everything on, lock cards included, plus the bubble count and the mind wipe. Used on
    /// the last three days only.
    /// Band: i .57 (day 12) to i .70 (day 14) - thirteen points, the tightest band in either program.
    /// No floor/ceiling pair can make thirteen points visible across three days without a negative
    /// floor, so days 13 and 14 each carry one Overrides entry and this pair is authored for the *feel*
    /// of the last week rather than as an escalation device. Day 12 gets its perceptible change for
    /// free by being the first day this template appears at all.
    /// </summary>
    private static ProgramSessionTemplate PrShow() => new()
    {
        Id = "PR-Show",
        Name = "Show",
        Description = "All of it at once. Dressed, painted, watched, and asked to say so.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 95,
            FlashPerHourEnd = 175,
            FlashImages = 3,
            FlashOpacity = 52,
            FlashOpacityEnd = 72,
            FlashScale = 105,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 5,
            SubliminalFrames = 4,
            SubliminalOpacity = 62,
            SubliminalPhrases = PrShowSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 20,
            AudioDuckLevel = 50,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 5,
            BouncingTextSize = 88,
            BouncingTextOpacity = 78,
            BouncingTextPhrases = PrShowBouncingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 4,
            BubblesFrequency = 3,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 11,
            BubblesPerBurst = 3,
            BubblesGapMin = 4,
            BubblesGapMax = 8,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 6,
            PinkFilterStartOpacity = 20,
            PinkFilterEndOpacity = 48,

            SpiralEnabled = true,
            SpiralStartMinute = 14,
            SpiralOpacity = 10,
            SpiralOpacityEnd = 26,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 10,
            VideosPerHour = 2,

            LockCardEnabled = true,
            LockCardStartMinute = 9,
            LockCardFrequency = 2,
            LockCardPhrases = PrShowLockCards(),

            BubbleCountEnabled = true,
            BubbleCountStartMinute = 18,
            BubbleCountFrequency = 1,

            MindWipeEnabled = true,
            MindWipeStartMinute = 12,
            MindWipeBaseMultiplier = 1,
            MindWipeVolume = 26,

            CornerGifEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 150,
            FlashPerHourEnd = 300,
            FlashImages = 4,
            FlashOpacity = 74,
            FlashOpacityEnd = 96,
            FlashScale = 120,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 9,
            SubliminalFrames = 5,
            SubliminalOpacity = 90,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 38,
            AudioDuckLevel = 72,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 8,
            BouncingTextSize = 125,
            BouncingTextOpacity = 100,

            BubblesEnabled = true,
            BubblesStartMinute = 1,
            BubblesFrequency = 5,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 20,
            BubblesPerBurst = 3,
            BubblesGapMin = 2,
            BubblesGapMax = 5,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = 34,
            PinkFilterEndOpacity = 72,

            SpiralEnabled = true,
            SpiralStartMinute = 3,
            SpiralOpacity = 22,
            SpiralOpacityEnd = 52,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 4,
            VideosPerHour = 3,

            LockCardEnabled = true,
            LockCardStartMinute = 4,
            LockCardFrequency = 4,

            BubbleCountEnabled = true,
            BubbleCountStartMinute = 8,
            BubbleCountFrequency = 2,

            MindWipeEnabled = true,
            MindWipeStartMinute = 2,
            MindWipeBaseMultiplier = 4,
            MindWipeVolume = 58,

            CornerGifEnabled = false
        }
    };
}
