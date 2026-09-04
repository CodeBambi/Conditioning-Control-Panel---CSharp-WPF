using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

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
    ///  - <c>ShowIfFirstTime</c> is dropped, not stubbed. App.Settings and App.Mods are NOT the
    ///    blockers any more (CoreSettings / CoreMods answer both), but App.Programs is:
    ///    ConditioningControlPanel/Services/Program/ProgramService.cs is head-side and the gate is
    ///    "has a program been started", which nothing here can answer. An empty gate that silently
    ///    never shows the card would be worse than no gate.
    ///  - <c>ProgramDefinition</c> is in the WPF head, so <see cref="Featured"/> is a local
    ///    stand-in holding exactly the four fields the card dresses itself from.
    ///  - <c>ProgramArt.Sigil/DayPlate</c> stays unresolved, blocked on the unlinked
    ///    <c>Assets/programs</c> art rather than on the resolver - see the note at its call site.
    ///    The sigil stays hidden and the rail shows its gradient, glow and program title, which is
    ///    what the WPF fallback plate draws.
    ///  - <c>PreviewKeyDown</c> -&gt; <c>KeyDown</c>; <c>DragMove()</c> -&gt; <c>BeginMoveDrag(e)</c>.
    /// </summary>
    public partial class ProgramsIntroPopup : Window
    {
        /// <summary>Render constructor: sample data, so --render-all can discover the window.</summary>
        internal ProgramsIntroPopup() : this(Featured.Sample()) { }

        public ProgramsIntroPopup(Featured? featured)
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
        private void ApplyFeaturedProgram(Featured? program)
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
                //   2. ProgramArt's key is Slug(program.Id) and DayPlate's is the session
                //      template's display name - both off ProgramDefinition, which is head-side
                //      (Featured below carries four display fields, not an Id or a day list).
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

        /// <summary>
        /// Stand-in for the head's ProgramDefinition: the four fields the card dresses itself from,
        /// nothing more. Replaced by the real definition when Models/Program moves to Core.
        /// </summary>
        public sealed class Featured
        {
            public string Title { get; set; } = "";
            public string Subtitle { get; set; } = "";
            public string? AccentColor { get; set; }
            public string? ModId { get; set; }

            internal static Featured Sample() => new()
            {
                Title = "First Week",
                Subtitle = "Seven days, one session a day",
                AccentColor = "#FF8AB4",
            };
        }
    }
}
