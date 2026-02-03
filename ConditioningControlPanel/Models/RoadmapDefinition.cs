using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Models;

public enum RoadmapTrack
{
    EmptyDoll,       // Track 1: "The Empty Doll" (Gateway Phase)
    ObedientPuppet,  // Track 2: "The Obedient Puppet" (Discipline Phase)
    SluttyBlowdoll   // Track 3: "The Slutty Blowdoll" (Lewd Phase)
}

public enum RoadmapStepType
{
    Regular,
    Boss
}

public class RoadmapStepDefinition
{
    public string Id { get; set; } = "";
    public RoadmapTrack Track { get; set; }
    public int StepNumber { get; set; }
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    public string PhotoRequirement { get; set; } = "";
    public RoadmapStepType StepType { get; set; } = RoadmapStepType.Regular;

    public RoadmapStepDefinition() { }

    public RoadmapStepDefinition(string id, RoadmapTrack track, int stepNumber, string title,
        string objective, string photoRequirement, RoadmapStepType stepType = RoadmapStepType.Regular)
    {
        Id = id;
        Track = track;
        StepNumber = stepNumber;
        Title = title;
        Objective = objective;
        PhotoRequirement = photoRequirement;
        StepType = stepType;
    }

    public static RoadmapStepDefinition? GetById(string id)
    {
        return AllSteps.FirstOrDefault(s => s.Id == id);
    }

    public static List<RoadmapStepDefinition> GetStepsForTrack(RoadmapTrack track)
    {
        return AllSteps.Where(s => s.Track == track).OrderBy(s => s.StepNumber).ToList();
    }

    public static string GetNextStepId(string currentStepId)
    {
        var current = GetById(currentStepId);
        if (current == null) return "";

        var trackSteps = GetStepsForTrack(current.Track);
        var currentIndex = trackSteps.FindIndex(s => s.Id == currentStepId);

        if (currentIndex >= 0 && currentIndex < trackSteps.Count - 1)
        {
            return trackSteps[currentIndex + 1].Id;
        }

        return ""; // No next step (track completed)
    }

    public static string GetFirstStepId(RoadmapTrack track)
    {
        return track switch
        {
            RoadmapTrack.EmptyDoll => "t1_step1",
            RoadmapTrack.ObedientPuppet => "t2_step1",
            RoadmapTrack.SluttyBlowdoll => "t3_step1",
            _ => ""
        };
    }

    public static string GetBossStepId(RoadmapTrack track)
    {
        return track switch
        {
            RoadmapTrack.EmptyDoll => "t1_boss",
            RoadmapTrack.ObedientPuppet => "t2_boss",
            RoadmapTrack.SluttyBlowdoll => "t3_boss",
            _ => ""
        };
    }

