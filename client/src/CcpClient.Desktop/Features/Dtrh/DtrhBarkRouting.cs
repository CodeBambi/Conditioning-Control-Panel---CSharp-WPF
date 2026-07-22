using System.Text.Json;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The DTRH page `bark {event, ...}` → bark-trigger routing table (SP-032 q2): the full
/// WPF `RouteBark` switch (DtrhHostService.cs:618-650) mapped onto the Notify* triggers
/// (BarkService.cs:262-365). Fill values carry the WPF fill KEYS (condition evaluation and
/// {key} substitution read them); JSON field names follow the payload's send sites.
/// Unrouted events are typed + logged (WPF default branch), never thrown, never silent.
/// Content-free: this file routes presence+shape only — bark TEXT never crosses here.
/// </summary>
public static class DtrhBarkRouting
{
    private sealed record Entry(string Trigger, params (string JsonKey, string FillKey)[] Fills);

    private static readonly Dictionary<string, Entry> Table = new(StringComparer.Ordinal)
    {
        // event → (trigger, jsonField → fillKey…) — WPF RouteBark order preserved.
        ["ending-soon"] = new("ChaosEndingSoon"),
        ["wave-cleared"] = new("ChaosWaveCleared", ("wave", "wave")),
        ["wave-escalated"] = new("ChaosWaveEscalated", ("wave", "wave")),
        ["act-changed"] = new("ChaosActChanged", ("act", "act"), ("wave", "wave")),
        // WPF NotifyChaosBenignPopped(variantId, payload, combo) — variant → variant_id.
        ["benign-popped"] = new("ChaosBenignPopped", ("variant", "variant_id"), ("payload", "payload"), ("combo", "combo")),
        ["defused"] = new("ChaosBubbleDefused", ("combo", "combo"), ("variant", "payload"), ("difficulty", "difficulty")),
        ["detonated"] = new("ChaosBubbleDetonated",
            ("variant", "payload"), ("strength", "strength"), ("runDetonations", "run_detonations"),
            ("combo", "combo"), ("difficulty", "difficulty")),
        ["detonated-absorbed"] = new("ChaosBubbleDetonatedAbsorbed",
            ("variant", "payload"), ("strength", "strength"), ("runDetonations", "run_detonations"),
            ("combo", "combo"), ("difficulty", "difficulty"), ("shields", "shields")),
        ["darter-caught"] = new("ChaosDarterCaught", ("points", "points"), ("combo", "combo"), ("quick", "quick")),
        ["freeze-caught"] = new("ChaosFreezeCaught", ("points", "points"), ("combo", "combo")),
        ["combo-milestone"] = new("ChaosComboMilestone", ("combo", "combo"), ("difficulty", "difficulty")),
        ["combo-big"] = new("ChaosComboBig", ("combo", "combo"), ("threshold", "threshold")),
        ["boon-picked"] = new("ChaosBoonPicked", ("name", "boon")),
        ["curse-picked"] = new("ChaosCursePicked", ("name", "boon"), ("rarity", "rarity"), ("mult", "run_mult_bonus")),
        ["boon-skipped"] = new("ChaosBoonSkipped", ("shields", "shields_now")),
        ["draft-autopick"] = new("ChaosDraftAutopick"),
        ["focus-low"] = new("ChaosFocusLow"),
        ["defuse-first"] = new("ChaosDefuseFirst"),
        ["defuse-nofocus"] = new("ChaosDefuseNoFocus"),
        ["defuse-release"] = new("ChaosDefuseRelease"),
        ["click-detonate"] = new("ChaosClickDetonate"),
        ["tease-debut"] = new("ChaosTeaseDebut"),
        ["tease-clicked"] = new("ChaosTeaseClicked"),
        ["tease-denied"] = new("ChaosTeaseDenied", ("count", "denied_count")),
        ["tease-denied-streak"] = new("ChaosTeaseDeniedStreak", ("count", "denied_count")),
        ["gold-first"] = new("ChaosGoldFirst"),
        // M5: Warren hub + lessons + happy path.
        ["dollhouse-first-open"] = new("ChaosDollhouseFirstOpen"),
        ["reveal-flash"] = new("ChaosRevealFlash", ("id", "element")),
        ["lesson-complete"] = new("ChaosLessonComplete", ("id", "lesson_id")),
        ["duo-demo"] = new("ChaosDuoDemo"),
        // Wave 2 tunnel pickups reuse the rabbit-catch voice: points=gold, combo=0, quick=true.
        ["rabbit-caught"] = new("ChaosDarterCaught", ("gold", "points")),
        // Crafting (THE BOUDOIR) reuses the first-time voice: bonus_id=id.
        ["crafted"] = new("ChaosFirstTime", ("id", "bonus_id")),
    };

    /// <summary>
    /// Route one bark message to (trigger, fills). False = unrouted event (typed — the
    /// caller logs it, WPF default-branch parity). Constant fills for reused voices
    /// (rabbit-caught → quick=true, combo=0) are applied per the WPF call sites.
    /// </summary>
    public static bool TryRoute(
        DtrhProtocol.DtrhPageMessage.Bark bark,
        out string trigger,
        out IReadOnlyDictionary<string, object?> fills)
    {
        trigger = "";
        fills = new Dictionary<string, object?>();
        if (bark.Event is not { Length: > 0 } eventName
            || !Table.TryGetValue(eventName, out var entry))
        {
            return false;
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (jsonKey, fillKey) in entry.Fills)
        {
            if (bark.Raw.ValueKind == JsonValueKind.Object
                && bark.Raw.TryGetProperty(jsonKey, out var prop))
            {
                values[fillKey] = ToValue(prop);
            }
        }

        // WPF call-site constants for reused voices (RouteBark :643,:647).
        if (eventName == "rabbit-caught")
        {
            values["combo"] = 0.0;
            values["quick"] = true;
        }

        trigger = entry.Trigger;
        fills = values;
        return true;
    }

    private static object? ToValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (double)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
