// PORTED from ConditioningControlPanel/Views/Tabs/FriendsTabView.xaml(.cs) (WPF ca2997d30):
// Social > Friends, the friends drawer as a page. One drawer class, two hosts: the rail chip keeps
// its popup as the quick surface, this page holds a second instance in page mode. No logic lives
// here; the drawer talks to the friends service exactly as the popup does.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public sealed class FriendsTabView : UserControl
{
    /// <summary>The hosted drawer body (page mode).</summary>
    internal FriendsDrawer Drawer { get; }

    public FriendsTabView() : this(null) { }

    /// <summary><paramref name="service"/> is for the suite; the app passes nothing.</summary>
    internal FriendsTabView(IFriendsService? service)
    {
        Drawer = new FriendsDrawer(service, asPage: true)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Drawer.SettingsRequested += () => Shell?.ShowTab("appsettings");
        Drawer.SignInRequested += () => _ = Shell?.OpenUnifiedLoginDialog();
        Drawer.InvitesRequested += () => Shell?.OpenInvitesCard();
        Content = new Grid { Margin = new Thickness(32, 20, 32, 20), Children = { Drawer } };
        // The shell hides tabs with IsVisible (P01): any switch away folds the card and picker and
        // stops listening, as WPF's OnHidden does; so does leaving the tree.
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) OnHidden(); };
        DetachedFromVisualTree += (_, _) => OnHidden();
    }

    private MainShellWindow? Shell => TopLevel.GetTopLevel(this) as MainShellWindow;

    /// <summary>ShowTab("friends"): subscribe, ask for a fresh list.</summary>
    internal void OnShown() => Drawer.OnOpened();

    /// <summary>Any tab switch away: the open card and picker close, the drawer stops listening.</summary>
    internal void OnHidden() => Drawer.OnClosed();
}
