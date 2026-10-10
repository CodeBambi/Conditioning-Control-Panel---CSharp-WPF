using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Small feel items of the third juice pass: the welcome show lights the card it previews
/// (WPF FeatureCard.SetTutorialPreview), the bundled Fredoka is a font choice (WPF FontPickerHelper),
/// and the companion's breathing ring carries no Effect. The look of each is owed a desk run.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class J3FeelTests
{
    private static void OnUi(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try { body(); }
        finally { CoreSettings.ServiceProvider = oldProvider; }
    });

    [Fact]
    public void TheWelcomeShowLightsTheCardItPreviews_AndNeverTurnsTheFeatureOn() => OnUi(() =>
    {
        var card = new FeatureCard { DimWhenInactive = true, Width = 220, Height = 180 };
        var w = new Window { Width = 300, Height = 260, Content = card };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var lit = card.FindControl<Border>("ActiveBorder")!;
            var content = card.FindControl<Control>("ContentRoot")!;
            Assert.False(lit.IsVisible);
            Assert.False(card.PaintsActive);
            double dim = content.Opacity;
            Assert.True(dim < 1);

            card.SetTutorialPreview(true);                         // IN: painted as an on card
            Assert.True(lit.IsVisible);
            Assert.True(card.PaintsActive);
            Assert.Equal(1, content.Opacity);
            Assert.False(card.IsActive);                           // appearance only

            card.SetTutorialPreview(false);                        // OUT: back to its truth
            Assert.False(lit.IsVisible);
            Assert.Equal(dim, content.Opacity);

            card.IsLocked = true;                                  // a locked card is never lit
            card.SetTutorialPreview(true);
            Assert.False(lit.IsVisible);
            Assert.False(card.PaintsActive);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void TheBundledFredokaLeadsBothFontPickers_AndDrawsInThePackedFace() => OnUi(() =>
    {
        Assert.Equal("Fredoka (bundled)", FontPicker.BundledFredoka);          // the WPF stored value, unchanged
        Assert.Equal(AppFonts.DisplayName, FontPicker.Resolve(FontPicker.BundledFredoka, "Segoe UI").FamilyNames[0]);
        Assert.Equal("Segoe UI", FontPicker.Resolve(FontPicker.BundledFredoka, "Segoe UI").FamilyNames[1]);
        Assert.Equal("Segoe UI", FontPicker.Resolve(null, "Segoe UI").FamilyNames[0]);
        Assert.Equal("Impact", FontPicker.Resolve("Impact, Evil", "Arial").FamilyNames[0]);   // a comma is cut
        Assert.Equal(2, FontPicker.Resolve("Impact, Evil", "Arial").FamilyNames.Count);

        var s = CoreSettings.Current;
        var bouncing = new BouncingTextFeatureControl();
        var subliminal = new SubliminalFeatureControl();
        var w = new Window { Width = 900, Height = 900, Content = new StackPanel { Children = { bouncing, subliminal } } };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            foreach (var picker in new[] { bouncing.FindControl<ComboBox>("CmbFont")!, subliminal.FindControl<ComboBox>("CmbFont")! })
            {
                var first = Assert.IsType<ComboBoxItem>(picker.Items[0]);
                Assert.Equal(FontPicker.BundledFredoka, first.Tag);
                Assert.Equal(AppFonts.DisplayName, first.FontFamily.FamilyNames[0]);
                Assert.Single(picker.Items.OfType<ComboBoxItem>(), i => (string?)i.Tag == FontPicker.BundledFredoka);
            }

            // picking it stores the sentinel, and the overlay reads the packed face from it
            var cmb = bouncing.FindControl<ComboBox>("CmbFont")!;
            cmb.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(FontPicker.BundledFredoka, s.BouncingTextFont);
            Assert.Equal(AppFonts.DisplayName, BouncingTextOverlay.Family().FamilyNames[0]);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void TheCompanionsBreathingRingCarriesNoEffect_ItsGlowIsAStillSibling() => OnUi(() =>
    {
        var hero = new CompanionHeroCard();
        var w = new Window { Width = 900, Height = 500, Content = hero };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var ring = hero.FindControl<Ellipse>("PortraitRing")!;
            var glow = hero.FindControl<Border>("PortraitRingGlow")!;
            Assert.Null(ring.Effect);
            Assert.Null(glow.Effect);
            Assert.Null(glow.RenderTransform);                     // the glow does not ride the breath
            Assert.Same(ring.GetVisualParent(), glow.GetVisualParent());
            if (!glow.Classes.Contains("asleep"))
            {
                Assert.Equal(26, glow.BoxShadow[0].Blur);          // WPF CmpRingGlow: pink, blur 26, opacity 0.35
                Assert.Equal(Color.Parse("#59FF69B4"), glow.BoxShadow[0].Color);
            }
        }
        finally { w.Close(); }
    });
}
