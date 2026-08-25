using System.Globalization;
using Avalonia.Controls;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Goon;
using CcpClient.Desktop.Features.Mantra;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// Hop 2 of the DTRH route (wpf-surface-reachability.md §3 @ 7527243e7): rail door <c>Play</c> -> this
/// page -> <c>FALL IN</c> / <c>Quick Drop</c> -> <see cref="DtrhLaunch"/>. The door navigates
/// and opens nothing; the two buttons here are the port's only DTRH launchers, which is WPF's
/// one-entry rule (<c>MainWindow/MainWindow.Presets.cs:1007</c>) and matches WPF's own count:
/// <c>DtrhHostService.Launch</c> has exactly these two in-app callers
/// (<c>MainWindow.Lab.cs:239,252,321</c> plus the CLI).
///
/// <para>This class RENDERS a decision it does not make. The gate is
/// <see cref="DtrhGate"/> — a pure function proved in the unit suite — so the three branches
/// cannot be quietly collapsed here: the page switches on the decision's TYPE, and the two
/// refusal types are different types precisely so one cannot be rendered as the other.</para>
/// </summary>
public partial class PlayPage : UserControl
{
    /// <summary>WPF's band caption for the tier-2 lock, <c>hm3_rail_lock_t2</c>
    /// (<c>en.json:4842</c>), shown when an authority actually answered about this account.</summary>
    public const string NotEntitledBandTitle = "LAB ONLY";

    /// <summary>The band caption for the branch WPF does not have. It must never read like a
    /// refusal of the person: nothing was determined about them (§10 D21).</summary>
    public const string UnverifiedBandTitle = "COULD NOT VERIFY";

    /// <summary>WPF's own failure headline, <c>MainWindow/MainWindow.Lab.cs:269</c>. It is not a
    /// caption for a refusal and must never be reused as one.</summary>
    public const string FaultBandTitleText = LaunchFaultText.DtrhHeadline;

    /// <summary>
    /// How long the tier refusal stays on the toast surface, and it is WPF's own number:
    /// <c>ShowDenied</c> passes <c>NotificationType.Warning, TimeSpan.FromSeconds(8)</c>
    /// (<c>Services/TierGate.cs:133</c>). Not the host's 5-second default, because a refusal is
    /// longer than an acknowledgement and upstream already decided by how much.
    /// </summary>
    public static readonly TimeSpan TierRefusalToastDuration = TimeSpan.FromSeconds(8);

    private readonly ToastHost _toasts;

    public PlayPage(DtrhLaunch dtrh, GoonLaunch goon, ArcademyLaunch arcademy, MantraLaunch mantra, ToastHost toasts)
    {
        ArgumentNullException.ThrowIfNull(dtrh);
        ArgumentNullException.ThrowIfNull(goon);
        ArgumentNullException.ThrowIfNull(arcademy);
        ArgumentNullException.ThrowIfNull(mantra);
        ArgumentNullException.ThrowIfNull(toasts);
        _toasts = toasts;
        InitializeComponent();

        // THE ARCADEMY STRIP IS DARK AND IS RE-ASSERTED DARK. Upstream ships the card
        // Visibility="Collapsed" (Views/Tabs/PlayTabView.xaml:1312) and RefreshPlayCards writes
        // that visibility from ArcademyHostService.DoorAvailable on EVERY repaint
        // (MainWindow/MainWindow.PlayTab.cs:106-112) — "flipping DoorAvailable is the whole
        // reveal". This is the port's repaint: the page is mounted each time the Play door is
        // chosen, so a hand-edited IsVisible cannot survive a navigation, and nothing else in the
        // build writes it. Absent, never locked: a lockband would advertise a feature nobody can
        // buy yet, which is upstream's stated reason for hiding rather than locking (:105-107).
        ApplyArcademyDoor();
        AttachedToVisualTree += (_, _) => ApplyArcademyDoor();

        // The one Arcademy entry, and it takes the click rather than being disabled — WPF's
        // BtnStartArcademy_Click is a try/catch around Launch() and nothing else
        // (MainWindow/MainWindow.Lab.cs:302-321), because "the code path which actually opens the
        // door has to be the one that can say no". Fire-and-forget for the reason the two DTRH
        // buttons are: the tier bar resolves asynchronously and a click handler may not block the
        // UI thread. Discarding is safe because AttendAsync types its own faults instead of
        // letting one escape (ArcademyAttendOutcome.Faulted).
        ArcademyAttendButton.Click += (_, _) => _ = AttendAsync(arcademy);

        // The Goon door. Synchronous, because GoonLaunch.Practice is - there is no gate
        // to resolve (upstream's card is ungated, PlayTabView.xaml:547-549) and no async read to
        // wait on. It never throws: a fault arrives on Faulted and is rendered below rather than
        // vanishing into an unobserved task, which is the launch-fault lesson applied to a second door.
        GoonPracticeButton.Click += (_, _) => goon.Practice();
        goon.Faulted += RenderGoonFault;

        // THE MANTRA DOOR, and the restoration this page exists to make. Synchronous, like the
        // Goon door beside it: upstream's StartMantraSession has no gate to resolve and no async
        // read to wait on either (MainWindow/MainWindow.PlayTab.cs:287-315), and the game is free
        // by design (:282-285). Its single-tenancy is the LAUNCHER's, not this page's - a second
        // press focuses the live window rather than restarting it, because "a second StartSession
        // would reset Completions and Streak mid-run, i.e. silently delete the user's progress"
        // (:294-303).
        MantraBeginButton.Click += (_, _) => BeginMantra(mantra);

        // Fire-and-forget on purpose: the gate resolves asynchronously (it reads the shipping
        // app's login) and a click handler may not block the UI thread. The awaited work
        // resumes on this thread, so Decided lands here and renders below. Neither button is
        // ever disabled, in any branch — a gated press must ARRIVE (PlayTabView.xaml:503-506).
        //
        // Discarding the task is SAFE ONLY BECAUSE the launcher no longer lets one fault escape
        // it: DtrhLaunch wraps the whole flow the way WPF wraps its whole handler
        // (MainWindow.Lab.cs:221-271) and raises Faulted. Before that, a throw from the descent
        // landed in TaskScheduler.UnobservedTaskException at some later GC and the user saw
        // nothing at all.
        FallInButton.Click += (_, _) => _ = dtrh.FallInAsync();
        QuickDropButton.Click += (_, _) => _ = dtrh.QuickDropAsync();

        dtrh.Decided += Render;
        dtrh.Faulted += RenderFault;
    }

