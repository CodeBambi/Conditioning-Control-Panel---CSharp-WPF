using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Mod-awareness lane C — the Studio door's art.
///
/// <para><b>The bug class this suite exists for.</b> A <c>.ccpmod</c> shadows
/// <c>resources/&lt;relpath&gt;</c> over the embedded <c>Resources/&lt;relpath&gt;</c>, but ONLY for
/// call sites that go through <c>ModResourceResolver</c>. Any surface that builds its own
/// <c>pack://application:,,,/Resources/…</c> URI silently opts out: it compiles, it renders, it
/// looks right under the default mod, and it draws the built-in picture under every other one.
/// The Studio rack did exactly that, and its eleven feature pages baked the same paths into XAML.
/// Nothing in the compiler or in the existing render suites notices, which is why these are
/// mostly source assertions — the failure is a lookup that was never made, and you cannot observe
/// a lookup that did not happen by looking at pixels.</para>
///
/// <para><b>The second half is freezing.</b> Resolving through the resolver on load is only half a
/// fix: a mod switch mid-session has to repaint. A frozen <see cref="Brush"/> cannot take a new
/// <c>ImageSource</c>, and assigning to one throws where it is caught and logged at Debug — so the
/// symptom is silence and stale art. Every brush this lane repaints is asserted unfrozen.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class StudioModArtRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    // =====================================================================================
    //  source assertions — the lookups that must exist
    // =====================================================================================

    [Fact]
    public void TheStudioRackResolvesItsFeatureArtThroughTheModResolver()
    {
        var src = File.ReadAllText(StudioTabViewCodeBehind());

        Assert.Contains("ModResourceResolver.ResolveImageDecoded(\"features/\"", src);

        // The old hand-built URI must be gone, not merely bypassed: leaving it behind is how the
        // next edit quietly reintroduces the opt-out. Matched as a STRING LITERAL (the quotes are
        // part of the needle) so the doc comment explaining the old bug does not trip this.
        Assert.DoesNotContain("\"pack://application:,,,/Resources/features/\"", src);

        // And the repaint half. RepaintModAwareChrome is the authoritative mod-switch signal
        // (MainWindow.xaml.cs wires it to ModService.ModChanged); if the art pass is not called
        // from it, a switch repaints the captions and leaves the pictures behind.
        var repaint = Section(src, "internal void RepaintModAwareChrome()");
        Assert.Contains("RefreshRackArt", repaint);
        Assert.Contains("ApplyDoorIcon", repaint);
        Assert.Contains("RetintChrome", repaint);
    }

    [Fact]
    public void TheStudioHeaderDoorIconIsResolvedRatherThanHardCoded()
    {
        var xaml = File.ReadAllText(Path.ChangeExtension(StudioTabViewCodeBehind(), null));
        Assert.Contains("x:Name=\"ImgStudioDoorIcon\"", xaml);

        // The pack:// URI STAYS in the XAML - it is the default the resolver overrides, and the
        // page must draw something before OnLoaded runs.
        Assert.Contains("Resources/nav/door_studio.png", xaml);

        var src = File.ReadAllText(StudioTabViewCodeBehind());
        Assert.Contains("ResolveImageDecoded(\"nav/door_studio.png\"", src);
    }

    [Fact]
    public void EveryFeaturePageWiresItsArtToTheResolverAndToModChanged()
    {
        // Enumerated from disk, not hand-listed: a twelfth feature page added later must be held
        // to the same contract without anyone remembering to edit this list.
        var pages = FeaturePagesDeclaringFeatureArt().ToList();
        Assert.True(pages.Count >= 11,
            $"expected at least the 11 known feature pages with features/*.png art, found {pages.Count}");

        var broken = new List<string>();
        foreach (var xamlPath in pages)
        {
            var name = Path.GetFileNameWithoutExtension(xamlPath);
            var xaml = File.ReadAllText(xamlPath);
            var csPath = xamlPath + ".cs";
            var cs = File.Exists(csPath) ? File.ReadAllText(csPath) : "";

            void Require(bool ok, string what) { if (!ok) broken.Add($"{name}: {what}"); }

            // Both plates have to be reachable from code-behind at all.
            Require(xaml.Contains("x:Name=\"HeroArtBrush\""), "hero ImageBrush is not named HeroArtBrush");
            Require(xaml.Contains("x:Name=\"SideArtBrush\""), "side-plate ImageBrush is not named SideArtBrush");

            Require(cs.Contains("private void ApplyFeatureArt()"), "no ApplyFeatureArt()");
            Require(cs.Contains("ModResourceResolver.ResolveImageDecoded("),
                    "ApplyFeatureArt does not go through ModResourceResolver");
            Require(cs.Contains("ApplyFeatureArt();"), "ApplyFeatureArt is never called");

            // Subscribe AND unsubscribe. The rack hosts these permanently, so an unbalanced hook
            // is a handler leak rather than a harmless extra.
            Require(cs.Contains("App.Mods.ModChanged += OnModChanged"), "does not subscribe to ModChanged");
            Require(cs.Contains("App.Mods.ModChanged -= OnModChanged"), "does not unsubscribe from ModChanged");

            // ModChanged can be raised off the UI thread (ModService raises it from whichever
            // thread activated the mod), so the repaint has to be marshalled.
            var handler = Section(cs, "private void OnModChanged(");
            Require(handler.Contains("Dispatcher.BeginInvoke"), "OnModChanged is not Dispatcher-marshalled");
            Require(handler.Contains("ApplyFeatureArt"), "OnModChanged does not repaint the art");
        }

        Assert.True(broken.Count == 0,
            "feature pages whose art is not mod-aware:" + Environment.NewLine
            + string.Join(Environment.NewLine, broken));
    }

    // =====================================================================================
    //  render assertions — the brushes a repaint has to be able to write into
    // =====================================================================================

    [Fact]
    public void TheRackArtBrushesAreUnfrozenSoAModSwitchCanRepaintThem()
    {
        OnStaThread(() =>
        {
            var page = new StudioTabView();
            var rack = (Panel)page.FindName("RackList")!;

            var art = ImageBrushesUnder(rack).ToList();

            // Twelve of the fifteen rack modules ship features/*.png, and each contributes two
            // brushes (resting chip + active tile). A collapse to near-zero means the rows stopped
            // being built from art at all, which the count catches before the freeze check can.
            Assert.True(art.Count >= 20,
                $"only {art.Count} art brushes on the rack — the rows are no longer painted from feature art");

            var frozen = art.Count(b => b.IsFrozen);
            Assert.True(frozen == 0,
                $"{frozen} of {art.Count} rack art brushes are frozen — a mod switch cannot repaint them");
        });
    }

    [Fact]
    public void RepaintingModAwareChromeKeepsEveryRackRowPainted()
    {
        // The repaint's degrade rule: a null resolve KEEPS the current art. A regression to
        // "assign whatever came back" turns one transient decode failure into a blank rack, and
        // this is the cheap way to prove the guard is there.
        OnStaThread(() =>
        {
            var page = new StudioTabView();
            var rack = (Panel)page.FindName("RackList")!;

            var before = ImageBrushesUnder(rack).Count(b => b.ImageSource != null);
            Assert.True(before > 0, "the rack drew no art at all before the repaint");

            page.RepaintModAwareChrome();
            page.RepaintModAwareChrome();   // idempotent: two switches in a row must not degrade

            var after = ImageBrushesUnder(rack).Count(b => b.ImageSource != null);
            Assert.Equal(before, after);
        });
    }

    [Fact]
    public void EveryFeaturePagesArtPlatesAreNamedUnfrozenAndPainted()
    {
        OnStaThread(() =>
        {
            var panels = new StudioTabView().HostedFeaturePanels
                                            .Where(p => p.FindName("HeroArtBrush") != null)
                                            .ToList();

            Assert.True(panels.Count >= 11,
                $"only {panels.Count} rack panels expose a named HeroArtBrush; expected at least 11");

            var broken = new List<string>();
            foreach (var p in panels)
            {
                foreach (var brushName in new[] { "HeroArtBrush", "SideArtBrush" })
                {
                    if (p.FindName(brushName) is not ImageBrush b)
                    {
                        broken.Add($"{p.GetType().Name}.{brushName} is missing or not an ImageBrush");
                        continue;
                    }
                    if (b.IsFrozen) broken.Add($"{p.GetType().Name}.{brushName} is frozen — it can never repaint");
                    // The XAML default has to survive construction: ApplyFeatureArt only ever
                    // OVERRIDES a non-null resolve, so an unpainted plate here would stay unpainted.
                    if (b.ImageSource == null) broken.Add($"{p.GetType().Name}.{brushName} has no XAML default art");
                }

                // And the resolver pass itself runs clean. Called directly rather than through
                // Loaded: an unrooted test tree never raises Loaded.
                var m = p.GetType().GetMethod("ApplyFeatureArt", BindingFlags.Instance | BindingFlags.NonPublic);
                if (m == null) { broken.Add($"{p.GetType().Name} has no ApplyFeatureArt"); continue; }
                m.Invoke(p, null);

                foreach (var brushName in new[] { "HeroArtBrush", "SideArtBrush" })
                    if (p.FindName(brushName) is ImageBrush b2 && b2.ImageSource == null)
                        broken.Add($"{p.GetType().Name}.{brushName} was BLANKED by ApplyFeatureArt");
            }

            Assert.True(broken.Count == 0, string.Join(Environment.NewLine, broken));
        });
    }

    [Fact]
    public void TheRackChromeTakesItsPinkFromTheModAccentNotFromALiteral()
    {
        // The dot halo is the cheapest observable of the whole accent pass: one Color, set from
        // FxTheme, re-set on every RepaintModAwareChrome. If it stops following the accent, so
        // have the group rules, the chip hue-wash and the NEW pill, which share the mechanism.
        OnStaThread(() =>
        {
            var res = Application.Current?.Resources;
            Assert.NotNull(res);

            var had = res!.Contains("FxGlowColor");
            var previous = had ? res["FxGlowColor"] : null;
            var probe = Color.FromRgb(0x40, 0xE0, 0xD0);   // nothing like #FF69B4

            try
            {
                var page = new StudioTabView();

                res["FxGlowColor"] = probe;
                page.RepaintModAwareChrome();

                var glow = (System.Windows.Media.Effects.DropShadowEffect)
                    typeof(StudioTabView)
                        .GetField("DotGlow", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(page)!;

                Assert.False(glow.IsFrozen, "the dot halo is frozen — it can never take a mod accent");
                Assert.Equal(probe, glow.Color);

                var wash = (LinearGradientBrush)
                    typeof(StudioTabView)
                        .GetField("ChipHueWashBrush", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(page)!;

                Assert.False(wash.IsFrozen, "the chip hue-wash is frozen — it can never take a mod accent");
                // Trailing stop is the accent itself at its original alpha; the leading stop is the
                // same colour hue-rotated, so it must differ in hue but share the alpha ramp.
                Assert.Equal(Color.FromArgb(0x1F, probe.R, probe.G, probe.B), wash.GradientStops[1].Color);
                Assert.Equal(0x40, wash.GradientStops[0].Color.A);
                Assert.NotEqual(wash.GradientStops[1].Color, wash.GradientStops[0].Color);
            }
            finally
            {
                if (had) res["FxGlowColor"] = previous;
                else res.Remove("FxGlowColor");
            }
        });
    }

    // ---- helpers -------------------------------------------------------------------------

    private static string RepoRoot()
    {
        // bin/Debug/net8.0 -> Tests/<proj> -> Tests -> repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string StudioTabViewCodeBehind() =>
        Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", "StudioTabView.xaml.cs");

    /// <summary>
    /// Every feature-page XAML that paints itself from a <c>Resources/features/</c> tile, wherever
    /// it lives. Two folders today (<c>Features\</c> and <c>Views\Controls\Studio\</c>) because
    /// Brain Drain was rebuilt in the Phase 4 rescue rather than restored to the old folder.
    /// </summary>
    private static IEnumerable<string> FeaturePagesDeclaringFeatureArt()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        foreach (var dir in new[] { Path.Combine(app, "Features"),
                                    Path.Combine(app, "Views", "Controls", "Studio") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*FeatureControl.xaml").OrderBy(x => x))
                if (File.ReadAllText(f).Contains("Resources/features/"))
                    yield return f;
        }
    }

    /// <summary>
    /// Every <see cref="ImageBrush"/> painting a background under <paramref name="root"/>.
    ///
    /// <para>Walks CONTENT, not the visual tree: the rack's rows are built in the constructor and
    /// this suite never realizes a template, so <c>VisualTreeHelper</c> would find nothing.</para>
    /// </summary>
    private static IEnumerable<ImageBrush> ImageBrushesUnder(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (!seen.Add(d)) continue;

            if (d is Border bd)
            {
                if (bd.Background is ImageBrush ib) yield return ib;
                if (bd.Child != null) stack.Push(bd.Child);
            }
            else if (d is Panel p)
            {
                if (p.Background is ImageBrush ib) yield return ib;
                foreach (UIElement c in p.Children) stack.Push(c);
            }
            else if (d is ContentControl cc)
            {
                if (cc.Background is ImageBrush ib) yield return ib;
                if (cc.Content is DependencyObject inner) stack.Push(inner);
            }
        }
    }

    /// <summary>
    /// The ~40 lines of source starting at <paramref name="anchor"/>. Crude on purpose: these are
    /// "is the call present in the right method" checks, not a parser.
    /// </summary>
    private static string Section(string src, string anchor)
    {
        var i = src.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(i >= 0, $"could not find '{anchor}' in the source");
        var lines = src.Substring(i).Split('\n');
        return string.Join("\n", lines.Take(Math.Min(40, lines.Length)));
    }
}
