using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace CcpClient.Desktop.Views;

/// <summary>
/// The four kinds a toast can be, and upstream's own enum
/// (<c>Services/Notifications/NotificationService.cs:12</c>). Order is upstream's; the accent for
/// each is in <c>ToastHost.axaml</c>.
/// </summary>
public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// <b>The in-app toast surface</b> — how this app tells the user something without a modal
/// (census #54, merged with #83). Upstream is
/// <c>Services/Notifications/NotificationService.cs</c>, a service that attaches itself to a
/// <c>StackPanel</c> at the top-right of the shell's root grid
/// (<c>MainWindow/MainWindow.xaml.cs:2745</c>, <c>MainWindow/MainWindow.xaml:3217-3219</c>). Here it
/// IS that panel, because the port's host is created with the window rather than statically before
/// it — which is also why upstream's replay queue (<c>NotificationService.cs:29-43</c>) is not
/// ported: nothing here can fire before the host exists.
///
/// <para><b>This is not the tray balloon.</b> <c>Tray/TrayNotification.cs</c> is the OS shell's
/// surface and is owned by the desktop; this one lives inside the window, stacks, and is dismissed
/// by the user rather than by the shell.</para>
///
/// <para><b>What is deliberately NOT ported, and why.</b> Upstream's <c>ShowSticky</c> and its
/// persisted <c>DismissedNotificationKeys</c> (<c>:61-68</c>, <c>:216-226</c>) belong to the
/// notification-settings census entry, which is DEFERRED; a dismissal that outlives the process
/// with no settings surface to review it would be a preference the user can set once and never
/// find again. Upstream's optional ACTION BUTTON (<c>:175-198</c>) has exactly one consumer,
/// <c>Services/TierGate.cs:133</c>'s "See tiers", which opens an App Info &amp; Data page this port
/// does not have; the refusal itself now arrives here, but that button would have nowhere to go, so
/// it is absent rather than stubbed and the route is named in words instead
/// (<c>Views/Pages/PlayPage.axaml</c> records the divergence).</para>
///
/// <para><b>The auto-dismiss timing is injected.</b> <see cref="Schedule"/> defaults to
/// <c>DispatcherTimer.RunOnce</c>, which fires on the UI thread — so nothing here marshals, and a
/// test drives the elapse itself rather than waiting for it. The seam is a delegate rather than
/// <see cref="Session.ISessionClock"/> for the reason that interface's own doc gives for not
/// reusing <c>Audio/ISoundClock</c>: a surface that owns one timer should not take on another
/// subsystem's dependency to get it.</para>
///
/// <para><b>ONE TOAST IS ON SCREEN AT A TIME, AND THE REST WAIT THEIR TURN.</b> Upstream appends
/// every notification to its host panel and lets them stack (<c>NotificationService.cs:91</c>),
/// which is free there because its host sits over empty chrome; this port's host floats over the
/// PAGE (<c>Views/MainWindow.axaml</c>, <c>ToastLayer</c>), so a stack walks up into the page's own
/// controls. Coalescing (<see cref="Show"/>) fixed the repeated sentence and did not fix three
/// DIFFERENT ones. Measured at the shell's own 1100x760, on the path the user really has — export
/// phrases, import phrases (both dismiss-only, neither expires), then a refused launch — three
/// notices occupied y 188..600 of a 610-DIP page area and covered BOTH launch buttons on the Play
/// card: <c>FALL IN</c> at 883,157 and <c>Quick Drop</c> at 905,211.</para>
///
/// <para><b>A CAP WAS REFUSED AND STILL IS.</b> Upstream has no number to port, and dropping an
/// unacknowledged notice to make room is itself a defect — a user who never saw the import result
/// is worse off than one whose toast overlapped a button. So nothing is ever dropped: a notice that
/// cannot be shown yet is still owed, is still in <see cref="Messages"/>, and takes the screen the
/// moment the one above it goes. What IS bounded is the FOOTPRINT, and bounding it at one is what
/// makes <c>TierRefusalRouteHeadlessTests</c>'s single-toast clearance measurement a fact about the
/// whole surface instead of about its best case.</para>
///
/// <para><b>The NEWEST takes the screen, not the oldest, and that is the census #41 guarantee
/// rather than a preference.</b> A refusal is tied to the press that raised it
/// (<c>Services/TierGate.cs:126-134</c>); queued behind an export result the user has not closed,
/// it would arrive minutes later attached to nothing, which is the "no dialog, no toast and nothing
/// tying the jump to the card they clicked" defect upstream recorded in its own words
/// (<c>MainWindow/MainWindow.Lab.cs:282-288</c>). A displaced TIMED toast keeps its clock running
/// while it waits, so an eight-second announcement cannot resurface stale; a displaced DISMISS-ONLY
/// one has no clock and therefore always gets its turn. That pairing is the whole policy.</para>
/// </summary>
public partial class ToastHost : UserControl
{
    /// <summary>How long a toast stays before it fades itself out, when the caller does not say.
    /// Upstream's default (<c>NotificationService.cs:49</c>).</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    /// <summary>The AutomationId every toast's message carries, so the headed harness can read what
    /// the app actually said before it reads a pixel.</summary>
    public const string MessageAutomationId = "ToastMessage";

