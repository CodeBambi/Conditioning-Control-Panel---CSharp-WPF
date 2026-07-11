namespace ConditioningControlPanel.Core.Services.AvatarTube;

/// <summary>
/// Pure fold math that mirrors WPF <c>AvatarTubeWindow.CirceEmotes.cs:388-393</c>: when a Circe
/// emote set carries a layout delta (<c>emotes.json</c> "layout"), its scale multiplier MULTIPLIES
/// the mod's global TubeLayout avatar scale, and its offsets ADD (X) / SUBTRACT (Y) to the mod's
/// global TubeLayout offsets. Avalonia has no <c>LayoutTransform</c>, so the same effective values
/// feed <c>ApplyAvatarTransform</c> (scale, as a bottom-center RenderTransform on AvatarBorder — the
/// parent of all three avatar images) and <c>ApplyTubeLayoutOffsets</c> (AvatarBorder bottom margin).
/// This keeps the emote frames at the SAME scale/position the neutral pose uses — fixing the
/// "avatar enlarged + shifted up during Circe emotes" bug (Engram obs #6, topic
/// avatartube-rootcause-2026-07-11).
/// </summary>
public static class AvatarTubeLayoutFold
{
    /// <summary>
    /// Effective avatar scale. WPF CirceEmotes.cs:391:
    /// <c>(App.Mods.GetAvatarScale()) * (EmoteLayoutActive ? _emoteScaleMul : 1.0)</c>.
    /// A <paramref name="modScale"/> of 1.0 with an inactive layout yields 1.0 (pure pose base).
    /// </summary>
    public static double Scale(double modScale, bool active, double emoteScaleMul)
        => modScale * (active ? emoteScaleMul : 1.0);

    /// <summary>
    /// Effective horizontal offset. WPF CirceEmotes.cs:392:
    /// <c>modOffX + (EmoteLayoutActive ? _emoteOffX : 0)</c>. offsetX is "+= right".
    /// </summary>
    public static int OffsetX(int modOffX, bool active, int emoteOffX)
        => modOffX + (active ? emoteOffX : 0);

    /// <summary>
    /// Effective vertical offset. WPF CirceEmotes.cs:393:
    /// <c>modOffY - (EmoteLayoutActive ? _emoteOffY : 0)</c> — the delta SUBTRACTS.
    /// The emote layout stores Y as "+= down" (so <c>offsetY:-30</c> = up 30px), but the
    /// AvatarBorder margin math is "+bottom = up", so the delta must subtract to land in the same
    /// on-screen direction. Concretely: <c>offsetY:-30</c> (bambisleep) folds to
    /// <c>modOffY - (-30) = modOffY + 30</c>, applied to the bottom margin <c>(210 + dy)</c> =>
    /// larger bottom margin => figure moves UP 30px on screen — matching the layout's intent and
    /// the prior center-origin TranslateTransform(0, -30) screen direction (Avalonia +Y = down).
    /// </summary>
    public static int OffsetY(int modOffY, bool active, int emoteOffY)
        => modOffY - (active ? emoteOffY : 0);
}
