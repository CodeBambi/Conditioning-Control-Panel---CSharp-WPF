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

    public ToastHost() => InitializeComponent();

    /// <summary>
    /// The one-shot timer seam. Production is <c>DispatcherTimer.RunOnce</c> (UI thread, cancelled
    /// by disposing the handle); a test replaces it to make the elapse a deterministic signal
    /// instead of a wall-clock wait.
    /// </summary>
    public Func<TimeSpan, Action, IDisposable> Schedule { get; set; } =
        static (due, fire) => DispatcherTimer.RunOnce(fire, due);

    /// <summary>What is on screen right now, oldest first — upstream appends
    /// (<c>NotificationService.cs:91</c>), so the newest toast is at the bottom of the stack.</summary>
    public IReadOnlyList<string> Messages =>
        [.. ToastStack.Children.Select(child => MessageOf(child) ?? string.Empty)];

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
        // page's own controls rather than over empty chrome. Four presses on a refused button would
        // otherwise build a stack tall enough to reach the button being pressed, which is exactly
        // the defect the bottom dock exists to prevent (Views/MainWindow.axaml, ToastLayer). The
        // timer is REPLACED rather than left alone so the newest press still gets its full
        // duration: a refusal that expired a second after the user asked again would announce less
        // than upstream does.
        var toast = Existing(message) ?? Add(message, kind);
        (toast.Tag as IDisposable)?.Dispose();

        // Upstream stashes the timer on the toast itself so the dismiss handler can stop it rather
        // than leak it for the rest of the window (NotificationService.cs:100-104, :215).
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

        // Already on screen and waiting to be acknowledged: there is no timer to refresh and a
        // second copy of the same sentence says nothing new. Upstream's own rule for a toast that
        // outlives its own call (<c>NotificationService.cs:110</c>).
        if (Existing(message) is null)
        {
            Add(message, kind);
        }
    }

    /// <summary>Takes every toast away. The shell has no consumer; a test uses it to reset.</summary>
    public void DismissAll()
    {
        foreach (var toast in ToastStack.Children.ToArray())
        {
            Remove(toast);
        }
    }

    /// <summary>The toast already saying this, if one is up. Matched on the sentence rather than on
    /// a key because the port's callers have no keys — see <see cref="Show"/>.</summary>
    private Border? Existing(string message) =>
        ToastStack.Children.OfType<Border>().FirstOrDefault(
            toast => string.Equals(MessageOf(toast), message, StringComparison.Ordinal));

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
        ToastStack.Children.Add(toast);
        return toast;
    }

    private void Remove(Control toast)
    {
        // Stop the timer whether the user got there first or it fired: a disposed handle that
        // already fired is a no-op, and one that has not is the leak upstream stops at :215.
        (toast.Tag as IDisposable)?.Dispose();
        toast.Tag = null;
        ToastStack.Children.Remove(toast);
    }

    private static string? MessageOf(Control toast) =>
        toast.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
}
