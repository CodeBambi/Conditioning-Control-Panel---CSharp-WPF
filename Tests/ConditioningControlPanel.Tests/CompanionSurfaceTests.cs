using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The two "living surfaces" of the redesigned Companion tab — Z2 <c>ChatThresholdView</c> and
/// Z3 <c>MemoryDiaryView</c> — and the viewmodel logic behind them.
///
/// <para>Both views are deliberately dumb: they draw a projected list and fire commands. What can
/// actually be wrong lives in three small pieces, and each one has a user-visible failure mode
/// worth a test:</para>
/// <list type="bullet">
///   <item><see cref="MemoryFactCard"/> — pin / inline edit / forget. A blank commit that erased
///   the fact would be a silent data loss with no confirm anywhere near it; a pinnable boundary
///   would promise that a consent record can sink.</item>
///   <item><see cref="MockMemoryDiaryVm"/>'s wall mutation — pinning has to re-run
///   <see cref="FactOrdering.Project"/>, otherwise the pin does nothing visible and reads as
///   broken; forgetting has to be able to tip the wall back into its empty state.</item>
///   <item><see cref="MemoryForgetConfirm"/> — the only path to wiping her memory. It must ask,
///   must run once, and must never survive a companion switch while armed.</item>
/// </list>
///
/// <para>Plus the chat surface's send path and the fact-kind palette. Nothing here needs a
/// window except the two render cases at the bottom, which run on their own STA thread.</para>
/// </summary>
public class CompanionSurfaceTests
{
    // =====================================================================================
    //  Z3 — a single fact card
    // =====================================================================================

    private static MemoryFactCard Card(string text = "Calls his cat “Prime Minister Beans.”",
        string kind = "joke", bool boundary = false, bool pinned = false, bool dormant = false)
        => new(text, kind, kind, "used 4× · last: yesterday",
               isBoundary: boundary, isPinned: pinned, isDormant: dormant)
        {
            UserEditedMetaLabel = "edited by you"
        };

    [Fact]
    public void Pin_TogglesAndAnnouncesItself()
    {
        var card = Card();
        var seen = new List<string?>();
        card.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        Assert.False(card.IsPinned);
        card.PinCommand.Execute(null);
        Assert.True(card.IsPinned);
        card.PinCommand.Execute(null);
        Assert.False(card.IsPinned);

        // The wall re-projects off this notification; without it the pin does nothing visible.
        Assert.Equal(2, seen.Count(n => n == nameof(IMemoryFactVm.IsPinned)));
    }

    [Fact]
    public void BoundaryCard_IsNeverPinnable_ButStaysEditableAndForgettable()
    {
        var card = Card("Never tease about chastity.", "boundary", boundary: true);

        Assert.False(card.PinCommand.CanExecute(null));
        card.PinCommand.Execute(null);
        Assert.False(card.IsPinned);          // it already sorts first; a pin would promise nothing

        Assert.True(card.EditCommand.CanExecute(null));
        Assert.True(card.ForgetCommand.CanExecute(null));
    }

    [Fact]
    public void DormantPromiseCard_IsInert()
    {
        var card = Card("“soon I'll remember what you say too…”", "all", dormant: true);

        Assert.False(card.PinCommand.CanExecute(null));
        Assert.False(card.EditCommand.CanExecute(null));
        Assert.False(card.ForgetCommand.CanExecute(null));

        card.IsEditing = true;
        Assert.False(card.IsEditing);         // copy, not a fact — there is nothing to edit
    }

    [Fact]
    public void Edit_SeedsTheBox_CommitsTrimmed_AndFlipsTheProvenanceLine()
    {
        var card = Card();
        card.EditCommand.Execute(null);

        Assert.True(card.IsEditing);
        Assert.Equal(card.Text, card.EditText);

        card.EditText = "   Calls his cat “Prime Minister Beans, Esq.”   ";
        card.CommitEditCommand.Execute(null);

        Assert.False(card.IsEditing);
        Assert.Equal("Calls his cat “Prime Minister Beans, Esq.”", card.Text);
        Assert.Equal("edited by you", card.MetaLabel);
    }

