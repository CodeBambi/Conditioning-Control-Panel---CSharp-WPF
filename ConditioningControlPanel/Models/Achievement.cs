using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Represents an achievement that can be unlocked
/// </summary>
public class Achievement
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Requirement { get; set; } = "";
    public string FlavorText { get; set; } = "";
    public string ImageName { get; set; } = "";
    public AchievementCategory Category { get; set; }

    /// <summary>
    /// When true, this is a patron-exclusive achievement. It lives in a separate
    /// gallery section with its own counter (never summed with the free count) and
    /// only unlocks for an entitled user (see AchievementService.TryUnlockExclusive).
    /// A user who already earned one keeps it after a downgrade.
    /// </summary>
    public bool IsExclusive { get; set; }

    /// <summary>Localized achievement name (falls back to hardcoded Name)</summary>
    public string LocalizedName => Loc.Get($"achievement_{Id}_name");
    /// <summary>Localized achievement requirement text (falls back to hardcoded Requirement)</summary>
    public string LocalizedRequirement => Loc.Get($"achievement_{Id}_req");
    /// <summary>Localized achievement flavor text (falls back to hardcoded FlavorText)</summary>
    public string LocalizedFlavorText => Loc.Get($"achievement_{Id}_flavor");
    
    /// <summary>
    /// All achievements in the game
    /// </summary>
    public static readonly Dictionary<string, Achievement> All = new()
    {
        // ========== PROGRESSION & LEVELS ==========
        ["plastic_initiation"] = new Achievement
        {
            Id = "plastic_initiation",
            Name = "Plastic Initiation",
            Requirement = "Reach Level 10",
            FlavorText = "Welcome to the dollhouse. You're just getting started.",
            ImageName = "lv_10.png",
            Category = AchievementCategory.Progression
        },
        ["dumb_bimbo"] = new Achievement
        {
            Id = "dumb_bimbo",
            Name = "Dumb Bimbo",
            Requirement = "Reach Level 20",
            FlavorText = "We're losing some IQ points, right Bambi?",
            ImageName = "Dumb_Bimbo.png",
            Category = AchievementCategory.Progression
        },
        ["fully_synthetic"] = new Achievement
        {
            Id = "fully_synthetic",
            Name = "Fully Synthetic",
            Requirement = "Reach Level 50",
            FlavorText = "More plastic than flesh. Your transformation is becoming permanent.",
            ImageName = "lv_50.png",
            Category = AchievementCategory.Progression
        },
        ["docile_cow"] = new Achievement
        {
            Id = "docile_cow",
            Name = "Docile Cow",
            Requirement = "Reach Level 75",
            FlavorText = "Moo~ Such a good, obedient cow. Content to graze and be milked.",
            ImageName = "docile_cow.png",
            Category = AchievementCategory.Progression
        },
        ["perfect_plastic_puppet"] = new Achievement
        {
            Id = "perfect_plastic_puppet",
            Name = "Perfect Plastic Puppet",
            Requirement = "Reach Level 100",
            FlavorText = "And you thought it was just a game, uh?",
            ImageName = "perfect_plastic_puppet.png",
            Category = AchievementCategory.Progression
        },
        ["brainwashed_slavedoll"] = new Achievement
        {
            Id = "brainwashed_slavedoll",
            Name = "Brainwashed Slavedoll",
            Requirement = "Reach Level 125",
            FlavorText = "Your mind belongs to the conditioning now. There's no going back.",
            ImageName = "BrainwashedSlavedoll.png",
            Category = AchievementCategory.Progression
        },
        ["platinum_puppet"] = new Achievement
        {
            Id = "platinum_puppet",
            Name = "Platinum Puppet",
            Requirement = "Reach Level 150",
            FlavorText = "The ultimate achievement. You've transcended into pure, devoted obedience.",
            ImageName = "PlatinumPuppet.png",
            Category = AchievementCategory.Progression
        },

        // ========== TIME & SESSIONS ==========
        ["rose_tinted_reality"] = new Achievement
        {
            Id = "rose_tinted_reality",
            Name = "Rose-Tinted Reality",
            Requirement = "Keep the Pink Filter active for 10 cumulative hours",
            FlavorText = "The world just looks better this way, doesn't it?",
            ImageName = "10_hours_pink.png",
            Category = AchievementCategory.TimeSessions
        },
        ["deep_sleep"] = new Achievement
        {
            Id = "deep_sleep",
            Name = "Deep Sleep Mode",
            Requirement = "Complete a session lasting longer than 3 hours",
            FlavorText = "Who needs the real world anyway?",
            ImageName = "deep_sleep.png",
            Category = AchievementCategory.TimeSessions
        },
        ["daily_maintenance"] = new Achievement
        {
            Id = "daily_maintenance",
            Name = "Daily Maintenance",
            Requirement = "Launch the app 7 days in a row",
            FlavorText = "Good dolls need regular updates.",
            ImageName = "daily_maintenance.png",
            Category = AchievementCategory.TimeSessions
        },
        ["retinal_burn"] = new Achievement
        {
            Id = "retinal_burn",
            Name = "Retinal Burn",
            Requirement = "Have 5,000 Flash Images displayed",
            FlavorText = "Close your eyes. You can still see them, can't you?",
            ImageName = "retinal_burn.png",
            Category = AchievementCategory.TimeSessions
        },
        ["morning_glory"] = new Achievement
        {
            Id = "morning_glory",
            Name = "Morning Glory",
            Requirement = "Complete Morning Drift between 6-9 AM",
            FlavorText = "Starting the day on the right frequency.",
            ImageName = "morning_glory.png",
            Category = AchievementCategory.TimeSessions
        },
        ["player_2_disconnected"] = new Achievement
        {
            Id = "player_2_disconnected",
            Name = "Player 2 Disconnected",
            Requirement = "Complete Gamer Girl without Alt+Tab",
            FlavorText = "Game over. The conditioning won.",
            ImageName = "player_2_disconnected.png",
            Category = AchievementCategory.TimeSessions
        },
        ["sofa_decor"] = new Achievement
        {
            Id = "sofa_decor",
            Name = "Sofa Decor",
            Requirement = "Complete The Distant Doll session",
            FlavorText = "You're just a pretty accessory for the furniture now.",
            ImageName = "Sofa_decor.png",
            Category = AchievementCategory.TimeSessions
        },
        ["look_but_dont_touch"] = new Achievement
        {
            Id = "look_but_dont_touch",
            Name = "Look, But Don't Touch",
            Requirement = "Complete Good Girls Don't Cum with Strict Lock",
            FlavorText = "Frustration is just another word for devotion.",
            ImageName = "look_but_dont_touch.png",
            Category = AchievementCategory.TimeSessions
        },
        ["spiral_eyes"] = new Achievement
        {
            Id = "spiral_eyes",
            Name = "Spiral Eyes",
            Requirement = "Stare at the Spiral Overlay for 20 minutes",
            FlavorText = "Round and round it goes, where your mind went, nobody knows.",
            ImageName = "spiral_eyes.png",
            Category = AchievementCategory.TimeSessions
        },
        
        // ========== MINIGAMES & SKILL ==========
        ["mathematicians_nightmare"] = new Achievement
        {
            Id = "mathematicians_nightmare",
            Name = "Mathematician's Nightmare",
            Requirement = "Guess correct bubble count 5 times in a row",
            FlavorText = "You're surprisingly good at counting for an airhead.",
            ImageName = "Mathematician's_nightmare.png",
            Category = AchievementCategory.Minigames
        },
        ["pop_the_thought"] = new Achievement
        {
            Id = "pop_the_thought",
            Name = "Pop Goes The Thought",
            Requirement = "Pop 1,000 bubbles total",
            FlavorText = "Every pop is a thought disappearing.",
            ImageName = "pop_the_Thought.png",
            Category = AchievementCategory.Minigames
        },
        ["typing_tutor"] = new Achievement
        {
            Id = "typing_tutor",
            Name = "Typing Tutor",
            Requirement = "Complete Lock Card with 100% accuracy",
            FlavorText = "Good muscle memory. Your fingers know what to say.",
            ImageName = "typing_tutor.png",
            Category = AchievementCategory.Minigames
        },
        ["obedience_reflex"] = new Achievement
        {
            Id = "obedience_reflex",
            Name = "Obedience Reflex",
            Requirement = "Complete Lock Card (3 phrases) in under 15 seconds",
            FlavorText = "You didn't even read it, you just typed. Speed is a sign of devotion.",
            ImageName = "obedience_reflex.png",
            Category = AchievementCategory.Minigames
        },
        ["mercy_beggar"] = new Achievement
        {
            Id = "mercy_beggar",
            Name = "Mercy Beggar",
            Requirement = "Fail the attention check 3 times",
            FlavorText = "Too dumb to focus? Time for a penalty.",
            ImageName = "mercy_beggar.png",
            Category = AchievementCategory.Minigames
        },
        ["clean_slate"] = new Achievement
        {
            Id = "clean_slate",
            Name = "Clean Slate",
            Requirement = "Let Mind Wipers run for 60 seconds",
            FlavorText = "Squeaky clean. No thoughts, just shine.",
            ImageName = "clean_slate.png",
            Category = AchievementCategory.Minigames
        },
        ["corner_hit"] = new Achievement
        {
            Id = "corner_hit",
            Name = "Corner Hit",
            Requirement = "Watch Bouncing Text hit the exact corner",
            FlavorText = "The most exciting thing that happened all day.",
            ImageName = "corner_hit.png",
            Category = AchievementCategory.Minigames
        },
        ["neon_obsession"] = new Achievement
        {
            Id = "neon_obsession",
            Name = "Neon Obsession",
            Requirement = "Click on the Avatar 20 times rapidly",
            FlavorText = "Hey! I'm just a drawing... or am I?",
            ImageName = "Neon_obsession.png",
            Category = AchievementCategory.Minigames
        },
        
        // ========== HARDCORE & SYSTEM ==========
        ["what_panic_button"] = new Achievement
        {
            Id = "what_panic_button",
            Name = "Panic Button? What Panic Button?",
            Requirement = "Complete any session with Disable Panic enabled",
            FlavorText = "There is no escape, and you love it.",
            ImageName = "What_panic_button.png",
            Category = AchievementCategory.Hardcore
        },
        ["relapse"] = new Achievement
        {
            Id = "relapse",
            Name = "Relapse",
            Requirement = "Press ESC to stop, then restart within 10 seconds",
            FlavorText = "You got scared, but you came running right back. You need this.",
            ImageName = "relapse.png",
            Category = AchievementCategory.Hardcore
        },
        ["total_lockdown"] = new Achievement
        {
            Id = "total_lockdown",
            Name = "Total Lockdown",
            Requirement = "Activate Strict Lock, No Panic, and Pink Filter together",
            FlavorText = "The Danger Combination. Brave... or foolish?",
            ImageName = "total_lockdown.png",
            Category = AchievementCategory.Hardcore
        },
        ["system_overload"] = new Achievement
        {
            Id = "system_overload",
            Name = "System Overload",
            Requirement = "Have Bubbles, Bouncing Text, and Spiral all active",
            FlavorText = "Too much input. Brain.exe has stopped working.",
            ImageName = "system_overload.png",
            Category = AchievementCategory.Hardcore
        },

        // ========== CREATOR (achievements v2 — Phase 1) ==========
        ["not_a_video_editor"] = new Achievement
        {
            Id = "not_a_video_editor",
            Name = "Not a Video Editor",
            Requirement = "Build your first enhancement",
            FlavorText = "btw, not a video editor. and yet.",
            ImageName = "not_a_video_editor.png",
            Category = AchievementCategory.Creator
        },
        ["mad_scientist"] = new Achievement
        {
            Id = "mad_scientist",
            Name = "Mad Scientist",
            Requirement = "Build an enhancement using 5+ triggers",
            FlavorText = "Five triggers in one build. What are you cooking?",
            ImageName = "mad_scientist.png",
            Category = AchievementCategory.Creator
        },
        ["modder"] = new Achievement
        {
            Id = "modder",
            Name = "Modder",
            Requirement = "Install your first mod",
            FlavorText = "First mod installed. The rabbit hole goes deeper than you thought.",
            ImageName = "modder.png",
            Category = AchievementCategory.Creator
        },
        ["curator"] = new Achievement
        {
            Id = "curator",
            Name = "Curator",
            Requirement = "Activate 10 different mods",
            FlavorText = "Ten mods deep. Quite the collection.",
            ImageName = "curator.png",
            Category = AchievementCategory.Creator
        },
        ["community_supported"] = new Achievement
        {
            Id = "community_supported",
            Name = "Community Supported",
            Requirement = "Activate 3 community mods",
            FlavorText = "Running other people's work. We're all in this together.",
            ImageName = "community_supported.png",
            Category = AchievementCategory.Creator
        },

        // ========== KEYWORD (achievements v2 — Phase 1) ==========
        ["magic_word"] = new Achievement
        {
            Id = "magic_word",
            Name = "Magic Word",
            Requirement = "Fire your first keyword trigger",
            FlavorText = "Said the word, felt the pull. Just like that.",
            ImageName = "magic_word.png",
            Category = AchievementCategory.TimeSessions
        },
        ["pavlov"] = new Achievement
        {
            Id = "pavlov",
            Name = "Pavlov",
            Requirement = "Fire 500 keyword triggers",
            FlavorText = "Five hundred times. The bell rings, you respond. No thinking required.",
            ImageName = "pavlov.png",
            Category = AchievementCategory.TimeSessions
        },

        // ========== COMPANION (achievements v2 — Phase 1) ==========
        ["pleased_to_meet_you"] = new Achievement
        {
            Id = "pleased_to_meet_you",
            Name = "Pleased to Meet You",
            Requirement = "Send your first message to the companion",
            FlavorText = "First words with her. This is the start of something.",
            ImageName = "pleased_to_meet_you.png",
            Category = AchievementCategory.Minigames
        },
        ["pillow_talk"] = new Achievement
        {
            Id = "pillow_talk",
            Name = "Pillow Talk",
            Requirement = "Send 100 messages to the companion",
            FlavorText = "A hundred messages in. She's getting to know you.",
            ImageName = "pillow_talk.png",
            Category = AchievementCategory.Minigames
        },
        ["best_friends"] = new Achievement
        {
            Id = "best_friends",
            Name = "Best Friends",
            Requirement = "Reach a companion level milestone",
            FlavorText = "She's leveled up right alongside you. Inseparable now.",
            ImageName = "best_friends.png",
            Category = AchievementCategory.Minigames
        },

        // ========== PATRON-EXCLUSIVE (achievements v2 — Phase 1) ==========
        ["blink_and_youll_miss_it"] = new Achievement
        {
            Id = "blink_and_youll_miss_it",
            Name = "Blink and You'll Miss It",
            Requirement = "Log 100 blinks in the Blink Trainer",
            FlavorText = "A hundred blinks tracked. Every one of them counted.",
            ImageName = "blink_and_youll_miss_it.png",
            Category = AchievementCategory.Minigames,
            IsExclusive = true
        },
        ["locked_in"] = new Achievement
        {
            Id = "locked_in",
            Name = "Locked In",
            Requirement = "Trigger your first lockdown",
            FlavorText = "Door's shut. You chose this.",
            ImageName = "locked_in.png",
            Category = AchievementCategory.Hardcore,
            IsExclusive = true
        },
        ["throw_away_the_key"] = new Achievement
        {
            Id = "throw_away_the_key",
            Name = "Throw Away the Key",
            Requirement = "Sit through a 60+ minute lockdown",
            FlavorText = "A full hour locked down. You weren't going anywhere anyway.",
            ImageName = "throw_away_the_key.png",
            Category = AchievementCategory.Hardcore,
            IsExclusive = true
        },
        ["hand_over_control"] = new Achievement
        {
            Id = "hand_over_control",
            Name = "Hand Over Control",
            Requirement = "Hand over control for the first time",
            FlavorText = "You gave someone else the wheel. Brave.",
            ImageName = "hand_over_control.png",
            Category = AchievementCategory.Hardcore,
            IsExclusive = true
        },
        ["puppet_strings"] = new Achievement
        {
            Id = "puppet_strings",
            Name = "Puppet Strings",
            Requirement = "Take 100 remote commands in one session",
            FlavorText = "A hundred commands, one session. Whose hands are these?",
            ImageName = "puppet_strings.png",
            Category = AchievementCategory.Hardcore,
            IsExclusive = true
        },

        // ========== DEEPER (achievements v2 — Phase 2) ==========
        ["going_deeper"] = new Achievement
        {
            Id = "going_deeper",
            Name = "Going Deeper",
            Requirement = "Play your first enhancement",
            FlavorText = "First descent. The water's warm down here.",
            ImageName = "going_deeper.png",
            Category = AchievementCategory.Deeper
        },
        ["down_the_rabbit_hole"] = new Achievement
        {
            Id = "down_the_rabbit_hole",
            Name = "Down the Rabbit Hole",
            Requirement = "Play 25 enhancements",
            FlavorText = "Twenty five trips down. You know the way by now.",
            ImageName = "down_the_rabbit_hole.png",
            Category = AchievementCategory.Deeper
        },
        ["permanent_resident"] = new Achievement
        {
            Id = "permanent_resident",
            Name = "Permanent Resident",
            Requirement = "Spend 10 hours total in the Deeper player",
            FlavorText = "Ten hours under. You live here now, don't you?",
            ImageName = "permanent_resident.png",
            Category = AchievementCategory.Deeper
        },
        ["directors_cut"] = new Achievement
        {
            Id = "directors_cut",
            Name = "Director's Cut",
            Requirement = "Finish a featured enhancement start to end",
            FlavorText = "You sat through the whole thing. Good girl.",
            ImageName = "directors_cut.png",
            Category = AchievementCategory.Deeper
        },
        ["wired_in"] = new Achievement
        {
            Id = "wired_in",
            Name = "Wired In",
            Requirement = "Play an enhancement with webcam triggers active",
            FlavorText = "Camera on, eyes tracked. Nowhere to hide now.",
            ImageName = "wired_in.png",
            Category = AchievementCategory.Deeper
        },
        ["dont_look_away"] = new Achievement
        {
            Id = "dont_look_away",
            Name = "Don't Look Away",
            Requirement = "Hold gaze through a full webcam enhancement",
            FlavorText = "You held it the entire time. Not one glance away.",
            ImageName = "dont_look_away.png",
            Category = AchievementCategory.Deeper
        },
        ["on_rails"] = new Achievement
        {
            Id = "on_rails",
            Name = "On Rails",
            Requirement = "Fire 5+ different trigger types in one enhancement",
            FlavorText = "Every trigger firing at once. No driver needed.",
            ImageName = "on_rails.png",
            Category = AchievementCategory.Deeper
        },

        // ========== CREATOR — publish (achievements v2 — Phase 2) ==========
        ["on_the_shelf"] = new Achievement
        {
            Id = "on_the_shelf",
            Name = "On the Shelf",
            Requirement = "Publish an enhancement to the catalogue",
            FlavorText = "You made something and put it out there. Look at you.",
            ImageName = "on_the_shelf.png",
            Category = AchievementCategory.Creator
        },

        // ========== PATRON-EXCLUSIVE — quiz + gaze (achievements v2 — Phase 2) ==========
        ["top_of_the_class"] = new Achievement
        {
            Id = "top_of_the_class",
            Name = "Top of the Class",
            Requirement = "Get a perfect score on a quiz",
            FlavorText = "Perfect score. Empty head, perfect score. Funny how that works.",
            ImageName = "top_of_the_class.png",
            Category = AchievementCategory.Minigames,
            IsExclusive = true
        },
        ["teachers_pet"] = new Achievement
        {
            Id = "teachers_pet",
            Name = "Teacher's Pet",
            Requirement = "Pass 25 quizzes",
            FlavorText = "Twenty five quizzes passed. Such a good student.",
            ImageName = "teachers_pet.png",
            Category = AchievementCategory.Minigames,
            IsExclusive = true
        },
        ["honor_roll"] = new Achievement
        {
            Id = "honor_roll",
            Name = "Honor Roll",
            Requirement = "Get a perfect score in 3 different categories",
            FlavorText = "Cleared category after category. Nothing left to learn here.",
            ImageName = "honor_roll.png",
            Category = AchievementCategory.Minigames,
            IsExclusive = true
        },
        ["held_back"] = new Achievement
        {
            Id = "held_back",
            Name = "Held Back",
            Requirement = "Fail three quizzes in a row",
            FlavorText = "Three failures in a row. Maybe the material's too hard. Maybe that's the point.",
            ImageName = "held_back.png",
            Category = AchievementCategory.Minigames,
            IsExclusive = true
        },
        ["hands_free"] = new Achievement
        {
            Id = "hands_free",
            Name = "Hands-Free",
            Requirement = "Pop 50 bubbles by gaze alone",
            FlavorText = "Fifty pops, no hands. Just your eyes doing the work.",
            ImageName = "hands_free.png",
            Category = AchievementCategory.Minigames,
            IsExclusive = true
        },
        ["she_remembers"] = new Achievement
        {
            Id = "she_remembers",
            Name = "She Remembers",
            Requirement = "Companion recalls something across sessions",
            FlavorText = "She brought up something from before. She doesn't forget.",
            ImageName = "she_remembers.png",
            Category = AchievementCategory.Minigames,
            IsExclusive = true
        }
    };
}

public enum AchievementCategory
{
    Progression,
    TimeSessions,
    Minigames,
    Hardcore,
    Deeper,
    Creator
}
