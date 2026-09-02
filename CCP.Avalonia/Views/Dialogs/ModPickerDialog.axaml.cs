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
using ConditioningControlPanel.Localization;

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
    ///  - <c>ArtUri</c> was a <c>pack://</c> URI into the WPF assembly's Resource items. This head
    ///    ships no such assets, so the card exposes an <see cref="Art"/> image that is null today
    ///    and the accent-framed tile behind it is what draws — the same thing WPF shows before the
    ///    bitmap resolves.
    /// </summary>
    public sealed class ModPickerCard : INotifyPropertyChanged
    {
        public enum CardState { Available, Queued, Downloading, Installing, Installed, Failed }

        public string ModId { get; init; } = "";
        public string? PackId { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";

        /// <summary>
        /// ponytail: needs the mod card art as an avares:// AvaloniaResource, wired when the
        /// Resources/intake/ art moves off the WPF head. Null draws the accent-framed tile.
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
    /// touched by this port. <c>ArtUri</c> is dropped — its pack:// URIs point into the WPF
    /// assembly, so nothing here could resolve one.
    /// </summary>
    public sealed class ModPickerCatalogEntry
    {
        /// <summary>Built-in mod id (BuiltInMods).</summary>
        public string ModId { get; init; } = "";

        /// <summary>Release-content pack id, or null for CCP Default (nothing to download).</summary>
        public string? PackId { get; init; }

        public string NameLocKey { get; init; } = "";
        public string DescriptionLocKey { get; init; } = "";

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
    /// ConditioningControlPanel/Dialogs/ModPackCatalog.cs. The mod ids come from
    /// <c>Models/BuiltInMods.cs</c> and the pack ids from
    /// <c>Services/Content/ReleaseContentService.cs</c>; both are WPF-head constants, so they are
    /// inlined as the literals those constants hold.
    /// </summary>
    internal static class ModPickerCatalog
    {
        private const long Mb = 1024L * 1024L;

        /// <summary>Display order: baseline first, then the five optional mods.</summary>
        public static readonly IReadOnlyList<ModPickerCatalogEntry> All = new[]
        {
            new ModPickerCatalogEntry
            {
                ModId = "builtin-ccp-default",
                PackId = null, // ships in the box — the neutral baseline is what "skip everything" gives you
                NameLocKey = "modpicker_name_ccp_default",
                DescriptionLocKey = "modpicker_desc_ccp_default",
                AccentHex = "#E84393",
                ApproxBytes = 0
            },
            new ModPickerCatalogEntry
            {
                ModId = "builtin-bambisleep",
                PackId = "mod-bambi",
                NameLocKey = "label_bambi_sleep",
                DescriptionLocKey = "modpicker_desc_bambi",
                AccentHex = "#FF69B4",
                ApproxBytes = 77 * Mb
            },
            new ModPickerCatalogEntry
            {
                ModId = "builtin-sissyhypno",
                PackId = "mod-sissy",
                NameLocKey = "label_sissy_hypno",
                DescriptionLocKey = "modpicker_desc_sissy",
                AccentHex = "#9B59B6",
                ApproxBytes = 331 * Mb
            },
            new ModPickerCatalogEntry
            {
                ModId = "builtin-locked",
                PackId = "mod-locked",
                NameLocKey = "modpicker_name_circe",
                DescriptionLocKey = "modpicker_desc_circe",
                AccentHex = "#E81CA8",
                ApproxBytes = 329 * Mb,
                PremiumProgramNote = true // the "kept" program is Premium; the mod itself is free
            },
            new ModPickerCatalogEntry
            {
                ModId = "drone-mode",
                PackId = "mod-drone",
                NameLocKey = "modpicker_name_drone",
                DescriptionLocKey = "modpicker_desc_drone",
                AccentHex = "#00FF41",
                ApproxBytes = 184 * Mb,
                PremiumProgramNote = true, // "firmware_install" is Premium
                NoVoiceNote = true         // drone-mode.ccpmod carries no companion_audio at all
            },
            new ModPickerCatalogEntry
            {
                ModId = "infection-control",
                PackId = "mod-infection",
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

        /// <summary>Pack id for a built-in mod id, or null (CCP Default and every user mod).</summary>
        public static string? PackIdForMod(string? modId) => ForMod(modId)?.PackId;

        /// <summary>
        /// Best known download size. ponytail: needs ReleaseContentService for the manifest's real
        /// sizeBytes, wired when it moves to Core — until then the baked-in approximation, which is
        /// exactly what the WPF original falls back to offline.
        /// </summary>
        public static long SizeBytesFor(ModPickerCatalogEntry entry) => entry?.ApproxBytes ?? 0;

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
    ///  - everything that reaches <c>App.ReleaseContent</c>, <c>App.Mods</c>,
    ///    <c>PendingModActivation</c> or <c>App.Settings</c> is a stub: the pack service, the mod
    ///    service and the settings store all still live in the WPF head. That is the whole download
    ///    flow (<see cref="BtnDownload_Click"/>), the three service event handlers, and
    ///    <see cref="ShowIfNeeded"/>. The card state machine they drive is fully ported and the
    ///    render constructor exercises it.
    ///  - the service event handlers took a WPF-head <c>PackProgressEventArgs</c> and marshalled
    ///    through <c>Application.Current.Dispatcher</c>; both go with the subscription, so
    ///    <c>MarshalToUi</c> and <c>FindCard</c> are dropped rather than kept dead.
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
        private bool _offline;
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

            _txtHint = this.FindControl<TextBlock>("TxtHint")!;
            _txtDownload = this.FindControl<TextBlock>("TxtDownload")!;
            _btnDownload = this.FindControl<Button>("BtnDownload")!;

            BuildCards(preselectModId);
            this.FindControl<ItemsControl>("CardsList")!.ItemsSource = _cards;

            // Handlers live here rather than in markup, per the porting convention.
            this.FindControl<Border>("TitleBar")!.PointerPressed += TitleBar_PointerPressed;
            this.FindControl<Button>("BtnCloseX")!.Click += (_, _) => BtnSkip_Click();
            this.FindControl<Button>("BtnSkip")!.Click += (_, _) => BtnSkip_Click();
            _btnDownload.Click += (_, _) => BtnDownload_Click();

            // ponytail: needs ReleaseContentService.PackProgressChanged / PackInstalled and
            // ModService.ModAvailabilityChanged, wired when they move to Core. Nothing pushes
            // progress into the cards on this head yet.

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

        /// <summary>
        /// ponytail: needs ReleaseContentService (IsFullInstall / IsInstalled), wired when it moves
        /// to Core. False matches the WPF path for a missing service.
        /// </summary>
        private static bool IsPackInstalled(string? packId) => false;

        // ------------------------------------------------------------------ manifest

        private void OnDialogLoaded(object? sender, EventArgs e)
        {
            // ponytail: needs ReleaseContentService, wired when it moves to Core. The WPF original
            // branches here on IsFullInstall / a manifest fetch and falls back to SetOfflineState;
            // this head can only show the baked-in sizes, which is the manifest-already-fetched path.
            RefreshSizes();
            UpdateDownloadButton();
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

        private void BtnDownload_Click()
        {
            if (_finished)
            {
                Close();
                return;
            }
            if (_offline) return;

            // ponytail: needs ReleaseContentService.RequestPackAsync and PendingModActivation,
            // wired when they move to Core. That branch is also where the WPF original's
            // `_preselectModId` and `_downloading` fields are read - both are dropped here rather
            // than kept as dead state, so re-add them with the queue. Without a pack service there
            // is nothing to download, which is exactly the WPF original's `svc == null` branch.
            SetOfflineState();
        }

        private void UpdateDownloadButton()
        {
            if (_closed || _offline) return;

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
            // ponytail: the WPF original unsubscribes from ReleaseContentService and
            // ModService here; nothing is subscribed on this head yet.
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
        /// Shows the picker when this install has never seen it and its mod media has to come off the
        /// network.
        ///
        /// ponytail: needs App.Settings (ModPickerShown / ModPickerOfflineOffers / OfflineMode /
        /// ActiveModId) and ReleaseContentService, wired when they move to Core. The two guards this
        /// method exists for are ported above as pure predicates; only the plumbing that reads and
        /// writes settings is missing, so this returns false — "nothing was shown" — rather than
        /// opening a picker whose choices could not be recorded.
        /// </summary>
        public static bool ShowIfNeeded(Window? owner = null, bool preselectActiveMod = false) => false;
    }
}
