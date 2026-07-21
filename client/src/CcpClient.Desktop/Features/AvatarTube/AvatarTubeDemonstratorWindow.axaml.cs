using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>
/// The explicitly-labeled DEMONSTRATOR AvatarTube surface (SP-015; SP-007/SP-013 pattern:
/// really-functioning, superseded by the first real AvatarTube feature, owner may
/// async-veto). Every behavior routes through the ONE engine — no parallel timers, no
/// second timing authority (first-attempt leak REJECTs). Transition/liveness constants are
/// demonstrator values recorded pending-owner (see <see cref="AvatarEngineOptions"/>).
///
/// Window behavior per the Step-1 archaeology: attached = owned by the dashboard, follows
/// owner move/resize, owner minimize pauses + hides, restore resumes + shows; detached =
/// ownerless topmost widget, draggable; attach/detach preserves the animation pipeline
/// (view-only switch — the engine never sees it); closing disposes subscriptions and
/// cancels the engine generation through the participant.
/// </summary>
public partial class AvatarTubeDemonstratorWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly Window _dashboard;
    private readonly AvatarTubeParticipant _participant;
    private readonly AvatarBitmapCache _bitmaps = new();
    private bool _attached = true;
    private bool _pauseToggled;
    private bool _cleanupDone;

    public AvatarTubeDemonstratorWindow(ApplicationHost host, Window dashboard, AvatarTubeParticipant participant)
    {
        _host = host;
        _dashboard = dashboard;
        _participant = participant;
        InitializeComponent();

        Opened += OnOpened;
        Closing += OnClosing;

        // Real user paths for every demonstrator behavior.
        ModeButton.Click += OnModeClick;
        TalkButton.Click += OnTalkClick;
        PauseButton.Click += OnPauseClick;
        PackButton.Click += OnPackClick;
        AttachButton.Click += OnAttachClick;
        // Click reaction: the real gesture on the avatar itself (distinct from speech).
        AvatarStage.PointerPressed += OnAvatarPressed;
        // Detached-mode drag: the demonstrator label row is the drag strip (drag/click
        // conflict avoided — WPF's dead-zone semantics are the full-widget contract, a
        // Step-1 contract-only limit).
        AvatarStage.PointerPressed += OnDragPressed;

        // Field-backed, symmetric subscriptions (first-attempt subscription-leak REJECT):
        // the same delegates removed in OnClosing.
        _participant.FramePresented += OnFramePresented;
        _dashboard.PositionChanged += OnOwnerMoved;
        _dashboard.SizeChanged += OnOwnerResized;
        _dashboard.PropertyChanged += OnOwnerPropertyChanged;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Phase-4 user path: decode-probe + engine start (idempotent; tests may have
        // already started the participant with an injected clock).
        _participant.StartTube();
        RepositionToOwner();
    }

    private void OnFramePresented(object? sender, AvatarFrameEventArgs args)
    {
        // args.Pack is the pack these frames belong to (self-sufficient across pack
        // switches — a queued post from the old pack renders what was presented).
        var pack = args.Pack;
        LayerAImage.Source = _bitmaps.Get(pack, args.LayerA.ClipId, args.LayerA.FrameIndex);
        LayerAImage.Opacity = args.LayerA.Opacity;
        if (args.LayerB is not null)
        {
            LayerBImage.Source = _bitmaps.Get(pack, args.LayerB.ClipId, args.LayerB.FrameIndex);
            LayerBImage.Opacity = args.LayerB.Opacity;
        }
        else
        {
            LayerBImage.Source = null;
            LayerBImage.Opacity = 0;
        }

        // Gentle float: the avatar CONTENT only — never the top-level window (WPF parity).
        FloatCanvas.RenderTransform = new TranslateTransform(0, args.FloatYDip);

        CapabilityText.Text = $"capability avatar-animation: {Describe(_participant.AvatarCapability)}";
        // The stage's SCREEN origin + scale: the headed harness's capture rect (physical
        // pixels, UIA-readable — the SP-007 layout-probe pattern).
        var stageOrigin = AvatarStage.PointToScreen(new Point(0, 0));
        ProbeText.Text =
            $"avatar-probe: pack={args.PackId} clip={args.LayerA.ClipId} frame={args.LayerA.FrameIndex} " +
            $"mode={args.Mode} outstanding={_host.Registry.OutstandingOperations} subs={_participant.FrameSubscriberCount} " +
            $"stage={stageOrigin.X},{stageOrigin.Y} scale={RenderScaling:0.##}";
    }

    private void OnModeClick(object? sender, RoutedEventArgs e)
    {
        var engine = _participant.Engine;
        if (engine is null)
        {
            return;
        }

        var target = engine.Mode == AvatarMode.Static ? AvatarMode.Animated : AvatarMode.Static;
        engine.SetMode(target);
        ModeButton.Content = target == AvatarMode.Animated ? "Static" : "Animate";
    }

    private void OnTalkClick(object? sender, RoutedEventArgs e) =>
        _participant.Engine?.TriggerTalk(3000); // synthetic declared-duration line (no audio pipeline yet)

    private void OnPauseClick(object? sender, RoutedEventArgs e)
    {
        var engine = _participant.Engine;
        if (engine is null)
        {
            return;
        }

        _pauseToggled = !_pauseToggled;
        if (_pauseToggled)
        {
            engine.Pause();
            PauseButton.Content = "Resume";
        }
        else
        {
            engine.Resume();
            PauseButton.Content = "Pause";
        }
    }

    private void OnPackClick(object? sender, RoutedEventArgs e)
    {
        var other = _participant.ActivePackId == SyntheticAvatarPacks.Circuit.PackId
            ? SyntheticAvatarPacks.Pulse.PackId
            : SyntheticAvatarPacks.Circuit.PackId;
        _participant.SwitchPack(other);
        // Pack-switch disposes the old pack's bitmaps (disposal parity); the cache rebuilds lazily.
        // Button label stays constant — it always switches to the other pack.
    }

    private void OnAttachClick(object? sender, RoutedEventArgs e)
    {
        // View-only switch: the engine/pipeline is UNTOUCHED (WPF attach/detach state
        // preservation parity). Hide -> restyle -> Show; the current frame keeps ticking.
        // Show(owner)/Show() establish the owner relationship — the Owner PROPERTY is not
        // relied on across a re-show (Avalonia 12.1.0 resets it on the unowned path).
        _attached = !_attached;
        Hide();
        if (_attached)
        {
            Topmost = false;
            AttachButton.Content = "Detach";
            Show(_dashboard);
            RepositionToOwner();
        }
        else
        {
            Topmost = true;
            AttachButton.Content = "Attach";
            Show();
        }
    }

    private void OnAvatarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(AvatarStage).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            _participant.Engine?.TriggerClick();
            e.Handled = true;
        }
    }

    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        // Detached = independently movable topmost widget. Never in attached mode (the
        // tube follows the owner there).
        if (!_attached && e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    // ---- owner transitions (attached mode, Step-1 archaeology) ----

    private void OnOwnerMoved(object? sender, EventArgs e)
    {
        if (_attached)
        {
            RepositionToOwner();
        }
    }

    private void OnOwnerResized(object? sender, EventArgs e)
    {
        if (_attached)
        {
            RepositionToOwner();
        }
    }

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_attached || e.Property != WindowStateProperty)
        {
            return;
        }

        // Owner minimize pauses + hides; restore resumes + shows (WPF Windowing.cs:643-716
        // parity — resume restarts exactly ONE pipeline by construction).
        var engine = _participant.Engine;
        if (engine is null)
        {
            return;
        }

        if (_dashboard.WindowState == WindowState.Minimized)
        {
            engine.Pause();
            Hide();
        }
        else
        {
            engine.Resume();
            _pauseToggled = false;
            PauseButton.Content = "Pause";
            Show();
            RepositionToOwner();
        }
    }

    private void RepositionToOwner()
    {
        // Left of the owner, vertically near its top (scaled-down WPF BaseOffsetFromParent
        // parity — demonstrator constants pending-owner).
        var x = Math.Max(0, _dashboard.Position.X - (int)Width - 8);
        var y = Math.Max(0, _dashboard.Position.Y + 20);
        Position = new PixelPoint(x, y);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_cleanupDone)
        {
            return;
        }

        _cleanupDone = true;
        // Cleanup parity (WPF OnClosed): subscriptions removed symmetrically, engine
        // generation cancelled through the participant — no orphaned timers or handlers.
        _participant.FramePresented -= OnFramePresented;
        _dashboard.PositionChanged -= OnOwnerMoved;
        _dashboard.SizeChanged -= OnOwnerResized;
        _dashboard.PropertyChanged -= OnOwnerPropertyChanged;
        _participant.StopTube();
        _bitmaps.Dispose();
    }

    private static string Describe(CapabilityState state) => state switch
    {
        CapabilityState.Available available => $"Available — {available.Detail}",
        CapabilityState.Unavailable unavailable => $"Unavailable ({unavailable.Reason.Code})",
        CapabilityState.Degraded degraded => $"Degraded ({degraded.Reason.Code}) — survives: {degraded.SurvivingSemantics}",
        CapabilityState.PermissionRequired permission => $"PermissionRequired ({permission.Reason.Code})",
        CapabilityState.DependencyMissing missing => $"DependencyMissing ({missing.Dependency})",
        CapabilityState.Faulted faulted => $"Faulted ({faulted.Reason.Code})",
        _ => state.GetType().Name,
    };
}
