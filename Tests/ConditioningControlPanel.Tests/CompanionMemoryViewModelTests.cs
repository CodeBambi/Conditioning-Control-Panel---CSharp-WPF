using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.ViewModels;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — the "What she knows about you" panel (doc 01 §2.4).
///
/// This is the trust surface for an adult app that keeps personal statements on disk, so the
/// plumbing between the panel and <see cref="IMemoryStore"/> is the compliance story, not polish:
/// if "Forget everything" or the per-row bin silently fails to reach the store, the UI is lying
/// about what has been deleted. These tests pin the view model against the real shell store plus a
/// hostile fake (rejecting writes, vanishing rows, no store at all).
/// </summary>
public class CompanionMemoryViewModelTests
{
    private static MemoryStore StoreWith(params (string Text, MemoryFactKind Kind)[] facts)
    {
        var store = new MemoryStore();
        foreach (var (text, kind) in facts) store.AddFact(text, kind);
        return store;
    }

    private static MemoryFactRowViewModel Row(CompanionMemoryViewModel vm, string text)
        => vm.Groups.SelectMany(g => g.Facts).Single(r => r.Text == text);

    // ---------- empty / unavailable ----------

    [Fact]
    public void NullStore_RendersUnavailableAndSwallowsEveryMutation()
    {
        var vm = new CompanionMemoryViewModel(null);

        Assert.False(vm.IsAvailable);
        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Groups);
        Assert.Empty(vm.ProfileSignals);

