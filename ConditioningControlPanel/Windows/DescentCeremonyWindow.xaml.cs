using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.Descent;
using Serilog;

// NAMESPACE TRAP, and it is not a style preference. Every window under Windows/ lives in the
// FLAT ConditioningControlPanel namespace — not one of them declares ConditioningControlPanel.Windows.
// Declaring it here compiles for a moment and then breaks ScreenOcrService, whose
// `Windows.Graphics.Imaging.BitmapDecoder` is resolved relative to the enclosing
// ConditioningControlPanel namespace: the instant a ConditioningControlPanel.Windows exists, it
// shadows the WinRT `Windows` root and the OCR service stops finding its decoder. Keep it flat.
namespace ConditioningControlPanel
{
    /// <summary>
    /// THE MIGRATION CEREMONY (CONTRACTS-0812 §4, design doc §6). Four acts, one question, asked
    /// once in the life of an account: intro → the two doors → a plain-language one-way confirm →
    /// the close.
    ///
    /// <para><b>This window never opens on its own.</b> Its only constructor takes a
    /// <see cref="DescentMigrationOffer"/>, and the only thing in the app that can build one is
    /// ProfileSyncService parsing <c>descent_migration.required</c> off a sync response. No menu
    /// item reaches it, no setting reveals it, no flag turns it on. Delete the server flag and
    /// this file is dead code.</para>
    ///
    /// <para><b>No offers, ever</b> (CONTRACTS §0.6). Nothing on this surface may mention a price,
    /// a tier, a pack or an upgrade — not on the closing act, not on a shelf, not anywhere. This
    /// is the night the wipe does not happen.</para>
    ///
    /// <para><b>Escape is not handled here, on purpose.</b> Escape is the app's panic key and it
    /// is delivered by a global low-level hook regardless of focus; swallowing it would put a
    /// fullscreen topmost window between a user and the one key they are promised always works.
    /// The way out of this window is the "Not tonight" button, and taking it costs nothing —
    /// the server re-offers on the next sync.</para>
    /// </summary>
    public partial class DescentCeremonyWindow : Window
    {
        /// <summary>
        /// Live instances, so the panic path can clear the screen. Static because panic has no
        /// reference to hand: it is a global keystroke, not a UI interaction.
        /// </summary>
        private static readonly List<DescentCeremonyWindow> Live = new();

        /// <summary>
        /// The height, in DIPs, below which the act stops scaling down and starts scrolling
        /// instead (ccp-bugs #1109). The tallest act is the two doors at roughly 630 DIPs, so the
        /// floor is a little under half scale - and the machines that reach it are running 250% to
        /// 300% Windows scaling, where half scale is still larger on the glass than the design is
        /// at 100%.
        ///
        /// <para>The act host is the window less the margins, the eyebrow and the escape hatch,
        /// which is about 187 DIPs: every scaling Windows actually offers clears this floor and
        /// scales to fit rather than scrolling. 3840x2160 at 300% (1280x720 DIPs), 2560x1600 at
        /// 250% (1024x640), 1920x1080 at 200% (960x540) and 2560x1440 at 300% (853x480) all fit
        /// whole. Past that the scroller takes over, because a button that has to be scrolled to
        /// is still a button, and one shrunk past reading is not.</para>
        /// </summary>
        private const double ActMinHeight = 290;

        private readonly DescentMigrationOffer _offer;
        private readonly int _restoreLevel;
        private string? _pendingChoice;
        private bool _committed;

        public DescentCeremonyWindow(DescentMigrationOffer offer)
        {
            _offer = offer ?? throw new ArgumentNullException(nameof(offer));

            InitializeComponent();

            // Both doors are priced BEFORE the user sees either, from the same pure function the
            // submit will use — so the number on the card is the number they get, not an estimate.
            _restoreLevel = DescentMigration
                .Resolve(DescentMigrationChoices.Restore, _offer).Level;

            PopulateCopy();

            Loaded += OnLoaded;
            Closed += OnClosedInternal;

            lock (Live) Live.Add(this);
        }

        // ------------------------------------------------------------------
        // Copy
        // ------------------------------------------------------------------

