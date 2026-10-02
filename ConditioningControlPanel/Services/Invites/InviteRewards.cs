using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Invites;

/// <summary>One step of the ladder: how many converted friends, the badge, and the wardrobe item
/// that badge unlocks (gated in Resources/cosmetics/registry.json, mod bucket "invites").</summary>
public sealed record InviteRung(int Converted, string AchievementId, string WardrobeItemId);

/// <summary>
/// The inviter's reward ladder. Rewards count CONVERTED friends (a first paid month), never
/// redemptions, so handing out codes alone earns nothing. Each rung is an ordinary visible
/// achievement, so it can be worn as a title and pinned like any other, and it gates one wardrobe
/// item through the registry's existing <c>achievement:</c> unlock form.
/// </summary>
public static class InviteRewards
{
    public static readonly IReadOnlyList<InviteRung> Ladder = new[]
    {
        new InviteRung(1, "invite_first", "invite_pink_envelope"),
        new InviteRung(3, "invite_hostess", "invite_hostess_headset"),
        new InviteRung(5, "invite_pied_piper", "invite_recruiter_rose"),
        new InviteRung(10, "invite_recruiter_chief", "invite_velvet_crown"),
    };

    /// <summary>Badges the count has earned that are not unlocked yet, lowest rung first.</summary>
    public static IReadOnlyList<string> Due(int convertedTotal, ICollection<string>? unlocked)
        => Ladder.Where(r => convertedTotal >= r.Converted && (unlocked == null || !unlocked.Contains(r.AchievementId)))
                 .Select(r => r.AchievementId)
                 .ToList();

    /// <summary>The next rung still to reach, or null at the top of the ladder.</summary>
    public static InviteRung? Next(int convertedTotal)
        => Ladder.FirstOrDefault(r => convertedTotal < r.Converted);

    /// <summary>
    /// Unlock whatever a fresh <c>mine</c> snapshot has earned. Idempotent (TryUnlock is), so it is
    /// safe on every read. Returns how many badges were newly unlocked.
    /// </summary>
    public static int Apply(InviteSnapshot? snapshot)
    {
        if (snapshot == null) return 0;
        try
        {
            var achievements = App.Achievements;
            if (achievements == null) return 0;
            var unlocked = achievements.Progress?.UnlockedAchievements;
            var count = 0;
            foreach (var id in Due(snapshot.ConvertedTotal, unlocked))
                if (achievements.TryUnlock(id)) count++;
            if (count > 0)
                App.Logger?.Information("[Invites] {Count} invite reward(s) unlocked at {Converted} converted", count, snapshot.ConvertedTotal);
            return count;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "[Invites] reward apply failed");
            return 0;
        }
    }
}
