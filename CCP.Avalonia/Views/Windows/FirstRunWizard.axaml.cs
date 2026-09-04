using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The Avalonia twin of the WPF head's <c>ModPackCatalog</c>: THE mapping between built-in mod
    /// ids and release-content pack ids, plus the sizes and copy that the first-run picker and the
    /// Mod Manager both draw from.
    ///
    /// <para>Every id comes from Core (<see cref="BuiltInMods"/>, <see cref="CoreReleaseContent"/>)
    /// rather than being retyped. The placeholder table this replaces carried invented ids
    /// (<c>"bambi_sleep"</c>, <c>"ccp_default"</c>) that match nothing the mod service answers, so
    /// the wizard's pre-selection could never find the active mod.</para>
    ///
    /// <para>Two things the WPF catalogue has that this cannot: the <c>pack://</c> card art (this
    /// head ships no Resources/) and <c>ReleaseContentService.IsFullInstall</c>, which has no Core
    /// seam - a full/dev layout therefore reads here as "pack state unknown", not as installed.</para>
    ///
    /// <para>ponytail: belongs in its own file beside the dialogs, exactly as ModPackCatalog does.
    /// It is here because the layer that wrote it owns no third file; moving it is a rename.</para>
    /// </summary>
    internal static class ModPacks
    {
        private const long Mb = 1024L * 1024L;

        internal sealed record Entry(
            string ModId,
            string? PackId,
            string NameLocKey,
            string DescriptionLocKey,
            string AccentHex,
            long ApproxBytes,
            bool PremiumProgramNote = false,
            bool NoVoiceNote = false);

        /// <summary>Display order: the baseline first, then the five optional mods.</summary>
        internal static readonly Entry[] All =
        {
            // PackId null: CCP Default ships in the box, so "skip everything" still gets a mod.
            new(BuiltInMods.CCPDefaultId, null, "modpicker_name_ccp_default", "modpicker_desc_ccp_default", "#E84393", 0),
            new(BuiltInMods.BambiSleepId, CoreReleaseContent.PackModBambi, "label_bambi_sleep", "modpicker_desc_bambi", "#FF69B4", 77 * Mb),
            new(BuiltInMods.SissyHypnoId, CoreReleaseContent.PackModSissy, "label_sissy_hypno", "modpicker_desc_sissy", "#9B59B6", 331 * Mb),
            // The "kept" program is Premium; the mod itself is free.
            new(BuiltInMods.LockedId, CoreReleaseContent.PackModLocked, "modpicker_name_circe", "modpicker_desc_circe", "#E81CA8", 329 * Mb, PremiumProgramNote: true),
            // "firmware_install" is Premium, and drone-mode.ccpmod carries no companion_audio at all.
            new(BuiltInMods.DronificationId, CoreReleaseContent.PackModDrone, "modpicker_name_drone", "modpicker_desc_drone", "#00FF41", 184 * Mb, PremiumProgramNote: true, NoVoiceNote: true),
            new(BuiltInMods.InfectionControlId, CoreReleaseContent.PackModInfection, "modpicker_name_infection", "modpicker_desc_infection", "#2855F0", 209 * Mb),
        };

        internal static Entry? ForMod(string? modId) =>
            string.IsNullOrEmpty(modId)
                ? null
                : All.FirstOrDefault(e => string.Equals(e.ModId, modId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Pack id for a built-in mod id, or null (CCP Default and every user mod).</summary>
        internal static string? PackIdForMod(string? modId) => ForMod(modId)?.PackId;

        /// <summary>
        /// Best known download size: the manifest's real number once the head has fetched it,
        /// otherwise the baked-in approximation so an offline picker still tells the truth.
        /// </summary>
        internal static long SizeBytesFor(Entry? entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.PackId)) return 0;
            var info = CoreReleaseContent.GetPackInfo(entry.PackId!);
            return info != null && info.SizeBytes > 0 ? info.SizeBytes : entry.ApproxBytes;
        }

        /// <summary>"331 MB" / "1.2 GB". Empty for a zero size.</summary>
        internal static string FormatSize(long bytes)
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

        /// <summary>
        /// The pack's bytes are stamped on disk. An unseeded <c>StampProvider</c> is this head's
        /// version of WPF's <c>svc == null</c>: nothing is KNOWN to be installed, and nothing is
        /// reported missing either - see <see cref="NeedsDownload"/>. Never guess "missing" from
        /// the absence of a service, or every built-in wears a download badge it cannot act on.
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
            var packId = PackIdForMod(modId);
            if (string.IsNullOrEmpty(packId)) return false;
            if (CoreReleaseContent.StampProvider is null) return false;
            return CoreReleaseContent.GetStampFor(packId!) == null;
        }
    }

    /// <summary>
    /// One mod row on the wizard's second step. Deliberately a separate view-model from
    /// <c>ModPickerCard</c> even though the two look alike: the picker's card is a multi-select
    /// download queue (its checkbox hides for anything without a pack), while this one is a
    /// single-choice "which flavour do you want to run", so CCP Default and already-installed mods
    /// must be pickable too. Same discipline though - every visual decision is a plain INPC
    /// property, including the visibility ones, so the DataTemplate needs no value converters.
    ///
    /// <para>PORTED from the WPF code-behind's nested-in-the-same-file class. Deviations, all
    /// forced by Avalonia: <c>Brush</c> -> <c>IBrush</c>, and the five <c>Visibility</c> properties
    /// become <c>bool</c> ones (<c>NoteVisible</c>, ...) because Avalonia's <c>IsVisible</c> binds a
    /// bool directly - see CLAUDE.md. The names keep their shape so the two files still diff.</para>
    /// </summary>
    public sealed class FirstRunModCard : INotifyPropertyChanged
    {
        public string ModId { get; init; } = "";
        public string? PackId { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public IBrush AccentBrush { get; init; } = Brushes.HotPink;
        public string Note { get; init; } = "";

        /// <summary>
        /// ponytail: the WPF card's ArtUri is a compiled <c>&lt;Resource&gt;</c> pack:// path
        /// (Resources/intake/pass_card_*.png). This head ships no Resources/ PNGs and cannot
        /// reference the WPF assembly, so the art frame draws its accent border over the flat
        /// #1A1A2E ground. Bind a real IImage here when the art moves to Core as avares://.
        /// </summary>
        public IImage? Art => null;

        public bool HasPack => !string.IsNullOrEmpty(PackId);

        public bool NoteVisible => !string.IsNullOrEmpty(Note);

        // ---- selection ----

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectMark));
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardBorderThickness));
            }
        }

        public string SelectMark => IsSelected ? "◉" : "○";

        public IBrush CardBorderBrush => IsSelected ? AccentBrush : Brushes.Transparent;

        public Thickness CardBorderThickness => new Thickness(IsSelected ? 2 : 0);

        // ---- download state (mirrors ModPickerCard's, minus the queue affordances) ----

        public enum CardState { Available, Queued, Downloading, Installing, Installed, Failed }

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

        public string InstalledText { get; init; } = "";

        public bool IsInstalled => State == CardState.Installed;

        public bool InstalledVisible => IsInstalled && HasPack;

        public bool SizeVisible => !IsInstalled && !string.IsNullOrEmpty(SizeText);

        public bool ProgressVisible => State == CardState.Downloading || State == CardState.Installing;

        public bool StatusOnlyVisible => State == CardState.Queued || State == CardState.Failed;

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
        }

        public void MarkFailed()
        {
            State = CardState.Failed;
            StatusText = Loc.Get("modpicker_status_failed");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Phase 8's first run: three steps (Welcome, mod pick, doors tour) in place of the up-to-ten
    /// popup gauntlet a fresh install used to walk through.
    ///
    /// <para>PORTED from ConditioningControlPanel/Windows/FirstRunWizard.xaml.cs. What survives is
    /// everything the VIEW owns - the three steps and their copy, the step counter and footer
    /// captions, card selection, the seven door rows, the chrome (drag, Esc, X, Back/Next/Skip) -
    /// plus everything the Core seams can now answer: the <c>Welcomed</c> gate
    /// (<see cref="ShouldRunAndClaim"/> / <see cref="HandBackFirstRun"/>, on
    /// <c>CoreSettings</c> and <c>CoreReleaseContent.AppVersion</c>), the affirmation in the
    /// welcome heading, the active mod the cards pre-select against, the real pack ids and sizes
    /// (see <see cref="ModPacks"/>), and the pack-installed signal.</para>
    ///
    /// <para>What does not survive: <see cref="Run"/> (it drives <c>MainWindow</c>'s folder picker
    /// and <c>TutorialService</c>), <see cref="PrepareModStep"/> (the offline-offer bookkeeping is
    /// <c>ModPickerDialog</c>'s, and reimplementing it is how a modular install loses its mod media)
    /// and <see cref="CommitModChoice"/> (<c>PendingModActivation</c> and
    /// <c>ModManagerService.ActivateMod</c>). Each is a stub naming what it needs.</para>
    ///
    /// <para>The hardening lessons the original documents are preserved as comments where the code
    /// they guard still exists, and dropped with the code where it does not - a comment about
    /// spending a one-shot flag on a method that no longer spends one is worse than no comment.</para>
    /// </summary>
    public partial class FirstRunWizard : Window
    {
        private const int StepCount = 3;

        private readonly ObservableCollection<FirstRunModCard> _cards = new();

        private int _step = 1;
        private FirstRunModCard? _selected;

        // Named controls. FindControl rather than the generated fields, matching the other ports.
        private readonly TextBlock _txtWizardTitle;
        private readonly TextBlock _txtStepCounter;
        private readonly Image _imgWelcomeLogo;
        private readonly TextBlock _txtAppTitle;
        private readonly TextBlock _txtWelcomeHeading;
        private readonly TextBlock _txtWelcomeBody;
        private readonly TextBlock _txtTipsTitle;
        private readonly TextBlock _txtTipHelp;
        private readonly TextBlock _txtTipHover;
        private readonly TextBlock _txtTipAssets;
        private readonly TextBlock _txtPerfTitle;
        private readonly TextBlock _txtPerfBody;
        private readonly TextBlock _txtModHeading;
        private readonly TextBlock _txtModSub;
        private readonly TextBlock _txtModHint;
        private readonly TextBlock _txtTourHeading;
        private readonly TextBlock _txtTourOutro;
        private readonly TextBlock _txtPickFolder;
        private readonly TextBlock _txtBack;
        private readonly TextBlock _txtSkip;
        private readonly TextBlock _txtNext;
        private readonly Grid _step1;
        private readonly Grid _step2;
        private readonly Grid _step3;
        private readonly Button _btnBack;
        private readonly Button _btnPickFolder;
        private readonly ItemsControl _modCardsList;
        private readonly StackPanel _doorsHost;

        /// <summary>Set by "Take the tour"; read by the caller after the modal returns.</summary>
        public bool StartTourRequested { get; private set; }

        /// <summary>Set by the Welcome step's folder button; the picker opens after this window closes.</summary>
        public bool PickAssetsFolderRequested { get; private set; }

        /// <summary>
        /// The only constructor. WPF's took the owning MainWindow so the mod commit could call
        /// back into it; nothing here can, so it takes nothing - which is also what
        /// <c>--render-all</c> needs to discover the view. Internal for the same reason
        /// TextEditorDialog's render constructor is: no production caller ships the placeholder.
        /// </summary>
        internal FirstRunWizard()
        {
            AvaloniaXamlLoader.Load(this);

            _txtWizardTitle = this.FindControl<TextBlock>("TxtWizardTitle")!;
            _txtStepCounter = this.FindControl<TextBlock>("TxtStepCounter")!;
            _imgWelcomeLogo = this.FindControl<Image>("ImgWelcomeLogo")!;
            _txtAppTitle = this.FindControl<TextBlock>("TxtAppTitle")!;
            _txtWelcomeHeading = this.FindControl<TextBlock>("TxtWelcomeHeading")!;
            _txtWelcomeBody = this.FindControl<TextBlock>("TxtWelcomeBody")!;
            _txtTipsTitle = this.FindControl<TextBlock>("TxtTipsTitle")!;
            _txtTipHelp = this.FindControl<TextBlock>("TxtTipHelp")!;
            _txtTipHover = this.FindControl<TextBlock>("TxtTipHover")!;
            _txtTipAssets = this.FindControl<TextBlock>("TxtTipAssets")!;
            _txtPerfTitle = this.FindControl<TextBlock>("TxtPerfTitle")!;
            _txtPerfBody = this.FindControl<TextBlock>("TxtPerfBody")!;
            _txtModHeading = this.FindControl<TextBlock>("TxtModHeading")!;
            _txtModSub = this.FindControl<TextBlock>("TxtModSub")!;
            _txtModHint = this.FindControl<TextBlock>("TxtModHint")!;
            _txtTourHeading = this.FindControl<TextBlock>("TxtTourHeading")!;
            _txtTourOutro = this.FindControl<TextBlock>("TxtTourOutro")!;
            _txtPickFolder = this.FindControl<TextBlock>("TxtPickFolder")!;
            _txtBack = this.FindControl<TextBlock>("TxtBack")!;
            _txtSkip = this.FindControl<TextBlock>("TxtSkip")!;
            _txtNext = this.FindControl<TextBlock>("TxtNext")!;
            _step1 = this.FindControl<Grid>("Step1")!;
            _step2 = this.FindControl<Grid>("Step2")!;
            _step3 = this.FindControl<Grid>("Step3")!;
            _btnBack = this.FindControl<Button>("BtnBack")!;
            _btnPickFolder = this.FindControl<Button>("BtnPickFolder")!;
            _modCardsList = this.FindControl<ItemsControl>("ModCardsList")!;
            _doorsHost = this.FindControl<StackPanel>("DoorsHost")!;

            ApplyStaticText();
            BuildModCards();
            BuildDoorRows();
            _modCardsList.ItemsSource = _cards;

            // A pack finishing while this window is open flips its card to Installed. Raised on the
            // head's download thread, hence the hop. CoreReleaseContent.PackInstalled is a STATIC
            // event, so the Closed handler below must detach or the window outlives its own close.
            //
            // ponytail: still needs App.ReleaseContent's PackProgressChanged (the per-percent
            // MarkProgress) and App.Mods' ModAvailabilityChanged (extracted, not merely downloaded);
            // neither has a Core seam yet, so a download shows nothing until it lands.
            CoreReleaseContent.PackInstalled += OnPackInstalled;

            // Handlers live here rather than in markup, per the porting convention.
            this.FindControl<Border>("TitleBar")!.PointerPressed += TitleBar_PointerPressed;
            this.FindControl<Button>("BtnCloseX")!.Click += (_, _) => CloseSafely();
            _btnPickFolder.Click += BtnPickFolder_Click;
            _btnBack.Click += (_, _) => ShowStep(_step - 1);
            this.FindControl<Button>("BtnSkip")!.Click += (_, _) => CloseSafely();
            this.FindControl<Button>("BtnNext")!.Click += BtnNext_Click;

            // One handler on the list instead of one inside the DataTemplate: template content has
            // no name scope to FindControl through, and PointerReleased bubbles the same way WPF's
            // MouseLeftButtonUp did.
            _modCardsList.AddHandler(InputElement.PointerReleasedEvent, ModCard_PointerReleased);

            // WPF's PreviewKeyDown. Tunnel, so Esc closes the window before any focused child eats it.
            AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);

            Closed += OnWizardClosed;
            ShowStep(1);
        }

        // ------------------------------------------------------------------ entry points

        /// <summary>
        /// First launch? Reads <c>Welcomed</c> and latches it (plus the assets-prompt one-shot),
        /// exactly where <c>WelcomeDialog.ShowIfNeeded</c> used to. On this head that dialog still
        /// carries its own copy of the latch and has no caller: whoever wires startup calls ONE of
        /// the two, never both, or the second sees an already-claimed flag and shows nothing.
        ///
        /// <para>Three flags are spent here rather than when the window opens, on purpose:</para>
        /// <list type="bullet">
        /// <item><c>Welcomed</c> - a crash inside the wizard must not make it an every-launch
        /// screen (the ModPickerShown lesson).</item>
        /// <item><c>FirstRunAssetsPromptShown</c> - the "choose a content folder" prompt fires
        /// before this wizard can open, so spending it in the gate is the only way a first-run user
        /// never gets that modal on top of the wizard; the Welcome step carries the affordance.</item>
        /// <item><c>LastSeenVersion</c> - the first-run branch is the ONE path that never reaches
        /// the What's New check, which is where every other launch stamps it. Left blank, this
        /// install's first upgrade reads as a fresh install and has its first ever patch notes
        /// stamped away unshown. Correct on both wizard outcomes, so
        /// <see cref="HandBackFirstRun"/> deliberately does not undo it.</item>
        /// </list>
        ///
        /// <para>WPF returned false when <c>App.Settings</c> was null. The Core seam's
        /// <c>Current</c> is never null - with no head attached it is a throwaway default - so
        /// <see cref="CoreSettings.Service"/> is what stands in for that check. Without it the
        /// headless render and the smoke runner would claim a first run against an object nobody
        /// ever saves.</para>
        /// </summary>
        public static bool ShouldRunAndClaim()
        {
            try
            {
                // WPF's `App.Settings?.Current` null check. The seam's Current is never null - with
                // no head attached it is a throwaway default nobody saves - so the service itself is
                // what "is there settings to claim against" has to ask.
                if (CoreSettings.Service == null) return false;

                var settings = CoreSettings.Current;
                if (settings.Welcomed) return false;

                settings.Welcomed = true;
                settings.FirstRunAssetsPromptShown = true;
                settings.LastSeenVersion = CoreReleaseContent.AppVersion;
                CoreSettings.Save();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Gate check failed - skipping the first-run wizard");
                return false;
            }
        }

        /// <summary>
        /// Undoes <see cref="ShouldRunAndClaim"/> when the caller decided NOT to open the wizard
        /// after all (an update dialog outlasted its wait, the window never loaded). Without this
        /// the flags are spent on a screen nobody ever saw and the install silently never gets a
        /// first run. A crash INSIDE the wizard is deliberately NOT covered - that is what spending
        /// up front buys. <c>LastSeenVersion</c> is not handed back: it IS a fresh install of this
        /// version either way, and the next launch re-stamps whatever version it is.
        /// </summary>
        public static void HandBackFirstRun(string reason)
        {
            try
            {
                if (CoreSettings.Service == null) return;

                var settings = CoreSettings.Current;
                settings.Welcomed = false;
                settings.FirstRunAssetsPromptShown = false;
                CoreSettings.Save();
                Log.Information("[FirstRun] Not shown ({Reason}) - handing the first run back to the next launch", reason);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] Could not hand the first run back");
            }
        }

        /// <summary>
        /// ponytail: needs MainWindow (BtnPickAssetsFolder_Click, StartTutorial) and
        /// TutorialService, wired when they move to Core. WPF opens the wizard modally and then, at
        /// Dispatcher Normal priority (never Loaded - Loaded-priority work is starved in this app),
        /// runs the content-folder picker first and the SHORT WALK tutorial second.
        /// </summary>
        public static void Run(object owner) { }

        // ------------------------------------------------------------------ copy

        /// <summary>
        /// Localized string with an English fallback. New <c>fr8_</c> keys land in the language
        /// files on their own schedule; until then (and for any language that has not caught up)
        /// this renders the English draft rather than the raw key. Core's
        /// <c>LocalizationManager.Get</c> returns the key itself on a miss, which is what the
        /// equality check below detects.
        /// </summary>
        private static string Str(string key, string english)
        {
            try
            {
                var value = Loc.Get(key);
                return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal)
                    ? english
                    : value;
            }
            catch { return english; }
        }

        private static string StrF(string key, string english, params object[] args)
        {
            var template = Str(key, english);
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }

        /// <summary>
        /// The one art load on this screen that CAN be mod-aware: logo.png is a real
        /// resource-relative path, so a mod shipping its own brand mark shows it here.
        /// <para>
        /// Which wordmark is WPF's branch verbatim: logo2.png is the neutral "Conditioning Control
        /// Panel" mark used by CCP Default and Sissy, logo.png the Bambi-branded one. A fresh
        /// install IS CCP Default, so the wizard must not hardcode logo.png.
        /// </para>
        /// <para>
        /// WPF's <c>ModResourceResolver.ResolveUri</c> is <see cref="Helpers.ModArt.TryLoad"/> here
        /// - the mod override through <see cref="CoreModArt"/>, then this head's own avares:// copy,
        /// which Assets/logo.png and logo2.png are linked into. Null means neither exists, and the
        /// frame is hidden rather than left as an empty 46px gap. WPF's DecodePixelWidth=92 has no
        /// TryLoad equivalent; the PNGs are small and the Image scales, so full-res is the cost.
        /// </para>
        /// </summary>
        private void RefreshWelcomeLogo()
        {
            try
            {
                var useNeutralLogo = CoreMods.IsCCPDefault || CoreSettings.Current.IsSissyMode;
                var logo = Helpers.ModArt.TryLoad(useNeutralLogo ? "logo2.png" : "logo.png");
                _imgWelcomeLogo.Source = logo;
                _imgWelcomeLogo.IsVisible = logo != null;
            }
            catch (Exception ex) { Log.Debug("[FirstRun] logo resolve failed: {E}", ex.Message); }
        }

        private void ApplyStaticText()
        {
            Title = Str("fr8_wizard_title", "Getting started");
            _txtWizardTitle.Text = Title;

            // --- step 1 ---
            RefreshWelcomeLogo();

            _txtAppTitle.Text = Loc.Get("app_title");
            _txtWelcomeHeading.Text = StrF("fr8_welcome_heading", "Welcome, {0}.", CoreMods.Affirmation);
            _txtWelcomeBody.Text = Str("fr8_welcome_body",
                "Conditioning Control Panel layers the effects you choose - flashes, videos, subliminals, " +
                "screen overlays and a companion who reacts to all of it - over whatever you are already doing. " +
                "Nothing runs until you press START, and every camera, microphone or screen-reading feature " +
                "asks for your consent separately, the first time you use it.");

            _txtTipsTitle.Text = Loc.Get("label_tips");
            _txtTipHelp.Text = Str("fr8_welcome_tip_help",
                "Click the ? button in the title bar any time for the full tour and per-feature guides.");
            _txtTipHover.Text = Str("fr8_welcome_tip_hover", "Hover over any setting to see what it does.");
            _txtTipAssets.Text = Str("fr8_welcome_tip_assets",
                "Add your own images and videos from the Library door, or point the app at any folder you like.");

            _txtPerfTitle.Text = Loc.Get("label_performance_warning");
            _txtPerfBody.Text = Str("fr8_welcome_perf_warning",
                "Running many features at once, especially at high frequencies, is heavy on older machines. " +
                "Turn some off or lower their rates in Settings if things get sluggish.");

            _txtPickFolder.Text = Str("fr8_welcome_pick_folder", "Choose a content folder");

            // --- step 2 ---
            _txtModHeading.Text = Str("fr8_modpick_heading", "Pick your flavour");
            _txtModSub.Text = Str("fr8_modpick_sub",
                "A mod re-skins the whole app: her name and voice, the art, the phrases, the programs. " +
                "Pick the one you want to start with - you can switch any time from the title bar.");
            _txtModHint.Text = Str("fr8_modpick_skip_hint",
                "Skipping keeps the neutral CCP Default. Downloads carry on in the background, so you can " +
                "close this window whenever you like.");

            // --- step 3 ---
            _txtTourHeading.Text = Str("fr8_tour_heading", "Seven doors");
            _txtTourOutro.Text = Str("fr8_tour_outro",
                "The rail on the left is always there. Take the tour for a ninety-second walk through the " +
                "essentials, or explore on your own - the ? button replays it, and the full door-by-door " +
                "tour is in there too.");

            // --- chrome ---
            _txtBack.Text = Str("fr8_wizard_back", "Back");
        }

        // ------------------------------------------------------------------ steps

        private void ShowStep(int step)
        {
            _step = Math.Max(1, Math.Min(StepCount, step));

            _step1.IsVisible = _step == 1;
            _step2.IsVisible = _step == 2;
            _step3.IsVisible = _step == 3;

            _txtStepCounter.Text = StrF("fr8_wizard_step_of", "Step {0} of {1}", _step, StepCount);
            _btnBack.IsVisible = _step != 1;

            if (_step == 3)
            {
                _txtSkip.Text = Str("fr8_tour_explore", "Explore on my own");
                _txtNext.Text = Str("fr8_tour_take", "Take the tour");
            }
            else
            {
                _txtSkip.Text = Str("fr8_wizard_skip", "Skip setup");
                _txtNext.Text = Str("fr8_wizard_next", "Next");
            }

            // WPF re-called RefreshWelcomeLogo from ShowStep(1), so Back-navigation after a mod
            // pick repaints instead of showing the previous mod's mark.
            if (_step == 1) RefreshWelcomeLogo();
            if (_step == 2) PrepareModStep();

            FadeInCurrentStep();
        }

        /// <summary>
        /// The only motion on this screen, and it is gated, exactly as WPF has it: at MotionLevel
        /// Off the page simply swaps. No loop, so there is nothing for the motion kill-switch to
        /// stop.
        ///
        /// <para><c>MotionFx</c> itself is WPF Storyboard code and is NOT coming to Core, but its
        /// DECISION is <c>CoreSettings.Current.MotionLevel</c>, which this head already reads
        /// through <see cref="AmbientFxCanvas.Env"/> - so the gate is the real one.
        /// <c>MotionFx.AllowTransitions</c> is <c>Level != Off</c>.</para>
        ///
        /// <para>The animation ends at 1 and stays there (<c>FillMode.Forward</c>), and a throw
        /// lands on the swap rather than leaving a step parked at Opacity 0 - the wizard is the
        /// first thing a new install sees, so an invisible page here is unrecoverable.</para>
        /// </summary>
        private void FadeInCurrentStep()
        {
            var host = _step == 1 ? (Control)_step1 : _step == 2 ? _step2 : _step3;

            if (AmbientFxCanvas.Env.Level == MotionLevel.Off)
            {
                host.Opacity = 1;
                return;
            }

            try
            {
                host.Opacity = 0;
                _ = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(140),
                    Easing = new QuadraticEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1d) } },
                    },
                }.RunAsync(host);
            }
            catch (Exception ex)
            {
                Log.Debug("[FirstRun] Step fade failed, swapping instead: {Error}", ex.Message);
                host.Opacity = 1;
            }
        }

        // ------------------------------------------------------------------ step 2: mod pick

        private void BuildModCards()
        {
            var installedBadge = Loc.Get("modpicker_installed_badge");
            var activeModId = CoreMods.ActiveModId;

            foreach (var entry in ModPacks.All)
            {
                IBrush accent;
                try { accent = new SolidColorBrush(Color.Parse(entry.AccentHex)); }
                catch { accent = Brushes.HotPink; }

                var note = entry.PremiumProgramNote ? Loc.Get("modpicker_note_premium_programs") : "";
                if (entry.NoVoiceNote)
                {
                    var voice = Loc.Get("modpicker_note_no_voice");
                    note = string.IsNullOrEmpty(note) ? voice : note + "  " + voice;
                }

                var card = new FirstRunModCard
                {
                    ModId = entry.ModId,
                    PackId = entry.PackId,
                    Name = Loc.Get(entry.NameLocKey),
                    Description = Loc.Get(entry.DescriptionLocKey),
                    AccentBrush = accent,
                    Note = note,
                    InstalledText = installedBadge
                };

                if (!card.HasPack)
                {
                    // CCP Default ships in the box - always a legitimate choice, never a download.
                    card.State = FirstRunModCard.CardState.Installed;
                }
                else
                {
                    card.SizeText = ModPacks.FormatSize(ModPacks.SizeBytesFor(entry));
                    if (ModPacks.IsInstalled(entry.PackId)) card.State = FirstRunModCard.CardState.Installed;
                }

                _cards.Add(card);
            }

            // Pre-select what this install is already running (CCP Default on a fresh box), so the
            // screen reads as "here is what you have, here is what you can have" and pressing Next
            // without touching anything is a no-op rather than an accidental switch.
            Select(_cards.FirstOrDefault(c => string.Equals(c.ModId, activeModId, StringComparison.OrdinalIgnoreCase))
                   ?? _cards.FirstOrDefault());
        }

        private void Select(FirstRunModCard? card)
        {
            _selected = card;
            foreach (var c in _cards) c.IsSelected = ReferenceEquals(c, card);
        }

        private void ModCard_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            if ((e.Source as Control)?.DataContext is FirstRunModCard card) Select(card);
        }

        /// <summary>
        /// ponytail: settings and release content are NOT the blockers - CoreSettings.Current and
        /// CoreReleaseContent both answer today. What is missing is the WPF ModPickerDialog's
        /// offline policy (ShouldDeferForOffline / ShouldReArmAfterOfflineShowing /
        /// MaxOfflineOffers); this head's ported Dialogs.ModPickerDialog does not carry it, and it
        /// is the only part that matters. In WPF this is where the picker's one-shot offer is spent - at the
        /// moment the step is first shown, not when a download is queued - where a full/dev layout
        /// marks every card installed, and where offline is handled as a first-class state that
        /// hands the offer BACK rather than burning it. A reimplemented offline guard is how a
        /// modular install loses its mod media permanently, so port the real one; do not restate it.
        /// </summary>
        private void PrepareModStep() { }

        /// <summary>
        /// ponytail: needs PendingModActivation and MainWindow.ActivateChosenMod, both still WPF
        /// head-side. CoreMods is not the gap - it reads the active mod but cannot SWITCH one, and
        /// CoreModsHooks.SwitchCompanion is unseeded on this head, so there is no path to commit
        /// through. WPF hands the chosen mod to the EXACT existing switching path:
        /// content on disk goes straight through ActivateChosenMod, content that must be fetched
        /// records the intent and starts the download, so the switch happens the moment the pack
        /// lands - even after this window is gone. Choosing a mod MEANS choosing to run it.
        /// <see cref="_selected"/> is what it commits.
        /// </summary>
        private void CommitModChoice() { }

        // ------------------------------------------------------------------ step 3: the doors

        /// <summary>
        /// The seven doors, in rail order. Mirrors <c>MainWindow.NavDoorMap</c> (which is private,
        /// and is navigation truth - this list is only the wizard's description of it). Glyphs match
        /// the rail headers; the labels reuse the existing <c>nav_door_*</c> keys, so only the
        /// one-line blurbs are new copy.
        /// </summary>
        private static readonly (string Glyph, string LabelKey, string BlurbKey, string Blurb)[] Doors =
        {
            ("\U0001F3E0", "nav_door_home", "fr8_door_home_blurb",
                "Your dashboard: the START hero, the feature mosaic, the browser card, today's program and the marquee."),
            ("\U0001F39B️", "nav_door_studio", "fr8_door_studio_blurb",
                "Where every effect is tuned: the rack, presets and sessions, the scheduler, the intensity ramp and your toys."),
            ("\U0001F916", "nav_door_companion", "fr8_door_companion_blurb",
                "Her room: personality, Takeover, She's Listening, Awareness, and every AI permission in one grid."),
            ("\U0001F3AE", "nav_door_play", "fr8_door_play_blurb",
                "The card wall: DTRH, Goon, Gaze, Bureau, Deeper, Graded Intake, Lockdown, Remote Control and the Showcase shelf."),
            ("\U0001F464", "nav_door_you", "fr8_door_you_blurb",
                "Your progress: Trainer Card, quests, achievements, the Skill Tree, training programs and the leaderboard."),
            ("\U0001F4DA", "nav_door_library", "fr8_door_library_blurb",
                "Everything you own: assets and content packs, mods, the catalogue, your phrase pools and the media log."),
            ("⚙️", "nav_door_settings", "fr8_door_settings_blurb",
                "The system side: language, audio, devices, performance, notifications, account, data and updates."),
        };

        private void BuildDoorRows()
        {
            foreach (var door in Doors)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var glyph = new TextBlock
                {
                    Text = door.Glyph,
                    FontSize = 20,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 0, 14, 0)
                };
                row.Children.Add(glyph);

                var text = new StackPanel();
                text.Children.Add(new TextBlock
                {
                    Text = Loc.Get(door.LabelKey),
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeight.Bold
                });
                text.Children.Add(new TextBlock
                {
                    Text = Str(door.BlurbKey, door.Blurb),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);

                var shell = new Border
                {
                    // Avalonia's TryFindResource is an extension on the control, not on Application:
                    // the lookup has to start somewhere in the tree to see this window's resources.
                    Background = this.TryFindResource("PanelBgBrush", out var bg) ? bg as IBrush : null,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = row
                };
                _doorsHost.Children.Add(shell);
            }
        }

        // ------------------------------------------------------------------ pack events

        private void OnPackInstalled(object? sender, string packId) =>
            Dispatcher.UIThread.Post(() => FindCard(packId)?.MarkInstalled());

        private FirstRunModCard? FindCard(string? packId) =>
            string.IsNullOrEmpty(packId)
                ? null
                : _cards.FirstOrDefault(c => string.Equals(c.PackId, packId, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ chrome

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            try { BeginMoveDrag(e); } catch { /* as WPF's DragMove, throws if the button already went up */ }
        }

        private void BtnNext_Click(object? sender, RoutedEventArgs e)
        {
            if (_step == 2) CommitModChoice();

            if (_step >= StepCount)
            {
                // Last step's primary action is the doors tour itself.
                StartTourRequested = true;
                CloseSafely();
                return;
            }

            ShowStep(_step + 1);
        }

        private void BtnPickFolder_Click(object? sender, RoutedEventArgs e)
        {
            // Deferred rather than opened here: in WPF the folder browser is a modal owned by
            // MainWindow, and stacking it under this modal is exactly the modal-on-modal the wizard
            // exists to remove. The caller opens it the instant this window is gone.
            PickAssetsFolderRequested = true;
            _btnPickFolder.IsEnabled = false;
            _txtPickFolder.Text = Str("fr8_welcome_pick_folder_queued",
                "We'll ask for your content folder right after this");
        }

        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseSafely();
        }

        private void CloseSafely()
        {
            try { Close(); } catch { }
        }

        private bool _closed;

        private void OnWizardClosed(object? sender, EventArgs e)
        {
            // Detached first and unconditionally: a static event holding a closed window is a leak
            // whatever the rest of this handler decides to do.
            CoreReleaseContent.PackInstalled -= OnPackInstalled;

            if (_closed) return;
            _closed = true;

            // A choice made but never "Next"-ed (Esc, the X, Explore on my own) still counts - the
            // user ticked a mod, and honouring it is what the picker's own contract promises.
            CommitModChoice();
        }
    }
}
