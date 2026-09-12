using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One card in the dashboard billboard's 2x2 rack. A tiny view model rather than four sets of
    /// x:Names: the card's anatomy is declared once in the DataTemplate and a swap is four property
    /// writes, which is also what lets the paint fade the one slot that changed and leave the other
    /// three alone.
    /// </summary>
    internal sealed class BillboardSlotVm : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private ImageSource? _art;
        private bool _plate;
        private string _eyebrow = string.Empty;
        private string _title = string.Empty;
        private string _line = string.Empty;
        private string _tip = string.Empty;

        /// <summary>Roster id of the card in this slot; what the click resolves through.</summary>
        public string Id { get => _id; set => Set(ref _id, value); }

        public ImageSource? Art { get => _art; set => Set(ref _art, value); }

        public string Eyebrow { get => _eyebrow; set => Set(ref _eyebrow, value); }
        public string Title { get => _title; set => Set(ref _title, value); }
        public string Line { get => _line; set => Set(ref _line, value); }
        public string Tip { get => _tip; set => Set(ref _tip, value); }

        /// <summary>True for a square mark, which gets the plate instead of a cover fit.</summary>
        public bool Plate
        {
            get => _plate;
            set
            {
                if (_plate == value) return;
                _plate = value;
                Raise(nameof(Plate));
                Raise(nameof(CoverVisibility));
                Raise(nameof(PlateVisibility));
            }
        }

        public Visibility CoverVisibility => _plate ? Visibility.Collapsed : Visibility.Visible;
        public Visibility PlateVisibility => _plate ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            Raise(name);
        }

        private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// The billboard that takes the row the folded browser card gives back (owner ask,
    /// 2026-09-12). A 2x2 rack: four of the six roster cards on screen at once, and every twelve
    /// seconds ONE slot swaps to the card that has been off screen longest.
    ///
    /// <para>It was a single full-width poster until the first desk pass, where a 512px square mark
    /// cover-fitted into 874x504 read as a blurry giant. Small cards fixed the scale and
    /// <see cref="BillboardArt.Plate"/> fixed the square art: the same file blurred and dimmed
    /// behind, the mark itself at 72px in front.</para>
    ///
    /// <para>The roster and the walk are pure and live in <see cref="Services.DashboardBillboard"/>;
    /// this file is the paint, the clock and the click. Manners are the remix doors': nothing opens
    /// by itself, the pointer on the rack stops the clock, and the dashboard works exactly the same
    /// with the rack ignored. A tick touches the content of ONE slot and nothing else - never the
    /// fold, never a Visibility outside the rack, never the setting.</para>
    ///
    /// <para>The clock is an ambient loop by MotionFx's own definition (8-60s), so it only runs at
    /// <see cref="MotionFx.AllowAmbientLoops"/>. Below that the rack is four still cards, which is
    /// the house fallback: static art, never a slower loop.</para>
    /// </summary>
    public partial class MainWindow
    {
        private const int BillboardFadeMs = 220;

        private readonly ObservableCollection<BillboardSlotVm> _billboardSlots = new();
        private DispatcherTimer? _billboardTimer;
        private BillboardRack? _billboardRack;
        private bool _billboardPointerOver;
        private bool _billboardWired;

        /// <summary>The fold's one hook: the rack exists only while the browser is shut.</summary>
        partial void OnBrowserFoldChanged(bool collapsed)
        {
            try { ApplyBillboard(BrowserFoldRule.BillboardShown(collapsed)); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard billboard: fold hook failed"); }
        }

        private void ApplyBillboard(bool show)
        {
            var host = SettingsTab?.DashBillboard;
            if (host == null) return;

            host.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show)
            {
                StopBillboardClock();
                return;
            }

            EnsureBillboardWired();
            RestartBillboardClock();
        }

        /// <summary>
        /// One-time wiring: the slot collection, the four cards, and the two reasons the clock ever
        /// stops. The pointer pauses it, and the host's own visibility starts and stops it, so
        /// switching away from Home costs nothing and coming back needs no help from the tab
        /// machinery.
        /// </summary>
        private void EnsureBillboardWired()
        {
            if (_billboardWired) return;
            var dash = SettingsTab;
            var host = dash?.DashBillboard;
            var rack = dash?.BillboardRackHost;
            if (host == null || rack == null) return;

            _billboardRack = Services.DashboardBillboard.InitialRack(Services.DashboardBillboard.Roster.Count);
            _billboardSlots.Clear();
            for (int i = 0; i < _billboardRack.Slots.Count; i++)
            {
                var vm = new BillboardSlotVm();
                FillBillboardSlot(vm, _billboardRack.Slots[i]);
                _billboardSlots.Add(vm);
            }
            rack.ItemsSource = _billboardSlots;

            host.MouseEnter += (_, _) => { _billboardPointerOver = true; StopBillboardClock(); };
            host.MouseLeave += (_, _) => { _billboardPointerOver = false; RestartBillboardClock(); };
            host.IsVisibleChanged += (_, _) =>
            {
                if (host.IsVisible) RestartBillboardClock();
                else StopBillboardClock();
            };
            _billboardWired = true;
        }

        private void RestartBillboardClock()
        {
            StopBillboardClock();

            var host = SettingsTab?.DashBillboard;
            if (host == null || !host.IsVisible) return;
            if (!MotionFx.AllowAmbientLoops) return;
            if (!Services.DashboardBillboard.ShouldAdvance(_billboardPointerOver, onScreen: true)) return;

            _billboardTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(Services.DashboardBillboard.RotateSeconds),
            };
            _billboardTimer.Tick += (_, _) => StepBillboardRack();
            _billboardTimer.Start();
        }

        private void StopBillboardClock()
        {
            try { _billboardTimer?.Stop(); } catch { }
            _billboardTimer = null;
        }

        /// <summary>One swap: the content of exactly one slot, and nothing else anywhere.</summary>
        private void StepBillboardRack()
        {
            try
            {
                var host = SettingsTab?.DashBillboard;
                if (!Services.DashboardBillboard.ShouldAdvance(_billboardPointerOver, host?.IsVisible == true)) return;
                if (_billboardRack == null) return;

                var next = Services.DashboardBillboard.NextRack(_billboardRack, Services.DashboardBillboard.Roster.Count);
                _billboardRack = next;
                int slot = next.ChangedSlot;
                if (slot < 0 || slot >= _billboardSlots.Count) return;

                FillBillboardSlot(_billboardSlots[slot], next.Slots[slot]);
                FadeBillboardSlot(slot);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard billboard: rack step failed"); }
        }

        private void FillBillboardSlot(BillboardSlotVm vm, int cardIndex)
        {
            var card = Services.DashboardBillboard.CardAt(cardIndex);
            vm.Id = card.Id;
            vm.Eyebrow = Loc.Get(card.EyebrowKey);
            vm.Title = Loc.Get(card.TitleKey);
            vm.Line = Loc.Get(card.LineKey);
            vm.Tip = Loc.Get(card.TitleKey) + "  ·  " + Loc.Get(card.LineKey);
            vm.Plate = card.Art == BillboardArt.Plate;
            vm.Art = LoadBillboardArt(card);
        }

        private static ImageSource? LoadBillboardArt(BillboardCard card)
        {
            try
            {
                // Missing art must leave the words readable over the shade, not throw.
                var art = new BitmapImage();
                art.BeginInit();
                art.UriSource = new Uri(Services.DashboardBillboard.PosterUri(card), UriKind.Absolute);
                art.CacheOption = BitmapCacheOption.OnLoad;
                art.EndInit();
                art.Freeze();
                return art;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Billboard art {Poster} did not load: {E}", card.Poster, ex.Message);
                return null;
            }
        }

        /// <summary>Fades the one slot that changed, and only when the motion gate allows it.</summary>
        private void FadeBillboardSlot(int slot)
        {
            if (!MotionFx.AllowTransitions) return;
            var rack = SettingsTab?.BillboardRackHost;
            if (rack?.ItemContainerGenerator.ContainerFromIndex(slot) is not UIElement container) return;
            container.BeginAnimation(UIElement.OpacityProperty, null);
            container.Opacity = 0;
            container.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(BillboardFadeMs))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }

        /// <summary>
        /// The only thing on the rack that ever leaves the page. A Link goes out through
        /// BrowserLauncher - the four-strategy opener with the clipboard fallback, the same one the
        /// Web App door uses - and a Tab is a plain in-app navigation.
        /// </summary>
        internal void BillboardCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.DataContext is not BillboardSlotVm vm) return;
                BillboardCard? card = null;
                foreach (var c in Services.DashboardBillboard.Roster)
                    if (string.Equals(c.Id, vm.Id, StringComparison.Ordinal)) { card = c; break; }
                if (card == null) return;

                if (card.Kind == BillboardTargetKind.Link)
                    Helpers.BrowserLauncher.OpenUrlOrPrompt(card.Target, Loc.Get(card.TitleKey));
                else
                    ShowTab(card.Target);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard billboard: card click failed"); }
        }
    }
}
