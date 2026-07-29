using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One-time explainer for the Training Programs tab, shown the first time the user opens it.
    ///
    /// Dressed with the sigil of whichever program matches the ACTIVE MOD, so a Sissy Hypno install
    /// is introduced to programs by Presentation rather than by Bambi's First Week. Everything else
    /// on the card is mod-neutral: the copy describes the mechanic, not the fiction, because the
    /// mechanic is identical whichever program the user eventually picks.
    ///
    /// Copy is hardcoded English, matching <see cref="AnnouncementPopup"/> and the other one-shot
    /// popups in this folder. That is deliberate rather than lazy - see the note on ShowIfFirstTime.
    /// </summary>
    public partial class ProgramsIntroPopup : Window
    {
        private ProgramsIntroPopup(ProgramDefinition? featured)
        {
            InitializeComponent();
            ApplyFeaturedProgram(featured);
        }

        /// <summary>Set between queueing the popup and opening it, so a double-click queues one card.</summary>
        private static bool _opening;

        /// <summary>
        /// Shows the explainer once per install, then never again.
        ///
        /// The flag is spent at the moment the dialog is about to open, NOT when it is queued. That
        /// ordering is load-bearing: spending it up front means any failure to actually display the
        /// card burns the user's only chance to ever see it, which is exactly the failure this had
        /// when it was queued at the wrong dispatcher priority. Saved immediately before the modal
        /// opens, so being killed while it is on screen still counts as seen - showing it a second
        /// time would read as a bug.
        ///
        /// Wholly guarded - this is called from a tab click handler, and an explainer must never be
        /// the reason the Programs tab fails to open.
        /// </summary>
        internal static void ShowIfFirstTime(Window? owner)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null || settings.HasSeenProgramsIntro || _opening) return;

                var featured = PickFeaturedProgram();

                // Deferred so the click handler returns and the Programs tab behind it gets to lay
                // out before a modal takes over the message pump.
                //
                // Normal priority, NOT Loaded: Loaded (6) sits below Normal (9), and this app keeps
                // the dispatcher busy with the compositor host and the avatar's animations, so a
                // Loaded-priority item can be starved indefinitely. Measured - at Loaded the popup
                // never opened at all while the flag had already been spent, which is the worst of
                // both outcomes.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                _opening = true;
                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                {
                    try
                    {
                        var live = App.Settings?.Current;
                        if (live == null || live.HasSeenProgramsIntro) return;

                        var popup = new ProgramsIntroPopup(featured);
                        if (owner != null && owner.IsLoaded) popup.Owner = owner;

                        // Constructed without throwing, so the card is going to be shown - spend the
                        // flag now rather than after ShowDialog returns, which only happens on dismiss.
                        live.HasSeenProgramsIntro = true;
                        App.Settings?.Save();

                        popup.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Programs intro popup failed to show");
                    }
                    finally
                    {
                        _opening = false;
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Programs intro popup gate failed");
            }
        }

        /// <summary>
        /// The program the card is dressed with: the one themed on the active mod, else the first in
        /// the library.
        ///
        /// Reads the live library (which includes anything a mod or the catalogue added) and falls
        /// back to the built-in list only if the service has not started yet, so the popup cannot be
        /// the one place in the app that disagrees about what programs exist.
        /// </summary>
        private static ProgramDefinition? PickFeaturedProgram()
        {
            try
            {
                var library = App.Programs?.Library;
                if (library is not { Count: > 0 }) library = BuiltInPrograms.All();
                if (library is not { Count: > 0 }) return null;

                var activeModId = App.Mods?.ActiveModId;
                if (!string.IsNullOrWhiteSpace(activeModId))
                {
                    var themed = library.FirstOrDefault(p =>
                        string.Equals(p.ModId, activeModId, StringComparison.OrdinalIgnoreCase));
                    if (themed != null) return themed;
                }

                return library[0];
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Programs intro could not pick a featured program: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Points the sigil rail at the featured program, or collapses it.
        ///
        /// Same null contract as <see cref="ProgramArt"/>: no art means the rail goes away and the
        /// copy takes the whole card. The sigil is preferred over the program's dashboard banner on
        /// purpose - the banner is a 2800x113 texture strip authored to wash behind a one-line card,
        /// and at this size it would read as a smear rather than as the program's emblem.
        /// </summary>
        private void ApplyFeaturedProgram(ProgramDefinition? program)
        {
            try
            {
                if (program == null)
                {
                    ArtPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                var accent = AccentBrush(program.AccentColor);

                // The bullet glyphs pick up the program accent so the card reads as one object with
                // the rail beside it, rather than a pink card with an unrelated picture stapled on.
                Bullet1.Foreground = accent;
                Bullet2.Foreground = accent;
                Bullet3.Foreground = accent;
                Bullet4.Foreground = accent;

                // Day 1's mood plate is the fallback: every program has one via the shared archetype
                // chain, so a program with no sigil of its own still gets a dressed rail.
                var art = ProgramArt.Sigil(program) ?? ProgramArt.DayPlate(program, program.GetDay(1));
                if (art == null)
                {
                    ArtPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                ArtSigilMask.ImageSource = art;
                ArtSigil.Fill = accent;
                ArtGlow.Fill = GlowBrush(accent);

                TxtArtProgramTitle.Text = program.Title;
                TxtArtProgramTitle.Foreground = accent;
                TxtArtProgramSubtitle.Text = program.Subtitle;
                TxtArtProgramSubtitle.Visibility = string.IsNullOrWhiteSpace(program.Subtitle)
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                ArtPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                // Dressing is decoration - lose the rail, keep the explainer.
                App.Logger?.Warning(ex, "Programs intro art failed");
                try { ArtPanel.Visibility = Visibility.Collapsed; } catch { }
            }
        }

        /// <summary>Program accent, falling back to the app pink a bad hex would otherwise cost us.</summary>
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
}
