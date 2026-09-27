using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Flashes v2 box and the motion picker inside it, read as text rather than as markup.
///
/// <para><b>Why this suite exists.</b> The picker shipped painting its rows
/// <c>Brushes.Black</c>. That looks like the house rule ("a ComboBox needs an explicit black
/// Foreground") but the rule is about the STOCK template, whose popup is a light system surface.
/// This picker wears <c>DarkComboBoxStyle</c>, whose popup is <c>ElevatedSurface</c> (#222240) and
/// whose box is <c>SurfaceBg</c> - so the rows measured about 1.7:1 and a tester read them off the
/// Circe skin as "pretty low contrast" (tier2, 2026-09-19). Nothing in a compile or a render test
/// notices a legal colour; only a measurement does.</para>
///
/// <para><b>Why the mods are swept.</b> A mod rewrites <c>SurfaceBg</c> / <c>PanelBg</c> /
/// <c>DarkerBg</c> (MainWindow.xaml.cs) but NOT <c>ElevatedSurface</c>, which no mod overrides at
/// all - so the popup is the same navy on every skin and the closed box is whatever the mod says.
/// Both have to clear AA for the same one brush, so both are measured.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class V2BoxContrastTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>WCAG 2.1 AA for body text.</summary>
    private const double MinRatio = 4.5;

    /// <summary>The dropdown surface in DarkComboBoxStyle. Not mod-overridable - see the class doc.</summary>
    private const string PopupSurface = "#222240";

    // ---------------------------------------------------------------- WCAG maths

    private static double Channel(byte c)
    {
        var s = c / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color c)
        => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>
    /// Realized, not bare: the picker's <c>{DynamicResource DarkComboBoxStyle}</c> is what decides
    /// its Foreground, and a style only reaches an element that has been through a layout pass.
    /// </summary>
    private static FlashFeatureControl RealizedFlashPanel()
    {
        var control = new FlashFeatureControl();
        var host = new Grid { Width = 520, Height = 900 };
        host.Children.Add(control);
        host.Measure(new Size(520, 900));
        host.Arrange(new Rect(new Point(0, 0), new Size(520, 900)));
        host.UpdateLayout();
        return control;
    }

    /// <summary>
    /// The colour a brush actually paints. Anything that is not a plain solid (a gradient, an
    /// unset value) is a failure here: the picker rows are flat text and nothing else reads.
    /// </summary>
    private static Color Solid(Brush? brush, string what)
    {
        var solid = brush as SolidColorBrush;
        Assert.True(solid != null, $"{what} is not a SolidColorBrush ({brush?.GetType().Name ?? "null"})");
        return solid!.Color;
    }

    // ---------------------------------------------------------------- the picker

    /// <summary>
    /// Builds one picker row through the control's own private builder, so the test measures what
    /// the user sees rather than a copy of it. Grants are all false in a test process, so the row
    /// is built by hand with <paramref name="v2"/> rather than through BuildMotionPicker.
    /// </summary>
    private static ComboBoxItem BuildRow(FlashFeatureControl control, bool v2)
    {
        var add = typeof(FlashFeatureControl).GetMethod("AddMotionChoice",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(add != null, "FlashFeatureControl.AddMotionChoice is gone - this suite measures nothing");

        var combo = (ComboBox)control.FindName("CmbMotion")!;
        combo.Items.Clear();
        add!.Invoke(control, new object[] { FlashMotionStyle.Pendulum, "option_flash_motion_pendulum", v2 });
        return Assert.IsType<ComboBoxItem>(combo.Items[0]);
    }

    private static IEnumerable<TextBlock> TextOf(DependencyObject root)
    {
        if (root is TextBlock t) yield return t;
        var n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            foreach (var found in TextOf(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
                yield return found;
        if (root is ContentControl cc && cc.Content is DependencyObject content)
            foreach (var found in TextOf(content)) yield return found;
        if (root is Panel p)
            foreach (var child in p.Children.OfType<DependencyObject>())
                foreach (var found in TextOf(child)) yield return found;
        if (root is Border b && b.Child is DependencyObject bc)
            foreach (var found in TextOf(bc)) yield return found;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryRowOfTheMotionPickerReadsOnTheDropdownItIsDrawnOn(bool v2)
    {
        OnStaThread(() =>
        {
            var control = RealizedFlashPanel();
            var row = BuildRow(control, v2);

            var popup = Parse(PopupSurface);
            var texts = TextOf(row).Distinct().ToList();
            Assert.NotEmpty(texts);

            foreach (var text in texts)
            {
                var colour = Solid(text.Foreground, $"picker row text '{text.Text}'");
                var ratio = Contrast(colour, popup);
                Assert.True(ratio >= MinRatio,
                    $"'{text.Text}' paints {colour} on the {PopupSurface} dropdown: {ratio:0.00}:1, "
                    + "under the 4.5:1 floor");
            }
        });
    }

    [Fact]
    public void TheClosedPickerBoxTakesItsTextFromTheThemeAndNotFromBlack()
    {
        OnStaThread(() =>
        {
            var control = RealizedFlashPanel();
            var combo = (ComboBox)control.FindName("CmbMotion")!;

            // The closed box draws a VisualBrush of the selected row, so the row's own brush is
            // what shows there too - but a local Foreground="Black" on the ComboBox would still
            // paint anything the row leaves unset. It must come from DarkComboBoxStyle.
            var colour = Solid(combo.Foreground, "CmbMotion.Foreground");
            Assert.True(Contrast(colour, Parse(PopupSurface)) >= MinRatio,
                $"the picker box paints {colour}, which cannot be read on its own dropdown");
        });
    }

    /// <summary>
    /// The same one brush, against every surface a bundled mod can put behind it. The box sits on
    /// the mod's SurfaceBg; the popup is the fixed ElevatedSurface; the panel behind the whole v2
    /// box is the mod's PanelBg. All three, all six mods.
    /// </summary>
    [Fact]
    public void ThePickerTextClearsAaOnEveryBundledModSkin()
    {
        OnStaThread(() =>
        {
            var control = RealizedFlashPanel();
            var row = BuildRow(control, v2: false);
            var colour = Solid(TextOf(row).First().Foreground, "picker row text");

            var skins = new (string Name, ModTheme? Theme)[]
            {
                ("CCP Default", BuiltInMods.CCPDefault.Theme),
                ("Bambi Sleep", BuiltInMods.BambiSleep.Theme),
                ("Sissy Hypno", BuiltInMods.SissyHypno.Theme),
                ("Dronification", BuiltInMods.Dronification.Theme),
                ("Circe's Lock", BuiltInMods.Locked.Theme),
                ("Infection Control", BuiltInMods.InfectionControl.Theme),
            };

            foreach (var (name, theme) in skins)
            {
                Assert.True(theme != null, $"{name} lost its theme block");
                foreach (var (what, hex) in new[]
                {
                    ("surface", theme!.SurfaceColor), ("panel", theme.PanelColor),
                    ("background", theme.BackgroundColor), ("dropdown", PopupSurface),
                })
                {
                    if (string.IsNullOrWhiteSpace(hex)) continue;
                    var ratio = Contrast(colour, Parse(hex!));
                    Assert.True(ratio >= MinRatio,
                        $"{name}: picker text {colour} on {what} {hex} is {ratio:0.00}:1");
                }
            }
        });
    }

    /// <summary>
    /// The measurement that would have caught the shipped bug, kept as the floor's reason: black
    /// text is unreadable on this dropdown, whatever the house rule says about stock ComboBoxes.
    /// </summary>
    [Fact]
    public void BlackIsNotAReadableColourOnThisDropdown()
        => Assert.True(Contrast(Colors.Black, Parse(PopupSurface)) < 2.0,
            "ElevatedSurface has been lightened - re-check the whole 'never black on a dark popup' rule");
}
