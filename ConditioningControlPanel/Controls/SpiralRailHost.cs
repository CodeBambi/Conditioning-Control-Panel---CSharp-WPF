using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE SPIRAL RAIL — the 80px miniature docked in the nav rail (CONTRACTS §9),
    /// with the static stage badge it falls back to.
    ///
    /// TWO LAYERS, ONE ALWAYS PRESENT:
    ///   • the BADGE — a plain WPF Border/TextBlock pair showing the stage numeral. It
    ///     is composited like everything else on the rail, costs nothing, and is what a
    ///     machine with no WebView2 runtime sees. §9's rule is "never an empty hole",
    ///     and the badge is how that is kept.
    ///   • the EMBED — <see cref="SpiralEmbedView"/> at ?mode=mini, created lazily and
    ///     only when the flag is on. It sits ON TOP of the badge and hides it once the
    ///     canvas is ready; any failure disposes it and the badge is simply uncovered.
    ///
    /// DARK BY DEFAULT. AppSettings.DescentSpiralRailEnabled is false and has no editor,
    /// so in a shipped build <see cref="Arm"/> returns before it touches anything and
    /// this control stays Collapsed — zero pixels, zero HWNDs, zero requests. The embed
    /// route does not exist yet either; when it 404s, the fallback path above is the one
    /// that runs, which is exactly the path this had to prove anyway.
    ///
    /// THE AIRSPACE MITIGATION, AND THE OPEN QUESTION BEHIND IT. DECISIONS.md
    /// (2026-08-10) amended the "one web canvas" law with "rail MINI = native WPF
    /// Canvas/Path — a rail-docked WebView2 HWND would sit over every tab transition";
    /// CONTRACTS-0812-FINISH §9 and Master Doc v2.1 §9 (both newer, both frozen) say the
    /// mini IS the shared canvas hosted via WebView2. This class implements the contract
    /// and mitigates the amendment's objection: the embed is shown ONLY while the rail is
    /// wide enough to hold it and is torn down whenever the rail collapses or the control
    /// leaves the screen (<see cref="OnSizeChanged"/> / <see cref="OnIsVisibleChanged"/>),
    /// so a native HWND never sits in the middle of the flyout's width animation. It is a
    /// mitigation, not a proof — whether the mini stays WebView2 or reverts to native WPF
    /// is an owner/coordinator call, and nothing downstream of this file depends on which
    /// way it goes.
    /// </summary>
    public sealed class SpiralRailHost : Grid
    {
        /// <summary>Below this width the embed is not shown — see the airspace note above.</summary>
        private const double MinEmbedWidth = 72;

        private readonly Border _badge;
        private readonly TextBlock _badgeText;
        private SpiralEmbedView? _embed;
        private bool _wired;
        private bool _embedGaveUp;

        public SpiralRailHost()
        {
            Height = 80;
            Margin = new Thickness(0, 6, 0, 2);
            Cursor = Cursors.Hand;
            // The rail is dark and nothing here has a flag on: stay out of the layout
            // entirely until Arm() says otherwise, so an un-armed build measures as if
            // this control were not in the tree.
            Visibility = Visibility.Collapsed;

            _badgeText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0xAF)),
            };

            _badge = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x69, 0xB4)),
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = _badgeText,
            };
            Children.Add(_badge);

            MouseLeftButtonUp += (_, _) => OpenMap();
            SizeChanged += OnSizeChanged;
            IsVisibleChanged += OnIsVisibleChanged;
            // SELF-WIRING. The host window adds this control and nothing else: no init
            // call in MainWindow, no line in the startup sequence, no partial to keep in
            // step. A Collapsed element still raises Loaded, so the flag-off case reaches
            // Arm(), returns immediately and never touches DescentService.
            Loaded += (_, _) => Wire();
        }

        /// <summary>The flag. False in every shipped build; there is deliberately no editor for it.</summary>
        public static bool FlagEnabled => App.Settings?.Current?.DescentSpiralRailEnabled == true;

        // ============================== lifecycle ==============================

        /// <summary>
        /// Light the rail up, if it is allowed to be lit. Four gates, all required: the
        /// flag, an account the server has actually shipped a descent block to, a live
        /// DescentService, and no migration ceremony still owed. Missing any one leaves
        /// the control Collapsed.
        ///
        /// Called on every block change, so an account whose key lights up mid-session
        /// gets the rail without a restart — and one whose key is withdrawn loses it.
        ///
        /// THE WITHHOLD (CONTRACT-FUSE-0816 §2.4) is the fourth gate and the newest: at
        /// zero the server's block dial promotes to 'all' on the same sync that offers a
        /// veteran the migration ceremony, so block presence alone would light this rail
        /// up while the question is still on screen. DescentMigrationService raises
        /// BlockChanged when the withhold flips, which is why re-reading it here — in the
        /// method that already re-runs on every block change — is the entire wiring.
        /// </summary>
        public void Arm()
        {
            try
            {
                bool allowed = FlagEnabled
                               && App.Descent?.Current is not null
                               && App.DescentMigration?.SpiralWithheld != true;
                if (!allowed)
                {
                    Visibility = Visibility.Collapsed;
                    TeardownEmbed();
                    return;
                }

                Visibility = Visibility.Visible;
                ApplyBlock(App.Descent?.Current);
                EvaluateEmbed();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] Arm: {E}", ex.Message); }
        }

        /// <summary>
        /// Subscribe to the descent block. Idempotent; call once from the host window.
        /// The rail feeds off the SAME service the Trainer Card's vat does and issues no
        /// request of its own — one reader, one cadence, one 10s floor.
        /// </summary>
        public void Wire()
        {
            if (_wired) return;
            _wired = true;
            try
            {
                if (App.Descent != null) App.Descent.BlockChanged += (_, _) => Arm();
                Arm();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] Wire: {E}", ex.Message); }
        }

        // ============================== the badge ==============================

        private void ApplyBlock(DescentBlock? block)
        {
            int n = block?.Stage?.N ?? 0;
            _badgeText.Text = StageNumeral(n);

            // THE NAME LIVES IN THE TOOLTIP, because that is the only place on this
            // control where a word actually fits. The badge is a 40px circle sized for
            // one glyph, so the numeral stays where it is and the hover says what the
            // rung is called and what it feels like - which is the whole reason the
            // names were locked. A block that has not arrived leaves the tooltip alone
            // rather than hovering "The Edge" over a rail that is about to light up.
            ToolTip = block is null
                ? null
                : DescentStageCopy.Name(n) + "\n" + DescentStageCopy.Flavor(n);

            _embed?.PostState(block);
        }

        /// <summary>
        /// Stage as a Roman numeral, which is the only stage copy the BADGE ships. The
        /// stage names were an open owner decision when this was written; they were
        /// locked on 2026-08-11 and now live in <see cref="DescentStageCopy"/> with
        /// localization keys behind them, so the reason the numeral stays is no longer
        /// "we have no copy" - it is that a 40px circle has room for a glyph and not for
        /// "Crush Depth". The name rides the tooltip instead (see
        /// <see cref="ApplyBlock"/>), and every surface with room for a word uses one.
        /// n = 0 is the real pre-begin rung and reads as a dot.
        /// </summary>
        internal static string StageNumeral(int n) => n switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
            7 => "VII",
            8 => "VIII",
            _ => n > 8 ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "·",
        };

        // ============================== the embed ==============================

        /// <summary>
        /// Create or tear down the embed to match the space available. The width test is
        /// the airspace mitigation: a collapsed 56px rail gets the badge, and the native
        /// HWND only exists while there is a stable slab wide enough to hold it.
        /// </summary>
        private void EvaluateEmbed()
        {
            if (_embedGaveUp) return;                      // one failure is permanent for the session
            if (!FlagEnabled) { TeardownEmbed(); return; }

            bool room = IsVisible && ActualWidth >= MinEmbedWidth;
            if (!room) { TeardownEmbed(); return; }
            if (_embed != null) return;

            var embed = new SpiralEmbedView("mini");
            embed.Failed += (_, _) =>
            {
                // The badge is already underneath — uncovering it IS the fallback.
                _embedGaveUp = true;
                TeardownEmbed();
            };
            embed.Ready += (_, _) => _badge.Visibility = Visibility.Hidden;
            _embed = embed;
            Children.Add(embed);
            embed.Start();
            embed.PostState(App.Descent?.Current);
        }

        private void TeardownEmbed()
        {
            if (_embed == null) return;
            try
            {
                Children.Remove(_embed);
                _embed.Dispose();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] TeardownEmbed: {E}", ex.Message); }
            _embed = null;
            _badge.Visibility = Visibility.Visible;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Visibility == Visibility.Visible) EvaluateEmbed();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) EvaluateEmbed();
            else TeardownEmbed();
        }

        /// <summary>
        /// Click-to-expand: the same canvas at ?mode=map, in the SPIRAL ROOM.
        ///
        /// <para><b>It used to be a window</b> (<c>SpiralMapWindow</c>), on the reasoning that a
        /// separate top-level HWND has no airspace problem at all. True — but it also meant the one
        /// moment this feature was built for opened a second window over the app instead of opening
        /// a room in it, so the window retired on 2026-08-16 and the tab took over. The airspace
        /// problem is solved there by NOT BUILDING the browser except while that tab is the one on
        /// screen in the spiral state; see Views/Tabs/SpiralTabView.xaml.cs.</para>
        ///
        /// <para><b>The gates moved with it.</b> This method deliberately no longer tests the block
        /// or the withhold: the tab re-reads every gate on entry and shows the fog, the waiting room
        /// or the canvas accordingly, so a deep link, a stale click or a future caller lands on the
        /// right room rather than on nothing happening.</para>
        /// </summary>
        private static void OpenMap()
        {
            if (!FlagEnabled) return;
            try
            {
                if (Application.Current?.MainWindow is ConditioningControlPanel.MainWindow main)
                    main.ShowTab(SpiralRoom.TabKey);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] OpenMap: {E}", ex.Message); }
        }
    }
}
