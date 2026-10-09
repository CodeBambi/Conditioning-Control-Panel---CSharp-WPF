// PORTED from WPF 7.1.5 Views/Tabs/LeashTabView.xaml(.cs): Social > Leash, the leash cards as a page
// for both roles. Hosts a second LeashDrawerSection (the drawer keeps its own); every action, the
// cut included, is the section's own, so the page adds no path the drawer does not have. The "?"
// explainer sits top right. Empty (no offer, no leash, signed out): one line and a button to
// Friends, where offers start. Deviation: the gate stand-in under the section (LeashTabView.Cards.cs)
// until the LeashGateCard overlay lands on this head.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>Social &gt; Leash: the leash cards as a page; cards, or the empty state.</summary>
    public sealed partial class LeashTabView : UserControl
    {
        /// <summary>Where the leash section goes (WPF SectionHost).</summary>
        internal StackPanel SectionHost { get; } = new();

        internal LeashDrawerSection Section { get; }

        /// <summary>The "?" explainer button (LeashExplainHost).</summary>
        internal Button Help { get; }

        internal StackPanel EmptyPanel { get; }

        internal Button EmptyButton { get; }

        /// <summary>The service the page draws (tests hand one in; the app reads LeashHead).</summary>
        internal Func<ILeashService?> Resolve { get; set; } = () => Platform.LeashHead.Service;

        private readonly TextBlock _emptyLine;
        private Control? _gate;

        public LeashTabView()
        {
            Section = new LeashDrawerSection(() => Resolve());
            Section.Changed += () => { SyncGate(); SyncEmpty(); };
            SectionHost.Children.Add(Section);

            Help = LeashLook.Help(LeashExplainRole.Leashed);
            var helpHost = new Border { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8), Child = Help };

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
            grid.Children.Add(helpHost);
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

        /// <summary>True while there is nothing leash-shaped to show.</summary>
        internal bool ShowingEmpty => EmptyPanel.IsVisible;

        private void SyncEmpty() => EmptyPanel.IsVisible = !Section.IsVisible;

        /// <summary>The gate stand-in follows the section's repaint (see LeashTabView.Cards.cs).</summary>
        private void SyncGate()
        {
            if (_gate != null) SectionHost.Children.Remove(_gate);
            _gate = null;
            try
            {
                if (Section.Service is { Available: true } s && s.Snapshot.Me is { } me && GateStandIn(s, me) is { } g)
                {
                    _gate = g;
                    SectionHost.Children.Add(g);
                }
            }
            catch (Exception ex) { Serilog.Log.Debug("[Leash] gate stand-in failed: {E}", ex.Message); }
        }

        /// <summary>Repaints the section (rebinding to the live service) and the empty state.</summary>
        internal void Rebuild()
        {
            try { Section.Render(); }
            catch (Exception ex) { Serilog.Log.Debug("[Leash] page repaint failed: {E}", ex.Message); }
            SyncGate();
            SyncEmpty();
        }

        /// <summary>ShowTab("leash"): re-read the copy, repaint, and report the offers / today's task on screen.</summary>
        internal void OnShown()
        {
            _emptyLine.Text = Loc.Get("social_leash_empty");
            if (EmptyButton.Content is TextBlock label) label.Text = Loc.Get("social_leash_empty_open");
            Rebuild();
        }
    }
}
