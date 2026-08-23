using System.Text.Json;
using System.Text.Json.Nodes;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// The write half of the settings loop: <c>set-setting {key,value}</c> → validate, CLAMP, store,
/// and hand back the value that is ACTUALLY STORED so the caller can echo it
/// (<c>ArcademyHostService.OnSetSetting</c> <c>:1164-1188</c> → <c>ApplySetting</c>
/// <c>:1192-1319</c>). The host re-clamps every gated field on the way in and the page clamps
/// again on receipt, so neither side alone can widen a ceiling.
///
/// <para><b>Keys are the init projection's own names, flattened</b> (<c>:1157-1162</c>):
/// <c>caps.flashRate</c> or the bare <c>flashRate</c>, <c>audioLevels.fx</c> or the bare
/// <c>fx</c>. Anything unrecognised is a PER-GAME knob and lands in the flat bag; a global is
/// never writable under a per-game name.</para>
///
/// <para><b>Three keys are APP-WIDE upstream and are app-wide here too.</b>
/// <c>effectIntensity</c> (<c>:1214</c>, the one photosensitivity guard), <c>masterVolume</c>
/// (<c>:1220-1222</c>) and <c>remoteMediaRatio</c> (<c>:1225-1227</c>) write
/// <c>AppSettings</c> fields, not Arcademy ones, so they are applied to
/// <see cref="ArcademyAppFacts"/> rather than copied into the Arcademy document — the duplication
/// <c>AppSettings.cs:6531-6537</c> forbids. <b>DIVERGENCE, stated:</b> this build has no app-wide
/// store to persist them into, so the clamped value lives for the session and rides
/// <c>ArcademySession.AppFactsChanged</c>; the day one exists, that hook is where it attaches.</para>
///
/// <para><b>A wrong-KIND value is refused and echoed rather than dropped.</b> Upstream converts
/// through <c>JToken</c> and throws on garbage, which returns from <c>OnSetSetting</c>
/// (<c>:1176-1181</c>) with NO echo — and a row the page marked pending then stays pending
/// forever (<c>arcademy/shell/settings.js:17-21</c>). Here a bool sent to a numeric key leaves the
/// stored value untouched, exactly as upstream, and echoes THAT value, so the page converges on
/// the truth. A JSON <c>null</c> still resolves to the key's fallback, which is upstream's
/// <c>?? fallback</c> and is why a null is not a "leave it alone".</para>
/// </summary>
public static class ArcademySettingsEcho
{
    /// <summary>Effective budget for the flat per-game bag. Deliberately BELOW the document's own
    /// 65536 cap (<c>:1312-1316</c>), because that setter answers an over-long value by throwing
    /// the ENTIRE bag away — a silent total loss this path must never be able to reach.</summary>
    public const int MaxGameSettingsBagChars = 60000;

    /// <summary>Same posture for the keybind blob against the document's 8192 cap
    /// (<c>:1318-1319</c>).</summary>
    public const int MaxKeybindsJsonChars = 7000;

    /// <summary>Upstream's key guard (<c>:1165-1166</c>): trimmed, non-empty, at most 64 chars.
    /// A key outside it is not answered at all.</summary>
    public static bool IsWritableKey(string? key)
    {
        var k = (key ?? "").Trim();
        return k.Length is > 0 and <= 64;
    }

    /// <summary>The outcome of one write: the value to echo (null only when the key was refused
    /// outright, <c>:1189-1190</c>) and the possibly-updated app-wide facts.</summary>
    public sealed record ArcademyWriteResult(object? Echo, ArcademyAppFacts Facts, bool AppFactsChanged);

