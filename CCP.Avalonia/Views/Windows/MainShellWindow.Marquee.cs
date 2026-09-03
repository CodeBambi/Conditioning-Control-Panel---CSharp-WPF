// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Marquee.cs (1,170 lines) - the
// header banner's rotation, which is the part of that file this head can actually run.
//
// WHAT IS REAL HERE: the two-or-three beat rotation over the three TextBlocks
// MainShellWindow.axaml already carries (TxtBannerPrimary, TxtBannerSecondary, TxtBannerWeb).
// The beat array is built from CoreSettings (the One Account beat rides SeenFeatureIntros and
// drops out once spent), the welcome-back line is written from CoreSettings' offline name /
// display name, the 4s DispatcherTimer crossfades between beats, SetBannerAnnouncement replaces
// the welcome line, and RetireWebBannerBeat spends the key and hands the beat's slot back with
// the same 500ms fade rather than blinking empty.
//
// The crossfade is an Avalonia Transitions entry on Opacity, not a WPF DoubleAnimation:
// BeginAnimation/Storyboard have no twin here, and a DoubleTransition attached once and driven
// by plain Opacity writes is the same 500ms quadratic curve with none of the clock bookkeeping
// (no FillBehavior, no BeginAnimation(prop, null) afterwards to release the animated value).
//
// This partial owns OnLoaded on MainShellWindow. It has to: the window's constructor is in
// MainShellWindow.axaml.cs, which this layer does not own, so there is no other hook to start
// the rotation from. OnOpened is already taken by MainShellWindow.WorkAreaFit.cs.
//
// Beat text and {loc:Str}: TxtBannerSecondary carries {loc:Str} in the XAML, and writing .Text
// over a binding is undone on the next language change (the porting rule). The welcome line is
// a FORMATTED string (Loc.GetF with a name), so there is no key to bind instead - so
// UpdateBannerWelcomeMessage re-runs on LocalizationManager.LanguageChanged, which is what
// actually keeps it honest.
//
// STILL BLOCKED, each naming what it needs rather than guessing at "when X moves to Core":
//   ShowOgWelcomePopup / TryPresentSeasonRecap / ShowWhatsNewIfNeeded - App.Achievements,
//     App.Seasons and the startup dialog queue in ConditioningControlPanel/App.xaml.cs. The
//     dialogs themselves DO exist here (CCP.Avalonia/Views/Dialogs/WhatsNewDialog.axaml.cs,
//     Views/Controls/SeasonRecapCard); what is missing is the "has this launch already shown a
//     modal" arbitration those read from App.
//   RefreshMarqueeFromSettings / CheckServerUpdateBanner / CheckServerAnnouncement and their
//     three response DTOs (MarqueeResponse, UpdateBannerResponse, AnnouncementResponse), plus
//     _serverUpdateUrl and _serverAnnouncementShownThisLaunch - all three are HttpClient calls
//     to the CC Labs API. Networking is not a Core seam and this head has no API client.
//   CheckIntakePassNudge / StartIntakeFromNudge - App.Programs (the weekly intake pass); the
//     accepted nudge then wants ShowTab("gradedintake"), which does exist here.
//   BannerWebLink_Click - Helpers.BrowserLauncher (ConditioningControlPanel/Helpers/
//     BrowserLauncher.cs), a ShellExecute with a no-default-browser prompt. There is no browser
//     launcher on this head, so the URL renders as text (see the <Run> notes in
//     MainShellWindow.axaml) and only the Web App door and the one-account card can retire the
//     beat.
//   LocOr - a Loc.Get with an English literal fallback. Used only by the marquee and takeaway
//     formatters, neither of which runs here; Loc.Get already returns the key when a string is
//     missing, so there is nothing to restore in isolation.
//   StartMarqueeAnimation / UpdateMarqueeMessage - the scrolling marquee strip, a WPF Storyboard
//     over a TranslateTransform on a control MainShellWindow.axaml does not carry. Nothing to
//     drive: a still banner that says the right thing beats a faked scroll.
//   SweepBannerSheen - MainShellWindow.ChromeFx.cs, still a stub. The BannerSheen border is in
//     the XAML and parked at Opacity 0, so its absence costs nothing visible.

