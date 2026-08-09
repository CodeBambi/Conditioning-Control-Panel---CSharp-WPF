using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// A one-shot thing that happened, compressed into a line the companion can react to.
    /// Doc 01 §1.2 (<see cref="TurnKind.AmbientEvent"/>).
    /// </summary>
    /// <param name="Descriptor">
    /// Plain text, no sigil — "user has been on YouTube 22m", "finished mandatory video 'Bambi Bae'",
    /// "level up → 41". <see cref="CompanionTurn.WireText"/> adds the «event: …» wrapper.
    /// </param>
    public sealed record CompanionEvent(string Descriptor)
    {
        /// <summary>Ambient descriptors are one line. Longer ones are truncated, not rejected.</summary>
        public const int MaxTokens = 25;

        /// <summary>chars/4, so 25 tokens ≈ 100 chars.</summary>
        public const int MaxChars = MaxTokens * 4;

        /// <summary>
        /// The clamped, single-line descriptor actually stored on the turn. Whitespace runs
        /// (including the newlines a caller might interpolate from a window title) collapse to one
        /// space, so an event can never look like several messages on the wire.
        /// </summary>
        public string Normalized()
        {
            var text = System.Text.RegularExpressions.Regex
                .Replace(Descriptor ?? string.Empty, @"\s+", " ").Trim();
            if (text.Length <= MaxChars) return text;
            return text[..MaxChars].TrimEnd() + "…";
        }
    }

    /// <summary>
    /// The companion's conversational spine (<c>App.Brain</c>). Owns the turn log, assembles every
    /// request, and is the only thing that talks to <see cref="IAiService.SendAsync"/>. Providers
    /// become dumb transports, so history no longer lives inside the local provider and switching
    /// cloud ↔ Ollama mid-conversation keeps the thread.
    ///
    /// <para><b>Moderation.</b> The spine is untouched and lives where it always did — inside the
    /// transport's <c>SendAsync</c>, which runs <c>CheckInput</c> on the newest user-role message and
    /// <c>CheckOutput</c> on the reply, records to <c>ModerationLog</c>, and escalates
    /// <c>ModerationCounter</c> only for interactive turns. The brain deliberately does NOT re-check
    /// the same text: two checks would double the CCBill log and halve the number of hits needed to
    /// trip the user-facing cooldown. What the brain owns is the P2/H5 half of the invariant —
    /// <b>a refused turn is rolled back out of the log and never reaches disk.</b></para>
    ///
    /// <para><b>Kill switch.</b> <see cref="IsEnabled"/> reads <c>AppSettings.UseCompanionBrain</c>.
    /// False routes every call site back to the legacy stateless path; the brain is still constructed
    /// (so it can hold a session for diagnostics) but nothing calls it.</para>
    ///
    /// <para><b>Train 1 scope.</b> Simple truncation windowing. No LLM memory extraction, no
    /// compaction/summaries, no relationship stages, no daily mood, no voice arbiter — those are
    /// Trains 2 and 4.</para>
    /// </summary>
    public sealed class CompanionBrain : IDisposable
    {
        private readonly IAiService _transport;
        private readonly IPromptAssembler _assembler;
        private readonly ICompanionSessionStore _store;

        // The linkable media pool, exactly as the stable prefix lists it. Injected so the
        // recommendation scan is testable without standing up BambiSprite and the whole mod stack.
        private readonly Func<IReadOnlyList<string>> _mediaTitles;

        // Single-flight (doc 01 §1.6, promoted from LocalAiService): exactly one LLM call in flight;
        // a second USER send queues at depth 1; ambient requests are DROPPED when busy. Barks are
        // unaffected — they never wait on the LLM.
        private readonly SemaphoreSlim _gate = new(1, 1);
        private volatile bool _isProcessing;
        private volatile bool _isUserQueued;

        private BarkService? _barks;
        private Action<BarkRule, string>? _barkHandler;

        private bool _memoryRecallSignaled;
        private bool _disposed;

        // True when WE built the memory store, and are therefore the thing that must shut it down.
        // An injected store belongs to its caller (tests own theirs); disposing someone else's store
        // out from under them would be the more surprising bug of the two.
        private readonly bool _ownsMemory;

        private static readonly Random _random = new();

        public CompanionBrain(
            IAiService transport,
            IPromptAssembler? assembler = null,
            IMemoryStore? memory = null,
            ICompanionSessionStore? store = null,
            RecentRecommendations? recommendations = null,
            Func<IReadOnlyList<string>>? mediaTitles = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _ownsMemory = memory == null;
            Memory = memory ?? new MemoryStore();
            Recommendations = recommendations ?? new RecentRecommendations();
            _assembler = assembler ?? new PromptAssembler(Memory, Recommendations);
            _store = store ?? new CompanionSessionStore();
            _mediaTitles = mediaTitles ?? DefaultMediaTitles;

            Session = new ChatSession();

            var snapshot = _store.Load();
            if (snapshot.Turns.Count > 0)
            {
                Session.Restore(snapshot.Turns);
                App.Logger?.Information(
                    "CompanionBrain: session restored ({Count} turn(s), imported={Imported})",
                    snapshot.Turns.Count, snapshot.ImportedFromLegacy);
            }
        }

        /// <summary>
        /// The pool titles the stable prefix actually offered her. Never throws: a prompt-stack
        /// failure costs the exclusion line, not the reply.
        /// </summary>
        private static IReadOnlyList<string> DefaultMediaTitles()
        {
            try { return BambiSprite.StableMediaTitles(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("CompanionBrain: media title lookup failed: {Error}", ex.Message);
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Master routing switch. False = today's legacy stateless behaviour on every call site.
        /// Read at each call site rather than cached so flipping it takes effect immediately.
        /// </summary>
        public static bool IsEnabled => App.Settings?.Current?.UseCompanionBrain != false;

        /// <summary>
        /// The routing predicate EVERY call site should use, so the kill switch and the
        /// "brain failed to construct" fallback can never drift apart across the call sites the
        /// other Train 1 branches migrate. False means: take the legacy stateless
        /// <see cref="IAiService"/> path, unchanged.
        /// </summary>
        public static bool ShouldRoute(CompanionBrain? brain) => ShouldRoute(brain, IsEnabled);

        /// <summary>Testable core of <see cref="ShouldRoute(CompanionBrain?)"/>.</summary>
        internal static bool ShouldRoute(CompanionBrain? brain, bool killSwitchOn) =>
            brain != null && killSwitchOn;

        /// <summary>
        /// True while an LLM call owns the single-flight gate. Ambient sources that do NOT route
        /// through <see cref="ChatAsync"/>/<see cref="ReactAsync"/> — Train 2's awareness leg has its
        /// own small prompt and its own transport call — must ask this and stand down, or the
        /// "ambient requests are dropped when busy" half of the contract only holds for the callers
        /// that happen to take the gate.
        /// </summary>
        public bool IsBusy => _isProcessing;

        /// <summary>The live turn log. Read-only for callers in Train 1.</summary>
        public ChatSession Session { get; }

        /// <summary>The durable user model. A shell in Train 1 — see <see cref="MemoryStore"/>.</summary>
        public IMemoryStore Memory { get; }

        /// <summary>Titles she suggested recently, injected as an exclusion line.</summary>
        public RecentRecommendations Recommendations { get; }

        /// <summary>Turns restored from a previous launch — non-zero means persistent memory is live.</summary>
        public int RestoredTurnCount => Session.RestoredTurnCount;

        // ===================== chat =====================

        /// <summary>
        /// The chat box. Appends the user's turn, sends the assembled window, and on success appends
        /// the reply and persists the dialogue.
        ///
        /// <para>Returns the same <see cref="AiReplyResult"/> shape the legacy path returned, with the
        /// same badge semantics: <c>IsAiGenerated</c> is true only for genuine model text, so the pink
        /// AI badge still cannot appear over a canned fallback or a refusal.</para>
        ///
        /// <para>P2/H5: if the guard refuses (either direction), the user turn is removed from the log
        /// and nothing is written to disk — the file stays at its prior known-clean state.</para>
        /// </summary>
        public async Task<AiReplyResult> ChatAsync(string userText, CancellationToken cancellationToken = default)
        {
            var input = (userText ?? string.Empty).Trim();
            if (input.Length == 0)
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);

            // Depth-1 user queue: a third concurrent send gets a "still thinking" phrase instead of
            // piling up requests behind the semaphore.
            if (_isUserQueued)
            {
                App.Logger?.Debug("CompanionBrain: user send dropped (one already queued)");
                return new AiReplyResult(GetThinkingPhrase(), IsAiGenerated: false, Refusal: null);
            }

            _isUserQueued = true;
            try
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _isUserQueued = false;
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
            }

            _isUserQueued = false;
            _isProcessing = true;

            var userTurn = Session.Append(TurnKind.UserChat, input);

            // EMIT hook for the app's "the user just talked to her" signal (#877). It belongs
            // HERE, in the funnel every chat surface goes through, not at one call site: the
            // signal used to be raised only by the tube's legacy send handler, so Her Room's
            // chat zone — and any future surface — never counted, and the companion-chat
            // achievements sat at zero for anyone who chats from the room. Raised past the
            // guards above (blank input, depth-1 queue refusal) so only a send that genuinely
            // takes the gate counts, and at send time rather than after the reply so bark
            // timing and MemorySignalWriter's relationship counter are unchanged. Call sites
            // that do NOT reach here (legacy stateless path, no-AI preset path) still raise it
            // themselves — see AvatarTubeWindow.SendChatMessageAsync.
            try { App.Companion?.NotifyUserMessageSent(); }
            catch (Exception ex) { App.Logger?.Debug("CompanionBrain: user-message signal failed: {Error}", ex.Message); }
            try
            {
                var request = _assembler.BuildRequest(AiPurpose.Chat, Session, input);
                // The effects envelope is not prose: JSON scaffolding + effects blow through
                // the 100-token chat cap and get guillotined mid-string. Size the budget to
                // what we asked the model to produce. See AiCallOptions.ChatWithEffects.
                var effectsOn = App.Settings?.Current?.CompanionPrompt?.AllowAiToControlEffects == true;
                var result = await _transport
                    .SendAsync(request.Messages,
                        effectsOn ? AiCallOptions.ChatWithEffects : AiCallOptions.Chat,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (result.Refusal != null)
                {
                    // P2/H5 rollback: the prohibited turn leaves no trace in memory or on disk.
                    Session.Remove(userTurn);
                    return result;
                }

                if (!result.IsAiGenerated)
                {
                    // Canned fallback / login hint / transport failure. Roll the user turn back the
                    // way the legacy path always did ("don't poison history with an unanswered
                    // turn"): with Ollama down a user retries, and a run of consecutive user
                    // messages with no answers is both wasted history budget and terrible context
                    // once the provider comes back. The bubble transcript the user actually reads is
                    // AvatarTubeWindow.ChatHistory, which is untouched by this.
                    Session.Remove(userTurn);
                    return result;
                }

                // Live 0806: small models imitate the bark-echo sigil and wrap their own replies in
                // «X said aloud: "…"». Unwrap before the text reaches the bubble, history, or disk.
                var chatText = AiTextHygiene.UnwrapSpokenSigil(result.Text);

                // Live 0806: and they invent URLs when asked for a video they have no link for.
                // Strip before the reply is appended — a fabricated link left in the window teaches
                // the next turn to write another one.
                chatText = AiTextHygiene.StripUnsanctionedLinks(chatText);

                // Rewrite invented titles to the nearest real pool video BEFORE the turn is
                // stored: the persisted history is the model's few-shot bait, so leaving her
                // inventions in it is how one made-up title became a fixation (0807). Rewriting
                // here heals bubble, chip, history and future prompts in one place.
                chatText = AiTextHygiene.RewriteOffPoolTitles(chatText);

                if (chatText.Length == 0)
                {
                    // Nothing but sigil shell, or nothing but invented links. Either way there is no
                    // reply left to show: same treatment as a canned fallback.
                    Session.Remove(userTurn);
                    return new AiReplyResult(GetFallbackPhrase(), IsAiGenerated: false, Refusal: null);
                }
                if (!string.Equals(chatText, result.Text, StringComparison.Ordinal))
                    result = result with { Text = chatText };

                Session.Append(TurnKind.AssistantChat, result.Text);
                NoteRecommendedTitles(result.Text);
                PersistAsync();
                SignalMemoryRecallIfDue();
                return result;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionBrain: chat turn failed");
                return new AiReplyResult(GetFallbackPhrase(), IsAiGenerated: false, Refusal: null);
            }
            finally
            {
                _isProcessing = false;
                try { _gate.Release(); } catch (ObjectDisposedException) { }
            }
        }

        // ===================== ambient =====================

        /// <summary>
        /// An ambient moment. Appends the event, sends it with the SMALL window (~300 tokens, last ~4
        /// dialogue turns) plus the anti-repeat line, and appends the reply as
        /// <see cref="TurnKind.AmbientReply"/> — so if the user then opens the chat box and asks
        /// "why'd you say that?", she knows. Neither half is persisted: see that enum member.
        ///
        /// <para>Dropped silently when a call is already in flight: an ambient line is worth less than
        /// the chat reply it would queue behind.</para>
        ///
        /// <para>Returns a non-AI empty result when there is nothing to say. Callers fall back to a
        /// preset phrase exactly as they do today, and the badge stays off.</para>
        /// </summary>
        public async Task<AiReplyResult> ReactAsync(CompanionEvent evt, CancellationToken cancellationToken = default)
        {
            var descriptor = evt?.Normalized() ?? string.Empty;
            if (descriptor.Length == 0)
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);

            if (_isProcessing || _isUserQueued)
            {
                App.Logger?.Debug("CompanionBrain: ambient reaction dropped (busy)");
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
            }

            if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                App.Logger?.Debug("CompanionBrain: ambient reaction dropped (gate held)");
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
            }

            _isProcessing = true;
            var eventTurn = Session.Append(TurnKind.AmbientEvent, descriptor);
            try
            {
                var request = _assembler.BuildRequest(AiPurpose.Reaction, Session, descriptor);
                var result = await _transport
                    .SendAsync(request.Messages, AiCallOptions.Reaction, cancellationToken)
                    .ConfigureAwait(false);

                if (result.Refusal != null || !result.IsAiGenerated)
                {
                    // Nothing usable came back. Drop the event turn so a dead moment doesn't sit in
                    // the window shaping the next reply (and, on a refusal, so it never persists).
                    Session.Remove(eventTurn);
                    return result.Refusal != null
                        ? result
                        : new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
                }

                var reactionText = AiTextHygiene.UnwrapSpokenSigil(result.Text);
                reactionText = AiTextHygiene.StripUnsanctionedLinks(reactionText);
                if (reactionText.Length == 0)
                {
                    Session.Remove(eventTurn);
                    return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
                }
                if (!string.Equals(reactionText, result.Text, StringComparison.Ordinal))
                    result = result with { Text = reactionText };

                // AmbientReply, not AssistantChat: it shapes the live window but never reaches disk.
                // See TurnKind.AmbientReply for why persisting the reply without its event is worse
                // than persisting neither. Nothing dialogue-shaped changed, so there is nothing to
                // persist here at all.
                Session.Append(TurnKind.AmbientReply, result.Text);
                NoteRecommendedTitles(result.Text);
                SignalMemoryRecallIfDue();
                return result;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionBrain: ambient reaction failed");
                Session.Remove(eventTurn);
                return new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null);
            }
            finally
            {
                _isProcessing = false;
                try { _gate.Release(); } catch (ObjectDisposedException) { }
            }
        }

        /// <summary>Convenience overload for call sites that only have a descriptor string.</summary>
        public Task<AiReplyResult> ReactAsync(string descriptor, CancellationToken cancellationToken = default)
            => ReactAsync(new CompanionEvent(descriptor), cancellationToken);

        // ===================== barks =====================

        /// <summary>
        /// Subscribes to <see cref="BarkService.BarkSpoken"/> so the LLM knows what her recorded voice
        /// just said. Called from <c>App.OnStartup</c> once both services exist. Idempotent.
        /// </summary>
        public void AttachBarkSource(BarkService? barks)
        {
            if (barks == null || ReferenceEquals(barks, _barks)) return;
            DetachBarkSource();

            _barks = barks;
            _barkHandler = OnBarkSpoken;
            barks.BarkSpoken += _barkHandler;
        }

        /// <summary>Unsubscribes from the bark source. Safe to call when never attached.</summary>
        public void DetachBarkSource()
        {
            if (_barks != null && _barkHandler != null) _barks.BarkSpoken -= _barkHandler;
            _barks = null;
            _barkHandler = null;
        }

        private void OnBarkSpoken(BarkRule rule, string line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                var speaker = App.Mods?.GetCompanionName() ?? "she";
                // BarkEchoes are flavor: capped at 5 per window and never persisted (they replay from
                // bark_rules.json anyway, and they say nothing about the user).
                Session.Append(TurnKind.BarkEcho, CompanionTurn.FormatBarkEcho(speaker, line),
                    mood: rule?.Mood, voiced: true);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("CompanionBrain: bark echo failed: {Error}", ex.Message);
            }
        }

        // ===================== misc =====================

        /// <summary>
        /// Records a title she just suggested so it lands in the exclusion line for the next 24h.
        /// </summary>
        public void NoteRecommendation(string? title) => Recommendations.Note(title);

        /// <summary>
        /// Scans a delivered reply for titles from our own media pool and bans them for the next 24h.
        ///
        /// <para>This is the PRODUCER half of the structural anti-fixation fix (doc 01 §3.4). The
        /// per-call Fisher-Yates shuffle it replaced is genuinely gone — the stable prefix lists the
        /// pool alphabetically and resolves its <c>{{VIDEO}}</c> examples once per launch — so without
        /// a producer <see cref="RecentRecommendations.BuildExclusionLine"/> returns null forever and
        /// the only anti-fixation left is one line of prose. Train 1 also newly puts her own previous
        /// replies into the window, which is the exact condition that produced the fixation bug.</para>
        ///
        /// <para>Recognised titles come from our own list. When the reply named NOTHING we
        /// recognise, its quoted spans are noted instead — model-authored, so they are length-
        /// capped and newline-stripped before they can reach the exclusion line (without this,
        /// invented titles were exempt from the ban and became the fixation loop).</para>
        /// </summary>
        internal void NoteRecommendedTitles(string? replyText)
        {
            var text = replyText ?? string.Empty;
            if (text.Length == 0) return;

            try
            {
                var titles = _mediaTitles() ?? Array.Empty<string>();
                var noted = new List<string>();
                // Longest first, and a shorter title contained in an already-noted one is skipped, so
                // "Bambi Bae" doesn't burn a slot every time "Bambi Bae 2" is named.
                foreach (var title in titles
                             .Where(t => !string.IsNullOrWhiteSpace(t))
                             .OrderByDescending(t => t.Length))
                {
                    if (text.IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (noted.Any(n => n.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    noted.Add(title);
                    Recommendations.Note(title);
                }

                // She invents titles more often than she lands real ones — and an invented title
                // was never noted, so the exclusion line never listed it and she repeated it
                // forever (observed 2026-08-07: three invented titles said 3x each; the two real
                // ones once each). Ban what she SAID, not just what we recognised. These strings
                // are model-authored and flow into future prompts via the exclusion line, so they
                // are bounded hard: quoted spans only, length-capped, newlines stripped, two per
                // reply.
                if (noted.Count == 0)
                {
                    int banned = 0;
                    foreach (var (start, length, quoted) in Services.Companion.CompanionTitleMatcher.CandidateSpans(text))
                    {
                        if (banned >= 2) break;
                        // Quoted only: every observed invented title was quoted, and unquoted
                        // Title-Case runs are too often ordinary prose to feed into the prompt.
                        if (!quoted) continue;
                        if (length < Services.Companion.CompanionTitleMatcher.MinSpanLength || length > 60) continue;
                        var span = text.Substring(start, length).Replace('\n', ' ').Replace('\r', ' ').Trim();
                        if (span.Length == 0) continue;
                        Recommendations.Note(span);
                        noted.Add(span);
                        banned++;
                    }
                }

                if (noted.Count > 0)
                    App.Logger?.Debug("CompanionBrain: banned {Count} just-recommended title(s) for 24h", noted.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("CompanionBrain: recommendation scan failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Drops the THREAD and nothing else: the in-memory turn log, session.json, the recommendation
        /// ban list, and the legacy local-Ollama transcript. No memory fact is touched.
        ///
        /// <para>This is the Engine Room's "clear conversation" — the scope the legacy Reset Memory
        /// button had, for the user whose companion is stuck in an old pattern and who does not want
        /// to lose that she is level 41. Its confirmation copy says in nine languages that "what she
        /// knows about you is untouched", so it must not reach
        /// <see cref="MemoryStore.ForgetChatDerived"/>, whose <c>RemoveAll</c> ignores
        /// <c>IsProtected</c> and would take pinned and Boundary facts with it.</para>
        ///
        /// <para>Clearing the live <see cref="Session"/> is the load-bearing half: deleting
        /// session.json alone would be undone by the very next reply, which re-persists the whole
        /// in-RAM log.</para>
        /// </summary>
        public void ForgetThread()
        {
            Session.Clear();
            Recommendations.Clear();
            _store.Wipe();
            _memoryRecallSignaled = false;
            ClearLegacyLocalHistory();
            App.Logger?.Information("CompanionBrain: conversation thread dropped");
        }

        /// <summary>
        /// Forgets the CONVERSATION and everything derived from it: <see cref="ForgetThread"/> plus
        /// the per-mod relationship counters and chat-sourced facts. The deterministic profile
        /// (level, streak, sessions, archetype) survives.
        ///
        /// <para>This is what the chat-memory surfaces call — "Forget everything" on the Lab tab and
        /// turning <c>ChatMemoryEnabled</c> off, which is a privacy promise about dialogue and not
        /// about what level you are. The Engine Room's narrower button calls
        /// <see cref="ForgetThread"/> instead, because its copy promises durable memory is
        /// untouched.</para>
        /// </summary>
        public void ForgetConversation()
        {
            ForgetThread();

            // Memory derived from the conversation goes too — the per-mod turn counters and any
            // chat-sourced facts. Level/streak/archetype are app state, not dialogue, and stay.
            if (Memory is MemoryStore store)
            {
                try { store.ForgetChatDerived(); }
                catch (Exception ex) { App.Logger?.Debug("CompanionBrain: chat-derived wipe failed: {Error}", ex.Message); }
            }

            App.Logger?.Information("CompanionBrain: conversation wiped");
        }

        /// <summary>
        /// Clears <c>LocalAiService</c>'s in-RAM <c>_messages</c> list as well as its file.
        ///
        /// <para><see cref="MemoryStore.Wipe"/> deletes <c>local_chat_history.json</c>, but a file
        /// delete is only half the job while <c>UseCompanionBrain=false</c>: the legacy provider
        /// still holds the whole conversation in memory and re-saves it on the very next reply, so a
        /// "blank slate" that only removed the file was not one. Every wipe scope inherits this from
        /// here so the narrow and the wide wipe cannot drift apart again.</para>
        /// </summary>
        private static void ClearLegacyLocalHistory()
        {
            try { (App.Ai as AiServiceStrategy)?.ClearLocalHistory(); }
            catch (Exception ex) { App.Logger?.Debug("CompanionBrain: legacy local history clear failed: {Error}", ex.Message); }
        }

        /// <summary>
        /// Forgets the conversation: in-memory log, disk file, and the recommendation ban list.
        /// Memory facts go with <see cref="IMemoryStore.Wipe"/>, which this also calls. Backs the
        /// "What she knows about you" panel's all-or-nothing wipe.
        /// </summary>
        public void Forget()
        {
            ForgetConversation();
            Memory.Wipe();
            App.Logger?.Information("CompanionBrain: memory wiped");
        }

        /// <summary>Writes the current dialogue synchronously — used on shutdown.</summary>
        public void Flush() => _store.Save(Session.DialogueTurns());

        /// <summary>
        /// Fire-and-forget persistence so chat latency never pays for disk I/O. Only reached AFTER
        /// output moderation passed (P2/H5), and only dialogue turns are handed over.
        /// </summary>
        private void PersistAsync()
        {
            var dialogue = Session.DialogueTurns();
            _ = Task.Run(() =>
            {
                try { _store.Save(dialogue); }
                catch (Exception ex) { App.Logger?.Debug("CompanionBrain: persist failed: {Error}", ex.Message); }
            });
        }

        /// <summary>
        /// she_remembers: a reply was produced while turns restored from a previous session are in
        /// context — persistent memory surfacing across launches. Signalled once per session through
        /// the existing <c>LocalAiService.PersistentMemoryRecalled</c> event so its two subscribers
        /// (the achievement bridge and the bark trigger) need no change — and so the achievement now
        /// fires for CLOUD users too, which it never could before.
        /// </summary>
        private void SignalMemoryRecallIfDue()
        {
            if (_memoryRecallSignaled || Session.RestoredTurnCount <= 0) return;
            _memoryRecallSignaled = true;
            LocalAiService.SignalPersistentMemoryRecalled();
        }

        private static string GetThinkingPhrase()
        {
            var pool = App.Mods?.GetPhrases("Thinking");
            if (pool == null || pool.Length == 0) return "Hmm... still thinking.";
            return pool[_random.Next(pool.Length)];
        }

        private static string GetFallbackPhrase()
        {
            var pool = App.Mods?.GetPhrases("Idle");
            if (pool == null || pool.Length == 0) return "...";
            return pool[_random.Next(pool.Length)];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DetachBarkSource();
            try { Flush(); } catch { /* shutdown is best-effort */ }

            // Deterministic memory shutdown, ordered before the transport goes away. MemoryStore
            // debounces its writes, so without this the last window's worth of signals would only
            // reach disk via its AppDomain.ProcessExit backstop — which still exists, but runs
            // after WPF has torn everything else down and cannot be relied on for ordering.
            // MemoryStore.Dispose unhooks that handler and does the final SaveNow itself.
            if (_ownsMemory)
            {
                try { (Memory as IDisposable)?.Dispose(); }
                catch (Exception ex) { App.Logger?.Debug("CompanionBrain: memory dispose failed: {Error}", ex.Message); }
            }

            _gate.Dispose();
        }
    }
}