    /// <summary>Write one setting and report what is stored (<c>ApplySetting</c>, <c>:1192</c>).</summary>
    public static ArcademyWriteResult Apply(
        ArcademySettingsDocument s, ArcademyAppFacts facts, string key, JsonElement? value)
    {
        var trimmed = key.Trim();

        double? Num(double fallback) => value switch
        {
            null => fallback,
            { ValueKind: JsonValueKind.Null } => fallback,
            { ValueKind: JsonValueKind.Number } e => e.TryGetDouble(out var d) && double.IsFinite(d) ? d : fallback,
            _ => null,
        };

        bool? Flag(bool fallback) => value switch
        {
            null => fallback,
            { ValueKind: JsonValueKind.Null } => fallback,
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            _ => null,
        };

        switch (Strip(trimmed, "caps."))
        {
            case "masterIntensity":                                             // :1203
                if (Num(0.7) is { } masterIntensity) s.MasterIntensity = masterIntensity;
                return Unchanged(s.MasterIntensity);
            case "flashRate":                                                   // :1204
                if (Num(1.0) is { } flashRate) s.CapFlashRate = flashRate;
                return Unchanged(s.CapFlashRate);
            case "flashOpacity":                                                // :1205
                if (Num(1.0) is { } flashOpacity) s.CapFlashOpacity = flashOpacity;
                return Unchanged(s.CapFlashOpacity);
            case "subDensity":                                                  // :1206
                if (Num(1.0) is { } subDensity) s.CapSubDensity = subDensity;
                return Unchanged(s.CapSubDensity);
            case "duckDepth":                                                   // :1207
                if (Num(1.0) is { } duckDepth) s.CapDuckDepth = duckDepth;
                return Unchanged(s.CapDuckDepth);
            case "bubbleRate":                                                  // :1208
                if (Num(1.0) is { } bubbleRate) s.CapBubbleRate = bubbleRate;
                return Unchanged(s.CapBubbleRate);
            case "binauralDepth":                                               // :1209
                if (Num(1.0) is { } binauralDepth) s.CapBinauralDepth = binauralDepth;
                return Unchanged(s.CapBinauralDepth);
            case "bgIntensity":                                                 // :1210
                if (Num(1.0) is { } bgIntensity) s.CapBgIntensity = bgIntensity;
                return Unchanged(s.CapBgIntensity);

            // Shared with the descent on purpose: ONE photosensitivity guard app-wide (:1213-1214).
            case "effectIntensity":
                if (Num(0.85) is { } effect)
                {
                    facts = facts with { EffectIntensity = Math.Clamp(effect, 0.0, 1.0) };
                    return new ArcademyWriteResult(facts.EffectIntensity, facts, true);
                }

                return Unchanged(facts.EffectIntensity);

            case "audioMute":                                                   // :1215
                if (Flag(false) is { } mute) s.AudioMute = mute;
                return Unchanged(s.AudioMute);
            case "hideTutorial":                                                // :1216
                if (Flag(false) is { } hide) s.HideTutorial = hide;
                return Unchanged(s.HideTutorial);

            // App-wide comfort volume, stored 0-100 and projected 0..1 (:1219-1222).
            case "masterVolume":
                if (Num(0.32) is { } volume)
                {
                    facts = facts with { MasterVolume = (int)Math.Round(Math.Clamp(volume, 0.0, 1.0) * 100) };
                    return new ArcademyWriteResult(Math.Clamp(facts.MasterVolume / 100.0, 0.0, 1.0), facts, true);
                }

                return Unchanged(Math.Clamp(facts.MasterVolume / 100.0, 0.0, 1.0));

            // Shared asset-source vocabulary; the ratio is 5..95 in AppSettings (:1224-1227).
            case "remoteMediaRatio":
                if (Num(0.30) is { } ratio)
                {
                    facts = facts with { RemoteMediaRatio = (int)Math.Round(Math.Clamp(ratio, 0.0, 1.0) * 100) };
                    return new ArcademyWriteResult(Math.Clamp(facts.RemoteMediaRatio / 100.0, 0.0, 1.0), facts, true);
                }

                return Unchanged(Math.Clamp(facts.RemoteMediaRatio / 100.0, 0.0, 1.0));

            // REFUSE, never wipe (:1228-1249). A non-object here used to store "" — one malformed
            // frame and every rebind the player had made was gone. Echo what is STORED so the
            // page's pending paint converges on the truth instead of on the refused value.
            case "keybinds":
                if (value is not { ValueKind: JsonValueKind.Object } keybinds)
                {
                    return Unchanged(ArcademyProtocol.ParseJsonObject(s.KeybindsJson));
                }

                var keybindsJson = keybinds.GetRawText();
                if (keybindsJson.Length > MaxKeybindsJsonChars)
                {
                    return Unchanged(ArcademyProtocol.ParseJsonObject(s.KeybindsJson));
                }

                s.KeybindsJson = keybindsJson;
                return Unchanged(ArcademyProtocol.ParseJsonObject(s.KeybindsJson));
        }

        var group = Strip(trimmed, "audioLevels.");
        if (ArcademySettingsDocument.DefaultAudioLevels().ContainsKey(group))   // :1250-1251
        {
            return Num(0.5) is { } level
                ? Unchanged(SetAudioLevel(s, group, level))
                : Unchanged(CurrentAudioLevel(s, group));
        }

        return Unchanged(SetGameSetting(s, trimmed, value));

        ArcademyWriteResult Unchanged(object? echo) => new(echo, facts, false);
    }

