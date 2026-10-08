// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Tabs/FriendsTabView.xaml(.cs): Social > Friends
// (nav rework 2026-10-06). The page hosts the SAME FriendsDrawer the rail chip pops up. Code only:
// the WPF XAML is one Grid (DrawerHost, margin 32,20) and nothing else.
//
// Deviation: WPF builds the drawer in PAGE MODE (asPage: no shadow, fills the column, Escape never
// claimed). This head's FriendsDrawer has no page mode yet (seam request in the lane hand-back), so
// the page un-pins its fixed width / max height from outside and ignores CloseRequested. The drawer
// still marks Escape handled while focused; the panic key is a global hook, so it is not starved.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.Friends;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// Social &gt; Friends: the friends drawer as a page. One drawer class, two hosts: the rail
    /// chip keeps its popup as the quick surface, this page holds a second instance.
    /// No logic lives here; the drawer talks to the friends service exactly as the popup does.
    /// </summary>
    public sealed class FriendsTabView : UserControl
    {
        /// <summary>The hosted drawer body.</summary>
        internal FriendsDrawer Drawer { get; }

        /// <summary>The cell the drawer sits in (WPF DrawerHost).</summary>
        internal Grid DrawerHost { get; } = new() { Margin = new Thickness(32, 20, 32, 20) };

        public FriendsTabView() : this(null) { }

        /// <summary><paramref name="service"/> is for the suite; the app passes nothing.</summary>
        internal FriendsTabView(IFriendsService? service)
        {
            Drawer = new FriendsDrawer(service)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                // Page mode by hand: the drawer pins 300 x 548 for the rail popup.
                Width = double.NaN,
                MaxHeight = double.PositiveInfinity,
            };
            Drawer.SettingsRequested += OpenSettings;
            Drawer.SignInRequested += () => _ = Shell?.OpenUnifiedLoginDialog();
            Drawer.InvitesRequested += () => Shell?.OpenInvitesCard();
            DrawerHost.Children.Add(Drawer);
            Content = DrawerHost;
        }

        private Windows.MainShellWindow? Shell => TopLevel.GetTopLevel(this) as Windows.MainShellWindow;

        /// <summary>ShowTab("friends"): subscribe, ask for a fresh list.</summary>
        internal void OnShown() => Drawer.OnOpened();

        /// <summary>Any tab switch away: the open card closes, the drawer stops counting as open.</summary>
        internal void OnHidden() => Drawer.OnClosed();

        private void OpenSettings()
        {
            try { Shell?.ShowTab("appsettings"); }
            catch (Exception ex) { Log.Debug("[Friends] page settings failed: {E}", ex.Message); }
        }
    }
}