    /// <summary>The door, re-asserted onto the strip. The ONLY writer of this visibility, and it
    /// reads <see cref="ArcademyDoor.Available"/> — a <c>static readonly false</c> with no
    /// override seam anywhere in this build.</summary>
    private void ApplyArcademyDoor() => ArcademyEntry.IsVisible = ArcademyDoor.Available;

    /// <summary>
    /// One Attend press. The refusal it renders is the TIER bar's; the DOOR's refusal renders
    /// nothing at all, exactly as upstream's is silent — "there is no announced feature to explain
    /// a refusal about yet" (<c>ArcademyHostService.cs:139-141</c>) — and it is unreachable from
    /// here anyway while the strip is dark.
    /// </summary>
    private async Task AttendAsync(ArcademyLaunch arcademy)
    {
        var outcome = await arcademy.AttendAsync().ConfigureAwait(true);
        ArcademyGateText.IsVisible = outcome is ArcademyLaunch.ArcademyAttendOutcome.Gated;
        ArcademyGateText.Text = outcome switch
        {
            // The two refusals are different TYPES on the decision precisely so this line cannot
            // render "I could not tell" as "you are not a patron".
            ArcademyLaunch.ArcademyAttendOutcome.Gated { Decision: ArcademyGateDecision.RefusedNotEntitled r } => r.Message,
            ArcademyLaunch.ArcademyAttendOutcome.Gated { Decision: ArcademyGateDecision.RefusedUnverified u } => u.Message,
            _ => string.Empty,
        };
    }

    private void Render(DtrhGateDecision decision)
    {
        // A gate decision is the newest thing known about this card, so a failure plate from an
        // earlier press is stale — including on the SAME press, where Decided fires before the
        // descent that may fault.
        ClearFault();

        switch (decision)
        {
            case DtrhGateDecision.Proceed:
                // The hole is opening. Any band from an earlier refusal is stale the instant a
                // later press succeeds — leaving it up would tell the user they were refused
                // while the descent runs.
                GateBand.IsVisible = false;
                GateBandTitle.Text = string.Empty;
                GateBandText.Text = string.Empty;
                break;

            case DtrhGateDecision.RefusedNotEntitled refused:
                Show(NotEntitledBandTitle, refused.Message);

                // AND IT IS SAID OUT LOUD, which is upstream's own lesson rather than a flourish:
                // a bare refusal with "no dialog, no toast and nothing tying the jump to the card
                // they clicked" is the defect ShowDenied was written to fix
                // (MainWindow/MainWindow.Lab.cs:282-288, Services/TierGate.cs:126-134). Upstream's
                // severity and duration, verbatim; upstream's "See tiers" ACTION is still absent,
                // because it opens App Info & Data and this port has no such page — the route is
                // named in words inside the message instead (DtrhGate.UpgradeRoute).
                //
                // This is ONLY the not-entitled branch, exactly as upstream: ShowDenied is reached
                // from DemandLab's refusal and nothing else. The port's third answer is not a
                // refusal of the person and does not borrow a refusal's announcement.
                _toasts.Show(refused.Message, ToastKind.Warning, TierRefusalToastDuration);
                break;

            case DtrhGateDecision.RefusedUnverified unverified:
                Show(UnverifiedBandTitle, unverified.Message);
                break;
        }
    }

