using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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

    /// <summary>What the first run does with the window the user just closed.</summary>
    public enum FirstRunOutcome
    {
        /// <summary>The gate was accepted and Enter was pressed: the app carries on.</summary>
        Proceed,

        /// <summary>Nobody accepted: hand the first run back and shut the app down.</summary>
        DeclineAndShutDown
    }

    /// <summary>
    /// The first run's two decisions, pure: no WPF, no <c>App</c>, no settings file. The wizard
    /// (and its tests) call these rather than restating the rules inline, because both of them used
    /// to live in code nobody could exercise: the age gate was a MessageBox in front of the window,
    /// and the offline flavour latch was buried in an event handler.
    /// </summary>
    public static class FirstRunGate
    {
        /// <summary>
        /// The 18+ gate. Enter on the Welcome step is the ONLY way past it: a tick with no Enter is
        /// somebody who read the sentence and closed the window, which is the same answer the old
        /// "Do you wish to continue?" MessageBox took as No.
        /// </summary>
        public static FirstRunOutcome Decide(bool ageChecked, bool enterPressed) =>
            (ageChecked && enterPressed) ? FirstRunOutcome.Proceed : FirstRunOutcome.DeclineAndShutDown;

        /// <summary>
        /// What <c>ModPickerShown</c> must be after the wizard's flavour step ended offline: always
        /// latched, whatever the offer count.
        ///
        /// <para>The wizard used to hand the offer back here (the standalone picker's own re-arm
        /// rule, <see cref="ModPickerDialog.ShouldReArmAfterOfflineShowing"/>), and that is exactly
        /// how "the Circe one" came back days later on its own. A first run is offered once; if the
        /// box was offline for it, the Mod Manager and the Library own downloads from then on and
        /// no popup ever asks again. <c>ModPickerOfflineOffers</c> keeps counting, for diagnostics
        /// only. The standalone upgrader path keeps its own re-arm - that population never saw a
        /// wizard.</para>
        /// </summary>
        public static bool ModPickerShownAfterOfflineFlavourStep(int offersAfterShowing) => true;

        /// <summary>
        /// Must this launch stop, given what the settings say about the 18+ gate?
        ///
        /// <para>The question is not "did the user decline" - it is "is this process about to keep
        /// running with nobody having answered". <see cref="Decide"/> covers the wizard's own two
        /// buttons; this covers everything that happens INSTEAD of them. The flags are claimed
        /// before the window exists (that is what stops the old MessageBox firing on top of it), and
        /// the App-level gate stands down for the whole launch once they are, so a wizard whose
        /// constructor threw, whose ShowDialog threw, or that the ladder gave up on left the app
        /// running an adult product with <c>HasAcceptedAgeVerification</c> still false and nothing
        /// left to ask. One line, so the answer is the same on all three paths and can be asserted
        /// without a Window.</para>
        /// </summary>
        /// <param name="ageAccepted">AppSettings.HasAcceptedAgeVerification as it stands now.</param>
        public static bool MustShutDown(bool ageAccepted) => !ageAccepted;
    }

    /// <summary>
    /// The first run, in two steps: Welcome (language, your own content folder, the 18+ gate) and
    /// Flavour (which mod to run). It replaces what used to be a modal gauntlet: the age-verify
    /// MessageBox, <c>WelcomeDialog</c>, the first-launch <c>ModPickerDialog.ShowIfNeeded</c> call,
    /// the seven-step spotlight tour and the hardcoded-English "choose a content folder" MessageBox.
    ///
    /// <para><b>One gate.</b> The age check is this window's primary button rather than a
    /// MessageBox in front of it: Enter stays disabled until the box is ticked, ticking and
    /// pressing Enter writes <c>HasAcceptedAgeVerification</c>, and closing step 1 without that
    /// hands the first run back and shuts the app down - precisely what the MessageBox's No did.
    /// <see cref="FirstRunGate.Decide"/> is that rule, on its own, unit tested.</para>
    ///
    /// <para><b>One tour.</b> There is no doors step and no "Take the tour" button. EMI offers the
    /// single walk there is, once, from her chip, after this window is gone.</para>
    ///
    /// <para>Every PRESERVED surface is untouched: the consent dialogs (webcam / mic / awareness /
    /// explicit content) are still lazily user-initiated and are never folded in here, the lockdown
    /// intro keeps its secret-exit line, and <c>NewYearNoteReactionSeen</c> is never touched.</para>
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
    /// starved in this app), closing mid-download does NOT cancel the download, and an offline
    /// first run LATCHES the flavour offer rather than re-arming it.</para>
    /// </summary>
    public partial class FirstRunWizard : Window
    {
        private const int StepCount = 2;

        private readonly MainWindow? _owner;
        private readonly ObservableCollection<FirstRunModCard> _cards = new();

        private int _step = 1;
        private FirstRunModCard? _selected;

        /// <summary>The mod id already handed to the activation path, so nothing can double-fire it.</summary>
        private string? _committedModId;

        private bool _modStepPrepared;
        private bool _modStepOffline;
        private bool _offlineOfferCounted;
        private bool _closed;
        private bool _populatingLanguage;

        /// <summary>True once Enter was pressed on the Welcome step with the 18+ box ticked.</summary>
        private bool _enteredPastGate;

        /// <summary>
        /// The gate's verdict, read by <see cref="Run"/> on the far side of the modal: false means
        /// the user declined and the app is already shutting down, so nothing else may be started.
        /// </summary>
        public bool AgeAccepted => _enteredPastGate;

        /// <summary>Set by the Welcome step's folder button; the picker opens after this window closes.</summary>
        public bool PickAssetsFolderRequested { get; private set; }

        /// <summary>
        /// True once <see cref="ShouldRunAndClaim"/> has claimed THIS launch for the wizard.
        ///
        /// <para>Read by <c>App.OnStartup</c>'s age-verification block, which runs several hundred
        /// lines after MainWindow's constructor and therefore always sees <c>Welcomed = true</c> on
        /// a fresh install - the claim above set it. Without this the MessageBox the wizard exists
        /// to replace would fire in front of the wizard on exactly the population that must never
        /// see it. Not reset by <see cref="HandBackFirstRun"/>: the launch was still the wizard's,
        /// whatever became of the window.</para>
        /// </summary>
        public static bool FirstRunClaimedThisLaunch { get; private set; }

        private FirstRunWizard(MainWindow? owner)
        {
            _owner = owner;
            InitializeComponent();

            ApplyStaticText();
            PopulateLanguages();
            BuildModCards();
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
        /// <item><c>LastSeenVersion</c> - the first-run branch is the ONE path that never reaches
        /// <c>MainWindow.ShowWhatsNewIfNeeded</c>, which is where every other launch stamps it.
        /// Left blank, this install's first upgrade hit that method's empty-string guard, was
        /// read as "fresh install", and had its first ever patch notes stamped away unshown.
        /// Stamping here is correct on both wizard outcomes: it IS a fresh install of this
        /// version whether or not the wizard ended up on screen, so <see cref="HandBackFirstRun"/>
        /// deliberately does not undo it (the next launch re-stamps whatever version it is).</item>
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
                settings.LastSeenVersion = UpdateService.AppVersion;
                App.Settings?.Save();
                FirstRunClaimedThisLaunch = true;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Gate check failed - skipping the first-run wizard");
                return false;
            }
        }

        /// <summary>
        /// Undoes <see cref="ShouldRunAndClaim"/> when the wizard was never shown (the update dialog
        /// outlasted its 30s wait, the window never loaded) or when the user declined the 18+ gate.
        /// Without this the flags would be spent on a screen nobody accepted and the install would
        /// silently never get a first run at all. A crash inside the wizard is deliberately NOT
        /// covered - that is what spending up front buys.
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

        /// <summary>True once some path has already handed the first run back and asked for the
        /// shutdown, so the others neither log it again nor call Shutdown twice.</summary>
        private static bool _ungatedShutdownHandled;

        /// <summary>
        /// Stops a launch that is about to continue with the 18+ question unanswered.
        ///
        /// <para>Three paths reach it and all three mean the same thing: the wizard declined
        /// (OnWizardClosed), the wizard never reached its gate (a throwing constructor or
        /// ShowDialog, see <see cref="Run"/>), or the ladder gave up on it after five minutes
        /// (MainWindow's onAbandoned). The verdict itself is
        /// <see cref="FirstRunGate.MustShutDown"/> so it can be asserted without a Window.</para>
        ///
        /// <para>Does nothing when the flag IS set. That is not a hypothetical: a launch that was
        /// handed back after the user had already accepted on an earlier launch arrives here with a
        /// perfectly good acceptance on file, and shutting that down would be an app that refuses to
        /// start.</para>
        /// </summary>
        /// <param name="reason">For the log line; also the hand-back's reason when it makes one.</param>
        /// <param name="handBack">False when the caller has already handed the first run back with a
        /// more specific reason of its own, so the log does not say it twice.</param>
        internal static void AbortUngatedLaunch(string reason, bool handBack = true)
        {
            try
            {
                if (_ungatedShutdownHandled) return;

                bool accepted = App.Settings?.Current?.HasAcceptedAgeVerification == true;
                if (!FirstRunGate.MustShutDown(accepted)) return;

                _ungatedShutdownHandled = true;

                if (handBack) HandBackFirstRun(reason);
                App.Logger?.Information(
                    "[FirstRun] The 18+ gate was never accepted ({Reason}) - shutting down rather than running ungated", reason);

                try { Application.Current?.Shutdown(); } catch { }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Could not stop an ungated launch");
            }
        }

        /// <summary>
        /// Opens the wizard modally and performs whatever the user asked for on the way out: the
        /// content-folder picker (it is modal too, so it runs alone), and then the quiet window that
        /// keeps the next ten minutes free of popups. Never throws - a first-run screen must never
        /// be the reason a fresh install fails to start.
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

            // Every exit that is not "Enter, with the box ticked" lands here: the gate said no (the
            // shutdown is already in flight, see OnWizardClosed), the constructor threw, or
            // ShowDialog threw and was swallowed above. On the last two the window never reached the
            // gate at all, and because the flags were claimed BEFORE it opened - and the App-level
            // age MessageBox stands down for the whole launch once they are - the app would
            // otherwise carry on with nobody having answered the 18+ question and nothing left to
            // ask it. Idempotent with the decline path, which already did both.
            if (wizard == null || !wizard.AgeAccepted)
            {
                AbortUngatedLaunch("wizard never reached the gate");
                return;
            }

            bool pickFolder = wizard.PickAssetsFolderRequested;

            // Normal, never Loaded: Loaded-priority work is starved in this app (compositor host +
            // avatar animations keep the dispatcher busy) and silently never runs.
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (pickFolder)
                {
                    try { owner.BtnPickAssetsFolder_Click(owner, new RoutedEventArgs()); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "[FirstRun] Assets folder picker failed"); }
                }

                // The far side of the first run: the screen is the user's, and it stays theirs.
                // The quiet window: ten minutes in which nothing else pops (the presenter parks it in the Inbox).
                try { App.Startup?.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10)); }
                catch (Exception ex) { App.Logger?.Debug(ex, "[FirstRun] Could not open the quiet window"); }
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
        /// hardcode logo.png.
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

        /// <summary>
        /// Every string on the screen, re-runnable: the language row switches the app's language in
        /// place, and this window's own copy is code-behind rather than <c>{loc:Str}</c> bindings,
        /// so it has to be repainted by hand when that happens.
        /// </summary>
        private void ApplyStaticText()
        {
            Title = Str("fr8_wizard_title", "Getting started");
            TxtWizardTitle.Text = Title;

            // --- step 1: welcome ---
            RefreshWelcomeLogo();

            TxtAppTitle.Text = Loc.Get("app_title");
            TxtWelcomeHeading.Text = Str("fr8_welcome_title", "Welcome.");
            TxtWelcomeBody.Text = Str("fr8_welcome_body",
                "Two quick choices and she is all yours. Everything else can wait until you ask for it.");

            TxtLanguageLabel.Text = Str("fr8_welcome_language", "Language");
            TxtLanguageHint.Text = Str("fr8_welcome_language_hint", "You can change this any time from the title bar.");

            TxtFolderLabel.Text = Str("fr8_welcome_folder", "Your own content");
            TxtFolderHint.Text = Str("fr8_welcome_folder_hint",
                "Optional. Point the app at a folder of your own images and videos.");

            // Keep the queued confirmation if the folder was already asked for: this method also
            // runs on a language switch, and repainting the button would quietly un-say it.
            BtnPickFolder.Content = PickAssetsFolderRequested
                ? Str("fr8_welcome_pick_folder_queued", "We'll ask for your content folder right after this")
                : Str("fr8_welcome_pick_folder", "Choose a content folder");

            TxtAgeConfirm.Text = Str("fr8_age_confirm",
                "I am 18 or older and I have read the content policy.");
            RunContentPolicy.Text = Str("fr8_age_policy_link", "Read the content policy");

            // --- step 2: flavour ---
            TxtModHeading.Text = Str("fr8_modpick_heading", "Pick your flavour");
            TxtModSub.Text = Str("fr8_modpick_sub",
                "A mod re-skins the whole app: her name and voice, the art, the phrases, the programs. " +
                "Pick the one you want to start with - you can switch any time from the title bar.");
            if (!_modStepOffline)
            {
                TxtModHint.Text = Str("fr8_modpick_offline_hint",
                    "Offline? The download waits. No second ask.");
            }
        }

        // ------------------------------------------------------------------ steps

        private void ShowStep(int step)
        {
            _step = Math.Max(1, Math.Min(StepCount, step));

            Step1.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;

            if (_step == 1) RefreshWelcomeLogo();
            if (_step == 2) PrepareModStep();

            ApplyStepChrome();
            FadeInCurrentStep();
        }

        /// <summary>
        /// The title-bar counter and the two footer buttons. Split out of <see cref="ShowStep"/>
        /// because a language switch and a card selection both change this copy without changing
        /// which step is on screen.
        /// </summary>
        private void ApplyStepChrome()
        {
            TxtStepCounter.Text = StrF("fr8_wizard_step_of", "Step {0} of {1}", _step, StepCount);

            if (_step == 1)
            {
                // No skip on the gate: not accepting is closing the window, and the muted line
                // under the button says so rather than dressing it up as a third choice.
                BtnSkip.Visibility = Visibility.Collapsed;
                BtnNext.Content = Str("fr8_welcome_enter", "Enter");
                BtnNext.IsEnabled = ChkAgeConfirm.IsChecked == true;

                TxtCloseHint.Text = Str("fr8_welcome_close_hint", "Not for you? Just close this window.");
                TxtCloseHint.Visibility = Visibility.Visible;
            }
            else
            {
                BtnSkip.Visibility = Visibility.Visible;
                BtnSkip.Content = Str("fr8_modpick_keep_default", "Keep the default");
                BtnNext.IsEnabled = true;
                UpdateFlavourButton();

                TxtCloseHint.Text = "";
                TxtCloseHint.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>"Enter with Circe", or plain "Enter" for the in-box default.</summary>
        private void UpdateFlavourButton()
        {
            if (_step != 2) return;

            var card = _selected;
            var isDefault = card == null
                            || string.Equals(card.ModId, BuiltInMods.CCPDefaultId, StringComparison.OrdinalIgnoreCase);

            BtnNext.Content = isDefault
                ? Str("fr8_welcome_enter", "Enter")
                : StrF("fr8_modpick_enter_with", "Enter with {0}", card!.Name);
        }

        /// <summary>
        /// The only motion on this screen, and it is gated: at MotionLevel Off the page simply
        /// swaps. No loop, so there is nothing for the motion kill-switch to stop.
        /// </summary>
        private void FadeInCurrentStep()
        {
            try
            {
                var host = _step == 1 ? (FrameworkElement)Step1 : Step2;
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

        // ------------------------------------------------------------------ step 1: language

        /// <summary>
        /// The same language list the title-bar pill and Settings offer, from the same helper -
        /// <c>MainWindow.FillLanguageCombo</c> - so this screen can never drift from them.
        /// </summary>
        private void PopulateLanguages()
        {
            _populatingLanguage = true;
            try { MainWindow.FillLanguageCombo(CmbWizardLanguage, shortLabels: false); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[FirstRun] Could not fill the language list"); }
            finally { _populatingLanguage = false; }
        }

        private void CmbWizardLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_populatingLanguage) return;
            if (CmbWizardLanguage.SelectedItem is not ComboBoxItem item) return;

            try
            {
                // The one writer of AppSettings.Language, either through MainWindow (which also
                // re-selects its own two surfaces and raises the restart banner) or, when there is
                // no owner, through the same static core it calls.
                if (_owner != null) _owner.ApplyLanguageSelection(item.Tag as string);
                else MainWindow.SetApplicationLanguage(item.Tag as string);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Language switch failed");
            }

            // This window's copy is assigned in code-behind, so it does not follow the live
            // LocalizationManager change on its own.
            ApplyStaticText();
            ApplyStepChrome();
        }

        // ------------------------------------------------------------------ step 1: the gate

        private void AgeConfirm_Changed(object sender, RoutedEventArgs e) => ApplyStepChrome();

        /// <summary>The sentence is the target too - a 20px box is a mean thing to aim at.</summary>
        private void AgeConfirmLabel_Click(object sender, MouseButtonEventArgs e)
        {
            ChkAgeConfirm.IsChecked = ChkAgeConfirm.IsChecked != true;
            e.Handled = true;
        }

        private void LnkContentPolicy_Click(object sender, RoutedEventArgs e)
        {
            // The same constant the moderation warning uses - one URL, one place.
            var url = ContentPolicyWarningDialog.PolicyUrl;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Failed to open the content policy URL {Url}", url);
            }
        }

        /// <summary>
        /// Writes the acceptance the moment Enter is pressed, before anything else on the way to
        /// step 2 can throw. <c>App.xaml.cs</c>'s MessageBox now only covers the other population -
        /// an install that is already <c>Welcomed</c> but somehow never accepted.
        /// </summary>
        private void RecordAgeAcceptance()
        {
            _enteredPastGate = true;
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;

                settings.HasAcceptedAgeVerification = true;
                App.Settings?.Save();
                App.Logger?.Information("[FirstRun] Age verification accepted on the Welcome step");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Could not record the age acceptance");
            }
        }

        // ------------------------------------------------------------------ step 2: flavour

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
            // screen reads as "here is what you have, here is what you can have" and pressing Enter
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
            UpdateFlavourButton();
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
                    // sizes) but there is nothing to download from here.
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
                    // Already offered somehow (a hand-edited settings file). Picking still works;
                    // the offer is simply not spent twice.
                    return;
                }

                if (ModPickerDialog.ShouldDeferForOffline(settings.OfflineMode, svc.ManifestUnavailable,
                                                          settings.ModPickerOfflineOffers))
                {
                    App.Logger?.Information(
                        "[FirstRun] {Reason} - the flavour step opens read-only",
                        settings.OfflineMode ? "Offline mode is on" : "Manifest unreachable this session");
                    SetModStepOffline(countOffer: true);
                    return;
                }

                // Spend the offer BEFORE anything can go wrong on this page, exactly as
                // ModPickerDialog.ShowIfNeeded does.
                settings.ModPickerShown = true;
                App.Settings?.Save();

                _ = RefreshManifestAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Flavour step preparation failed - falling back to the offline copy");
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
        /// baked-in sizes) but nothing can be downloaded from here.
        ///
        /// <para>The offer is NOT handed back. An offline first run latches the picker
        /// (<see cref="FirstRunGate.ModPickerShownAfterOfflineFlavourStep"/>) so no standalone
        /// picker ever fires days later on its own - the Mod Manager and the Library own downloads
        /// from here. <c>ModPickerOfflineOffers</c> keeps counting for diagnostics only.</para>
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
                settings.ModPickerShown = FirstRunGate.ModPickerShownAfterOfflineFlavourStep(offerCount);
                App.Settings?.Save();

                App.Logger?.Information(
                    "[FirstRun] Flavour step ended offline (offer {Count}, diagnostics only) - latched; " +
                    "the Mod Manager owns downloads from here",
                    offerCount);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[FirstRun] Could not record the offline flavour offer");
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

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_step == 1)
            {
                // Belt and braces: the button is disabled until the box is ticked, but the gate is
                // the one thing on this screen that must not be reachable by accident.
                if (ChkAgeConfirm.IsChecked != true) return;

                RecordAgeAcceptance();
                ShowStep(2);
                return;
            }

            CommitModChoice();
            CloseSafely();
        }

        /// <summary>
        /// "Keep the default": the second step's secondary. It puts the selection back on whatever
        /// this install is already running before closing, so a card the user clicked and then
        /// changed their mind about is not committed on the way out.
        /// </summary>
        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            var activeModId = App.Mods?.ActiveModId ?? BuiltInMods.CCPDefaultId;
            Select(_cards.FirstOrDefault(c => string.Equals(c.ModId, activeModId, StringComparison.OrdinalIgnoreCase))
                   ?? _cards.FirstOrDefault());
            CloseSafely();
        }

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

            UnsubscribePackEvents();

            if (FirstRunGate.Decide(ChkAgeConfirm.IsChecked == true, _enteredPastGate)
                == FirstRunOutcome.DeclineAndShutDown)
            {
                // Exactly what the old age-verification MessageBox's "No" did, plus the hand-back
                // the MessageBox never had: the flags were spent before this window opened, so a
                // later launch would otherwise never offer the screen again. Latching it here is
                // what keeps Run's safety net from saying the same thing a second time.
                _ungatedShutdownHandled = true;
                HandBackFirstRun("age gate declined");
                App.Logger?.Information("[FirstRun] The 18+ gate was not accepted - shutting down");
                try { Application.Current?.Shutdown(); } catch { }
                return;
            }

            // A choice made but never entered (Esc, the X on the flavour step) still counts - the
            // user ticked a mod, and honouring it is what the picker's own contract promises.
            CommitModChoice();
        }

        private void UnsubscribePackEvents()
        {
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
