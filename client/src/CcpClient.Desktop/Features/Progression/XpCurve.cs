namespace CcpClient.Desktop.Features.Progression;

/// <summary>
/// THE LEVEL CURVE, and nothing else. Pure, static, clock-free and store-free, so the arithmetic
/// can be checked without a file on disk — which is also how upstream keeps it
/// (<c>Services/Progression/ProgressionService.cs:242-248</c>: <i>"Pure and static so the recurve
/// can be tested"</i>).
///
/// <para><b>WHICH CURVE, AND WHY ONLY ONE.</b> Upstream carries two: v1
/// (<c>ProgressionService.cs:264-335</c>, <c>CurveEpochLegacy</c>) and the Descent recurve v2
/// (<c>:360-393</c>, <c>CurveEpochDescent</c>). Which one an account is on is read from
/// <c>AppSettings.DescentEpoch</c> (<c>:236-237</c>), and the ONLY thing in the shipping product
/// that ever writes that field is the migration ceremony's submit
/// (<c>Services/Descent/DescentMigrationService.cs:361</c>). Upstream says so itself at
/// <c>:227-229</c> — <i>"every account that has not been through the Descent migration ceremony —
/// which today is all of them"</i>. This port has no migration ceremony, so no account here can be
/// on v2, and a v2 implementation would be a curve nothing could ever select. v2 is therefore a
/// NAMED LIMIT rather than dead code: the day a Descent migration row lands, it brings its own
/// curve, its cycle bonus and its relevel with it.</para>
///
/// <para><b>THE MULTIPLIERS UPSTREAM APPLIES BEFORE BANKING ARE ALL 1.0 HERE, AND EACH ONE IS
/// UPSTREAM'S OWN FALLBACK RATHER THAN AN INVENTION.</b> <c>AddXP</c> scales the amount by
/// <c>App.SkillTree?.GetTotalXpMultiplier() ?? 1.0</c> and by
/// <c>DescentMigration.ActiveCycleXpBonus</c> (<c>:75-77</c>). There is no skill tree in this build
/// — the <c>?? 1.0</c> IS the parity outcome, the same reading
/// <c>Features/Dtrh/DtrhMeta.cs</c> already takes for its own payout — and the cycle bonus reads
/// <c>DescentCycleXpBonus</c>, which is 1.0 for anyone who has not been through the ceremony
/// (<c>DescentMigration.cs:110-118</c>, and <c>ProgressionService.cs:72-75</c>:
/// <i>"arithmetically invisible until a Cycle mark exists"</i>). So the port's granted amount is
/// upstream's granted amount, exactly, for every account that exists in either product today.</para>
/// </summary>
public static class XpCurve
{
    /// <summary>Where a fresh account stands (<c>Models/AppSettings.cs:237</c>,
    /// <c>private int _playerLevel = 1;</c>).</summary>
    public const int FirstLevel = 1;

    /// <summary>
    /// The ceiling any level may be derived to — <c>ProgressionService.MaxDerivableLevel</c>
    /// (<c>:27</c>), whose own remark (<c>:22-26</c>) is the reason it exists: <i>"this can only
    /// ever be hit by garbage input — and a derive loop that trusts its input is a hang"</i>.
    ///
    /// <para><b>APPLYING IT TO THE SPEND LOOP IS A DELIBERATE DIVERGENCE.</b> Upstream's
    /// <c>SpendXPOnLevels</c> (<c>:212-259</c>) has no ceiling at all; only its derive loop
    /// (<c>:443</c>) does. That is safe upstream because every one of its thirty award sites is a
    /// literal or a bounded local computation. In THIS port the three wired sources are payouts a
    /// hosted WEB PAGE reports over a bridge (see <see cref="ProgressionLedger"/>), and one of them
    /// — the descent's <c>score</c> — is an unbounded double the page chooses. That is precisely
    /// the "garbage input" upstream's own constant is named for, so the constant is applied where
    /// this port's input can actually be garbage. XP past the ceiling is NOT destroyed: it stays
    /// banked as <see cref="LevelSpend.XpIntoLevel"/>, exactly as upstream's derive loop leaves its
    /// remainder (<c>:451</c>).</para>
    /// </summary>
    public const int MaxLevel = 500;

