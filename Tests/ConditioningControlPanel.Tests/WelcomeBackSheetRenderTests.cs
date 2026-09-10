using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The welcome-back sheet: does it build, does it measure, and does each row appear exactly when
/// the decision says it should.
///
/// <para><b>Why this suite exists.</b> This window replaces five modal surfaces on the one launch
/// nobody can rehearse - a returning user's first run on a new machine. It is opened from the
/// startup ladder with its exceptions caught and logged, so a <c>{StaticResource}</c> resolved in
/// the wrong scope, or a style key that moved, would not crash anything: the sheet would simply
/// never appear, the backup would never be offered, and the only trace would be one warning in a
/// log file on somebody else's PC.</para>
///
/// <para>The window is never <c>Show()</c>n - layout is driven on its content root, which realizes
/// every template and resolves every resource lookup without putting a modal on a test agent's
/// desktop. Nothing here closes it either: <c>Commit</c> is production behaviour.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class WelcomeBackSheetRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    private static WelcomeBackSheetContent Content(
        WelcomeBackPlan? plan = null,
        string name = "Bambi",
        int level = 24,
        string? mod = "Circe",
        string notes = "- the race got a caption line\n- the pixel block starts off on glass",
        string? season = null,
        Action? tour = null)
        => new()
        {
            DisplayName = name,
            Level = level,
            BackupModName = mod,
            BackupTakenAt = new DateTime(2026, 9, 3, 14, 30, 0, DateTimeKind.Utc),
            Plan = plan ?? new WelcomeBackPlan(true, true, true, "mod-locked"),
            FlavourModName = mod,
            FlavourSizeText = "329 MB",
            VersionLabel = "6.8.0",
            PatchNotes = notes,
            SeasonLine = season,
            TourAction = tour,
        };

    private static WelcomeBackSheet Realize(WelcomeBackSheetContent content)
    {
        var sheet = new WelcomeBackSheet(content);

        var root = sheet.Content as FrameworkElement;
        Assert.True(root != null, "the sheet's content root is not a FrameworkElement");

        root!.Measure(new Size(560, 660));
        root.Arrange(new Rect(0, 0, 560, 660));
        root.UpdateLayout();

        Assert.True(root.DesiredSize.Height > 0, "the sheet measured to zero height - its content did not realize");
        return sheet;
    }

    private static T Find<T>(WelcomeBackSheet sheet, string name) where T : class
    {
        var found = sheet.FindName(name) as T;
        Assert.True(found != null, $"WelcomeBackSheet.{name} is missing or is no longer a {typeof(T).Name}");
        return found!;
    }

    // =====================================================================================
    //  it renders at all
    // =====================================================================================

    [Fact]
    public void TheSheetConstructsAndRealizes()
    {
        Assert.Null(PackUriBootstrap.Failure);
        OnStaThread(() => Realize(Content()));
    }

    [Fact]
    public void EveryLineOfCopyIsAssignedAndNoneRendersAsARawKey()
    {
        // Str()/StrF() fall back to their English draft, so an empty TextBlock here means the
        // assignment was lost and a value containing "wb_" means the fallback broke.
        var names = new[]
        {
            "TxtHeading", "TxtSubline",
            "TxtRestoreLabel", "TxtRestoreHint",
            "TxtFlavourLabel", "TxtFlavourHint",
            "TxtWhatsNewToggle", "TxtNotes", "TxtTour",
        };

        OnStaThread(() =>
        {
            var sheet = Realize(Content(tour: () => { }));
            foreach (var name in names)
            {
                var text = Find<TextBlock>(sheet, name).Text;
                Assert.False(string.IsNullOrWhiteSpace(text), $"{name} rendered empty");
                Assert.DoesNotContain("wb_", text, StringComparison.Ordinal);
            }

            var go = Find<Button>(sheet, "BtnGo").Content as string;
            Assert.False(string.IsNullOrWhiteSpace(go), "the primary button has no label");
        });
    }

    [Fact]
    public void TheHeadingCarriesTheNameAndTheSublineTheBackup()
    {
        // wb_heading and wb_sub_* are the format strings on this screen, and a FormatException is
        // caught and would silently render the raw template - so assert the substitution, not just
        // that something is there.
        OnStaThread(() =>
        {
            var sheet = Realize(Content());

            var heading = Find<TextBlock>(sheet, "TxtHeading").Text;
            Assert.Contains("Bambi", heading, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", heading, StringComparison.Ordinal);

            var sub = Find<TextBlock>(sheet, "TxtSubline").Text;
            Assert.Contains("24", sub, StringComparison.Ordinal);
            Assert.Contains("Circe", sub, StringComparison.Ordinal);
            Assert.Contains("2026", sub, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", sub, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AnAnonymousReturnIsStillASentence()
    {
        OnStaThread(() =>
        {
            var sheet = Realize(Content(name: "", level: 0, mod: null));
            var heading = Find<TextBlock>(sheet, "TxtHeading").Text;

            Assert.False(string.IsNullOrWhiteSpace(heading));
            Assert.DoesNotContain("{0}", heading, StringComparison.Ordinal);
            Assert.DoesNotContain(",", heading, StringComparison.Ordinal);
        });
    }

    // =====================================================================================
    //  the rows appear exactly when the decision says so
    // =====================================================================================

    [Fact]
    public void TheRowsFollowThePlan()
    {
        OnStaThread(() =>
        {
            var both = Realize(Content(new WelcomeBackPlan(true, true, true, "mod-locked")));
            Assert.Equal(Visibility.Visible, Find<Border>(both, "RowRestore").Visibility);
            Assert.Equal(Visibility.Visible, Find<Border>(both, "RowFlavour").Visibility);

            var neither = Realize(Content(new WelcomeBackPlan(true, false, false, null)));
            Assert.Equal(Visibility.Collapsed, Find<Border>(neither, "RowRestore").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<Border>(neither, "RowFlavour").Visibility);
        });
    }

    [Fact]
    public void AShownToggleStartsTicked()
    {
        // Both offers are what the user almost always wants on a new PC. Making them opt-in would
        // turn one sheet into a sheet plus a trip through the Mod Manager and Settings.
        OnStaThread(() =>
        {
            var sheet = Realize(Content());
            Assert.True(Find<CheckBox>(sheet, "ChkRestore").IsChecked);
            Assert.True(Find<CheckBox>(sheet, "ChkFlavour").IsChecked);
        });
    }

    [Fact]
    public void AHiddenRowCanNeverReportAChoice()
    {
        // The commit reads the plan as well as the box, so a stale IsChecked on a Collapsed row
        // cannot restore settings nobody was offered.
        OnStaThread(() =>
        {
            var sheet = Realize(Content(new WelcomeBackPlan(true, false, false, null)));
            Assert.False(sheet.RestoreChosen);
            Assert.False(sheet.BringFlavourChosen);
        });
    }

    // =====================================================================================
    //  progressive disclosure
    // =====================================================================================

    [Fact]
    public void ThePatchNotesStartFoldedAway()
    {
        // Lead with capability, not with a wall of release notes. The notes are one click away.
        OnStaThread(() =>
        {
            var sheet = Realize(Content());
            Assert.Equal(Visibility.Visible, Find<StackPanel>(sheet, "RowWhatsNew").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<Border>(sheet, "NotesPanel").Visibility);
        });
    }

    [Fact]
    public void NoNotesMeansNoWhatChangedRowAtAll()
    {
        OnStaThread(() =>
        {
            var sheet = Realize(Content(notes: ""));
            Assert.Equal(Visibility.Collapsed, Find<StackPanel>(sheet, "RowWhatsNew").Visibility);
        });
    }

    [Fact]
    public void TheTourIsOfferedOnlyWhenThereIsATourToRun()
    {
        OnStaThread(() =>
        {
            Assert.Equal(Visibility.Collapsed, Find<TextBlock>(Realize(Content()), "TxtTour").Visibility);
            Assert.Equal(Visibility.Visible, Find<TextBlock>(Realize(Content(tour: () => { })), "TxtTour").Visibility);
        });
    }

    // =====================================================================================
    //  the season line
    // =====================================================================================

    [Fact]
    public void TheSeasonLineIsHiddenUnlessTheCallerHandsOneOver()
    {
        // The sheet decides nothing about seasons: WelcomeBackDecision.ShouldShowSeasonLine does,
        // at the call site, off the server's confirmed key. Null here is the normal case.
        OnStaThread(() =>
        {
            Assert.Equal(Visibility.Collapsed, Find<TextBlock>(Realize(Content()), "TxtSeasonLine").Visibility);

            var told = Realize(Content(season: "The monthly leaderboard rotated to season 2026-09 while you were away."));
            var line = Find<TextBlock>(told, "TxtSeasonLine");
            Assert.Equal(Visibility.Visible, line.Visibility);
            Assert.Contains("2026-09", line.Text, StringComparison.Ordinal);
        });
    }
}
