using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// What a one-shot feature intro card says. Copy is hardcoded English on purpose, matching
    /// ProgramsIntroPopup and the other one-shot popups (see the note there).
    /// </summary>
    public sealed class FeatureIntroContent
    {
        public string Key { get; init; } = "";
        public string Glyph { get; init; } = "✨";
        public string RailTitle { get; init; } = "";
        public string Title { get; init; } = "";
        public string Tagline { get; init; } = "";
        public string[] Bullets { get; init; } = Array.Empty<string>();
        public string? Footer { get; init; }
        /// <summary>Accent hex; falls back to app pink when unset or invalid.</summary>
        public string? Accent { get; init; }
        public string DismissLabel { get; init; } = "Got it";

        /// <summary>
        /// Optional CTA (v6.8.0, built for the one-account card). Both must be set or the card
        /// renders exactly as it always did - one dismiss button. When present, the CTA takes
        /// the accent fill and the dismiss button steps back to a quiet ghost, and the action
        /// runs AFTER the modal unwinds (the WhatsNewDialog tour-button pattern), never inside
        /// the card's own message loop.
        /// </summary>
        public string? ActionLabel { get; init; }
        public Action? OnAction { get; init; }
    }

    /// <summary>
    /// One reusable first-open explainer card, generalizing what ProgramsIntroPopup does for the
    /// Programs tab: shown once per install per feature key, dressed with a glyph rail instead of
    /// per-feature bitmaps. All the hardening lessons from that popup are kept: the seen-flag is
    /// spent at open time (not queue time), the open is deferred at Normal priority (Loaded is
    /// starved in this app), and every path is guarded so an explainer can never be the reason a
    /// tab fails to open.
    /// </summary>
    public partial class FeatureIntroPopup : Window
    {
        private Action? _onAction;

        private FeatureIntroPopup(FeatureIntroContent content)
        {
            InitializeComponent();
            ApplyContent(content);
        }

        /// <summary>Set between queueing a card and opening it, so a double-click queues one card.</summary>
        private static bool _opening;

        /// <summary>
        /// Pacing gate: a curious first-day user clicking through every tab must not eat a modal
        /// per click. A card suppressed by pacing is NOT spent - it shows on a later visit.
        /// </summary>
        private static DateTime _lastShownUtc = DateTime.MinValue;
        private static readonly TimeSpan ShowCooldown = TimeSpan.FromMinutes(10);

        /// <summary>Seen-list key for the one-time premium celebration card.</summary>
        internal const string CelebrationKey = "premium-celebration";

        /// <summary>
        /// Phase 8: the doors each own more than one card (Companion has awareness + she's
        /// listening, Play has lockdown + blink trainer), so "one card per first door visit" cannot
        /// be a pure rename of the per-tab trigger. This is the budget instead - the FIRST card a
        /// door produces in a launch is the only one that door produces, and the sibling shows on a
        /// later launch's first visit. Session-local on purpose: it is a pacing rule, not a
        /// seen-flag, so nothing is ever permanently lost and no settings property is needed.
        /// Claimed at OPEN time (next to the seen-flag spend), never at queue time - a card
        /// suppressed by pacing must leave the door's slot for the card that actually shows.
        /// </summary>
        private static readonly HashSet<string> _doorSlotsSpentThisLaunch =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Shows the intro for <paramref name="key"/> once per install, then never again.
        /// Silently does nothing while the guided tour is active (the tour navigates tabs through
        /// ShowTab, and a modal would ambush it), within the pacing cooldown of another card, or
        /// when <paramref name="doorKey"/>'s door has already shown a card this launch.
        /// </summary>
        /// <param name="doorKey">
        /// The sidebar door that owns this card's tab, or null to opt out of the per-door budget.
        /// </param>
        internal static void ShowIfFirstTime(string key, Window? owner, string? doorKey = null) =>
            ShowCore(key, owner, paced: true, doorKey: doorKey);

        /// <summary>
        /// The premium celebration rides the same card and seen-list but skips the pacing
        /// cooldown: it fires at most once per install and has retry points on every launch,
        /// so suppressing it (unspent) is always safe and delaying it never is. It belongs to no
        /// door, so it neither claims nor is blocked by the per-door budget.
        /// </summary>
        internal static void ShowCelebrationIfFirstTime(Window? owner) =>
            ShowCore(CelebrationKey, owner, paced: false, doorKey: null);

        /// <summary>Keys with a settle timer already running, so a tab that is shown twice during
        /// startup arms one clock and not two.</summary>
        private static readonly HashSet<string> _settling = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// For the ONE card whose surface the app lands on by itself: the Dashboard is visible
        /// from XAML before anything navigates, so its card has no ShowTab case to ride and would
        /// otherwise open in the middle of the startup ladder - update dialog, What's New, season
        /// recap, the first-run wizard, then the guided tour.
        ///
        /// <para>So it waits. A 1s clock re-tests the same conditions App.xaml.cs waits on before
        /// its own startup offers, calls <see cref="ShowCore"/> once they are clear, and keeps
        /// ticking until the card is actually spent - which also lets it outlast the 10-minute
        /// pacing cooldown if another card got in first. Gives up after five minutes (the wizard
        /// can legitimately take that long) leaving the seen-flag UNSPENT, so the next launch
        /// simply tries again.</para>
        /// </summary>
        internal static void ShowWhenStartupSettles(string key, Window? owner, string? doorKey)
        {
            try
            {
                if (!FeatureIntros.All.ContainsKey(key)) return;
                if (App.Settings?.Current?.SeenFeatureIntros.Contains(key) == true) return;
                if (!_settling.Add(key)) return;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) { _settling.Remove(key); return; }

                int ticks = 0;
                var timer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(1),
                };
                timer.Tick += (_, _) =>
                {
                    try
                    {
                        var live = App.Settings?.Current;
                        if (live == null || live.SeenFeatureIntros.Contains(key) || ++ticks > 300)
                        {
                            timer.Stop();
                            _settling.Remove(key);
                            return;
                        }

                        // Same three gates the startup ladder uses elsewhere. The tutorial is
                        // also checked inside ShowCore; it is repeated here so a tour that runs
                        // long simply keeps the clock ticking instead of burning a try.
                        if (App.IsUpdateDialogActive
                            || MainWindow.IsStartupDialogShowing
                            || App.Tutorial?.IsActive == true) return;

                        ShowCore(key, owner, paced: true, doorKey: doorKey);
                    }
                    catch (Exception ex)
                    {
                        timer.Stop();
                        _settling.Remove(key);
                        App.Logger?.Warning(ex, "Feature intro settle tick failed for {Key}", key);
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                _settling.Remove(key);
                App.Logger?.Warning(ex, "Feature intro settle gate failed for {Key}", key);
            }
        }

        private static void ShowCore(string key, Window? owner, bool paced, string? doorKey)
        {
            try
            {
                if (!FeatureIntros.All.TryGetValue(key, out var content)) return;

                var settings = App.Settings?.Current;
                if (settings == null || settings.SeenFeatureIntros.Contains(key) || _opening) return;

                if (App.Tutorial?.IsActive == true) return;
                if (paced && DateTime.UtcNow - _lastShownUtc < ShowCooldown) return;
                if (doorKey != null && _doorSlotsSpentThisLaunch.Contains(doorKey)) return;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                _opening = true;
                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                {
                    try
                    {
                        var live = App.Settings?.Current;
                        if (live == null || live.SeenFeatureIntros.Contains(key)) return;

                        // Never stack a modal on a modal. Written for the celebration, which is
                        // queued during startup where the What's New and update dialogs also
                        // live; kept for EVERY key since v6.8.0, because the Dashboard's card is
                        // queued from a tab that is already visible at launch (see
                        // ShowWhenStartupSettles). Yielding here costs nothing: the flag is spent
                        // below this line, so a suppressed card is retried on a later visit.
                        if (App.IsUpdateDialogActive || MainWindow.IsStartupDialogShowing) return;

                        var popup = new FeatureIntroPopup(content);
                        if (owner != null && owner.IsLoaded) popup.Owner = owner;

                        // Constructed without throwing, so the card is going to be shown - spend
                        // the flag now, so being killed while it is on screen still counts as seen.
                        live.SeenFeatureIntros.Add(key);
                        App.Settings?.Save();
                        _lastShownUtc = DateTime.UtcNow;
                        // ...and claim this door's one slot for the launch, here rather than at
                        // queue time so a card that never opened cannot spend its sibling's turn.
                        if (doorKey != null) _doorSlotsSpentThisLaunch.Add(doorKey);

                        popup.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Feature intro popup failed to show for {Key}", key);
                    }
                    finally
                    {
                        _opening = false;
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Feature intro gate failed for {Key}", key);
            }
        }

        private void ApplyContent(FeatureIntroContent content)
        {
            Title = content.Title;
            TxtTitle.Text = content.Title;
            TxtTagline.Text = content.Tagline;
            RailGlyph.Text = content.Glyph;
            TxtRailTitle.Text = content.RailTitle;
            BtnDismiss.Content = content.DismissLabel;

            TxtFooter.Text = content.Footer ?? "";
            TxtFooter.Visibility = string.IsNullOrWhiteSpace(content.Footer)
                ? Visibility.Collapsed
                : Visibility.Visible;

            // Optional CTA: only when both halves are provided. The CTA takes the accent fill
            // below; the dismiss button steps back so the card has one obvious action.
            var hasAction = !string.IsNullOrWhiteSpace(content.ActionLabel) && content.OnAction != null;
            if (hasAction)
            {
                _onAction = content.OnAction;
                BtnAction.Content = content.ActionLabel;
                BtnAction.Visibility = Visibility.Visible;
            }

            var accent = AccentBrush(content.Accent);
            try
            {
                CardBorder.BorderBrush = accent;
                TxtTitle.Foreground = accent;
                TxtRailTitle.Foreground = accent;
                RailGlow.Fill = GlowBrush(accent);
                if (accent is SolidColorBrush solid)
                {
                    CardShadow.Color = solid.Color;
                    RailSeam.Fill = new SolidColorBrush(
                        Color.FromArgb(0x55, solid.Color.R, solid.Color.G, solid.Color.B));
                    if (hasAction)
                    {
                        BtnAction.Background = accent;
                        // Quiet ghost: readable, clearly secondary, still themed by the card.
                        BtnDismiss.Background = new SolidColorBrush(
                            Color.FromArgb(0x30, solid.Color.R, solid.Color.G, solid.Color.B));
                    }
                    else
                    {
                        BtnDismiss.Background = accent;
                    }
                }
            }
            catch { /* accent dressing is decoration - the pink defaults stand */ }

            BuildBullets(content.Bullets, accent);
        }

        /// <summary>
        /// Same accent-glyph row layout ProgramsIntroPopup authors statically, built here from
        /// content so one XAML card serves every feature.
        /// </summary>
        private void BuildBullets(string[] bullets, Brush accent)
        {
            BulletsHost.Children.Clear();
            foreach (var text in bullets)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new TextBlock
                {
                    Text = "●",
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 5, 9, 0),
                    Foreground = accent
                };
                row.Children.Add(dot);

                var body = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 13.5,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                };
                Grid.SetColumn(body, 1);
                row.Children.Add(body);

                BulletsHost.Children.Add(row);
            }
        }

        private static Brush AccentBrush(string? hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    return brush;
                }
            }
            catch { /* a bad accent must never break the card */ }

            return Application.Current?.TryFindResource("PinkBrush") as Brush ?? Brushes.HotPink;
        }

        private static Brush GlowBrush(Brush accent)
        {
            try
            {
                if (accent is SolidColorBrush solid)
                {
                    var c = solid.Color;
                    var brush = new RadialGradientBrush(
                        Color.FromArgb(90, c.R, c.G, c.B),
                        Color.FromArgb(0, c.R, c.G, c.B))
                    {
                        Center = new Point(0.5, 0.42),
                        GradientOrigin = new Point(0.5, 0.42),
                        RadiusX = 0.7,
                        RadiusY = 0.7
                    };
                    brush.Freeze();
                    return brush;
                }
            }
            catch { /* a glow failing must never break the card */ }

            return Brushes.Transparent;
        }

        private void BtnDismiss_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }
        }

        /// <summary>
        /// The optional CTA. QUEUED, never called inline - Close() only starts the modal unwind,
        /// and the action (typically a browser launch, possibly ending in a fallback MessageBox)
        /// must run after ShowDialog() has returned to whoever opened this card. Same pattern,
        /// same reason, as WhatsNewDialog.BtnTour_Click.
        /// </summary>
        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }

            var action = _onAction;
            if (action == null) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { action(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Feature intro CTA threw"); }
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            try { Close(); } catch { }
        }

        /// <summary>Chromeless window, so dragging the card is the only way to move it.</summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }
    }

    /// <summary>
    /// The intro card registry. Adding an intro for a new feature is one entry here plus one
    /// MaybeShowFeatureIntro call at its ShowTab case - no new settings property, no new XAML.
    /// Cards deliberately show for free users too: the card explains what the gated tab IS,
    /// which the premium overlay alone never does.
    /// </summary>
    internal static class FeatureIntros
    {
        public static readonly Dictionary<string, FeatureIntroContent> All = new(StringComparer.OrdinalIgnoreCase)
        {
            ["awareness"] = new FeatureIntroContent
            {
                Key = "awareness",
                Glyph = "👁️",
                RailTitle = "Awareness Engine",
                Title = "👁️  The Awareness Engine",
                Tagline = "She notices your words - on screen, and as you type.",
                Accent = "#64C8FF",
                Bullets = new[]
                {
                    "Choose trigger words, and she watches for them in what's on your screen and what you type.",
                    "When one appears she can respond: a highlight, a sound, a flash, a spoken comment, haptics, a little XP.",
                    "One-click starter packs install a themed set of triggers - or build your own, word by word.",
                    "Cooldowns and loop protection keep it gentle, and the live feed shows you everything she noticed."
                },
                Footer = "Premium feature. The master switch stops screen reading and keyboard capture completely, any time."
            },

            ["shelistening"] = new FeatureIntroContent
            {
                Key = "shelistening",
                Glyph = "🎙️",
                RailTitle = "She's Listening",
                Title = "🎙️  She's Listening",
                Tagline = "Voice control that never leaves your PC.",
                Accent = "#B478FF",
                Bullets = new[]
                {
                    "Say the wake word - or hold push-to-talk - then speak: start effects, pause a session, ask for a mantra.",
                    "Your safety word stops every effect, instantly, always.",
                    "Everything runs offline on your machine. No audio is stored or uploaded, ever.",
                    "Nothing arms until you give mic consent - and one button revokes it all just as fast."
                },
                Footer = "Premium feature. Needs a microphone; the title-bar pill always shows when she's listening."
            },

            ["blinktrainer"] = new FeatureIntroContent
            {
                Key = "blinktrainer",
                Glyph = "😉",
                RailTitle = "Blink Trainer",
                Title = "😉  Blink Trainer",
                Tagline = "Every blink changes what you see.",
                Accent = "#FF69B4",
                Bullets = new[]
                {
                    "Your webcam watches for blinks - each one swaps the full-screen overlay to something new.",
                    "It draws from your own folders of images, GIFs and videos. Add as many as you like.",
                    "You set the duration and the overlay opacity; it ends itself when time is up.",
                    "Webcam consent comes first, and the demo stage below works before anything is armed."
                },
                Footer = "Premium feature (beta). Live mode needs webcam consent and at least one folder."
            },

            ["lockdown"] = new FeatureIntroContent
            {
                Key = "lockdown",
                Glyph = "🔒",
                RailTitle = "Lockdown",
                Title = "🔒  Lockdown",
                Tagline = "Lock yourself in - for as long as you dare.",
                Accent = "#FF4D6D",
                Bullets = new[]
                {
                    "Pick a duration and commit: the Safeties on the card decide what gets taken away until the timer runs out.",
                    "From five minutes to four hours. The countdown is all you'll see.",
                    "Possession is switched on by default: while the timer runs, the app's own UI misbehaves on purpose. Ember glow means it was Lockdown, never a bug.",
                    "If the app crashes mid-lockdown, your real settings are restored on the next launch - nothing stays stuck.",
                    "And if you truly need out early... the timer keeps a secret. Ask it nicely."
                },
                Footer = "Premium feature. Sitting through a long lockdown counts toward achievements."
            },

            // The rules card. It fires ONCE, on the first lockdown that actually runs with Possession
            // on (MainWindow.Lab.cs), not on the first visit to the tab - the point is to state the
            // rules while they are about to matter, and a user who turned the haunt off should never
            // meet this card at all.
            ["possession"] = new FeatureIntroContent
            {
                Key = "possession",
                Glyph = "🕯️",
                RailTitle = "Possession",
                Title = "🔒  Possession",
                Tagline = "The room misbehaves while you're locked in.",
                Accent = "#FF8A5C",
                Bullets = new[]
                {
                    "Things move. Buttons swap places, labels drift, cards sag, letters fall out of titles, and your companion gets up and wanders the window.",
                    "The rule: an ember glow, and her naming what just happened, means it was Lockdown. Never a bug, never real damage - every last piece puts itself back when the timer ends.",
                    "It grows with the timer, and it reacts when you try to leave. Minimizing still works. Ctrl+Alt+Del always works.",
                    "Your dials are on the Lockdown card: Gentle, Eerie or Full Doki, a photosensitive-safe switch, and the three Safeties."
                },
                Footer = "Premium feature. Turn Possession off on the Lockdown card to get the plain timed cage back."
            },

            ["haptics"] = new FeatureIntroContent
            {
                Key = "haptics",
                Glyph = "📳",
                RailTitle = "Haptics",
                Title = "📳  Haptics",
                Tagline = "Let her reach your toys.",
                Accent = "#F783AC",
                Bullets = new[]
                {
                    "Connects to Lovense Connect or Buttplug.io / Intiface - paste the URL and hit Connect.",
                    "Nearly everything can drive it: flashes, bubbles, videos in sync with their audio, subliminals, level-ups, keywords, blinks.",
                    "Each feature gets its own intensity and pattern, on top of one global dial.",
                    "No hardware yet? The Mock provider lets you feel out the settings without a device."
                },
                Footer = "Premium feature. Auto-connect on startup is optional."
            },

            // Deliberately says nothing about premium: remote media as an asset source is FREE.
            // It is the fix for "I installed this and have no media", which is overwhelmingly a
            // free-tier first launch - a card that hinted at a paywall here would sell against
            // the one thing this feature exists to solve. (The For You *feed* stays premium; see
            // the gating note in MainWindow.Assets.cs.)
            ["remotemedia"] = new FeatureIntroContent
            {
                Key = "remotemedia",
                Glyph = "🌐",
                RailTitle = "Remote Media",
                Title = "🌐  Remote Media",
                Tagline = "No folder of your own? She'll bring her own.",
                Accent = "#5EC8F2",
                Bullets = new[]
                {
                    "You don't need a library to start. The app can pull images and clips straight from Reddit.",
                    "You choose the niches, and you can add any subreddit by name - only what you picked is ever fetched.",
                    "It streams from your machine to your screen - nothing is saved, nothing is uploaded, and none of it passes through our servers.",
                    "Switch back to your own assets whenever you like. The choice lives at the top of the Assets tab."
                },
                Footer = "Free for everyone - an empty assets folder should never be a dead end. It is adult content, so only switch it on if you want to see it."
            },

            // ------------------------------------------------------------------------------
            // v6.8.0 door tour. The restructure moved surfaces users already knew, so these five
            // explain a RELOCATION as much as a feature - which is why each one says where the
            // thing used to be. Their keys are NOT tab keys (a rack is not a tab, a bubble is not
            // a tab), so every trigger passes its owning tab explicitly to MaybeShowFeatureIntro;
            // see the doorTab overload in MainWindow.TabNavigation.cs.
            // ------------------------------------------------------------------------------

            ["studio-rack"] = new FeatureIntroContent
            {
                Key = "studio-rack",
                Glyph = "🎛️",
                RailTitle = "The Studio",
                Title = "🎛️  The Studio Rack",
                Tagline = "Every effect in one rack, instead of a popup for each.",
                Accent = "#9B8CFF",
                Bullets = new[]
                {
                    "The dashboard popups are gone. Flashes, subliminals, bubbles, bouncing text and the rest are all rows in the list down the left.",
                    "Left-click a row to open its panel. Right-click it to flip that effect on or off without opening anything at all.",
                    "The dot on each row is live: at a glance you can see everything that is currently running.",
                    "Dashboard tiles land here too, on the module you clicked. Same dials, one room."
                },
                Footer = "Free. Haptics is a module of this rack now, and still introduces itself when you open it."
            },

            ["play-wall"] = new FeatureIntroContent
            {
                Key = "play-wall",
                Glyph = "🎪",
                RailTitle = "The Play Wall",
                Title = "🎪  The Play Wall",
                Tagline = "The Lab moved in here, and brought the whole toy box.",
                Accent = "#FF9F45",
                Bullets = new[]
                {
                    "This is where the Lab page went. Down the Rabbit Hole sits across the top, and every other big toy is a card below it.",
                    "TOGETHER is the things you do with someone else, EYES is anything your webcam watches, SESSIONS is anything on a timer, and MORE is the odds and ends.",
                    "The rail on the left holds the rest of this door: Deeper, Exclusives, Graded Intake, Lockdown, Blink Trainer and Remote Control.",
                    "Locked cards still say what they are, so you can always read the room before you buy into it."
                },
                Footer = "The door is free to walk through. Some of what is behind it is premium, and each card says so on its face."
            },

            ["profile-hub"] = new FeatureIntroContent
            {
                Key = "profile-hub",
                Glyph = "🫧",
                RailTitle = "Your Bubble",
                Title = "🫧  The Header Bubble",
                Tagline = "That little avatar in the top-right corner is you.",
                Accent = "#6EA8FF",
                Bullets = new[]
                {
                    "Hover it and an account panel drops down: your name and tier, your level and XP bar, your badges, and quick jumps to Profile, Achievements and Settings.",
                    "Click it instead of hovering and you land straight on this Trainer Card.",
                    "It reacts while you play. XP pulses it, a level-up bounces it, an achievement rings it in gold.",
                    "The bug button moved to the title bar, up beside minimise. Same report, new address - the bubble took its old seat."
                },
                Footer = "Free, and always on screen. Your photo only appears here if profile-picture sharing is switched on."
            },

            ["daily-free"] = new FeatureIntroContent
            {
                Key = "daily-free",
                Glyph = "🎁",
                RailTitle = "Free Today",
                Title = "🎁  Today's Free Feature",
                Tagline = "One premium feature, genuinely free, every single day.",
                Accent = "#FFD700",
                Bullets = new[]
                {
                    "The box on the Dashboard names today's pick. Hover it and the tile turns over to show you what you have got.",
                    "It really is unlocked: click through and the feature simply works. No trial, no timer, nothing to enter.",
                    "The gold tag is how you spot it. At midnight the wheel turns and tomorrow's feature takes its place.",
                    "The same feature never comes back inside three days, so the box is worth a look every morning."
                },
                Footer = "Free for everyone. If you already have premium you own the whole pool anyway, and the box just says hello."
            },

            // Written now, dark for almost everybody: the vat ships behind the server's rollout
            // dial and the trigger (MainWindow.ProfileVat.ApplyDescentToVat) only fires once the
            // jar is actually armed AND on screen, so a card for a thing that does not exist can
            // never appear. Says nothing about the companion's avatar tube - different feature,
            // different window, and confusing the two is the one mistake this copy must not make.
            ["descent-vat"] = new FeatureIntroContent
            {
                Key = "descent-vat",
                Glyph = "🫙",
                RailTitle = "The Vat",
                Title = "🫙  The Vat",
                Tagline = "Earn it anywhere. Pour it here.",
                Accent = "#66E0C0",
                Bullets = new[]
                {
                    "Your portrait is inside a jar on the Trainer Card now, and how full that jar is, is what you have earned today.",
                    "Everything you earn lands in the tap first, wherever you earned it. Press and hold the tap and it pours into the jar; the percentage under the glass is the same number, said plainly.",
                    "It fills against a daily cap that grows with your level, so a full jar always means the same thing: you are done for today.",
                    "Overnight it empties, and tomorrow you start pouring again."
                },
                Footer = "Rolling out gradually. If you can see the jar, you are in."
            },

            // THE VAT'S OTHER HALF. descent-vat above explains the jar; this one explains what
            // filling it is FOR, and the two are deliberately separate cards because they are
            // reached from different places by people at different points - the jar introduces
            // itself on the Trainer Card, and this waits until someone actually walks into the
            // Spiral Room. Fired from ShowTab("spiral") and gated on the room having painted
            // the MAP (SpiralTabView.IsShowingSpiral), so it can never explain banked days over
            // a fog layer or land on top of the first-light reveal.
            //
            // ENGLISH-ONLY, and that is a debt rather than a decision: every card in this
            // registry is hardcoded English and matching the file beats being the one localized
            // outlier in it. The help topic this ships alongside took the other road
            // (SpiralTabView.BuildDescentHelpContent reads Loc), so the same explanation exists
            // translated for anyone who opens the "?" - which is the mitigation, not the fix.
            ["descent-spiral"] = new FeatureIntroContent
            {
                Key = "descent-spiral",
                Glyph = "🌀",
                RailTitle = "The Spiral",
                Title = "🌀  The Spiral",
                Tagline = "Every day you bank, you sink a little further.",
                Accent = "#66E0C0",
                Bullets = new[]
                {
                    // The middle clause is verbatim the canonical sentence (6.9.1's
                    // profile_vat_tick_drain), so the tooltip on the jar and this card say the
                    // same thing in the same words rather than two paraphrases of it.
                    "The jar on your Trainer Card is today. A fifth of the jar banks the day, and banked days are the only thing that moves you down the Spiral.",
                    "Seven stages are waiting at 1, 7, 21, 50, 100, 180 and 300 days, and the map lights up as you pass each one.",
                    "A short day that banks counts the same as a long one, so showing up beats grinding.",
                    "Nothing here resets, and nothing is going to. Your level, your XP and every stage you've reached stay yours however long you leave them."
                },
                Footer = "Open the Spiral from the rail any time, or from the plate on your Trainer Card."
            },

            // v6.8.0, the One Account lane: the first card with a real CTA, because a card about
            // a DESTINATION must be able to take you there. Queued on the same settle path as
            // daily-free (MainWindow.TabNavigation.OnDashboardTabVisibilityChanged) and owned by
            // the Home door, so the two drip across launches instead of stacking. Fires for
            // fresh installs too - they never see What's New, so without this card they hear
            // nothing about the web at all. The spiral bullet reuses the exact phrasing already
            // public in the v6.8.0 release notes - it is a tease, not an explanation, and it
            // stays that way until the Sept 1 announcement.
            ["one-account"] = new FeatureIntroContent
            {
                Key = "one-account",
                Glyph = "🌐",
                RailTitle = "One Account",
                Title = "🌐  One Account",
                Tagline = "Desktop, web and mobile are windows onto the same account now.",
                Accent = "#5EC8F2",
                Bullets = new[]
                {
                    "Sign in at app.cclabs.app with the same account you use here.",
                    "XP you earn on the web lands in your desktop level through the normal sync.",
                    "Link this device in seconds with a 6-digit code from the web dashboard.",
                    "A sealed spiral has quietly appeared on the web dashboard. It is not explained."
                },
                Footer = "The new Web door above Settings takes you there any time.",
                ActionLabel = "Open the web app",
                DismissLabel = "Later",
                // BrowserLauncher, not Process.Start: this click is the app's invitation to the
                // web, and the no-default-browser machines are exactly who must not see it fail.
                // Acting on the card also retires the One Account banner beat - the nudge worked.
                OnAction = () =>
                {
                    Helpers.BrowserLauncher.OpenUrlOrPrompt(MainWindow.WebAppUrl, "open the CC Labs web app");
                    (Application.Current?.MainWindow as MainWindow)?.RetireWebBannerBeat();
                }
            },

            [FeatureIntroPopup.CelebrationKey] = new FeatureIntroContent
            {
                Key = FeatureIntroPopup.CelebrationKey,
                Glyph = "💖",
                RailTitle = "Thank You",
                Title = "💖  Premium Unlocked",
                Tagline = "Your support keeps the Lab running - and it just opened every door.",
                Accent = "#FF69B4",
                Bullets = new[]
                {
                    "All the exclusive tabs are yours: Takeover, Remote Control, She's Listening, Blink Trainer, Haptics, Awareness, Lockdown, Graded Intake.",
                    "Your companion's AI limits go up, and premium quests, programs and exclusive achievements switch on.",
                    "Look for the Premium Rail on the Dashboard - one flip per feature, all in one strip.",
                    "Each exclusive tab introduces itself the first time you open it. Wander."
                },
                Footer = "Everything lives under the Exclusives menu. Enjoy - you earned it.",
                DismissLabel = "Let's go 💖"
            }
        };
    }
}
