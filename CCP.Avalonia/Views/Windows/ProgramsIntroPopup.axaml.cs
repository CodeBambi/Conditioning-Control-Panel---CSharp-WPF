using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// One-time explainer for the Training Programs tab, shown the first time the user opens it.
    ///
    /// Dressed with the sigil of whichever program matches the ACTIVE MOD, so a Sissy Hypno install
    /// is introduced to programs by Presentation rather than by Bambi's First Week. Everything else
    /// on the card is mod-neutral: the copy describes the mechanic, not the fiction, because the
    /// mechanic is identical whichever program the user eventually picks.
    ///
    /// Copy is hardcoded English, matching AnnouncementPopup and the other one-shot popups in this
    /// folder. That is deliberate rather than lazy - see the note on ShowIfFirstTime.
    ///
    /// PORTED from ConditioningControlPanel/Windows/ProgramsIntroPopup.xaml.cs. Deviations:
    ///  - <c>ShowIfFirstTime</c> reads CoreSettings / CoreMods and the built-in library; no
    ///    ProgramService is constructed (its timers must not start on a head with no run panel).
    ///    No StartupLadder here, so the card opens directly instead of via the Inbox presenter.
    ///  - <c>ProgramArt.Sigil/DayPlate</c> stays unresolved, blocked on the unlinked
    ///    <c>Assets/programs</c> art rather than on the resolver - see the note at its call site.
    ///    The sigil stays hidden and the rail shows its gradient, glow and program title, which is
    ///    what the WPF fallback plate draws.
    ///  - <c>PreviewKeyDown</c> -&gt; <c>KeyDown</c>; <c>DragMove()</c> -&gt; <c>BeginMoveDrag(e)</c>.
    /// </summary>
    public partial class ProgramsIntroPopup : Window
    {
        /// <summary>Render constructor: sample data, so --render-all can discover the window.</summary>
        internal ProgramsIntroPopup() : this(BuiltInPrograms.All().FirstOrDefault()) { }

        public ProgramsIntroPopup(ProgramDefinition? featured)
        {
            AvaloniaXamlLoader.Load(this);
            ApplyFeaturedProgram(featured);

            this.FindControl<Button>("BtnDismiss")!.Click += (_, _) => TryClose();
            this.FindControl<Button>("BtnCloseX")!.Click += (_, _) => TryClose();

            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                TryClose();
            };

            // Chromeless window, so dragging the card is the only way to move it.
            PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                try { BeginMoveDrag(e); } catch { /* dragging can throw if the press was consumed */ }
            };
        }

        /// <summary>
        /// Points the sigil rail at the featured program, or collapses it.
        ///
        /// Same null contract as ProgramArt: no art means the rail goes away and the copy takes the
        /// whole card. The sigil is preferred over the program's dashboard banner on purpose - the
        /// banner is a 2800x113 texture strip authored to wash behind a one-line card, and at this
        /// size it would read as a smear rather than as the program's emblem.
        /// </summary>
        /// <summary>Set between queueing the popup and opening it, so a double-click queues one card.</summary>
        private static bool _opening;

        /// <summary>
        /// Shows the explainer once per install (WPF ProgramsIntroPopup.xaml.cs:46,73). The flag is
        /// spent just before the dialog opens, never when queued, so a failed show leaves it owed.
        /// Wholly guarded: an explainer must never be the reason the Programs tab fails to open.
        /// </summary>
        internal static void ShowIfFirstTime(Window? owner)
        {
            try
            {
                if (CoreSettings.Current.HasSeenProgramsIntro || _opening) return;
                var featured = PickFeaturedProgram();

                _opening = true;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var live = CoreSettings.Current;
                        if (live.HasSeenProgramsIntro) return;
                        var popup = new ProgramsIntroPopup(featured);
                        live.HasSeenProgramsIntro = true;
                        CoreSettings.Save();
                        if (owner is { IsVisible: true }) _ = popup.ShowDialog(owner);
                        else popup.Show();
                    }
                    catch (Exception ex) { Log.Warning(ex, "Programs intro popup failed to show"); }
                    finally { _opening = false; }
                });
            }
            catch (Exception ex)
            {
                _opening = false;
                Log.Warning(ex, "Programs intro popup gate failed");
            }
        }

        /// <summary>The program themed on the active mod, else the first in the library.</summary>
        private static ProgramDefinition? PickFeaturedProgram()
        {
            var library = BuiltInPrograms.All();
            if (library.Count == 0) return null;
            var modId = CoreMods.ActiveModId;
            return library.FirstOrDefault(p => string.Equals(p.ModId, modId, StringComparison.OrdinalIgnoreCase))
                   ?? library[0];
        }

        private void ApplyFeaturedProgram(ProgramDefinition? program)
        {
            var artPanel = this.FindControl<Border>("ArtPanel")!;
            try
            {
                if (program == null)
                {
                    artPanel.IsVisible = false;
                    return;
                }

                var accent = AccentBrush(program.AccentColor);

                // The bullet glyphs pick up the program accent so the card reads as one object with
                // the rail beside it, rather than a pink card with an unrelated picture stapled on.
                for (int i = 1; i <= 4; i++)
                    this.FindControl<TextBlock>("Bullet" + i)!.Foreground = accent;

                // ponytail: the sigil Rectangle stays hidden, and NOT for the reason the old note
                // gave. Avalonia has OpacityMask on Visual, and Helpers.ModArt.TryLoad is already
                // this head's ModResourceResolver.ResolveImage - so the mask itself is portable.
                // Two things actually block it:
                //   1. Assets/programs (sigil_*.png, plate_default.png) is NOT linked into
                //      CCP.Avalonia.csproj, so avares://CCP.Avalonia/Resources/programs/... does
                //      not exist and TryLoad returns null for every one. A csproj this layer does
                //      not own; see Assets/README.md for the Link= shape.
                // The rail still draws its gradient, glow and title, which is what the WPF
                // shared-fallback-plate path looks like.
                this.FindControl<Rectangle>("ArtGlow")!.Fill = GlowBrush(accent);

                var title = this.FindControl<TextBlock>("TxtArtProgramTitle")!;
                title.Text = program.Title;
                title.Foreground = accent;

                var subtitle = this.FindControl<TextBlock>("TxtArtProgramSubtitle")!;
                subtitle.Text = program.Subtitle;
                subtitle.IsVisible = !string.IsNullOrWhiteSpace(program.Subtitle);

                artPanel.IsVisible = true;
            }
            catch
            {
                // Dressing is decoration - lose the rail, keep the explainer.
                try { artPanel.IsVisible = false; } catch { }
            }
        }

        /// <summary>Program accent, falling back to the app pink a bad hex would otherwise cost us.</summary>
        private IBrush AccentBrush(string? hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                    return new SolidColorBrush(Color.Parse(hex!));
            }
            catch { /* a bad accent must never break the card */ }

            return this.TryFindResource("PinkBrush", out var found) && found is IBrush brush
                ? brush
                : Brushes.HotPink;
        }

        private static IBrush GlowBrush(IBrush accent)
        {
            try
            {
                if (accent is ISolidColorBrush solid)
                {
                    var c = solid.Color;
                    return new RadialGradientBrush
                    {
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(90, c.R, c.G, c.B), 0),
                            new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
                        },
                        Center = new RelativePoint(0.5, 0.42, RelativeUnit.Relative),
                        GradientOrigin = new RelativePoint(0.5, 0.42, RelativeUnit.Relative),
                        RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
                        RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
                    };
                }
            }
            catch { /* a glow failing must never break the card */ }

            return Brushes.Transparent;
        }

        private void TryClose()
        {
            try { Close(); } catch { }
        }
    }
}
