using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-024 slice b2: the save picker's draw-level facts (verification-harness evidence-class
/// rule): visual-tree structure, card classes (sel/locked), texts, confirm overlay, commit
/// and cancel outcomes. No rendered frames, no presentation claims.
/// </summary>
public class DtrhSlotPickerHeadlessTests
{
    private static (DtrhSaveSlots slots, string dir) NewSlots()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp024-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var slots = new DtrhSaveSlots(
            new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new Sink()), dir);
        slots.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (slots, dir);
    }

    private static string AllText(Control root) =>
        string.Join("|", root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    [AvaloniaFact]
    public void FreshProfile_ThreeCards_Slots23Locked_NewJourney()
    {
        var (slots, _) = NewSlots();
        var picker = new DtrhSlotPickerWindow(slots);
        picker.Show();

        Assert.Equal(3, picker.CardCount);
        var text = AllText(picker);
        Assert.Contains("New Journey", text);
        Assert.Contains("SAVE 1", text);
        Assert.Contains("Stitched Shut", text);
        Assert.Contains("craft the Ragdoll in THE BOUDOIR", text);
        // Footer: the save folder is shown verbatim (WPF: PathText = SaveFolder).
        Assert.Contains(slots.SaveFolder, text);

        // Selection defaults to the active slot (1); locked cards are not selectable visuals.
        Assert.Equal(1, picker.SelectedSlot);
        var cards = picker.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("slot-card")).ToList();
        Assert.Equal(3, cards.Count);
        Assert.Contains("sel", cards[0].Classes);
        Assert.Contains("locked", cards[1].Classes);
        Assert.Contains("locked", cards[2].Classes);
        // Delete only exists on populated cards: fresh profile has no delete buttons.
        Assert.DoesNotContain(picker.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "🗑"));
        picker.Close();
    }

    [AvaloniaFact]
    public async Task PopulatedSlot_ShowsRankStatsLastPlayed_AndDeleteFlow()
    {
        var (slots, _) = NewSlots();
        await slots.DescendInto(2);
        slots.StoreFor(2).Mutate(m =>
        {
            m.RunsCompleted = 12;   // rank: Slipping (thresholds 0/3/10/25/50/100)
            m.Sparks = 340;
            m.Gold = 55;
            m.BestScore = 12345;
        });
        await slots.StoreFor(2).SaveImmediate();

        var picker = new DtrhSlotPickerWindow(slots);
        picker.Show();

        var text = AllText(picker);
        Assert.Contains("Slipping", text);
        Assert.Contains("12", text);
        Assert.Contains("descents", text);
        Assert.Contains("340", text);
        Assert.Contains("55", text);
        Assert.Contains("best score", text); // N0 group separator is culture-dependent
        Assert.Contains("Last played", text);
        Assert.Equal(2, picker.SelectedSlot); // defaults to the active slot

        // The populated card carries a delete button; clicking it shows the confirm overlay.
        var del = picker.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "🗑"));
        del.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var overlay = picker.FindControl<Border>("ConfirmOverlay")!;
        Assert.True(overlay.IsVisible);
        Assert.Contains("Erase Save 2", AllText(overlay));

        // Confirm erases: file gone, card rebuilt as New Journey.
        picker.FindControl<Button>("ConfirmEraseButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(overlay.IsVisible);
        Assert.False(File.Exists(slots.SlotFilePath(2)));
        Assert.Contains("New Journey", AllText(picker));
        picker.Close();
    }

    [AvaloniaFact]
    public void Commit_AndCancel_Outcomes()
    {
        var (slots, _) = NewSlots();
        var picker = new DtrhSlotPickerWindow(slots);
        picker.Show();
        picker.CommitCurrentSelection();
        Assert.Equal(1, picker.ChosenSlot);

        var picker2 = new DtrhSlotPickerWindow(slots);
        picker2.Show();
        picker2.FindControl<Button>("CancelButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(picker2.ChosenSlot); // cancel backs out — no launch
    }

    [Fact]
    public void RankName_MatchesWpfThresholds()
    {
        // ChaosRanks.cs:11-31 — {0,3,10,25,50,100}.
        Assert.Equal("Curious", DtrhSlotPickerWindow.RankName(0));
        Assert.Equal("Curious", DtrhSlotPickerWindow.RankName(2));
        Assert.Equal("Tempted", DtrhSlotPickerWindow.RankName(3));
        Assert.Equal("Slipping", DtrhSlotPickerWindow.RankName(10));
        Assert.Equal("Entranced", DtrhSlotPickerWindow.RankName(25));
        Assert.Equal("Devoted", DtrhSlotPickerWindow.RankName(50));
        Assert.Equal("Claimed", DtrhSlotPickerWindow.RankName(100));
        Assert.Equal("Claimed", DtrhSlotPickerWindow.RankName(9999));
    }

    private sealed class Sink : ILogSink
    {
        public void Log(string message) { }
    }
}