    /// <summary>The AutomationId every toast's dismiss button carries.</summary>
    public const string DismissAutomationId = "ToastDismiss";

    /// <summary>
    /// Everything the surface still owes the user, oldest first. Exactly one of these — the last —
    /// is a child of <c>ToastStack</c> and therefore on screen; the others are detached, so they
    /// occupy no layout, capture no pointer and expose no automation peer while they wait.
    /// </summary>
    private readonly List<Border> _pending = [];

    public ToastHost() => InitializeComponent();

    /// <summary>
    /// The one-shot timer seam. Production is <c>DispatcherTimer.RunOnce</c> (UI thread, cancelled
    /// by disposing the handle); a test replaces it to make the elapse a deterministic signal
    /// instead of a wall-clock wait.
    /// </summary>
    public Func<TimeSpan, Action, IDisposable> Schedule { get; set; } =
        static (due, fire) => DispatcherTimer.RunOnce(fire, due);

    /// <summary>Everything this surface still owes the user, oldest first — upstream appends
    /// (<c>NotificationService.cs:91</c>), so the newest is last. Only the last is ON SCREEN; the
    /// rest are waiting, and they are listed because a notice that is owed has not been dropped.
    /// A sentence said again is moved to the end rather than duplicated, so this order is
    /// last-said-last, not first-said-last.</summary>
    public IReadOnlyList<string> Messages =>
        [.. _pending.Select(toast => MessageOf(toast) ?? string.Empty)];

    /// <summary>
    /// Says something, non-blocking, and takes it away again after <paramref name="duration"/>.
    /// Upstream's <c>Show</c> (<c>NotificationService.cs:45-50</c>).
    /// </summary>
    public void Show(string message, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        // THE SAME SENTENCE TWICE IS ONE TOAST WITH A FRESH CLOCK, NOT TWO TOASTS. Upstream states
        // the rule for its keyed toasts - already showing is a no-op (NotificationService.cs:110) -
        // and this port needs it for the unkeyed ones as well, because ITS host floats over the
        // page's own controls rather than over empty chrome. The timer is REPLACED rather than left
        // alone so the newest press still gets its full duration: a refusal that expired a second
        // after the user asked again would announce less than upstream does. Bring() puts it back on
        // screen for the same reason - a re-armed clock on a toast the user cannot see would
        // announce nothing at all.
        var toast = Bring(Existing(message) ?? Add(message, kind));
        (toast.Tag as IDisposable)?.Dispose();

        // Upstream stashes the timer on the toast itself so the dismiss handler can stop it rather
        // than leak it for the rest of the window (NotificationService.cs:100-104, :215). It keeps
        // running if a later notice takes the screen, so a timed announcement cannot resurface long
        // after the press it belongs to.
        toast.Tag = Schedule(duration ?? DefaultDuration, () => Remove(toast));
    }

