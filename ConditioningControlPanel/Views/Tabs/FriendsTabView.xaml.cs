using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Social &gt; Friends: the friends drawer as a page. One drawer class, two hosts: the rail
    /// chip keeps its popup as the quick surface, this page holds a second instance in page mode.
    /// No logic lives here; the drawer talks to App.Friends exactly as the popup does.
    /// </summary>
    public partial class FriendsTabView : UserControl
    {
        /// <summary>The hosted drawer body (page mode).</summary>
        internal FriendsDrawer Drawer { get; }

        public FriendsTabView() : this(null) { }

        /// <summary><paramref name="service"/> is for the suite; the app passes nothing.</summary>
        internal FriendsTabView(IFriendsService? service)
        {
            InitializeComponent();
            Drawer = new FriendsDrawer(service, asPage: true)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Drawer.SettingsRequested += OpenSettings;
            DrawerHost.Children.Add(Drawer);
        }

        /// <summary>ShowTab("friends"): subscribe, ask for a fresh list, play the entrance.</summary>
        internal void OnShown() => Drawer.OnOpened();

        /// <summary>Any tab switch away: the open card and picker close, the table poll stops.</summary>
        internal void OnHidden() => Drawer.OnClosed();

        private void OpenSettings()
        {
            try { (Window.GetWindow(this) as MainWindow)?.ShowTab("appsettings"); }
            catch (Exception ex) { App.Logger?.Debug("[Friends] page settings failed: {E}", ex.Message); }
        }
    }
}
