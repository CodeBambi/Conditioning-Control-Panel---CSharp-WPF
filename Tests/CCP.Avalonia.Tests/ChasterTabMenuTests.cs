using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Circe's Tab, the menu (ledger Y4 / Y5): the two boards, a row click, the click-to-set
/// figures, Reset, the red flash switch, the two limits, Relock, the trailer, the paper tag and the
/// unlinked page. Ported from WPF ChasterTabRenderTests / TabPriceEditTests where a view is needed.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ChasterTabMenuTests
{
    private sealed class FakeChaster : HttpMessageHandler
    {
        /// <summary>True = one running lock ("l1"); false = linked with no lock at all.</summary>
        public bool Locked;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var ends = DateTime.UtcNow.AddDays(3).ToString("o");
            var body = r.RequestUri!.AbsolutePath == "/locks"
                ? (Locked ? "[{\"_id\":\"l1\",\"title\":\"Test Cage\",\"status\":\"locked\",\"role\":\"wearer\",\"endDate\":\"" + ends + "\"}]" : "[]")
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    /// <summary>A linked service on a fake Chaster; the player's own Chaster settings are put back after.</summary>
    private static async Task Run(Func<ChasterService, FakeChaster, ConditioningControlPanel.Models.AppSettings, Task> body)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var saved = (s.ChasterLockId, s.ChasterTabEnabled, Prices: s.ChasterPrices, Overrides: s.ChasterPriceOverrides, s.ChasterFlashDodge,
            Day: s.ChasterDayLimit, Backlog: s.ChasterBacklogLimit, s.ChasterRelockPastEnd);
        s.ChasterLockId = "l1";
        s.ChasterTabEnabled = false;
        s.ChasterPrices = new List<string>();
        s.ChasterPriceOverrides = new();
        s.ChasterFlashDodge = false;
        s.ChasterDayLimit = new LimitSetting(180, 0, null);
        s.ChasterBacklogLimit = new LimitSetting(720, 0, null);
        s.ChasterRelockPastEnd = false;
        var dir = Directory.CreateTempSubdirectory("ccp-chaster-menu-").FullName;
        SecretStore.Seed();
        new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
        var fake = new FakeChaster();
        var chaster = new ChasterService(new ChasterClient(fake), new SecretChasterTokenStore(), Path.Combine(dir, "chaster_tab.json"),
            () => new ChasterOptions(CoreSettings.Current.ChasterTabEnabled, CoreSettings.Current.ChasterLockId,
                new HashSet<string>(CoreSettings.Current.ChasterPrices ?? new List<string>())));
        ChasterHead.Service = chaster;
        try
        {
            await chaster.RefreshLockAsync();
            await body(chaster, fake, s);
        }
        finally
        {
            ChasterHead.Service = null;
            chaster.Dispose();
            new SecretChasterTokenStore().Clear();
            try { Directory.Delete(dir, true); } catch { }
            (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPrices, s.ChasterPriceOverrides, s.ChasterFlashDodge) =
                (saved.ChasterLockId, saved.ChasterTabEnabled, saved.Prices, saved.Overrides, saved.ChasterFlashDodge);
            (s.ChasterDayLimit, s.ChasterBacklogLimit, s.ChasterRelockPastEnd) = (saved.Day, saved.Backlog, saved.ChasterRelockPastEnd);
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
    }

    private static void Press(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Named<T>(Control root, string name) where T : Control => root.FindControl<T>(name)!;

    /// <summary>WPF The_menu_is_one_row_per_price_on_two_boards: every priced row once, costs on the
    /// left and earn-backs on the right, and no way-out id (panic, the emergency exit, a safeword,
    /// unlink, a leash cut) ever gets a row.</summary>
    [Fact]
    public Task TheMenuIsOneRowPerPriceOnTwoBoardsAndNoWayOutHasARow() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, _) =>
    {
        var tab = new ChasterTabView();
        var (costs, earns) = TabPageText.Split(TabPrices.All);
        Assert.True(Named<StackPanel>(tab, "MenuPanel").IsVisible);
        Assert.Equal(costs.Select(p => p.Id), Named<Panel>(tab, "CostRows").Children.Select(c => (string)c.Tag!));
        Assert.Equal(earns.Select(p => p.Id), Named<Panel>(tab, "EarnRows").Children.Select(c => (string)c.Tag!));
        Assert.NotEmpty(TabPrices.NeverPriced);
        foreach (var id in TabPrices.NeverPriced)
        {
            Assert.Null(tab.RowFor(id));
            Assert.Null(tab.StampFor(id));
            Assert.False(tab.BeginPriceEdit(id));
        }
        // every stamp prints the table's own figure until the player edits one
        foreach (var p in costs.Concat(earns))
            Assert.Equal(TabPageText.Price(p, Loc.Get("chaster_each")), tab.StampFor(p.Id)!.Text);
        return Task.CompletedTask;
    }));

    /// <summary>A row click writes the settings list (a new list each time) and lights the fourth key.</summary>
    [Fact]
    public Task ARowClickSwitchesThatPriceOnAndOffInTheSettings() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, s) =>
    {
        var tab = new ChasterTabView();
        var row = tab.RowFor("typo")!;
        var before = s.ChasterPrices;
        row.IsChecked = true;
        Press(row);
        Assert.Equal(new[] { "typo" }, s.ChasterPrices);
        Assert.NotSame(before, s.ChasterPrices);
        Assert.True(Named<global::Avalonia.Controls.Primitives.ToggleButton>(tab, "BtnPresetCustom").IsChecked);
        row.IsChecked = false;
        Press(row);
        Assert.Empty(s.ChasterPrices);
        return Task.CompletedTask;
    }));

    /// <summary>A key pushes its set onto the rows (WPF Preset_Click -> ApplyPriceToggles).</summary>
    [Fact]
    public Task AKeyLightsItsRowsOnTheBoards() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, s) =>
    {
        var tab = new ChasterTabView();
        Press(Named<global::Avalonia.Controls.Primitives.ToggleButton>(tab, "BtnPresetGentle"));
        var ids = TabPresets.Apply(TabPresets.Gentle);
        Assert.NotEmpty(ids);
        Assert.Equal(ids.OrderBy(x => x), s.ChasterPrices.OrderBy(x => x));
        foreach (var id in tab.RowIds)
            Assert.Equal(ids.Contains(id), tab.RowFor(id)!.IsChecked == true);
        return Task.CompletedTask;
    }));

    /// <summary>WPF Clicking_the_figure_opens_a_box_only_on_rows_with_one_fixed_figure + TabPriceEdit:
    /// the box keeps a typed time as an override, the sign never moves, Reset puts the table back,
    /// and no price default changes.</summary>
    [Fact]
    public Task AFigureIsSetInItsBoxKeepsItsSignAndResetPutsTheTableBack() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, s) =>
    {
        var tab = new ChasterTabView();
        Assert.True(ChasterTabView.FiguresEditable());
        Assert.Equal(Loc.Get("chaster_menu_hint_edit"), Named<TextBlock>(tab, "TxtMenuHint").Text);
        Assert.False(tab.BeginPriceEdit(CircesMisses.EventId));   // doubles by itself: no one figure
        Assert.False(Named<Button>(tab, "BtnResetPrices").IsVisible);

        Assert.True(tab.BeginPriceEdit("typo"));
        Assert.Equal("typo", tab.EditingPriceId);
        Assert.False(tab.StampFor("typo")!.IsVisible);
        Assert.True(EscapeClaim.InASurface(tab.PriceEditor));
        tab.PriceEditor!.Text = "2:30";
        tab.CommitPriceEdit();
        Assert.Null(tab.EditingPriceId);
        Assert.Equal(150, s.ChasterPriceOverrides["typo"]);
        Assert.Equal(150, ChasterTabView.ShownSeconds("typo"));
        Assert.Equal(FontStyle.Italic, tab.StampFor("typo")!.FontStyle);
        Assert.True(tab.StampFor("typo")!.IsVisible);
        Assert.True(Named<Button>(tab, "BtnResetPrices").IsVisible);

        // an earn-back stays an earn-back whatever is typed
        Assert.True(tab.BeginPriceEdit("lockcard"));
        tab.PriceEditor!.Text = "+5:00";
        tab.CommitPriceEdit();
        Assert.Equal(-300, ChasterTabView.ShownSeconds("lockcard"));

        // text that is not a time is dropped
        Assert.True(tab.BeginPriceEdit("attention"));
        tab.PriceEditor!.Text = "soon";
        tab.CommitPriceEdit();
        Assert.False(s.ChasterPriceOverrides.ContainsKey("attention"));

        tab.ResetPrices();
        Assert.Empty(s.ChasterPriceOverrides);
        Assert.Equal(30, ChasterTabView.ShownSeconds("typo"));
        Assert.Equal(30, TabPrices.Find("typo")!.Seconds);
        Assert.Equal(FontStyle.Normal, tab.StampFor("typo")!.FontStyle);
        return Task.CompletedTask;
    }));

    /// <summary>WPF Escape_in_the_open_price_box_belongs_to_the_box_not_the_panic_key: Esc drops the edit.</summary>
    [Fact]
    public Task EscapeInTheBoxDropsTheEditAndEnterKeepsIt() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, s) =>
    {
        var tab = new ChasterTabView();
        Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "9:00";
        var esc = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        tab.PriceBoxKey(esc);
        Assert.True(esc.Handled);
        Assert.Null(tab.EditingPriceId);
        Assert.Empty(s.ChasterPriceOverrides);

        Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "9:00";
        tab.PriceBoxKey(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal(540, s.ChasterPriceOverrides["typo"]);
        return Task.CompletedTask;
    }));

    /// <summary>Figures move only while no lock runs: with a lock up the stamp is not a box, an open
    /// box closes without keeping, the line says why, and Reset does nothing.</summary>
    [Fact]
    public Task WhileALockRunsTheFiguresStayFixed() => AvaloniaTestDispatcher.RunAsync(() => Run(async (chaster, fake, s) =>
    {
        var tab = new ChasterTabView();
        Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "9:00";
        s.ChasterPriceOverrides = TabPriceEdit.With(null, "attention", 600);
        fake.Locked = true;
        await chaster.RefreshLockAsync();
        Assert.Equal(LockLookup.Chosen, chaster.LockLookup);
        tab.PaintStamps();
        Assert.Null(tab.EditingPriceId);
        Assert.False(s.ChasterPriceOverrides.ContainsKey("typo"));
        Assert.False(tab.BeginPriceEdit("typo"));
        Assert.Equal(Loc.Get("chaster_menu_hint_locked"), Named<TextBlock>(tab, "TxtMenuHint").Text);
        Assert.False(Named<Button>(tab, "BtnResetPrices").IsVisible);
        tab.ResetPrices();
        Assert.Equal(600, s.ChasterPriceOverrides["attention"]);
    }));

    /// <summary>The red flash switch writes its setting; its line follows Natasha's row and her figure.</summary>
    [Fact]
    public Task TheRedFlashSwitchWritesItsSettingAndItsLineFollowsNatashasRow() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, s) =>
    {
        var tab = new ChasterTabView();
        var sub = Named<TextBlock>(tab, "TxtFlashDodgeSub");
        Assert.Equal(Loc.Get("chaster_flash_dodge_needs"), sub.Text);
        Named<CheckBox>(tab, "ChkFlashDodge").IsChecked = true;
        Assert.True(s.ChasterFlashDodge);
        Named<CheckBox>(tab, "ChkFlashDodge").IsChecked = false;
        Assert.False(s.ChasterFlashDodge);

        var row = tab.RowFor(NatashasFavourite.EventId)!;
        row.IsChecked = true;
        Press(row);
        Assert.Equal(Loc.GetF("chaster_flash_dodge_sub", NatashasFavourite.DodgeMs / 1000,
            CircesTab.Format(NatashasFavourite.Seconds, signed: false)), sub.Text);
        return Task.CompletedTask;
    }));

    /// <summary>The two limits: building the page asks for nothing, lowering applies at once, a raise
    /// waits a day and says when, the backlog never sits under the day, and Relock writes its setting.</summary>
    [Fact]
    public Task LoweringALimitAppliesNowARaiseWaitsADayAndRelockIsASetting() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, s) =>
    {
        var tab = new ChasterTabView();
        // the defaults the page opens on: 3:00:00 a day, 12:00:00 on the tab
        Assert.Equal(new LimitSetting(180, 0, null), s.ChasterDayLimit);
        Assert.Equal(new LimitSetting(720, 0, null), s.ChasterBacklogLimit);
        var day = Named<Slider>(tab, "SliderDayLimit");
        var backlog = Named<Slider>(tab, "SliderBacklogLimit");
        Assert.Equal(180, day.Value);
        Assert.Equal(720, backlog.Value);
        Assert.Equal(CircesTab.Format(180 * 60, signed: false), Named<TextBlock>(tab, "TxtDayLimit").Text);

        Assert.False(Named<Border>(tab, "LimitsCard").IsVisible);
        Press(Named<Button>(tab, "BtnLimits"));
        Assert.True(Named<Border>(tab, "LimitsCard").IsVisible);

        day.Value = 60;
        Assert.Equal(new LimitSetting(60, 0, null), s.ChasterDayLimit);
        Assert.False(Named<TextBlock>(tab, "TxtDayPending").IsVisible);

        day.Value = 300;
        Assert.Equal(60, s.ChasterDayLimit.Minutes);
        Assert.True(s.ChasterDayLimit.HasPending);
        Assert.Equal(300, s.ChasterDayLimit.PendingMinutes);
        Assert.True(Named<TextBlock>(tab, "TxtDayPending").IsVisible);

        // the day dragged past the backlog carries it along
        backlog.Value = 120;
        day.Value = 30;
        day.Value = 720;
        Assert.True(backlog.Value >= 720);

        var relock = Named<CheckBox>(tab, "ChkRelock");
        relock.IsChecked = true;
        Assert.True(s.ChasterRelockPastEnd);
        relock.IsChecked = false;
        Assert.False(s.ChasterRelockPastEnd);
        return Task.CompletedTask;
    }));

    /// <summary>WPF The_trailer_dresses_itself_for_the_row_it_is_aimed_at_and_closes_clean: the words
    /// carry the row's live figure ("{0}" filled from the figure, never a second number).</summary>
    [Fact]
    public Task TheTrailerDressesItselfForItsRowWithTheLiveFigure() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, s) =>
    {
        var tab = new ChasterTabView();
        Assert.Null(tab.TrailerId);
        s.ChasterPriceOverrides = TabPriceEdit.With(null, "attention", 420);
        tab.OpenTrailer(tab.RowFor("attention"));
        Assert.Equal("attention", tab.TrailerId);
        Assert.Equal(Loc.Get(TabMenuCopy.FlavourKey("attention")), Named<TextBlock>(tab, "TxtTrailerFlavour").Text);
        Assert.Equal(TabMenuCopy.Why("attention", Loc.Get, 420), Named<TextBlock>(tab, "TxtTrailerWhy").Text);
        Assert.DoesNotContain("{0}", Named<TextBlock>(tab, "TxtTrailerWhy").Text);
        Assert.Equal(CircesTab.Format(420), Named<TextBlock>(tab, "TxtTrailerFigure").Text);
        Assert.NotNull(Named<Image>(tab, "TrailerArt").Source);
        // a row with no one figure prints none
        tab.OpenTrailer(tab.RowFor(CircesMisses.EventId));
        Assert.Equal("", Named<TextBlock>(tab, "TxtTrailerFigure").Text);
        tab.HideTrailer();
        Assert.Null(tab.TrailerId);
        return Task.CompletedTask;
    }));

    /// <summary>WPF The_paper_tag_says_what_is_on_the_tab_and_stamps_it_unpaid_clear_or_credit.</summary>
    [Fact]
    public Task ThePaperTagStampsUnpaidClearOrCredit() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, _) =>
    {
        var tab = new ChasterTabView();
        Assert.True(Named<Panel>(tab, "PaperTag").IsVisible);
        Assert.Equal(Loc.Get("chaster_tag_clear"), Named<TextBlock>(tab, "TxtTagStamp").Text);
        tab.RefreshTag(90);
        Assert.Equal(Loc.Get("chaster_tag_unpaid"), Named<TextBlock>(tab, "TxtTagStamp").Text);
        Assert.Equal(CircesTab.Format(90), Named<TextBlock>(tab, "TxtTagAmount").Text);
        tab.RefreshTag(-90);
        Assert.Equal(Loc.Get("chaster_tag_credit"), Named<TextBlock>(tab, "TxtTagStamp").Text);
        Assert.Equal(Loc.Get("chaster_tag_credit_lands"), Named<TextBlock>(tab, "TxtTagLands").Text);
        return Task.CompletedTask;
    }));

    /// <summary>Ledger Y5, WPF With_no_service_the_page_is_the_ask: unlinked, the hero keeps its art,
    /// and there is no menu, no tag and no limits to press.</summary>
    [Fact]
    public Task UnlinkedThePageIsTheAskOverTheHeroArtWithNoMenu() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, _) =>
    {
        ChasterHead.Service = null;
        var tab = new ChasterTabView();
        Assert.NotNull(Named<Image>(tab, "HeroArt").Source);
        Assert.NotNull(Named<Border>(tab, "HeroArtFade").Background);
        Assert.True(Named<StackPanel>(tab, "UnlinkedPanel").IsVisible);
        Assert.False(Named<StackPanel>(tab, "MenuPanel").IsVisible);
        Assert.False(Named<Panel>(tab, "PaperTag").IsVisible);
        Assert.Equal(CircesTab.Format(TabLimits.Default.DailySeconds, signed: false), Named<TextBlock>(tab, "TxtFactCap1").Text);
        return Task.CompletedTask;
    }));

    /// <summary>WPF Every_row_with_a_picture_points_at_a_png_that_ships.</summary>
    [Fact]
    public Task EveryRowWithAPicturePointsAtAPngThatShips() => AvaloniaTestDispatcher.RunAsync(() => Run((_, _, _) =>
    {
        foreach (var id in TabMenuCopy.ArtIds)
            Assert.True(global::Avalonia.Platform.AssetLoader.Exists(new Uri("avares://CCP.Avalonia/Resources/" + TabMenuCopy.ArtFor(id))), id);
        return Task.CompletedTask;
    }));
}
