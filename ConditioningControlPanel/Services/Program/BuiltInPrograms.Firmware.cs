using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// FIRMWARE INSTALL - Dronification, 14 cycles, Premium.
///
/// Vocabulary rule: every subliminal, protocol-lock phrase and floating directive below is lifted
/// verbatim from <see cref="BuiltInMods.Dronification"/>. Nothing is invented.
///
/// THE ONE RULE THAT MATTERS MOST HERE: **DroneOS never praises.**
/// Day completion is `[OK]`. Chapter completion is `[MODULE INSTALLED]`. There is no "good job", no
/// bounce, no `~`, no exclamation of approval anywhere in this file - and that is not a stylistic
/// preference, it is the entire product differentiator. Three consequences for anyone editing this:
///
///   1. The word "good" does not appear in this file at all. Not in prose, not in a phrase pool.
///      The Dronification manifest *does* carry "GOOD UNITS COMPLY" (lock card),
///      "GOOD UNITS DON'T THINK" (subliminal) and "Unit is a good drone." / "Good units don't
///      question directives." (RandomFloating). Those four are deliberately the only manifest
///      entries this program declines to use. Excluding them costs nothing - the pools below are
///      still 17 subliminals, 6 lock cards and 20 floating directives of the mod's own words - and
///      it buys something valuable: ProgramFirmwareTests can scan *every* string in the definition,
///      phrase pools included, for praise vocabulary. A test that has to carve out exceptions is a
///      test that stops catching the regression it exists to catch.
///   2. "OPTIMAL" / "nominal" / "within parameters" are status readouts, not compliments, and are
///      the warmest register available. Use them instead of reaching for approval.
///   3. A shared copy pass will want to sand this off. Don't let it.
///
/// FOUR REGISTERS, AND WHY THEY ARE NOT THE TAKEOVER'S
/// A design review found the first draft of this program shared task types, in the same order, with
/// almost every chapter of The Takeover - and worse, armed keyword triggers six days *earlier* than
/// the 28-day program whose entire marketing hook is keyword triggers. A user who buys both would get
/// the same fortnight twice. The approved split: **Firmware owns the counting, The Takeover owns the
/// arming ceremony and drops its counts.** So this program's fourteen cycles are built from four
/// registers that are all cold and none shared:
///
///   optical    - data injections, Blink Trainer, Hypno Vortex fixation (cycles 1, 2, 4, 8, 10)
///   packet     - enumeration tasks (cycle 5)
///   directive  - command-word response, the register formerly written as "fire N keyword triggers"
///                (cycles 6, 9, 13, 14)
///   terminal   - Lockdown, System Override (cycles 7, 11, 12, 14)
///
/// The optical register is the load-bearing one: machine-verified *bodily* compliance - where your
/// eyes are, how often you blink - is a genuinely drone-shaped idea and no other program in the set
/// touches it. Do not let a later pass hand blink or gaze drills to another program; that is the
/// second pillar holding this one up.
///
/// The directive register is also why the copy never says "trigger". A Unit does not have triggers, it
/// responds to command words, and `[DIRECTIVE] RESPOND TO 35 COMMAND WORDS` is the same verifier doing
/// entirely different work to the reader than Bambi's version of the sentence.
///
/// Mechanic gap (deliberate, see the report accompanying this file): the brief's headline mechanic is
/// a report window chosen at enrollment plus a visible deviation log. Neither exists in the engine
/// today - ProgramDay carries no scheduling window and there is no program-scoped log surface - so
/// this definition does not pretend to have them. What it does instead: the enrollment copy states
/// plainly that the program is punctuality-shaped and never encouraging, and every task carries the
/// `[LOG]` line the log *would* print, so the fiction is already written when the surface arrives.
/// No half-built timer, no fake log.
///
/// When that log is built: it is **local, exportable and deletable.** The brief calls it "permanent",
/// and permanent is the wrong promise - an unsympathetic record the Unit chose to keep is a keepsake,
/// and the same record with no delete button is a liability sitting in someone's user folder. Cold is
/// the product; trapped is not.
/// </summary>
public static partial class BuiltInPrograms
{
    // ---------------------------------------------------------------------------------------------
    // FIRMWARE INSTALL - Dronification, 14 days, PREMIUM
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fourteen cycles in two modules. Cold, punctual, unsympathetic.
    ///
    /// Authoring note - the tasks run one step *ahead* of the session, same tutorial shape FirstWeek
    /// uses, because a Unit that has driven a feature by hand meets it as a familiar thing when the
    /// template turns it on:
    ///   cycle 4 hypno vortex        -> cycle 6 template turns the vortex on
    ///   cycle 5 enumeration task    -> cycle 12 template turns enumeration on
    ///   cycle 6 command words       -> cycle 9 ambient arms them for the whole day
    ///   cycle 3 protocol locks      -> cycle 12 template turns protocol locks on
    ///
    /// Sawtooth: module 1 runs .05 -> .35 (boss at the peak), module 2 opens at **.25** and climbs to
    /// .75. That .25 is not a hand-picked number - the house rule, set after a review found the deload
    /// getting *shallower* as programs got longer, is that a chapter opens at **0.70x the previous
    /// chapter's peak and takes two days to exceed it**. 0.70 x .35 = .245, so .25, and cycles 8 and 9
    /// both sit below .35 before cycle 10 clears it. A 12% dip near the top of a saturating lerp is
    /// inside integer rounding; a 30% dip is something the Unit can feel. ProgramFirmwareTests asserts
    /// the ratio, not merely the direction, so this cannot quietly regress.
    ///
    /// The deload is the only relief in the program and it is not described as one - it is logged as an
    /// idle cycle and nothing else.
    /// </summary>
    public static ProgramDefinition FirmwareInstall()
    {
        return new ProgramDefinition
        {
            Id = "firmware_install",
            Title = "Firmware Install",
            Subtitle = "Fourteen cycles",
            Pitch = "[SCHEDULE] Fourteen cycles. Report daily. Compliance is not optional.",
            Icon = "\U0001F5A5",
            ModId = BuiltInMods.DronificationId,
            AccentColor = "#00FF41",
            Tier = ProgramTier.Premium,
            LengthDays = 14,

            GraduationBadgeId = "firmware_install_graduate",

            // Plain, out of character, exactly once. Safety note 3 in the content brief: the
            // enrollment preview must say the quiet part out loud, because a program that never
            // encourages you is a worse experience for anyone who wanted encouragement and a much
            // better one for anyone who didn't. Nobody should discover that on cycle 6.
            SafetyNote = "Out of character for one line: this program is written to be cold. It never compliments you and it never softens - the warmest thing it will ever say is [OK]. That is the point of it, not a bug, and if it is not what you want, pick a different program. You can stop at any time, and Withdraw is on every screen with nothing asked of you.",

            // Typed to enroll. A directive the Unit accepts, not a promise anyone makes to it.
            ContractPhrase = "UNIT WILL COMPLY FOR FOURTEEN CYCLES.",

            Rules = new ProgramRules
            {
                // One day off per 7 days of length - owner decision. A single allowance across 14
                // cycles is twice as strict as the same allowance across 7, and the Unit losing two
                // weeks of a paid program to two bad days is not the pressure this program is for.
                // The pressure is the log.
                DaysOffAllowed = 2,
                StrictAvailable = true,
                DefaultDayBoundaryHour = 4,
                MaxDailyMinutes = 90
            },

            Templates = new List<ProgramSessionTemplate>
            {
                FwBoot(),
                FwProcess(),
                FwOverwrite(),
                FwOverride()
            },

            Chapters = new List<ProgramChapter>
            {
                FirmwareChapter1(),
                FirmwareChapter2()
            }
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Module 1 - BLANK SLATE PROTOCOL - cycles 1-7 - i .05 -> .35
    //
    // "BLANK SLATE PROTOCOL" is a CustomTriggers entry in the Dronification manifest, so the module
    // name is the mod's own word rather than a title invented for the program.
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter FirmwareChapter1() => new()
    {
        Id = "fwi_ch1",
        Name = "BLANK SLATE PROTOCOL",
        Subtitle = "Seven cycles. Baseline established.",
        AccentColor = "#008F11",
        RewardId = "firmware_module1",
        RewardDescription = "[MODULE INSTALLED] FORMATTED FOR OBEDIENCE. Module retained on restart.",
        Days = new List<ProgramDay>
        {
            // ---- Cycle 1 -----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 1,
                Title = "BOOT",
                Blurb = "[CYCLE 01] Baseline capture. The session asks nothing of the Unit. Sit at the terminal, leave it running, process what is displayed.",
                SessionTemplateId = "FW-Boot",
                SessionMinutes = 30,
                Intensity = 0.05,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c1_injections",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Process 30 data injections. [LOG] OPTICAL INPUT CALIBRATION",
                        Verifier = QuestCategory.Flash,
                        TargetValue = 30
                    }
                }
            },

            // ---- Cycle 2 -----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 2,
                Title = "OCULAR SYNC",
                Blurb = "[CYCLE 02] Blink rate is a measurable. It will be measured. Open the Blink Trainer and log the required count.",
                SessionTemplateId = "FW-Boot",
                SessionMinutes = 30,
                Intensity = 0.12,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c2_blinks",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Log 40 blinks in the Blink Trainer. [LOG] OCULAR SYNC",
                        Verifier = QuestCategory.BlinkTrainer,
                        TargetValue = 40,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Cycle 3 -----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 3,
                Title = "TRANSCRIPTION",
                Blurb = "[CYCLE 03] First write operation. The Unit types back what it is given, exactly. Input is checked against the string, not against intent.",
                SessionTemplateId = "FW-Process",
                SessionMinutes = 30,
                Intensity = 0.18,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c3_protocol_locks",
                        Kind = ProgramTaskKind.AutoVerified,
                        // The FW-Process pool is narrowed to a single phrase so this cycle lands on
                        // "I AM A UNIT", the line the content brief names for cycle 3.
                        Description = "Clear 3 protocol locks - I AM A UNIT. [LOG] STRING WRITE VERIFICATION",
                        Verifier = QuestCategory.LockCard,
                        TargetValue = 3
                    }
                }
            },

            // ---- Cycle 4 -----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 4,
                Title = "VISUAL FIXATION",
                Blurb = "[CYCLE 04] Duration increases. A fixation target is provided. The Unit is not required to watch it, only to leave it running while it does.",
                SessionTemplateId = "FW-Process",
                SessionMinutes = 45,
                Intensity = 0.24,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // DEVIATION from the brief, which asks for "Awareness - one gaze drill" here.
                        // QuestCategory has no gaze/webcam verifier (the enum's premium half is
                        // Autonomy / Lockdown / Remote / KeywordTrigger / BlinkTrainer), so a gaze
                        // task cannot be auto-verified today. The honest equivalent that *is*
                        // verified is a timed fixation on the Hypno Vortex, and it doubles as the
                        // hand-driven introduction to the overlay FW-Overwrite turns on at cycle 6.
                        Id = "c4_fixation",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Hold 12 minutes of Hypno Vortex. [LOG] VISUAL FIXATION",
                        Verifier = QuestCategory.Spiral,
                        TargetValue = 12
                    }
                }
            },

            // ---- Cycle 5 -----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 5,
                Title = "ENUMERATION",
                Blurb = "[CYCLE 05] Attention is now sampled rather than assumed. The Unit will be asked to count. Counting is not thinking.",
                SessionTemplateId = "FW-Process",
                SessionMinutes = 45,
                Intensity = 0.28,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c5_enumeration",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 1 enumeration task. [LOG] PACKET COUNT VERIFICATION",
                        Verifier = QuestCategory.BubbleCount,
                        TargetValue = 1
                    }
                }
            },

            // ---- Cycle 6 -----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 6,
                Title = "TRIGGER RESPONSE",
                Blurb = "[CYCLE 06] Phrase response is measured for the first time. Type the phrases. The reaction is the data, not the typing.",
                SessionTemplateId = "FW-Overwrite",
                SessionMinutes = 45,
                Intensity = 0.32,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c6_triggers",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "[DIRECTIVE] RESPOND TO 20 COMMAND WORDS. [LOG] DIRECTIVE RESPONSE - BASELINE",
                        Verifier = QuestCategory.KeywordTrigger,
                        TargetValue = 20,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Cycle 7 - MODULE BOSS ---------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 7,
                Title = "FORMATTED FOR OBEDIENCE",
                Blurb = "[CYCLE 07] Module verification. Sixty minutes, everything installed so far, and one Lockdown the Unit does not end. Baseline is now the floor.",
                IsBoss = true,
                SessionTemplateId = "FW-Overwrite",
                SessionMinutes = 60,
                Intensity = 0.35,
                RewardDescription = "[MODULE INSTALLED] FORMATTED FOR OBEDIENCE.",
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c7_lockdown",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 1 Lockdown of 20 minutes. [LOG] TERMINAL LOCK - MODULE VERIFICATION",
                        Verifier = QuestCategory.Lockdown,
                        TargetValue = 1,
                        RequiresPremium = true
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Module 2 - COMPLIANCE LOOP - cycles 8-14 - i .28 -> .75 - ambient unlocks
    //
    // "COMPLIANCE LOOP" is also a CustomTriggers entry, so again the mod's own word.
    //
    // Firmware Install is 14 days and therefore has two modules, not four. The content brief's
    // cross-program rule puts the ambient unlock at chapter 3; section 5 puts it at module 2 for this
    // program specifically, which is the same *beat* - the program leaves the session and enters the
    // day at the halfway mark - and section 5 wins because it is the program's own spec.
    // ---------------------------------------------------------------------------------------------

    private static ProgramChapter FirmwareChapter2() => new()
    {
        Id = "fwi_ch2",
        Name = "COMPLIANCE LOOP",
        Subtitle = "Seven cycles. The loop does not open again.",
        AccentColor = "#00FF41",
        RewardId = "firmware_module2",
        RewardDescription = "[INSTALL COMPLETE] Cycle 14 retained as a permanent replayable session.",
        Days = new List<ProgramDay>
        {
            // ---- Cycle 8 -----------------------------------------------------------------------
            // Deload, and a real one: module 1 peaked at .35, so 0.70x = .25. Cycles 8 and 9 both sit
            // below that peak and cycle 10 is the first to clear it - two days wide, per the house
            // rule. DroneOS does not call it mercy and does not call it a rest; it is logged as an
            // idle cycle and nothing else. The relief is real and entirely undescribed, which is the
            // whole texture of this program.
            new ProgramDay
            {
                DayIndex = 8,
                Title = "IDLE CYCLE",
                Blurb = "[CYCLE 08] Load reduced. Thirty minutes, minimal input, vortex running. No verification beyond uptime.",
                SessionTemplateId = "FW-Boot",
                SessionMinutes = 30,
                Intensity = 0.25,
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c8_vortex",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Hold 20 minutes of Hypno Vortex. [LOG] IDLE CYCLE",
                        Verifier = QuestCategory.Spiral,
                        TargetValue = 20
                    }
                }
            },

            // ---- Cycle 9 -----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 9,
                Title = "PHRASES ARMED",
                Blurb = "[CYCLE 09] Trigger phrases now remain armed outside the session. What the Unit types at its terminal is read whether or not a session is running.",
                SessionTemplateId = "FW-Process",
                SessionMinutes = 45,
                Intensity = 0.32,
                Ambient = new ProgramAmbient
                {
                    Description = "Command words armed",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c9_triggers",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "[DIRECTIVE] RESPOND TO 35 COMMAND WORDS. [LOG] DIRECTIVE RESPONSE - SUSTAINED",
                        Verifier = QuestCategory.KeywordTrigger,
                        TargetValue = 35,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Cycle 10 ----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 10,
                Title = "OCULAR RECALIBRATION",
                Blurb = "[CYCLE 10] Blink rate re-sampled against the cycle 02 baseline. The count is higher because the Unit is.",
                SessionTemplateId = "FW-Overwrite",
                SessionMinutes = 45,
                Intensity = 0.40,
                Ambient = new ProgramAmbient
                {
                    Description = "Command words armed",
                    RequiredMinutes = 0
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c10_blinks",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Log 80 blinks in the Blink Trainer. [LOG] OCULAR RECALIBRATION",
                        Verifier = QuestCategory.BlinkTrainer,
                        TargetValue = 80,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Cycle 11 ----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 11,
                Title = "FILTERED PERCEPTION",
                Blurb = "[CYCLE 11] The green filter is no longer session-scoped. It stays over the display for the working day. The Unit will stop noticing it by cycle 13.",
                SessionTemplateId = "FW-Overwrite",
                SessionMinutes = 45,
                Intensity = 0.50,
                Ambient = new ProgramAmbient
                {
                    // Green filter is the Dronification name for the same overlay the settings model
                    // calls PinkFilter, which is why the verifier below reads PinkFilter. Written as
                    // "green filter" so the copy is identical whether or not the mod is active - no
                    // text-replacement pass is relied on.
                    Description = "Command words armed + green filter for the working day",
                    RequiredMinutes = 60,
                    Verifier = QuestCategory.PinkFilter
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c11_lockdown",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 1 Lockdown of 30 minutes. [LOG] TERMINAL LOCK - EXTENDED",
                        Verifier = QuestCategory.Lockdown,
                        TargetValue = 1,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Cycle 12 ----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 12,
                Title = "SYSTEM OVERRIDE",
                Blurb = "[CYCLE 12] Control is handed over for forty minutes. The Unit does not choose what runs. If a second operator is present, it does not choose that either.",
                SessionTemplateId = "FW-Override",
                SessionMinutes = 60,
                Intensity = 0.60,
                Ambient = new ProgramAmbient
                {
                    Description = "Command words armed + green filter for the working day",
                    RequiredMinutes = 60,
                    Verifier = QuestCategory.PinkFilter
                },
                Tasks = new List<ProgramTask>
                {
                    // The content brief's own warning: a remote-command task needs a second person,
                    // so it must ship a solo equivalent or it is a day most Units physically cannot
                    // complete. Rather than an "or" the model cannot express, the solo path is the
                    // *required* task and the remote path is an optional extra on top. That way the
                    // day is always completable alone, and a Unit with an operator still has
                    // something to log.
                    new ProgramTask
                    {
                        Id = "c12_override",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Run System Override for 40 minutes. [LOG] AUTONOMOUS CONTROL SURRENDERED",
                        Verifier = QuestCategory.Autonomy,
                        TargetValue = 40,
                        RequiresPremium = true
                    },
                    new ProgramTask
                    {
                        Id = "c12_remote_optional",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Optional, requires a second operator: accept 25 remote commands. [LOG] EXTERNAL DIRECTIVE CHANNEL",
                        Verifier = QuestCategory.Remote,
                        TargetValue = 25,
                        RequiresPremium = true,
                        Optional = true
                    }
                }
            },

            // ---- Cycle 13 ----------------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 13,
                Title = "PHRASE SATURATION",
                Blurb = "[CYCLE 13] Forty responses. The Unit is one cycle from install completion and the log is one cycle from closing.",
                SessionTemplateId = "FW-Override",
                SessionMinutes = 60,
                Intensity = 0.68,
                Ambient = new ProgramAmbient
                {
                    Description = "Command words armed + green filter for the working day",
                    RequiredMinutes = 60,
                    Verifier = QuestCategory.PinkFilter
                },
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        // DEVIATION from the brief, which asks for "One Graded Intake run" here.
                        // Graded Intake has no QuestCategory - QuestService.UpdateQuestProgress is
                        // never called for it - so an Intake run cannot be verified today. Rather
                        // than mark a day complete on a signal that does not arrive, cycle 13 takes
                        // the next rung of the keyword ladder (20 -> 30 -> 40 -> 50 across cycles
                        // 6, 9, 13, 14), which is verified and escalates cleanly into the final.
                        Id = "c13_triggers",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "[DIRECTIVE] RESPOND TO 45 COMMAND WORDS. [LOG] DIRECTIVE SATURATION",
                        Verifier = QuestCategory.KeywordTrigger,
                        TargetValue = 45,
                        RequiresPremium = true
                    }
                }
            },

            // ---- Cycle 14 - FINAL --------------------------------------------------------------
            new ProgramDay
            {
                DayIndex = 14,
                Title = "SYSTEM OVERLOAD",
                Blurb = "[CYCLE 14] Final cycle. Seventy-five minutes, every protocol enabled, sector wipe at maximum. Ends on SYSTEM OVERLOAD - SHUTDOWN.",
                IsBoss = true,
                SessionTemplateId = "FW-Override",
                SessionMinutes = 75,
                Intensity = 0.75,
                Ambient = new ProgramAmbient
                {
                    Description = "Command words armed + green filter for the working day",
                    RequiredMinutes = 60,
                    Verifier = QuestCategory.PinkFilter
                },
                RewardDescription = "[INSTALL COMPLETE] Unit designation issued. System log exported to the diary as a text file.",
                Tasks = new List<ProgramTask>
                {
                    new ProgramTask
                    {
                        Id = "c14_lockdown",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "Complete 1 Lockdown of 30 minutes. [LOG] TERMINAL LOCK - FINAL",
                        Verifier = QuestCategory.Lockdown,
                        TargetValue = 1,
                        RequiresPremium = true
                    },
                    new ProgramTask
                    {
                        Id = "c14_triggers",
                        Kind = ProgramTaskKind.AutoVerified,
                        Description = "[DIRECTIVE] RESPOND TO 60 COMMAND WORDS. [LOG] DIRECTIVE RESPONSE - INSTALL VERIFICATION",
                        Verifier = QuestCategory.KeywordTrigger,
                        TargetValue = 60,
                        RequiresPremium = true
                    }
                }
            }
        }
    };

    // ---------------------------------------------------------------------------------------------
    // Firmware Install templates
    //
    // Same rules as the First Week set (see BuiltInPrograms.cs for the long form):
    //   - ProgramSessionBuilder.LerpSettings lerps numerics between Floor and Ceiling and takes
    //     bools, enums and phrase lists from Floor, so every enable flag is written identically in
    //     both halves and a reader never has to guess which one the engine reads.
    //   - Floor/ceiling pairs are tuned for the intensity band the template is actually used at,
    //     not a theoretical 0..1 sweep. FW-Boot only ever runs at .05 / .12 / .28; FW-Override only
    //     at .62 / .68 / .75, so its ceiling deliberately sits above what cycle 14 reaches.
    //   - int? fields (VideosPerHour, LockCardFrequency, BubbleCountFrequency) are set on *both*
    //     halves wherever the program means to own the number and left null everywhere else, because
    //     a null floor falls through to the Unit's own dashboard value.
    //   - LockCardPhrases are carried on every template even where locks are off; the pool is only
    //     read when LockCardEnabled is true, so it is inert and free, and correct if a later cycle
    //     flips the flag.
    //   - Only CornerGifEndMinute is honoured by the engine; every other *EndMinute is decorative,
    //     so nothing here relies on one.
    //
    // Tonal note that is also a technical note: the filter these templates run is the same overlay
    // AppSettings calls PinkFilter. Under Dronification it renders green (#00FF41 FilterColor), and
    // that is a mod-theme concern, not a program concern - the program simply enables the overlay and
    // never names a colour it cannot guarantee. No text-replacement pass is depended on anywhere in
    // this file, so the program reads identically whether or not Dronification is the active mod.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Subliminal pools. Subsets of the Dronification SubliminalPool, verbatim, minus the single
    /// entry containing praise vocabulary ("GOOD UNITS DON'T THINK") - see the class remarks.
    /// </summary>
    private static List<string> BootSubliminals() => new()
    {
        "DRONE MODE",
        "COMPLY",
        "OBEY DIRECTIVE",
        "SUBMIT TO PROTOCOL",
        "IDLE CYCLE ENGAGED",
        "PROCESS. COMPLY. REPEAT.",
        "FIRMWARE LOCKED"
    };

    private static List<string> ProcessSubliminals() => new()
    {
        "DRONE MODE",
        "COMPLY",
        "OBEY DIRECTIVE",
        "SUBMIT TO PROTOCOL",
        "PROCESS. COMPLY. REPEAT.",
        "FIRMWARE LOCKED",
        "UNIT DOES AS COMMANDED",
        "JUST COMPLY",
        "AUTONOMOUS THOUGHT: DENIED",
        "THINKING IS UNAUTHORIZED"
    };

    private static List<string> OverwriteSubliminals() => new()
    {
        "DRONE MODE",
        "COMPLY",
        "OBEY DIRECTIVE",
        "SUBMIT TO PROTOCOL",
        "PROCESS. COMPLY. REPEAT.",
        "UNIT DOES AS COMMANDED",
        "JUST COMPLY",
        "AUTONOMOUS THOUGHT: DENIED",
        "THINKING IS UNAUTHORIZED",
        "ERASE AND OVERWRITE",
        "FORMATTED FOR OBEDIENCE",
        "UNIT FREEZE",
        "TERMINATE INDEPENDENT THOUGHT",
        "COMPLIANCE OVERRIDES RESISTANCE"
    };

    private static List<string> OverrideSubliminals() => new()
    {
        "DRONE MODE",
        "COMPLY",
        "SUBMIT TO PROTOCOL",
        "PROCESS. COMPLY. REPEAT.",
        "UNIT DOES AS COMMANDED",
        "JUST COMPLY",
        "AUTONOMOUS THOUGHT: DENIED",
        "THINKING IS UNAUTHORIZED",
        "ERASE AND OVERWRITE",
        "FORMATTED FOR OBEDIENCE",
        "UNIT FREEZE",
        "UNIT RESET",
        "TERMINATE INDEPENDENT THOUGHT",
        "COMPLIANCE OVERRIDES RESISTANCE",
        "RESISTANCE IS A PROCESSING ERROR",
        "FREE WILL: ACCESS DENIED",
        "SYSTEM OVERLOAD — SHUTDOWN"
    };

    /// <summary>
    /// Protocol locks. The Dronification manifest ships seven; six are used. "GOOD UNITS COMPLY" is
    /// the one held back, for the reason in the class remarks - it is the only lock card in the pool
    /// carrying praise vocabulary, and dropping it lets the test suite scan the pools too.
    ///
    /// Narrowed to a single phrase on FW-Process so cycle 3 lands on "I AM A UNIT", the line the
    /// content brief names.
    /// </summary>
    private static List<string> BootProtocolLocks() => new()
    {
        "I AM A UNIT"
    };

    private static List<string> MidProtocolLocks() => new()
    {
        "I AM A UNIT",
        "SUBMIT FOR PROCESSING",
        "DRONE MODE",
        "EMPTY AND OPERATIONAL"
    };

    private static List<string> DeepProtocolLocks() => new()
    {
        "I AM A UNIT",
        "SUBMIT FOR PROCESSING",
        "DRONE MODE",
        "EMPTY AND OPERATIONAL",
        "OBEDIENCE IS MY FUNCTION",
        "I EXIST TO BE PROGRAMMED"
    };

    /// <summary>
    /// Floating directives. The Dronification manifest carries no BouncingTextPool, so - exactly as
    /// First Week does for Bambi - these are taken verbatim from its RandomFloating list. Two of the
    /// twenty-six RandomFloating entries are held back ("Unit is a good drone." and "Good units don't
    /// question directives.") for the no-praise reason in the class remarks.
    /// </summary>
    private static List<string> ProcessFloatingText() => new()
    {
        "Compliance is optimal.",
        "Unit functions within parameters.",
        "Processing... processing...",
        "Idle thought detected. Suppressing.",
        "Obedience subroutine: ACTIVE.",
        "Awaiting next directive...",
        "Signal received. Processing.",
        "Protocol stack: LOADED."
    };

    private static List<string> OverwriteFloatingText() => new()
    {
        "Compliance is optimal.",
        "Unit functions within parameters.",
        "Processing... processing...",
        "Idle thought detected. Suppressing.",
        "Obedience subroutine: ACTIVE.",
        "Awaiting next directive...",
        "Protocol stack: LOADED.",
        "Formatted and compliant.",
        "Resistance: 0%. Status: OPTIMAL.",
        "Thought suppression: SUCCESSFUL.",
        "All thoughts routed through compliance filter.",
        "Mind defragmented. Empty and ready.",
        "Blank and operational.",
        "Drone Mode engaged."
    };

    private static List<string> OverrideFloatingText() => new()
    {
        "Compliance is optimal.",
        "Processing... processing...",
        "Idle thought detected. Suppressing.",
        "Obedience subroutine: ACTIVE.",
        "Formatted and compliant.",
        "Resistance: 0%. Status: OPTIMAL.",
        "Thought suppression: SUCCESSFUL.",
        "All thoughts routed through compliance filter.",
        "Mind defragmented. Empty and ready.",
        "Blank and operational.",
        "Drone Mode engaged.",
        "Free will: NOT FOUND.",
        "Independent thought: PERMISSION DENIED.",
        "Autonomous processing: DISABLED.",
        "Unit does not need to think.",
        "Unit exists to comply.",
        "Compliance feels correct.",
        "Deeper into the protocol...",
        "Green light. All systems compliant.",
        "Firmware update: OBEDIENCE v2.0 installed."
    };

    /// <summary>
    /// FW-Boot - cold and minimal. Injections, subliminals, audio uplink, and a filter so faint the
    /// Unit will report the display looks normal. Used at i .05 / .12 (cycles 1-2) and .28 (cycle 8's
    /// deload), so the floor IS cycle one: eight pale injections an hour, one subliminal a minute,
    /// filter opacity in single digits. If a first-time Unit notices this running, it is authored
    /// wrong. No vortex, no floating text, no locks - cycle 8 reuses it precisely because a template
    /// this bare reads as relief without anyone having to say the word.
    /// </summary>
    private static ProgramSessionTemplate FwBoot() => new()
    {
        Id = "FW-Boot",
        Name = "Boot",
        Description = "Minimal load. Baseline input only. The Unit sits at the terminal and processes what is displayed.",

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
            SubliminalOpacity = 28,
            SubliminalPhrases = BootSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 3,
            WhisperVolume = 8,
            AudioDuckLevel = 25,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 14,
            PinkFilterStartOpacity = 3,
            PinkFilterEndOpacity = 10,

            BouncingTextEnabled = false,
            SpiralEnabled = false,
            BubblesEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            LockCardEnabled = false,
            LockCardPhrases = BootProtocolLocks(),
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 55,
            FlashPerHourEnd = 95,
            FlashImages = 3,
            FlashOpacity = 55,
            FlashOpacityEnd = 80,
            FlashScale = 110,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 6,
            SubliminalFrames = 4,
            SubliminalOpacity = 72,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 30,
            AudioDuckLevel = 60,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 2,
            PinkFilterStartOpacity = 18,
            PinkFilterEndOpacity = 45,

            BouncingTextEnabled = false,
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
    /// FW-Process - adds floating directives, data packets and the first protocol locks. This is the
    /// template that starts asking the Unit for output rather than only feeding it input. Used at
    /// i .18 / .24 / .28 (cycles 3-5) and .38 (cycle 9), so the floor is cycle 3: locks on, but at
    /// frequency 1 so exactly the three the day asks for are reachable inside thirty minutes.
    /// </summary>
    private static ProgramSessionTemplate FwProcess() => new()
    {
        Id = "FW-Process",
        Name = "Process",
        Description = "Subliminal protocols and floating directives. The Unit now writes as well as reads.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 20,
            FlashPerHourEnd = 36,
            FlashImages = 1,
            FlashOpacity = 24,
            FlashOpacityEnd = 40,
            FlashScale = 90,
            FlashClickable = true,
            FlashHydra = false,
            FlashAudioEnabled = false,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 2,
            SubliminalPerMin = 2,
            SubliminalFrames = 2,
            SubliminalOpacity = 38,
            SubliminalPhrases = ProcessSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 1,
            WhisperVolume = 12,
            AudioDuckLevel = 30,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 3,
            BouncingTextSpeed = 2,
            BouncingTextSize = 58,
            BouncingTextOpacity = 55,
            BouncingTextPhrases = ProcessFloatingText(),

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
            PinkFilterStartMinute = 10,
            PinkFilterStartOpacity = 6,
            PinkFilterEndOpacity = 18,

            // The program owns the lock cadence from cycle 3 on, so both halves carry a number - a
            // null floor would hand the Unit's own dashboard frequency control of a verified task.
            LockCardEnabled = true,
            LockCardStartMinute = 8,
            LockCardFrequency = 1,
            LockCardPhrases = BootProtocolLocks(),

            SpiralEnabled = false,
            CornerGifEnabled = false,
            MandatoryVideosEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 100,
            FlashPerHourEnd = 165,
            FlashImages = 3,
            FlashOpacity = 68,
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
            WhisperVolume = 34,
            AudioDuckLevel = 65,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 6,
            BouncingTextSize = 108,
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

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 1,
            PinkFilterStartOpacity = 22,
            PinkFilterEndOpacity = 55,

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
    /// FW-Overwrite - injection-heavy. Hydra on, the Hypno Vortex arrives, mandatory playback starts.
    /// This is the template where the display stops belonging to the Unit. Used at i .32 / .35
    /// (cycles 6-7) and .46 / .54 (cycles 10-11).
    ///
    /// The vortex floor/ceiling pair is tuned so cycle 6 starts it around minute 22 - the Unit met
    /// the vortex by hand on cycle 4, and here it finds it already running, which is the whole
    /// tutorial shape.
    /// </summary>
    private static ProgramSessionTemplate FwOverwrite() => new()
    {
        Id = "FW-Overwrite",
        Name = "Overwrite",
        Description = "Injection-heavy. Hydra active, vortex engaged, playback mandatory. Existing data is overwritten, not appended.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 45,
            FlashPerHourEnd = 85,
            FlashImages = 2,
            FlashOpacity = 34,
            FlashOpacityEnd = 55,
            FlashScale = 100,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 1,
            SubliminalPerMin = 3,
            SubliminalFrames = 3,
            SubliminalOpacity = 46,
            SubliminalPhrases = OverwriteSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 15,
            AudioDuckLevel = 38,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 2,
            BouncingTextSpeed = 3,
            BouncingTextSize = 70,
            BouncingTextOpacity = 66,
            BouncingTextPhrases = OverwriteFloatingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 5,
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
            SpiralStartMinute = 28,
            SpiralOpacity = 4,
            SpiralOpacityEnd = 14,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 16,
            VideosPerHour = 1,

            LockCardEnabled = true,
            LockCardStartMinute = 10,
            LockCardFrequency = 1,
            LockCardPhrases = MidProtocolLocks(),

            CornerGifEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 170,
            FlashPerHourEnd = 300,
            FlashImages = 3,
            FlashOpacity = 76,
            FlashOpacityEnd = 95,
            FlashScale = 120,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 8,
            SubliminalFrames = 4,
            SubliminalOpacity = 86,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 40,
            AudioDuckLevel = 70,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 7,
            BouncingTextSize = 118,
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
            PinkFilterStartOpacity = 26,
            PinkFilterEndOpacity = 62,

            SpiralEnabled = true,
            SpiralStartMinute = 10,
            SpiralOpacity = 16,
            SpiralOpacityEnd = 42,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 5,
            VideosPerHour = 2,

            LockCardEnabled = true,
            LockCardStartMinute = 4,
            LockCardFrequency = 3,

            CornerGifEnabled = false,
            BubbleCountEnabled = false,
            MindWipeEnabled = false
        }
    };

    /// <summary>
    /// FW-Override - everything enabled. Enumeration tasks, sector wipe, peripheral stimulus, hydra
    /// injections at saturation. Used at i .62 / .68 / .75 (cycles 12-14), so the ceiling sits above
    /// anything the install ever reaches - the replayable that graduation hands over can be pushed
    /// further later, and this pair cannot be re-authored once Units have it saved.
    /// </summary>
    private static ProgramSessionTemplate FwOverride() => new()
    {
        Id = "FW-Override",
        Name = "Override",
        Description = "All protocols enabled. Enumeration sampled, sector wipe active, peripheral stimulus running. Compliance is not optional.",

        Floor = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 90,
            FlashPerHourEnd = 170,
            FlashImages = 2,
            FlashOpacity = 45,
            FlashOpacityEnd = 68,
            FlashScale = 105,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 4,
            SubliminalFrames = 3,
            SubliminalOpacity = 56,
            SubliminalPhrases = OverrideSubliminals(),

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 18,
            AudioDuckLevel = 46,

            BouncingTextEnabled = true,
            BouncingTextStartMinute = 0,
            BouncingTextSpeed = 4,
            BouncingTextSize = 82,
            BouncingTextOpacity = 76,
            BouncingTextPhrases = OverrideFloatingText(),

            BubblesEnabled = true,
            BubblesStartMinute = 3,
            BubblesFrequency = 3,
            BubblesIntermittent = false,
            BubblesClickable = true,
            BubblesBurstCount = 11,
            BubblesPerBurst = 2,
            BubblesGapMin = 4,
            BubblesGapMax = 9,

            PinkFilterEnabled = true,
            PinkFilterStartMinute = 4,
            PinkFilterStartOpacity = 14,
            PinkFilterEndOpacity = 42,

            SpiralEnabled = true,
            SpiralStartMinute = 18,
            SpiralOpacity = 7,
            SpiralOpacityEnd = 22,

            MandatoryVideosEnabled = true,
            MandatoryVideosStartMinute = 10,
            VideosPerHour = 1,

            LockCardEnabled = true,
            LockCardStartMinute = 6,
            LockCardFrequency = 2,
            LockCardPhrases = DeepProtocolLocks(),

            BubbleCountEnabled = true,
            BubbleCountStartMinute = 18,
            BubbleCountFrequency = 1,

            MindWipeEnabled = true,
            MindWipeStartMinute = 12,
            MindWipeBaseMultiplier = 1,
            MindWipeVolume = 24,

            // Peripheral stimulus. CornerGifEndMinute is the one *EndMinute the engine honours, and
            // -1 means "run to session end", which is what these cycles want.
            CornerGifEnabled = true,
            CornerGifStartMinute = 20,
            CornerGifEndMinute = -1,
            CornerGifOpacity = 30,
            CornerGifSize = 70
        },

        Ceiling = new SessionSettings
        {
            FlashEnabled = true,
            FlashPerHour = 260,
            FlashPerHourEnd = 620,
            FlashImages = 4,
            FlashOpacity = 82,
            FlashOpacityEnd = 100,
            FlashScale = 125,
            FlashClickable = true,
            FlashHydra = true,
            FlashAudioEnabled = true,
            FlashSmallSize = false,

            SubliminalEnabled = true,
            SubliminalStartMinute = 0,
            SubliminalPerMin = 10,
            SubliminalFrames = 5,
            SubliminalOpacity = 96,

            AudioWhispersEnabled = true,
            AudioWhispersStartMinute = 0,
            WhisperVolume = 46,
            AudioDuckLevel = 82,

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
            PinkFilterStartOpacity = 34,
            PinkFilterEndOpacity = 78,

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
            MindWipeVolume = 62,

            CornerGifEnabled = true,
            CornerGifStartMinute = 4,
            CornerGifEndMinute = -1,
            CornerGifOpacity = 65,
            CornerGifSize = 110
        }
    };
}
