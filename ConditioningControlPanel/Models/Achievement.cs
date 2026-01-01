using System;
using System.Collections.Generic;

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
            ImageName = "lv 10.png",
            Category = AchievementCategory.Progression
        },
        ["dumb_bimbo"] = new Achievement
        {
            Id = "dumb_bimbo",
            Name = "Dumb Bimbo",
            Requirement = "Reach Level 20",
            FlavorText = "We're losing some IQ points, right Bambi?",
            ImageName = "Dumb Bimbo.png",
            Category = AchievementCategory.Progression
        },
        ["fully_synthetic"] = new Achievement
        {
            Id = "fully_synthetic",
            Name = "Fully Synthetic",
            Requirement = "Reach Level 50",
            FlavorText = "More plastic than flesh. Your transformation is becoming permanent.",
            ImageName = "lv 50.png",
            Category = AchievementCategory.Progression
        },
        ["perfect_plastic_puppet"] = new Achievement
        {
            Id = "perfect_plastic_puppet",
            Name = "Perfect Plastic Puppet",
            Requirement = "Reach Level 100",
            FlavorText = "And you thought it was just a game, uh?",
            ImageName = "perfect plastic puppet.png",
            Category = AchievementCategory.Progression
        },
        
        // ========== TIME & SESSIONS ==========
        ["rose_tinted_reality"] = new Achievement
        {
            Id = "rose_tinted_reality",
            Name = "Rose-Tinted Reality",
            Requirement = "Keep the Pink Filter active for 10 cumulative hours",
            FlavorText = "The world just looks better this way, doesn't it?",
            ImageName = "10 hours pink.png",
            Category = AchievementCategory.TimeSessions
        },
        ["deep_sleep"] = new Achievement
        {
            Id = "deep_sleep",
            Name = "Deep Sleep Mode",
            Requirement = "Complete a session lasting longer than 3 hours",
            FlavorText = "Who needs the real world anyway?",
            ImageName = "deep sleep.png",
            Category = AchievementCategory.TimeSessions
        },
        ["daily_maintenance"] = new Achievement
        {
            Id = "daily_maintenance",
            Name = "Daily Maintenance",
            Requirement = "Launch the app 7 days in a row",
            FlavorText = "Good dolls need regular updates.",
            ImageName = "daily mainenance.png",
            Category = AchievementCategory.TimeSessions
        },
        ["retinal_burn"] = new Achievement
        {
            Id = "retinal_burn",
            Name = "Retinal Burn",
            Requirement = "Have 5,000 Flash Images displayed",
            FlavorText = "Close your eyes. You can still see them, can't you?",
            ImageName = "retinal burn.png",
            Category = AchievementCategory.TimeSessions
        },
        ["morning_glory"] = new Achievement
        {
            Id = "morning_glory",
            Name = "Morning Glory",
            Requirement = "Complete Morning Drift between 6-9 AM",
            FlavorText = "Starting the day on the right frequency.",
            ImageName = "morning glory.png",
            Category = AchievementCategory.TimeSessions
        },
        ["player_2_disconnected"] = new Achievement
        {
            Id = "player_2_disconnected",
            Name = "Player 2 Disconnected",
            Requirement = "Complete Gamer Girl without Alt+Tab",
            FlavorText = "Game over. The conditioning won.",
            ImageName = "player 2 disconnected.png",
            Category = AchievementCategory.TimeSessions
        },
        ["sofa_decor"] = new Achievement
        {
            Id = "sofa_decor",
            Name = "Sofa Decor",
            Requirement = "Complete The Distant Doll session",
            FlavorText = "You're just a pretty accessory for the furniture now.",
            ImageName = "Sofa decor.png",
            Category = AchievementCategory.TimeSessions
        },
        ["look_but_dont_touch"] = new Achievement
        {
            Id = "look_but_dont_touch",
            Name = "Look, But Don't Touch",
            Requirement = "Complete Good Girls Don't Cum with Strict Lock",
            FlavorText = "Frustration is just another word for devotion.",
            ImageName = "look but dont touch.png",
            Category = AchievementCategory.TimeSessions
        },
        ["spiral_eyes"] = new Achievement
        {
            Id = "spiral_eyes",
            Name = "Spiral Eyes",
            Requirement = "Stare at the Spiral Overlay for 20 minutes",
            FlavorText = "Round and round it goes, where your mind went, nobody knows.",
            ImageName = "spiral eyes.png",
            Category = AchievementCategory.TimeSessions
        },
        
        // ========== MINIGAMES & SKILL ==========
        ["mathematicians_nightmare"] = new Achievement
        {
            Id = "mathematicians_nightmare",
            Name = "Mathematician's Nightmare",
            Requirement = "Guess correct bubble count 5 times in a row",
            FlavorText = "You're surprisingly good at counting for an airhead.",
            ImageName = "Mathematician's nightmare.png",
            Category = AchievementCategory.Minigames
        },
        ["pop_the_thought"] = new Achievement
        {
            Id = "pop_the_thought",
            Name = "Pop Goes The Thought",
            Requirement = "Pop 1,000 bubbles total",
            FlavorText = "Every pop is a thought disappearing.",
            ImageName = "pop the Thought.png",
            Category = AchievementCategory.Minigames
        },
        ["typing_tutor"] = new Achievement
        {
            Id = "typing_tutor",
            Name = "Typing Tutor",
            Requirement = "Complete Lock Card with 100% accuracy",
            FlavorText = "Good muscle memory. Your fingers know what to say.",
            ImageName = "typing tutor.png",
            Category = AchievementCategory.Minigames
        },
        ["obedience_reflex"] = new Achievement
        {
            Id = "obedience_reflex",
            Name = "Obedience Reflex",
            Requirement = "Complete Lock Card (3 phrases) in under 15 seconds",
            FlavorText = "You didn't even read it, you just typed. Speed is a sign of devotion.",
            ImageName = "obedience reflex.png",
            Category = AchievementCategory.Minigames
        },
        ["mercy_beggar"] = new Achievement
        {
            Id = "mercy_beggar",
            Name = "Mercy Beggar",
            Requirement = "Fail the attention check 3 times",
            FlavorText = "Too dumb to focus? Time for a penalty.",
            ImageName = "mercy beggar.png",
            Category = AchievementCategory.Minigames
        },
        ["clean_slate"] = new Achievement
        {
            Id = "clean_slate",
            Name = "Clean Slate",
            Requirement = "Let Mind Wipers run for 60 seconds",
            FlavorText = "Squeaky clean. No thoughts, just shine.",
            ImageName = "clean slate.png",
            Category = AchievementCategory.Minigames
        },
        ["corner_hit"] = new Achievement
        {
            Id = "corner_hit",
            Name = "Corner Hit",
            Requirement = "Watch Bouncing Text hit the exact corner",
            FlavorText = "The most exciting thing that happened all day.",
            ImageName = "corner hit.png",
            Category = AchievementCategory.Minigames
        },
        ["neon_obsession"] = new Achievement
        {
            Id = "neon_obsession",
            Name = "Neon Obsession",
            Requirement = "Click on the Avatar 20 times rapidly",
            FlavorText = "Hey! I'm just a drawing... or am I?",
            ImageName = "Neon obsession.png",
            Category = AchievementCategory.Minigames
        },
        
        // ========== HARDCORE & SYSTEM ==========
        ["what_panic_button"] = new Achievement
        {
            Id = "what_panic_button",
            Name = "Panic Button? What Panic Button?",
            Requirement = "Complete any session with Disable Panic enabled",
            FlavorText = "There is no escape, and you love it.",
            ImageName = "What panic button.png",
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
            ImageName = "total lockdown.png",
            Category = AchievementCategory.Hardcore
        },
        ["system_overload"] = new Achievement
        {
            Id = "system_overload",
            Name = "System Overload",
            Requirement = "Have Bubbles, Bouncing Text, and Spiral all active",
            FlavorText = "Too much input. Brain.exe has stopped working.",
            ImageName = "system overload.png",
            Category = AchievementCategory.Hardcore
        }
    };
}

public enum AchievementCategory
{
    Progression,
    TimeSessions,
    Minigames,
    Hardcore
}
