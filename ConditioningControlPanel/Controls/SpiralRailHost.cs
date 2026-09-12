using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE SPIRAL RAIL - the miniature docked in the nav rail (CONTRACTS §9).
    ///
    /// ONE LAYER, NATIVE WPF. The chip is a 40px medallion holding a
    /// <see cref="SpiralGlyph"/>: the same vector arm, travelling dot and stage numeral that
    /// the Trainer Card plate and the account menu row already wear. It costs no process, no
    /// HWND and no request, it paints on the frame it is asked to, and there is no state in
    /// which the circle is empty.
    ///
    /// ON BY DEFAULT since 6.9.3 (AppSettings.DescentSpiralRailEnabled, no editor; a
    /// hand-edited false in settings.json makes <see cref="Arm"/> return before it touches
    /// anything and this control stays Collapsed, zero pixels and zero requests).
    ///
    /// <para><b>IT USED TO BE A WEBVIEW2, and that is the bug this file was rewritten to
    /// fix (2026-09-09).</b> The chip hosted <see cref="SpiralEmbedView"/> at ?mode=mini,
    /// shown only while the rail was wide enough to hold it and torn down on collapse. Both
    /// halves of that arrangement misbehaved on the owner's machine.</para>
    ///
    /// <para>THE FLICKER. A WebView2 is a native child HWND, so a pointer moving onto it
    /// leaves the WPF visual tree and NavSidebar raises MouseLeave. MainWindow.NavRail.cs
    /// collapses the rail on MouseLeave with no timer in front of it, the rail's width then
    /// dropped under the embed's 72px minimum, the old EvaluateEmbed tore the browser down,
    /// the pointer was over plain WPF again, MouseEnter fired, the rail reopened and rebuilt
    /// the embed. A closed loop, running at animation rate, reachable from no other row on
    /// the rail because no other row owned an HWND.</para>
    ///
    /// <para>THE EMPTY CIRCLE. The old host hid its stage badge the moment the canvas posted
    /// 'spiral:ready' and had nothing else to draw, so a mini canvas that painted only its
    /// own opaque background left a hole where the spiral should be. Uncovered, the badge
    /// drew a Roman numeral: it never drew a spiral at all, at any point in its life.</para>
    ///
    /// <para>DECISIONS.md (2026-08-10) had already amended the "one web canvas" law with
    /// "rail MINI = native WPF Canvas/Path, a rail-docked WebView2 HWND would sit over every
    /// tab transition", and this file's previous revision called the disagreement with
    /// CONTRACTS-0812-FINISH §9 an open owner call that nothing downstream depended on. The
    /// owner's bug report is that call. The embed keeps §9's other host, the SPIRAL ROOM at
    /// ?mode=map (Views/Tabs/SpiralTabView.xaml.cs), where the browser gets a stable slab
    /// and has no rail animation to fight.</para>
    /// </summary>
    public sealed class SpiralRailHost : Grid
    {
        /// <summary>Door rhythm: the rail's medallion column is 56 wide, so the chip lands on
        /// the same centre line as every row above it and leaves no dead gap where the 80px
        /// embed slab used to sit.</summary>
        private const double ChipHeight = 56;

        /// <summary>The medallion, on the ring size the fuse chip uses one row below.</summary>
        private const double BadgeSize = 40;

        /// <summary>The arm inside the medallion. Above <see cref="SpiralGlyph.NumeralMinWidth"/>,
        /// so the stage numeral still reads in the middle of it.</summary>
        private const double GlyphSize = 32;

        private readonly Border _badge;
        private readonly SpiralGlyph _glyph;
        private bool _wired;

        public SpiralRailHost()
        {
            Height = ChipHeight;
            Margin = new Thickness(0, 6, 0, 2);
            Cursor = Cursors.Hand;
            // The rail is dark and nothing here has a flag on: stay out of the layout
            // entirely until Arm() says otherwise, so an un-armed build measures as if
            // this control were not in the tree.
            Visibility = Visibility.Collapsed;

            _glyph = new SpiralGlyph
            {
                Width = GlyphSize,
                Height = GlyphSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _badge = new Border
            {
                Width = BadgeSize,
                Height = BadgeSize,
                CornerRadius = new CornerRadius(BadgeSize / 2),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x69, 0xB4)),
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = _glyph,
            };
            Children.Add(_badge);

            MouseLeftButtonUp += (_, _) => OpenMap();
            // SELF-WIRING. The host window adds this control and nothing else: no init
            // call in MainWindow, no line in the startup sequence, no partial to keep in
            // step. A Collapsed element still raises Loaded, so the flag-off case reaches
            // Arm(), returns immediately and never touches DescentService.
            Loaded += (_, _) => Wire();
        }

        /// <summary>The flag. True by default since 6.9.3; there is deliberately no editor for it.</summary>
        public static bool FlagEnabled => App.Settings?.Current?.DescentSpiralRailEnabled == true;

        // ============================== lifecycle ==============================

        /// <summary>
        /// Light the rail up, if it is allowed to be lit. Four gates, all required: the
        /// flag, an account the server has actually shipped a descent block to, a live
        /// DescentService, and no migration ceremony still owed. Missing any one leaves
        /// the control Collapsed.
        ///
        /// Called on every block change, so an account whose key lights up mid-session
        /// gets the rail without a restart, and one whose key is withdrawn loses it.
        ///
        /// THE WITHHOLD (CONTRACT-FUSE-0816 §2.4) is the fourth gate and the newest: at
        /// zero the server's block dial promotes to 'all' on the same sync that offers a
        /// veteran the migration ceremony, so block presence alone would light this rail
        /// up while the question is still on screen. DescentMigrationService raises
        /// BlockChanged when the withhold flips, which is why re-reading it here, in the
        /// method that already re-runs on every block change, is the entire wiring.
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
                    return;
                }

                Visibility = Visibility.Visible;
                ApplyBlock(App.Descent?.Current);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] Arm: {E}", ex.Message); }
        }

        /// <summary>
        /// Subscribe to the descent block. Idempotent; call once from the host window.
        /// The rail feeds off the SAME service the Trainer Card's vat does and issues no
        /// request of its own: one reader, one cadence, one 10s floor.
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

        /// <summary>
        /// Re-evaluate the glyph's ambient breath. There is no app-wide motion-level event,
        /// so this rides the same choke point every other ambient loop does; MainWindow's
        /// RefreshSpiralGlyphMotion calls it alongside the two profile glyphs.
        /// </summary>
        public void RefreshGlyphMotion() => _glyph.RefreshMotion();

        // ============================== the medallion ==============================

        private void ApplyBlock(DescentBlock? block)
        {
            _glyph.Apply(block);

            // THE NAME LIVES IN THE TOOLTIP, because that is the only place on this
            // control where a word actually fits. The medallion is a 40px circle sized for
            // the arm and one glyph, so the numeral stays where it is and the hover says
            // what the rung is called and what it feels like - which is the whole reason
            // the names were locked. A block that has not arrived leaves the tooltip alone
            // rather than hovering "The Edge" over a rail that is about to light up.
            int n = block?.Stage?.N ?? 0;
            ToolTip = block is null
                ? null
                : DescentStageCopy.Name(n) + "\n" + DescentStageCopy.Flavor(n);
        }

        /// <summary>
        /// Stage as a Roman numeral, which is the only stage copy a medallion this size can
        /// carry. The stage names were an open owner decision when this was written; they
        /// were locked on 2026-08-11 and now live in <see cref="DescentStageCopy"/> with
        /// localization keys behind them, so the reason the numeral stays is no longer
        /// "we have no copy" - it is that a 40px circle has room for a glyph and not for
        /// "Crush Depth". The name rides the tooltip instead (see
        /// <see cref="ApplyBlock"/>), and every surface with room for a word uses one.
        /// n = 0 is the real pre-begin rung and reads as a dot.
        ///
        /// <para>Still the single source for it: <see cref="SpiralGlyph"/> calls straight
        /// into here for the numeral it draws, so the rail chip, the Trainer Card plate and
        /// the account menu row cannot drift apart.</para>
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

        /// <summary>
        /// Click-to-expand: the shared canvas at ?mode=map, in the SPIRAL ROOM.
        ///
        /// <para><b>It used to be a window</b> (<c>SpiralMapWindow</c>), on the reasoning that a
        /// separate top-level HWND has no airspace problem at all. True, but it also meant the one
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
