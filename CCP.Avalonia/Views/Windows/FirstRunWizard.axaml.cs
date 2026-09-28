using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
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
        /// The catalogue's shipped art, never the active mod's: WPF binds the compiled ArtUri for
        /// the same reason (five candidates, one active mod).
        /// </summary>
        public IImage? Art { get; init; }

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
    /// (see <see cref="ModPickerCatalog"/>), and the pack-installed signal.</para>
    ///
    /// <para><see cref="Run"/> is now real, and <c>MainShellWindow.FirstRun.cs</c> calls it: the
    /// shell claims the gate in its constructor and opens this window once it is on screen. Both
    /// of the wizard's outgoing actions land on this head - the content-folder picker is
    /// <c>MainShellWindow.PickAssetsFolder</c>, opened at once from the button, and the short walk is
    /// <c>CoreTutorial.Start("ShortWalk")</c>, a seam this head has not seeded, so that button
    /// reaches the tour service and no tour appears (the same state AwarenessTabView and
    /// ModCreatorWindow are in).</para>
    ///
    /// <para>KNOWN AND ACCEPTED, now that a fresh install actually walks these three steps: step 2
    /// takes a mod choice that <see cref="CommitModChoice"/> does not commit, and its hint copy
    /// ("Downloads carry on in the background", "you can switch any time from the title bar")
    /// describes a mod system this head does not have at all - <c>CoreMods</c> is unseeded, and
    /// the shell's own mod switcher (<c>BtnManageMods_Click</c>,
    /// <c>ModSelectorCombo_SelectionChanged</c>) is a stub for the same reason. Nothing is
    /// downloaded, charged, consented to or falsely reported as installed: the cards read their
    /// state from <c>CoreReleaseContent</c>'s stamps, which answer "unknown" unseeded rather than
    /// guessing. This is the mod seam's gap showing through, not a defect this screen introduces,
    /// and it is worth less than an install with no onboarding at all - but it is the first thing
    /// to fix once a mod service reaches Core.</para>
    ///
    /// <para>What does not survive: <see cref="PrepareModStep"/> (the offline-offer bookkeeping is
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
        /// <summary>The catalogue's shipped pass card, or null (the frame then shows its flat ground).</summary>
        private static IImage? LoadPassCard(string artName)
        {
            try
            {
                using var s = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + artName));
                return new Bitmap(s);
            }
            catch (Exception ex)
            {
                Log.Debug("FirstRunWizard: pass card {Name} would not load: {E}", artName, ex.Message);
                return null;
            }
        }

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
        private readonly CheckBox _chkAgeConfirm;
        private readonly TextBlock _txtAgeConfirm;
        private readonly TextBlock _txtContentPolicy;
        private readonly TextBlock _txtCloseHint;
        private readonly Button _btnSkip;
        private readonly Button _btnNext;

        /// <summary>True once Enter was pressed on the Welcome step with the 18+ box ticked.</summary>
        public bool AgeAccepted { get; private set; }

        /// <summary>Set by "Take the tour"; read by the caller after the modal returns.</summary>
        public bool StartTourRequested { get; private set; }

        /// <summary>The folder the Welcome step's button applied, shown on that button.</summary>
        private string? _pickedFolder;

        /// <summary>
        /// The only constructor. WPF's took the owning MainWindow so the mod commit could call
        /// back into it; nothing here can, so it takes nothing - which is also what
        /// <c>--render-all</c> needs to discover the view. The owner is passed to
        /// <c>ShowDialog</c> by <see cref="Run"/> instead, which is all Avalonia needs it for.
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
            _chkAgeConfirm = this.FindControl<CheckBox>("ChkAgeConfirm")!;
            _txtAgeConfirm = this.FindControl<TextBlock>("TxtAgeConfirm")!;
            _txtContentPolicy = this.FindControl<TextBlock>("TxtContentPolicy")!;
            _txtCloseHint = this.FindControl<TextBlock>("TxtCloseHint")!;
            _btnSkip = this.FindControl<Button>("BtnSkip")!;
            _btnNext = this.FindControl<Button>("BtnNext")!;

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
            _btnSkip.Click += (_, _) => CloseSafely();
            _btnNext.Click += BtnNext_Click;
            _chkAgeConfirm.IsCheckedChanged += (_, _) => ApplyStepChrome();
            // WPF AgeConfirmLabel_Click: the sentence is the target too.
            _txtAgeConfirm.PointerReleased += (_, _) => _chkAgeConfirm.IsChecked = _chkAgeConfirm.IsChecked != true;

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
        /// exactly where <c>WelcomeDialog.ShowIfNeeded</c> used to. That dialog carries a SECOND
        /// copy of the same latch, and only one of the two may ever be called or the second sees
        /// an already-claimed flag and shows nothing. SETTLED, and it is WPF's answer: the wizard
        /// owns the latch. <c>MainShellWindow.FirstRun.cs</c> calls this and nothing else;
        /// <c>WelcomeDialog.ShowIfNeeded</c> stays deliberately uncalled on this head.
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
        /// Opens the wizard modally on <paramref name="owner"/> and performs whatever the user
        /// asked for on the way out: the short walk (the folder picker already ran from its button). Never throws - a first-run screen must
        /// never be the reason a fresh install fails to start.
        ///
        /// <para>WPF's <c>Run(MainWindow)</c> is synchronous because <c>ShowDialog</c> is;
        /// Avalonia's is awaitable, so this returns a Task and the caller awaits it. The tail runs
        /// straight after the await rather than from a second dispatcher post: WPF needed the post
        /// only to get off <c>ShowDialog</c>'s nested message loop, and <c>await</c> already is
        /// that.</para>
        ///
        /// <para><c>StartTutorial(TutorialType.ShortWalk)</c> becomes
        /// <c>CoreTutorial.Start("ShortWalk")</c> - the seam takes the head's tutorial-type name as
        /// a string. This head seeds no <c>StartAction</c>, so that call is a silent no-op today
        /// and the button starts no tour; it reaches the real seam, which is the same state every
        /// other tour button on this head is in.</para>
        /// </summary>
        public static async Task Run(MainShellWindow owner)
        {
            FirstRunWizard? wizard = null;
            try
            {
                MainShellWindow.IsStartupDialogShowing = true;
                wizard = new FirstRunWizard();
                await wizard.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstRun] The first-run wizard failed to show");
            }
            finally
            {
                MainShellWindow.IsStartupDialogShowing = false;
            }

            // WPF Run's safety net (FirstRunWizard.xaml.cs:482-486): every exit that is not
            // "Enter, with the box ticked" stops the launch rather than running ungated.
            if (wizard == null || !wizard.AgeAccepted)
            {
                AbortUngatedLaunch(owner, "age gate not accepted");
                return;
            }

            if (wizard.StartTourRequested)
            {
                // The SHORT WALK, not the door-by-door tour: seven cards, about ninety seconds,
                // and it teaches the app rather than the furniture. Somebody who has just been
                // shown all seven doors on the screen behind this button does not need them named
                // again. The full tour is the first row in the ? panel, which is what the wizard's
                // outro says.
                try { CoreTutorial.Start("ShortWalk"); }
                catch (Exception ex) { Log.Warning(ex, "[FirstRun] Could not start the short walk"); }
            }
        }

        /// <summary>
        /// WPF AbortUngatedLaunch (FirstRunWizard.xaml.cs:428): an acceptance already on file
        /// (FirstRunGate.MustShutDown) lets the launch continue; otherwise hand the first run back
        /// and shut down, which is what WPF's decline does.
        /// </summary>
        internal static void AbortUngatedLaunch(MainShellWindow owner, string reason)
        {
            try
            {
                if (CoreSettings.Current.HasAcceptedAgeVerification) return;
                HandBackFirstRun(reason);
                Log.Information("[FirstRun] The 18+ gate was never accepted ({Reason}) - shutting down rather than running ungated", reason);
                owner.RequestExit();
            }
            catch (Exception ex) { Log.Warning(ex, "[FirstRun] Could not stop an ungated launch"); }
        }

        /// <summary>WPF RecordAgeAcceptance: written the moment Enter is pressed.</summary>
        private void RecordAgeAcceptance()
        {
            AgeAccepted = true;
            try
            {
                CoreSettings.Current.HasAcceptedAgeVerification = true;
                CoreSettings.Save();
                Log.Information("[FirstRun] Age verification accepted on the Welcome step");
            }
            catch (Exception ex) { Log.Warning(ex, "[FirstRun] Could not record the age acceptance"); }
        }

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

            _txtPickFolder.Text = _pickedFolder ?? Str("fr8_welcome_pick_folder", "Choose a content folder");
            _txtAgeConfirm.Text = Str("fr8_age_confirm", "I am 18 or older and I have read the content policy.");
            _txtContentPolicy.Text = Str("fr8_age_policy_link", "Read the content policy");

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
            ApplyStepChrome();

            // WPF re-called RefreshWelcomeLogo from ShowStep(1), so Back-navigation after a mod
            // pick repaints instead of showing the previous mod's mark.
            if (_step == 1) RefreshWelcomeLogo();
            if (_step == 2) PrepareModStep();

            FadeInCurrentStep();
        }

        /// <summary>WPF ApplyStepChrome (FirstRunWizard.xaml.cs:637-661): step 1 has no skip, its
        /// primary is Enter and stays disabled until the 18+ box is ticked.</summary>
        private void ApplyStepChrome()
        {
            _btnSkip.IsVisible = _step != 1;
            _btnNext.IsEnabled = _step != 1 || _chkAgeConfirm.IsChecked == true;
            _txtCloseHint.Text = _step == 1 ? Str("fr8_welcome_close_hint", "Not for you? Close this window.") : "";
            _txtCloseHint.IsVisible = _step == 1;

            if (_step == 1) _txtNext.Text = Str("fr8_welcome_enter", "Enter");
            else if (_step == 3)
            {
                _txtSkip.Text = Str("fr8_tour_explore", "Explore on my own");
                _txtNext.Text = Str("fr8_tour_take", "Take the tour");
            }
            else
            {
                _txtSkip.Text = Str("fr8_wizard_skip", "Skip setup");
                _txtNext.Text = Str("fr8_wizard_next", "Next");
            }
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

            foreach (var entry in ModPickerCatalog.All)
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
                    Art = LoadPassCard(entry.ArtName),
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
                    card.SizeText = ModPickerCatalog.FormatSize(ModPickerCatalog.SizeBytesFor(entry));
                    if (ModPickerCatalog.IsInstalled(entry.PackId)) card.State = FirstRunModCard.CardState.Installed;
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
            if (!_btnPickFolder.IsEnabled) return;   // folder picker still open: finish after it answers
            if (_step == 1)
            {
                // Belt and braces, as WPF: the button is disabled until the box is ticked.
                if (_chkAgeConfirm.IsChecked != true) return;
                RecordAgeAcceptance();
            }
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

        private async void BtnPickFolder_Click(object? sender, RoutedEventArgs e)
        {
            // Opened here, owned by this window: WPF deferred it (FirstRunWizard.xaml.cs:1175) only
            // to dodge a modal-on-modal Win32 folder browser, and the user saw nothing happen
            // (docs/avalonia-decisions.md). Same guard, write and follow-ups as the shell's picker.
            _btnPickFolder.IsEnabled = false;
            if (await MainShellWindow.PickAssetsFolder(this) is { } chosen)
            {
                _pickedFolder = chosen;
                _txtPickFolder.Text = chosen;
            }
            _btnPickFolder.IsEnabled = true;
        }

        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (!_btnPickFolder.IsEnabled) { e.Handled = true; return; }   // same: don't close under the picker
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
            if (!AgeAccepted) return;   // declined: Run hands the first run back and shuts down

            // A choice made but never "Next"-ed (Esc, the X, Explore on my own) still counts - the
            // user ticked a mod, and honouring it is what the picker's own contract promises.
            CommitModChoice();
        }
    }
}
