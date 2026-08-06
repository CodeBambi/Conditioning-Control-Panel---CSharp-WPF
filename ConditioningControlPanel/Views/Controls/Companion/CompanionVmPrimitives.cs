using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    // =====================================================================================
    //  Shared viewmodel primitives for the Companion tab redesign ("Her Room").
    //
    //  Every zone control binds to a small interface (I<Zone>Vm) so it can be rendered from a
    //  design-time mock with no services alive. These are the pieces those interfaces and
    //  mocks are built from. Nothing here touches App.*, settings, or the network.
    // =====================================================================================

    /// <summary>Minimal INotifyPropertyChanged base for the mocks (and for real VMs later).</summary>
    public abstract class CompanionObservable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>Sets the field and raises PropertyChanged when the value actually changed.</summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    /// <summary>
    /// The house relay command. Zone interfaces expose <see cref="ICommand"/> so the builders can
    /// hand in real handlers; the mocks hand in no-ops that simply record the last invocation,
    /// which makes the state gallery clickable without wiring anything up.
    /// </summary>
    public sealed class CompanionRelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public CompanionRelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public CompanionRelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute == null ? null : new Func<object?, bool>(_ => canExecute())) { }

        /// <summary>A command that does nothing but remember it was asked to. Used by the mocks.</summary>
        public static CompanionRelayCommand NoOp(string tag = "")
            => new(p => Note(tag, p));

        /// <summary>
        /// Records an invocation the way <see cref="NoOp"/> does, without owning the action. A mock
        /// that has real work to do — the room's deep links, for instance — calls this first so the
        /// gallery and the tests can still see which affordance was pressed.
        /// </summary>
        public static void Note(string tag, object? parameter = null)
        {
            LastInvokedTag = tag;
            LastInvokedParameter = parameter;
        }

        /// <summary>Diagnostics for the design-time gallery and unit tests.</summary>
        public static string LastInvokedTag { get; private set; } = string.Empty;
        public static object? LastInvokedParameter { get; private set; }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    /// <summary>
    /// The muted "which train lights this up" microtag in a section header. Design rule: a dormant
    /// zone is never an empty gray box — it carries in-character copy plus one of these.
    /// </summary>
    public enum CompanionTrainTag
    {
        /// <summary>No tag at all.</summary>
        None,
        /// <summary>Green "live" tag — the zone is real right now.</summary>
        Live,
        Train1,
        Train2,
        Train3,
        Train4
    }

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

    /// <summary>Which way a chat bubble leans, and whether it was spoken rather than typed.</summary>
    public enum CompanionBubbleKind
    {
        /// <summary>Her line — left, #2A1A3A with a pink left border.</summary>
        Her,
        /// <summary>Your line — right, #1F2A3A.</summary>
        You,
        /// <summary>A BarkEcho: what her voice said out loud. Italic whisper, dashed outline.</summary>
        Echo
    }

    /// <summary>A constellation node's visual state.</summary>
    public enum ConstellationNodeState
    {
        /// <summary>Reached and passed — pink filled star.</summary>
        Filled,
        /// <summary>Where you are — gold star, the one node that pulses.</summary>
        Current,
        /// <summary>Not reached — faint outline.</summary>
        Future
    }

    /// <summary>The 3-stop awareness intensity dial (Z5). Maps Off / Categories / Full.</summary>
    public enum AwarenessIntensity
    {
        Off,
        BroadStrokes,
        Everything
    }

    /// <summary>The Engine Room's provider segment (Z7) — the four legacy radios.</summary>
    public enum CompanionProviderMode
    {
        Off,
        Cloud,
        LocalOllama,
        Custom
    }

    /// <summary>
    /// The attention meter's copy ladder (Z6). Pure function of the remaining fraction so it is
    /// unit-testable and so the thresholds live in exactly one place. Never the word "tokens";
    /// the floor state must still say her voice keeps working.
    /// </summary>
    public enum AttentionMood
    {
        /// <summary>&gt;= 40% — "Plenty of her attention left today."</summary>
        Plenty,
        /// <summary>&lt; 40% — "she's saving her best lines".</summary>
        Saving,
        /// <summary>&lt; 15% — "she's whispering to conserve energy".</summary>
        Whispering,
        /// <summary>0% — "she'll be all yours again tomorrow~".</summary>
        Spent
    }

    /// <summary>
    /// Attention-meter decision logic (doc 01 §4.3 / design §3 Z6). Thresholds are inclusive at
    /// the top: exactly 40% is still <see cref="AttentionMood.Plenty"/>, exactly 15% is still
    /// <see cref="AttentionMood.Saving"/>, and only a true zero is <see cref="AttentionMood.Spent"/>.
    /// </summary>
    public static class AttentionCopy
    {
        /// <summary>Below this fraction the quiet, in-voice Patreon line appears.</summary>
        public const double UpsellThreshold = 0.40;
        /// <summary>Below this fraction she "whispers to conserve energy".</summary>
        public const double WhisperThreshold = 0.15;

        /// <summary>Maps 0..1 remaining attention to the copy ladder. Clamps, never throws.</summary>
        public static AttentionMood MoodFor(double fraction)
        {
            double f = FractionToStarConverter.ToFraction(fraction);
            if (f <= 0.0) return AttentionMood.Spent;
            if (f < WhisperThreshold) return AttentionMood.Whispering;
            if (f < UpsellThreshold) return AttentionMood.Saving;
            return AttentionMood.Plenty;
        }

        /// <summary>The loc key for the headline copy at this fraction.</summary>
        public static string CopyKeyFor(double fraction) => MoodFor(fraction) switch
        {
            AttentionMood.Spent => "companion_attention_spent",
            AttentionMood.Whispering => "companion_attention_whispering",
            AttentionMood.Saving => "companion_attention_saving",
            _ => "companion_attention_plenty"
        };

        /// <summary>The quiet upsell shows below 40% — and only there. Never at full, never twice.</summary>
        public static bool ShowUpsell(double fraction)
            => FractionToStarConverter.ToFraction(fraction) < UpsellThreshold;

        /// <summary>
        /// The bar never renders as literally nothing: a spent meter keeps a 4% sliver (the mockup's
        /// <c>.att-empty</c>) so the card reads as "empty", not "broken".
        /// </summary>
        public static double BarFractionFor(double fraction)
        {
            double f = FractionToStarConverter.ToFraction(fraction);
            return f <= 0.0 ? 0.04 : f;
        }
    }

    /// <summary>
    /// Relationship-constellation maths (Z1 bottom). Five stages, ratchet design — the mechanics
    /// stay vague in the UI but the node states are deterministic.
    /// </summary>
    public static class ConstellationMath
    {
        /// <summary>New ▸ Warming ▸ Bestie ▸ Possessive ▸ Inevitable.</summary>
        public const int StageCount = 5;

        /// <summary>Clamps any stage index into 0..4.</summary>
        public static int ClampStage(int stage)
            => stage < 0 ? 0 : (stage >= StageCount ? StageCount - 1 : stage);

        /// <summary>
        /// The node state for <paramref name="index"/> given the current stage. Pre-T4 (dormant)
        /// every node is <see cref="ConstellationNodeState.Future"/> — outlines with names, no
        /// lock icon, because this is a promise and not a paywall.
        /// </summary>
        public static ConstellationNodeState StateFor(int index, int currentStage, bool isLive)
        {
            if (!isLive) return ConstellationNodeState.Future;
            int cur = ClampStage(currentStage);
            if (index < cur) return ConstellationNodeState.Filled;
            if (index == cur) return ConstellationNodeState.Current;
            return ConstellationNodeState.Future;
        }

        /// <summary>The loc key for a stage name, with an optional per-mod reflavor override.</summary>
        public static string StageKey(int index, string? modId = null)
        {
            int i = ClampStage(index);
            return string.IsNullOrWhiteSpace(modId)
                ? $"companion_stage_{i}"
                : $"companion_stage_{i}_{modId}";
        }

        /// <summary>
        /// How far the pink connector runs, 0..1. The line stops just past the current node so the
        /// unearned tail stays hairline — the gradient in the theme does the fade.
        /// </summary>
        public static double ConnectorFraction(int currentStage, bool isLive)
        {
            if (!isLive) return 0.0;
            int cur = ClampStage(currentStage);
            return (cur + 0.5) / StageCount;
        }
    }

    /// <summary>
    /// Fact-wall ordering and filtering (Z3). Consent hygiene made visible: boundaries always sort
    /// first, then pinned cards, then salience — and the trailing dormant/ghost card never sorts
    /// into the middle of the wall.
    /// </summary>
    public static class FactOrdering
    {
        /// <summary>The kind-filter chip keys, in display order. "all" is the default chip.</summary>
        public static IReadOnlyList<string> FilterKeys { get; } = new[]
        {
            "all", "boundary", "joke", "preference", "goal", "moment"
        };

        /// <summary>True when a fact of <paramref name="kindKey"/> passes the chip <paramref name="filterKey"/>.</summary>
        public static bool Passes(string? kindKey, string? filterKey)
        {
            if (string.IsNullOrWhiteSpace(filterKey) ||
                string.Equals(filterKey, "all", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(kindKey, filterKey, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sort weight — lower sorts earlier. Boundary (0) ▸ pinned (1) ▸ normal (2) ▸ dormant (3).
        /// Ties fall through to salience descending, which the caller applies.
        /// </summary>
        public static int SortRank(bool isBoundary, bool isPinned, bool isDormant)
        {
            if (isDormant) return 3;
            if (isBoundary) return 0;
            if (isPinned) return 1;
            return 2;
        }

        /// <summary>
        /// The whole wall projection in one place: filter by the selected chip, then sort by
        /// <see cref="SortRank"/> with the original order preserved inside each rank (a stable
        /// sort, so the caller's salience ordering survives).
        ///
        /// <para>The dormant/ghost promise card is exempt from filtering — it belongs to the wall,
        /// not to a kind — and always lands last.</para>
        ///
        /// <para>The view does none of this: <c>IMemoryDiaryVm.Facts</c> hands over the finished
        /// list, which is what keeps the fact wall a plain virtualised ItemsControl.</para>
        /// </summary>
        public static IReadOnlyList<IMemoryFactVm> Project(
            IReadOnlyList<IMemoryFactVm>? all, string? filterKey)
        {
            if (all == null || all.Count == 0) return Array.Empty<IMemoryFactVm>();

            var kept = new List<IMemoryFactVm>(all.Count);
            foreach (var f in all)
            {
                if (f == null) continue;
                if (f.IsDormant || Passes(f.KindKey, filterKey)) kept.Add(f);
            }

            // List.Sort is unstable, so rank with the original index as the tiebreaker instead.
            var indexed = new List<(IMemoryFactVm Fact, int Rank, int Order)>(kept.Count);
            for (int i = 0; i < kept.Count; i++)
            {
                var f = kept[i];
                indexed.Add((f, SortRank(f.IsBoundary, f.IsPinned, f.IsDormant), i));
            }
            indexed.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Order.CompareTo(b.Order));

            var result = new List<IMemoryFactVm>(indexed.Count);
            foreach (var e in indexed) result.Add(e.Fact);
            return result;
        }
    }
}