    /// <summary>One audio group, clamped to ITS ceiling — music is a 0..2 multiplier, the rest are
    /// 0..1 gains (<c>SetAudioLevel</c>, <c>:1257-1266</c>).</summary>
    private static double SetAudioLevel(ArcademySettingsDocument s, string group, double raw)
    {
        var levels = s.AudioLevels ?? ArcademySettingsDocument.DefaultAudioLevels();
        var clamped = Math.Clamp(raw, 0.0, ArcademySettingsDocument.AudioCeiling(group));
        levels[group] = clamped;
        s.AudioLevels = levels;
        return clamped;
    }

    private static double CurrentAudioLevel(ArcademySettingsDocument s, string group) =>
        s.AudioLevels is { } levels && levels.TryGetValue(group, out var v) && double.IsFinite(v)
            ? Math.Clamp(v, 0.0, ArcademySettingsDocument.AudioCeiling(group))
            : ArcademySettingsDocument.DefaultAudioLevels()[group];

    /// <summary>
    /// Per-game knobs (manifest-declared, e.g. <c>dt_hard_mode</c>) into the one flat bag
    /// (<c>SetGameSetting</c>, <c>:1270-1310</c>). Bounded: 200 keys, scalars only — a game that
    /// wanted to store a blob is asking for the meta store instead.
    ///
    /// <para><b>THE ECHO MUST BE WHAT IS STORED.</b> The document's setter DISCARDS the whole bag
    /// past its own cap, so a write that tipped it over used to answer with the value the page
    /// asked for while every per-game knob on the machine was silently gone. Two guards, both
    /// ported: the budget here is BELOW the document's cap, and an over-budget write is refused
    /// outright and echoes the value that survived (<c>:1300-1307</c>).</para>
    /// </summary>
    private static object? SetGameSetting(ArcademySettingsDocument s, string key, JsonElement? value)
    {
        if (value is not { } element ||
            element.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Undefined)
        {
            return null;                                                        // :1272-1276
        }

        if (element.ValueKind == JsonValueKind.String && (element.GetString()?.Length ?? 0) > 256)
        {
            return null;                                                        // :1277-1281
        }

        var bag = ArcademyProtocol.ParseJsonObject(s.SettingsJson) ?? new JsonObject();
        if (bag[key] is null && bag.Count >= 200)
        {
            return null;                                                        // :1284-1288
        }

        var previous = bag[key]?.DeepClone();
        bag[key] = JsonNode.Parse(element.GetRawText());
        // localAssets is a host-built manifest riding this bag at init; never persist it (:1292).
        bag.Remove("localAssets");

        var json = bag.ToJsonString();
        if (json.Length > MaxGameSettingsBagChars)
        {
            return previous;   // null when the key was new, which reads page-side as "not stored"
        }

        s.SettingsJson = json;
        return bag[key];
    }

    /// <summary>Accept the dotted or the bare form (<c>Strip</c>, <c>:1321-1322</c>).</summary>
    private static string Strip(string key, string prefix) =>
        key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;

    /// <summary>
    /// Every key the init projection carries, in upstream's own re-push order
    /// (<c>ProjectedProperties</c>, <c>:1809-1829</c>), paired with its resolved value
    /// (<c>ProjectedSetting</c>, <c>:1864-1886</c>). Used when the settings instance is REPLACED
    /// underneath the session (a restore or a reset), where no per-property change fires for the
    /// values that moved and the page's model only ever moves on an echo (<c>:1797-1807</c>).
    /// </summary>
    public static IReadOnlyList<(string Key, object? Value)> Projected(
        ArcademySettingsDocument s, ArcademyAppFacts f) =>
    [
        ("masterIntensity", s.MasterIntensity),
        ("caps.flashRate", s.CapFlashRate),
        ("caps.flashOpacity", s.CapFlashOpacity),
        ("caps.subDensity", s.CapSubDensity),
        ("caps.duckDepth", s.CapDuckDepth),
        ("caps.bubbleRate", s.CapBubbleRate),
        ("caps.binauralDepth", s.CapBinauralDepth),
        ("caps.bgIntensity", s.CapBgIntensity),
        ("audioMute", s.AudioMute),
        ("hideTutorial", s.HideTutorial),
        ("audioLevels", ArcademyProtocol.BuildAudioLevels(s)),
        ("effectIntensity", f.EffectIntensity),
        ("masterVolume", Math.Clamp(f.MasterVolume / 100.0, 0.0, 1.0)),
        ("remoteMediaRatio", Math.Clamp(f.RemoteMediaRatio / 100.0, 0.0, 1.0)),
        ("remoteMediaEnabled", f.RemoteMediaEnabled),
        ("offlineMode", f.OfflineMode),
        ("motionLevel", ArcademyProtocol.ResolvedMotionLevel(f.Motion)),
        ("protectBrowserVideo", f.ProtectBrowserVideo),
    ];
}
