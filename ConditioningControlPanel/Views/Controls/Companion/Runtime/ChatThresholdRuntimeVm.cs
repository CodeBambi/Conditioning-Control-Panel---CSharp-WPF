using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z2 — Talk to her, wired to <see cref="CompanionBrain"/>.
    ///
    /// <para><b>The AI badge invariant, and why this zone can honour it without a flag on the
    /// turn.</b> <see cref="CompanionBrain.ChatAsync"/> appends an
    /// <see cref="TurnKind.AssistantChat"/> turn on exactly one path: a reply that came back with
    /// <c>IsAiGenerated == true</c>. A refusal, a canned fallback, a transport failure and a login
    /// hint all roll the user's turn back out and append nothing. So every AssistantChat turn in the
    /// log is genuine model output, and the badge can key off the kind. If that ever stops being
    /// true the badge becomes a lie, which is why it is stated here as loudly as this.</para>
    ///
    /// <para><b>Bark echoes.</b> <see cref="TurnKind.BarkEcho"/> turns render as the italic
    /// whisper bubble — her recorded voice, visualised, so the one-mouth design is visible on the
    /// page. They never carry the badge: a bark is a recording, not a completion.</para>
    ///
    /// <para><b>Ambient turns are deliberately not shown.</b> <see cref="TurnKind.AmbientEvent"/>
    /// and <see cref="TurnKind.AmbientReply"/> shape the prompt window but are not dialogue, and
    /// they never reach disk. A threshold surface that quietly listed "user is on Chrome (fun) for
    /// 22m" would be showing the user their own browsing history back at them, in a card whose
    /// whole promise is that it holds a conversation.</para>
    ///
    /// <para><b>Kill switch.</b> No brain, or <c>UseCompanionBrain=false</c>, is the
    /// <see cref="CompanionZoneState.Dormant"/> state — which is precisely what that state was
    /// written for: the legacy stateless path really does forget every conversation the moment it
    /// ends. The input row stands down with it, because there is no thread for a typed line to join.</para>
    /// </summary>
    internal sealed class ChatThresholdRuntimeVm : CompanionObservable, IChatThresholdVm
    {
        /// <summary>How many turns the threshold shows. It is a doorway, not a chat app.</summary>
        public const int VisibleTurnCount = 3;

        private readonly CompanionRuntimeContext _ctx;
        private readonly ObservableCollection<IChatBubbleVm> _turns = new();

        private CompanionZoneState _state = CompanionZoneState.Live;
        private string _draft = string.Empty;
        private bool _isThinking;
        private string _lastHeardCopy = string.Empty;
        private DateTime? _lastHeardUtc;

        public ChatThresholdRuntimeVm(CompanionRuntimeContext ctx)
        {
            _ctx = ctx;
            TeaserTurns = BuildTeaser();

            SendCommand = new CompanionRelayCommand(_ => Send(), _ => CanSend && !string.IsNullOrWhiteSpace(Draft));
            OpenFullChatCommand = new CompanionRelayCommand(
                () => CompanionRuntimeContext.Guarded(() => App.AvatarWindow?.OpenChatInput(), "open full chat"));
            HistoryCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => CompanionTranscriptWindow.ShowFor(w)));
            UnlockCommand = new CompanionRelayCommand(() => _ctx.WithWindow(w => w.ShowTab("patreon")));
            OpenEngineRoomCommand = new CompanionRelayCommand(() => _ctx.Navigator?.RevealEngineRoom());

            Sync();
        }

        public CompanionZoneState State
        {
            get => _state;
            private set
            {
                if (!Set(ref _state, value)) return;
                Raise(nameof(CanSend));
                Raise(nameof(StateCopy));
                Raise(nameof(FooterCopy));
            }
        }

        public IReadOnlyList<IChatBubbleVm> Turns => _turns;
        public IReadOnlyList<IChatBubbleVm> TeaserTurns { get; }

        public string Draft
        {
            get => _draft;
            set => Set(ref _draft, value ?? string.Empty);
        }

        public bool IsThinking
        {
            get => _isThinking;
            private set
            {
                if (!Set(ref _isThinking, value)) return;
                Raise(nameof(CanSend));
                Raise(nameof(FooterCopy));
            }
        }

        public bool CanSend => State == CompanionZoneState.Live && !IsThinking;

        public string LastHeardCopy { get => _lastHeardCopy; private set => Set(ref _lastHeardCopy, value); }

        public string FooterCopy => IsThinking
            ? Loc.Get("companion_chat_footer_picking")
            : _turns.Count == 0
                ? Loc.Get("companion_chat_footer_first")
                : Loc.Get("companion_chat_footer_remembers");

        public string StateCopy => State switch
        {
            CompanionZoneState.Dormant => Loc.Get("companion_chat_dormant_copy"),
            CompanionZoneState.Disabled => Loc.Get("companion_chat_disabled_copy"),
            _ => string.Empty
        };

        public string LockCopy => Loc.Get("companion_chat_lock_copy");
        public string LockCtaLabel => Loc.Get("companion_chat_lock_cta");
        public string InputPlaceholder => Loc.Get("companion_chat_input_placeholder");

        public ICommand SendCommand { get; }
        public ICommand OpenFullChatCommand { get; }
        public ICommand HistoryCommand { get; }
        public ICommand UnlockCommand { get; }
        public ICommand OpenEngineRoomCommand { get; }

        // =====================================================================================
        //  state
        // =====================================================================================

        /// <summary>
        /// Re-reads the provider, the entitlement and the thread. Called by the room's Sync and
        /// after every send.
        /// </summary>
        public void Sync()
        {
            CompanionRuntimeContext.Guarded(() =>
            {
                State = ResolveState();
                RebuildThread();
                RefreshLastHeard();
            }, "chat sync");
        }

        /// <summary>
        /// The four states, in priority order. Pure and static so the ladder is testable without a
        /// brain, a login or a settings file.
        /// </summary>
        internal static CompanionZoneState ResolveState(
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

        private static CompanionZoneState ResolveState()
        {
            var settings = App.Settings?.Current;
            bool aiEnabled = settings?.AiChatEnabled == true;
            var provider = settings?.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud;
            bool entitled = App.Patreon?.HasAiAccess == true || App.HasCloudIdentity;
            return ResolveState(
                brainRouting: CompanionBrain.ShouldRoute(App.Brain),
                aiEnabled: aiEnabled,
                cloudProvider: provider == AiProviderType.Cloud,
                entitled: entitled);
        }

        // =====================================================================================
        //  the thread
        // =====================================================================================

        /// <summary>
        /// Projects the brain's turn log onto the last <see cref="VisibleTurnCount"/> bubbles.
        ///
        /// <para>Pure, static and log-shaped so the mapping — which is where the AI badge lives —
        /// can be tested without standing up a brain.</para>
        /// </summary>
        internal static IReadOnlyList<IChatBubbleVm> ProjectThread(
            IReadOnlyList<CompanionTurn>? turns, int take = VisibleTurnCount)
        {
            if (turns == null || turns.Count == 0) return Array.Empty<IChatBubbleVm>();

            var picked = new List<CompanionTurn>(take);
            for (int i = turns.Count - 1; i >= 0 && picked.Count < take; i--)
            {
                var turn = turns[i];
                if (turn == null) continue;
                if (turn.Kind is not (TurnKind.UserChat or TurnKind.AssistantChat or TurnKind.BarkEcho)) continue;
                picked.Add(turn);
            }
            picked.Reverse();

            var bubbles = new List<IChatBubbleVm>(picked.Count);
            foreach (var turn in picked)
            {
                bubbles.Add(new CompanionChatBubble(
                    kind: turn.Kind switch
                    {
                        TurnKind.UserChat => CompanionBubbleKind.You,
                        TurnKind.BarkEcho => CompanionBubbleKind.Echo,
                        _ => CompanionBubbleKind.Her
                    },
                    text: turn.Kind == TurnKind.BarkEcho ? UnwrapEcho(turn.Text) : turn.Text,
                    // Only an AssistantChat turn is a genuine completion — see the class remarks.
                    isAi: turn.Kind == TurnKind.AssistantChat,
                    timestamp: RelativeTime(turn.Utc)));
            }
            return bubbles;
        }

        /// <summary>
        /// Strips the «name said aloud: "…"» wrapper so the whisper bubble shows the line she
        /// actually spoke. The sigil is prompt plumbing; printing it would be showing the user our
        /// wire format.
        /// </summary>
        internal static string UnwrapEcho(string? text)
        {
            var body = text ?? string.Empty;
            var match = Regex.Match(body, "^«[^:]*:\\s*\"(.*)\"»$", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : body.Trim('«', '»').Trim();
        }

        private void RebuildThread()
        {
            _turns.Clear();
            var brain = App.Brain;
            if (brain != null && CompanionBrain.ShouldRoute(brain))
            {
                foreach (var bubble in ProjectThread(brain.Session.Turns)) _turns.Add(bubble);
            }
            Raise(nameof(Turns));
            Raise(nameof(FooterCopy));
        }

        private void RefreshLastHeard()
        {
            var brain = App.Brain;
            var last = brain?.Session.Turns.LastOrDefault(t => t.Kind == TurnKind.UserChat);
            _lastHeardUtc = last?.Utc;
            LastHeardCopy = _lastHeardUtc == null
                ? string.Empty
                : Loc.GetF("companion_chat_last_heard_fmt", RelativeTime(_lastHeardUtc.Value));
        }

        /// <summary>"just now" / "22m ago" / "2h ago" / "3d ago". Never a raw timestamp.</summary>
        internal static string RelativeTime(DateTime utc)
        {
            var delta = DateTime.UtcNow - utc;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
            if (delta.TotalMinutes < 1) return Loc.Get("companion_chat_time_now");
            if (delta.TotalHours < 1) return Loc.GetF("companion_chat_time_minutes", (int)delta.TotalMinutes);
            if (delta.TotalDays < 1) return Loc.GetF("companion_chat_time_hours", (int)delta.TotalHours);
            return Loc.GetF("companion_chat_time_days", (int)delta.TotalDays);
        }

        // =====================================================================================
        //  sending
        // =====================================================================================

        private void Send()
        {
            var text = (Draft ?? string.Empty).Trim();
            if (text.Length == 0 || !CanSend) return;

            var brain = App.Brain;
            if (brain == null || !CompanionBrain.ShouldRoute(brain))
            {
                // The zone should already be Dormant here and the row hidden; re-checking costs
                // nothing and means a race with the kill switch cannot swallow someone's line.
                Sync();
                return;
            }

            Draft = string.Empty;
            IsThinking = true;
            _ = SendAsync(brain, text);
        }

        private async System.Threading.Tasks.Task SendAsync(CompanionBrain brain, string text)
        {
            AiReplyResult? result = null;
            try
            {
                // Same entry point as the tube box: same moderation spine, same single-flight, same
                // "still thinking" phrase on queue overflow. This zone adds no path of its own.
                result = await brain.ChatAsync(text).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Companion room: chat send failed");
            }

            // Fire-and-forget continuation: never touch UI without a live dispatcher (Known Issues #6).
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                IsThinking = false;
                Sync();
                ShowNonThreadReply(result);
            }), DispatcherPriority.Normal);
        }

        /// <summary>
        /// A refusal or a canned fallback never lands in the turn log (P2/H5 rollback), so it would
        /// vanish from this card entirely — the user would watch their line disappear and get
        /// nothing back at all.
        ///
        /// <para>Both go to the tube bubble, which is the surface every other caller already uses
        /// for them and the one that owns the POLICY badge. A refusal goes through
        /// <c>ShowModerationRefusalBubble</c> unchanged, so the moderation UX this zone inherits is
        /// literally the same code path the chat box uses; a fallback goes through
        /// <c>GigglePriority(aiGenerated:false)</c>, so the pink AI badge stays off it.</para>
        /// </summary>
        private static void ShowNonThreadReply(AiReplyResult? result)
        {
            if (result == null || result.IsAiGenerated) return;

            CompanionRuntimeContext.Guarded(() =>
            {
                var tube = App.AvatarWindow;
                if (tube == null) return;

                if (result.Refusal != null)
                {
                    tube.ShowModerationRefusalBubble(result.Refusal.Source);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(result.Text))
                    tube.GigglePriority(result.Text, aiGenerated: false);
            }, "surface non-thread reply");
        }

        // =====================================================================================
        //  the veil's staged thread
        // =====================================================================================

        /// <summary>
        /// Static mock bubbles for the locked state. Never live content: the blur runs on this tiny
        /// non-scrolling panel only, which is what keeps the flagship teaser cheap.
        /// </summary>
        private static IReadOnlyList<IChatBubbleVm> BuildTeaser() => new IChatBubbleVm[]
        {
            new CompanionChatBubble(CompanionBubbleKind.You,
                Loc.Get("companion_chat_teaser_you")),
            new CompanionChatBubble(CompanionBubbleKind.Her,
                Loc.Get("companion_chat_teaser_her"), isAi: true)
        };
    }
}
