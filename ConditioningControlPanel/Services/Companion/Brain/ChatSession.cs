using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.AIService;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// How much conversation a single call is allowed to carry. Doc 01 §1.3.
    ///
    /// Train 1 does simple truncation only — no rolling summary, no LLM compaction (those are
    /// Train 4). Turns that don't fit are dropped from the front and are gone from the prompt;
    /// they remain in <see cref="ChatSession.Turns"/> and on disk.
    /// </summary>
    /// <param name="TokenBudget">Approximate history tokens (chars/4) the window may spend.</param>
    /// <param name="MaxMessages">
    /// Hard cap on messages in the window, EXCLUDING the system prompt. The cloud proxy accepts up
    /// to 50 and trims to its own char budget server-side; we self-cap well under that so the
    /// server trim is never load-bearing.
    /// </param>
    /// <param name="MaxBarkEchoes">
    /// Only the most recent N <see cref="TurnKind.BarkEcho"/> turns may enter a window. They are
    /// flavor, not content — an unbounded stream of them would crowd out real dialogue.
    /// </param>
    /// <param name="MaxDialogueTurns">
    /// Cap on UserChat + AssistantChat turns. Ambient calls set this low (~4): carrying the whole
    /// chat thread into a one-line reaction is what made past "watch X~" lines act as few-shot bait
    /// and fixate the model on one title.
    /// </param>
    /// <param name="MaxAmbientTurns">
    /// Cap on <see cref="TurnKind.AmbientEvent"/> + <see cref="TurnKind.AmbientReply"/> turns.
    /// Awareness fires on a ~10s cooldown, so an hour of browsing produces enough event/reply pairs
    /// to fill the whole chat budget on their own and push the actual conversation out of the window.
    /// Older ambient turns are SKIPPED rather than ending the walk, so real dialogue behind them is
    /// still reachable — the same rule bark echoes follow.
    /// </param>
    public sealed record ChatWindowSpec(
        int TokenBudget,
        int MaxMessages = ChatSession.MaxWindowMessages,
        int MaxBarkEchoes = ChatSession.MaxBarkEchoesInWindow,
        int MaxDialogueTurns = int.MaxValue,
        int MaxAmbientTurns = ChatSession.MaxAmbientTurnsInChatWindow)
    {
        /// <summary>Chat-box window: ~1,600 tokens of history, self-capped at 40 messages.</summary>
        public static ChatWindowSpec Chat { get; } = new(ChatSession.ChatHistoryTokenBudget);

        /// <summary>Ambient window: ~300 tokens, last ~4 dialogue turns (doc 01 §1.4).</summary>
        public static ChatWindowSpec Ambient { get; } = new(
            ChatSession.AmbientHistoryTokenBudget,
            MaxDialogueTurns: ChatSession.AmbientDialogueTurnLimit,
            MaxAmbientTurns: ChatSession.MaxAmbientTurnsInAmbientWindow);
    }

    /// <summary>
    /// The companion's in-memory typed turn log plus the pure prompt-window assembly on top of it.
    /// Owned by <see cref="CompanionBrain"/>; every provider shares this one thread, so switching
    /// cloud ↔ Ollama mid-conversation keeps the thread.
    ///
    /// <para>Everything here is pure and synchronous — no I/O, no clock beyond what callers pass in —
    /// so window budgeting is unit-testable headlessly. Persistence lives in
    /// <see cref="CompanionSessionStore"/>.</para>
    ///
    /// <para>Thread safety: all mutation goes through a private lock, because bark echoes arrive on
    /// whatever thread <c>BarkService</c> fired on while a chat call may be in flight.</para>
    /// </summary>
    public sealed class ChatSession
    {
        /// <summary>Self-cap on window size, excluding the system prompt (server accepts 50).</summary>
        public const int MaxWindowMessages = 40;

        /// <summary>Approximate history tokens carried by a chat-box call.</summary>
        public const int ChatHistoryTokenBudget = 1600;

        /// <summary>Approximate history tokens carried by an ambient reaction call.</summary>
        public const int AmbientHistoryTokenBudget = 300;

        /// <summary>Only the last N bark echoes may ever enter a prompt window (doc 01 §3.3).</summary>
        public const int MaxBarkEchoesInWindow = 5;

        /// <summary>Dialogue turns an ambient window may carry (doc 01 §1.4).</summary>
        public const int AmbientDialogueTurnLimit = 4;

        /// <summary>Ambient event/reply turns a CHAT window may carry — 3 pairs of recent context.</summary>
        public const int MaxAmbientTurnsInChatWindow = 6;

        /// <summary>Ambient event/reply turns an AMBIENT window may carry — 4 pairs.</summary>
        public const int MaxAmbientTurnsInAmbientWindow = 8;

        private readonly object _lock = new();
        private readonly List<CompanionTurn> _turns = new();

        /// <summary>
        /// Turns restored from a previous app launch (session.json, or the one-time
        /// local_chat_history.json import). Non-zero means persistent memory is in play, which is
        /// what the <c>she_remembers</c> achievement keys off.
        /// </summary>
        public int RestoredTurnCount { get; private set; }

        /// <summary>Snapshot of the full log, oldest first.</summary>
        public IReadOnlyList<CompanionTurn> Turns
        {
            get { lock (_lock) return _turns.ToList(); }
        }

        /// <summary>Total turns currently held.</summary>
        public int Count { get { lock (_lock) return _turns.Count; } }

        /// <summary>
        /// The brain's single token estimator: chars / 4. Deliberately crude and deliberately the
        /// SAME arithmetic <see cref="AiMeter"/> logs, so a budget decision and a meter line can be
        /// compared without conversion.
        /// </summary>
        public static int ApproxTokens(string? text) =>
            string.IsNullOrEmpty(text) ? 0 : text.Length / 4;

        /// <summary>Appends a new turn and returns it.</summary>
        public CompanionTurn Append(TurnKind kind, string text, string? mood = null,
            bool voiced = false, DateTime? utc = null)
        {
            var turn = CompanionTurn.Create(kind, text, mood, voiced, utc);
            Append(turn);
            return turn;
        }

        /// <summary>Appends an already-built turn (used by the loader and by tests).</summary>
        public void Append(CompanionTurn turn)
        {
            if (turn == null) return;
            lock (_lock) _turns.Add(turn);
        }

        /// <summary>
        /// Seeds the log from disk. Marks the seeded turns as restored so
        /// <see cref="RestoredTurnCount"/> reports prior-session memory. Replaces any existing
        /// content — only ever called once, at brain construction.
        /// </summary>
        public void Restore(IEnumerable<CompanionTurn> turns)
        {
            lock (_lock)
            {
                _turns.Clear();
                if (turns != null) _turns.AddRange(turns.Where(t => t != null));
                RestoredTurnCount = _turns.Count;
            }
        }

        /// <summary>
        /// Removes a specific turn by identity. This is the P2/H5 rollback primitive: a moderation
        /// refusal must leave the log exactly as it was, and removing "the last turn" positionally
        /// would be wrong if a bark echo landed in between.
        /// </summary>
        public bool Remove(CompanionTurn turn)
        {
            if (turn == null) return false;
            lock (_lock)
            {
                for (int i = _turns.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(_turns[i], turn) || _turns[i].Id == turn.Id)
                    {
                        _turns.RemoveAt(i);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Drops everything, including the restored-turn marker. Backs "forget everything".</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _turns.Clear();
                RestoredTurnCount = 0;
            }
        }

        /// <summary>The dialogue subset, in order — exactly what is allowed to reach disk.</summary>
        public IReadOnlyList<CompanionTurn> DialogueTurns()
        {
            lock (_lock) return _turns.Where(t => t.IsDialogue).ToList();
        }

        /// <summary>
        /// Assembles the prompt window: the most recent turns that fit <paramref name="spec"/>,
        /// oldest first. Pure function of the log — no side effects, safe to call repeatedly.
        ///
        /// <para>Rules, applied while walking BACKWARDS from the newest turn:</para>
        /// <list type="number">
        ///   <item>SystemNotes never enter a window (they are housekeeping, not conversation).</item>
        ///   <item>At most <see cref="ChatWindowSpec.MaxBarkEchoes"/> bark echoes; older ones are skipped
        ///         but do NOT stop the walk — real dialogue behind them is still reachable.</item>
        ///   <item>At most <see cref="ChatWindowSpec.MaxAmbientTurns"/> ambient event/reply turns, same
        ///         skip-don't-stop rule: awareness fires often enough to fill the whole budget by
        ///         itself and evict the conversation.</item>
        ///   <item>At most <see cref="ChatWindowSpec.MaxDialogueTurns"/> dialogue turns; hitting the cap
        ///         STOPS the walk, because anything older is older dialogue.</item>
        ///   <item>Stop on the message cap or when the next turn would exceed the token budget.</item>
        ///   <item>The newest eligible turn is always included even if it alone exceeds the budget —
        ///         a window that drops the thing being reacted to is useless.</item>
        /// </list>
        /// Truncation only: Train 1 ships no summary of what fell off the front (Train 4 does).
        /// </summary>
        public IReadOnlyList<CompanionTurn> BuildWindow(ChatWindowSpec spec)
        {
            spec ??= ChatWindowSpec.Chat;

            List<CompanionTurn> source;
            lock (_lock) source = _turns.ToList();

            var picked = new List<CompanionTurn>();
            int tokens = 0, barkEchoes = 0, dialogue = 0, ambient = 0;

            static bool IsAmbient(CompanionTurn t) =>
                t.Kind is TurnKind.AmbientEvent or TurnKind.AmbientReply;

            for (int i = source.Count - 1; i >= 0; i--)
            {
                var turn = source[i];
                if (turn.Kind == TurnKind.SystemNote) continue;

                if (turn.Kind == TurnKind.BarkEcho)
                {
                    if (barkEchoes >= spec.MaxBarkEchoes) continue;
                }
                else if (IsAmbient(turn))
                {
                    if (ambient >= spec.MaxAmbientTurns) continue;
                }
                else if (turn.IsDialogue)
                {
                    if (dialogue >= spec.MaxDialogueTurns) break;
                }

                if (picked.Count >= spec.MaxMessages) break;

                // Sigils are part of what we pay for, so budget against the wire text.
                int cost = ApproxTokens(turn.WireText);
                if (picked.Count > 0 && tokens + cost > spec.TokenBudget) break;

                picked.Add(turn);
                tokens += cost;
                if (turn.Kind == TurnKind.BarkEcho) barkEchoes++;
                else if (IsAmbient(turn)) ambient++;
                else if (turn.IsDialogue) dialogue++;
            }

            picked.Reverse();
            return picked;
        }

        /// <summary>Maps a window onto transport messages, preserving order.</summary>
        public static IReadOnlyList<ChatMessage> ToMessages(IEnumerable<CompanionTurn> turns) =>
            (turns ?? Enumerable.Empty<CompanionTurn>())
                .Where(t => t != null && t.Kind != TurnKind.SystemNote)
                .Select(t => t.ToMessage())
                .ToList();
    }
}
