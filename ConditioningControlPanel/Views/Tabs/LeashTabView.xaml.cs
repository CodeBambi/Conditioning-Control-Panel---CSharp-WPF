using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Social &gt; Leash: the leash cards as a page, for both roles. Hosts a second
    /// <see cref="LeashDrawerSection"/> (the drawer keeps its own); every action, the cut included,
    /// is the section's own, so the page adds no path the drawer does not have. Empty (no offer,
    /// no leash, signed out): one line and a button to Friends, where offers start.
    /// </summary>
    public partial class LeashTabView : UserControl
    {
        internal LeashDrawerSection Section { get; }

        /// <summary>The "?" explainer button (LeashExplainHost).</summary>
        internal Button Help { get; }

        internal Button EmptyButton { get; }

        public LeashTabView() : this(null) { }

        /// <summary><paramref name="service"/> is for the suite; the app passes nothing.</summary>
        internal LeashTabView(Func<ILeashService?>? service)
        {
            InitializeComponent();
            Section = new LeashDrawerSection(service);
            Section.Changed += SyncEmpty;
            SectionHost.Children.Add(Section);

            Help = LeashLook.Help(LeashExplainRole.Leashed);
            HelpHost.Child = Help;

            EmptyButton = FriendsLook.Pill(Loc.Get("social_leash_empty_open"), FriendsLook.MintBrush, FriendsLook.MintInkBrush,
                FriendsLook.MintBrush, 10, new Thickness(16, 6, 16, 6), FriendsLook.MintBrush);
            EmptyButton.FontSize = 13;
            EmptyButton.Tag = "leash-page-open-friends";
            EmptyButton.Click += (_, _) => (Window.GetWindow(this) as MainWindow)?.ShowTab("friends");
            EmptyButtonHost.Child = EmptyButton;
            SyncEmpty();
        }

        /// <summary>True while there is nothing leash-shaped to show.</summary>
        internal bool ShowingEmpty => EmptyPanel.Visibility == Visibility.Visible;

        private void SyncEmpty() =>
            EmptyPanel.Visibility = Section.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>ShowTab("leash"): repaint, and report the offers / today's task on screen.</summary>
        internal void OnShown()
        {
            try { Section.Render(); }
            catch (Exception ex) { App.Logger?.Debug("[Leash] page repaint failed: {E}", ex.Message); }
            SyncEmpty();
        }
    }
}
