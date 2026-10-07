using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>Whether an LLM surface is usable, teased, or merely sleeping until a train lands.</summary>
    public enum CompanionZoneState
    {
        /// <summary>Fully functional.</summary>
        Live,
        /// <summary>Shipped but the feature is not built yet — shimmer + in-character promise.</summary>
        Dormant,
        /// <summary>Entitlement gate — Velvet-Vault veil with a personal sell line and a CTA chip.</summary>
        Locked,
        /// <summary>Works, but there is nothing in it yet.</summary>
        Empty,
        /// <summary>Provider is Off / the companion is disabled.</summary>
        Disabled
    }

    /// <summary>
    /// The pure half of Her Room's chat threshold (Z2) and memory diary (Z3), shared by the WPF
    /// runtime viewmodels (ChatThresholdRuntimeVm, MemoryFactRuntimeVm) and the Avalonia twins so
    /// the state ladder, the thread pick, the echo unwrap and the relative-time copy exist once.
    /// </summary>
    public static class CompanionRoomLogic
    {
        /// <summary>How many turns the threshold shows. It is a doorway, not a chat app.</summary>
        public const int VisibleTurnCount = 3;

        /// <summary>The four states, in priority order.</summary>
        public static CompanionZoneState ResolveState(
            bool brainRouting, bool aiEnabled, bool cloudProvider, bool entitled)
        {
            if (!aiEnabled) return CompanionZoneState.Disabled;
            // Entitlement is checked before the kill switch: a free user is being SOLD something,
            // and telling them "that's about to change" instead of showing the veil would bury the
            // one surface on this page that converts.
            if (cloudProvider && !entitled) return CompanionZoneState.Locked;
            if (!brainRouting) return CompanionZoneState.Dormant;
            return CompanionZoneState.Live;
        }

        /// <summary>
        /// The last <paramref name="take"/> dialogue turns (user, assistant, bark echo), oldest
        /// first. Ambient turns shape the prompt but are not dialogue and never show.
        /// </summary>
        public static IReadOnlyList<CompanionTurn> PickThread(
            IReadOnlyList<CompanionTurn>? turns, int take = VisibleTurnCount)
        {
            if (turns == null || turns.Count == 0) return Array.Empty<CompanionTurn>();
            var picked = new List<CompanionTurn>(take);
            for (int i = turns.Count - 1; i >= 0 && picked.Count < take; i--)
            {
                var turn = turns[i];
                if (turn == null) continue;
                if (turn.Kind is not (TurnKind.UserChat or TurnKind.AssistantChat or TurnKind.BarkEcho)) continue;
                picked.Add(turn);
            }
            picked.Reverse();
            return picked;
        }

        /// <summary>The bubble text for a turn: a bark echo loses its «name said aloud: "…"» wrapper.</summary>
        public static string BubbleText(CompanionTurn turn)
            => turn.Kind == TurnKind.BarkEcho ? UnwrapEcho(turn.Text) : turn.Text;

        /// <summary>Only a genuine model completion wears the AI badge; app replies keep their provenance.</summary>
        public static bool IsAiBubble(CompanionTurn turn)
            => turn.Kind == TurnKind.AssistantChat && !turn.IsApplicationReply;

        /// <summary>
        /// Strips the «name said aloud: "…"» wrapper so the whisper bubble shows the line she
        /// actually spoke. The sigil is prompt plumbing; printing it would be showing the user our
        /// wire format.
        /// </summary>
        public static string UnwrapEcho(string? text)
        {
            var body = text ?? string.Empty;
            var match = Regex.Match(body, "^«[^:]*:\\s*\"(.*)\"»$", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : body.Trim('«', '»').Trim();
        }

        /// <summary>"just now" / "22m ago" / "2h ago" / "3d ago". Never a raw timestamp.</summary>
        public static string RelativeTime(DateTime utc)
        {
            var delta = DateTime.UtcNow - utc;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
            if (delta.TotalMinutes < 1) return Loc.Get("companion_chat_time_now");
            if (delta.TotalHours < 1) return Loc.GetF("companion_chat_time_minutes", (int)delta.TotalMinutes);
            if (delta.TotalDays < 1) return Loc.GetF("companion_chat_time_hours", (int)delta.TotalHours);
            return Loc.GetF("companion_chat_time_days", (int)delta.TotalDays);
        }

        /// <summary>
        /// Memory kinds → the wall's filter chips. <see cref="MemoryFactKind.Event"/> is the design's
        /// "moment"; <see cref="MemoryFactKind.Identity"/> has no chip of its own and so appears only
        /// under "all", which is deliberate — an identity fact is not something anyone filters FOR.
        /// </summary>
        public static string KindKeyFor(MemoryFactKind kind) => kind switch
        {
            MemoryFactKind.Boundary => "boundary",
            MemoryFactKind.Joke => "joke",
            MemoryFactKind.Preference => "preference",
            MemoryFactKind.Goal => "goal",
            MemoryFactKind.Identity => "identity",
            _ => "moment"
        };

        /// <summary>"used 4× · last: 2d ago", or the provenance line for a hand-edited fact.</summary>
        public static string BuildMeta(int uses, DateTime? lastUsed, bool userEdited)
        {
            var parts = new List<string>(3);
            if (uses > 0) parts.Add(Loc.GetF("companion_memory_meta_uses", uses));
            if (lastUsed.HasValue)
                parts.Add(Loc.GetF("companion_memory_meta_last", RelativeTime(lastUsed.Value)));
            if (userEdited) parts.Add(Loc.Get("companion_memory_meta_edited"));
            return parts.Count == 0
                ? Loc.Get("companion_memory_meta_new")
                : string.Join(" · ", parts);
        }
    }
}
