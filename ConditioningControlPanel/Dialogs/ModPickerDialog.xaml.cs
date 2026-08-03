using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One row in the first-run picker. Every visual decision is an INPC property (including the
    /// Visibility ones) so the DataTemplate needs no value converters — a converter declared in the
    /// wrong resource scope is one of this codebase's recurring WPF traps.
    /// </summary>
    public sealed class ModPickerCard : INotifyPropertyChanged
    {
        public enum CardState { Available, Queued, Downloading, Installing, Installed, Failed }

        public string ModId { get; init; } = "";
        public string? PackId { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string ArtUri { get; init; } = "";
        public Brush AccentBrush { get; init; } = Brushes.HotPink;
        public string PremiumNote { get; init; } = "";
        public string VoiceNote { get; init; } = "";

        public bool HasPack => !string.IsNullOrEmpty(PackId);

        public Visibility PremiumNoteVisibility =>
            string.IsNullOrEmpty(PremiumNote) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility VoiceNoteVisibility =>
            string.IsNullOrEmpty(VoiceNote) ? Visibility.Collapsed : Visibility.Visible;

        private CardState _state = CardState.Available;
        public CardState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressVisibility));
                OnPropertyChanged(nameof(StatusOnlyVisibility));
                OnPropertyChanged(nameof(InstalledVisibility));
                OnPropertyChanged(nameof(SelectVisibility));
                OnPropertyChanged(nameof(SizeVisibility));
            }
        }

        private string _sizeText = "";
        public string SizeText
        {
            get => _sizeText;
            set
            {
                if (_sizeText == value) return;
                _sizeText = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeVisibility));
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set { if (_statusText == value) return; _statusText = value ?? ""; OnPropertyChanged(); }
        }

        private double _percent;
        public double Percent
        {
            get => _percent;
            set { if (Math.Abs(_percent - value) < 0.01) return; _percent = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
        }

        private bool _canSelect = true;
        public bool CanSelect
        {
            get => _canSelect;
            set { if (_canSelect == value) return; _canSelect = value; OnPropertyChanged(); }
        }

        public bool IsInstalled => State == CardState.Installed;

        public Visibility InstalledVisibility => IsInstalled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SelectVisibility =>
            (HasPack && !IsInstalled) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SizeVisibility =>
            (!IsInstalled && !string.IsNullOrEmpty(SizeText)) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ProgressVisibility =>
            (State == CardState.Downloading || State == CardState.Installing) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility StatusOnlyVisibility =>
            (State == CardState.Queued || State == CardState.Failed) ? Visibility.Visible : Visibility.Collapsed;

        // ---- state transitions (all UI-thread) ----

        public void MarkQueued()
        {
            State = CardState.Queued;
            StatusText = Loc.Get("modpicker_status_queued");
        }

        public void MarkProgress(double percent)
        {
            Percent = Math.Max(0, Math.Min(100, percent));
            if (Percent >= 100)
            {
                State = CardState.Installing;
                StatusText = Loc.Get("modpicker_status_installing");
            }
            else
            {
                State = CardState.Downloading;
                StatusText = Loc.GetF("modpicker_status_downloading", (int)Math.Round(Percent));
            }
        }

        public void MarkInstalled()
        {
            Percent = 100;
            State = CardState.Installed;
            StatusText = Loc.Get("modpicker_status_done");
            CanSelect = false;
        }

        public void MarkFailed()
        {
            State = CardState.Failed;
            StatusText = Loc.Get("modpicker_status_failed");
            CanSelect = true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// First-run mod picker (docs/CONTENT_PACKS_PLAN.md §4). The mod media no longer ships in the
    /// installer, so a fresh modular install gets one screen to choose which mods to pull down.
    ///
    /// Contract, in order of importance:
    /// <list type="bullet">
    /// <item>Skipping is always allowed and costs nothing — every mod degrades to the CCP baseline
    /// exactly as the graceful-missing behaviour already does.</item>
    /// <item>Closing mid-download does NOT cancel: <see cref="ReleaseContentService.RequestPackAsync"/>
    /// keeps running (and de-dupes, so re-opening the Mod Manager joins the same task).</item>
    /// <item>Offline / manifest-unavailable is a first-class state: cards still render with the
    /// baked-in approximate sizes, the download button is disabled, and the hint says we retry next
    /// launch. <c>App.ReleaseContent</c> being null must not crash anything.</item>
    /// <item>The audio-base pack is deliberately NOT a card — it downloads automatically in the
    /// background at startup (<c>EnsureBaselineAsync</c>). This screen is mods only.</item>
    /// </list>
    /// </summary>
    public partial class ModPickerDialog : Window
    {
        private readonly ObservableCollection<ModPickerCard> _cards = new();
        private bool _offline;
        private bool _downloading;
        private bool _finished;
        private bool _closed;

        public ModPickerDialog()
        {
            InitializeComponent();

            BuildCards();
            CardsList.ItemsSource = _cards;

            try
            {
                var svc = App.ReleaseContent;
                if (svc != null)
                {
                    svc.PackProgressChanged += OnPackProgressChanged;
                    svc.PackInstalled += OnPackInstalled;
                }

                // Fires once the pack's .ccpmod has been extracted into its built-in slot and the
                // resolver cache dropped — the moment the mod is genuinely usable, one step past
                // "bytes on disk". Both handlers land on the same MarkInstalled, so order is moot.
                if (App.Mods != null) App.Mods.ModAvailabilityChanged += OnModAvailabilityChanged;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ModPicker] Could not subscribe to release-content events");
            }

            Loaded += OnDialogLoaded;
            Closed += OnDialogClosed;
        }

        // ------------------------------------------------------------------ build

        private void BuildCards()
        {
            foreach (var entry in ModPackCatalog.All)
            {
                Brush accent;
                try
                {
                    accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(entry.AccentHex));
                    accent.Freeze();
                }
                catch { accent = Brushes.HotPink; }

                var card = new ModPickerCard
                {
                    ModId = entry.ModId,
                    PackId = entry.PackId,
                    Name = Loc.Get(entry.NameLocKey),
                    Description = Loc.Get(entry.DescriptionLocKey),
                    ArtUri = entry.ArtUri,
                    AccentBrush = accent,
                    PremiumNote = entry.PremiumProgramNote ? Loc.Get("modpicker_note_premium_programs") : "",
                    VoiceNote = entry.NoVoiceNote ? Loc.Get("modpicker_note_no_voice") : ""
                };

                if (!card.HasPack)
                {
                    // CCP Default ships in the box — shown so the picker reads as "what you already
                    // have + what you can add", never selectable.
                    card.State = ModPickerCard.CardState.Installed;
                    card.CanSelect = false;
                }
                else
                {
                    card.SizeText = ModPackCatalog.FormatSize(ModPackCatalog.SizeBytesFor(entry));
                    if (IsPackInstalled(entry.PackId))
                    {
                        card.State = ModPickerCard.CardState.Installed;
                        card.CanSelect = false;
                    }
                }

                _cards.Add(card);
            }
        }

        private static bool IsPackInstalled(string? packId)
        {
            try
            {
                if (string.IsNullOrEmpty(packId)) return false;
                var svc = App.ReleaseContent;
                if (svc == null) return false;
                return svc.IsFullInstall || svc.IsInstalled(packId!);
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ manifest

        private async void OnDialogLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var svc = App.ReleaseContent;
                if (svc == null)
                {
                    // The service failed to construct this session. Show the cards (with approximate
                    // sizes) but there is nothing to download from here.
                    SetOfflineState();
                    return;
                }
                if (svc.IsFullInstall)
                {
                    // Bundled audio is on disk: everything is already "installed".
                    foreach (var card in _cards) card.MarkInstalled();
                    UpdateDownloadButton();
                    return;
                }

                // Startup's EnsureBaselineAsync usually fetched the manifest already; only pay for a
                // round trip when a pack is genuinely unknown.
                var needsFetch = ModPackCatalog.Optional.Any(en => svc.GetPackInfo(en.PackId!) == null);
                if (needsFetch)
                {
                    var manifest = await svc.FetchManifestAsync().ConfigureAwait(true);
                    if (manifest == null)
                    {
                        SetOfflineState();
                        return;
                    }
                }

                RefreshSizes();
                UpdateDownloadButton();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ModPicker] Manifest refresh failed - falling back to offline copy");
                SetOfflineState();
            }
        }

        private void RefreshSizes()
        {
            foreach (var entry in ModPackCatalog.Optional)
            {
                var card = _cards.FirstOrDefault(c => c.PackId == entry.PackId);
                if (card == null || card.IsInstalled) continue;
                card.SizeText = ModPackCatalog.FormatSize(ModPackCatalog.SizeBytesFor(entry));
            }
        }

        private void SetOfflineState()
        {
            _offline = true;
            TxtHint.Text = Loc.Get("modpicker_hint_offline");
            BtnDownload.IsEnabled = false;
            // The shared PinkButton template has no disabled visual, so dim it explicitly —
            // otherwise a dead button looks live.
            BtnDownload.Opacity = 0.45;
            foreach (var card in _cards)
            {
                card.IsSelected = false;
                card.CanSelect = false;
            }
        }

        // ------------------------------------------------------------------ events

        private void OnPackProgressChanged(object? sender, PackProgressEventArgs e)
        {
            // Fired from the download loop's background thread.
            MarshalToUi(() =>
            {
                var card = FindCard(e.PackId);
                card?.MarkProgress(e.Percent);
            });
        }

        private void OnPackInstalled(object? sender, string packId)
        {
            MarshalToUi(() =>
            {
                var card = FindCard(packId);
                card?.MarkInstalled();
                UpdateDownloadButton();
            });
        }

        /// <summary>Argument is a mod id (pack id as a fallback) — map it back to this screen's cards.</summary>
        private void OnModAvailabilityChanged(object? sender, string modOrPackId)
        {
            MarshalToUi(() =>
            {
                var card = FindCard(ModPackCatalog.PackIdForMod(modOrPackId) ?? modOrPackId);
                card?.MarkInstalled();
                UpdateDownloadButton();
            });
        }

        private ModPickerCard? FindCard(string? packId) =>
            string.IsNullOrEmpty(packId)
                ? null
                : _cards.FirstOrDefault(c => string.Equals(c.PackId, packId, StringComparison.OrdinalIgnoreCase));

        private static void MarshalToUi(Action action)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                if (dispatcher.CheckAccess())
                {
                    action();
                    return;
                }

                // Normal, never Loaded: Loaded-priority work is starved in this app (compositor host
                // + avatar animations keep the dispatcher busy) and silently never runs.
                dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
            }
            catch { }
        }

        // ------------------------------------------------------------------ actions

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { /* DragMove throws if the button was already released */ }
            }
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            // Skipping never cancels anything already in flight — that is the whole point of the
            // "downloads keep going in the background" hint.
            Close();
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_finished)
            {
                Close();
                return;
            }
            if (_downloading || _offline) return;

            var svc = App.ReleaseContent;
            if (svc == null)
            {
                SetOfflineState();
                return;
            }

            var queue = _cards
                .Where(c => c.HasPack && c.IsSelected && !c.IsInstalled)
                .ToList();

            if (queue.Count == 0)
            {
                Close();
                return;
            }

            _downloading = true;
            BtnDownload.IsEnabled = false;
            BtnDownload.Opacity = 0.45;
            foreach (var card in queue)
            {
                card.CanSelect = false;
                card.MarkQueued();
            }

            // Sequential: one saturated connection beats four fighting over the same line, and the
            // per-card bars stay readable.
            foreach (var card in queue)
            {
                card.MarkProgress(0);
                var progress = new Progress<double>(p => card.MarkProgress(p));

                var ok = false;
                try
                {
                    // CancellationToken.None on purpose: closing this dialog must not kill the download.
                    ok = await svc.RequestPackAsync(card.PackId!, progress, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "[ModPicker] Pack {Pack} download threw", card.PackId);
                }

                if (ok) card.MarkInstalled();
                else card.MarkFailed();
            }

            _downloading = false;
            _finished = true;
            UpdateDownloadButton();
        }

        private void UpdateDownloadButton()
        {
            if (_closed || _offline) return;

            if (_downloading)
            {
                BtnDownload.IsEnabled = false;
                BtnDownload.Opacity = 0.45;
                return;
            }

            BtnDownload.IsEnabled = true;
            BtnDownload.Opacity = 1.0;

            if (_finished || _cards.All(c => !c.HasPack || c.IsInstalled))
            {
                _finished = true;
                BtnDownload.Content = Loc.Get("modpicker_btn_close");
            }
        }

        private void OnDialogClosed(object? sender, EventArgs e)
        {
            _closed = true;
            try
            {
                if (App.Mods != null) App.Mods.ModAvailabilityChanged -= OnModAvailabilityChanged;

                var svc = App.ReleaseContent;
                if (svc != null)
                {
                    svc.PackProgressChanged -= OnPackProgressChanged;
                    svc.PackInstalled -= OnPackInstalled;
                }
            }
            catch { }
        }

        /// <summary>
        /// Shows the picker when this is a fresh modular install that has never seen it. Returns false
        /// (and shows nothing) for full/dev layouts, when the pack service is unavailable, or once the
        /// <see cref="Models.AppSettings.ModPickerShown"/> flag is set.
        /// </summary>
        public static bool ShowIfNeeded(Window? owner = null)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return false;
                if (settings.ModPickerShown) return false;

                var svc = App.ReleaseContent;
                if (svc == null) return false;      // no pack service this session — nothing to offer
                if (svc.IsFullInstall) return false; // full/dev layout: the mod media is already on disk

                // Set the flag BEFORE showing: a crash inside the dialog must not turn it into a
                // every-launch popup.
                settings.ModPickerShown = true;
                App.Settings?.Save();

                var dialog = new ModPickerDialog();
                if (owner != null && !ReferenceEquals(owner, dialog)) dialog.Owner = owner;
                dialog.ShowDialog();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[ModPicker] ShowIfNeeded failed - skipping the picker this launch");
                return false;
            }
        }
    }
}
