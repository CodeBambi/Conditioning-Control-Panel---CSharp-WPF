using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// Wave-2 registration hook for the COMPANION batch (POSSESSION.md - "Companion wave"). One partial
/// per author so nine parallel builders never race on the catalog file itself.
///
/// <para>Only one effect lands here. The rest of the companion wave is not deck material: the audio
/// tics ride the director's events (PossessionAudio), the remembered charge happens on a launch where
/// no lockdown is running at all (PossessionRemember), the note in the empty tube belongs to the
/// warden verb that emptied it (Warden.LeaveAsync), and the R3 retitle is a rung change on an effect
/// the core catalog already registers.</para>
/// </summary>
public static partial class PossessionEffectCatalog
{
    static partial void AddWaveC(List<IPossessionEffect> list)
    {
        // R4, Full Doki, photosafe-skipped (UsesFlicker). Weight 2 - the same as dokidialog, so at
        // "It knows" the room, the warden and the title all get a turn instead of one of them
        // monopolising the rung.
        list.Add(new GlitchPortraitEffect());
    }
}