        private void PopulateCopy()
        {
            var currentLevel = App.Settings?.Current?.PlayerLevel ?? 1;

            IntroHeadline.Text = DescentCeremonyCopy.IntroHeadline;
            IntroStanding.Text = DescentCeremonyCopy.IntroStanding(
                currentLevel, _offer.TotalXpEarned, _offer.DevotionDays);
            IntroBody.Text = DescentCeremonyCopy.IntroBody;
            BtnIntroContinue.Content = DescentCeremonyCopy.IntroContinue;

            ChoiceHeadline.Text = DescentCeremonyCopy.ChoiceHeadline;

            RestoreTitle.Text = DescentCeremonyCopy.RestoreTitle;
            RestoreKicker.Text = DescentCeremonyCopy.RestoreKicker;
            RestoreDelta.Text = DescentCeremonyCopy.RestoreDelta(currentLevel, _restoreLevel);
            RestoreBody.Text = DescentCeremonyCopy.RestoreBody(currentLevel, _restoreLevel);
            BtnChooseRestore.Content = DescentCeremonyCopy.RestoreTitle;

            CycleTitle.Text = DescentCeremonyCopy.CycleTitle;
            CycleKicker.Text = DescentCeremonyCopy.CycleKicker;
            CycleBonus.Text = DescentCeremonyCopy.CycleBonusLine();
            CycleBody.Text = DescentCeremonyCopy.CycleBody();
            BtnChooseCycle.Content = DescentCeremonyCopy.CycleTitle;

            BothDoorsFooter.Text = DescentCeremonyCopy.BothDoorsFooter;

            ConfirmHeadline.Text = DescentCeremonyCopy.ConfirmHeadline;
            BtnConfirmBack.Content = DescentCeremonyCopy.ConfirmBack;

            DoneHeadline.Text = DescentCeremonyCopy.DoneHeadline;
            BtnDoneClose.Content = DescentCeremonyCopy.DoneClose;

            BtnLater.Content = DescentCeremonyCopy.Later;
            LaterHint.Text = DescentCeremonyCopy.LaterHint;
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // A slow breath under the whole thing. Opacity only — no layout, no layered
                // window, nothing the render thread can trip over.
                var breath = new DoubleAnimation(0.22, 0.42, TimeSpan.FromSeconds(5.5))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                GlowLayer.BeginAnimation(OpacityProperty, breath);
            }
            catch (Exception ex)
            {
                Log.Debug("[Descent] Ceremony backdrop animation skipped: {Error}", ex.Message);
            }

            Say(DescentCeremonyCopy.CompanionIntro);
        }

        /// <summary>
        /// Hand the act's Viewbox a height budget, which is the one thing XAML cannot state on its
        /// own: inside a vertical ScrollViewer the Viewbox is measured against infinity, so without
        /// a MaxHeight it would never scale anything down and would scroll instead every time.
        ///
        /// <para>The budget is the viewport, or <see cref="ActMinHeight"/> when the viewport is
        /// smaller than that - and that floor is what hands the overflow back to the scroller.</para>
        ///
        /// <para>It reads the scroller's own height and not its ViewportHeight, which is still 0
        /// the first time this fires and never changes again on a window that cannot be resized:
        /// reading it there left the Viewbox at MaxHeight=Infinity for the life of the ceremony,
        /// which is to say scaled by width alone, which is to say the bug was still there.</para>
        /// </summary>
        private void OnActScrollerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var viewport = e.NewSize.Height;
            if (viewport <= 0) return;

            var budget = Math.Max(ActMinHeight, viewport);