    /// <summary>
    /// Says something that stays until the user takes it away.
    ///
    /// <para>This is the shape upstream's phrase export and import results have: they are reported
    /// through a MODAL dialog with an OK button (<c>MainWindow/MainWindow.PresetIO.cs:81-83</c>,
    /// <c>:125-127</c>), which by construction does not disappear on its own. The port answers
    /// without a modal — that is the whole point of this surface — so the acknowledgement it owes
    /// is a toast the user closes, not one that expires while they are reading it. It carries no
    /// key and persists nothing, so it is NOT upstream's <c>ShowSticky</c>
    /// (<c>NotificationService.cs:61</c>).</para>
    /// </summary>
    public void ShowUntilDismissed(string message, ToastKind kind)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Still owed and not yet acknowledged: a second copy of the same sentence says nothing new,
        // so it is the SAME toast, brought back to the front. Upstream's own rule for a toast that
        // outlives its own call is that an existing one is a no-op
        // (<c>NotificationService.cs:110</c>); here "existing" splits into on-screen and waiting,
        // and only the waiting case has anything left to do.
        Bring(Existing(message) ?? Add(message, kind));
    }

    /// <summary>Takes every toast away, including the ones still waiting. The shell has no
    /// consumer; a test uses it to reset.</summary>
    public void DismissAll()
    {
        foreach (var toast in _pending.ToArray())
        {
            Remove(toast);
        }
    }

    /// <summary>The toast already saying this, on screen or waiting. Matched on the sentence rather
    /// than on a key because the port's callers have no keys — see <see cref="Show"/>.</summary>
    private Border? Existing(string message) =>
        _pending.FirstOrDefault(
            toast => string.Equals(MessageOf(toast), message, StringComparison.Ordinal));

    /// <summary>Puts <paramref name="toast"/> at the front of the queue and therefore on screen,
    /// displacing whatever was there into the wait behind it.</summary>
    private Border Bring(Border toast)
    {
        if (_pending.Count > 0 && ReferenceEquals(_pending[^1], toast))
        {
            return toast;   // already the one on screen
        }

        _pending.Remove(toast);
        _pending.Add(toast);
        Reveal();
        return toast;
    }

    /// <summary>Makes the stack hold exactly the newest owed toast and nothing else.</summary>
    private void Reveal()
    {
        var showing = _pending.Count > 0 ? _pending[^1] : null;
        if (ToastStack.Children.Count == 1 && ReferenceEquals(ToastStack.Children[0], showing))
        {
            return;
        }

        ToastStack.Children.Clear();
        if (showing is not null)
        {
            ToastStack.Children.Add(showing);
        }
    }

    private Border Add(string message, ToastKind kind)
    {
        var text = new TextBlock { Text = message };
        text.Classes.Add("toast-message");
        text.SetValue(AutomationProperties.AutomationIdProperty, MessageAutomationId);
        Grid.SetColumn(text, 0);

        var dismiss = new Button { Content = "×" };   // upstream's glyph (:202)
        dismiss.Classes.Add("toast-dismiss");
        dismiss.SetValue(AutomationProperties.AutomationIdProperty, DismissAutomationId);
        dismiss.SetValue(AutomationProperties.NameProperty, "Dismiss");
        Grid.SetColumn(dismiss, 1);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        body.Children.Add(text);
        body.Children.Add(dismiss);

        var toast = new Border { Child = body };
        toast.Classes.Add("toast");
        toast.Classes.Add(kind switch
        {
            ToastKind.Success => "success",
            ToastKind.Warning => "warning",
            ToastKind.Error => "error",
            _ => "info",
        });

        dismiss.Click += (_, _) => Remove(toast);
        _pending.Add(toast);
        Reveal();
        return toast;
    }

    private void Remove(Border toast)
    {
        // Stop the timer whether the user got there first or it fired: a disposed handle that
        // already fired is a no-op, and one that has not is the leak upstream stops at :215.
        (toast.Tag as IDisposable)?.Dispose();
        toast.Tag = null;
        _pending.Remove(toast);
        ToastStack.Children.Remove(toast);

        // Whatever was waiting behind it now takes the screen. Each toast is still dismissed on its
        // own (upstream's :213-228) — closing one never clears the ones the user has not read.
        Reveal();
    }

    private static string? MessageOf(Control toast) =>
        toast.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
}
