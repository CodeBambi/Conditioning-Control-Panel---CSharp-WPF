namespace ConditioningControlPanel.Services.Companion;

/// <summary>House-only preview identity. A mod can still own its own set 8.</summary>
public static class EmiTubePreview
{
    public const int Set = 8;
    public static bool Available => CompanionExperience.IsV2Enabled && App.Mods?.IsCCPDefault == true;
    public static bool IsEmi(int set) => Available && set == Set;
    public static int InitialSet(int savedSet, int companionId, bool available)
        => available && companionId == 0 && savedSet is >= 1 and <= 3 ? Set : savedSet;
    public static bool SuppressesDesk(bool available, int set, bool visible, bool attached)
        => available && set == Set && visible && attached;
    public static int PoseForMood(string? mood)
    {
        foreach (var token in (mood ?? "").ToLowerInvariant().Split(','))
        {
            var pose = token.Trim() switch
            {
                "smug" or "teasing" or "wink" => 2,
                "sad" or "disappointed" => 3,
                "affectionate" or "warm" => 4,
                "surprised" or "shocked" => 5,
                "happy" or "excited" or "praise" => 6,
                "sleepy" or "dreamy" => 7,
                "dizzy" or "overwhelmed" => 8,
                _ => 0
            };
            if (pose != 0) return pose;
        }
        return 1;
    }
}
