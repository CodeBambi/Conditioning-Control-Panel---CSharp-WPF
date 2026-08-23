using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// Wave 2 batch A - DENSITY. The first live Eerie run was judged "not dense, not impressive"
/// (POSSESSION.md, Wave 2): with only a handful of possessable targets and a slow deck, minutes went
/// by between tics and two big effects got named in nine minutes. Batch A answers the second half of
/// that - the deck itself now has more to say, and more of it happens where the user is looking:
///
///   R0 Settle    slidercreep   the slider you just set creeps back a notch (a ghost thumb, never the value)
///   R1 Drift     ghostcursor   a second cursor visits a button, presses it, drifts off (never clicks)
///                rewrite       a label briefly says something else, from the mod-voiced pools
///   R2 Melt      relabel       Start / Stop says "Stay" for two and a half seconds
///                togglelie     the toggle looks flipped, then snaps back (IsChecked never touched)
///                glyphrot      one word rots into box glyphs, holds, then heals
///
/// Two existing effects were upgraded in place rather than duplicated: <see cref="DodgeEffect"/> now
/// dodges what the cursor is HEADING FOR (and may dodge the auto-tagged title-bar X), and
/// <see cref="SwapEffect"/> pairs buttons by what the eye sees (near + similar size, Start/Stop
/// preferred) instead of demanding a shared parent, which almost never matched.
///
/// <para>Weights are read at the effect's MinRung and scaled up by the director per rung above it, so
/// they are a mix ratio, not a schedule: rewrite is the workhorse (4), the two cursor / button set
/// pieces sit at 3, and the quiet ones (slidercreep, togglelie, glyphrot) at 2 so a rung never turns
/// into a slideshow of the same trick.</para>
/// </summary>
public static partial class PossessionEffectCatalog
{
    static partial void AddWaveA(List<IPossessionEffect> list)
    {
        // R0 Settle
        list.Add(new SliderCreepEffect());   // weight 2

        // R1 Drift
        list.Add(new GhostCursorEffect());   // weight 3
        list.Add(new RewriteEffect());       // weight 4

        // R2 Melt
        list.Add(new RelabelEffect());       // weight 3
        list.Add(new ToggleLieEffect());     // weight 2
        list.Add(new GlyphRotEffect());      // weight 2
    }
}
