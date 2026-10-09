// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Tabs/LeashTabView.xaml(.cs): Social > Leash.
// WPF hosts a LeashDrawerSection (offers, your own card with the cut, one card per account you
// hold) over an empty state. The cards live in LeashTabView.Cards.cs (Core LeashService via LeashHead);
// with no leash, no offer and no one held, the page shows exactly what 7.1.5 shows with no
// leash: one line and a button to Friends, where offers start.
// ponytail: LeashLook.Help (the "?" explainer).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>Social &gt; Leash: the leash cards as a page; cards, or the empty state.</summary>
    public sealed partial class LeashTabView : UserControl
    {
        /// <summary>Where the leash cards go once the service exists (WPF SectionHost).</summary>
        internal StackPanel SectionHost { get; } = new();

        internal StackPanel EmptyPanel { get; }

        internal Button EmptyButton { get; }

        private readonly TextBlock _emptyLine;

        public LeashTabView()
        {
            _emptyLine = new TextBlock
            {
                Text = Loc.Get("social_leash_empty"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
            };
            _emptyLine[!TextBlock.ForegroundProperty] = _emptyLine.GetResourceObservable("TextMutedBrush").ToBinding();

            EmptyButton = FriendsDrawer.Pill(Loc.Get("social_leash_empty_open"), FriendsDrawer.Mint, FriendsDrawer.MintInk,
                "leash-page-open-friends", FriendsDrawer.Mint);
            EmptyButton.FontSize = 13;
            EmptyButton.CornerRadius = new CornerRadius(10);
            EmptyButton.Padding = new Thickness(16, 6, 16, 6);
            EmptyButton.HorizontalAlignment = HorizontalAlignment.Center;
            EmptyButton.Cursor = FriendsDrawer.Hand();
            if (EmptyButton.Content is TextBlock label) label.FontSize = 13;
            EmptyButton.Click += (_, _) => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.ShowTab("friends");

            EmptyPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(18, 40, 18, 28),
                Children = { _emptyLine, EmptyButton },
            };

            var grid = new Grid
            {
                Margin = new Thickness(32, 20, 32, 20),
                MaxWidth = 660,
                RowDefinitions = new RowDefinitions("Auto,*"),
            };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new StackPanel { Children = { SectionHost, EmptyPanel } },
            };
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);
            Content = grid;
            Rebuild();
        }

        /// <summary>True while there is nothing leash-shaped to show (always, on this head).</summary>
        internal bool ShowingEmpty => EmptyPanel.IsVisible;

        private void SyncEmpty() => EmptyPanel.IsVisible = SectionHost.Children.Count == 0;

        /// <summary>ShowTab("leash"): re-read the copy (a language change since the last visit) and
        /// the empty state.</summary>
        internal void OnShown()
        {
            _emptyLine.Text = Loc.Get("social_leash_empty");
            if (EmptyButton.Content is TextBlock label) label.Text = Loc.Get("social_leash_empty_open");
            Rebuild();
        }
    }
}