    /// <summary>
    /// The launch threw. WPF's counterpart is a modal warning dialog carrying the same headline
    /// and the exception's message (<c>MainWindow/MainWindow.Lab.cs:266-271</c>).
    ///
    /// <para>The refusal band comes DOWN first, unconditionally. The two surfaces mean different
    /// things — "we could not determine your entitlement" and "the app broke" — and showing both
    /// at once would put the user back where a single shared band would have left them.</para>
    /// </summary>
    private void RenderFault(Exception exception)
    {
        GateBand.IsVisible = false;
        GateBandTitle.Text = string.Empty;
        GateBandText.Text = string.Empty;

        FaultBandTitle.Text = FaultBandTitleText;
        FaultBandText.Text = LaunchFaultText.Compose(FaultBandTitleText, exception);
        FaultBand.IsVisible = true;
    }

    private void ClearFault()
    {
        FaultBand.IsVisible = false;
        FaultBandTitle.Text = string.Empty;
        FaultBandText.Text = string.Empty;
    }

    private void Show(string title, string message)
    {
        GateBandTitle.Text = title;
        GateBandText.Text = message;
        GateBand.IsVisible = true;
    }

    /// <summary>
    /// The Goon launch threw. Its own line under its own card: a fault is not a refusal, and the
    /// four Goon refusals are not faults - they live on the host window's rail and are reached by
    /// a launch that WORKED. Rendering either as the other is exactly what the DTRH card's two
    /// separate bands above exist to prevent.
    /// </summary>
    private void RenderGoonFault(Exception exception)
    {
        GoonFaultText.Text = LaunchFaultText.Compose(GoonFaultHeadline, exception);
        GoonFaultText.IsVisible = true;
    }

    /// <summary>
    /// One Begin press. Upstream reads its picker and hands the count to the launcher
    /// (<c>Views/Tabs/PlayTabView.Cards.cs:107-117</c> at <c>a9859e7b6^</c>), which is the whole
    /// of what this does - every rule the run obeys belongs to <c>MantraSession</c>.
    ///
    /// <para>A fault gets this card's own line, and a LATER success takes it back down: the plate
    /// is the newest thing known about this door, and leaving a stale failure under a window that
    /// just opened would be telling the user the app is broken while she types into it. Same rule
    /// the DTRH card's <see cref="ClearFault"/> follows above.</para>
    /// </summary>
    private void BeginMantra(MantraLaunch mantra)
    {
        // Null is the ONLY faulted answer: the launcher types its own faults rather than letting
        // one escape into the click (MantraLaunch.Open's catch), which is the launch-fault lesson
        // applied to a third door.
        if (mantra.Open(SelectedMantraReps()) is null && mantra.LastFault is { } fault)
        {
            MantraFaultText.Text = LaunchFaultText.Compose(MantraFaultHeadline, fault);
            MantraFaultText.IsVisible = true;
            return;
        }

        MantraFaultText.Text = string.Empty;
        MantraFaultText.IsVisible = false;
    }

    /// <summary>
    /// The picker's answer. Upstream's four values with upstream's own fallback: it reads the
    /// <c>ComboBoxItem</c>'s <c>Tag</c> and keeps <see cref="DefaultCardReps"/> when the read
    /// fails, so a picker that somehow has nothing selected still starts a run rather than
    /// swallowing the press (<c>PlayTabView.Cards.cs:109-115</c> at <c>a9859e7b6^</c>).
    /// </summary>
    private int SelectedMantraReps() =>
        MantraRepsPicker.SelectedItem is ComboBoxItem { Tag: string tag }
        && int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var reps)
            ? reps
            : DefaultCardReps;

    /// <summary>Upstream's card default - its picker opens on <c>SelectedIndex="1"</c>, the second
    /// of 10/25/50/100, and its handler falls back to the same 25
    /// (<c>PlayTabView.Cards.cs:109</c> at <c>a9859e7b6^</c>). It is deliberately NOT
    /// <c>MantraSession.DefaultTargetReps</c>: that is the launcher's answer when no card asked,
    /// and this card always asks.</summary>
    public const int DefaultCardReps = 25;

    /// <summary>WPF's own failure headline for the mantra launch, verbatim from
    /// <c>MainWindow/MainWindow.PlayTab.cs:311</c> minus its trailing colon (the colon joins
    /// headline to detail in <see cref="LaunchFaultText.Compose"/>, exactly as WPF's
    /// concatenation does). Kept here rather than in <see cref="LaunchFaultText"/> for the reason
    /// <see cref="GoonFaultHeadline"/> is.</summary>
    public const string MantraFaultHeadline = "Couldn't start the mantra session";

    /// <summary>WPF's own failure headline for this card, verbatim from
    /// <c>MainWindow/MainWindow.Lab.cs:207</c> minus its trailing colon (the colon joins headline
    /// to detail in <see cref="LaunchFaultText.Compose"/>, exactly as WPF's concatenation does).
    /// Note the verb: the Goon card says "open", where the DTRH and intake cards say "start".
    /// It is not kept in <see cref="LaunchFaultText"/> because that file is outside this
    /// packet's file scope; the composed body still runs through the shared helper, so the
    /// empty-line layout hazard it guards cannot arrive by this route.</summary>
    public const string GoonFaultHeadline = "Couldn't open the Goon Game";
}
