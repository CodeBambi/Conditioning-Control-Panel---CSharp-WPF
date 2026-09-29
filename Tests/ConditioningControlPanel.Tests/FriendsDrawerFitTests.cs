using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The drawer at its real width (<see cref="FriendsDrawer.DrawerWidth"/>) in English and in the
/// longest languages: your own code (the only place it is drawn, and what a friend types to add
/// you) and its Copy button are whole, "Add friend" and "Settings" fit, and every request row's
/// subline shows its age ("15m", "3h") whatever the name and the buttons leave.
/// </summary>
public partial class FriendsDrawerTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    [InlineData("fr")]
    public void At_its_real_width_the_drawer_shows_your_code_and_every_request_age(string lang)
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(lang);
            WpfRenderHarness.OnStaThread(() =>
            {
                var snap = Sample() with
                {
                    Incoming = new[]
                    {
                        new FriendRequest("dee", "Deedeedee Longname", null, "Sam", Now.AddMinutes(-15)),
                        new FriendRequest("eli", "Eli", null, null, Now.AddHours(-3)),
                    },
                };
                var d = NewDrawer(new FakeFriends(snap));
                Layout(d);

                foreach (var tag in new[] { "friends-my-code", "friends-copy", "friends-add-open", "friends-settings" })
                    AssertWhole(d, Find(d, tag), $"{lang} {tag}");
                AssertNotTrimmed((TextBlock)Find(d, "friends-my-code")!, $"{lang} code");

                foreach (var (id, age) in new[] { ("dee", Loc.GetF("friends_notice_minutes", 15)), ("eli", Loc.GetF("friends_notice_hours", 3)) })
                {
                    var row = d.RowFor("in:" + id)!;
                    foreach (var tag in new[] { "friends-accept", "friends-decline", "friends-request-more" })
                        AssertWhole(d, Find(row, tag), $"{lang} {id} {tag}");
                    var sub = (TextBlock)Find(row, "friends-request-sub")!;
                    Assert.StartsWith(age, sub.Text);
                    // The words after it trim, so the age reads whole only with room for the "...".
                    var probe = new TextBlock { Text = age + "…", FontSize = sub.FontSize, FontFamily = sub.FontFamily };
                    probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.True(sub.ActualWidth + 0.5 >= probe.DesiredSize.Width,
                        $"{lang} {id}: the age '{age}' needs {probe.DesiredSize.Width:F0} px, the line gets {sub.ActualWidth:F0}");
                }
            });
        }
        finally { loc.SetLanguage(previous); }
    }

    /// <summary>Drawn in full inside <paramref name="root"/>: visible all the way up, cut by no
    /// layout clip on the way, and inside the root's width.</summary>
    private static void AssertWhole(FrameworkElement root, FrameworkElement? el, string what)
    {
        Assert.True(el != null, what + ": not in the drawer");
        for (DependencyObject? p = el; p != null && !ReferenceEquals(p, root); p = VisualTreeHelper.GetParent(p))
        {
            if (p is not FrameworkElement f) continue;
            Assert.True(f.Visibility == Visibility.Visible, $"{what}: a {f.GetType().Name} on the way is {f.Visibility}");
            var clip = LayoutInformation.GetLayoutClip(f);
            Assert.True(clip == null || clip.Bounds.Width + 0.5 >= f.RenderSize.Width,
                $"{what}: a {f.GetType().Name} {f.RenderSize.Width:F0} px wide is cut to {clip?.Bounds.Width:F0}");
        }
        var box = el!.TransformToAncestor(root).TransformBounds(new Rect(el.RenderSize));
        Assert.True(box.Width > 1 && box.Left >= -0.5 && box.Right <= root.ActualWidth + 0.5,
            $"{what}: drawn at {box.Left:F0}..{box.Right:F0} of {root.ActualWidth:F0}");
    }

    private static void AssertNotTrimmed(TextBlock tb, string what)
    {
        var probe = new TextBlock { Text = tb.Text, FontSize = tb.FontSize, FontFamily = tb.FontFamily, FontWeight = tb.FontWeight };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(tb.ActualWidth + 0.5 >= probe.DesiredSize.Width,
            $"{what}: '{tb.Text}' needs {probe.DesiredSize.Width:F0} px, gets {tb.ActualWidth:F0}");
    }
}
