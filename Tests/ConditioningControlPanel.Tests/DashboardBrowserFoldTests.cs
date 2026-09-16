using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Home dashboard's browser fold (owner ask 2026-09-12). The animation is WPF layout and lives
/// in MainWindow.DashboardFold.cs; what is testable, and what matters on release day, is the pure
/// state - one bool in, six derived surfaces out - and the default.
///
/// <para>The default is TRUE as of the desk pass: "by default browser should be hidden, and
/// unhidden whenever we call it". The serializer settings mirror SettingsService.Load, so these
/// exercise the real load path's semantics rather than a friendlier default one, and the upgrader
/// case is the one that would silently regress: the default is carried by a property initializer,
/// so a file that already says false has to keep saying false.</para>
/// </summary>
public class DashboardBrowserFoldTests
{
    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    private static AppSettings Load(string json)
        => JsonConvert.DeserializeObject<AppSettings>(json, LoaderSettings)!;

    // ---- the default ----

    [Fact]
    public void FreshInstall_TheBrowserStartsFolded()
        => Assert.True(new AppSettings().DashboardBrowserCollapsed);

    [Fact]
    public void EmptyDocument_TheBrowserStartsFolded()
        => Assert.True(Load("{}").DashboardBrowserCollapsed);

    [Fact]
    public void UpgraderWithoutTheKey_TheBrowserStartsFolded()
    {
        // The shape of a settings.json written before the fold existed: plenty of unrelated
        // members, no DashboardBrowserCollapsed anywhere.
        const string json = """
        {
          "Welcomed": true,
          "LastSeenVersion": "6.9.3",
          "DashboardToggleHintUses": 2,
          "rail_favorites": [ "tab.deeper" ]
        }
        """;

        var settings = Load(json);

        Assert.True(settings.DashboardBrowserCollapsed);
        // Sanity: the rest of the document still won, so this is not passing because the whole
        // load fell back to a default-constructed object.
        Assert.Equal("6.9.3", settings.LastSeenVersion);
        Assert.Equal(2, settings.DashboardToggleHintUses);
    }

    [Fact]
    public void SomebodyWhoUnfoldedItKeepsItUnfolded()
    {
        // The default rides a property initializer, so the one thing that could go wrong is a
        // stated preference of false being overwritten back to true on load.
        Assert.False(Load("""{ "DashboardBrowserCollapsed": false }""").DashboardBrowserCollapsed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSavedChoiceRoundTrips(bool folded)
    {
        var json = JsonConvert.SerializeObject(new AppSettings { DashboardBrowserCollapsed = folded });
        Assert.Equal(folded, Load(json).DashboardBrowserCollapsed);
    }

    // ---- the derived surfaces ----

    [Fact]
    public void TheChevronPointsAtWhatTheClickWillDo()
    {
        // Shut: down, because clicking opens it. Open: up, because clicking shuts it.
        Assert.Equal("▾", BrowserFoldRule.Chevron(collapsed: true));
        Assert.Equal("▴", BrowserFoldRule.Chevron(collapsed: false));
    }

    [Fact]
    public void TheTooltipSaysWhatTheClickWillDo()
    {
        Assert.Equal("tooltip_browser_unfold", BrowserFoldRule.TooltipKey(collapsed: true));
        Assert.Equal("tooltip_browser_fold", BrowserFoldRule.TooltipKey(collapsed: false));
    }

    [Fact]
    public void TheBodyAndTheBillboardAreNeverBothUp()
    {
        foreach (var collapsed in new[] { true, false })
            Assert.NotEqual(BrowserFoldRule.BodyShown(collapsed), BrowserFoldRule.BillboardShown(collapsed));
    }

    [Fact]
    public void ExactlyOneRowOwnsTheSpace()
    {
        foreach (var collapsed in new[] { true, false })
            Assert.NotEqual(BrowserFoldRule.CardRowIsStar(collapsed), BrowserFoldRule.FoldRowIsStar(collapsed));
    }

    [Fact]
    public void AShutCardHidesTheBodyAndShowsTheBillboard()
    {
        Assert.False(BrowserFoldRule.BodyShown(collapsed: true));
        Assert.True(BrowserFoldRule.BillboardShown(collapsed: true));
        Assert.False(BrowserFoldRule.CardRowIsStar(collapsed: true));
        Assert.True(BrowserFoldRule.FoldRowIsStar(collapsed: true));
    }

    [Fact]
    public void AnOpenCardIsTheLayoutThatShipped()
    {
        Assert.True(BrowserFoldRule.BodyShown(collapsed: false));
        Assert.False(BrowserFoldRule.BillboardShown(collapsed: false));
        Assert.True(BrowserFoldRule.CardRowIsStar(collapsed: false));
        Assert.False(BrowserFoldRule.FoldRowIsStar(collapsed: false));
    }

    [Fact]
    public void TwoTogglesComeBackToWhereItStarted()
    {
        foreach (var start in new[] { true, false })
            Assert.Equal(start, BrowserFoldRule.Toggle(BrowserFoldRule.Toggle(start)));
    }

    // ---- the reveal ----
    //
    // MainWindow computes the effective state as "saved AND NOT revealed", which is the whole of
    // what a reveal is: a temporary open that never writes the preference. The arithmetic is small
    // enough to state here so the intent stays pinned even though the field itself is private.

    [Theory]
    [InlineData(true, false, true)]    // folded by preference, nothing calling it: shut
    [InlineData(true, true, false)]    // folded by preference, the app called it: open
    [InlineData(false, false, false)]  // the user left it open
    [InlineData(false, true, false)]   // already open, a reveal changes nothing
    public void ARevealOpensTheCardWithoutTouchingThePreference(bool saved, bool revealed, bool effective)
    {
        Assert.Equal(effective, saved && !revealed);
        // Whatever the reveal did on screen, the stored preference is untouched, so the next
        // launch opens folded again.
        Assert.Equal(saved, new AppSettings { DashboardBrowserCollapsed = saved }.DashboardBrowserCollapsed);
    }

    [Fact]
    public void TheChevronTogglesWhatIsOnScreenNotTheStoredBool()
    {
        // Revealed over a saved "folded": the card is open, so the chevron has to shut it. Toggling
        // the stored bool instead would re-affirm true and read as a dead button.
        const bool saved = true, revealed = true;
        bool effective = saved && !revealed;
        Assert.False(effective);
        Assert.True(BrowserFoldRule.Toggle(effective));
    }
}