    [Fact]
    public void CommittingABlankEdit_CancelsInsteadOfErasingTheFact()
    {
        // Forgetting is a separate, worded, confirmed action. Clearing a textbox must never be it.
        var card = Card();
        string original = card.Text;

        card.EditCommand.Execute(null);
        card.EditText = "    ";
        card.CommitEditCommand.Execute(null);

        Assert.False(card.IsEditing);
        Assert.Equal(original, card.Text);
    }

    [Fact]
    public void CommittingAnUnchangedEdit_LeavesTheProvenanceAlone()
    {
        var card = Card();
        string meta = card.MetaLabel;

        card.EditCommand.Execute(null);
        card.CommitEditCommand.Execute(null);

        Assert.Equal(meta, card.MetaLabel);   // she didn't rewrite it, so it isn't "edited by you"
    }

    [Fact]
    public void Forget_AsksItsOwnerToRemoveIt_AndClosesAnyOpenEdit()
    {
        var card = Card();
        MemoryFactCard? forgotten = null;
        card.Forgotten = c => forgotten = c;

        card.EditCommand.Execute(null);
        card.ForgetCommand.Execute(null);

        Assert.Same(card, forgotten);
        Assert.False(card.IsEditing);
    }

    // =====================================================================================
    //  Z3 — the wall reacts
    // =====================================================================================

    [Fact]
    public void PinningACard_MovesItUpTheWall_AndUnpinningPutsItBack()
    {
        IMemoryDiaryVm vm = MockMemoryDiaryVm.Populated();

        var joke = vm.Facts.First(f => f.KindKey == "joke");
        var preference = vm.Facts.First(f => f.KindKey == "preference");
        Assert.True(vm.Facts.ToList().IndexOf(joke) < vm.Facts.ToList().IndexOf(preference));

        preference.PinCommand.Execute(null);

        var afterPin = vm.Facts.ToList();
        Assert.True(afterPin.IndexOf(preference) < afterPin.IndexOf(joke),
            "a pinned fact must climb above the unpinned ones — that jump is the pin's whole feedback");

        // …and the boundary card is still on top of everything. Consent hygiene outranks a pin.
        Assert.True(afterPin[0].IsBoundary);

        preference.PinCommand.Execute(null);
        var afterUnpin = vm.Facts.ToList();
        Assert.True(afterUnpin.IndexOf(joke) < afterUnpin.IndexOf(preference));
    }

    [Fact]
    public void TheDormantPromiseCard_StaysLastNoMatterWhatIsPinned()
    {
        IMemoryDiaryVm vm = MockMemoryDiaryVm.Populated();
        foreach (var fact in vm.Facts.Where(f => f.PinCommand.CanExecute(null)).ToList())
            fact.PinCommand.Execute(null);

        Assert.True(vm.Facts[^1].IsDormant);
    }

    [Fact]
    public void ForgettingACard_TakesItOffTheWall()
    {
        IMemoryDiaryVm vm = MockMemoryDiaryVm.Populated();
        var joke = vm.Facts.First(f => f.KindKey == "joke");
        int before = vm.Facts.Count;

        joke.ForgetCommand.Execute(null);

        Assert.Equal(before - 1, vm.Facts.Count);
        Assert.DoesNotContain(joke, vm.Facts);
        Assert.False(vm.IsEmpty);             // plenty left
    }

    [Fact]
    public void ForgettingTheLastFact_LandsOnTheDesignedEmptyState()
    {
        IMemoryDiaryVm vm = MockMemoryDiaryVm.Dormant();     // one real fact + the promise card
        var only = vm.Facts.First(f => !f.IsDormant);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        only.ForgetCommand.Execute(null);

        Assert.True(vm.IsEmpty);
        Assert.Contains(nameof(IMemoryDiaryVm.IsEmpty), raised);
        // never blank: the promise card survives, and the ghost copy is what the view draws
        Assert.Single(vm.Facts);
        Assert.True(vm.Facts[0].IsDormant);
        Assert.False(string.IsNullOrWhiteSpace(vm.EmptyCopy));
    }

    [Fact]
    public void ForgetEverything_WipesTheFacts_KeepsThePromiseCard_AndKeepsTheProfileStrip()
    {
        IMemoryDiaryVm vm = MockMemoryDiaryVm.Populated();

        vm.ForgetEverythingCommand.Execute(null);

        Assert.True(vm.IsEmpty);
        Assert.All(vm.Facts, f => Assert.True(f.IsDormant));
        // the strip is deterministic and local — a wipe of her diary does not erase your level
        Assert.NotEmpty(vm.ProfileStats);
    }

