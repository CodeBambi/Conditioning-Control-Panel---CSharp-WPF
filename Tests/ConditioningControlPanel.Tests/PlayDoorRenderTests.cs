using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// UX restructure Phase 6 — the Play door, and the retirement of the Lab view.
///
/// <para><b>Why this suite exists.</b> Three failure modes survive a clean compile on this phase.
/// (1) A <c>{StaticResource}</c> key that lived in the retired <c>LabTabView</c>'s own
/// <c>Grid.Resources</c> does not follow a card into <see cref="PlayTabView"/>, and StaticResource
/// resolves while the tree is built — so it throws the first time a user opens the door, never at
/// compile time. (2) An <c>x:Name</c> that <c>MainWindow.PlayTab.cs</c>, <c>MainWindow.LabTab.cs</c>
/// or <c>MainWindow.Presets.cs</c> dereferences through <c>PlayTab.&lt;name&gt;</c> compiles fine
/// and then NullReferences at runtime; the Lab retirement re-pointed a dozen of those in one pass.
/// (3) The lockband contract is invertible and silent: a band that defaults to Visible hides a card
/// nobody paid for, and a band that swallows the click turns a paid upsell into a dead end.</para>
///
/// <para>The key-alias tests are the other half, and they are the ones that cannot be caught by
/// looking at the screen. <c>ShowTab("lab")</c> is API — 54 bark rules per built-in mod, ~60
/// tutorial steps, the ambient-FX registry and the Ctrl+K palette are all keyed off tab strings —
/// so the alias pair has to point in opposite directions: <c>BarkTabAliases["play"] = "lab"</c>
/// (the barks keep hearing the OLD key) and <c>CanonicalTabKey("lab") = "play"</c> (anything keyed
/// to a VIEW gets the NEW one). Getting those the same way round parks the Play canvas on arrival
/// and leaves it running off-screen on the way out.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PlayDoorRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>
    /// A bare host — the theme lives on <c>Application.Resources</c> where App.xaml puts it and
    /// where a parse-time StaticResource looks. Merging a second copy here would shadow it with a
    /// broken one (see <see cref="WpfRenderHarness"/>).
    /// </summary>
    private static void Realize(FrameworkElement element, double width = 1469, double height = 891)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);

        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();

        Assert.True(element.DesiredSize.Height > 0,
            $"{element.GetType().Name} measured to zero height — its content did not realize");
    }

    // =====================================================================================
    //  the page
    // =====================================================================================

    [Fact]
    public void TheHarnessCanResolveTheAppsShortFormPackUris()
    {
        // Checked first and on its own, because when this redirect stops working every render
        // test below fails with "Cannot locate resource 'resources/features/...'" — which reads
        // exactly like a deleted art file and would send the next reader hunting the wrong bug.
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
    }

    [Fact]
    public void TheWholePlayDoorRealizes()
    {
        // The whole wall in one tree (the 0812 remake trimmed it to nine cards across eleven
        // slots). This is the test that would catch a card whose
        // StaticResource lived in LabTabView's Grid.Resources: a UserControl's XAML is parsed
        // before it is attached to MainWindow, so a key that did not travel never resolves.
        OnStaThread(() => Realize(new PlayTabView()));
    }

    [Fact]
    public void TheWallOnlyEverScrollsVertically()
    {
        // The MainWindow canvas is a fixed 1489x901 Viewbox (PLAN §2.8) and PlayTab sits in the
        // content column with Margin="10,5,10,10", so 1469px is all the wall ever gets. Horizontal
        // scrolling must stay Disabled — that is the constraint the zones are authored against,
        // and turning it on would trade a visible layout bug for an invisible one.
        OnStaThread(() =>
        {
            var page = new PlayTabView();
            Realize(page);

            var scroller = FindDescendant<ScrollViewer>(page);
            Assert.True(scroller != null, "the Play wall lost its ScrollViewer — a tall wall would simply clip");
            Assert.Equal(ScrollBarVisibility.Disabled, scroller!.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
        });
    }

    [Fact]
    public void EveryCardGetsTheColumnWidthItsZoneContractsFor()
    {
        // The floors below are what the zones' fixed Columns counts produce at the real canvas
        // width (1429px of content after the StackPanel's 20px margin: /3 = 476, /2 = 714). A zone
        // that quietly gained a column, or a card that escaped into the wrong zone, halves a
        // card's width — and the wall's own comment says the fixed Columns exist precisely so a
        // hidden neighbour cannot restretch the row. Since the 0812 remake: TOGETHER is the
        // 2-column zone, EYES and SESSIONS are the 3-column ones, and DTRH / the webcam chip /
        // the Loom strip span the whole wall.
        //
        // The real clip sweep still belongs to the UIA play-test; this only proves the geometry
        // cannot regress below the contracted floor.
        OnStaThread(() =>
        {
            var page = new PlayTabView();
            Realize(page);

            foreach (var name in SlotNames)
            {
                var slot = (FrameworkElement)page.FindName(name)!;
                if (slot.Visibility != Visibility.Visible) continue;

                var floor = name switch
                {
                    "SlotDtrh" or "SlotWebcamChip" or "SlotLoom" => 900.0,
                    "SlotGoon" or "SlotRemoteControl" => 640.0,
                    _ => 400.0,
                };

                Assert.True(slot.ActualWidth >= floor,
                    $"{name} measured {slot.ActualWidth:F0}px, below its zone's {floor:F0}px floor "
                    + "— a column count changed and its card will crowd");
            }
        });
    }

    [Fact]
    public void EveryNamedElementOnTheWallResolves()
    {
        // Reflective on purpose. MainWindow partials dereference these as PlayTab.<name> with no
        // null guard on the inner names (MainWindow.PlayTab.cs, .LabTab.cs, .Presets.cs,
        // .xaml.cs's playHeroMap), and a hand-written list is exactly the thing that goes stale
        // when the next card lands in a Slot.
        OnStaThread(() =>
        {
            var page = new PlayTabView();

            // DeclaredOnly: without it the sweep also picks up FrameworkElement's own private
            // fields (_parent, _templatedParent), which are legitimately null on an unparented
            // control and would fail every run.
            var fields = typeof(PlayTabView)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                           | BindingFlags.DeclaredOnly)
                .Where(f => typeof(DependencyObject).IsAssignableFrom(f.FieldType))
                .ToList();

            // ~57 named elements since the 0812 remake dropped five cards; the floor exists to
            // catch the sweep silently seeing zero BAML fields, not to pin the exact count.
            Assert.True(fields.Count >= 50,
                $"only {fields.Count} named elements found on PlayTabView — the sweep is not seeing the BAML fields");

            var broken = fields.Where(f => f.GetValue(page) == null).Select(f => f.Name).ToList();
            Assert.True(broken.Count == 0,
                "named elements that did not resolve (a MainWindow partial will NullReference on these): "
                + string.Join(", ", broken));
        });
    }

    // =====================================================================================
    //  the slots
    // =====================================================================================

    /// <summary>
    /// The wall's seams. Duplicated here on purpose: this list IS the assertion, and a slot that
    /// quietly disappears takes its card with it.
    /// </summary>
    private static readonly string[] SlotNames =
    {
        "SlotDtrh", "SlotGoon", "SlotRemoteControl", "SlotWebcamChip",
        "SlotGaze", "SlotFocusGaze", "SlotBlinkTrainer",
        "SlotGradedIntake", "SlotFyp", "SlotLockdown", "SlotLoom",
    };

    [Fact]
    public void EverySlotExistsAndCarriesACard()
    {
        OnStaThread(() =>
        {
            var page = new PlayTabView();
            Realize(page);

            foreach (var name in SlotNames)
            {
                var slot = page.FindName(name) as Grid;
                Assert.True(slot != null, $"{name} is missing from PlayTabView.xaml");
                Assert.True(slot!.Children.Count > 0, $"{name} is an empty slot — its card never landed");
            }
        });
    }

    [Fact]
    public void EveryCardsPrimaryButtonIsReachableAndEnabled()
    {
        // A card whose button is disabled is a dead door: the Play wall's contract is that the
        // click ALWAYS reaches the handler and TierGate does the refusing, so nothing on this
        // surface may be disabled up front. (The Goon perk sub-lines are dimmed, never disabled —
        // they are TextBlocks, not buttons, so they are not in this set.)
        var buttons = new[]
        {
            "BtnPlayFallIn", "BtnPlayQuickDrop", "BtnPlayGoon", "BtnPlayRemoteControl",
            "BtnPlayGazeMinigame", "BtnPlayGradedIntake", "BtnPlayBlinkTrainer",
            "BtnPlayFyp", "BtnPlayLockdown", "BtnPlayLoom",
        };

        OnStaThread(() =>
        {
            var page = new PlayTabView();
            Realize(page);

            foreach (var name in buttons)
            {
                var btn = page.FindName(name) as ButtonBase;
                Assert.True(btn != null, $"{name} is missing — its card cannot be launched");
                Assert.True(btn!.IsEnabled, $"{name} ships disabled; the wall gates in the handler, never in the markup");
            }

            // Focus Gaze is the one switch rather than a button, and it must start unchecked and
            // live: its Tier 2 bar is on the handler's ON branch (MainWindow.LabTab.cs), and a
            // pre-disabled toggle would never reach it.
            var chk = page.FindName("ChkPlayFocusGaze") as CheckBox;
            Assert.True(chk != null, "ChkPlayFocusGaze is missing — Focus Gaze has no switch anywhere in the app");
            Assert.True(chk!.IsEnabled);
            Assert.NotEqual(true, chk.IsChecked);
        });
    }

    // =====================================================================================
    //  lockbands
    // =====================================================================================

    /// <summary>Every band <c>RefreshPlayCards</c> paints. The seven TierGate ones plus the
    /// intake's, which is driven by pass state rather than tier.</summary>
    private static readonly string[] LockbandNames =
    {
        "PlayLockDtrh", "PlayLockGaze", "PlayLockFocusGaze", "PlayLockRemote",
        "PlayLockLockdown", "PlayLockBlink", "PlayLockFyp", "PlayLockIntake",
    };

    [Fact]
    public void EveryLockbandStartsCollapsedAndNeverSwallowsTheClick()
    {
        // Both halves are silent when wrong. A band that defaults to Visible hides a card the
        // account may own; a band that is hit-test visible turns the whole upsell into a dead end
        // — the click never reaches the handler, so no "See tiers" toast is ever raised.
        OnStaThread(() =>
        {
            var page = new PlayTabView();
            Realize(page);

            foreach (var name in LockbandNames)
            {
                var band = page.FindName(name) as FrameworkElement;
                Assert.True(band != null, $"{name} is missing — its card can never show a lock");
                Assert.Equal(Visibility.Collapsed, band!.Visibility);
                Assert.False(band.IsHitTestVisible,
                    $"{name} is hit-test visible — a locked click would be swallowed and no upsell toast would fire");
            }
        });
    }

    [Fact]
    public void TheTierPaletteIsTheRailsToTheCharacter()
    {
        // Gold = Tier 1, violet = Tier 2, everywhere in the app. The Play set is a deliberate twin
        // of the rail's RailLock* (SettingsTabView.xaml) until a Phase 7/8 cleanup promotes both
        // into the theme; twins drift, so the values are asserted rather than trusted.
        OnStaThread(() =>
        {
            var page = new PlayTabView();
            var res = ((FrameworkElement)page.Content).Resources;

            Assert.Equal(Color.FromArgb(0xFF, 0xF0, 0xC2, 0x4B), ((SolidColorBrush)res["PlayLockT1Brush"]).Color);
            Assert.Equal(Color.FromArgb(0xFF, 0xB4, 0x7B, 0xFF), ((SolidColorBrush)res["PlayLockT2Brush"]).Color);
            Assert.Equal(Color.FromArgb(0xA8, 0x12, 0x0A, 0x1E), ((SolidColorBrush)res["PlayLockScrimBrush"]).Color);
        });
    }

    [Fact]
    public void NoLockbandCarriesAClock()
    {
        // FX law: a lockband is a static state. A Storyboard here would have to be threaded into
        // the motion kill-switch (SwitchTabFx("")) and it is not — so there must not be one.
        OnStaThread(() =>
        {
            var page = new PlayTabView();
            Realize(page);

            foreach (var name in LockbandNames)
            {
                var band = (FrameworkElement)page.FindName(name)!;
                Assert.Empty(band.Triggers);
            }
        });
    }

    // =====================================================================================
    //  art
    // =====================================================================================

    /// <summary>
    /// The brushes <c>LoadFeatureImages</c>'s <c>playHeroMap</c> mutates in place, and the resource
    /// path each one is fed. The paths are the mod compatibility surface (PLAN §2.4) and are never
    /// renamed to match the room the card now hangs in.
    /// </summary>
    private static readonly (string Brush, string Path)[] HeroBrushes =
    {
        ("PlayGazeHeroBrush",     "features/lab_gaze_hero.png"),
        ("PlayFocusHeroBrush",    "features/lab_focusgaze_hero.png"),
        ("PlayGoonHeroBrush",     "features/goon_game.png"),
        ("PlayIntakeHeroBrush",   "features/lab_quiz_hero.png"),
        ("PlayBlinkHeroBrush",    "features/blink_trainer.png"),
        ("PlayRemoteHeroBrush",   "features/remote_control.png"),
        ("PlayFypHeroBrush",      "features/fyp.png"),
        ("PlayLockdownHeroBrush", "lockdown_icon.png"),
        // 0812 remake: the hero and the Loom strip carry art too. playHeroMap does not mutate
        // these two yet (they fall back to the embedded copies), but the named-and-mutable
        // contract holds so adopting them later is a map entry, not a XAML change.
        ("PlayDtrhHeroBrush",     "features/dtrh.png"),
        ("PlayLoomHeroBrush",     "features/loom.png"),
    };

    [Fact]
    public void EveryHeroBrushIsNamedAndStillMutable()
    {
        // playHeroMap assigns brush.ImageSource on every mod switch and skips any brush that is
        // frozen. A card that binds a frozen brush silently stops repainting — the exact failure
        // the rail's Clone() hazard produced, and the reason the map guards on IsFrozen instead of
        // throwing.
        OnStaThread(() =>
        {
            var page = new PlayTabView();
            foreach (var (name, _) in HeroBrushes)
            {
                var brush = page.FindName(name) as ImageBrush;
                Assert.True(brush != null, $"{name} is missing — its card would never repaint on a mod switch");
                Assert.False(brush!.IsFrozen, $"{name} is frozen; playHeroMap skips frozen brushes and the card goes stale");
            }
        });
    }

    [Fact]
    public void EveryHeroArtPathStillExistsUnderTheEmbeddedResources()
    {
        // The paths are resolved through ModResourceResolver at runtime, which falls back to the
        // embedded copy when no mod overrides them. A renamed or missing fallback is a blank card
        // on the default install and a broken contract for every .ccpmod on disk.
        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources");
        foreach (var (brush, path) in HeroBrushes)
        {
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"{brush} points at {path}, which is not under Resources/");
        }
    }

    // =====================================================================================
    //  the mantra rescue — retired with the card
    // =====================================================================================

    [Fact]
    public void TheMantraEntryPointSurvivesTheCardsRemoval()
    {
        // The 0812 remake removed the Mantra card (and its rep picker) from the wall, which made
        // this page's card the app's ONLY entry point no more — MantraWindow is orphaned until an
        // owner call re-homes it. StartMantraSession is the one method that knows the window
        // needs StartSession(n) before load, so it must survive the interregnum: deleting it
        // "because nothing calls it" would turn the re-home from a one-line change into an
        // archaeology dig.
        var m = typeof(global::ConditioningControlPanel.MainWindow)
            .GetMethod("StartMantraSession", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.True(m != null,
            "MainWindow.StartMantraSession is gone — MantraWindow now has no viable entry point left to re-home");
    }

    // =====================================================================================
    //  'lab' is API — the alias pair, pointing in opposite directions
    // =====================================================================================

    [Fact]
    public void TheBarkAliasMapsTheNewLiveKeyOntoTheOldBarkKey()
    {
        // Direction is the whole test. BarkTabAliases is consulted at the single
        // NotifyTabNavigated call site with the key ShowTab was CALLED with, so the entry has to
        // be ["play"] = "lab": navigating to the new key announces itself with the old one, and
        // every mod's nav_lab rule keeps firing. Reversed, it is a no-op that looks correct.
        var map = PrivateStatic<IDictionary>(typeof(global::ConditioningControlPanel.MainWindow), "BarkTabAliases");
        Assert.True(map.Contains("play"), "BarkTabAliases has no \"play\" entry — the Play door is voiceless");
        Assert.Equal("lab", (string)map["play"]!);
        Assert.False(map.Contains("lab"),
            "BarkTabAliases[\"lab\"] exists — that is the reversed direction; ShowTab(\"lab\") must announce \"lab\" directly");
    }

    [Fact]
    public void TheViewAliasMapsTheOldKeyOntoTheLiveView()
    {
        // The inverse map, for everything keyed to a VIEW rather than to a rule file: the
        // ambient-FX registry (SwitchTabFx), the nav indicator and the door accordion. If this
        // returned "lab" for "lab", ShowTab("lab") would park the Play canvas while showing the
        // Play wall — a loop stopped on the surface it decorates, with no error anywhere.
        var m = typeof(global::ConditioningControlPanel.MainWindow)
            .GetMethod("CanonicalTabKey", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(m != null, "CanonicalTabKey is gone — the view-side half of the alias pair");

        Assert.Equal("play", (string)m!.Invoke(null, new object?[] { "lab" })!);
        Assert.Equal("play", (string)m.Invoke(null, new object?[] { "LAB" })!);
        Assert.Equal("play", (string)m.Invoke(null, new object?[] { "play" })!);
        // Everything else passes through untouched, or every other door loses its own key.
        foreach (var other in new[] { "settings", "studio", "companion", "exclusives", "deeper" })
            Assert.Equal(other, (string)m.Invoke(null, new object?[] { other })!);
    }

    [Fact]
    public void TheNavOrderRenamedTheRowInPlaceSoNoIndexMoved()
    {
        // ChromeFxNav.NavOrder positions drive slide direction, and ChromeFxNavTests asserts four
        // of them by number. Phase 6 renamed index 8 rather than inserting; the legacy alias has
        // to score the same index or every caller still passing "lab" gets the fallback "rise"
        // entrance instead of its neighbours' horizontal slide.
        Assert.Equal("play", ChromeFxNav.NavOrder[8]);
        Assert.DoesNotContain("lab", ChromeFxNav.NavOrder);
        Assert.Equal(8, ChromeFxNav.IndexOf("lab"));
        Assert.Equal(8, ChromeFxNav.IndexOf("play"));
    }

    [Fact]
    public void TheDoorMapRoutesBothKeysToThePlayDoor()
    {
        // NavDoorForTab is what expands the right door and lights the right row for code-driven
        // navigation (tutorial spotlights, notifications, Ctrl+K). "lab" is deliberately NOT a
        // listed entry — it resolves through the alias — so both have to land on "play".
        var m = typeof(global::ConditioningControlPanel.MainWindow)
            .GetMethod("NavDoorForTab", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(m != null, "NavDoorForTab is gone");

        Assert.Equal("play", (string?)m!.Invoke(null, new object?[] { "play" }));
        Assert.Equal("play", (string?)m.Invoke(null, new object?[] { "lab" }));

        // And the other Play entries did not lose their door on the way past.
        foreach (var key in new[] { "deeper", "exclusives", "gradedintake", "lockdown",
                                    "blinktrainer", "remotecontrol", "availablesubjects" })
            Assert.Equal("play", (string?)m.Invoke(null, new object?[] { key }));
    }

    [Fact]
    public void TheTutorialsDoorMapPointsTheUnchangedElementNameAtTheLiveKey()
    {
        // The x:Name "BtnLab" is API (the nav has always been keyed by x:Names and the button kept
        // its name); only the VALUE moved to the live tab key.
        var map = PrivateStatic<IDictionary>(typeof(TutorialService), "NavEntryDoorKeys");
        Assert.True(map.Contains("BtnLab"), "TutorialService lost its BtnLab row — that nav entry stops opening its door");
        Assert.Equal("play", (string)map["BtnLab"]!);
    }

    [Fact]
    public void TheCtrlKPaletteFindsThePlayDoorAndItsCardsAndNavigatesToALiveKey()
    {
        // The palette navigates by tab key. A row still pointing at "lab" would work (the alias
        // catches it) but names a view that no longer exists; more importantly the wall's cards
        // have to be findable by name, because the room they used to live in is gone.
        var rows = SettingsPaletteIndex.All.Where(e => e.TabKey == "play").ToList();
        Assert.NotEmpty(rows);
        Assert.DoesNotContain(SettingsPaletteIndex.All, e => e.TabKey == "lab");

        foreach (var term in new[] { "lab", "gaze", "rabbit hole", "bureau", "mantra" })
            Assert.True(SettingsPaletteIndex.Search(term).Any(e => e.TabKey == "play"),
                $"searching the palette for \"{term}\" no longer finds the Play door");
    }

    // =====================================================================================
    //  bark parity (pure — reads the shipped rule files, no WPF)
    // =====================================================================================

    [Fact]
    public void EveryBuiltInModStillHasItsLabNavRuleAndNeedsNoPlayRule()
    {
        // The alias exists precisely so no rule file has to change: third-party .ccpmod files on
        // disk carry their own copies we can never edit. A "play" rule appearing here would mean
        // someone "fixed" the alias by duplicating the content, which double-announces for every
        // mod that has both.
        foreach (var file in BuiltInModRuleFiles())
        {
            var mod = Path.GetFileName(Path.GetDirectoryName(file));
            Assert.True(TabEqRuleIds(file, "lab").Any(), $"{mod} lost its tab_eq:\"lab\" rules — the Play door is silent there");
            Assert.False(TabEqRuleIds(file, "play").Any(),
                $"{mod} has tab_eq:\"play\" rules as well as \"lab\" — the alias already routes play->lab, so this double-announces");
        }
    }

    // =====================================================================================
    //  the Lab view is really gone
    // =====================================================================================

    [Fact]
    public void TheLabViewTypeNoLongerExists()
    {
        // A leftover compiled LabTabView would mean the view is still mounted somewhere, which is
        // how a second RabbitHoleFx canvas ends up running behind a hidden tab.
        var lab = typeof(PlayTabView).Assembly.GetType("ConditioningControlPanel.Views.Tabs.LabTabView", throwOnError: false);
        Assert.True(lab == null, "LabTabView is still compiled into the assembly");
    }

    [Fact]
    public void TheSmokescreenIsGoneFromTheSource()
    {
        // The overlay WAS the Tier 2 gate for DTRH / Gaze / Focus Gaze — geography instead of a
        // check, which is why the CLI switches walked past it. It must not come back, and a
        // page-wide curtain on a door that mixes Free/T1/T2 would hide cards people own.
        var offenders = SourceFiles()
            .Where(f => File.ReadAllText(f).Contains("LabSmokescreen.Visibility", StringComparison.Ordinal))
            .ToList();
        Assert.True(offenders.Count == 0, "something still drives LabSmokescreen: " + string.Join(", ", offenders));
    }

    // ---- helpers -------------------------------------------------------------------------

    private static T PrivateStatic<T>(Type owner, string name)
    {
        var f = owner.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.True(f != null, $"{owner.Name}.{name} is gone");
        var v = f!.GetValue(null);
        Assert.True(v is T, $"{owner.Name}.{name} is no longer a {typeof(T).Name}");
        return (T)v!;
    }

    private static TChild? FindDescendant<TChild>(DependencyObject root) where TChild : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TChild hit) return hit;
            var deeper = FindDescendant<TChild>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    /// <summary>
    /// The product's own C# sources. Build output is skipped for the obvious reason, and
    /// <c>.claude/</c> because a developer's git worktrees live under it — those are other
    /// branches' copies of this repo and asserting against them would fail on whatever anyone
    /// happens to have checked out.
    /// </summary>
    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel"), "*.cs", SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                             && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                             && !f.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}"));

    private static IEnumerable<string> BuiltInModRuleFiles()
    {
        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "sounds",
                                "companion_audio", "mods");
        var files = Directory.GetDirectories(root, "builtin-*")
                             .Select(d => Path.Combine(d, "bark_rules.json"))
                             .Where(File.Exists)
                             .OrderBy(f => f)
                             .ToList();
        Assert.True(files.Count >= 3, $"expected the three built-in mods' rule files under {root}, found {files.Count}");
        return files;
    }

    private static IEnumerable<string> TabEqRuleIds(string file, string tabKey)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        var array = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("rules");
        return array.EnumerateArray()
                    .Where(r => r.TryGetProperty("conditions", out var c)
                                && c.TryGetProperty("tab_eq", out var t)
                                && string.Equals(t.GetString(), tabKey, StringComparison.Ordinal))
                    .Select(r => r.GetProperty("id").GetString()!)
                    .Where(id => id != null)
                    .ToList();
    }
}