            // Only on a real change: assigning MaxHeight re-measures, and a same-value write on
            // every SizeChanged is a layout pass nobody asked for.
            if (Math.Abs(ActBox.MaxHeight - budget) > 0.5) ActBox.MaxHeight = budget;
        }

        private void OnClosedInternal(object? sender, EventArgs e)
        {
            lock (Live) Live.Remove(this);

            try { GlowLayer.BeginAnimation(OpacityProperty, null); }
            catch (Exception ex) { Log.Debug("[Descent] Ceremony animation teardown: {Error}", ex.Message); }
        }

        /// <summary>
        /// Clear the ceremony off the screen for the panic path. Safe at any act: nothing here is
        /// lost by closing, because the ceremony is only "taken" once <see cref="_committed"/> —
        /// and once it is, the choice is already on disk and riding the sync queue.
        /// </summary>
        public static void ForceCloseAll()
        {
            List<DescentCeremonyWindow> snapshot;
            lock (Live) snapshot = new List<DescentCeremonyWindow>(Live);

            foreach (var w in snapshot)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("[Descent] Ceremony force-close failed: {Error}", ex.Message); }
            }
        }

        // ------------------------------------------------------------------
        // Acts
        // ------------------------------------------------------------------

        private void ShowAct(UIElement act)
        {
            ActIntro.Visibility = ReferenceEquals(act, ActIntro) ? Visibility.Visible : Visibility.Collapsed;
            ActChoice.Visibility = ReferenceEquals(act, ActChoice) ? Visibility.Visible : Visibility.Collapsed;
            ActConfirm.Visibility = ReferenceEquals(act, ActConfirm) ? Visibility.Visible : Visibility.Collapsed;
            ActDone.Visibility = ReferenceEquals(act, ActDone) ? Visibility.Visible : Visibility.Collapsed;

            // The way out disappears once the choice is taken — there is nothing left to defer.
            LaterHost.Visibility = ReferenceEquals(act, ActDone) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnIntroContinue(object sender, RoutedEventArgs e) => ShowAct(ActChoice);

        private void OnChooseRestore(object sender, RoutedEventArgs e) => AskToConfirm(DescentMigrationChoices.Restore);

        private void OnChooseCycle(object sender, RoutedEventArgs e) => AskToConfirm(DescentMigrationChoices.Cycle);

        private void AskToConfirm(string choice)
        {
            _pendingChoice = choice;
            ConfirmBody.Text = DescentCeremonyCopy.ConfirmBody(choice);
            BtnConfirmYes.Content = DescentCeremonyCopy.ConfirmYes(choice);
            ConfirmError.Visibility = Visibility.Collapsed;
            ShowAct(ActConfirm);
        }

        private void OnConfirmBack(object sender, RoutedEventArgs e)
        {
            _pendingChoice = null;
            ShowAct(ActChoice);
        }

        private void OnConfirmYes(object sender, RoutedEventArgs e)
        {
            if (_committed) return;

            var choice = _pendingChoice;
            if (!DescentMigrationChoices.IsValid(choice))
            {
                ShowAct(ActChoice);
                return;
            }

            // Everything irreversible happens inside ApplyChoice: the local relevel, the epoch
            // flip, the keepsakes, and arming the submit. It returns false only for states this
            // window should never have reached (already migrated, no settings) — in which case
            // say so plainly rather than pretending it worked.
            bool applied;
            try
            {
                applied = App.DescentMigration?.ApplyChoice(choice!, _offer) == true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Descent] ApplyChoice threw during the ceremony.");
                applied = false;
            }

            if (!applied)
            {
                ConfirmError.Text = "That didn't take. Nothing has changed — close this and it will be offered again.";
                ConfirmError.Visibility = Visibility.Visible;
                return;
            }

            _committed = true;

            var level = App.Settings?.Current?.PlayerLevel ?? 1;
            DoneBody.Text = DescentCeremonyCopy.DoneBody(choice!, level);
            ShowAct(ActDone);

            Say(DescentCeremonyCopy.CompanionDone(choice!));
        }

        private void OnDoneClose(object sender, RoutedEventArgs e) => Close();

        private void OnLater(object sender, RoutedEventArgs e)
        {
            // Nothing is written here on purpose. The service's Closed handler is what arms the
            // session deferral (#1111) — it has to, because the chrome's X and the panic key close
            // this window without ever reaching "Not tonight".
            Log.Information("[Descent] Ceremony deferred by the user — nothing written, the server will re-offer on the next launch.");
            Close();
        }

        // ------------------------------------------------------------------
        // Companion
        // ------------------------------------------------------------------

        /// <summary>
        /// One scripted line, spoken by whoever the active mod's companion is.
        /// <c>aiGenerated: false</c> keeps the AI badge off and stops the line opening a
        /// chat-suppression window; <c>playSound: false</c> because there is no voiced clip for
        /// these beats yet — the bark lane can add <c>descent_ceremony_open</c> /
        /// <c>descent_ceremony_done</c> rules later and this becomes a PickVoiceLine call.
        /// </summary>
        private static void Say(string line)
        {
            try
            {
                App.AvatarWindow?.GigglePriority(line, playSound: false, aiGenerated: false);
            }
            catch (Exception ex)
            {
                Log.Debug("[Descent] Companion line skipped: {Error}", ex.Message);
            }
        }
    }
}
