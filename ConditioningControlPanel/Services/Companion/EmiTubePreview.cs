namespace ConditioningControlPanel.Services.Companion;

/// <summary>House-only preview identity. A mod can still own its own set 8.</summary>
public static class EmiTubePreview
{
    public const int Set = 8;
    public static bool Available => CompanionExperience.IsV2Enabled && App.Mods?.IsCCPDefault == true;
    public static bool IsEmi(int set) => Available && set == Set;
    public static int InitialSet(int savedSet, int companionId, bool available, bool choiceMade = false)
        => available && !choiceMade && companionId == 0 && savedSet is >= 0 and <= 3 ? Set : savedSet;

    internal static int RestoreChoice(int savedSet)
    {
        var settings = App.Settings?.Current;
        if (!Available || settings == null) return savedSet;
        var selected = InitialSet(savedSet, settings.ActiveCompanionId, true, settings.CompanionEmiPreviewChoiceMade);
        var isEmi = selected == Set && settings.ActiveCompanionId == 0;
        var changed = EmiPersonality.FenceOldVoice(settings, isEmi, isEmi);
        if (!settings.CompanionEmiPreviewChoiceMade)
        {
            settings.SelectedAvatarSet = selected;
            settings.CompanionEmiPreviewChoiceMade = true;
            changed = true;
        }
        if (changed) App.Settings?.Save();
        return selected;
    }
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
