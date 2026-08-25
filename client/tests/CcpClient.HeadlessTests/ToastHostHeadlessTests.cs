using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The in-app toast surface (census #54, merged with #83) — the shell's one way of saying something
/// without a modal. Upstream is <c>Services/Notifications/NotificationService.cs</c>, attached at
/// the top-right of its root grid (<c>MainWindow/MainWindow.xaml.cs:2745</c>).
///
/// <para>Every fact here drives the REAL shell: the host is the one
/// <see cref="MainWindow.Toasts"/> the window declares, and the toasts are the ones its own code
/// builds. Draw-level ONLY (verification-harness.md evidence class) — visual tree, style-resolved
/// brushes, hit-test routing and real input. <b>Nothing here claims a composited pixel</b>; that a
/// toast is legible where it is drawn is the headed <c>toast</c> surface's job.</para>
/// </summary>
public class ToastHostHeadlessTests : HeadlessTest
{
    private async Task<(ApplicationHost Host, MainWindow Window)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-toast-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return (host, window);
    }

    private static IReadOnlyList<Border> Toasts(MainWindow window) =>
        [.. window.Toasts.GetLogicalDescendants().OfType<Border>().Where(b => b.Classes.Contains("toast"))];

    private static Color AccentOf(Border toast) =>
        ((ISolidColorBrush)toast.BorderBrush!).Color;

    private static void Click(MainWindow window, Control control)
    {
        window.UpdateLayout();
        var centre = control.TranslatePoint(
                         new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
                     ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    /// <summary>
    /// The four kinds paint four DIFFERENT accents, and they are upstream's own hexes
    /// (<c>Services/Notifications/NotificationService.cs:120-126</c>). A refusal that looked like a
    /// success would be worse than no toast at all — the whole reason the type exists is that a
    /// user can tell which one they are reading without reading it.
    ///
    /// <para>All four are raised at once and read one at a time, because this host shows one at a
    /// time (<see cref="ToastHost"/>): the newest holds the screen and the rest wait. So the accents
    /// are read NEWEST FIRST, dismissing as it goes, which also means each colour is read off a
    /// toast that was really attached and really style-resolved rather than off a detached
    /// <c>Border</c> no style has reached.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TheFourKindsPaintUpstreamsFourAccents_AndNoTwoOfThemAreTheSame()
    {
        var (host, window) = await BootAsync();
        try
        {
            foreach (var kind in new[] { ToastKind.Info, ToastKind.Success, ToastKind.Warning, ToastKind.Error })
            {
                window.Toasts.ShowUntilDismissed(kind.ToString(), kind);
            }

            window.UpdateLayout();
            var accents = new List<Color>();
            while (window.Toasts.Messages.Count > 0)
            {
                var showing = Assert.Single(Toasts(window));
                accents.Insert(0, AccentOf(showing));
                Click(window, showing.GetLogicalDescendants().OfType<Button>().Single());
            }

            Assert.Equal(
                new[]
                {
                    Color.FromRgb(0xFF, 0x69, 0xB4),   // Info    — NotificationService.cs:125
                    Color.FromRgb(0x4C, 0xAF, 0x50),   // Success — :122
                    Color.FromRgb(0xFF, 0xB3, 0x47),   // Warning — :123
                    Color.FromRgb(0xFF, 0x6B, 0x6B),   // Error   — :124
                },
                accents);
            Assert.Equal(4, accents.Distinct().Count());
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// Each toast is dismissed on its own, and dismissing one NEVER takes away a message the user
    /// has not read. Upstream appends to its host panel (<c>NotificationService.cs:91</c>) and each
    /// toast's × removes only itself (<c>:213-228</c>).
    ///
    /// <para><b>Where this port diverges, and it is the whole of the divergence.</b> Upstream's
    /// three are on screen together; this host's are one at a time, newest on screen and the rest
    /// waiting, because its host floats over the PAGE and a stack of three covered the Play card's
    /// launch buttons (<see cref="ToastHost"/>, measured). So "leaves the others" is proved by the
    /// next one ARRIVING rather than by three plates being visible at once — which is the stronger
    /// half anyway: a queue that dropped what it could not show would pass a visibility check and
    /// fail a user.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task DismissingOneToastRemovesThatOne_AndLeavesTheOthersInOrder()
    {
        var (host, window) = await BootAsync();
        try
        {
            window.Toasts.ShowUntilDismissed("first", ToastKind.Info);
            window.Toasts.ShowUntilDismissed("second", ToastKind.Warning);
            window.Toasts.ShowUntilDismissed("third", ToastKind.Error);
            window.UpdateLayout();
            Assert.Equal(["first", "second", "third"], window.Toasts.Messages);

            // One plate, and it is the newest — the other two are owed, not shown.
            var showing = Assert.Single(Toasts(window));
            Assert.Equal("third", showing.GetLogicalDescendants().OfType<TextBlock>().First().Text);

            Click(window, showing.GetLogicalDescendants().OfType<Button>().Single());

            // Only that one went, and the one behind it took the screen rather than waiting for
            // another event to nudge it there.
            Assert.Equal(["first", "second"], window.Toasts.Messages);
            var next = Assert.Single(Toasts(window));
            Assert.Equal("second", next.GetLogicalDescendants().OfType<TextBlock>().First().Text);

            Click(window, next.GetLogicalDescendants().OfType<Button>().Single());
            Assert.Equal(["first"], window.Toasts.Messages);
            Assert.Equal(
                "first",
                Assert.Single(Toasts(window)).GetLogicalDescendants().OfType<TextBlock>().First().Text);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// The auto-dismiss is on the INJECTED schedule, and the elapse is a signal this test raises
    /// rather than a wall clock it waits for. Upstream's is a <c>DispatcherTimer</c> started with
    /// the toast (<c>NotificationService.cs:94-104</c>), defaulting to five seconds (<c>:49</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task AToastExpiresWhenItsScheduledCallbackFires_AndNotBefore()
    {
        var (host, window) = await BootAsync();
        try
        {
            var schedule = new RecordingSchedule();
            window.Toasts.Schedule = schedule.Take;

            window.Toasts.Show("this one goes away by itself", ToastKind.Info);
            window.UpdateLayout();

            Assert.Equal([ToastHost.DefaultDuration], schedule.Requested);
            Assert.Equal(["this one goes away by itself"], window.Toasts.Messages);

            schedule.FireAll();
            window.UpdateLayout();
            Assert.Empty(window.Toasts.Messages);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// A toast the user closes first RETIRES its timer rather than leaving it to fire into an
    /// empty stack — upstream stops the stashed timer on the same line it removes the border
    /// (<c>NotificationService.cs:215</c>). A live timer here would remove whichever toast happened
    /// to be in that slot later.
    /// </summary>
    [AvaloniaFact]
    public async Task DismissingATransientToastCancelsItsPendingTimer()
    {
        var (host, window) = await BootAsync();
        try
        {
            var schedule = new RecordingSchedule();
            window.Toasts.Schedule = schedule.Take;
            window.Toasts.Show("closed by hand", ToastKind.Info, TimeSpan.FromSeconds(8));
            window.UpdateLayout();
            Assert.Equal([TimeSpan.FromSeconds(8)], schedule.Requested);

            Click(window, Toasts(window).Single().GetLogicalDescendants().OfType<Button>().Single());

            Assert.Empty(window.Toasts.Messages);
            Assert.Equal(1, schedule.Cancelled);

            // And the retired callback cannot reach back into the stack: a later toast survives
            // the fire the dismissed one would otherwise have delivered.
            window.Toasts.ShowUntilDismissed("still here", ToastKind.Success);
            schedule.FireAll();
            window.UpdateLayout();
            Assert.Equal(["still here"], window.Toasts.Messages);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// <see cref="ToastHost.ShowUntilDismissed"/> asks the clock for NOTHING. It is the shape
    /// upstream's phrase-backup results have — a modal with an OK button
    /// (<c>MainWindow/MainWindow.PresetIO.cs:81-83</c>, <c>:125-127</c>) — and an outcome that
    /// expired while it was being read would be a worse answer than the modal it replaces.
    /// </summary>
    [AvaloniaFact]
    public async Task AToastShownUntilDismissedSchedulesNothingAtAll()
    {
        var (host, window) = await BootAsync();
        try
        {
            var schedule = new RecordingSchedule();
            window.Toasts.Schedule = schedule.Take;
            window.Toasts.ShowUntilDismissed("read me at your leisure", ToastKind.Success);
            window.UpdateLayout();

            Assert.Empty(schedule.Requested);
            Assert.Equal(["read me at your leisure"], window.Toasts.Messages);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// <b>The empty space around a toast is click-through</b> — upstream's own rule, stated in its
    /// own words: "The host panel has no Background, so empty space is click-through — only the
    /// toast bodies themselves capture clicks" (<c>NotificationService.cs:16-18</c>,
    /// <c>MainWindow/MainWindow.xaml:3210-3213</c>).
    ///
    /// <para>It matters more here than upstream, because this host floats over the whole page
    /// area: a 380-DIP invisible rectangle that ate every press would disable a 380x163 block of
    /// every page in the app for as long as a message was up, and nothing would look wrong. Where
    /// that block sits is <c>Views/MainWindow.axaml</c>'s decision - bottom-right, measured against
    /// the pages' own controls - and this fact is about the empty space wherever it is put.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TheEmptySpaceInTheToastHostPassesClicksToThePageUnderneath()
    {
        var (host, window) = await BootAsync();
        try
        {
            window.Toasts.ShowUntilDismissed("something happened", ToastKind.Success);
            window.UpdateLayout();

            var toast = Toasts(window).Single();
            var hostBounds = window.Toasts.Bounds;
            var toastBounds = toast.Bounds;
            // The host is 380 DIP and a toast is at most 360 (NotificationService.cs:136), right
            // aligned — so there is real empty host to the LEFT of every toast.
            Assert.True(toastBounds.Width < hostBounds.Width,
                $"the toast fills its host ({toastBounds.Width} of {hostBounds.Width} DIP), so this "
                + "fact would be testing a point that is not empty");

            // Driven with REAL headless pointer input rather than a hit-test query, because the
            // routing is the claim: Avalonia sets IsPointerOver on the whole chain it delivers to,
            // so the host reads false only when the press genuinely went past it.
            var empty = window.Toasts.TranslatePoint(new Point(2, hostBounds.Height / 2), window)!.Value;
            window.MouseMove(empty, RawInputModifiers.None);
            window.UpdateLayout();
            Assert.False(window.Toasts.IsPointerOver,
                "the pointer over the toast host's EMPTY area was delivered to the host instead of "
                + "passing through to the page — the host or its stack has acquired a Background");

            // The toast body itself DOES capture its own pointer, which is the other half of the
            // same rule: click-through must not mean the dismiss button is unreachable.
            var onToast = toast.TranslatePoint(
                new Point(toastBounds.Width / 2, toastBounds.Height / 2), window)!.Value;
            window.MouseMove(onToast, RawInputModifiers.None);
            window.UpdateLayout();
            Assert.True(toast.IsPointerOver, "the toast body did not receive its own pointer");
            Assert.True(window.Toasts.IsPointerOver);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// <b>The same sentence twice is one toast, and it is the placement's other half.</b> This host
    /// is docked over the page rather than over empty chrome (<c>Views/MainWindow.axaml</c>), so an
    /// unbounded stack walks up into the page's own controls: three copies of the tier refusal grew
    /// the surface to 489 DIP and covered the FALL IN button that raised them, which is the exact
    /// defect the dock was moved to prevent. Upstream states the rule for the toasts that outlive
    /// their call — already showing is a no-op (<c>NotificationService.cs:110</c>) — and this port
    /// needs it for the timed ones too.
    ///
    /// <para>The TIMER is refreshed rather than left alone, and that half matters on its own: a
    /// refusal that expired one second after the user asked again would announce less than upstream
    /// does. The old handle is cancelled, so the toast cannot be removed by the press before last.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task RepeatingAMessageThatIsAlreadyUpRefreshesItsTimerInsteadOfStackingACopy()
    {
        var (host, window) = await BootAsync();
        try
        {
            var schedule = new RecordingSchedule();
            window.Toasts.Schedule = schedule.Take;

            window.Toasts.Show("the door did not open", ToastKind.Warning, TimeSpan.FromSeconds(8));
            window.Toasts.Show("the door did not open", ToastKind.Warning, TimeSpan.FromSeconds(8));
            window.Toasts.Show("the door did not open", ToastKind.Warning, TimeSpan.FromSeconds(8));
            window.UpdateLayout();

            // One toast, three full durations asked for, and the two superseded timers retired
            // rather than left to remove a toast the newest press has just re-armed.
            Assert.Equal(["the door did not open"], window.Toasts.Messages);
            Assert.Equal(
                [TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8)],
                schedule.Requested);
            Assert.Equal(2, schedule.Cancelled);

            // A toast the user must acknowledge does not duplicate either, and asks for no timer:
            // there is nothing to refresh and a second copy of the same sentence says nothing new.
            window.Toasts.ShowUntilDismissed("saved 36 phrases", ToastKind.Success);
            window.Toasts.ShowUntilDismissed("saved 36 phrases", ToastKind.Success);
            window.UpdateLayout();
            Assert.Equal(["the door did not open", "saved 36 phrases"], window.Toasts.Messages);
            Assert.Equal(3, schedule.Requested.Count);

            // And a DIFFERENT sentence is still a SEPARATE notice: this is de-duplication, not a
            // one-message host. Three are owed; the newest holds the screen and the other two wait
            // (ToastHost), which is why nothing here reads three plates.
            window.Toasts.ShowUntilDismissed("and 4 pools were skipped", ToastKind.Info);
            window.UpdateLayout();
            Assert.Equal(
                ["the door did not open", "saved 36 phrases", "and 4 pools were skipped"],
                window.Toasts.Messages);
            Assert.Equal(
                "and 4 pools were skipped",
                Assert.Single(Toasts(window)).GetLogicalDescendants().OfType<TextBlock>().First().Text);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>The timer seam, recorded. The elapse is raised by the test, so nothing here waits
    /// on a clock.</summary>
    private sealed class RecordingSchedule
    {
        private readonly List<Pending> _pending = [];

        public List<TimeSpan> Requested { get; } = [];

        public int Cancelled => _pending.Count(p => p.Disposed);

        public IDisposable Take(TimeSpan due, Action fire)
        {
            Requested.Add(due);
            var pending = new Pending(fire);
            _pending.Add(pending);
            return pending;
        }

        public void FireAll()
        {
            foreach (var pending in _pending.ToArray())
            {
                pending.Fire();
            }
        }

        private sealed class Pending(Action fire) : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;

            public void Fire()
            {
                if (!Disposed)
                {
                    fire();
                }
            }
        }
    }
}
