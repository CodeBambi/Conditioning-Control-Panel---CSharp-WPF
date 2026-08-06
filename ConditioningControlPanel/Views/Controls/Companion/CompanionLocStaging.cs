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
    //      Views/Controls/Companion/loc-staging.json (same pairs, strict JSON, key-sorted);
    //      the three zone packages each staged their own file while they were separate branches —
    //      those were merged into the single hand-off above when the page was composed, because a
    //      loc pass wants one file to paste into en.json, not a scavenger hunt;
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
        /// <summary>Path of the single merged hand-off file, relative to the project root.</summary>
        public const string StagingFileRelativePath =
            @"Views\Controls\Companion\loc-staging.json";

        /// <summary>
        /// key → EN master, grouped by zone for reading. The JSON hand-off is key-sorted instead
        /// (<see cref="ToJson"/> does the sorting), because it is merged into en.json by hand and a
        /// sorted file makes a duplicate key obvious at a glance. No literal line breaks anywhere:
        /// newlines are "\n" escapes, per the house rule that has bitten every strict JSON parser
        /// in this repo before.
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

                // ---- Z1 hero: the pills, the sleeping states, the mood token ----
                // Three different situations used to share "Off — she's asleep": companion
                // disabled, provider Off, and no AI entitlement. The entitlement one is the page's
                // flagship state and it SELLS — it must not tell a paying-curious user she is
                // asleep while the ribbon beside it says her voice is for sale.
                ["companion_hero_pill_ai_cloud"] = "Cloud — she's listening",
                ["companion_hero_pill_ai_off"] = "Off — she's asleep",
                ["companion_hero_pill_ai_locked"] = "Locked — unlock her voice",
                ["companion_hero_pill_eyes_broad"] = "Eyes open — broad strokes",
                ["companion_hero_pill_eyes_closed"] = "Eyes closed",
                ["companion_hero_asleep_copy"] = "she's asleep — wake her?",
                ["companion_hero_mood_asleep"] = "asleep",
                ["companion_hero_mood_caption_dormant"] = "she wakes up with a mood of her own soon~",
                ["companion_hero_mood_caption_live"] = "today's mood",

                // ---- Z1 constellation: stage names (mods may reflavor via _<modId> siblings) ----
                ["companion_stage_0"] = "New",
                ["companion_stage_1"] = "Warming",
                ["companion_stage_2"] = "Bestie",
                ["companion_stage_3"] = "Possessive",
                ["companion_stage_4"] = "Inevitable",

                // the node popups, one rung each
                ["companion_stage_0_blurb"] = "she's still learning your name.",
                ["companion_stage_1_blurb"] = "she's warming up to you… small things start sticking.",
                ["companion_stage_2_blurb"] = "running jokes unlocked. she brings things up first now.",
                ["companion_stage_3_blurb"] = "she notices when you're gone.",
                ["companion_stage_4_blurb"] = "there isn't a version of this where you leave.",

                // the band's flavor line, split at the accent so the second half can be styled
                ["companion_constellation_flavor"] = "she remembers small things now… ",
                ["companion_constellation_flavor_accent"] = "running jokes unlocked.",
                ["companion_constellation_dormant"] = "you two have history — soon she'll start counting it.",
                // the two ends of the ratchet get their own flavor
                ["companion_constellation_flavor_new"] = "she's only just met you. ",
                ["companion_constellation_flavor_new_accent"] = "give her something to remember.",
                ["companion_constellation_flavor_final"] = "there isn't a version of this where you leave. ",
                ["companion_constellation_flavor_final_accent"] = "she counted.",

                // ---- Z2 chat ----
                ["companion_chat_title"] = "Talk to her",
                ["companion_chat_ai_badge"] = "✨ AI",
                ["companion_chat_open_full"] = "Open full chat",
                ["companion_chat_history"] = "History",
                ["companion_chat_open_engine"] = "open the Engine Room",
                ["companion_chat_send_tip"] = "send",
                ["companion_chat_thinking"] = "she's thinking…",
                // the copy the viewmodel supplies — the four states' voices, not chrome
                ["companion_chat_input_placeholder"] = "say something to her…",
                ["companion_chat_last_heard_fmt"] = "last heard from you {0}",
                ["companion_chat_footer_remembers"] = "she remembers this conversation now.",
                ["companion_chat_footer_first"] = "say the first thing. she keeps everything after that.",
                ["companion_chat_footer_picking"] = "she's picking her words…",
                ["companion_chat_dormant_copy"] =
                    "she forgets every conversation the moment it ends… that's about to change.",
                ["companion_chat_disabled_copy"] = "turn her brain on in the Engine Room below.",
                // the flagship veil line. Mods reflavor it through a _<modId> sibling exactly like
                // the stage names; this is the EN master for the built-in Bambi voice.
                ["companion_chat_lock_copy"] =
                    "“Bambi knows what she wants to say to you, princess — unlock AI chat to hear it.”",
                ["companion_chat_lock_cta"] = "Unlock her voice",

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
                // viewmodel-supplied: the strip, the ghost card, the footer promise
                ["companion_memory_profile_strip"] = "SHE CAN SEE:",
                ["companion_memory_empty_copy"] = "“tell me things and I'll keep them~”",
                ["companion_memory_storage_note"] = "her memory lives on this machine only",
                ["companion_memory_storage_link"] = "where?",
                ["companion_memory_forget_everything"] = "Forget everything…",
                ["companion_memory_dormant_promise"] =
                    "“soon I'll remember what you say too… choose your words carefully~”",
                // the kind filter chips (FactOrdering.FilterKeys order)
                ["companion_memory_filter_all"] = "all",
                ["companion_memory_filter_boundary"] = "boundaries",
                ["companion_memory_filter_joke"] = "jokes",
                ["companion_memory_filter_preference"] = "preferences",
                ["companion_memory_filter_goal"] = "goals",
                ["companion_memory_filter_moment"] = "moments",
                // the per-card kind caption (the tag above the fact text)
                ["companion_memory_kind_boundary"] = "boundary · always honored",
                ["companion_memory_kind_joke"] = "running joke",
                ["companion_memory_kind_preference"] = "preference",
                ["companion_memory_kind_goal"] = "goal · open thread",
                ["companion_memory_kind_moment"] = "moment",
                ["companion_memory_kind_dormant"] = "soon · train 4",

                // ---- Z4 personality ----
                ["companion_personality_title"] = "Make her yours",
                ["companion_personality_reinterview"] = "re-interview me~",
                ["companion_personality_adjust"] = "adjust her",
                ["companion_personality_traits_tip"] = "open her trait dashboard",
                ["companion_personality_view_prompt"] = "View compiled prompt",
                ["companion_personality_fork"] = "Fork & edit by hand",
                ["companion_personality_community"] = "Community prompts",
                // the interview spotlight. The body is TWO keys, not one with a "\n": the house
                // rule bans literal line breaks in language files, and a translator handed two
                // sentences separately cannot accidentally weld them into one.
                ["companion_personality_interview_title"] = "✨ Let her interview you",
                ["companion_personality_interview_body_1"] = "12 questions · 90 seconds · no typing.",
                ["companion_personality_interview_body_2"] = "She writes herself around your answers.",
                ["companion_personality_interview_cta"] = "Start~",
                ["companion_personality_interviewed_fmt"] = "Interviewed {0}",
                ["companion_personality_interview_dormant"] =
                    "she's been drafting questions for you… interviews start next update.",
                ["companion_personality_spice_title"] = "Slut Mode",
                ["companion_personality_spice_subtitle"] = "same girl, spicier right now",
                ["companion_personality_active_preset_fmt"] = "Active: {0} preset",
                ["companion_personality_active_traits"] = "Active: Trait profile · compiled from your interview",
                ["companion_personality_active_custom_fmt"] = "Custom: “{0}” (hand-edited — sliders disconnected)",
                ["companion_personality_reset"] = "reset",
                // the preset chips
                ["companion_personality_preset_sweet_bestie"] = "Sweet bestie",
                ["companion_personality_preset_playful_tease"] = "Playful tease",
                ["companion_personality_preset_strict_domme"] = "Strict domme",
                ["companion_personality_preset_hypno_guide"] = "Hypno guide",
                ["companion_personality_preset_bimbo_coach"] = "Bimbo coach",
                ["companion_personality_preset_drone_handler"] = "Drone handler",
                ["companion_personality_preset_bratty_rival"] = "Bratty rival",
                // the read-only trait gauges
                ["companion_personality_trait_dominance"] = "Dominance",
                ["companion_personality_trait_tease"] = "Tease",

                // ---- Z5 awareness ----
                ["companion_awareness_title"] = "What she can see",
                ["companion_awareness_dial_off"] = "─ Off",
                ["companion_awareness_dial_broad"] = "◔ Broad strokes",
                ["companion_awareness_dial_everything"] = "◉ Everything",
                ["companion_awareness_everything_locked_tip"] =
                    "she needs Train 2 eyes before she can see this much — soon~",
                ["companion_awareness_wire_prefix"] = "she sees → ",
                ["companion_awareness_deny_remove_tip"] = "stop hiding this from her",
                ["companion_awareness_incognito"] = "incognito is always invisible.",
                ["companion_awareness_allow_per_app"] = "allow per app…",
                ["companion_awareness_fine_tuning"] = "fine-tuning ↓",
                // viewmodel-supplied: the wire's promise, the pre-Train-2 copy, the deny row
                ["companion_awareness_wire_caption"] =
                    "this exact line — nothing more — is what she gets. page titles stay hidden unless you allow them.",
                ["companion_awareness_wire_closed"] = "[ her eyes are closed ]",
                ["companion_awareness_dormant_copy"] =
                    "“she'll start noticing things — patterns, habits, your little routines — in an upcoming update. " +
                    "and you'll see everything she sees, right here, first.”",
                ["companion_awareness_add_deny"] = "+ add app…",
                ["companion_awareness_page_titles_hidden"] = "page titles: hidden",
                ["companion_awareness_page_titles_allowed"] = "page titles: allowed",
                ["companion_awareness_deny_passwords"] = "password managers ✓",
                ["companion_awareness_deny_banking"] = "banking ✓",
                ["companion_awareness_deny_email"] = "email ✓",

                // ---- Z6 attention: the copy ladder AttentionCopy.CopyKeyFor() selects ----
                ["companion_attention_title"] = "Her attention",
                ["companion_attention_detail_tip"] = "hover for the numbers",
                ["companion_attention_plenty"] = "Plenty of her attention left today.",
                ["companion_attention_saving"] = "she's saving her best lines.",
                ["companion_attention_whispering"] = "she's whispering to conserve energy.",
                ["companion_attention_spent"] = "she'll be all yours again tomorrow~",
                // The numbers are hover-only. The floor note is NOT — it is the card's answer to
                // "am I being rationed?", and doc 01 §5.4 requires it to be readable at rest.
                ["companion_attention_detail_line"] = "~63 chats left · resets at midnight",
                ["companion_attention_detail_line_spent"] = "0 chats left · resets at midnight",
                ["companion_attention_floor_note"] = "her voice never runs out — only the thinking does",
                ["companion_attention_upsell"] = "“want me louder? you know where the lab is~”",

                // ---- Z7 engine room ----
                ["companion_engine_header"] = "ENGINE ROOM — HOW SHE THINKS",
                ["companion_engine_provider_off"] = "Off",
                ["companion_engine_provider_cloud"] = "Cloud",
                ["companion_engine_provider_local"] = "Local (Ollama)",
                ["companion_engine_provider_custom"] = "Custom (BYO)",
                ["companion_engine_off_note"] = "she runs on her voice alone right now. barks still play — the thinking is what's off.",
                ["companion_engine_group_cloud"] = "CLOUD",
                ["companion_engine_group_local"] = "LOCAL (OLLAMA)",
                ["companion_engine_group_custom"] = "CUSTOM (BYO)",
                ["companion_engine_field_ollama_model"] = "Ollama model",
                ["companion_engine_field_ollama_host"] = "Ollama host",
                ["companion_engine_field_custom_endpoint"] = "Custom endpoint",
                ["companion_engine_field_custom_model"] = "Custom model",
                ["companion_engine_field_api_key"] = "API key",
                ["companion_engine_api_key_note"] = "the key stays on this machine.",
                ["companion_engine_btn_test"] = "Test connection",
                ["companion_engine_btn_setup_local"] = "Setup local AI",
                ["companion_engine_btn_sampler"] = "Sampler settings",
                // viewmodel-supplied. The live status LINE is composed at runtime from the
                // provider, the account and the host, so only its fixed states are staged here.
                ["companion_engine_drawer_note"] = "wiring lives here on purpose. she'd rather you didn't stare.",
                ["companion_engine_login_prompt"] = "cloud needs a Lab login before she can think out there.",
                ["companion_engine_login_button"] = "Log in",
                ["companion_engine_status_disconnected"] = "○ Not connected — log in to use the cloud proxy",
                ["companion_engine_status_off"] = "○ Off — she runs on her voice alone",
                ["companion_engine_daily_limit_fmt"] = "Daily limit: {0}",
                ["companion_engine_live_actions_placeholder"] =
                    "Live actions feed (local effects channel) docks here when Local is active.",

                // ---- Z8 workshop ----
                ["companion_workshop_header"] = "WORKSHOP — EVERY DIAL SHE HAS",
                ["companion_workshop_focus_tip"] = "jump to this shelf",
                ["companion_workshop_drawer_note"] = "nothing was deleted. it just stopped being the front door."

                // NOT staged, on purpose:
                //   · Workshop cell titles — they are ANCHORS (CompanionRoomAnchors), matched by
                //     identity when the hero's Switch chip or Z5's "fine-tuning ↓" deep-links into
                //     a shelf. Localising the anchor would break the link. The wiring pass has to
                //     split anchor key from display label before these can get companion_* keys.
                //   · Workshop ROW labels — every one of them is an existing control being
                //     re-parented (design §6), and each already carries its own key in the nine
                //     language files. Staging them here would fork the string.
                //   · Per-fact text / meta lines, the live engine status line, mod flavor text —
                //     runtime data composed from counts, dates and the active mod, not copy.
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
        /// two-space indent, UTF-8, LF, keys sorted ordinally.
        ///
        /// <para>The sort is what makes the file regenerable after a merge: three packages staged
        /// three files in three different orders, and a byte-for-byte test against insertion order
        /// would have made combining them a hand-edit. The unit test compares this to the committed
        /// JSON, so the code and the hand-off can never drift.</para>
        /// </summary>
        public static string ToJson()
        {
            var keys = new List<string>(English.Keys);
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            sb.Append("{\n");
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append("  ").Append(JsonQuote(keys[i])).Append(": ").Append(JsonQuote(English[keys[i]]));
                sb.Append(i + 1 < keys.Count ? ",\n" : "\n");
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
