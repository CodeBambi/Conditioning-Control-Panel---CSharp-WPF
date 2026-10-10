using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The DtRH page's <c>bark</c> message -> a bark trigger and its context: the pure half of WPF
/// <c>DtrhHostService.RouteBark</c> (Services/Chaos/DtrhHostService.cs:749) folded together with
/// the <c>BarkService.NotifyChaos*</c> hooks it calls (Services/Companion/BarkService.cs:488-591).
/// The trigger names and context keys are the ones the mods' bark rules match on, so they are
/// copied letter for letter. Numbers go in as doubles, as BarkService sets them.
///
/// <para>The head raises the result through <see cref="CoreBark.Raise"/>. A recorded line plays or
/// nothing does: this never speaks by itself.</para>
/// </summary>
public static class DtrhBarkRouter
{
    public sealed record Bark(string Trigger, IReadOnlyDictionary<string, object> Values);

    /// <summary>Null for an event that has no voice (<c>effect-fired</c> is haptics only) or that
    /// the host does not know.</summary>
    public static Bark? Route(JObject o)
    {
        string S(string k, string d = "") => (string?)o[k] ?? d;
        double I(string k) => (int?)o[k] ?? 0;
        double D(string k) => (double?)o[k] ?? 0;
        static Bark B(string trigger, params (string Key, object Value)[] values)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (k, v) in values) d[k] = v;
            return new Bark(trigger, d);
        }

        switch ((string?)o["event"])
        {
            case "ending-soon": return B("ChaosEndingSoon");
            case "wave-cleared": return B("ChaosWaveCleared", ("wave", I("wave")));
            case "wave-escalated": return B("ChaosWaveEscalated", ("wave", I("wave")));
            case "act-changed": return B("ChaosActChanged", ("act", I("act")), ("wave", I("wave")));
            case "benign-popped":
                return B("ChaosBenignPopped", ("variant_id", S("variant")), ("payload", S("payload")), ("combo", I("combo")));
            case "defused":
                return B("ChaosBubbleDefused", ("combo", I("combo")), ("payload", S("variant")), ("difficulty", S("difficulty")));
            case "detonated":
                return B("ChaosBubbleDetonated", ("payload", S("variant")), ("strength", D("strength")),
                    ("shield_absorbed", false), ("run_detonations", D("runDetonations")), ("combo", I("combo")),
                    ("difficulty", S("difficulty")));
            case "detonated-absorbed":
                return B("ChaosBubbleDetonatedAbsorbed", ("payload", S("variant")), ("strength", D("strength")),
                    ("shield_absorbed", true), ("run_detonations", D("runDetonations")), ("combo", I("combo")),
                    ("difficulty", S("difficulty")), ("shields_left", I("shields")));
            case "darter-caught":
                return B("ChaosDarterCaught", ("points", D("points")), ("combo", I("combo")), ("quick", (bool?)o["quick"] ?? false));
            case "freeze-caught": return B("ChaosFreezeCaught", ("points", D("points")), ("combo", I("combo")));
            case "combo-milestone": return B("ChaosComboMilestone", ("combo", I("combo")), ("difficulty", S("difficulty")));
            case "combo-big": return B("ChaosComboBig", ("combo", I("combo")), ("threshold", D("threshold")));
            case "boon-picked": return B("ChaosBoonPicked", ("boon", S("name")));
            case "curse-picked":
                return B("ChaosCursePicked", ("boon", S("name")), ("rarity", S("rarity")), ("run_mult_bonus", D("mult")));
            case "boon-skipped": return B("ChaosBoonSkipped", ("shields_now", I("shields")));
            case "draft-autopick": return B("ChaosDraftAutopick");
            case "focus-low": return B("ChaosFocusLow");
            case "defuse-first": return B("ChaosDefuseFirst");
            case "defuse-nofocus": return B("ChaosDefuseNoFocus");
            case "defuse-release": return B("ChaosDefuseRelease");
            case "click-detonate": return B("ChaosClickDetonate");
            case "tease-debut": return B("ChaosTeaseDebut");
            case "tease-clicked": return B("ChaosTeaseClicked");
            case "tease-denied": return B("ChaosTeaseDenied", ("denied_count", I("count")));
            case "tease-denied-streak": return B("ChaosTeaseDeniedStreak", ("denied_count", I("count")));
            case "gold-first": return B("ChaosGoldFirst");
            case "dollhouse-first-open": return B("ChaosDollhouseFirstOpen");
            case "reveal-flash": return B("ChaosRevealFlash", ("element", S("id")));
            case "lesson-complete": return B("ChaosLessonComplete", ("lesson_id", S("id")));
            case "duo-demo": return B("ChaosDuoDemo");
            // Tunnel pickups reuse the rabbit-catch voice (WPF :793).
            case "rabbit-caught": return B("ChaosDarterCaught", ("points", I("gold")), ("combo", 0d), ("quick", true));
            // Crafting reuses the first-time voice (WPF :795).
            case "crafted": return B("ChaosFirstTime", ("bonus_id", S("id")));
            default: return null;
        }
    }

    /// <summary>WPF NotifyChaosRunStarted (BarkService.cs:488).</summary>
    public static Bark RunStarted(string difficulty) =>
        new("ChaosRunStarted", new Dictionary<string, object> { ["difficulty"] = difficulty });

    /// <summary>WPF NotifyChaosRunCompleted (BarkService.cs:537).</summary>
    public static Bark RunCompleted(int xp, string difficulty) =>
        new("ChaosRunCompleted", new Dictionary<string, object>
        {
            ["xp"] = (double)xp,
            ["difficulty"] = difficulty,
            ["runs_completed"] = (double)(ChaosMeta.State?.RunsCompleted ?? 0),
            ["rank"] = ChaosRanks.NameLower(ChaosMeta.RankIndex),
        });
}
