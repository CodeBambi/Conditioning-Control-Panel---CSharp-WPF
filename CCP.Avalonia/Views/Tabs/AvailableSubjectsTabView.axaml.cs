// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Tabs/AvailableSubjectsTabView.xaml.cs (the
// Lobby page) + the page half of MainWindow/MainWindow.Lobby.cs (PaintLobbyInto, PaintLobbyHostBar,
// LobbyEntrance). The shell half (the lease, the gates, Join and Host) is in
// MainShellWindow.RegisterSocialTabs.cs.
//
// Deviations:
//   - WPF took the poll lease in ShowTab("availablesubjects") and dropped it on the way out. This
//     head's ShowTab belongs to the nav lane, so the page watches its own EFFECTIVE visibility (the
//     same chain watch CompanionRoomView uses) and asks the shell for the lease itself.
//   - The entrance (stagger in, then a pop + ring for a new table) runs on Avalonia transitions:
//     opacity/translate set from code with per-card delays, gated on the motion level.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>The Lobby page (the old Available Subjects tab). Paints a <see cref="LobbySnapshot"/>;
    /// every decision is Core's (<see cref="LobbyMerge"/>, <see cref="LobbyGates"/>).</summary>
    public partial class AvailableSubjectsTabView : UserControl
    {
        /// <summary>Sparse on purpose - MainWindow.SubjectsFx: the air behind a roster you read
        /// must never compete with a name.</summary>
        private const int SubjectsFogPuffs = 2;
        private const double SubjectsFogIntensity = 0.40;

        /// <summary>Below this content width the three columns become a three-tab switcher.</summary>
        internal const double NarrowWidth = 900;

        internal static bool IsNarrowWidth(double width) => width > 0 && width < NarrowWidth;

        private readonly AmbientFxCanvas _ambientFx;
        private readonly List<IDisposable> _visibilityWatch = new();
        private bool _shownLatch;

        /// <summary>Row ids already drawn, so only a table that is new to this page pops.</summary>
        internal HashSet<string> SeenRows { get; } = new();

        public bool IsNarrow { get; private set; }
        public int SelectedColumn { get; private set; }

        /// <summary>Raised when the page becomes / stops being visible on screen (the lease).</summary>
        internal event Action<bool>? EffectiveVisibilityChanged;

        public AvailableSubjectsTabView()
        {
            AvaloniaXamlLoader.Load(this);
            _ambientFx = this.FindControl<AmbientFxCanvas>("SubjectsAmbientFx")!;

            Loaded += OnTabLoaded;
            Unloaded += OnTabUnloaded;
            SizeChanged += (_, e) => { if (e.WidthChanged) SetNarrow(IsNarrowWidth(e.NewSize.Width)); };
            SetNarrow(false);
            // Drawn once signed out with the host bar unlocked for nobody, so the page is never blank
            // before the shell paints the first real snapshot.
            PaintInto(this, LobbySnapshot.Empty, LobbyGates.From(false, false, false), error: false);
        }

        internal T Part<T>(string name) where T : Control => this.FindControl<T>(name)!;

        private void OnTabLoaded(object? sender, RoutedEventArgs e)
            => _ambientFx.StartLayers(new AmbientFxConfig
            {
                Layers = AmbientFxLayers.FogDrift,
                FogPuffs = SubjectsFogPuffs,
                Intensity = SubjectsFogIntensity,
            });

        private void OnTabUnloaded(object? sender, RoutedEventArgs e) => _ambientFx.Stop();

        // =====================================================================================
        //  effective visibility (the poll lease)
        // =====================================================================================

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            foreach (var v in this.GetSelfAndVisualAncestors())
                _visibilityWatch.Add(v.GetObservable(IsVisibleProperty).Subscribe(new VisibilityObserver(this)));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            foreach (var d in _visibilityWatch) d.Dispose();
            _visibilityWatch.Clear();
            SyncShown(false);
            base.OnDetachedFromVisualTree(e);
        }

        private void SyncShown(bool shown)
        {
            if (shown == _shownLatch) return;
            _shownLatch = shown;
            try { EffectiveVisibilityChanged?.Invoke(shown); }
            catch (Exception ex) { Log.Debug("Lobby visibility hook: {E}", ex.Message); }
        }

        /// <summary>True while the page holds (or should hold) the poll lease.</summary>
        internal bool IsShownOnScreen => _shownLatch;

        private sealed class VisibilityObserver : IObserver<bool>
        {
            private readonly AvailableSubjectsTabView _owner;
            public VisibilityObserver(AvailableSubjectsTabView owner) => _owner = owner;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(bool value) => _owner.SyncShown(_owner.IsEffectivelyVisible);
        }

        // =====================================================================================
        //  narrow layout
        // =====================================================================================

        /// <summary>Wide: three columns side by side. Narrow: the tab strip shows and only the
        /// selected column is drawn, full width.</summary>
        internal void SetNarrow(bool narrow)
        {
            IsNarrow = narrow;
            Part<StackPanel>("LobbyTabStrip").IsVisible = narrow;
            var cols = new Control[] { Part<Border>("LobbyOpenColumn"), Part<Border>("LobbyPlayingSection"), Part<Border>("LobbyFriendsSection") };
            for (int i = 0; i < cols.Length; i++)
            {
                bool shown = !narrow || i == SelectedColumn;
                cols[i].IsVisible = shown;
                Grid.SetColumn(cols[i], narrow ? 0 : i);
                Grid.SetColumnSpan(cols[i], narrow ? 3 : 1);
                cols[i].Margin = narrow ? new Thickness(0) : new Thickness(i == 0 ? 0 : i == 1 ? 4 : 8, 0, i == 0 ? 8 : i == 1 ? 4 : 0, 0);
            }
            var tabs = new[] { Part<Button>("TabOpen"), Part<Button>("TabPlaying"), Part<Button>("TabFriends") };
            for (int i = 0; i < tabs.Length; i++) tabs[i].Opacity = i == SelectedColumn ? 1.0 : 0.6;
        }

        internal void SelectColumn(int index)
        {
            SelectedColumn = Math.Clamp(index, 0, 2);
            SetNarrow(IsNarrow);
        }

        private void LobbyTab_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && int.TryParse(c.Tag as string, out var i)) SelectColumn(i);
        }

        // =====================================================================================
        //  paint (WPF MainWindow.Lobby PaintLobbyInto / PaintLobbyHostBar)
        // =====================================================================================

        /// <summary>Pure paint of the page from a snapshot. Static so the render harness draws
        /// exactly what the app draws.</summary>
        internal static void PaintInto(AvailableSubjectsTabView tab, LobbySnapshot snap, LobbyGates gates, bool error, bool animate = false)
        {
            PaintHostBar(tab, gates);
            var open = LobbyRowView.From(snap.Open, gates);
            var playing = LobbyRowView.From(snap.Playing, gates);
            var friends = LobbyRowView.From(snap.Friends, gates);
            tab.Part<ItemsControl>("AvailableSubjectsList").ItemsSource = open;
            tab.Part<ItemsControl>("LobbyPlayingList").ItemsSource = playing;
            tab.Part<ItemsControl>("LobbyFriendsList").ItemsSource = friends;

            static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
            tab.Part<TextBlock>("LobbyOpenCount").Text = N(snap.Open.Count);
            tab.Part<TextBlock>("LobbyPlayingCount").Text = N(snap.Playing.Count);
            tab.Part<TextBlock>("LobbyFriendsCount").Text = N(snap.Friends.Count);
            tab.Part<TextBlock>("LobbyPlayingEmpty").IsVisible = snap.Playing.Count == 0;
            tab.Part<TextBlock>("LobbyFriendsEmpty").IsVisible = snap.Friends.Count == 0;
            SetLabel(tab.Part<Button>("TabOpen"), Loc.Get("lobby_list_open") + "  " + snap.Open.Count);
            SetLabel(tab.Part<Button>("TabPlaying"), Loc.Get("lobby_list_playing") + "  " + snap.Playing.Count);
            SetLabel(tab.Part<Button>("TabFriends"), Loc.Get("lobby_list_friends") + "  " + snap.Friends.Count);

            // Entrance: the first fill staggers in; after that only a NEW table pops.
            var ids = open.Concat(playing).Concat(friends).Select(v => v.Id).ToList();
            var fresh = ids.Where(id => !tab.SeenRows.Contains(id)).ToHashSet();
            bool firstFill = tab.SeenRows.Count == 0;
            foreach (var id in ids) tab.SeenRows.Add(id);
            if (animate && ids.Count > 0)
                Dispatcher.UIThread.Post(() =>
                {
                    try { Entrance(tab, firstFill ? null : fresh); }
                    catch (Exception ex) { Log.Debug("Lobby entrance: {E}", ex.Message); }
                }, DispatcherPriority.Normal);

            tab.Part<TextBlock>("TxtLobbyEmpty").Text = Loc.Get(snap.SignedIn ? "lobby_empty_open" : "lobby_signed_out");
            tab.Part<Border>("AvailableSubjectsEmptyPanel").IsVisible = snap.Open.Count == 0;
            tab.Part<Border>("AvailableSubjectsErrorPanel").IsVisible = error;
            tab.Part<TextBlock>("TxtLobbyCount").Text = snap.OpenCount > 0
                ? Loc.GetF("lobby_open_count", snap.OpenCount)
                : Loc.Get("desc_available_subjects");
        }

        internal static void PaintHostBar(AvailableSubjectsTabView tab, LobbyGates gates)
        {
            Paint(tab.Part<Button>("BtnHostChess"), "lobby_host_chess", gates.CanHost(LobbyGame.Chess));
            Paint(tab.Part<Button>("BtnHostGoon"), "lobby_host_goon", gates.CanHost(LobbyGame.Goon));
            Paint(tab.Part<Button>("BtnBecomeASubject"), "lobby_host_remote", gates.CanHost(LobbyGame.Remote));
            Paint(tab.Part<Button>("BtnEmptyHostChess"), "lobby_host_chess", gates.CanHost(LobbyGame.Chess));
            Paint(tab.Part<Button>("BtnEmptyHostGoon"), "lobby_host_goon", gates.CanHost(LobbyGame.Goon));
            // The Remote pill's one line for free accounts: what unlocks it.
            tab.Part<TextBlock>("TxtBecomeASubjectSubtitle").IsVisible = gates.SignedIn && !gates.CanHost(LobbyGame.Remote);

            static void Paint(Button b, string key, bool open)
            {
                SetLabel(b, (open ? "" : "🔒 ") + Loc.Get(key));
                b.Opacity = open ? 1.0 : 0.7;
            }
        }

        /// <summary>A button's words as a TextBlock child (trap 1: "_" would be an access key).</summary>
        private static void SetLabel(Button b, string text)
        {
            if (b.Content is TextBlock tb) tb.Text = text;
            else b.Content = new TextBlock { Text = text };
        }

        /// <summary>The card containers the three lists drew, in order, with their row.</summary>
        internal IEnumerable<(Control Card, LobbyRowView Row)> Cards()
        {
            foreach (var name in new[] { "AvailableSubjectsList", "LobbyPlayingList", "LobbyFriendsList" })
            {
                var list = Part<ItemsControl>(name);
                for (int i = 0; i < list.ItemCount; i++)
                {
                    if (list.ContainerFromIndex(i) is not Control c || list.Items[i] is not LobbyRowView v) continue;
                    var card = c.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => Equals(g.Tag, "lobby-card"));
                    if (card != null) yield return (card, v);
                }
            }
        }

        /// <summary>First fill (<paramref name="fresh"/> null): every card staggers in. Later: a card
        /// whose table was not there before pops and sends out one ring. Off honours the motion
        /// level; Reduced keeps the stagger and drops the ring.</summary>
        internal static void Entrance(AvailableSubjectsTabView tab, ISet<string>? fresh)
        {
            if (!AmbientFxCanvas.Env.AllowTransitions) return;
            int n = 0;
            foreach (var (card, row) in tab.Cards().ToList())
            {
                if (fresh == null)
                {
                    var delay = TimeSpan.FromMilliseconds(40 * Math.Min(n++, 12));
                    // Hide without the style's fade (it would fade OUT first), then fade in.
                    card.Transitions = null;
                    card.Opacity = 0;
                    DispatcherTimer.RunOnce(() => { card.ClearValue(TransitionsProperty); card.Opacity = 1; },
                        delay + TimeSpan.FromMilliseconds(16));
                    continue;
                }
                if (!fresh.Contains(row.Id)) continue;
                card.Transitions = null;
                card.RenderTransform = TransformOperations.Parse("scale(0.9)");
                DispatcherTimer.RunOnce(() =>
                {
                    card.ClearValue(TransitionsProperty);
                    card.ClearValue(RenderTransformProperty);
                }, TimeSpan.FromMilliseconds(16));
                var ring = card.GetVisualChildren().OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "lobby-ring"));
                if (ring != null && AmbientFxCanvas.Env.AllowParticles)
                {
                    ring.Transitions = new Transitions
                    {
                        new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(520) },
                        new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(520) },
                    };
                    ring.Opacity = 0.9;
                    ring.RenderTransform = TransformOperations.Parse("scale(1)");
                    DispatcherTimer.RunOnce(() =>
                    {
                        ring.Opacity = 0;
                        ring.RenderTransform = TransformOperations.Parse("scale(1.08)");
                    }, TimeSpan.FromMilliseconds(16));
                }
            }
        }

        // =====================================================================================
        //  clicks: forwarded to the shell (MainShellWindow.RegisterSocialTabs.cs)
        // =====================================================================================

        private Windows.MainShellWindow? Shell => TopLevel.GetTopLevel(this) as Windows.MainShellWindow;

        private void BtnLobbyRow_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is LobbyRowView row) Shell?.OnLobbyRowClick(row, b);
        }

        private void BtnHostChess_Click(object? sender, RoutedEventArgs e) => Shell?.LobbyHost(LobbyGame.Chess);

        private void BtnHostGoon_Click(object? sender, RoutedEventArgs e) => Shell?.LobbyHost(LobbyGame.Goon);

        private void BtnBecomeASubject_Click(object? sender, RoutedEventArgs e) => Shell?.LobbyHost(LobbyGame.Remote);
    }
}
