using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Markup;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    // =====================================================================================
    //  STAGED LOCALIZATION for the Companion tab redesign.
    //
    //  The nine Localization/Languages/*.json files are owned by a concurrent workflow, so this
    //  package must not touch them. Instead:
    //
    //    · every user-visible string in these controls is referenced by its real loc key
    //      through {cmp:CmpStr companion_xxx}, exactly like {loc:Str} elsewhere;
    //    · the EN masters live here, in CompanionLocStaging.English, and in the hand-off file
    //      Views/Controls/Companion/loc-staging-companion-tab.json (same pairs, strict JSON);
    //    · CmpStr resolves through LocalizationManager first and only falls back to the staged
    //      English when the key is not in the language files yet.
    //
    //  Consequence: the scaffold renders real copy today, and the moment the loc pass merges the
    //  staging file into en.json (plus translations) every string switches over with no code
    //  change. When all nine files carry the keys, CmpStr can be swapped for the house {loc:Str}
    //  and this file deleted.
    //
    //  CompanionLocStagingTests pins English against the on-disk JSON so the two cannot drift.
    // =====================================================================================

    /// <summary>The EN masters for the <c>companion_*</c> keys this package introduces.</summary>
    public static class CompanionLocStaging
    {
        /// <summary>Path of the hand-off file, relative to the project root.</summary>
        public const string StagingFileRelativePath =
            @"Views\Controls\Companion\loc-staging-companion-tab.json";

        /// <summary>
        /// key → EN master. Kept in the same order as the JSON hand-off file so a diff of the two
        /// reads cleanly. No literal line breaks anywhere: newlines are "\n" escapes, per the
        /// house rule that has bitten every strict JSON parser in this repo before.
        /// </summary>
        public static IReadOnlyDictionary<string, string> English { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ---- shared microtags ----
                ["companion_tag_train1"] = "TRAIN 1",
                ["companion_tag_train2"] = "TRAIN 2",
                ["companion_tag_train3"] = "TRAIN 3",
                ["companion_tag_train4"] = "TRAIN 4",

                // ---- Z0 header band ----
                ["companion_header_title"] = "Companion",
                ["companion_header_subtitle"] = "her room — everything she is, knows, and notices",
                ["companion_header_tutorial"] = "Tutorial",
                ["companion_header_plate_ai"] = "LAB · AI",
                ["companion_header_plate_next"] = "PRIME",
                ["companion_header_teaser"] = "unlock her voice",

                // ---- Z1 hero ----
                ["companion_hero_chat"] = "Chat with her",
                ["companion_hero_switch"] = "⇄ Switch",
                ["companion_hero_detach"] = "⧉ Detach",
                ["companion_hero_show_tip"] = "show companion",
                ["companion_hero_mute_tip"] = "mute",
                ["companion_hero_wake"] = "Wake her up",
                ["companion_hero_level_plate"] = "LEVEL",

                // ---- Z1 constellation: stage names (mods may reflavor via _<modId> siblings) ----
                ["companion_stage_0"] = "New",
                ["companion_stage_1"] = "Warming",
                ["companion_stage_2"] = "Bestie",
                ["companion_stage_3"] = "Possessive",
                ["companion_stage_4"] = "Inevitable",

                // ---- Z2 chat ----
                ["companion_chat_title"] = "Talk to her",
                ["companion_chat_ai_badge"] = "✨ AI",
                ["companion_chat_open_full"] = "Open full chat",
                ["companion_chat_history"] = "History",
                ["companion_chat_open_engine"] = "open the Engine Room",
                ["companion_chat_send_tip"] = "send",
                ["companion_chat_thinking"] = "she's thinking…",

                // ---- Z3 memory ----
                ["companion_memory_title"] = "What she knows about you",
                ["companion_memory_hint"] = "her diary. read at your own risk~",
                ["companion_memory_pin_tip"] = "pin",
                ["companion_memory_edit_tip"] = "edit",
                ["companion_memory_forget_tip"] = "forget",
                ["companion_memory_edit_save"] = "save",
                ["companion_memory_edit_cancel"] = "cancel",
                // the wipe asks first, in her voice (design §3 Z3)
                ["companion_memory_forget_confirm"] = "…all of it? even the good parts?",
                ["companion_memory_forget_yes"] = "Yes, wipe it",
                ["companion_memory_forget_no"] = "No, keep us",

                // ---- Z4 personality ----
                ["companion_personality_title"] = "Make her yours",
                ["companion_personality_view_prompt"] = "View compiled prompt",
                ["companion_personality_fork"] = "Fork & edit by hand",
                ["companion_personality_community"] = "Community prompts",

                // ---- Z5 awareness ----
                ["companion_awareness_title"] = "What she can see",
                ["companion_awareness_dial_off"] = "─ Off",
                ["companion_awareness_dial_broad"] = "◔ Broad strokes",
                ["companion_awareness_dial_everything"] = "◉ Everything",
                ["companion_awareness_wire_prefix"] = "she sees → ",
                ["companion_awareness_incognito"] = "incognito is always invisible.",
                ["companion_awareness_allow_per_app"] = "allow per app…",
                ["companion_awareness_fine_tuning"] = "fine-tuning ↓",

                // ---- Z6 attention: the copy ladder AttentionCopy.CopyKeyFor() selects ----
                ["companion_attention_title"] = "Her attention",
                ["companion_attention_plenty"] = "Plenty of her attention left today.",
                ["companion_attention_saving"] = "she's saving her best lines.",
                ["companion_attention_whispering"] = "she's whispering to conserve energy.",
                ["companion_attention_spent"] = "she'll be all yours again tomorrow~",

                // ---- Z7 engine room ----
                ["companion_engine_header"] = "ENGINE ROOM — HOW SHE THINKS",
                ["companion_engine_provider_off"] = "Off",
                ["companion_engine_provider_cloud"] = "Cloud",
                ["companion_engine_provider_local"] = "Local (Ollama)",
                ["companion_engine_provider_custom"] = "Custom (BYO)",
                ["companion_engine_field_ollama_model"] = "Ollama model",
                ["companion_engine_field_ollama_host"] = "Ollama host",
                ["companion_engine_field_custom_endpoint"] = "Custom endpoint",
                ["companion_engine_field_custom_model"] = "Custom model",
                ["companion_engine_field_api_key"] = "API key",
                ["companion_engine_btn_test"] = "Test connection",
                ["companion_engine_btn_setup_local"] = "Setup local AI",
                ["companion_engine_btn_sampler"] = "Sampler settings",

                // ---- Z8 workshop ----
                ["companion_workshop_header"] = "WORKSHOP — EVERY DIAL SHE HAS"
            };

        /// <summary>
        /// Resolves a key: the live language files win, the staged English is the safety net, and
        /// an unknown key returns itself (the house behaviour, so a typo is visible in the UI).
        /// </summary>
        public static string Resolve(string? key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            string live;
            try
            {
                live = LocalizationManager.Instance[key];
            }
            catch (Exception)
            {
                // No language files (unit tests, designer) — fall straight through to the masters.
                live = key;
            }

            // LocalizationManager returns the key itself when nothing matched.
            if (!string.IsNullOrEmpty(live) && !string.Equals(live, key, StringComparison.Ordinal)) return live;

            return English.TryGetValue(key, out var staged) ? staged : key;
        }

        /// <summary>
        /// Serialises <see cref="English"/> in the exact shape of the hand-off file: a flat object,
        /// two-space indent, UTF-8, LF, sorted by nothing (insertion order preserved).
        /// The unit test compares this to the committed JSON so the two can never drift.
        /// </summary>
        public static string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            int i = 0;
            foreach (var kv in English)
            {
                sb.Append("  ").Append(JsonQuote(kv.Key)).Append(": ").Append(JsonQuote(kv.Value));
                sb.Append(++i < English.Count ? ",\n" : "\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>Minimal strict-JSON string escaping. Control characters become \n / \u00XX.</summary>
        private static string JsonQuote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    /// <summary>
    /// XAML markup extension for a staged companion string.
    /// Usage: <c>Text="{cmp:CmpStr companion_chat_title}"</c>
    ///
    /// <para>Behaves like the house <c>{loc:Str}</c> — a live binding to LocalizationManager, so it
    /// still updates on a language switch — but routes the result through
    /// <see cref="CompanionLocStaging.Resolve"/> so a key that has not reached the language files
    /// yet renders its EN master instead of the raw key.</para>
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class CmpStrExtension : MarkupExtension
    {
        public CmpStrExtension() { Key = string.Empty; }
        public CmpStrExtension(string key) { Key = key; }

        /// <summary>The <c>companion_*</c> loc key.</summary>
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key)) return string.Empty;

            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay,
                Converter = StagedFallbackConverter.Instance,
                ConverterParameter = Key
            };
            return binding.ProvideValue(serviceProvider);
        }

        /// <summary>Swaps a "key echoed back" result for the staged English master.</summary>
        private sealed class StagedFallbackConverter : IValueConverter
        {
            internal static readonly StagedFallbackConverter Instance = new();

            public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            {
                string key = parameter as string ?? string.Empty;
                string resolved = value as string ?? string.Empty;
                if (!string.IsNullOrEmpty(resolved) && !string.Equals(resolved, key, StringComparison.Ordinal))
                    return resolved;
                return CompanionLocStaging.English.TryGetValue(key, out var staged) ? staged : key;
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
                => Binding.DoNothing;
        }
    }
}
