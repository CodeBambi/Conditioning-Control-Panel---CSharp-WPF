using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// THE MIGRATION CEREMONY (CONTRACTS-0812 §4, design doc §6). Four acts, one question, asked
    /// once in the life of an account: intro → the two doors → a plain-language one-way confirm →
    /// the close.
    ///
    /// <para><b>This window never opens on its own.</b> Its only public constructor takes an
    /// <see cref="Offer"/>, and the only thing in the app that can build one is ProfileSyncService
    /// parsing <c>descent_migration.required</c> off a sync response. No menu item reaches it, no
    /// setting reveals it, no flag turns it on.</para>
    ///
    /// <para><b>No offers, ever</b> (CONTRACTS §0.6). Nothing on this surface may mention a price,
    /// a tier, a pack or an upgrade — not on the closing act, not on a shelf, not anywhere.</para>
    ///
    /// <para><b>Escape is not handled here, on purpose.</b> Escape is the app's panic key and it is
    /// delivered by a global hook regardless of focus; swallowing it would put a fullscreen topmost
    /// window between a user and the one key they are promised always works. The way out is the
    /// "Not tonight" button, and taking it costs nothing.</para>
    ///
    /// PORTED from ConditioningControlPanel/Windows/DescentCeremonyWindow.xaml.cs. Deviations:
    ///  - <c>DescentMigrationOffer</c>, <c>DescentMigrationChoices</c>, <c>DescentMigration</c> and
    ///    <c>DescentCeremonyCopy</c> are all still in the WPF head, and this project may not
    ///    reference it, so <see cref="Offer"/>, <see cref="Choices"/> and <see cref="Copy"/> below
    ///    are local stand-ins carrying the fields and strings this window actually reads. The copy
    ///    is placeholder — the real sentences come back when DescentCeremonyCopy moves to Core.
    ///  - <c>App.Settings</c>, <c>App.DescentMigration.ApplyChoice</c> and
    ///    <c>App.AvatarWindow.GigglePriority</c> are stubs for the same reason.
    ///  - The GlowLayer breath is a declarative Animation in the XAML, so the <c>Closed</c> teardown
    ///    of it disappears; <c>OnLoaded</c> shrinks to the companion's opening line, which it also
    ///    spoke in the original.
    ///  - <c>SizeChanged</c> is wired in the constructor rather than from markup (Avalonia's
    ///    SizeChangedEventArgs is the same shape), and reads <c>e.NewSize.Height</c> exactly as
    ///    the original did.
    ///  - <c>Visibility</c> → <c>IsVisible</c> throughout.
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
        /// floor is a little under half scale — and the machines that reach it are running 250% to
        /// 300% scaling, where half scale is still larger on the glass than the design is at 100%.
        /// </summary>
        private const double ActMinHeight = 290;

        private readonly Offer _offer;
        private readonly int _restoreLevel;
        private string? _pendingChoice;
        private bool _committed;

        private readonly ScrollViewer _actScroller;
        private readonly Viewbox _actBox;
        private readonly StackPanel _actIntro;
        private readonly Grid _actChoice;
        private readonly StackPanel _actConfirm;
        private readonly StackPanel _actDone;
        private readonly StackPanel _laterHost;
        private readonly TextBlock _confirmBody;
        private readonly TextBlock _confirmError;
        private readonly TextBlock _doneBody;
        private readonly Button _btnConfirmYes;

        /// <summary>Render constructor: sample offer, so --render-all can discover the window.</summary>
        internal DescentCeremonyWindow() : this(Offer.Sample()) { }

        public DescentCeremonyWindow(Offer offer)
        {
            _offer = offer ?? throw new ArgumentNullException(nameof(offer));

            AvaloniaXamlLoader.Load(this);

            _actScroller = this.FindControl<ScrollViewer>("ActScroller")!;
            _actBox = this.FindControl<Viewbox>("ActBox")!;
            _actIntro = this.FindControl<StackPanel>("ActIntro")!;
            _actChoice = this.FindControl<Grid>("ActChoice")!;
            _actConfirm = this.FindControl<StackPanel>("ActConfirm")!;
            _actDone = this.FindControl<StackPanel>("ActDone")!;
            _laterHost = this.FindControl<StackPanel>("LaterHost")!;
            _confirmBody = this.FindControl<TextBlock>("ConfirmBody")!;
            _confirmError = this.FindControl<TextBlock>("ConfirmError")!;
            _doneBody = this.FindControl<TextBlock>("DoneBody")!;
            _btnConfirmYes = this.FindControl<Button>("BtnConfirmYes")!;

            // Both doors are priced BEFORE the user sees either, from the same pure function the
            // submit will use — so the number on the card is the number they get, not an estimate.
            // ponytail: needs DescentMigration.Resolve (WPF head, Services/Descent), wired when it
            // moves to Core. The sample offer carries the level the resolve would have produced.
            _restoreLevel = _offer.ResolvedRestoreLevel;

            PopulateCopy();

            this.FindControl<Button>("BtnIntroContinue")!.Click += (_, _) => ShowAct(_actChoice);
            this.FindControl<Button>("BtnChooseRestore")!.Click += (_, _) => AskToConfirm(Choices.Restore);
            this.FindControl<Button>("BtnChooseCycle")!.Click += (_, _) => AskToConfirm(Choices.Cycle);
            this.FindControl<Button>("BtnConfirmBack")!.Click += (_, _) => { _pendingChoice = null; ShowAct(_actChoice); };
            _btnConfirmYes.Click += (_, _) => OnConfirmYes();
            this.FindControl<Button>("BtnDoneClose")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnLater")!.Click += (_, _) => OnLater();

            _actScroller.SizeChanged += OnActScrollerSizeChanged;

            // All that survives of WPF's OnLoaded: the glow breath is declarative in the XAML now,
            // but the companion's opening line still belongs to the moment the ceremony appears.
            Loaded += (_, _) => Say(Copy.CompanionIntro);

            Closed += OnClosedInternal;

            lock (Live) Live.Add(this);
        }

        // ------------------------------------------------------------------
        // Copy
        // ------------------------------------------------------------------

        private void PopulateCopy()
        {
            // ponytail: needs App.Settings.Current.PlayerLevel, wired when settings move to Core.
            var currentLevel = _offer.PlayerLevel;

            this.FindControl<TextBlock>("IntroHeadline")!.Text = Copy.IntroHeadline;
            this.FindControl<TextBlock>("IntroStanding")!.Text = Copy.IntroStanding(
                currentLevel, _offer.TotalXpEarned, _offer.DevotionDays);
            this.FindControl<TextBlock>("IntroBody")!.Text = Copy.IntroBody;
            this.FindControl<Button>("BtnIntroContinue")!.Content = Copy.IntroContinue;

            this.FindControl<TextBlock>("ChoiceHeadline")!.Text = Copy.ChoiceHeadline;

            this.FindControl<TextBlock>("RestoreTitle")!.Text = Copy.RestoreTitle;
            this.FindControl<TextBlock>("RestoreKicker")!.Text = Copy.RestoreKicker;
            this.FindControl<TextBlock>("RestoreDelta")!.Text = Copy.RestoreDelta(currentLevel, _restoreLevel);
            this.FindControl<TextBlock>("RestoreBody")!.Text = Copy.RestoreBody(currentLevel, _restoreLevel);
            this.FindControl<Button>("BtnChooseRestore")!.Content = Copy.RestoreTitle;

            this.FindControl<TextBlock>("CycleTitle")!.Text = Copy.CycleTitle;
            this.FindControl<TextBlock>("CycleKicker")!.Text = Copy.CycleKicker;
            this.FindControl<TextBlock>("CycleBonus")!.Text = Copy.CycleBonusLine();
            this.FindControl<TextBlock>("CycleBody")!.Text = Copy.CycleBody();
            this.FindControl<Button>("BtnChooseCycle")!.Content = Copy.CycleTitle;

            this.FindControl<TextBlock>("BothDoorsFooter")!.Text = Copy.BothDoorsFooter;

            this.FindControl<TextBlock>("ConfirmHeadline")!.Text = Copy.ConfirmHeadline;
            this.FindControl<Button>("BtnConfirmBack")!.Content = Copy.ConfirmBack;

            this.FindControl<TextBlock>("DoneHeadline")!.Text = Copy.DoneHeadline;
            this.FindControl<Button>("BtnDoneClose")!.Content = Copy.DoneClose;

            this.FindControl<Button>("BtnLater")!.Content = Copy.Later;
            this.FindControl<TextBlock>("LaterHint")!.Text = Copy.LaterHint;
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        /// <summary>
        /// Hand the act's Viewbox a height budget, which is the one thing XAML cannot state on its
        /// own: inside a vertical ScrollViewer the Viewbox is measured against infinity, so without
        /// a MaxHeight it would never scale anything down and would scroll instead every time.
        ///
        /// <para>The budget is the viewport, or <see cref="ActMinHeight"/> when the viewport is
        /// smaller than that — and that floor is what hands the overflow back to the scroller.</para>
        /// </summary>
        private void OnActScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var viewport = e.NewSize.Height;
            if (viewport <= 0) return;

            var budget = Math.Max(ActMinHeight, viewport);

            // Only on a real change: assigning MaxHeight re-measures, and a same-value write on
            // every SizeChanged is a layout pass nobody asked for.
            if (Math.Abs(_actBox.MaxHeight - budget) > 0.5) _actBox.MaxHeight = budget;
        }

        private void OnClosedInternal(object? sender, EventArgs e)
        {
            lock (Live) Live.Remove(this);
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

        private void ShowAct(Control act)
        {
            _actIntro.IsVisible = ReferenceEquals(act, _actIntro);
            _actChoice.IsVisible = ReferenceEquals(act, _actChoice);
            _actConfirm.IsVisible = ReferenceEquals(act, _actConfirm);
            _actDone.IsVisible = ReferenceEquals(act, _actDone);

            // The way out disappears once the choice is taken — there is nothing left to defer.
            _laterHost.IsVisible = !ReferenceEquals(act, _actDone);
        }

        private void AskToConfirm(string choice)
        {
            _pendingChoice = choice;
            _confirmBody.Text = Copy.ConfirmBody(choice);
            _btnConfirmYes.Content = Copy.ConfirmYes(choice);
            _confirmError.IsVisible = false;
            ShowAct(_actConfirm);
        }

        private void OnConfirmYes()
        {
            if (_committed) return;

            var choice = _pendingChoice;
            if (!Choices.IsValid(choice))
            {
                ShowAct(_actChoice);
                return;
            }

            // Everything irreversible happens inside ApplyChoice: the local relevel, the epoch
            // flip, the keepsakes, and arming the submit. It returns false only for states this
            // window should never have reached — in which case say so plainly rather than
            // pretending it worked.
            var applied = ApplyChoice(choice!);

            if (!applied)
            {
                _confirmError.Text = "That didn't take. Nothing has changed — close this and it will be offered again.";
                _confirmError.IsVisible = true;
                return;
            }

            _committed = true;

            var level = _offer.PlayerLevel;
            _doneBody.Text = Copy.DoneBody(choice!, level);
            ShowAct(_actDone);

            Say(Copy.CompanionDone(choice!));
        }

        /// <summary>
        /// Take the choice. Returns false until the migration service exists on this head, so the
        /// ceremony shows its honest "that didn't take" line rather than faking a one-way commit.
        /// </summary>
        private bool ApplyChoice(string choice)
        {
            // ponytail: needs App.DescentMigration.ApplyChoice(choice, offer), wired when
            // Services/Descent moves to Core. Never make this return true before it is real —
            // a faked commit on an irreversible choice is the one bug this surface cannot survive.
            Log.Warning("[Descent] ApplyChoice({Choice}) is not wired on this head — nothing was committed.", choice);
            return false;
        }

        private void OnLater()
        {
            // Nothing is written here on purpose. The service's Closed handler is what arms the
            // session deferral (#1111) — it has to, because the chrome's X and the panic key close
            // this window without ever reaching "Not tonight".
            Log.Information("[Descent] Ceremony deferred by the user — nothing written, the server will re-offer on the next launch.");
            Close();
        }

        /// <summary>
        /// One scripted line, spoken by whoever the active mod's companion is.
        /// </summary>
        private static void Say(string line)
        {
            // ponytail: needs App.AvatarWindow.GigglePriority, wired when the companion moves to Core.
            Log.Debug("[Descent] Companion line (not yet spoken on this head): {Line}", line);
        }

        // ------------------------------------------------------------------
        // Local stand-ins for the WPF head's Descent services
        // ------------------------------------------------------------------

        /// <summary>
        /// The fields of <c>DescentMigrationOffer</c> this window reads, plus the resolved restore
        /// level and the player level it took off App.Settings.
        /// ponytail: replaced by the real DescentMigrationOffer when Services/Descent moves to Core.
        /// </summary>
        public sealed class Offer
        {
            public double TotalXpEarned { get; init; }
            public int DevotionDays { get; init; }
            public int PlayerLevel { get; init; }
            public int ResolvedRestoreLevel { get; init; }

            /// <summary>Internal, so no production caller can ship the render sample.</summary>
            internal static Offer Sample() => new()
            {
                TotalXpEarned = 184_500,
                DevotionDays = 213,
                PlayerLevel = 47,
                ResolvedRestoreLevel = 38,
            };
        }

        /// <summary>ponytail: mirrors DescentMigrationChoices; the two literals are the wire values.</summary>
        private static class Choices
        {
            public const string Restore = "restore";
            public const string Cycle = "cycle";
            public static bool IsValid(string? choice) => choice == Restore || choice == Cycle;
        }

        /// <summary>
        /// PLACEHOLDER COPY. The real sentences live in DescentCeremonyCopy in the WPF head; the
        /// short constants are verbatim, the multi-paragraph bodies are stood in for so the acts
        /// render with real text rather than blank panels.
        /// ponytail: needs DescentCeremonyCopy, wired when it moves to Core — this class deletes
        /// itself entirely at that point.
        /// </summary>
        private static class Copy
        {
            /// <summary>ponytail: mirrors DescentMigration.CycleXpBonus (1.10).</summary>
            private const double CycleXpBonus = 1.10;

            public const string IntroHeadline = "The last season has ended.";

            public const string IntroBody =
                "Nothing was wiped. Nothing will be — this time the wipe simply doesn't happen.\n\n" +
                "But the ladder underneath you has been rebuilt, and it keeps getting steeper all " +
                "the way down. A level is going to mean more than it used to.\n\n" +
                "So you get to choose how you meet it. Once.";

            public const string IntroContinue = "Show me the two doors";

            public static string IntroStanding(int level, double lifetimeXp, int devotionDays) =>
                $"You stand at Level {level}  ·  {lifetimeXp.ToString("N0", CultureInfo.CurrentCulture)} lifetime XP  ·  " +
                $"{devotionDays} {(devotionDays == 1 ? "day" : "days")} of devotion";

            public const string ChoiceHeadline = "Two doors. Both of them go down.";

            public const string RestoreTitle = "Take it all back";
            public const string RestoreKicker = "Everything you built, re-measured.";

            public static string RestoreBody(int oldLevel, int newLevel) =>
                $"Your lifetime XP is spent again on the new ladder, and it buys you Level {newLevel}.\n\n" +
                "The number moves because the ladder moved — not because anything was taken. And " +
                "the stages you already climbed come back to you one per day.";

            public static string RestoreDelta(int oldLevel, int newLevel) =>
                oldLevel == newLevel
                    ? $"Level {oldLevel} → Level {newLevel}  (unchanged)"
                    : $"Level {oldLevel} → Level {newLevel}";

            public const string CycleTitle = "Descend again";
            public const string CycleKicker = "Cycle I. From the top.";

            public static string CycleBody() =>
                "Level 1. The whole fall again, first night to last, with everything you learned " +
                "the first time.\n\n" +
                "A Cycle wipes nothing but the number. What it adds is a permanent mark on your " +
                "card, and a bonus on every point of XP you earn from here on.";

            public static string CycleBonusLine() =>
                $"+{(CycleXpBonus - 1.0) * 100:0.#}% XP, permanently";

            public const string BothDoorsFooter =
                "Either way: the veteran badge and your keepsake archive are yours. And either way " +
                "the spiral starts tonight at Day 1. Nobody arrives with a track already lit. " +
                "We all fall together.";

            public const string ConfirmHeadline = "This is the part that doesn't come back.";

            public static string ConfirmBody(string choice) =>
                (choice == Choices.Cycle
                    ? "You are choosing to descend again. Your level goes to 1 tonight and stays " +
                      "there until you earn it back.\n\n"
                    : "You are choosing to take it all back. Your level is re-measured on the new " +
                      "ladder and the stages you climbed will be handed back one a day.\n\n") +
                "There is no undo. You get asked this once, and this is the once.\n\n" +
                "Take a breath first if you need one. The door will still be here.";

            public static string ConfirmYes(string choice) =>
                choice == Choices.Cycle ? "Yes. Take me back to Level 1." : "Yes. Give it back to me.";

            public const string ConfirmBack = "Not that one";

            public const string DoneHeadline = "It's done.";

            public static string DoneBody(string choice, int level) =>
                choice == Choices.Cycle
                    ? $"Cycle I. Level {level}.\n\nThe mark is on your card and it stays there. " +
                      "The spiral starts tonight, at Day 1 — the same Day 1 as everyone else."
                    : $"Level {level}, measured on the ladder you're actually standing on.\n\n" +
                      "The spiral starts tonight, at Day 1 — the same Day 1 as everyone else.";

            public const string DoneClose = "Begin";

            public const string Later = "Not tonight";

            public const string LaterHint =
                "Closing this changes nothing. The ceremony will be waiting the next time you sync.";

            public const string CompanionIntro = "Come here. Something is ending, and I want you with me for it.";

            public static string CompanionDone(string choice) =>
                choice == Choices.Cycle
                    ? "All the way back to the top with you. Good. I get to watch you fall all over again."
                    : "There you are. Same you, honest numbers. Let's keep going down.";
        }
    }
}