    /// <summary>
    /// All roadmap step definitions across all tracks
    /// </summary>
    public static readonly List<RoadmapStepDefinition> AllSteps = new()
    {
        // ============================================
        // TRACK 1: The Empty Doll (Gateway Phase)
        // Focus: Self-Care, Grooming, Aesthetics, Mild Hypnosis
        // ============================================
        new("t1_step1", RoadmapTrack.EmptyDoll, 1, "The Blank Slate",
            "Perform a full exfoliation and moisturizing routine (face & body).",
            "Photo: Your skincare products arranged neatly on the counter."),

        new("t1_step2", RoadmapTrack.EmptyDoll, 2, "Smooth Touch",
            "Shave legs (or chest) for the first time (or maintain smooth).",
            "Photo: A close-up of smooth skin (Leg or Arm)."),

        new("t1_step3", RoadmapTrack.EmptyDoll, 3, "Pink Accent",
            "Wear one hidden pink item (socks/underwear) for a full day.",
            "Photo: The pink item (not worn, or worn if comfortable)."),

        new("t1_step4", RoadmapTrack.EmptyDoll, 4, "Soft Focus",
            "Complete a low intensity hypno session.",
            "Photo: A 'POV' shot of your screen running the session."),

        new("t1_step5", RoadmapTrack.EmptyDoll, 5, "Glossy Lips",
            "Apply clear lip balm or gloss. Keep it on for 1 hour.",
            "Photo: Close up of lips (or just the gloss tube)."),

        new("t1_step6", RoadmapTrack.EmptyDoll, 6, "Doll Posture",
            "Sit in the 'Doll Pose' (knees together, back straight, hands on lap) for 15 mins.",
            "Photo: Your hands resting obediently on your lap."),

        new("t1_boss", RoadmapTrack.EmptyDoll, 7, "The First Shedding",
            "BOSS: Shave all body hair (below neck) + Moisturize.",
            "Photo: Full body (neck down) mirror selfie showing smooth skin.",
            RoadmapStepType.Boss),

        // ============================================
        // TRACK 2: The Obedient Puppet (Discipline Phase)
        // Focus: Degradation, Boredom, Makeup, Uniforms
        // Unlock: Track 1 Boss Photo Uploaded
        // ============================================
        new("t2_step1", RoadmapTrack.ObedientPuppet, 1, "Repetition Protocol",
            "Write 'I am a blank puppet' 100 times on paper.",
            "Photo: The handwritten page."),

        new("t2_step2", RoadmapTrack.ObedientPuppet, 2, "Stare Drill",
            "Stare at a wall (or static noise video) for 30 minutes. No moving.",
            "Photo: A selfie of your 'bored/blank' face immediately after."),

        new("t2_step3", RoadmapTrack.ObedientPuppet, 3, "Painted Face",
            "Apply foundation, blush, and lipstick. It doesn't have to be perfect.",
            "Photo: Face selfie (or mask + makeup)."),

        new("t2_step4", RoadmapTrack.ObedientPuppet, 4, "Lash Out",
            "Apply false eyelashes (or heavy mascara). Blink slowly for 5 mins.",
            "Photo: Close up of the eyes/lashes."),

        new("t2_step5", RoadmapTrack.ObedientPuppet, 5, "Uniform Fitting",
            "Wear a specific 'Uniform' (Maid/School/Nurse) for 1 hour indoors.",
            "Photo: Mirror selfie in the outfit."),

        new("t2_step6", RoadmapTrack.ObedientPuppet, 6, "Public Secret",
            "Wear panties/lingerie under normal clothes and go for a walk.",
            "Photo: A picture of your feet on the pavement (context implies the secret)."),

        new("t2_boss", RoadmapTrack.ObedientPuppet, 7, "The Inspection",
            "BOSS: Full Shave + Full Makeup + Full Uniform + Complete a session (30 mins).",
            "Photo: Full body 'Inspection Pose' (standing straight, arms out).",
            RoadmapStepType.Boss),

        // ============================================
        // TRACK 3: The Slutty Blowdoll (Lewd Phase)
        // Focus: Sexualization, Toys/Haptics, Deep Hypnosis
        // Unlock: Track 2 Boss Photo Uploaded
        // Final step awards "Certified Blowdoll" badge
        // ============================================
        new("t3_step1", RoadmapTrack.SluttyBlowdoll, 1, "Port Calibration",
            "Sync a toy to the CCP. Run a 'Haptic Ramp' test (10 mins).",
            "Photo: The toy next to the screen running the app."),

        new("t3_step2", RoadmapTrack.SluttyBlowdoll, 2, "Oral Fixation",
            "Practice 'Deep Throat' technique on an object/toy for 10 mins.",
            "Photo: The object used (or action shot if user desires)."),

        new("t3_step3", RoadmapTrack.SluttyBlowdoll, 3, "Ahegao Training",
            "Hold the 'Ahegao' (tongue out, eyes up) face for 2 mins straight.",
            "Photo: The Ahegao face."),

        new("t3_step4", RoadmapTrack.SluttyBlowdoll, 4, "Glitch & Goon",
            "Watch a hypno video with Auto-Haptics enabled.",
            "Photo: A 'messy' after-session selfie (sweaty/disheveled)."),

        new("t3_step5", RoadmapTrack.SluttyBlowdoll, 5, "Double Trouble",
            "Use two forms of stimulation (e.g., Anal plug + Magic Wand) simultaneously.",
            "Photo: The layout of the toys used."),

        new("t3_step6", RoadmapTrack.SluttyBlowdoll, 6, "Exhibitionist",
            "Take a lewd photo in a 'semi-public' place (e.g., your car, backyard, or open window).",
            "Photo: The lewd photo."),

        new("t3_boss", RoadmapTrack.SluttyBlowdoll, 7, "The Creation",
            "FINAL BOSS: Full 'Bimbo' Transformation (Wig, Heels, Makeup, Lingerie) + High Intensity Haptic Session.",
            "Photo: The 'Final Form' portrait. This unlocks the 'Certified Blowdoll' badge.",
            RoadmapStepType.Boss)
    };
}

public class RoadmapTrackDefinition
{
    public RoadmapTrack Track { get; set; }
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Description { get; set; } = "";
    public string AccentColor { get; set; } = "#FF69B4";

    public RoadmapTrackDefinition() { }

    public RoadmapTrackDefinition(RoadmapTrack track, string name, string subtitle, string description, string accentColor)
    {
        Track = track;
        Name = name;
        Subtitle = subtitle;
        Description = description;
        AccentColor = accentColor;
    }

    public static RoadmapTrackDefinition? GetByTrack(RoadmapTrack track)
    {
        return AllTracks.FirstOrDefault(t => t.Track == track);
    }

    /// <summary>
    /// All track definitions
    /// </summary>
    public static readonly List<RoadmapTrackDefinition> AllTracks = new()
    {
        new(RoadmapTrack.EmptyDoll,
            "The Empty Doll",
            "Gateway Phase",
            "Self-Care, Grooming, Aesthetics, Mild Hypnosis",
            "#FF69B4"), // Pink

        new(RoadmapTrack.ObedientPuppet,
            "The Obedient Puppet",
            "Discipline Phase",
            "Degradation, Boredom, Makeup, Uniforms",
            "#9370DB"), // Purple

        new(RoadmapTrack.SluttyBlowdoll,
            "The Slutty Blowdoll",
            "Lewd Phase",
            "Sexualization, Toys, Deep Hypnosis",
            "#FF1493") // Deep Pink
    };
}
