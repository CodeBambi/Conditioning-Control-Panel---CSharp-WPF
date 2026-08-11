using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One mod row on the wizard's second step. Deliberately a separate view-model from
    /// <see cref="ModPickerCard"/> even though the two look alike: the picker's card is a
    /// multi-select download queue (its checkbox hides for anything without a pack), while this one
    /// is a single-choice "which flavour do you want to run", so CCP Default and already-installed
    /// mods must be pickable too. Same discipline though - every visual decision is a plain INPC
    /// property, including the Visibility ones, so the DataTemplate needs no value converters.
    /// </summary>
    public sealed class FirstRunModCard : INotifyPropertyChanged
    {
        public string ModId { get; init; } = "";
        public string? PackId { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string ArtUri { get; init; } = "";
        public Brush AccentBrush { get; init; } = Brushes.HotPink;
        public string Note { get; init; } = "";

        public bool HasPack => !string.IsNullOrEmpty(PackId);

        public Visibility NoteVisibility =>
            string.IsNullOrEmpty(Note) ? Visibility.Collapsed : Visibility.Visible;

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

        public Brush CardBorderBrush => IsSelected ? AccentBrush : Brushes.Transparent;

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
                OnPropertyChanged(nameof(ProgressVisibility));
                OnPropertyChanged(nameof(StatusOnlyVisibility));
                OnPropertyChanged(nameof(InstalledVisibility));
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

        public string InstalledText { get; init; } = "";

        public bool IsInstalled => State == CardState.Installed;

        public Visibility InstalledVisibility =>
            (IsInstalled && HasPack) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SizeVisibility =>
            (!IsInstalled && !string.IsNullOrEmpty(SizeText)) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ProgressVisibility =>
            (State == CardState.Downloading || State == CardState.Installing) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility StatusOnlyVisibility =>
            (State == CardState.Queued || State == CardState.Failed) ? Visibility.Visible : Visibility.Collapsed;

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
    /// <para>What it replaces, and nothing else: <c>WelcomeDialog</c>, the first-launch
    /// <c>ModPickerDialog.ShowIfNeeded</c> call, the seven-step spotlight tour that fired straight
    /// after it, and the hardcoded-English "choose a content folder" MessageBox. Every PRESERVED
    /// surface is untouched - the consent dialogs (webcam / mic / awareness / explicit content) are
    /// still lazily user-initiated and are never folded in here, the staged 3s/5s/7s/14s startup
    /// ladder keeps its slots, the lockdown intro keeps its secret-exit line, and
    /// <c>NewYearNoteReactionSeen</c> is never touched.</para>
    ///
    /// <para>Only a genuinely fresh install ever sees this: the gate is
    /// <see cref="ShouldRunAndClaim"/>, which reads the same <c>Welcomed</c> flag the old
    /// WelcomeDialog did, at the same instant in MainWindow's constructor. An upgrade install
    /// arrives with <c>Welcomed = true</c> and falls into the untouched What's New / season recap /
    /// upgrader-picker branch.</para>
    ///
    /// <para>Hardening lessons carried over from the popups it replaces: one-shot flags are spent
    /// BEFORE the screen opens (a crash inside must never turn it into an every-launch popup),
    /// deferred work runs at <see cref="DispatcherPriority.Normal"/> and never Loaded (Loaded is
    /// starved in this app), closing mid-download does NOT cancel the download, and offline is a
    /// first-class state that hands the mod offer back instead of burning it.</para>
    /// </summary>
    public partial class FirstRunWizard : Window
    {
        private const int StepCount = 3;

        private readonly MainWindow? _owner;
        private readonly ObservableCollection<FirstRunModCard> _cards = new();

        private int _step = 1;
        private FirstRunModCard? _selected;

        /// <summary>The mod id already handed to the activation path, so Back/Next can't double-fire it.</summary>
        private string? _committedModId;

        private bool _modStepPrepared;
        private bool _modStepOffline;
        private bool _offlineOfferCounted;
        private bool _closed;

        /// <summary>Set by "Take the tour"; read by <see cref="Run"/> after the modal returns.</summary>
        public bool StartTourRequested { get; private set; }

        /// <summary>Set by the Welcome step's folder button; the picker opens after this window closes.</summary>
        public bool PickAssetsFolderRequested { get; private set; }

        private FirstRunWizard(MainWindow? owner)
        {
            _owner = owner;
            InitializeComponent();

            ApplyStaticText();
            BuildModCards();
            BuildDoorRows();
            ModCardsList.ItemsSource = _cards;

            try
            {
                var svc = App.ReleaseContent;
                if (svc != null)
                {
                    svc.PackProgressChanged += OnPackProgressChanged;
                    svc.PackInstalled += OnPackInstalled;
                }
                // Fires once the pack's .ccpmod is extracted and the resolver cache dropped - the
                // moment the mod is genuinely usable, one step past "bytes on disk".
                if (App.Mods != null) App.Mods.ModAvailabilityChanged += OnModAvailabilityChanged;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Could not subscribe to release-content events");
            }

            Closed += OnWizardClosed;
            ShowStep(1);
        }

        // ------------------------------------------------------------------ entry points

        /// <summary>
        /// First launch? Reads <c>Welcomed</c> and latches it (plus the assets-prompt one-shot)
        /// exactly where <c>WelcomeDialog.ShowIfNeeded</c> used to, so the surrounding
        /// first-launch/else branch in MainWindow's constructor keeps its original shape and the
        /// What's New path for upgraders is bit-for-bit unchanged.
        ///
        /// <para>Two flags are spent here rather than when the window opens, on purpose:</para>
        /// <list type="bullet">
        /// <item><c>Welcomed</c> - a crash inside the wizard must not make it an every-launch
        /// screen (the ModPickerShown lesson).</item>
        /// <item><c>FirstRunAssetsPromptShown</c> - the hardcoded-English "choose a content folder"
        /// MessageBox fires ~500ms after MainWindow.Loaded, which is BEFORE this wizard can open.
        /// Spending it in the constructor is the only way to guarantee a first-run user never gets
        /// that modal on top of the wizard; the Welcome step carries the folder affordance
        /// instead. The property keeps its persistence either way.</item>
        /// </list>
        /// </summary>
        public static bool ShouldRunAndClaim()
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return false;
                if (settings.Welcomed) return false;

                settings.Welcomed = true;
                settings.FirstRunAssetsPromptShown = true;
                App.Settings?.Save();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Gate check failed - skipping the first-run wizard");
                return false;
            }
        }

        /// <summary>
        /// Undoes <see cref="ShouldRunAndClaim"/> when the caller decided NOT to open the wizard
        /// after all (the update dialog outlasted its 30s wait, the window never loaded). Without
        /// this the flags would be spent on a screen nobody ever saw and the install would silently
        /// never get a first run at all. A crash inside the wizard is deliberately NOT covered -
        /// that is what spending up front buys.
        /// </summary>
        public static void HandBackFirstRun(string reason)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;

                settings.Welcomed = false;
                settings.FirstRunAssetsPromptShown = false;
                App.Settings?.Save();
                App.Logger?.Information("[FirstRun] Not shown ({Reason}) - handing the first run back to the next launch", reason);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Could not hand the first run back");
            }
        }

        /// <summary>
        /// Opens the wizard modally and performs whatever the user asked for on the way out: the
        /// content-folder picker first (it is modal too, so it must not race the tour), then the
        /// existing TutorialService ladder. Never throws - a first-run screen must never be the
        /// reason a fresh install fails to start.
        /// </summary>
        public static void Run(MainWindow owner)
        {
            FirstRunWizard? wizard = null;
            try
            {
                MainWindow.IsStartupDialogShowing = true;
                wizard = new FirstRunWizard(owner) { Owner = owner };
                wizard.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] The first-run wizard failed to show");
            }
            finally
            {
                MainWindow.IsStartupDialogShowing = false;
            }

            if (wizard == null) return;

            bool pickFolder = wizard.PickAssetsFolderRequested;
            bool startTour = wizard.StartTourRequested;
            if (!pickFolder && !startTour) return;

            // Normal, never Loaded: Loaded-priority work is starved in this app (compositor host +
            // avatar animations keep the dispatcher busy) and silently never runs. One action for
            // both so the folder dialog is finished before the spotlight overlay appears.
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (pickFolder)
                {
                    try { owner.BtnPickAssetsFolder_Click(owner, new RoutedEventArgs()); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "[FirstRun] Assets folder picker failed"); }
                }

                if (startTour)
                {
                    try { owner.StartTutorial(); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "[FirstRun] Could not start the doors tour"); }
                }
            }), DispatcherPriority.Normal);
        }

        // ------------------------------------------------------------------ copy

        /// <summary>
        /// Localized string with an English fallback. New <c>fr8_</c> keys land in the language
        /// files on their own schedule; until then (and for any language that has not caught up)
        /// this renders the English draft rather than the raw key.
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
        /// resource-relative path, so a mod that ships its own brand mark shows it here.
        /// (The mod cards on step 2 cannot use the resolver - it resolves against the ACTIVE
        /// mod only, and that step is showing five candidates.)
        /// <para>
        /// Which wordmark is the same branch <c>MainWindow.LoadLogo</c> takes: logo.png is
        /// the Bambi-branded mark, logo2.png the neutral "Conditioning Control Panel" one used by
        /// CCP Default and Sissy - and a fresh install IS CCP Default, so the wizard must not
        /// hardcode logo.png. Re-called from ShowStep(1) so Back-navigation after a mod pick
        /// repaints instead of showing the previous mod's mark.
        /// </para>
        /// </summary>
        private void RefreshWelcomeLogo()
        {
            try
            {
                var useNeutralLogo = App.Mods?.IsCCPDefault == true
                                     || App.Settings?.Current?.IsSissyMode == true;
                var logoFile = useNeutralLogo ? "logo2.png" : "logo.png";

                // ResolveUri + DecodePixelWidth instead of ResolveImage: the frame is 46 DIP, so a
                // 2x decode is plenty and the full-res PNG never reaches memory.
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(ModResourceResolver.ResolveUri(logoFile), UriKind.Absolute);
                bitmap.DecodePixelWidth = 92;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                ImgWelcomeLogo.Source = bitmap;
            }
            catch (Exception ex) { App.Logger?.Debug("[FirstRun] logo resolve failed: {E}", ex.Message); }
        }

        private void ApplyStaticText()
        {
            Title = Str("fr8_wizard_title", "Getting started");
            TxtWizardTitle.Text = Title;

            // --- step 1 ---
            RefreshWelcomeLogo();

            TxtAppTitle.Text = Loc.Get("app_title");
            TxtWelcomeHeading.Text = StrF("fr8_welcome_heading", "Welcome, {0}.",
                App.Mods?.GetAffirmation() ?? "Subject");
            TxtWelcomeBody.Text = Str("fr8_welcome_body",
                "Conditioning Control Panel layers the effects you choose - flashes, videos, subliminals, " +
                "screen overlays and a companion who reacts to all of it - over whatever you are already doing. " +
                "Nothing runs until you press START, and every camera, microphone or screen-reading feature " +
                "asks for your consent separately, the first time you use it.");

            TxtTipsTitle.Text = Loc.Get("label_tips");
            TxtTipHelp.Text = Str("fr8_welcome_tip_help",
                "Click the ? button in the title bar any time for the full tour and per-feature guides.");
            TxtTipHover.Text = Str("fr8_welcome_tip_hover", "Hover over any setting to see what it does.");
            TxtTipAssets.Text = Str("fr8_welcome_tip_assets",
                "Add your own images and videos from the Library door, or point the app at any folder you like.");

            TxtPerfTitle.Text = Loc.Get("label_performance_warning");
            TxtPerfBody.Text = Str("fr8_welcome_perf_warning",
                "Running many features at once, especially at high frequencies, is heavy on older machines. " +
                "Turn some off or lower their rates in Settings if things get sluggish.");

            BtnPickFolder.Content = Str("fr8_welcome_pick_folder", "Choose a content folder");

            // --- step 2 ---
            TxtModHeading.Text = Str("fr8_modpick_heading", "Pick your flavour");
            TxtModSub.Text = Str("fr8_modpick_sub",
                "A mod re-skins the whole app: her name and voice, the art, the phrases, the programs. " +
                "Pick the one you want to start with - you can switch any time from the title bar.");
            TxtModHint.Text = Str("fr8_modpick_skip_hint",
                "Skipping keeps the neutral CCP Default. Downloads carry on in the background, so you can " +
                "close this window whenever you like.");

            // --- step 3 ---
            TxtTourHeading.Text = Str("fr8_tour_heading", "Seven doors");
            TxtTourOutro.Text = Str("fr8_tour_outro",
                "The rail on the left is always there. Take the tour and she will point at each door in turn, " +
                "or explore on your own - the ? button replays it whenever you want.");

            // --- chrome ---
            BtnBack.Content = Str("fr8_wizard_back", "Back");
        }

        // ------------------------------------------------------------------ steps

        private void ShowStep(int step)
        {
            _step = Math.Max(1, Math.Min(StepCount, step));

            Step1.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

            TxtStepCounter.Text = StrF("fr8_wizard_step_of", "Step {0} of {1}", _step, StepCount);
            BtnBack.Visibility = _step == 1 ? Visibility.Collapsed : Visibility.Visible;

            if (_step == 3)
            {
                BtnSkip.Content = Str("fr8_tour_explore", "Explore on my own");
                BtnNext.Content = Str("fr8_tour_take", "Take the tour");
            }
            else
            {
                BtnSkip.Content = Str("fr8_wizard_skip", "Skip setup");
                BtnNext.Content = Str("fr8_wizard_next", "Next");
            }

            if (_step == 1) RefreshWelcomeLogo();
            if (_step == 2) PrepareModStep();

            FadeInCurrentStep();
        }

        /// <summary>
        /// The only motion on this screen, and it is gated: at MotionLevel Off the page simply
        /// swaps. No loop, so there is nothing for the motion kill-switch to stop.
        /// </summary>
        private void FadeInCurrentStep()
        {
            try
            {
                var host = _step == 1 ? (FrameworkElement)Step1 : _step == 2 ? Step2 : Step3;
                host.BeginAnimation(OpacityProperty, null);

                if (!MotionFx.AllowTransitions)
                {
                    host.Opacity = 1;
                    return;
                }

                host.Opacity = 0;
                host.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            }
            catch { /* a transition must never be the reason a step fails to render */ }
        }

        // ------------------------------------------------------------------ step 2: mod pick

        private void BuildModCards()
        {
            var installedBadge = Loc.Get("modpicker_installed_badge");
            var activeModId = App.Mods?.ActiveModId ?? BuiltInMods.CCPDefaultId;

            foreach (var entry in ModPackCatalog.All)
            {
                Brush accent;
                try
                {
                    accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(entry.AccentHex));
                    accent.Freeze();
                }
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
                    // The compiled <Resource> art from the catalogue, exactly as ModPickerDialog uses
                    // it: these thumbnails survive the content-pack strip, so they can never be the
                    // thing that is missing on a fresh modular install. (ModResourceResolver resolves
                    // against the ACTIVE mod only, so it cannot paint five different candidates.)
                    ArtUri = entry.ArtUri,
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
                    card.SizeText = ModPackCatalog.FormatSize(ModPackCatalog.SizeBytesFor(entry));
                    if (IsPackInstalled(entry.PackId)) card.State = FirstRunModCard.CardState.Installed;
                }

                _cards.Add(card);
            }

            // Pre-select what this install is already running (CCP Default on a fresh box), so the
            // screen reads as "here is what you have, here is what you can have" and pressing Next
            // without touching anything is a no-op rather than an accidental switch.
            Select(_cards.FirstOrDefault(c => string.Equals(c.ModId, activeModId, StringComparison.OrdinalIgnoreCase))
                   ?? _cards.FirstOrDefault());
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

        private void Select(FirstRunModCard? card)
        {
            _selected = card;
            foreach (var c in _cards) c.IsSelected = ReferenceEquals(c, card);
        }

        private void ModCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FirstRunModCard card) Select(card);
        }

        /// <summary>
        /// The one-shot bookkeeping the standalone picker owns, applied at the moment this step is
        /// first shown (spend at open, not at queue). Deliberately reuses
        /// <see cref="ModPickerDialog.ShouldDeferForOffline"/> rather than restating it: a
        /// reimplemented offline guard is how a modular install loses its mod media permanently.
        /// </summary>
        private void PrepareModStep()
        {
            if (_modStepPrepared) return;
            _modStepPrepared = true;

            try
            {
                var settings = App.Settings?.Current;
                var svc = App.ReleaseContent;

                if (settings == null || svc == null)
                {
                    // No pack service this session: the cards still render (with their baked-in
                    // sizes) but there is nothing to download from here, and the one-shot offer is
                    // NOT spent - same call the picker's own guard makes.
                    SetModStepOffline(countOffer: false);
                    return;
                }

                if (svc.IsFullInstall)
                {
                    // Full/dev layout: the mod media is already on disk, so every card is installed
                    // and there is no offer to spend.
                    foreach (var card in _cards) card.MarkInstalled();
                    TxtModHint.Text = Str("fr8_modpick_installed_hint",
                        "Every mod is already on disk in this build - pick one and it switches straight away.");
                    return;
                }

                if (settings.ModPickerShown)
                {
                    // Already offered somehow (a re-armed offline showing, a hand-edited settings
                    // file). Picking still works; the offer is simply not spent twice.
                    return;
                }

                if (ModPickerDialog.ShouldDeferForOffline(settings.OfflineMode, svc.ManifestUnavailable,
                                                          settings.ModPickerOfflineOffers))
                {
                    App.Logger?.Information(
                        "[FirstRun] {Reason} - the mod step opens read-only and the offer is handed back",
                        settings.OfflineMode ? "Offline mode is on" : "Manifest unreachable this session");
                    SetModStepOffline(countOffer: true);
                    return;
                }

                // Spend the offer BEFORE anything can go wrong on this page, exactly as
                // ModPickerDialog.ShowIfNeeded does; the offline path below hands it back.
                settings.ModPickerShown = true;
                App.Settings?.Save();

                _ = RefreshManifestAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Mod step preparation failed - falling back to the offline copy");
                SetModStepOffline(countOffer: false);
            }
        }

        private async System.Threading.Tasks.Task RefreshManifestAsync()
        {
            try
            {
                var svc = App.ReleaseContent;
                if (svc == null) { SetModStepOffline(countOffer: false); return; }

                // Startup's EnsureBaselineAsync usually fetched the manifest already; only pay for a
                // round trip when a pack is genuinely unknown.
                var needsFetch = ModPackCatalog.Optional.Any(en => svc.GetPackInfo(en.PackId!) == null);
                if (needsFetch)
                {
                    var manifest = await svc.FetchManifestAsync().ConfigureAwait(true);
                    if (manifest == null) { SetModStepOffline(countOffer: true); return; }
                }

                foreach (var entry in ModPackCatalog.Optional)
                {
                    var card = _cards.FirstOrDefault(c => c.PackId == entry.PackId);
                    if (card == null || card.IsInstalled) continue;
                    card.SizeText = ModPackCatalog.FormatSize(ModPackCatalog.SizeBytesFor(entry));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Manifest refresh failed - falling back to the offline copy");
                SetModStepOffline(countOffer: true);
            }
        }

        /// <summary>
        /// Offline is a first-class state, not a showing: the cards stay readable (with their
        /// baked-in sizes) but nothing can be downloaded, so the one-shot offer is handed back for
        /// a launch that can reach the manifest - bounded by <c>MaxOfflineOffers</c>.
        /// </summary>
        private void SetModStepOffline(bool countOffer)
        {
            _modStepOffline = true;
            try { TxtModHint.Text = Loc.Get("modpicker_hint_offline"); } catch { }

            if (!countOffer || _offlineOfferCounted) return;
            _offlineOfferCounted = true;

            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;

                var offerCount = settings.ModPickerOfflineOffers + 1;
                settings.ModPickerOfflineOffers = offerCount;
                if (ModPickerDialog.ShouldReArmAfterOfflineShowing(offerCount))
                {
                    settings.ModPickerShown = false;
                    App.Logger?.Information(
                        "[FirstRun] Mod step ended offline (offer {Count}/{Max}) - re-arming the picker for a later launch",
                        offerCount, ModPickerDialog.MaxOfflineOffers);
                }
                else
                {
                    settings.ModPickerShown = true;
                    App.Logger?.Information(
                        "[FirstRun] Mod step ended offline for the {Count}th time - latching; the Mod Manager owns downloads from here",
                        offerCount);
                }
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Could not record the offline mod offer");
            }
        }

        /// <summary>
        /// Hands the chosen mod to the EXACT existing switching path - never a parallel one.
        /// Content already on disk goes straight through <c>MainWindow.ActivateChosenMod</c>
        /// (ActivateMod + ApplyActiveModChange); content that still has to be fetched records the
        /// intent with <see cref="PendingModActivation"/> and starts the download, so the switch
        /// happens the moment the pack lands - even after this window is gone, or in a later
        /// session. Choosing a mod MEANS choosing to run it.
        /// </summary>
        private void CommitModChoice()
        {
            try
            {
                var card = _selected;
                var chosen = card?.ModId;
                if (card == null || string.IsNullOrWhiteSpace(chosen)) return;
                if (string.Equals(_committedModId, chosen, StringComparison.OrdinalIgnoreCase)) return;
                _committedModId = chosen;

                if (string.Equals(chosen, App.Mods?.ActiveModId, StringComparison.OrdinalIgnoreCase)) return;

                if (PendingModActivation.IsContentAvailable(chosen))
                {
                    _owner?.ActivateChosenMod(chosen, PendingModActivation.Trigger.Immediate);
                    return;
                }

                if (_modStepOffline || !card.HasPack) return;

                PendingModActivation.Record(chosen);
                _ = DownloadChosenPackAsync(card);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Could not apply the chosen mod");
            }
        }

        private async System.Threading.Tasks.Task DownloadChosenPackAsync(FirstRunModCard card)
        {
            var svc = App.ReleaseContent;
            if (svc == null || string.IsNullOrEmpty(card.PackId)) return;

            card.MarkQueued();
            var progress = new Progress<double>(p => card.MarkProgress(p));

            bool ok = false;
            try
            {
                // CancellationToken.None on purpose: closing this window must not kill the download.
                // RequestPackAsync de-dupes and resumes from the surviving .partial, so re-opening
                // the Mod Manager joins the same task instead of starting a second one.
                ok = await svc.RequestPackAsync(card.PackId!, progress, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Pack {Pack} download threw", card.PackId);
            }

            if (ok) card.MarkInstalled();
            else card.MarkFailed();
        }

        // ------------------------------------------------------------------ step 3: the doors

        /// <summary>
        /// The seven doors, in rail order. Mirrors <c>MainWindow.NavDoorMap</c> (which is private,
        /// and is navigation truth - this list is only the wizard's description of it). Glyphs match
        /// the rail headers in MainWindow.xaml; the labels reuse the existing <c>nav_door_*</c>
        /// keys, so only the one-line blurbs are new copy.
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
                    FontWeight = FontWeights.Bold
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
                    Background = Application.Current?.TryFindResource("PanelBgBrush") as Brush,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = row
                };
                DoorsHost.Children.Add(shell);
            }
        }

        // ------------------------------------------------------------------ pack events

        private void OnPackProgressChanged(object? sender, PackProgressEventArgs e) =>
            MarshalToUi(() => FindCard(e.PackId)?.MarkProgress(e.Percent));

        private void OnPackInstalled(object? sender, string packId) =>
            MarshalToUi(() => FindCard(packId)?.MarkInstalled());

        /// <summary>Argument is a mod id (pack id as a fallback) - map it back to this screen's cards.</summary>
        private void OnModAvailabilityChanged(object? sender, string modOrPackId) =>
            MarshalToUi(() => FindCard(ModPackCatalog.PackIdForMod(modOrPackId) ?? modOrPackId)?.MarkInstalled());

        private FirstRunModCard? FindCard(string? packId) =>
            string.IsNullOrEmpty(packId)
                ? null
                : _cards.FirstOrDefault(c => string.Equals(c.PackId, packId, StringComparison.OrdinalIgnoreCase));

        private static void MarshalToUi(Action action)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                if (dispatcher.CheckAccess()) { action(); return; }

                // Normal, never Loaded: Loaded-priority work is starved in this app.
                dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
            }
            catch { }
        }

        // ------------------------------------------------------------------ chrome

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch { /* DragMove throws if the button was already released */ }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

        private void BtnNext_Click(object sender, RoutedEventArgs e)
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

        /// <summary>Skip always closes and never blocks; on the last step it reads "Explore on my own".</summary>
        private void BtnSkip_Click(object sender, RoutedEventArgs e) => CloseSafely();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => CloseSafely();

        private void BtnPickFolder_Click(object sender, RoutedEventArgs e)
        {
            // Deferred rather than opened here: the folder browser is a WinForms modal owned by
            // MainWindow, and stacking it under this modal is exactly the modal-on-modal the wizard
            // exists to remove. Run() opens it the instant this window is gone.
            PickAssetsFolderRequested = true;
            BtnPickFolder.IsEnabled = false;
            BtnPickFolder.Content = Str("fr8_welcome_pick_folder_queued",
                "We'll ask for your content folder right after this");
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseSafely();
        }

        private void CloseSafely()
        {
            try { Close(); } catch { }
        }

        private void OnWizardClosed(object? sender, EventArgs e)
        {
            if (_closed) return;
            _closed = true;

            // A choice made but never "Next"-ed (Esc, the X, Explore on my own) still counts - the
            // user ticked a mod, and honouring it is what the picker's own contract promises.
            CommitModChoice();

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
    }
}
