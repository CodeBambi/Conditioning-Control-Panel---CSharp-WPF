using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.FriendsWindows;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The knock card's countdown and its ring are its only clock (DESK-RUN 34, "a countdown from 5:00
/// and a ring"). Built by its own constructor (never shown) and laid out: whatever the button labels
/// say, in any language, the countdown, the ring and both buttons stay whole on the card, full size
/// and tucked.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class KnockCardFitTests
{
    [Theory]
    [InlineData("Join", "Not now")]
    [InlineData("Ausprobieren", "Not now")]
    [InlineData("Попробовать", "Not now")]
    [InlineData("Experimentar", "Not now")]
    [InlineData("Beitreten", "Nicht jetzt")]
    public void The_countdown_ring_and_buttons_stay_on_the_card(string go, string notNow)
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var (card, face) = Build("invites you to the Back Room", go, notNow);
            var countdown = (FrameworkElement)Field(card, "_countdown");
            var ring = (FrameworkElement)VisualTreeHelper.GetParent((Path)Field(card, "_arc"));
            var buttons = Walk(face).OfType<Button>().Where(b => b.Content as string == go || b.Content as string == notNow).ToList();
            Assert.Equal(2, buttons.Count);

            foreach (var tucked in new[] { false, true })
            {
                if (tucked)
                {
                    typeof(KnockCard).GetField("_tucked", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(card, true);
                    typeof(KnockCard).GetMethod("ApplyTuck", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, null);
                }
                face.Measure(new Size(400, 600));
                face.Arrange(new Rect(face.DesiredSize));
                face.UpdateLayout();
                var state = tucked ? "tucked" : "full";
                AssertOnCard(face, countdown, $"{go} / {notNow} {state}: countdown");
                AssertOnCard(face, ring, $"{go} / {notNow} {state}: ring");
                foreach (var b in buttons) AssertOnCard(face, b, $"{go} / {notNow} {state}: {b.Content}");
            }
        });
    }

    [Fact]
    public void The_clock_leaves_the_line_under_the_name_its_whole_width()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var (card, face) = Build("schickt dir eine Geschmacksrichtung", "Ausprobieren", "Nicht jetzt");
            face.Measure(new Size(400, 600));
            face.Arrange(new Rect(face.DesiredSize));
            face.UpdateLayout();
            // German's long word would break in two if the clock took the line's room.
            var line = (TextBlock)Field(card, "_line");
            var word = new TextBlock { Text = "Geschmacksrichtung", FontSize = line.FontSize, FontFamily = line.FontFamily };
            word.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(line.ActualWidth + 0.5 >= word.DesiredSize.Width,
                $"the line gets {line.ActualWidth:F0} px, 'Geschmacksrichtung' needs {word.DesiredSize.Width:F0}");
        });
    }

    /// <summary>A card built by its own constructor, never shown; its face is lifted out to lay out alone.</summary>
    private static (Window Card, FrameworkElement Face) Build(string line, string go, string notNow)
    {
        var now = DateTimeOffset.UtcNow;
        var invite = new InboxItem("0123456789abcdef", SendKind.Invite, "u_ann", "Annabelle Longname-Whitfield", null, null,
            InviteDestination.BackRoom, null, null, now, now.AddMinutes(5));
        var ctor = typeof(KnockCard).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
        Action<InboxItem, KnockOutcome> done = (_, _) => { };
        var card = (Window)ctor.Invoke(new object?[] { new Window(), invite, line, go, notNow, "Answer later", done, null });
        var face = (FrameworkElement)card.Content;
        card.Content = null;
        return (card, face);
    }

    private static object Field(object o, string name)
        => o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d)
                foreach (var x in Walk(d)) yield return x;
    }

    /// <summary>Whole, and inside the card's border: no layout clip on the way up, nothing past the edge.</summary>
    private static void AssertOnCard(FrameworkElement card, FrameworkElement el, string what)
    {
        for (DependencyObject? p = el; p != null && !ReferenceEquals(p, card); p = VisualTreeHelper.GetParent(p))
        {
            if (p is not FrameworkElement f) continue;
            var clip = LayoutInformation.GetLayoutClip(f);
            Assert.True(clip == null || clip.Bounds.Width + 0.5 >= f.RenderSize.Width,
                $"{what}: a {f.GetType().Name} {f.RenderSize.Width:F0} px wide is cut to {clip?.Bounds.Width:F0}");
        }
        var box = el.TransformToAncestor(card).TransformBounds(new Rect(el.RenderSize));
        Assert.True(box.Width > 1 && box.Left >= 0 && box.Right <= card.ActualWidth - 1 + 0.5,
            $"{what}: drawn at {box.Left:F0}..{box.Right:F0} on a card {card.ActualWidth:F0} px wide");
    }
}
