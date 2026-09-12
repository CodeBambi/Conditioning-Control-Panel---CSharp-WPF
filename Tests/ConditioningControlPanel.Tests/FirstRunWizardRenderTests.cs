using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The first-run wizard: two steps, Welcome and Flavour, and the 18+ gate that used to be a
/// MessageBox in front of it.
///
/// <para><b>Why this suite exists.</b> This screen is seen exactly once per install, by a user who
/// has never run the app, and only on a fresh box - which is the hardest thing in this codebase to
/// exercise by hand and the easiest to ship broken. Four failure modes it guards:</para>
/// <list type="number">
/// <item>A <c>{StaticResource}</c> or a converter declared in the wrong scope throws inside
/// <c>InitializeComponent()</c>. The wizard opens from MainWindow's constructor path with its
/// exceptions swallowed and logged, so a fresh install would silently get NO first run at all and
/// nobody would hear about it.</item>
/// <item>A step that measures to zero height. The window is a fixed 900x680 with
/// <c>WindowStyle="None"</c>, so a collapsed step reads as an empty pink box rather than an
/// error.</item>
/// <item>The flavour card list going empty or losing its single-select invariant - the mod choice
/// is committed on close, so "nothing selected" and "two selected" are both real data bugs.</item>
/// <item>The gate coming un-gated. Enter must be dead until the 18+ box is ticked; a wizard that
/// ships with it enabled is an app with no age check at all, because App.OnStartup's MessageBox no
/// longer covers this population.</item>
/// </list>
///
/// <para>The window is never <c>Show()</c>n: layout is driven on its content root, which is enough
/// to realize every template and resolve every resource lookup, and avoids putting a real HWND (and
/// a modal) on a test agent's desktop. Closing is deliberately skipped too - <c>Closed</c> runs the
/// gate verdict and <c>CommitModChoice</c>, which are production behaviour (including
/// <c>Application.Current.Shutdown()</c>) that has no business firing in a test.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class FirstRunWizardRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>
    /// The constructor is private (the entry points are <c>ShouldRunAndClaim</c> + <c>Run</c>), and
    /// deliberately so - this reaches past that rather than widening the production surface for a
    /// test. A null owner is a supported argument: the parameter is <c>MainWindow?</c> and every
    /// use of it in the class is null-conditional.
    /// </summary>
    private static FirstRunWizard NewWizard()
    {
        var ctor = typeof(FirstRunWizard).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(MainWindow) },
            modifiers: null);

        Assert.True(ctor != null,
            "FirstRunWizard(MainWindow?) is gone - MainWindow's first-launch branch constructs exactly this");

        return (FirstRunWizard)ctor!.Invoke(new object?[] { null });
    }

    /// <summary>Drives layout on the window's content root; see the class remarks for why not Show().</summary>
    private static FrameworkElement Realize(Window window)
    {
        var root = window.Content as FrameworkElement;
        Assert.True(root != null, "the wizard's content root is not a FrameworkElement");

        root!.Measure(new Size(900, 680));
        root.Arrange(new Rect(0, 0, 900, 680));
        root.UpdateLayout();

        Assert.True(root.DesiredSize.Height > 0,
            "the wizard measured to zero height - its content did not realize");
        return root;
    }

    private static T Find<T>(FirstRunWizard w, string name) where T : class
    {
        var found = w.FindName(name) as T;
        Assert.True(found != null, $"FirstRunWizard.{name} is missing or is no longer a {typeof(T).Name}");
        return found!;
    }

    // =====================================================================================
    //  it renders at all
    // =====================================================================================

    [Fact]
    public void TheWizardConstructsAndRealizes()
    {
        Assert.Null(PackUriBootstrap.Failure);
        OnStaThread(() => Realize(NewWizard()));
    }

    [Fact]
    public void EveryStepRealizesWithRealHeight()
    {
        // Step 2 is Collapsed at construction, so the first render only proves step 1. A step that
        // throws (or measures to nothing) would otherwise only show up when a real first-run user
        // pressed Enter.
        OnStaThread(() =>
        {
            var w = NewWizard();
            Realize(w);

            var steps = new[] { Find<Grid>(w, "Step1"), Find<Grid>(w, "Step2") };
            foreach (var step in steps)
            {
                step.Visibility = Visibility.Visible;
                step.Measure(new Size(848, 560));
                step.Arrange(new Rect(0, 0, 848, 560));
                step.UpdateLayout();
                Assert.True(step.DesiredSize.Height > 0, $"{step.Name} measured to zero height");
            }
        });
    }

    [Fact]
    public void ItOpensOnTheWelcomeStepWithTheFlavourStepPutAway()
    {
        OnStaThread(() =>
        {
            var w = NewWizard();
            Assert.Equal(Visibility.Visible, Find<Grid>(w, "Step1").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<Grid>(w, "Step2").Visibility);
        });
    }

    [Fact]
    public void TheDoorsStepIsGone()
    {
        // The seven-doors list and its "Take the tour" button left with the redesign: one tour,
        // offered once, by EMI, from her chip. Re-adding either here is re-adding the second
        // tutorial the owner complained about.
        OnStaThread(() =>
        {
            var w = NewWizard();
            Assert.Null(w.FindName("Step3"));
            Assert.Null(w.FindName("DoorsHost"));
        });

        Assert.Null(typeof(FirstRunWizard).GetProperty("StartTourRequested"));
    }

    // =====================================================================================
    //  the copy: every string is assigned, none renders as a raw key
    // =====================================================================================

    [Fact]
    public void EveryTextBlockOnTheWizardCarriesRealCopy()
    {
        // Str()/StrF() fall back to their English draft, so an empty TextBlock here means the
        // assignment itself was lost, and a value equal to the key means the fallback broke.
        var names = new[]
        {
            "TxtWizardTitle", "TxtStepCounter", "TxtAppTitle", "TxtWelcomeHeading", "TxtWelcomeBody",
            "TxtLanguageLabel", "TxtLanguageHint", "TxtFolderLabel", "TxtFolderHint",
            "TxtAgeConfirm", "TxtCloseHint",
            "TxtModHeading", "TxtModSub", "TxtModHint",
        };

        OnStaThread(() =>
        {
            var w = NewWizard();
            foreach (var name in names)
            {
                var text = Find<TextBlock>(w, name).Text;
                Assert.False(string.IsNullOrWhiteSpace(text), $"{name} rendered empty");
                Assert.DoesNotContain("fr8_", text, StringComparison.Ordinal);
            }

            var link = Find<System.Windows.Documents.Run>(w, "RunContentPolicy").Text;
            Assert.False(string.IsNullOrWhiteSpace(link), "the content policy link rendered empty");
        });
    }

    [Fact]
    public void TheStepCounterCountsTheStepsThatExist()
    {
        // fr8_wizard_step_of is the one format string on the screen, and ko.json reorders its two
        // placeholders. A FormatException there is caught and would silently render the raw
        // template - so assert the substituted numbers, not just non-emptiness.
        OnStaThread(() =>
        {
            var text = Find<TextBlock>(NewWizard(), "TxtStepCounter").Text;
            Assert.Contains("1", text, StringComparison.Ordinal);
            Assert.Contains("2", text, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", text, StringComparison.Ordinal);
        });
    }

    // =====================================================================================
    //  step 1: the gate, the language row, the folder row
    // =====================================================================================

    [Fact]
    public void EnterIsDeadUntilTheAgeBoxIsTicked()
    {
        // The whole point of the redesign's "one gate": this button IS the age check. If it ships
        // enabled, a fresh install walks straight past a check nothing else performs any more.
        OnStaThread(() =>
        {
            var w = NewWizard();
            Realize(w);

            var enter = Find<Button>(w, "BtnNext");
            var box = Find<CheckBox>(w, "ChkAgeConfirm");

            Assert.NotEqual(true, box.IsChecked);
            Assert.False(enter.IsEnabled, "Enter was live before anyone confirmed their age");

            box.IsChecked = true;
            Assert.True(enter.IsEnabled, "ticking the 18+ box did not release Enter");

            box.IsChecked = false;
            Assert.False(enter.IsEnabled, "un-ticking the 18+ box left Enter live");
        });
    }

    [Fact]
    public void TheWelcomeStepOffersNoWayPastTheGateButEnter()
    {
        // No "Skip setup" on step 1: the only alternatives to accepting are closing the window
        // (which shuts the app down) and the muted line that says so.
        OnStaThread(() =>
        {
            var w = NewWizard();
            Assert.Equal(Visibility.Collapsed, Find<Button>(w, "BtnSkip").Visibility);
            Assert.Equal(Visibility.Visible, Find<TextBlock>(w, "TxtCloseHint").Visibility);
        });
    }

    [Fact]
    public void TheLanguageRowOffersEveryLanguageTheAppHas()
    {
        // Same list as the title-bar pill and Settings, from MainWindow.FillLanguageCombo. A row
        // that lists its own subset is the drift this reuse exists to prevent.
        OnStaThread(() =>
        {
            var combo = Find<ComboBox>(NewWizard(), "CmbWizardLanguage");
            Assert.Equal(LocalizationManager.AvailableLanguages.Length, combo.Items.Count);

            var tags = combo.Items.Cast<ComboBoxItem>().Select(i => i.Tag as string).ToList();
            Assert.Equal(LocalizationManager.AvailableLanguages.Select(l => l.Code).ToList(), tags);

            // Something must be selected, or the first SelectionChanged is a language switch to
            // nothing.
            Assert.True(combo.SelectedIndex >= 0, "the language row opened with no language selected");
        });
    }

    [Fact]
    public void TheContentPolicyLinkPointsAtTheOnePolicyUrl()
    {
        // The wizard links to the same constant the moderation warning opens. Two copies of this
        // URL is one copy that gets left behind when the site moves.
        Assert.Equal("https://app.cclabs.app/policies/prohibited-content",
                     ContentPolicyWarningDialog.PolicyUrl);
    }

    // =====================================================================================
    //  step 2: the flavour cards
    // =====================================================================================

    [Fact]
    public void TheFlavourStepOffersEveryCatalogueEntryExactlyOnce()
    {
        OnStaThread(() =>
        {
            var list = Find<ItemsControl>(NewWizard(), "ModCardsList");
            var items = ((IEnumerable)list.ItemsSource!).Cast<object>().ToList();

            Assert.Equal(ModPackCatalog.All.Count, items.Count);

            var ids = items.Select(i => (string)i.GetType().GetProperty("ModId")!.GetValue(i)!).ToList();
            Assert.Equal(ModPackCatalog.All.Select(e => e.ModId).ToList(), ids);
        });
    }

    [Fact]
    public void ExactlyOneModCardIsSelectedOnArrival()
    {
        // Single-select is the whole contract of this step (the picker's multi-select download
        // queue is a different screen). Zero selected means pressing Enter commits nothing; two
        // means CommitModChoice picks by accident.
        OnStaThread(() =>
        {
            var list = Find<ItemsControl>(NewWizard(), "ModCardsList");
            var items = ((IEnumerable)list.ItemsSource!).Cast<object>().ToList();
            var selected = items.Count(i => (bool)i.GetType().GetProperty("IsSelected")!.GetValue(i)!);
            Assert.Equal(1, selected);
        });
    }

    [Fact]
    public void TheInBoxModIsNeverPresentedAsADownload()
    {
        // CCP Default has no PackId: it ships in the installer and must always be a legitimate,
        // already-installed choice, or a fresh offline install has nothing it can pick.
        OnStaThread(() =>
        {
            var list = Find<ItemsControl>(NewWizard(), "ModCardsList");
            var card = ((IEnumerable)list.ItemsSource!).Cast<object>()
                .First(i => (string)i.GetType().GetProperty("ModId")!.GetValue(i)! == BuiltInMods.CCPDefaultId);

            Assert.False((bool)card.GetType().GetProperty("HasPack")!.GetValue(card)!);
            Assert.True((bool)card.GetType().GetProperty("IsInstalled")!.GetValue(card)!);
        });
    }

    // =====================================================================================
    //  the claim (pure - no WPF)
    // =====================================================================================

    [Fact]
    public void TheClaimIsSilentWhenThereAreNoSettingsToRead()
    {
        // App.Settings is null in the test host, which is the same shape as the "settings failed to
        // load" case on a real box. The claim must answer "no first run" rather than throw: it is
        // called from MainWindow's constructor, where an exception is a failed launch.
        Assert.False(FirstRunWizard.ShouldRunAndClaim());
        FirstRunWizard.HandBackFirstRun("unit test");   // must not throw either
    }

    [Fact]
    public void AppStartupCanTellWhoseLaunchThisIs()
    {
        // App.OnStartup's age MessageBox reads this to stay off a fresh install's screen: by the
        // time it runs, ShouldRunAndClaim has already set Welcomed = true for this same launch, so
        // the flag alone cannot tell the two populations apart.
        Assert.NotNull(typeof(FirstRunWizard).GetProperty("FirstRunClaimedThisLaunch",
            BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void TheUpgraderPathStillGoesThroughTheStandaloneModPicker()
    {
        // The wizard calls ModPickerDialog's guard predicates instead of restating them, and
        // MainWindow's else branch still opens the dialog itself for installs that arrive already
        // Welcomed. Deleting ModPickerDialog as "replaced by the wizard" would break both.
        Assert.NotNull(typeof(ModPickerDialog).GetMethod("ShowIfNeeded",
            BindingFlags.Public | BindingFlags.Static));
        Assert.True(ModPickerDialog.MaxOfflineOffers > 0);
        Assert.True(ModPickerDialog.ShouldDeferForOffline(offlineMode: true, manifestUnavailable: false, offlineOffers: 0));
    }
}
