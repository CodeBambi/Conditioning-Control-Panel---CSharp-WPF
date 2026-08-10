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

                        // The celebration is queued during startup, where the What's New and
                        // update dialogs also live. Both were checked before queueing, but they
                        // can open in between - recheck at open time and yield (unspent; every
                        // launch retries) rather than stack a modal on a modal.
                        if (key == CelebrationKey &&
                            (App.IsUpdateDialogActive || MainWindow.IsStartupDialogShowing)) return;

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
                    BtnDismiss.Background = accent;
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
                    "Pick a duration and commit: strict lock switches on and the panic key switches off until the timer runs out.",
                    "From five minutes to four hours. The countdown is all you'll see.",
                    "If the app crashes mid-lockdown, your real settings are restored on the next launch - nothing stays stuck.",
                    "And if you truly need out early... the timer keeps a secret. Ask it nicely."
                },
                Footer = "Premium feature. Sitting through a long lockdown counts toward achievements."
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