    [Fact]
    public void FilteringStillHolds_AfterTheWallHasBeenMutated()
    {
        var vm = MockMemoryDiaryVm.Populated();
        vm.Facts.First(f => f.KindKey == "goal").PinCommand.Execute(null);
        vm.SelectedFilterKey = "boundary";

        Assert.All(vm.Facts, f => Assert.True(f.IsBoundary || f.IsDormant));
        Assert.Single(vm.Filters, f => f.IsSelected);
    }

    // =====================================================================================
    //  Z3 — "Forget everything…" asks first
    // =====================================================================================

    private sealed class CountingCommand : System.Windows.Input.ICommand
    {
        public int Runs { get; private set; }
        public bool Enabled { get; set; } = true;
        public bool CanExecute(object? parameter) => Enabled;
        public void Execute(object? parameter) => Runs++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    [Fact]
    public void TheWipe_NeverRunsWithoutTheQuestion()
    {
        var wipe = new CountingCommand();
        var flow = new MemoryForgetConfirm();
        flow.Bind(wipe);

        Assert.False(flow.IsArmed);
        flow.ConfirmCommand.Execute(null);        // a stray click before arming
        Assert.Equal(0, wipe.Runs);

        flow.ArmCommand.Execute(null);
        Assert.True(flow.IsArmed);
        Assert.Equal(0, wipe.Runs);               // the question alone destroys nothing
    }

    [Fact]
    public void Confirming_RunsTheWipeExactlyOnce_AndPutsTheQuestionAway()
    {
        var wipe = new CountingCommand();
        var flow = new MemoryForgetConfirm();
        flow.Bind(wipe);
        flow.ArmCommand.Execute(null);

        flow.ConfirmCommand.Execute(null);
        flow.ConfirmCommand.Execute(null);        // double-click on the same button

        Assert.Equal(1, wipe.Runs);
        Assert.Equal(1, flow.ConfirmedCount);
        Assert.False(flow.IsArmed);
    }

    [Fact]
    public void SayingNo_KeepsUs()
    {
        var wipe = new CountingCommand();
        var flow = new MemoryForgetConfirm();
        flow.Bind(wipe);
        flow.ArmCommand.Execute(null);

        flow.CancelCommand.Execute(null);

        Assert.False(flow.IsArmed);
        Assert.Equal(0, wipe.Runs);
    }

    [Fact]
    public void AnArmedQuestion_NeverSurvivesACompanionSwitch()
    {
        var first = new CountingCommand();
        var second = new CountingCommand();
        var flow = new MemoryForgetConfirm();

        flow.Bind(first);
        flow.ArmCommand.Execute(null);
        Assert.True(flow.IsArmed);

        flow.Bind(second);                        // the card's viewmodel changed under it
        Assert.False(flow.IsArmed);

        flow.ConfirmCommand.Execute(null);
        Assert.Equal(0, first.Runs);
        Assert.Equal(0, second.Runs);
    }

    [Fact]
    public void WithNothingBound_TheButtonCannotEvenAsk()
    {
        var flow = new MemoryForgetConfirm();
        Assert.False(flow.CanArm);
        flow.ArmCommand.Execute(null);
        Assert.False(flow.IsArmed);

        var disabled = new CountingCommand { Enabled = false };
        flow.Bind(disabled);
        Assert.False(flow.CanArm);
        flow.ArmCommand.Execute(null);
        Assert.False(flow.IsArmed);
    }

    // =====================================================================================
    //  Z2 — the send path
    // =====================================================================================

    [Fact]
    public void Sending_AppendsYourLine_ClearsTheBox_AndStartsHerThinking()
    {
        var vm = MockChatThresholdVm.Live();
        int before = vm.Turns.Count;
        vm.Draft = "  it still does a little  ";

        Assert.True(vm.SendCommand.CanExecute(null));
        vm.SendCommand.Execute(null);

        Assert.Equal(before + 1, vm.Turns.Count);
        var mine = vm.Turns[^1];
        Assert.Equal(CompanionBubbleKind.You, mine.Kind);
        Assert.Equal("it still does a little", mine.Text);
        Assert.False(mine.IsAiGenerated, "your own line is not model output");
        Assert.Equal(string.Empty, vm.Draft);
        Assert.True(vm.IsThinking);
        Assert.False(vm.SendCommand.CanExecute(null), "no second send while one is in flight");
    }