using System;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Seen-list key for the One Account banner beat (and the surfaces that retire it). It
        /// rides <see cref="Models.AppSettings.SeenFeatureIntros"/> rather than a new bool - the
        /// same registry the intro cards spend - but it is NOT a card key: FeatureIntros.All has
        /// no entry for it, so nothing can ever open a modal for it.
        /// </summary>
        internal const string WebBannerSeenKey = "banner-web";

        /// <summary>
        /// True while a startup modal owns the screen. FeatureIntroPopup reads this to refuse to
        /// stack a card on a modal. Nothing on this head SETS it yet - the startup dialog queue
        /// lives in App.xaml.cs - so it is a gate that is always open here; it exists so the
        /// readers compile against the real member rather than against nothing.
        /// </summary>
        public static bool IsStartupDialogShowing { get; set; }

        private const int BannerFadeMs = 500;
        private static readonly TimeSpan BannerRotationInterval = TimeSpan.FromSeconds(4);

        private DispatcherTimer? _bannerRotationTimer;
        private int _bannerCurrentIndex;          // 0 = Primary (support), 1 = Secondary (welcome)
        private TextBlock[] _bannerBeats = Array.Empty<TextBlock>();

        // Resolved through Named<T> on every read, never through a generated x:Name field: this
        // window loads with AvaloniaXamlLoader.Load, so its generated fields are permanently null.
        private TextBlock? BannerPrimary => Named<TextBlock>("TxtBannerPrimary");
        private TextBlock? BannerSecondary => Named<TextBlock>("TxtBannerSecondary");
        private TextBlock? BannerWeb => Named<TextBlock>("TxtBannerWeb");

        /// <summary>
        /// The only hook this layer has into the window's lifetime - see the header. Guarded as a
        /// whole: a banner that cannot build must never stop the shell from opening.
        /// </summary>
        protected override void OnLoaded(global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnLoaded(e);
            try
            {
                InitializeBannerRotation();
                UpdateQuickLoginUI();
                RefreshSessionFeatureLock();

                // Both of these write .Text over a control that carries {loc:Str}, which a
                // language change would otherwise undo - see the header and
                // MainShellWindow.Login.cs.
                LocalizationManager.Instance.LanguageChanged += (_, _) =>
                {
                    UpdateBannerWelcomeMessage();
                    UpdateQuickLoginUI();
                };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Shell OnLoaded (banner rotation / login state / session lock) failed");
            }
        }

        private void InitializeBannerRotation()
        {
            _bannerBeats = BuildBannerBeats();
            foreach (var beat in _bannerBeats) EnsureFadeTransition(beat);

            UpdateBannerWelcomeMessage();

            _bannerRotationTimer = new DispatcherTimer { Interval = BannerRotationInterval };
            _bannerRotationTimer.Tick += BannerRotationTimer_Tick;
            _bannerRotationTimer.Start();
        }

        /// <summary>
        /// Support + welcome-back always; the One Account beat only while unspent. The array is
        /// what the tick indexes, so a beat that is not in it simply never fades in - its
        /// TextBlock stays exactly as MainShellWindow.axaml authored it (Opacity 0, hit-test off).
        /// A beat the XAML does not carry is dropped rather than crashing the rotation.
        /// </summary>
        private TextBlock[] BuildBannerBeats()
        {
            var spent = CoreSettings.Current.SeenFeatureIntros.Contains(WebBannerSeenKey);
            var beats = spent
                ? new[] { BannerPrimary, BannerSecondary }
                : new[] { BannerPrimary, BannerSecondary, BannerWeb };
            return beats.Where(b => b is not null).Select(b => b!).ToArray();
        }

        /// <summary>
        /// One DoubleTransition on Opacity, attached once. Every later fade is then a plain
        /// Opacity write - which is why <see cref="RetireWebBannerBeat"/> needs no animation code
        /// of its own.
        /// </summary>
        private static void EnsureFadeTransition(TextBlock beat)
        {
            var transitions = beat.Transitions ??= new Transitions();
            foreach (var t in transitions)
                if (t is DoubleTransition dt && dt.Property == OpacityProperty) return;

            transitions.Add(new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(BannerFadeMs),
                Easing = new global::Avalonia.Animation.Easings.QuadraticEaseInOut(),
            });
        }

        private void UpdateBannerWelcomeMessage()
        {
            var secondary = BannerSecondary;
            if (secondary is null) return;

            var s = CoreSettings.Current;

            if (s.OfflineMode && !string.IsNullOrWhiteSpace(s.OfflineUsername))
            {
                secondary.Text = Loc.GetF("label_welcome_back_0_offline_mode", s.OfflineUsername);
                return;
            }

            // ponytail: WPF falls back to App.Patreon/App.Discord DisplayName when the unified
            // name is blank. Neither provider exists on this head, so a user who linked an
            // account but has no UserDisplayName written back gets the generic line.
            var displayName = s.UserDisplayName;
            secondary.Text = string.IsNullOrEmpty(displayName)
                ? Loc.Get("label_welcome_consider_logging_in_with_patreon_for")
                : Loc.GetF("label_welcome_back_0", displayName!);
        }

        private void BannerRotationTimer_Tick(object? sender, EventArgs e)
        {
            var banners = _bannerBeats;
            if (banners.Length < 2) return;
            if (_bannerCurrentIndex >= banners.Length) _bannerCurrentIndex = 0;

            var nextIndex = (_bannerCurrentIndex + 1) % banners.Length;
            Crossfade(banners[_bannerCurrentIndex], banners[nextIndex]);
            _bannerCurrentIndex = nextIndex;

            // ponytail: WPF also calls SweepBannerSheen() here. MainShellWindow.ChromeFx.cs is
            // still a stub, so the sheen pass is skipped rather than faked.
        }

        /// <summary>
        /// Hands the banner from one beat to the next. Hit-testing follows the fade rather than
        /// the opacity, for the reason WPF spells out: a link parked at Opacity 0 still eats
        /// clicks.
        /// </summary>
        private static void Crossfade(TextBlock outgoing, TextBlock incoming)
        {
            EnsureFadeTransition(outgoing);
            EnsureFadeTransition(incoming);

            outgoing.Opacity = 0;
            incoming.Opacity = 1;
            outgoing.IsHitTestVisible = false;
            incoming.IsHitTestVisible = true;
        }

        /// <summary>
        /// Set a temporary announcement message to display in the banner rotation. It takes the
        /// welcome beat's slot, exactly as in WPF, so the rotation length does not change.
        /// </summary>
        public void SetBannerAnnouncement(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            var secondary = BannerSecondary;
            if (secondary is not null) secondary.Text = message;

            if (_bannerRotationTimer is { IsEnabled: false }) _bannerRotationTimer.Start();
        }

        /// <summary>
        /// Spends the One Account banner beat and takes it out of the live rotation. Called from
        /// every surface that counts as "the nudge worked". Idempotent - once the key is spent
        /// the rebuild is a two-beat array either way.
        ///
        /// <para>If the beat is on screen at the moment it is retired, it hands its slot to the
        /// support beat with the same fade the rotation uses, so the banner never blinks empty.
        /// Any other beat on screen keeps its turn: the index is re-found in the rebuilt array
        /// rather than reset.</para>
        /// </summary>
        internal void RetireWebBannerBeat()
        {
            try
            {
                var settings = CoreSettings.Current;
                if (!settings.SeenFeatureIntros.Contains(WebBannerSeenKey))
                {
                    settings.SeenFeatureIntros.Add(WebBannerSeenKey);
                    CoreSettings.Save();
                }

                var web = BannerWeb;
                if (web is null || Array.IndexOf(_bannerBeats, web) < 0) return;

                var current = _bannerCurrentIndex < _bannerBeats.Length
                    ? _bannerBeats[_bannerCurrentIndex]
                    : BannerPrimary;
                _bannerBeats = BuildBannerBeats();

                if (ReferenceEquals(current, web))
                {
                    _bannerCurrentIndex = 0;
                    var primary = BannerPrimary;
                    if (primary is not null) Crossfade(web, primary);
                }
                else if (current is not null)
                {
                    var idx = Array.IndexOf(_bannerBeats, current);
                    _bannerCurrentIndex = idx >= 0 ? idx : 0;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RetireWebBannerBeat failed; the beat rotates on until next launch");
            }
        }
    }
}
