namespace CcpClient.Desktop.Companion;

/// <summary>
/// The compact BUILT-IN bark rule set (WPF-schema JSON, BarkRuleLoader-compatible so the
/// full WPF content migration drops in later — content volume is a named limit; the full
/// manifest lives at ConditioningControlPanel/Resources/sounds/companion_audio/bark_rules.json).
/// One rule per semantic the pipeline ports: safety, conditioned matching, lifetime
/// one-shot, easter_egg, chance, cooldown, priority preemption, DTRH-mapped triggers.
/// Audio filenames resolve through <see cref="IBarkAudioResolver"/>; this slice ships no
/// voice assets (named limit) so audio variants surface text-only with a typed reason
/// until the voice-content row lands.
/// </summary>
public static class DefaultBarkRules
{
    /// <summary>The built-in manifest JSON (WPF field names verbatim).</summary>
    public const string ManifestJson = """
        [
          {
            "id": "safety_panic",
            "trigger": "Panic",
            "priority": 1000,
            "cooldown_ms": 0,
            "repeatable": true,
            "scope": "session",
            "mood": "calm, grounding",
            "class": "safety",
            "variant_pool": [
              { "text": "Stopped. Everything is stopped. You're safe — breathe.", "audio": "safety_panic_1.mp3" },
              { "text": "All quiet now. Nothing is running. Take your time.", "audio": "safety_panic_2.mp3" }
            ]
          },
          {
            "id": "chaos_wave_cleared",
            "trigger": "ChaosWaveCleared",
            "priority": 100,
            "cooldown_ms": 20000,
            "repeatable": true,
            "scope": "session",
            "mood": "pleased, teasing",
            "class": "normal",
            "variant_pool": [
              { "text": "Wave {wave} cleared. Clean work — keep that focus.", "audio": "chaos_wave_cleared_1.mp3" },
              { "text": "That's wave {wave} down. You make it look easy.", "audio": "chaos_wave_cleared_2.mp3" },
              { "text": "Cleared. I almost think you're getting good at this." }
            ]
          },
          {
            "id": "chaos_wave_cleared_high",
            "trigger": "ChaosWaveCleared",
            "conditions": { "wave_gte": 5 },
            "priority": 110,
            "cooldown_ms": 0,
            "repeatable": true,
            "scope": "session",
            "mood": "impressed",
            "class": "normal",
            "variant_pool": [
              { "text": "Wave {wave}? Deeper than most ever get. I'm watching closely now.", "audio": "chaos_wave_cleared_high_1.mp3" }
            ]
          },
          {
            "id": "chaos_defuse_first",
            "trigger": "ChaosDefuseFirst",
            "priority": 320,
            "cooldown_ms": 0,
            "repeatable": false,
            "scope": "lifetime",
            "mood": "approving, warm",
            "class": "normal",
            "variant_pool": [
              { "text": "Your first defuse. Remember how that felt — calm in the middle of all that noise.", "audio": "chaos_defuse_first_1.mp3" }
            ]
          },
          {
            "id": "egg_mute_secret",
            "trigger": "MuteToggled",
            "conditions": { "mute_eq": true },
            "priority": 480,
            "cooldown_ms": 0,
            "repeatable": false,
            "scope": "lifetime",
            "mood": "fond, conspiratorial",
            "class": "easter_egg",
            "variant_pool": [
              { "text": "Silence, hm? I can work with silence. I'll just... write everything down for you." }
            ]
          },
          {
            "id": "chaos_detonated_chance",
            "trigger": "ChaosBubbleDetonated",
            "priority": 90,
            "cooldown_ms": 0,
            "repeatable": true,
            "scope": "session",
            "mood": "smug",
            "class": "normal",
            "chance": 0.5,
            "variant_pool": [
              { "text": "It went off because you hesitated. I noticed.", "audio": "chaos_detonated_1.mp3" },
              { "text": "Boom. That one was yours." }
            ]
          },
          {
            "id": "chaos_combo_milestone",
            "trigger": "ChaosComboMilestone",
            "priority": 150,
            "cooldown_ms": 30000,
            "repeatable": true,
            "scope": "session",
            "mood": "delighted",
            "class": "normal",
            "variant_pool": [
              { "text": "Combo {combo}. Rhythm suits you.", "audio": "chaos_combo_milestone_1.mp3" },
              { "text": "{combo} in a row. Don't stop now." }
            ]
          },
          {
            "id": "attention_check_fail",
            "trigger": "AttentionCheckFail",
            "priority": 200,
            "cooldown_ms": 0,
            "repeatable": true,
            "scope": "session",
            "mood": "stern",
            "class": "normal",
            "variant_pool": [
              { "text": "Eyes here. You drifted — come back.", "audio": "attention_check_fail_1.mp3" }
            ]
          }
        ]
        """;
}