    [Fact]
    public void HerReply_LandsWithTheBadge_AndStopsTheDots()
    {
        var vm = MockChatThresholdVm.Thinking();
        Assert.True(vm.IsThinking);

        vm.LandReply("good. it should~ 💕");

        Assert.False(vm.IsThinking);
        var hers = vm.Turns[^1];
        Assert.Equal(CompanionBubbleKind.Her, hers.Kind);
        Assert.True(hers.IsAiGenerated);
    }

    [Fact]
    public void ABarkEchoReply_NeverWearsTheAiBadge()
    {
        // The badge invariant: it rides IsAiGenerated, and a spoken line is not a completion.
        var vm = MockChatThresholdVm.Live();
        vm.LandReply("said aloud: “good girl~”", isAi: false);
        Assert.False(vm.Turns[^1].IsAiGenerated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyBox_NeverSends(string draft)
    {
        var vm = MockChatThresholdVm.Live();
        int before = vm.Turns.Count;
        vm.Draft = draft;

        Assert.False(vm.SendCommand.CanExecute(null));
        vm.SendCommand.Execute(null);

        Assert.Equal(before, vm.Turns.Count);
        Assert.False(vm.IsThinking);
    }

    [Fact]
    public void LockedAndProviderOff_CannotSendAtAll()
    {
        foreach (var vm in new[] { MockChatThresholdVm.Locked(), MockChatThresholdVm.AiOff() })
        {
            vm.Draft = "hello?";
            Assert.False(vm.CanSend);
            Assert.False(vm.SendCommand.CanExecute(null));
            vm.SendCommand.Execute(null);
            Assert.Empty(vm.Turns);
        }
    }

    [Fact]
    public void TheThreadIsObservable_SoTheViewCanStayPinnedToHerNewestLine()
    {
        var vm = MockChatThresholdVm.Live();
        var observable = Assert.IsAssignableFrom<INotifyCollectionChanged>(vm.Turns);

        int changes = 0;
        observable.CollectionChanged += (_, _) => changes++;
        vm.Draft = "one more thing";
        vm.SendCommand.Execute(null);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheLockedTeaserIsStaged_AndTheRealThreadStaysEmptyBehindIt()
    {
        var vm = MockChatThresholdVm.Locked();
        Assert.Empty(vm.Turns);                      // nothing real is blurred — only mock bubbles
        Assert.NotEmpty(vm.TeaserTurns);
        Assert.All(vm.TeaserTurns, b => Assert.False(b.IsAiGenerated));
    }

    // =====================================================================================
    //  fact-kind palette
    // =====================================================================================

    [Fact]
    public void EveryFilterKind_GetsItsOwnAccent_AndBoundaryGetsSteel()
    {
        var conv = new CompanionFactKindBrushConverter();
        var brushes = FactOrdering.FilterKeys
            .Where(k => k != "all")
            .ToDictionary(k => k, k => (SolidColorBrush)conv.Convert(
                k, typeof(Brush), null, CultureInfo.InvariantCulture));

        Assert.Equal(brushes.Count, brushes.Values.Select(b => b.Color).Distinct().Count());
        Assert.Equal(Color.FromRgb(0x7F, 0xB2, 0xD9), brushes["boundary"].Color);
        Assert.All(brushes.Values, b => Assert.True(b.IsFrozen, "recycled containers reuse these"));
    }

    [Fact]
    public void AnUnknownKind_StillRenders()
    {
        // A new memory kind arriving from the Brain must draw as a normal card, never blank.
        var conv = new CompanionFactKindBrushConverter();
        var brush = (SolidColorBrush)conv.Convert("chastity_streak", typeof(Brush), null,
            CultureInfo.InvariantCulture);
        Assert.NotNull(brush);
        Assert.Equal(255, brush.Color.A);
    }

    [Fact]
    public void TheRailIsTheSameHue_JustQuieter()
    {
        var solid = (SolidColorBrush)new CompanionFactKindBrushConverter()
            .Convert("joke", typeof(Brush), null, CultureInfo.InvariantCulture);
        var rail = (SolidColorBrush)new CompanionFactKindBrushConverter { Soft = true }
            .Convert("joke", typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Equal(solid.Color.R, rail.Color.R);
        Assert.Equal(solid.Color.G, rail.Color.G);
        Assert.Equal(solid.Color.B, rail.Color.B);
        Assert.True(rail.Color.A < solid.Color.A);
    }

    // =====================================================================================
    //  staged localization for this package
    // =====================================================================================

    private static string PackageStagingFilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel",
                "Views", "Controls", "Companion", "loc-staging-surfaces.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate loc-staging-surfaces.json walking up from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void ThePackageStagingFile_IsStrictJson_AndAgreesWithTheEnMasters()
    {
        var mine = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(PackageStagingFilePath()));
        Assert.NotNull(mine);
        Assert.NotEmpty(mine!);

        foreach (var kv in mine!)
        {
            Assert.True(CompanionLocStaging.English.TryGetValue(kv.Key, out var master),
                $"'{kv.Key}' is in this package's hand-off but has no EN master");
            Assert.Equal(master, kv.Value);
            Assert.DoesNotContain('\n', kv.Value);
            Assert.DoesNotContain('\r', kv.Value);
        }
    }

    [Fact]
    public void EveryStringTheseTwoSurfacesDraw_HasAnEnMaster()
    {
        string[] keys =
        {
            "companion_chat_title", "companion_chat_ai_badge", "companion_chat_open_full",
            "companion_chat_history", "companion_chat_open_engine", "companion_chat_send_tip",
            "companion_chat_thinking",
            "companion_memory_title", "companion_memory_hint", "companion_memory_pin_tip",
            "companion_memory_edit_tip", "companion_memory_forget_tip",
            "companion_memory_edit_save", "companion_memory_edit_cancel",
            "companion_memory_forget_confirm", "companion_memory_forget_yes",
            "companion_memory_forget_no", "companion_tag_train1"
        };

        foreach (var key in keys)
        {
            Assert.True(CompanionLocStaging.English.ContainsKey(key), $"'{key}' has no EN master");
            Assert.NotEqual(key, CompanionLocStaging.Resolve(key));
        }
    }

    // =====================================================================================
    //  the two states the smoke suite cannot reach declaratively
    // =====================================================================================

    private static void OnStaThread(Action body)
    {
        Exception? escaped = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { escaped = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA render thread did not finish in time");
        if (escaped != null) throw new Xunit.Sdk.XunitException(escaped.ToString());
    }

    private static void Realize(FrameworkElement control, double width)
    {
        control.Measure(new Size(width, double.PositiveInfinity));
        control.Arrange(new Rect(new Point(0, 0), new Size(width, Math.Max(1, control.DesiredSize.Height))));
        control.UpdateLayout();
        Assert.True(control.DesiredSize.Height > 0);
    }

    [Fact]
    public void TheConfirmStrip_Renders_AndItsButtonsReachTheViewModel()
    {
        // The strip binds through {RelativeSource AncestorType=UserControl} to the view's own
        // ForgetConfirm; a typo there compiles fine and then does nothing at runtime.
        OnStaThread(() =>
        {
            var vm = MockMemoryDiaryVm.Populated();
            var view = new MemoryDiaryView { DataContext = vm };
            Realize(view, 660);

            Assert.True(view.ForgetConfirm.CanArm);
            view.ForgetConfirm.ArmCommand.Execute(null);
            Realize(view, 660);
            Assert.True(view.ForgetConfirm.IsArmed);

            view.ForgetConfirm.ConfirmCommand.Execute(null);
            Realize(view, 660);

            Assert.False(view.ForgetConfirm.IsArmed);
            Assert.True(((IMemoryDiaryVm)vm).IsEmpty, "the confirmed wipe must reach the viewmodel");
        });
    }

    [Fact]
    public void AGrowingThread_KeepsRendering()
    {
        OnStaThread(() =>
        {
            var vm = MockChatThresholdVm.Live();
            var view = new ChatThresholdView { DataContext = vm };
            Realize(view, 660);

            vm.Draft = "one more";
            vm.SendCommand.Execute(null);
            vm.LandReply("mm~");
            view.ScrollThreadToEnd();          // must be safe with no message pump running
            Realize(view, 660);

            Assert.Equal(6, vm.Turns.Count);
        });
    }
}
