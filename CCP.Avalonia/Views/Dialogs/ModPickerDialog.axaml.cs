using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// One row in the first-run picker. Every visual decision is an INPC property (including the
    /// visibility ones) so the DataTemplate needs no value converters — a converter declared in the
    /// wrong resource scope is one of this codebase's recurring WPF traps.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/ModPickerDialog.xaml.cs. Deviations:
    ///  - the <c>Visibility</c> properties became bools named <c>XVisible</c>: Avalonia binds
    ///    <c>IsVisible</c> to a bool directly (CLAUDE.md trap 3).
    ///  - <c>Brush.Freeze()</c> has no Avalonia equivalent (immutability is <c>ToImmutable()</c> on
    ///    the concrete brush); the accent is built once per card and never mutated, so it is
    ///    dropped rather than emulated.
    ///  - <c>ArtUri</c> was a <c>pack://</c> URI bound straight to <c>Image.Source</c>, never
    ///    routed through <c>ModResourceResolver</c>. The same six PNGs are linked into this head
    ///    under <c>avares://CCP.Avalonia/Resources/</c>, and <see cref="Art"/> loads them through
    ///    <c>Helpers.ModArt.TryLoad</c> - so a mod MAY now override a pass card, which the WPF
    ///    pack:// path did not allow. Null still draws the accent-framed tile alone, as WPF does
    ///    before the bitmap resolves.
    /// </summary>
    public sealed class ModPickerCard : INotifyPropertyChanged
    {
        public enum CardState { Available, Queued, Downloading, Installing, Installed, Failed }

        public string ModId { get; init; } = "";
        public string? PackId { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";

        /// <summary>
        /// The pass-card art, mod override first (a tier WPF's bare pack:// URI did not have).
        /// Null when neither the override nor the shipped copy loads, and the accent-framed tile
        /// is then the whole card - not a failure.
        /// </summary>
        public IImage? Art { get; init; }

        public IBrush AccentBrush { get; init; } = Brushes.HotPink;
        public string PremiumNote { get; init; } = "";
        public string VoiceNote { get; init; } = "";

        public bool HasPack => !string.IsNullOrEmpty(PackId);

        public bool PremiumNoteVisible => !string.IsNullOrEmpty(PremiumNote);

        public bool VoiceNoteVisible => !string.IsNullOrEmpty(VoiceNote);

        private CardState _state = CardState.Available;
        public CardState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressVisible));
                OnPropertyChanged(nameof(StatusOnlyVisible));
                OnPropertyChanged(nameof(InstalledVisible));
                OnPropertyChanged(nameof(SelectVisible));
                OnPropertyChanged(nameof(SizeVisible));
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
                OnPropertyChanged(nameof(SizeVisible));
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

        public bool InstalledVisible => IsInstalled;

        public bool SelectVisible => HasPack && !IsInstalled;

        public bool SizeVisible => !IsInstalled && !string.IsNullOrEmpty(SizeText);

        public bool ProgressVisible =>
            State == CardState.Downloading || State == CardState.Installing;

        public bool StatusOnlyVisible =>
            State == CardState.Queued || State == CardState.Failed;

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
    /// One built-in mod's presentation data. Copied from
    /// ConditioningControlPanel/Dialogs/ModPackCatalog.cs (only the fields this screen reads): the
    /// catalogue lives in the WPF head next to the dialogs, not in CCP.Core, and neither may be
    /// touched by this port. WPF's <c>ArtUri</c> becomes <see cref="ArtName"/>: the same file,
    /// named the way <c>Helpers.ModArt</c> wants it rather than as a <c>pack://</c> URI.
    /// </summary>
    public sealed class ModPickerCatalogEntry
    {
        /// <summary>Built-in mod id (BuiltInMods).</summary>
        public string ModId { get; init; } = "";

        /// <summary>Release-content pack id, or null for CCP Default (nothing to download).</summary>
        public string? PackId { get; init; }

        public string NameLocKey { get; init; } = "";
        public string DescriptionLocKey { get; init; } = "";

        /// <summary>
        /// The card's art, as a forward-slash path under <c>Resources/</c> - the shape
        /// <c>Helpers.ModArt.TryLoad</c> takes, so an active mod may override it.
        /// </summary>
        public string ArtName { get; init; } = "";

        /// <summary>Mod accent colour, for the card's art frame.</summary>
        public string AccentHex { get; init; } = "#FF69B4";

        /// <summary>
        /// Fallback download size used before (or instead of) a manifest fetch — the picker must show
        /// honest numbers offline too. Overridden by the manifest's real sizeBytes when available.
        /// </summary>
        public long ApproxBytes { get; init; }

        /// <summary>The mod is free, but its flagship multi-day training program needs Premium.</summary>
        public bool PremiumProgramNote { get; init; }

        /// <summary>The mod ships no companion_audio — its companion is text-only.</summary>
        public bool NoVoiceNote { get; init; }
    }

    /// <summary>
    /// The mod-id/pack-id mapping plus card copy and sizes, copied from
    /// ConditioningControlPanel/Dialogs/ModPackCatalog.cs. Both id sets are read from the constants
    /// that own them — the mod ids from <see cref="BuiltInMods"/> and the pack ids from
    /// <see cref="CoreReleaseContent"/>, which the pack service aliases — so a renamed id cannot
    /// drift out of this screen.
    /// </summary>
    internal static class ModPickerCatalog
    {
        private const long Mb = 1024L * 1024L;

        /// <summary>Display order: baseline first, then the five optional mods.</summary>
        public static readonly IReadOnlyList<ModPickerCatalogEntry> All = new[]
        {
            new ModPickerCatalogEntry
            {
                ModId = BuiltInMods.CCPDefaultId,
                ArtName = "logo.png",
                PackId = null, // ships in the box — the neutral baseline is what "skip everything" gives you
                NameLocKey = "modpicker_name_ccp_default",
                DescriptionLocKey = "modpicker_desc_ccp_default",
                AccentHex = "#E84393",
                ApproxBytes = 0
            },
            new ModPickerCatalogEntry
            {
                ModId = BuiltInMods.BambiSleepId,
                ArtName = "intake/pass_card_bambi.png",
                PackId = CoreReleaseContent.PackModBambi,
                NameLocKey = "label_bambi_sleep",
                DescriptionLocKey = "modpicker_desc_bambi",
                AccentHex = "#FF69B4",
                ApproxBytes = 77 * Mb
            },
            new ModPickerCatalogEntry
            {
                ModId = BuiltInMods.SissyHypnoId,
                ArtName = "intake/pass_card_sissy.png",
                PackId = CoreReleaseContent.PackModSissy,
                NameLocKey = "label_sissy_hypno",
                DescriptionLocKey = "modpicker_desc_sissy",
                AccentHex = "#9B59B6",
                ApproxBytes = 331 * Mb
            },
            new ModPickerCatalogEntry
            {
                ModId = BuiltInMods.LockedId,
                ArtName = "intake/pass_card_circe.png",
                PackId = CoreReleaseContent.PackModLocked,
                NameLocKey = "modpicker_name_circe",
                DescriptionLocKey = "modpicker_desc_circe",
                AccentHex = "#E81CA8",
                ApproxBytes = 329 * Mb,
                PremiumProgramNote = true // the "kept" program is Premium; the mod itself is free
            },
            new ModPickerCatalogEntry
            {
                ModId = BuiltInMods.DronificationId,
                ArtName = "intake/pass_card_drone.png",
                PackId = CoreReleaseContent.PackModDrone,
                NameLocKey = "modpicker_name_drone",
                DescriptionLocKey = "modpicker_desc_drone",
                AccentHex = "#00FF41",
                ApproxBytes = 184 * Mb,
                PremiumProgramNote = true, // "firmware_install" is Premium
                NoVoiceNote = true         // drone-mode.ccpmod carries no companion_audio at all
            },
            new ModPickerCatalogEntry
            {
                ModId = BuiltInMods.InfectionControlId,
                ArtName = "intake/pass_card_infection.png",
                PackId = CoreReleaseContent.PackModInfection,
                NameLocKey = "modpicker_name_infection",
                DescriptionLocKey = "modpicker_desc_infection",
                AccentHex = "#2855F0",
                ApproxBytes = 209 * Mb
            },
        };

        /// <summary>The optional mods (everything with a downloadable pack).</summary>
        public static IEnumerable<ModPickerCatalogEntry> Optional => All.Where(e => !string.IsNullOrEmpty(e.PackId));

        public static ModPickerCatalogEntry? ForMod(string? modId) =>
            string.IsNullOrEmpty(modId)
                ? null
                : All.FirstOrDefault(e => string.Equals(e.ModId, modId, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The pack's bytes are stamped on disk. Unseeded (no pack service on this head) nothing is
        /// KNOWN to be installed, and nothing is reported missing either - see NeedsDownload.
        /// </summary>
        internal static bool IsInstalled(string? packId) =>
            !string.IsNullOrEmpty(packId)
            && CoreReleaseContent.StampProvider is not null
            && CoreReleaseContent.GetStampFor(packId!) != null;

        /// <summary>
        /// True when this mod's media has to come off the network. False for CCP Default and, as in
        /// WPF, whenever there is no pack service to fetch it with.
        /// </summary>
        internal static bool NeedsDownload(string? modId)
        {
            var packId = string.IsNullOrEmpty(modId) ? null : ModService.PackIdForMod(modId);
            return !string.IsNullOrEmpty(packId)
                   && CoreReleaseContent.StampProvider is not null
                   && CoreReleaseContent.GetStampFor(packId!) == null;
        }

        /// <summary>
        /// Best known download size: the manifest's real sizeBytes through
        /// <see cref="CoreReleaseContent.GetPackInfo"/>, falling back to the baked-in approximation
        /// when no head has seeded a pack service or the manifest has not been fetched — which is
        /// exactly what the WPF original falls back to offline.
        /// </summary>
        public static long SizeBytesFor(ModPickerCatalogEntry entry)
        {
            try
            {
                if (entry == null || string.IsNullOrEmpty(entry.PackId)) return entry?.ApproxBytes ?? 0;
                var info = CoreReleaseContent.GetPackInfo(entry.PackId!);
                if (info != null && info.SizeBytes > 0) return info.SizeBytes;
                return entry.ApproxBytes;
            }
            catch { return entry?.ApproxBytes ?? 0; }
        }

        /// <summary>"331 MB" / "1.2 GB". Empty for a zero size.</summary>
        public static string FormatSize(long bytes)
        {
            try
            {
                if (bytes <= 0) return "";
                var mb = bytes / (double)Mb;
                if (mb >= 1024)
                    return Loc.GetF("modpicker_size_gb", (mb / 1024.0).ToString("0.0"));
                return Loc.GetF("modpicker_size_mb", Math.Max(1, (int)Math.Round(mb)));
            }
            catch { return ""; }
        }
    }

    /// <summary>
    /// First-run mod picker (docs/CONTENT_PACKS_PLAN.md §4). The mod media no longer ships in the
    /// installer, so a fresh modular install gets one screen to choose which mods to pull down.
    ///
    /// Contract, in order of importance:
    /// <list type="bullet">
    /// <item>Skipping is always allowed and costs nothing — every mod degrades to the CCP baseline
    /// exactly as the graceful-missing behaviour already does.</item>
    /// <item>Closing mid-download does NOT cancel: the pack request keeps running (and de-dupes, so
    /// re-opening the Mod Manager joins the same task).</item>
    /// <item>Offline / manifest-unavailable is a first-class state: cards still render with the
    /// baked-in approximate sizes, the download button is disabled, and the hint says we retry next
    /// launch.</item>
    /// <item>The audio-base pack is deliberately NOT a card — it downloads automatically in the
    /// background at startup. This screen is mods only.</item>
    /// </list>
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/ModPickerDialog.xaml.cs. Deviations:
    ///  - <c>App.ReleaseContent</c> is this head's <see cref="App.ReleaseContent"/>; PendingModActivation
    ///    is Core's <see cref="PendingModChoice"/> plus <c>MainShellWindow.ApplyPendingModChoice</c>.
    ///    <c>MarshalToUi</c> hops through <c>Dispatcher.UIThread</c>.
    ///  - <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>; <c>MouseLeftButtonDown</c> ->
    ///    <c>PointerPressed</c>, wired in the constructor.
    ///  - the download button's caption is re-BOUND, not assigned: assigning <c>.Text</c> over a
    ///    <c>{loc:Str}</c> binding survives only until the next language change (CLAUDE.md).
    ///  - the public constructor LOST its <c>= null</c> default. --render-all needs a real
    ///    parameterless constructor, and beside one an optional-argument overload is never the
    ///    better candidate: <c>new ModPickerDialog()</c> would silently pick the sample-data
    ///    constructor. Callers pass <c>null</c> explicitly, as TextEditorDialog's do.
    /// </summary>
    public partial class ModPickerDialog : Window
    {
        private readonly ObservableCollection<ModPickerCard> _cards = new();
        private readonly TextBlock _txtHint;
        private readonly TextBlock _txtDownload;
        private readonly Button _btnDownload;
        private readonly string? _preselectModId;
        private bool _offline;
        private bool _downloading;
        private bool _finished;
        private bool _closed;

        /// <summary>
        /// Render/design constructor: the real catalogue, plus the first two downloadable cards
        /// pushed into a mid-download and a failed state so --render-view draws the progress bar
        /// and the status-only line, which nothing else on this screen would show. The first two,
        /// because anything further down the list is below the fold in an 840x720 render.
        /// </summary>
        internal ModPickerDialog() : this(null)
        {
            var downloadable = _cards.Where(c => c.HasPack && !c.IsInstalled).ToList();
            if (downloadable.Count > 0) downloadable[0].MarkProgress(43);
            if (downloadable.Count > 1) downloadable[1].MarkFailed();
        }

        /// <param name="preselectModId">
        /// Built-in mod to tick on open — used for upgraders, whose one active mod just lost its
        /// bundled media, so restoring what they had is a single click. Null on a fresh install
        /// (nothing to restore; everything starts unticked).
        /// </param>
        public ModPickerDialog(string? preselectModId)
        {
            AvaloniaXamlLoader.Load(this);
            _preselectModId = preselectModId;

            _txtHint = this.FindControl<TextBlock>("TxtHint")!;
            _txtDownload = this.FindControl<TextBlock>("TxtDownload")!;
            _btnDownload = this.FindControl<Button>("BtnDownload")!;

            BuildCards(preselectModId);
            this.FindControl<ItemsControl>("CardsList")!.ItemsSource = _cards;

            // Handlers live here rather than in markup, per the porting convention.
            this.FindControl<Border>("TitleBar")!.PointerPressed += TitleBar_PointerPressed;
            this.FindControl<Button>("BtnCloseX")!.Click += (_, _) => BtnSkip_Click();
            this.FindControl<Button>("BtnSkip")!.Click += (_, _) => BtnSkip_Click();
            _btnDownload.Click += async (_, _) => await BtnDownload_Click();

            // Fires once a pack's bytes are on disk and its .ccpmod has been extracted into its
            // built-in slot — the moment the mod is genuinely usable. Raised on the download
            // thread, so the handler marshals.
            CoreReleaseContent.PackInstalled += OnPackInstalled;
            if (App.ReleaseContent is { } svc) svc.PackProgressChanged += OnPackProgressChanged;
            if (App.Mods is { } mods) mods.ModAvailabilityChanged += OnModAvailabilityChanged;

            Loaded += OnDialogLoaded;
            Closed += OnDialogClosed;
        }

        // ------------------------------------------------------------------ build

        private void BuildCards(string? preselectModId = null)
        {
            foreach (var entry in ModPickerCatalog.All)
            {
                IBrush accent;
                try { accent = new SolidColorBrush(Color.Parse(entry.AccentHex)); }
                catch { accent = Brushes.HotPink; }

                var card = new ModPickerCard
                {
                    ModId = entry.ModId,
                    PackId = entry.PackId,
                    Name = Loc.Get(entry.NameLocKey),
                    Description = Loc.Get(entry.DescriptionLocKey),
                    Art = Helpers.ModArt.TryLoad(entry.ArtName),
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
                    card.SizeText = ModPickerCatalog.FormatSize(ModPickerCatalog.SizeBytesFor(entry));
                    if (IsPackInstalled(entry.PackId))
                    {
                        card.State = ModPickerCard.CardState.Installed;
                        card.CanSelect = false;
                    }
                    else if (!string.IsNullOrEmpty(preselectModId)
                             && string.Equals(entry.ModId, preselectModId, StringComparison.OrdinalIgnoreCase))
                    {
                        // The mod this user was already running — pre-ticked so one press restores it.
                        card.IsSelected = true;
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

        private async void OnDialogLoaded(object? sender, EventArgs e)
        {
            try
            {
                var svc = App.ReleaseContent;
                if (svc == null)
                {
                    // No pack service (render / --render-all): the cards show approximate sizes and
                    // nothing can be downloaded from here.
                    if (CoreSettings.HasProvider) SetOfflineState();
                    else UpdateDownloadButton();
                    return;
                }
                if (svc.IsFullInstall)
                {
                    foreach (var card in _cards) card.MarkInstalled();
                    UpdateDownloadButton();
                    return;
                }

                // Startup usually fetched the manifest already; only pay for a round trip when a
                // pack is genuinely unknown.
                if (ModPickerCatalog.Optional.Any(en => svc.GetPackInfo(en.PackId!) == null)
                    && await svc.FetchManifestAsync() == null)
                {
                    SetOfflineState();
                    return;
                }

                RefreshSizes();
                UpdateDownloadButton();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModPicker] Manifest refresh failed - falling back to offline copy");
                SetOfflineState();
            }
        }

        private void RefreshSizes()
        {
            foreach (var entry in ModPickerCatalog.Optional)
            {
                var card = _cards.FirstOrDefault(c => c.PackId == entry.PackId);
                if (card == null || card.IsInstalled) continue;
                card.SizeText = ModPickerCatalog.FormatSize(ModPickerCatalog.SizeBytesFor(entry));
            }
        }

        /// <summary>
        /// True when this showing ended in the offline state: the manifest could not be reached (or
        /// the pack service was missing), so every card was dead and the user could not have chosen
        /// anything. <see cref="ShowIfNeeded"/> uses it to hand the one-shot offer back instead of
        /// burning it on a screen that could not download.
        /// </summary>
        public bool EndedOffline => _offline;

        private void SetOfflineState()
        {
            _offline = true;
            BindLoc(_txtHint, "modpicker_hint_offline");
            _btnDownload.IsEnabled = false;
            // The shared PinkButton template has no disabled visual, so dim it explicitly —
            // otherwise a dead button looks live.
            _btnDownload.Opacity = 0.45;
            foreach (var card in _cards)
            {
                card.IsSelected = false;
                card.CanSelect = false;
            }
        }

        // ------------------------------------------------------------------ events

        private void OnPackProgressChanged(object? sender, PackProgressEventArgs e) =>
            MarshalToUi(() => FindCard(e.PackId)?.MarkProgress(e.Percent));

        /// <summary>Argument is a mod id (pack id as a fallback) - map it back to this screen's cards.</summary>
        private void OnModAvailabilityChanged(object? sender, string modOrPackId) =>
            MarshalToUi(() =>
            {
                FindCard(ModService.PackIdForMod(modOrPackId) ?? modOrPackId)?.MarkInstalled();
                UpdateDownloadButton();
            });

        private void OnPackInstalled(object? sender, string packId)
        {
            // Raised from the download loop's background thread.
            MarshalToUi(() =>
            {
                var card = FindCard(packId);
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
                if (Dispatcher.UIThread.CheckAccess())
                {
                    action();
                    return;
                }
                Dispatcher.UIThread.Post(action, DispatcherPriority.Normal);
            }
            catch { }
        }

        // ------------------------------------------------------------------ actions

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                try { BeginMoveDrag(e); } catch { /* dragging throws if the button was already released */ }
            }
        }

        private void BtnSkip_Click()
        {
            // Skipping never cancels anything already in flight — that is the whole point of the
            // "downloads keep going in the background" hint.
            Close();
        }

        /// <summary>WPF BtnDownload_Click (ModPickerDialog.xaml.cs:443-532). Returns when the queue is done.</summary>
        internal async Task BtnDownload_Click()
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

            var queue = _cards.Where(c => c.HasPack && c.IsSelected && !c.IsInstalled).ToList();
            if (queue.Count == 0)
            {
                Close();
                return;
            }

            // Choosing a mod here MEANS choosing to run it (first one in catalogue order). Upgraders
            // (preselect set) only re-download their own mod's media, so their intent is not recorded.
            if (_preselectModId == null)
            {
                PendingModChoice.Record(queue[0].ModId, App.Mods?.ActiveModId);
                Windows.MainShellWindow.ApplyPendingModChoice(Windows.MainShellWindow.ModChoiceTrigger.Immediate);
            }

            _downloading = true;
            _btnDownload.IsEnabled = false;
            _btnDownload.Opacity = 0.45;
            foreach (var card in queue)
            {
                card.CanSelect = false;
                card.MarkQueued();
            }

            // Sequential: one saturated connection beats several fighting over the same line.
            foreach (var card in queue)
            {
                card.MarkProgress(0);
                var ok = false;
                try
                {
                    // CancellationToken.None on purpose: closing this dialog must not kill the download.
                    ok = await svc.RequestPackAsync(card.PackId!, new Progress<double>(card.MarkProgress), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[ModPicker] Pack {Pack} download threw", card.PackId);
                }
                if (ok) card.MarkInstalled();
                else card.MarkFailed();
            }

            _downloading = false;

            // Failed cards stay ticked and selectable, and the button stays "Download selected":
            // RequestPackAsync resumes from the .partial, so a second press is the retry.
            var failed = queue.Where(c => c.State == ModPickerCard.CardState.Failed).ToList();
            foreach (var card in failed)
            {
                card.IsSelected = true;
                card.CanSelect = true;
            }
            _finished = failed.Count == 0;
            UpdateDownloadButton();
        }

        private void UpdateDownloadButton()
        {
            if (_closed || _offline) return;

            if (_downloading)
            {
                _btnDownload.IsEnabled = false;
                _btnDownload.Opacity = 0.45;
                return;
            }

            _btnDownload.IsEnabled = true;
            _btnDownload.Opacity = 1.0;

            // A failure outranks everything: the button has to stay an action, not a Close, or the
            // re-enabled checkbox has nothing to press.
            if (_cards.Any(c => c.State == ModPickerCard.CardState.Failed))
            {
                _finished = false;
                BindLoc(_txtDownload, "modpicker_btn_download");
                return;
            }

            if (_finished || _cards.All(c => !c.HasPack || c.IsInstalled))
            {
                _finished = true;
                BindLoc(_txtDownload, "modpicker_btn_close");
            }
        }

        /// <summary>What {loc:Str} does, for a key only known at runtime.</summary>
        private static void BindLoc(TextBlock target, string key) =>
            target[!TextBlock.TextProperty] = new Binding($"[{key}]") { Source = LocalizationManager.Instance };

        private void OnDialogClosed(object? sender, EventArgs e)
        {
            _closed = true;
            try
            {
                CoreReleaseContent.PackInstalled -= OnPackInstalled;
                if (App.ReleaseContent is { } svc) svc.PackProgressChanged -= OnPackProgressChanged;
                if (App.Mods is { } mods) mods.ModAvailabilityChanged -= OnModAvailabilityChanged;
            }
            catch { }
        }

        /// <summary>
        /// Upper bound on offline showings before <c>ModPickerShown</c> is allowed to latch anyway.
        /// Keeps "retry when the manifest comes back" from becoming an every-launch popup for
        /// someone who is deliberately offline forever.
        /// </summary>
        public const int MaxOfflineOffers = 3;

        /// <summary>
        /// Guard 1 as a pure predicate: skip opening the picker (without spending the one-shot
        /// offer) because this session cannot reach the manifest and we have not yet used up the
        /// offline allowance.
        /// </summary>
        internal static bool ShouldDeferForOffline(bool offlineMode, bool manifestUnavailable, int offlineOffers)
            => offlineOffers < MaxOfflineOffers && (offlineMode || manifestUnavailable);

        /// <summary>
        /// Guard 2 as a pure predicate: after a showing that ended offline (and whose count has
        /// already been incremented), should the offer be handed back for a later launch?
        /// </summary>
        internal static bool ShouldReArmAfterOfflineShowing(int offlineOffersAfterShowing)
            => offlineOffersAfterShowing < MaxOfflineOffers;

        /// <summary>
        /// ShowIfNeeded's open/skip guards as one predicate, in WPF's order
        /// (ConditioningControlPanel/Dialogs/ModPickerDialog.xaml.cs:631-647): already shown, no pack
        /// service, full install, then guard 1 (known offline, offer not spent).
        /// </summary>
        internal static bool ShouldShow(AppSettings settings, bool hasPackService, bool isFullInstall, bool manifestUnavailable)
            => !settings.ModPickerShown
               && hasPackService
               && !isFullInstall
               && !ShouldDeferForOffline(settings.OfflineMode, manifestUnavailable, settings.ModPickerOfflineOffers);

        /// <summary>WPF <c>App.ReleaseContent != null</c>: null on render / headless paths.</summary>
        internal static bool HasPackService => App.ReleaseContent != null;

        /// <summary>
        /// PORTED from ConditioningControlPanel/Dialogs/ModPickerDialog.xaml.cs:631. Deviation:
        /// Avalonia's ShowDialog is async and needs a non-null owner, so this is awaited.
        ///
        /// ponytail: no caller, on purpose. WPF 6.11.x removed the returning-user auto-open
        /// (MainWindow.xaml.cs:587-588) and constructs ModPickerDialog nowhere; mods are offered by
        /// the first-run wizard and the Mod Manager. The dialog and its download flow stay ported
        /// (tested) so re-adding a caller is one line if WPF brings a caller back.
        /// </summary>
        public static async Task<bool> ShowIfNeeded(Window owner, bool preselectActiveMod = false)
        {
            try
            {
                if (!CoreSettings.HasProvider) return false;   // render / nav-check: no profile
                var settings = CoreSettings.Current;
                var svc = App.ReleaseContent;
                if (!ShouldShow(settings, HasPackService, svc?.IsFullInstall == true, svc?.ManifestUnavailable == true))
                {
                    Log.Debug("[ModPicker] Not showing (shown {Shown}, service {Svc}, offline {Offline}, manifest unavailable {Unavail})",
                        settings.ModPickerShown, svc != null, settings.OfflineMode, svc?.ManifestUnavailable);
                    return false;
                }

                var preselect = preselectActiveMod ? settings.ActiveModId : null;
                if (!string.IsNullOrEmpty(preselect) && ModService.PackIdForMod(preselect) == null)
                    preselect = null;   // CCP Default or a user mod - nothing on this screen to tick

                // Latch BEFORE showing: a crash inside the dialog must not become an every-launch popup.
                settings.ModPickerShown = true;
                CoreSettings.Save();
                Log.Information("[ModPicker] Showing the mod picker (preselect {Mod})", preselect ?? "(none)");

                var dialog = new ModPickerDialog(preselect);
                await dialog.ShowDialogSafe(owner);

                // Guard 2: ended offline - count it and re-arm unless the allowance is spent.
                if (dialog.EndedOffline)
                {
                    settings.ModPickerOfflineOffers++;
                    if (ShouldReArmAfterOfflineShowing(settings.ModPickerOfflineOffers))
                        settings.ModPickerShown = false;
                    CoreSettings.Save();
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModPicker] ShowIfNeeded failed - skipping the picker this launch");
                return false;
            }
        }
    }
}
