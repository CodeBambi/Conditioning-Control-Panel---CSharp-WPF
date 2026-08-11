using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// UX restructure Phase 8 — the first-run wizard that replaces the up-to-ten popup gauntlet.
///
/// <para><b>Why this suite exists.</b> This screen is seen exactly once per install, by a user who
/// has never run the app, and only on a fresh box — which is the hardest thing in this codebase to
/// exercise by hand and the easiest to ship broken. Three failure modes it guards:</para>
/// <list type="number">
/// <item>A <c>{StaticResource}</c> or a converter declared in the wrong scope throws inside
/// <c>InitializeComponent()</c>. The wizard opens from MainWindow's constructor path with its
/// exceptions swallowed and logged, so a fresh install would silently get NO first run at all and
/// nobody would hear about it.</item>
/// <item>A step that measures to zero height. The window is a fixed 900x680 with
/// <c>WindowStyle="None"</c>, so a collapsed step reads as an empty pink box rather than an
/// error.</item>
/// <item>The step-2 card list going empty or losing its single-select invariant — the mod choice is
/// committed on close, so "nothing selected" and "two selected" are both real data bugs.</item>
/// </list>
///
/// <para>The window is never <c>Show()</c>n: layout is driven on its content root, which is enough
/// to realize every template and resolve every resource lookup, and avoids putting a real HWND (and
/// a modal) on a test agent's desktop. Closing is deliberately skipped too — <c>Closed</c> runs
/// <c>CommitModChoice</c>, which is production behaviour that has no business firing in a test.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class FirstRunWizardRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>
    /// The constructor is private (the entry points are <c>ShouldRunAndClaim</c> + <c>Run</c>), and
    /// deliberately so — this reaches past that rather than widening the production surface for a
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
            "FirstRunWizard(MainWindow?) is gone — MainWindow's first-launch branch constructs exactly this");

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
            "the wizard measured to zero height — its content did not realize");
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
        // Steps 2 and 3 are Collapsed at construction, so the first render only proves step 1.
        // A step that throws (or measures to nothing) would otherwise only show up when a real
        // first-run user pressed Next.
        OnStaThread(() =>
        {
            var w = NewWizard();
            Realize(w);

            var steps = new[] { Find<Grid>(w, "Step1"), Find<Grid>(w, "Step2"), Find<Grid>(w, "Step3") };
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
    public void ItOpensOnStepOneWithTheOtherTwoPutAway()
    {
        OnStaThread(() =>
        {
            var w = NewWizard();
            Assert.Equal(Visibility.Visible, Find<Grid>(w, "Step1").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<Grid>(w, "Step2").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<Grid>(w, "Step3").Visibility);

            // Back is meaningless on the first step and must not be offered.
            Assert.Equal(Visibility.Collapsed, Find<Button>(w, "BtnBack").Visibility);
        });
    }

    // =====================================================================================
    //  the copy: every string is assigned, none renders as a raw key
    // =====================================================================================

    [Fact]
    public void EveryTextBlockOnTheWelcomeStepCarriesRealCopy()
    {
        // Str()/StrF() fall back to their English draft, so an empty TextBlock here means the
        // assignment itself was lost, and a value equal to the key means the fallback broke.
        var names = new[]
        {
            "TxtWizardTitle", "TxtStepCounter", "TxtAppTitle", "TxtWelcomeHeading", "TxtWelcomeBody",
            "TxtTipsTitle", "TxtTipHelp", "TxtTipHover", "TxtTipAssets", "TxtPerfTitle", "TxtPerfBody",
            "TxtModHeading", "TxtModSub", "TxtModHint", "TxtTourHeading", "TxtTourOutro",
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
        });
    }

    [Fact]
    public void TheStepCounterCountsTheStepsThatExist()
    {
        // fr8_wizard_step_of is the one format string on the screen, and ko.json reorders its two
        // placeholders. A FormatException there is caught and would silently render the raw
        // template — so assert the substituted numbers, not just non-emptiness.
        OnStaThread(() =>
        {
            var text = Find<TextBlock>(NewWizard(), "TxtStepCounter").Text;
            Assert.Contains("1", text, StringComparison.Ordinal);
            Assert.Contains("3", text, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", text, StringComparison.Ordinal);
        });
    }

    // =====================================================================================
    //  step 2: the mod cards
    // =====================================================================================

    [Fact]
    public void TheModStepOffersEveryCatalogueEntryExactlyOnce()
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
        // queue is a different screen). Zero selected means pressing Next commits nothing; two
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
    //  step 3: the doors
    // =====================================================================================

    [Fact]
    public void TheDoorsStepListsAllSevenDoors()
    {
        // Seven rows, built in code from the Doors table. Six would mean a door the first-run user
        // is never told exists.
        OnStaThread(() =>
        {
            var host = Find<Panel>(NewWizard(), "DoorsHost");
            Assert.Equal(7, host.Children.Count);
        });
    }

    [Fact]
    public void EveryDoorRowCarriesALabelAndABlurb()
    {
        OnStaThread(() =>
        {
            var w = NewWizard();
            Realize(w);

            var host = Find<Panel>(w, "DoorsHost");
            foreach (var child in host.Children.OfType<FrameworkElement>())
            {
                var texts = Descendants(child).OfType<TextBlock>()
                                              .Select(t => t.Text)
                                              .Where(t => !string.IsNullOrWhiteSpace(t))
                                              .ToList();
                // glyph + label + blurb
                Assert.True(texts.Count >= 3, "a door row is missing its label or blurb: " + string.Join(" | ", texts));
                Assert.DoesNotContain(texts, t => t.Contains("fr8_", StringComparison.Ordinal)
                                               || t.Contains("nav_door_", StringComparison.Ordinal));
            }
        });
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    // =====================================================================================
    //  the gate (pure — no WPF)
    // =====================================================================================

    [Fact]
    public void TheGateIsSilentWhenThereAreNoSettingsToRead()
    {
        // App.Settings is null in the test host, which is the same shape as the "settings failed to
        // load" case on a real box. The gate must answer "no first run" rather than throw: it is
        // called from MainWindow's constructor, where an exception is a failed launch.
        Assert.False(FirstRunWizard.ShouldRunAndClaim());
        FirstRunWizard.HandBackFirstRun("unit test");   // must not throw either
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
