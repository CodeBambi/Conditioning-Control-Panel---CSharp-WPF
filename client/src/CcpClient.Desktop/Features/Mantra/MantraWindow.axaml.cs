using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace CcpClient.Desktop.Features.Mantra;

/// <summary>
/// The typed mantra minigame's window — upstream's <c>Windows/MantraWindow.xaml.cs</c>, ported by
/// outcome. Every rule it obeys lives in <see cref="MantraSession"/> and
/// <see cref="MantraIntensity"/>; this class does three things and nothing else: it feeds delivered
/// keystrokes to the session, it paints what the session says, and it runs the two timers upstream
/// runs.
///
/// <para><b>No edit control.</b> Upstream's box is a WPF <c>TextBox</c> and most of its constructor
/// is spent taking the <c>TextBox</c>'s own features back off it (<c>:46-52</c>). Characters here
/// arrive on <see cref="OnTextInput"/> and Backspace and Escape on <see cref="OnKeyDown"/>, so there
/// is no paste route, no undo stack, no context menu and no drop target to close — the same answer
/// this port's lock card reached (<c>Effects/LockCardTyping.cs:31-40</c>).</para>
///
/// <para><b>What this window does NOT do</b>, each decided somewhere it can be read: it makes no
/// sound (<see cref="MantraIntensity"/>), it runs no storyboard (<c>MantraWindow.axaml</c>'s own
/// header), and it opens no XP ledger of its own — <see cref="MantraLaunch"/> owns that, for the
/// life of one window, which is the shape <c>Features/Progression/ProgressionLedger.Open</c>
/// describes.</para>
/// </summary>
public partial class MantraWindow : Window
{
    /// <summary>How long the completion overlay stays up before the window closes itself —
    /// upstream's <c>DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }</c>
    /// (<c>Windows/MantraWindow.xaml.cs:301-307</c>). Any key closes it sooner (<c>:449-453</c>).
    /// </summary>
    public static readonly TimeSpan CompletionAutoClose = TimeSpan.FromSeconds(5);

    private readonly MantraSession _session;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _closeTimer;
    private bool _sessionComplete;

    /// <param name="session">The run this window plays. Constructed already started, which is what
    /// removes upstream's one documented footgun — its window reads <c>CurrentMantra</c> and
    /// <c>TargetCount</c> off a service in <c>Window_Loaded</c> and has "always assumed a session
    /// was already running" (<c>MainWindow/MainWindow.PlayTab.cs:272-275</c>).</param>
    public MantraWindow(MantraSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        InitializeComponent();

        // Upstream restarts a 5s DispatcherTimer on every change to the box (:78-80, :213-214) and
        // breaks the streak when it fires (:200-206). Same timer, same interval, restarted at the
        // same moments — but the DECISION is the session's clock comparison, so a tick that arrives
        // early cannot take a streak the user has just fed.
        _idleTimer = new DispatcherTimer { Interval = MantraSession.IdleTimeout };
        _idleTimer.Tick += (_, _) =>
        {
            if (_session.BreakStreakIfIdle())
            {
                Repaint();
            }
        };

        _closeTimer = new DispatcherTimer { Interval = CompletionAutoClose };
        _closeTimer.Tick += (_, _) => Close();                                // :301-307
    }