        // Kill switch off / brain init failed must degrade to "nothing to show", never to a crash.
        Assert.False(vm.TogglePin(null));
        Assert.False(vm.CommitEdit(null));
        Assert.False(vm.Delete(null));
        Assert.False(vm.ForgetEverything());
    }

    [Fact]
    public void EmptyStore_IsTheNormalTrain1State()
    {
        var vm = new CompanionMemoryViewModel(new MemoryStore());

        Assert.True(vm.IsAvailable);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasFacts);
        Assert.False(vm.HasProfile);
        Assert.Equal(0, vm.FactCount);
    }

    // ---------- grouping ----------

    [Fact]
    public void Groups_AreOrderedWithBoundariesFirst()
    {
        var store = StoreWith(
            ("likes the spiral", MemoryFactKind.Preference),
            ("hit level 41", MemoryFactKind.Event),
            ("no teasing about work", MemoryFactKind.Boundary),
            ("wants level 50", MemoryFactKind.Goal));

        var vm = new CompanionMemoryViewModel(store);

        Assert.Equal(
            new[] { MemoryFactKind.Boundary, MemoryFactKind.Preference, MemoryFactKind.Goal, MemoryFactKind.Event },
            vm.Groups.Select(g => g.Kind).ToArray());
        Assert.Equal(4, vm.FactCount);
        Assert.All(vm.Groups, g => Assert.NotEmpty(g.Facts));
    }

    [Fact]
    public void Groups_OmitKindsWithNoFacts()
    {
        var vm = new CompanionMemoryViewModel(StoreWith(("calls his cat Beans", MemoryFactKind.Joke)));

        var group = Assert.Single(vm.Groups);
        Assert.Equal(MemoryFactKind.Joke, group.Kind);
    }

    // ---------- pin ----------

    [Fact]
    public void TogglePin_PinsAndUnpinsThroughTheStore()
    {
        var store = StoreWith(("likes the spiral", MemoryFactKind.Preference));
        var vm = new CompanionMemoryViewModel(store);
        var row = Row(vm, "likes the spiral");

        Assert.True(vm.TogglePin(row));
        Assert.True(row.Pinned);
        Assert.True(store.GetFacts().Single().Pinned);

        Assert.True(vm.TogglePin(row));
        Assert.False(row.Pinned);
        Assert.False(store.GetFacts().Single().Pinned);
    }

    [Fact]
    public void TogglePin_DoesNotRestampTheSource()
    {
        // Pinning is not authorship. If pinning marked the fact "user-edited", a future extractor
        // would treat every pinned app-sourced fact as hand-written and stop maintaining it.
        var store = new MemoryStore();
        store.AddFact("level 41", MemoryFactKind.Event, source: MemoryFact.SourceApp);
        var vm = new CompanionMemoryViewModel(store);

        vm.TogglePin(Row(vm, "level 41"));

        Assert.Equal(MemoryFact.SourceApp, store.GetFacts().Single().Source);
        Assert.False(Row(vm, "level 41").IsUserEdited);
    }

    [Fact]
    public void TogglePin_OnAVanishedFact_ResyncsInsteadOfLying()
    {
        var store = StoreWith(("likes the spiral", MemoryFactKind.Preference));
        var vm = new CompanionMemoryViewModel(store);
        var row = Row(vm, "likes the spiral");

        store.ForgetFact(row.Id); // forgotten behind the panel's back

        Assert.False(vm.TogglePin(row));
        Assert.True(vm.IsEmpty);
    }

    // ---------- edit ----------

    [Fact]
    public void CommitEdit_WritesTextAndMarksItUserEdited()
    {
        var store = StoreWith(("calls his cat Beans", MemoryFactKind.Joke));
        var vm = new CompanionMemoryViewModel(store);
        var row = Row(vm, "calls his cat Beans");

        vm.BeginEdit(row);
        row.EditText = "  calls his cat Prime Minister Beans  ";

        Assert.True(vm.CommitEdit(row));
        Assert.False(row.IsEditing);

        var stored = store.GetFacts().Single();
        Assert.Equal("calls his cat Prime Minister Beans", stored.Text);
        Assert.Equal(MemoryFact.SourceUserEdited, stored.Source);
        Assert.True(row.IsUserEdited);
        Assert.Equal(stored.Text, row.Text);
    }

    [Fact]
    public void CommitEdit_FloorsSalienceSoTheFixedFactActuallyGetsUsed()
    {
        var store = new MemoryStore();
        store.AddFact("wrong name", MemoryFactKind.Identity, salience: 0.1);
        var vm = new CompanionMemoryViewModel(store);
        var row = Row(vm, "wrong name");

        vm.BeginEdit(row);
        row.EditText = "right name";
        Assert.True(vm.CommitEdit(row));

        Assert.Equal(CompanionMemoryViewModel.EditedSalienceFloor, store.GetFacts().Single().Salience, 3);
    }

    [Fact]
    public void CommitEdit_NeverLowersAnAlreadySalientFact()
    {
        var store = new MemoryStore();
        store.AddFact("no teasing about work", MemoryFactKind.Boundary, salience: 0.95);
        var vm = new CompanionMemoryViewModel(store);
        var row = Row(vm, "no teasing about work");

        vm.BeginEdit(row);
        row.EditText = "no teasing about my job";
        Assert.True(vm.CommitEdit(row));

        Assert.Equal(0.95, store.GetFacts().Single().Salience, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CommitEdit_RejectsBlankText(string blank)
    {
        var store = StoreWith(("likes the spiral", MemoryFactKind.Preference));
        var vm = new CompanionMemoryViewModel(store);
        var row = Row(vm, "likes the spiral");

        vm.BeginEdit(row);
        row.EditText = blank;

        // Blanking is not how you delete a memory - the bin is. The original text survives.
        Assert.False(vm.CommitEdit(row));
        Assert.False(row.IsEditing);
        Assert.Equal("likes the spiral", store.GetFacts().Single().Text);
        Assert.Equal(MemoryFact.SourceChat, store.GetFacts().Single().Source);
    }

    [Fact]
    public void CommitEdit_UnchangedTextIsANoOp()
    {
        var store = StoreWith(("likes the spiral", MemoryFactKind.Preference));
        var vm = new CompanionMemoryViewModel(store);
        var row = Row(vm, "likes the spiral");

        vm.BeginEdit(row);
        row.EditText = "likes the spiral";

        Assert.False(vm.CommitEdit(row));
        Assert.Equal(MemoryFact.SourceChat, store.GetFacts().Single().Source);
    }

    [Fact]
    public void CancelEdit_DiscardsTheBuffer()
    {
        var store = StoreWith(("likes the spiral", MemoryFactKind.Preference));
        var vm = new CompanionMemoryViewModel(store);
        var row = Row(vm, "likes the spiral");

        vm.BeginEdit(row);
        row.EditText = "something else entirely";
        vm.CancelEdit(row);

        Assert.False(row.IsEditing);
        Assert.Equal("likes the spiral", row.EditText);
        Assert.Equal("likes the spiral", store.GetFacts().Single().Text);
    }

    [Fact]
    public void BeginEdit_ClosesAnyOtherOpenRow()
    {
        var store = StoreWith(
            ("likes the spiral", MemoryFactKind.Preference),
            ("wants level 50", MemoryFactKind.Goal));
        var vm = new CompanionMemoryViewModel(store);
        var first = Row(vm, "likes the spiral");
        var second = Row(vm, "wants level 50");

        vm.BeginEdit(first);
        vm.BeginEdit(second);

        Assert.False(first.IsEditing);
        Assert.True(second.IsEditing);
    }

    // ---------- delete / wipe ----------

    [Fact]
    public void Delete_ForgetsTheFactAndDropsTheEmptiedGroup()
    {
        var store = StoreWith(
            ("calls his cat Beans", MemoryFactKind.Joke),
            ("wants level 50", MemoryFactKind.Goal));
        var vm = new CompanionMemoryViewModel(store);

        Assert.True(vm.Delete(Row(vm, "calls his cat Beans")));

        Assert.Equal(1, vm.FactCount);
        Assert.Equal(MemoryFactKind.Goal, Assert.Single(vm.Groups).Kind);
        Assert.Equal("wants level 50", store.GetFacts().Single().Text);
    }

    [Fact]
    public void Delete_LastFact_LeavesTheEmptyState()
    {
        var store = StoreWith(("calls his cat Beans", MemoryFactKind.Joke));
        var vm = new CompanionMemoryViewModel(store);

        Assert.True(vm.Delete(Row(vm, "calls his cat Beans")));

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Groups);
        Assert.Empty(store.GetFacts());
    }

    [Fact]
    public void ForgetEverything_WipesFactsAndProfileSignals()
    {
        var store = StoreWith(
            ("calls his cat Beans", MemoryFactKind.Joke),
            ("no teasing about work", MemoryFactKind.Boundary));
        store.UpdateProfileSignal("level", 41);
        var vm = new CompanionMemoryViewModel(store);

        Assert.True(vm.HasFacts);
        Assert.True(vm.HasProfile);

        Assert.True(vm.ForgetEverything());

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasProfile);
        Assert.Empty(store.GetFacts());
        Assert.Empty(store.Profile);
    }

    [Fact]
    public void ForgetEverything_DoesNotEvenSpareAPinnedBoundary()
    {
        // "Forget everything" has to mean everything, pins and boundaries included, or the
        // user-facing deletion promise is false.
        var store = StoreWith(("no teasing about work", MemoryFactKind.Boundary));
        var vm = new CompanionMemoryViewModel(store);
        vm.TogglePin(Row(vm, "no teasing about work"));

        vm.ForgetEverything();

        Assert.Empty(store.GetFacts());
    }

    // ---------- profile block ----------

    [Fact]
    public void ProfileSignals_AreSortedAndSkipEmptyValues()
    {
        var store = new MemoryStore();
        store.UpdateProfileSignal("streakDays", 12);
        store.UpdateProfileSignal("level", 41);
        store.UpdateProfileSignal("archetype", "   ");

        var vm = new CompanionMemoryViewModel(store);

        Assert.Equal(new[] { "level", "streakDays" }, vm.ProfileSignals.Select(p => p.Key).ToArray());
        Assert.Equal("41", vm.ProfileSignals[0].Value);
        Assert.True(vm.HasProfile);
    }

    [Fact]
    public void ProfileSignals_FlattenListValues()
    {
        var store = new MemoryStore();
        store.UpdateProfileSignal("favoriteFeatures", new List<string> { "flash", "chaos" });

        var vm = new CompanionMemoryViewModel(store);

        Assert.Equal("flash, chaos", Assert.Single(vm.ProfileSignals).Value);
    }

    [Fact]
    public void LabelForSignal_FallsBackToAHumanizedKey()
    {
        // The memory branch writes open-ended signal keys, so most will never have a loc entry.
        // An unlocalized key must still read as prose, never as a raw identifier. (Deliberately
        // uses keys with no companion_memory_signal_* translation, so the assertion does not
        // depend on whether an earlier test in the run has initialized LocalizationManager.)
        Assert.Equal("Longest chat run", CompanionMemoryViewModel.LabelForSignal("longestChatRun"));
        Assert.Equal("Bedtime habit", CompanionMemoryViewModel.LabelForSignal("bedtime-habit"));
        Assert.Equal(string.Empty, CompanionMemoryViewModel.LabelForSignal(" "));
    }

    [Theory]
    [InlineData("streakDays", "streak_days")]
    [InlineData("lastSessionRecap", "last_session_recap")]
    [InlineData("level", "level")]
    [InlineData("favorite features", "favorite_features")]
    public void ToSnakeCase_BuildsTheLocKeySuffix(string key, string expected)
    {
        Assert.Equal(expected, CompanionMemoryViewModel.ToSnakeCase(key));
    }

    // ---------- hostile store ----------

    /// <summary>A store that accepts reads but refuses every write, like a read-only/corrupt file.</summary>
    private sealed class RejectingStore : IMemoryStore
    {
        private readonly List<MemoryFact> _facts = new();

        public RejectingStore(params MemoryFact[] facts) => _facts.AddRange(facts);

        public string? GetInjectionBlock(int tokenBudget) => null;
        public void UpdateProfileSignal(string key, object? value) { }
        public IReadOnlyDictionary<string, object?> Profile => new Dictionary<string, object?>();
        public IReadOnlyList<MemoryFact> GetFacts() => _facts;
        public MemoryFact AddFact(string text, MemoryFactKind kind, double salience = 0.5,
            string source = MemoryFact.SourceChat) => throw new NotSupportedException();
        public bool UpdateFact(string id, string? text = null, double? salience = null, bool? pinned = null) => false;
        public bool ForgetFact(string id) => false;
        public void NoteFactUsed(string id) { }
        public int WipeCalls { get; private set; }
        public void Wipe() => WipeCalls++;
    }

    private static MemoryFact Fact(string text) => new(
        Id: "f-" + text.GetHashCode().ToString("x8"),
        Text: text,
        Kind: MemoryFactKind.Preference,
        Salience: 0.5,
        Created: new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc),
        LastUsed: null,
        Uses: 0,
        Pinned: false,
        Source: MemoryFact.SourceChat);

    [Fact]
    public void RejectedWrites_ReportFailureAndLeaveTheRowUnchanged()
    {
        var store = new RejectingStore(Fact("likes the spiral"));
        var vm = new CompanionMemoryViewModel(store);
        var row = Row(vm, "likes the spiral");

        Assert.False(vm.TogglePin(row));
        Assert.False(Row(vm, "likes the spiral").Pinned);

        var again = Row(vm, "likes the spiral");
        vm.BeginEdit(again);
        again.EditText = "likes the pink spiral";
        Assert.False(vm.CommitEdit(again));
        Assert.Equal("likes the spiral", Row(vm, "likes the spiral").Text);

        // A refused delete must leave the row on screen: showing a deletion that did not happen is
        // exactly the lie this panel exists to prevent.
        Assert.False(vm.Delete(Row(vm, "likes the spiral")));
        Assert.Equal(1, vm.FactCount);
    }

    [Fact]
    public void ForgetEverything_ReachesTheStoreExactlyOnce()
    {
        var store = new RejectingStore(Fact("likes the spiral"));
        var vm = new CompanionMemoryViewModel(store);

        Assert.True(vm.ForgetEverything());

        Assert.Equal(1, store.WipeCalls);
    }
}