    /// <summary>
    /// XP to clear <paramref name="level"/> — upstream's <c>XpForLevelV1</c>
    /// (<c>ProgressionService.cs:300-335</c>), literal for literal.
    ///
    /// <para>Its five bands, in upstream's own words (<c>:254-262</c>): L1-80 linear 800 → 2500;
    /// L81-100 linear 2500 → 4000; L101-125 linear 4000 → 6000; L126-150 linear 6000 → 10000;
    /// L150+ 3% compound growth from 10000.</para>
    ///
    /// <para><b>The rounding mode is upstream's DEFAULT and stays that way.</b> v1 calls
    /// <c>Math.Round(x)</c> — banker's, to-even — while v2 explicitly passes
    /// <c>MidpointRounding.AwayFromZero</c> because a server rounds the same costs
    /// (<c>:354-358</c>). No v1 band can land a cost on a midpoint (79 does not divide 3400, and
    /// the other three linear steps are whole numbers), so the mode is not observable here — which
    /// is exactly why it must not be "tidied" to match v2 by someone who assumes it is.</para>
    /// </summary>
    public static double XpForLevel(int level)
    {
        if (level <= 80)
        {
            return Math.Round(800 + (level - 1) * (1700.0 / 79));            // :301-305
        }

        if (level <= 100)
        {
            return Math.Round(2500 + (level - 80) * (1500.0 / 20));          // :307-312
        }

        if (level <= 125)
        {
            return Math.Round(4000 + (level - 100) * (2000.0 / 25));         // :314-319
        }

        if (level <= 150)
        {
            return Math.Round(6000 + (level - 125) * (4000.0 / 25));         // :321-326
        }

        return Math.Round(10000 * Math.Pow(1.03, level - 150));              // :328-334
    }

    /// <summary>Cumulative XP a subject must have earned to STAND at <paramref name="level"/>
    /// — upstream's <c>CumulativeXpToReachLevel</c> (<c>:413-421</c>): iterative, uncached, and
    /// clamped to <see cref="MaxLevel"/> at the door for the same reason.</summary>
    public static double CumulativeXpToReachLevel(int level)
    {
        if (level <= FirstLevel)
        {
            return 0;                                                        // :415
        }

        if (level > MaxLevel)
        {
            level = MaxLevel;                                                // :416
        }

        double total = 0;
        for (var l = FirstLevel; l < level; l++)
        {
            total += XpForLevel(l);                                          // :419
        }

        return total;
    }

    /// <summary>What a bank of XP bought. <paramref name="XpIntoLevel"/> is the remainder that did
    /// NOT buy a level — upstream's <c>PlayerXP</c> is progress INTO the current level, never a
    /// lifetime total (<c>DescentMigrationService.cs:359-362</c> states the pair explicitly).
    /// <paramref name="AtCeiling"/> reports that <see cref="MaxLevel"/> stopped the loop, so a
    /// caller can say so rather than silently swallowing the rest.</summary>
    public readonly record struct LevelSpend(int Level, double XpIntoLevel, int LevelsGained, bool AtCeiling);

    /// <summary>
    /// Upstream's <c>SpendXPOnLevels</c> (<c>:212-223</c>), reduced to its arithmetic: drain the
    /// bank into levels for as long as the next one is affordable.
    ///
    /// <para>The comparison is <c>&gt;=</c> (<c>:215</c>: <c>while (settings.PlayerXP &gt;=
    /// xpNeeded)</c>), so a bank exactly equal to the cost buys the level. The
    /// <c>cost &lt;= 0</c> break is upstream's own guard-not-a-case (<c>:446</c>).</para>
    ///
    /// <para>Everything ELSE <c>SpendXPOnLevels</c> does per level is the level-up CEREMONY and is
    /// deliberately absent from this port: haptics (<c>:228</c>), level achievements (<c>:231</c>),
    /// skill points (<c>:234</c>), Discord presence and the milestone webhook (<c>:240-248</c>) and
    /// the leaderboard sync (<c>:251</c>). None of those subsystems exist here, and this method
    /// raises nothing at all — it returns a value.</para>
    /// </summary>
    public static LevelSpend Spend(int level, double xpIntoLevel)
    {
        var current = Math.Clamp(level, FirstLevel, MaxLevel);
        var bank = double.IsFinite(xpIntoLevel) && xpIntoLevel > 0 ? xpIntoLevel : 0;
        var gained = 0;

        while (current < MaxLevel)
        {
            var cost = XpForLevel(current);                                  // :214, :221
            if (cost <= 0 || bank < cost)
            {
                break;                                                       // :215, :446
            }

            bank -= cost;                                                    // :217
            current++;                                                       // :218
            gained++;
        }

        return new LevelSpend(current, bank, gained, current >= MaxLevel);
    }
}