    /// <summary>The run this window is playing, for a caller that wants the outcome after the
    /// window closes.</summary>
    public MantraSession Session => _session;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Focus();                                                              // :90
        _idleTimer.Start();                                                   // :78-80
        Repaint();                                                            // :66-70
    }

    protected override void OnClosed(EventArgs e)
    {
        // Upstream's CleanupAndClose (:456-471): stop both timers, then end the run.
        _idleTimer.Stop();
        _closeTimer.Stop();
        _session.EndSession();
        base.OnClosed(e);
    }

    /// <summary>Escape leaves, and once the run is over any key does (<c>:440-454</c>). Backspace is
    /// the one editing key the box has.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();                                                          // :442-447
            return;
        }

        if (_sessionComplete)
        {
            e.Handled = true;
            Close();                                                          // :449-453
            return;
        }

        if (e.Key == Key.Back)
        {
            e.Handled = true;
            Step(_session.Apply('\0', isCharacter: false, isBackspace: true, isCancel: false));
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>One delivered character — or several, because a platform text-input event carries a
    /// STRING (an IME commit, a dead-key composition, or a paste on a platform that routes one
    /// through here). Each character is fed to the session in turn, so a multi-character delivery
    /// cannot slip past the per-character match.</summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (_sessionComplete || string.IsNullOrEmpty(e.Text))
        {
            base.OnTextInput(e);
            return;
        }

        e.Handled = true;
        var step = MantraStep.Ignored;
        foreach (var character in e.Text)
        {
            var applied = _session.Apply(character, isCharacter: true, isBackspace: false, isCancel: false);
            if (applied != MantraStep.Ignored)
            {
                step = applied;
            }
        }

        Step(step);
    }

    private void Step(MantraStep step)
    {
        if (step == MantraStep.Ignored)
        {
            return;
        }

        // The idle window restarts on every change to the box, exactly where upstream restarts it
        // (:213-214) — including a change that produced a mistake or a refused completion.
        _idleTimer.Stop();
        _idleTimer.Start();

        if (step == MantraStep.SessionComplete)
        {
            // :286-308. The overlay goes up, input stops counting, and the window closes itself
            // five seconds later unless a key gets there first.
            _sessionComplete = true;
            _idleTimer.Stop();
            CompletionStatsText.Text =
                $"{_session.Completions} repetitions  |  Best streak: {_session.BestStreak}";  // :293
            CompletionOverlay.IsVisible = true;                               // :294
            _closeTimer.Start();                                              // :301-307
        }

        Repaint();
    }

    /// <summary>
    /// Paint the whole surface from the session. Upstream splits this across four event handlers
    /// (<c>:245-284</c>) and one rebuild (<c>:95-111</c>); one repaint is the same picture and
    /// cannot leave two of the four disagreeing.
    /// </summary>
    private void Repaint()
    {
        var intensity = MantraIntensity.For(_session.Streak);                 // :310-313

        CompletionsText.Text = _session.Completions.ToString();               // :251
        TargetText.Text = $"/{_session.TargetCount}";                         // :67
        StreakText.Text = _session.Streak.ToString();                         // :265
        BestStreakText.Text = _session.BestStreak.ToString();                 // :266
        AnswerText.Text = _session.Answer;

        BuildMantraRuns(intensity);

        // :339-341 — the glow. Avalonia's DropShadow is a render effect on the control rather than
        // a brush, so the radius and colour ride the same numbers upstream's DropShadowEffect does.
        MantraText.Effect = new DropShadowEffect
        {
            BlurRadius = intensity.GlowBlurRadius,
            OffsetX = 0,
            OffsetY = 0,
            Opacity = intensity.GlowOpacity,
            Color = ToColor(intensity.GlowColour),
        };

        // :347 and markup :66-69 — the backdrop's centre warms, its edge does not.
        BaseLayer.Background = Radial(
            ToColor(intensity.BaseCentre), ToColor(MantraIntensity.BaseEdge), 0.7);

        // :335-336 — the wash.
        WashLayer.Opacity = intensity.WashOpacity;
        WashLayer.Background = Radial(ToColor(intensity.WashCentre), Colors.Transparent, 0.6);

        // :344 — the box's border fades in without changing hue.
        InputBand.BorderBrush = new SolidColorBrush(ToColor(intensity.InputBorder));
    }

    /// <summary>
    /// One <see cref="Run"/> per character of the mantra, coloured by
    /// <see cref="MantraSession.StateOf"/> — upstream's <c>BuildMantraRuns</c> plus the colouring
    /// loop of <c>UpdateHighlights</c> (<c>:95-111</c>, <c>:133-144</c>), collapsed because upstream
    /// only ever runs them together.
    ///
    /// <para>The collection is rebuilt rather than recoloured in place. A mantra is a sentence, the
    /// rebuild is a few dozen <c>Run</c>s, and it cannot leave a stale <c>Run</c> behind when the
    /// mantra changes under it — which is the bug upstream's own <c>_mantraRuns</c> list exists to
    /// avoid.</para>
    /// </summary>
    private void BuildMantraRuns(MantraIntensity intensity)
    {
        var mantra = _session.CurrentMantra ?? string.Empty;                  // :66, :250
        var match = _session.CurrentMatch;

        var matched = new SolidColorBrush(ToColor(intensity.Highlight));
        var dim = new SolidColorBrush(ToColor(MantraIntensity.Dim));
        var wrong = new SolidColorBrush(ToColor(MantraIntensity.Wrong));

        var inlines = new InlineCollection();
        for (var i = 0; i < mantra.Length; i++)
        {
            inlines.Add(new Run(mantra[i].ToString())
            {
                Foreground = MantraSession.StateOf(i, match) switch
                {
                    MantraCharState.Matched => matched,
                    MantraCharState.Wrong => wrong,
                    _ => dim,
                },
            });
        }

        MantraText.Inlines = inlines;
    }

    private static Color ToColor(MantraColour colour) =>
        Color.FromArgb(colour.A, colour.R, colour.G, colour.B);

    private static RadialGradientBrush Radial(Color centre, Color edge, double radius) => new()
    {
        Center = RelativePoint.Center,
        GradientOrigin = RelativePoint.Center,
        RadiusX = new RelativeScalar(radius, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(radius, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(centre, 0),
            new GradientStop(edge, 1),
        ],
    };
}
