using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// Classification of a durable fact. Doc 01 §2.2.
    /// <see cref="Boundary"/> is load-bearing, not decorative: for this product a remembered
    /// "stop teasing me about X" is consent hygiene, so boundaries get the highest injection
    /// priority and are never evicted.
    /// </summary>
    public enum MemoryFactKind
    {
        Identity,
        Preference,
        Boundary,
        Joke,
        Goal,
        Event
    }

    /// <summary>
    /// One durable thing the companion knows about the user. Doc 01 §2.2 schema.
    /// </summary>
    /// <param name="Salience">0..1 relevance weight; decays on non-reference.</param>
    /// <param name="Pinned">User-pinned in the "What she knows about you" panel — never evicted.</param>
    /// <param name="Source">chat | app | user-edited.</param>
    public sealed record MemoryFact(
        string Id,
        string Text,
        MemoryFactKind Kind,
        double Salience,
        DateTime Created,
        DateTime? LastUsed,
        int Uses,
        bool Pinned,
        string Source)
    {
        public const string SourceChat = "chat";
        public const string SourceApp = "app";
        public const string SourceUserEdited = "user-edited";
    }

    /// <summary>
    /// The companion's durable model of the user: a deterministic profile block (level, streak,
    /// archetype, favourite features — all free, the app already knows them) plus a fact list.
    ///
    /// <para><b>Train 1 scope.</b> The interface is final; the shipped implementation
    /// (<see cref="MemoryStore"/>) is a shell. There is NO LLM extraction and NO persistence in
    /// Train 1 — the deterministic <c>MemorySignalWriter</c> and the "What she knows about you"
    /// panel are the memory agent's work on its own branch, and the LLM extractor is Train 4.
    /// Everything here is designed so a caller cannot tell the difference beyond an empty
    /// injection block.</para>
    ///
    /// <para>Implementations must never throw: a broken memory file must degrade the companion to
    /// "amnesiac but working", never to "chat is down".</para>
    /// </summary>
    public interface IMemoryStore
    {
        /// <summary>
        /// The memory block for the prompt's DYNAMIC TAIL — never the cached prefix, since it
        /// changes per call and would bust provider prompt caching.
        /// Must fit <paramref name="tokenBudget"/> (chars/4 estimate) and returns null when there is
        /// nothing worth injecting, so the assembler emits no empty section.
        /// Priority order once populated: profile line → boundaries → top-K facts by salience×recency.
        /// </summary>
        string? GetInjectionBlock(int tokenBudget);

        /// <summary>
        /// Records a deterministic app signal (level, streak days, session count, archetype,
        /// favourite feature…). Zero LLM cost — this is the ~60% of "she knows me" that is free.
        /// Unknown keys are accepted and stored; null clears the key.
        /// </summary>
        void UpdateProfileSignal(string key, object? value);

        /// <summary>Read-only view of the current profile signals.</summary>
        IReadOnlyDictionary<string, object?> Profile { get; }

        /// <summary>All facts, most salient first.</summary>
        IReadOnlyList<MemoryFact> GetFacts();

        /// <summary>
        /// Adds a fact. Callers that source text from a model MUST have passed it through
        /// <c>ModerationGuard.CheckOutput</c> first — the memory file must never accumulate content
        /// the display path would refuse, or a later prompt would re-launder it.
        /// </summary>
        MemoryFact AddFact(string text, MemoryFactKind kind, double salience = 0.5,
            string source = MemoryFact.SourceChat);

        /// <summary>Edits a fact in place. Returns false when the id is unknown.</summary>
        bool UpdateFact(string id, string? text = null, double? salience = null, bool? pinned = null);

        /// <summary>Deletes a fact ("forget that"). Returns false when the id is unknown.</summary>
        bool ForgetFact(string id);

        /// <summary>
        /// Marks a fact as used — feeds the recency half of salience×recency ranking and the
        /// running-gag cooldown. No-op for unknown ids.
        /// </summary>
        void NoteFactUsed(string id);

        /// <summary>
        /// Forgets everything: profile signals and facts. Backs the panel's "Forget everything"
        /// button. Must leave the store usable, not disposed.
        /// </summary>
        void Wipe();
    }
}
