using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Training Programs tab and its enrollment ceremony, as they actually lay out.
///
/// <para><b>What this suite is for.</b> Every failure it guards is silent at compile time and
/// invisible in a screenshot of the shipped 7-day program:</para>
/// <list type="number">
/// <item>The 28-day reward rail. Nodes were a fixed 54px inside a <c>UniformGrid Rows=1</c>, so past
/// ~26 days the cell is narrower than the item and WPF's automatic layout clip slices the nodes and
/// the today halo flat. Only two of the five shipped programs are long enough to show it, and the
/// clip has no error, no binding failure and no log line.</item>
/// <item>The today node's breathing halo. It is a <c>Forever</c> storyboard in a DataTemplate, and
/// the only thing that keeps it off a reduced-motion machine is which property its DataTrigger
/// watches - a one-word edit that nothing else would notice.</item>
/// <item>The enrollment dialog's height. It is chromeless, non-resizable and modal: if the footer
/// falls below the work area there is no title bar to drag, no border to pull, and Alt+F4 is the
/// only way out. That happens on a 1366x768 panel and at 150% DPI - i.e. not on the machine this is
/// developed on.</item>
/// <item>Accent-filled chips (the layer strip's NEW pill, the task card's done tick) rendering their
/// own text invisible. Program accents are author-supplied hex strings, so a pale one is a
/// supported input, not a hypothetical.</item>
/// </list>
///
/// <para>Nothing here is <c>Show()</c>n: layout is driven on the content root, which realizes every
/// template and resolves every resource without putting an HWND (or a modal) on a test agent's
/// desktop.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ProgramsTabRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>The app's fixed design width, minus the chrome the tab sits inside.</summary>
    private const double TabWidth = 1400;

    private static void Realize(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(e => e.Name == name);

    // =====================================================================================
    //  the reward rail
    // =====================================================================================

    private static List<ProgramDayPip> Pips(int days, int today)
    {
        var pips = new List<ProgramDayPip>(days);
        for (var i = 1; i <= days; i++)
        {
            pips.Add(new ProgramDayPip
            {
                DayIndex = i,
                Label = i.ToString(),
                NodeSize = i == today ? 42 : 32,
                IsCurrent = i == today,
                GlowVisibility = i == today ? Visibility.Visible : Visibility.Collapsed,
                GlowBrush = Brushes.HotPink,
            });
        }
        return pips;
    }

    /// <summary>
    /// The 28-day case, which is what "Kept" and "Takeover" both are. A layout clip on ANY node is
    /// the failure: it is WPF telling us the item asked for more width than its cell has, and what
    /// the user sees is nodes and the today halo sheared off flat down both sides.
    /// </summary>
    [Fact]
    public void TheDayRailClipsNothingAtTwentyEightDays()
    {
        OnStaThread(() =>
        {
            var tab = new ProgramsTabView();
            tab.ProgramsRunPanel.Visibility = Visibility.Visible;
            tab.RailWidthColumn.MaxWidth = double.PositiveInfinity;   // what code does past 14 days
            tab.RailSpacerColumn.Width = new GridLength(0);
            tab.ProgramDayStrip.ItemsSource = Pips(28, 9);

            Realize(tab, TabWidth, 1600);

            var nodes = Descendants(tab.ProgramDayStrip)
                .OfType<Grid>()
                .Where(g => Math.Abs(g.Height - 82) < 0.01)
                .ToList();

            Assert.Equal(28, nodes.Count);

            foreach (var node in nodes)
            {
                Assert.True(LayoutInformation.GetLayoutClip(node) == null,
                    $"day rail node {nodes.IndexOf(node) + 1} of 28 is layout-clipped at "
                    + $"{node.ActualWidth:0.#}px - the nodes and the today halo render sheared");
            }

            // And the cells are genuinely shared out, rather than every item collapsing to nothing.
            var narrowest = nodes.Min(n => n.ActualWidth);
            Assert.True(narrowest > 30, $"day rail cells came out {narrowest:0.#}px wide");
        });
    }

    /// <summary>The 7-day case still centres its nodes rather than stretching them into strips.</summary>
    [Fact]
    public void AShortRailStillCentresItsNodes()
    {
        OnStaThread(() =>
        {
            var tab = new ProgramsTabView();
            tab.ProgramsRunPanel.Visibility = Visibility.Visible;
            tab.ProgramDayStrip.ItemsSource = Pips(7, 3);

            Realize(tab, TabWidth, 1600);

            var node = Descendants(tab.ProgramDayStrip)
                .OfType<Border>()
                .FirstOrDefault(b => Math.Abs(b.Width - 42) < 0.01);

            Assert.True(node != null, "today's node is gone from the 7-day rail");
            Assert.Equal(42, node!.ActualWidth, 1);
        });
    }

    /// <summary>
    /// The breathing halo must hang off <c>Breathe</c>, not off <c>IsCurrent</c>: that carrier ANDs
    /// "this is today" with MotionFx.AllowAmbientLoops, and it is the only thing that stops a
    /// Forever clock starting on a machine set to reduced motion. Asserted on the template rather
    /// than on a running clock, because a storyboard needs real frames to observe.
    /// </summary>
    [Fact]
    public void TheBreathingHaloIsGatedOnTheMotionFlagNotOnTodayItself()
    {
        OnStaThread(() =>
        {
            var tab = new ProgramsTabView();
            var template = tab.ProgramDayStrip.ItemTemplate;
            Assert.True(template != null, "the day rail lost its item template");

            var trigger = template!.Triggers.OfType<DataTrigger>().FirstOrDefault();
            Assert.True(trigger != null, "the breathing-halo DataTrigger is gone");

            var binding = Assert.IsType<Binding>(trigger!.Binding);
            Assert.Equal("Breathe", binding.Path.Path);

            // BAML hands the trigger's Value back as the string it was authored as, not as a bool.
            Assert.Equal("True", trigger.Value?.ToString());

            // And the clock it starts is frame-capped like every other idle loop in the app.
            var begin = trigger.EnterActions.OfType<BeginStoryboard>().FirstOrDefault();
            Assert.True(begin?.Storyboard != null, "the halo storyboard is gone");
            Assert.Equal(24, System.Windows.Media.Animation.Timeline.GetDesiredFrameRate(begin!.Storyboard));
        });
    }

    // =====================================================================================
    //  the run header's identity slot
    // =====================================================================================

    /// <summary>
    /// Four of the five shipped programs have no sigil art and ProgramArt.Sigil has no fallback, so
    /// "no sigil" is the common case. Collapsing only the sigil's two children left the 84px WRAPPER
    /// measuring - a 102px identity column reserved for a 4px accent bar.
    /// </summary>
    [Fact]
    public void TheIdentityColumnCollapsesWithTheSigil()
    {
        OnStaThread(() =>
        {
            var tab = new ProgramsTabView();
            tab.ProgramsRunPanel.Visibility = Visibility.Visible;

            Assert.True(tab.RunSigilBox != null,
                "RunSigilBox is gone - code can no longer collapse the sigil's reserved width");

            tab.RunSigilBox.Visibility = Visibility.Collapsed;
            tab.RunSigilHost.Visibility = Visibility.Collapsed;
            tab.RunSigilGlow.Visibility = Visibility.Collapsed;
            tab.RunAccentBar.Visibility = Visibility.Visible;

            Realize(tab, TabWidth, 1600);

            var slot = (FrameworkElement)VisualTreeHelper.GetParent(tab.RunSigilBox);
            Assert.True(slot.ActualWidth < 20,
                $"the identity slot still reserves {slot.ActualWidth:0.#}px with no sigil to put in it");
        });
    }

    // =====================================================================================
    //  accent-filled chips
    // =====================================================================================

    /// <summary>
    /// <c>ProgramContrastForeground</c> is what keeps the NEW pill and the done tick legible on an
    /// author-supplied accent. Reached by reflection: it is a private static on the MainWindow
    /// partial and reaching it is cheaper - and far more honest - than standing a MainWindow up.
    /// </summary>
    private static Brush Contrast(Color accent)
    {
        var method = typeof(MainWindow).GetMethod("ProgramContrastForeground",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(method != null,
            "MainWindow.ProgramContrastForeground is gone - the accent chips are back to fixed white");

        return (Brush)method!.Invoke(null, new object[] { new SolidColorBrush(accent) })!;
    }

    private static double Luminance(Brush brush)
    {
        var c = Assert.IsType<SolidColorBrush>(brush).Color;
        return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
    }

    [Fact]
    public void APaleAccentGetsDarkInkAndTheShippedAccentsAreLeftAlone()
    {
        OnStaThread(() =>
        {
            // The two cases that shipped unreadable: a cream and a gold accent under near-white text.
            Assert.True(Luminance(Contrast(Color.FromRgb(0xF8, 0xE9, 0xC4))) < 0.4,
                "a cream accent still gets light ink - the NEW pill reads white on white");
            Assert.True(Luminance(Contrast(Color.FromRgb(0xFF, 0xD7, 0x00))) < 0.4,
                "a gold accent still gets light ink");

            // And the ones already on screen must not be restyled by this. #FF69B4 is the app's own
            // pink and is exactly why the luminance is linearised: on raw bytes it reads as "light"
            // (0.56) and every chip in the feature would have flipped to dark ink.
            Assert.True(Luminance(Contrast(Color.FromRgb(0xFF, 0x69, 0xB4))) > 0.6,
                "the shipped pink accent lost its light ink");
            Assert.True(Luminance(Contrast(Color.FromRgb(0x8A, 0x6F, 0xD8))) > 0.6,
                "a mid violet accent lost its light ink");
            Assert.True(Luminance(Contrast(Color.FromRgb(0x1A, 0x1A, 0x2E))) > 0.6,
                "a dark accent lost its light ink");
        });
    }

    // =====================================================================================
    //  literal-emoji sizing
    // =====================================================================================

    /// <summary>
    /// EmojiTextBlock sizes its inline Twemoji image from FontSize at the moment Text is SET, and
    /// XAML applies attributes in declaration order - so a literal-emoji site that writes Text first
    /// silently renders the glyph at the default size. Three sites in this view do it, and the only
    /// symptom is a slightly small emoji.
    /// </summary>
    [Fact]
    public void LiteralEmojiSitesSizeFromTheirOwnFontSize()
    {
        OnStaThread(() =>
        {
            var tab = new ProgramsTabView();
            tab.ProgramsRunPanel.Visibility = Visibility.Visible;
            tab.TodayCompleteBanner.Visibility = Visibility.Visible;
            tab.RunChapterRewardChip.Visibility = Visibility.Visible;
            tab.TodayRewardChip.Visibility = Visibility.Visible;

            Realize(tab, TabWidth, 1600);

            var literals = Descendants(tab)
                .OfType<EmojiTextBlock>()
                .Where(e => !string.IsNullOrEmpty(e.Text) && EmojiImage.Get(e.Text) != null)
                .ToList();

            Assert.True(literals.Count >= 3,
                $"expected the three literal-emoji chips (2x gift, 1x sparkle), found {literals.Count}");

            foreach (var block in literals)
            {
                var image = block.Inlines.OfType<InlineUIContainer>()
                    .Select(c => c.Child).OfType<Image>().FirstOrDefault();
                Assert.True(image != null, $"'{block.Text}' did not resolve to an inline image");

                Assert.Equal(block.FontSize * 1.15, image!.Width, 2);
            }
        });
    }

    // =====================================================================================
    //  the enrollment ceremony
    // =====================================================================================

    private static ProgramDefinition LongestProgram() =>
        BuiltInPrograms.All().OrderByDescending(p => p.LengthDays).First();

    /// <summary>
    /// Builds the ceremony with the two App.xaml dictionaries the shared harness does not carry.
    ///
    /// <para>WpfRenderHarness merges the five dictionaries the tab suites need; this dialog also
    /// reaches for <c>FocusGlowTextBox</c>, which lives in <c>Inputs.xaml</c> - and that one is
    /// merged LAST in App.xaml on purpose, because it carries the implicit (keyless) chrome for the
    /// stock controls and is meant to beat the older entries above it. Adding it to the shared theme
    /// for the whole run would hand every other render suite a different implicit ComboBox/TextBox,
    /// so it goes in for the length of this body and comes straight back out. The collection is
    /// non-parallel, so nothing else can be mid-render while it is installed.</para>
    /// </summary>
    private static void WithEnrollDialog(Action<ProgramEnrollDialog> body)
    {
        OnStaThread(() =>
        {
            var app = Application.Current!;
            var extra = new[] { "Resources/Theme/SeasonRecapCard.xaml", "Resources/Theme/Inputs.xaml" }
                .Select(rel => new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/ConditioningControlPanel;component/" + rel,
                                     UriKind.Absolute)
                })
                .ToList();

            foreach (var dictionary in extra) app.Resources.MergedDictionaries.Add(dictionary);
            try
            {
                body(new ProgramEnrollDialog(LongestProgram()));
            }
            finally
            {
                foreach (var dictionary in extra) app.Resources.MergedDictionaries.Remove(dictionary);
            }
        });
    }

    /// <summary>
    /// The dialog must never be taller than the screen it opens on. WorkArea is already in
    /// device-independent units, so this is the same assertion at 100% and at 150% DPI.
    /// </summary>
    [Fact]
    public void TheEnrollDialogNeverOutgrowsTheWorkArea()
    {
        WithEnrollDialog(dialog =>
        {
            Assert.Equal(SizeToContent.Height, dialog.SizeToContent);
            Assert.True(dialog.MaxHeight <= SystemParameters.WorkArea.Height,
                $"the enroll dialog may grow to {dialog.MaxHeight:0}px on a "
                + $"{SystemParameters.WorkArea.Height:0}px work area");
            Assert.True(dialog.MaxHeight >= dialog.MinHeight, "the clamp fell below the dialog's floor");
        });
    }

    /// <summary>
    /// The one thing the clamp must never cost: the footer. The middle section is a ScrollViewer in
    /// a star row precisely so that Cancel and Enroll keep their place when the height binds.
    /// </summary>
    [Fact]
    public void TheEnrollDialogKeepsItsFooterOnScreenWhenClamped()
    {
        WithEnrollDialog(dialog =>
        {
            // The worst realistic case: a 1366x768 panel's work area.
            const double clamped = 728;

            // Laid out in place rather than re-hosted: the content is already the Window's own
            // logical child, and this is exactly the box the clamped window would give it.
            var root = (FrameworkElement)dialog.Content;
            root.Measure(new Size(dialog.Width, clamped));
            root.Arrange(new Rect(0, 0, dialog.Width, clamped));
            root.UpdateLayout();

            var confirm = FindByName<Button>(root, "BtnConfirmEnroll");
            Assert.True(confirm != null, "the Enroll button is gone from the ceremony's footer");

            var box = confirm!.TransformToAncestor(root)
                .TransformBounds(new Rect(confirm.RenderSize));

            Assert.True(box.Bottom <= clamped + 0.5,
                $"the Enroll button's bottom edge sits at {box.Bottom:0.#}px inside a {clamped:0}px "
                + "window - it is off the screen and the modal has no other way out");
            Assert.True(box.Height > 0, "the Enroll button measured to nothing");
        });
    }

    /// <summary>
    /// Escape and click-drag: a chromeless, non-resizable modal with no close button needs both, and
    /// its sibling (ProgramsIntroPopup) has carried both since it shipped.
    /// </summary>
    [Fact]
    public void TheEnrollDialogCanBeEscapedAndDragged()
    {
        var type = typeof(ProgramEnrollDialog);

        Assert.True(
            type.GetMethod("Window_PreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance) != null,
            "the enroll dialog has no Escape handler - Alt+F4 is the only way out of the modal");
        Assert.True(
            type.GetMethod("Window_MouseLeftButtonDown", BindingFlags.NonPublic | BindingFlags.Instance) != null,
            "the enroll dialog cannot be dragged - a chromeless window that lands badly is stuck there");
    }
}