/// <summary>
/// THE FOUR LEVEL GATES — and the thing about them that the source says and a summary does not.
///
/// <para><b>UPSTREAM DELETED FEATURE LEVEL GATING. It is not off in this port; it is off in the
/// shipping product.</b> Every gate in the app funnels through
/// <c>AppSettings.IsLevelUnlocked(int)</c> (<c>Models/AppSettings.cs:5439</c>), whose entire body
/// is <c>return true;</c> under the comment <i>"Feature level gating has been removed — every
/// feature is available from level 1. XP, levels, quests, achievements, and the skill tree still
/// exist; they just no longer gate any features. Method stub preserved so existing call sites keep
/// compiling."</i> (<c>:5434-5438</c>). Its four remaining call sites
/// (<c>Services/LockCard/BrainDrainService.cs:208</c>,
/// <c>Services/Notifications/OverlayService.cs:415,622,3313</c>) therefore always pass, and one
/// more is commented out (<c>Services/Session/SessionEngine.cs:496</c>).</para>
///
/// <para><b>THREE OF THE FOUR NEVER HAD CODE AT ALL.</b> Only Brain Drain's requirement is written
/// as a call. Bubble Count's, the Lock Card's and Mind Wipe's live purely as prose in a doc comment
/// — <c>Services/BubbleCountService.cs:19</c> (<i>"Unlocks at Level 50"</i>) and
/// <c>Services/LockCard/MindWipeService.cs:16</c> (<i>"Unlockable at level 75"</i>); the Lock Card's
/// 35 has no surviving statement of any kind in the shipping tree, and is carried here from the
/// port's own record (<c>Effects/LockCardEffect.cs:87</c>) rather than from upstream code.</para>
///
/// <para><b>SO THIS PORT DOES NOT REFUSE EITHER, AND THAT IS THE PARITY OUTCOME.</b> Making these
/// gates bite would take Brain Drain, Bubble Count, the Lock Card and Mind Wipe away from every
/// user below level 70/50/35/75 — features the shipping app hands them at level 1. That is a
/// regression dressed as a port. What CHANGED with this row is where the answer comes from: it used
/// to be silence (there was no level to ask about), and it is now a cited decision with a test
/// behind it.</para>
/// </summary>
public static class LevelUnlocks
{
    /// <summary>Brain Drain (<c>BrainDrainService.cs:208-211</c> — the one requirement still
    /// written as code).</summary>
    public const int BrainDrain = 70;

    /// <summary>Bubble Count (<c>BubbleCountService.cs:19</c>, prose only).</summary>
    public const int BubbleCount = 50;

    /// <summary>The Lock Card (<c>Effects/LockCardEffect.cs:87</c> — the port's own record; no
    /// statement of it survives in the shipping tree).</summary>
    public const int LockCard = 35;

    /// <summary>Mind Wipe (<c>MindWipeService.cs:16</c>, prose only).</summary>
    public const int MindWipe = 75;

    /// <summary>
    /// <c>AppSettings.IsLevelUnlocked</c> (<c>Models/AppSettings.cs:5439-5442</c>), ported body and
    /// all. <paramref name="requiredLevel"/> is read by nothing, upstream or here — that is the
    /// behaviour, not an oversight, and the parameter is kept so the requirement stays legible at
    /// the call site exactly as upstream kept it.
    /// </summary>
    public static bool IsUnlocked(int requiredLevel) => true;

    /// <summary>The sentence a surface can print instead of inventing a refusal.</summary>
    public const string GatingRemovedNote =
        "Feature level gating was removed upstream: every feature is available from level 1 "
        + "(AppSettings.cs:5434-5442). The numbers here are the historical requirements, kept as a "
        + "record rather than enforced.";
}
