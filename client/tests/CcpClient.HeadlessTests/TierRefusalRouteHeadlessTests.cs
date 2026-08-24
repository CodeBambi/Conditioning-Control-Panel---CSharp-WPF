using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// <b>Why the Play page's tier refusal is still not routed to the in-app toast</b> — census #41,
/// answered with a measurement instead of an opinion.
///
/// <para>Upstream's refusal is an 8-second Warning toast whose action opens App Info &amp; Data
/// (<c>Services/TierGate.cs:128-139</c>). Two things were needed to port that and this port has
/// neither: the ACTION needs an App Info page, which does not exist, and the ANNOUNCEMENT needs
/// somewhere to sit that is not on top of the card it is about.</para>
///
/// <para><b>The second one is the fact below, and it was found by wiring the toast and watching a
/// landed test go red.</b> The shell's toast surface is docked top-right; the Play page's two
/// launch buttons are in the top-right of its hero card; a refusal toast lands squarely over
/// <c>FALL IN</c>, and because a toast body carries a background it captures the click — exactly as
/// upstream's do (<c>MainWindow/MainWindow.xaml:3212-3216</c>). The user is then refused AND
/// prevented from pressing again for eight seconds, which takes away the one thing this card
/// guarantees in every branch: "a gated press must ARRIVE"
/// (<c>Views/Tabs/PlayTabView.xaml:503-506</c>).</para>
///
/// <para><b>This fact is a tripwire, not a specification.</b> It reds the day the toast surface or
/// the Play card moves clear of each other — and that is the day the announcement half of #41
/// becomes free to build. The action half still needs the page.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): arranged bounds and hit-test
/// routing in the shell's own default window size. Nothing here claims composited pixels.</para>
/// </summary>
public class TierRefusalRouteHeadlessTests
{
    private static async Task<(ApplicationHost Host, MainWindow Window)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-tier-route-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return (host!, window);
    }

    private static T Descendant<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static Rect BoundsIn(MainWindow window, Visual control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        return new Rect(origin, control.Bounds.Size);
    }

    private static void Click(MainWindow window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public async Task AToastCarryingTheTierRefusalWouldCoverTheVeryButtonThatRaisedIt()
    {
        var (host, window) = await BootAsync();
        Click(window, window.FindControl<RadioButton>("DoorPlay")!);

        var fallIn = Descendant<Button>(window, "FallInButton");
        var fallInRect = BoundsIn(window, fallIn);
        var centre = fallInRect.Center;
        var pressed = 0;
        fallIn.Click += (_, _) => pressed++;

        // The message is the REAL one the gate produces, at the REAL severity and duration upstream
        // uses (Services/TierGate.cs:133) - a shorter stand-in would make the toast narrower than
        // the thing being measured.
        window.Toasts.Show(DtrhGate.TierRefusalMessage, ToastKind.Warning, TimeSpan.FromSeconds(8));
        window.UpdateLayout();

        // The toast's parts carry AutomationIds rather than names (ToastHost.MessageAutomationId), so
        // the body is reached from its message's own style class - the same way the headed harness
        // derives this rect from the message's UIA rectangle.
        var message = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Classes.Contains("toast-message"));
        var toast = message.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("toast"));
        var toastRect = BoundsIn(window, toast);

        // It is not a near miss: the refusal covers the button whole.
        Assert.True(
            toastRect.Contains(fallInRect),
            $"the toast at {toastRect} does not contain FALL IN at {fallInRect} in a {window.Bounds} window");

        // THE CLAIM, and it is the delivered press rather than a query: a real MouseDown/MouseUp at
        // the button's own centre does not reach the button while the refusal is up.
        Click(window, fallIn);
        Assert.Equal(0, pressed);

        // The confirmation, READ AFTER THE INPUT AND DELIBERATELY NOT BEFORE IT. Avalonia's
        // InputHitTest answered "FallInButton" for this same point between the toast's layout pass
        // and the first pointer event, and only agreed with the delivered press afterwards — so a
        // fact that had asked the query instead of pressing the button would have concluded the
        // button was reachable and been wrong.
        var hit = window.InputHitTest(centre) as Visual;
        Assert.NotNull(hit);
        Assert.Contains(hit!.GetSelfAndVisualAncestors().OfType<ToastHost>(), host => host == window.Toasts);

        // Taking the toast away gives the press back, which is the inversion: the occlusion belongs
        // to the toast and to nothing else on this page.
        window.Toasts.DismissAll();
        window.UpdateLayout();
        Click(window, fallIn);
        Assert.Equal(1, pressed);

        await host.ShutdownAsync();
    }
}
