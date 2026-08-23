using Avalonia.Controls;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Motion;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// Integration proof (contract §10.1): the phase-outcome trace, the demonstrator participants'
/// running state and the typed capability states are visibly reachable from a user path — now
/// through the System door rather than off the retired dashboard card.
///
/// <para>It also carries the ONE app-scoped preference this port owns: the motion level
/// (<see cref="MotionLevel"/>). Upstream keeps it in App Settings → Performance
/// (<c>Views/Controls/AppSettings/PerformanceSettingsSection.xaml:173-179</c>, written back at
/// <c>MainWindow/MainWindow.Settings.cs:325-326</c>); this port has no App Settings door and the
/// System door is where that class of surface landed.</para>
/// </summary>
public partial class SystemPage : UserControl
{
    private readonly PersistenceStore<MotionSettingsDocument>? _motion;

    public SystemPage(ApplicationHost host)
    {
        InitializeComponent();

        _motion = HostedMotion.StoreOf(host);
        // Upstream's own bind: SelectedIndex IS the enum ordinal, clamped to the item count
        // (PerformanceSettingsSection.xaml.cs:94-95).
        MotionLevelPicker.SelectedIndex = (int)(_motion?.Current.Level ?? MotionLevel.Full);
        MotionLevelPicker.SelectionChanged += (_, _) => OnMotionLevelPicked();
        DescribeMotion();

        var lines = host.Trace.Entries.Concat(
            host.Participants.Select(p => $"{p.Name}: {(p.Running ? "running" : "stopped")}"));
        if (host.Capabilities is { } capabilities)
        {
            // Capability contract §9.1: the shell surfaces each capability's typed state —
            // the composition root's probe results are visibly reachable from a user path.
            lines = lines.Concat(capabilities.Names.Select(n => $"capability {n}: {Describe(capabilities.GetState(n))}"));
        }

        TraceText.Text = string.Join(Environment.NewLine, lines);

        // Async contract §5.4: the heartbeat's tick text reaches the page through the real
        // dispatch boundary; the reporter is invoked only inside a boundary post.
        foreach (var heartbeat in host.Participants.OfType<HeartbeatParticipant>())
        {
            heartbeat.TickReporter = text => HeartbeatText.Text = text;
        }
    }

    /// <summary>
    /// The user's choice, written back the way upstream writes it: the picker's index mapped to
    /// the enum, with anything unexpected landing on Full
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:2471-2477</c>). Persisted immediately — the enqueued
    /// write is serialized by the store and the teardown flush is the backstop.
    /// </summary>
    private void OnMotionLevelPicked()
    {
        var level = MotionLevelPicker.SelectedIndex switch
        {
            1 => MotionLevel.Reduced,
            2 => MotionLevel.Off,
            _ => MotionLevel.Full,
        };

        if (_motion is not null && level != _motion.Current.Level)
        {
            _motion.Mutate(document => document.Level = level);
            _ = _motion.Save();
        }

        DescribeMotion();
    }

    /// <summary>
    /// What the choice actually does, said on the page.
    ///
    /// <para>"The next page you open" is not hedging: the switch is a Chromium command-line
    /// argument fixed when a hosted surface's WebView2 environment is created
    /// (<c>Chaos/ChaosWebViewHost.cs:832-836</c>), so a change cannot reach a window that is
    /// already up.</para>
    /// </summary>
    private void DescribeMotion()
    {
        if (_motion is null)
        {
            MotionLevelState.Text =
                "This build has no motion settings store, so the choice cannot be saved and hosted "
                + "pages run at Full.";
            return;
        }

        var level = _motion.Current.Level;
        var line = level == MotionLevel.Off
            ? "Pages this app hosts are told to stop moving (" + HostedMotion.ReducedArgument + ")."
            : "Pages this app hosts are told to keep moving (" + HostedMotion.NoReducedArgument + ").";
        line += " A change reaches the NEXT hosted page you open, not one already on screen.";

        // The OS disagreement, said where the user can act on it — the same condition the hosted
        // surfaces log (Chaos/ChaosWebViewHost.cs:782-800).
        if (HostedMotion.OverridesOsPreference(level, DtrhMotionPreference.ReadOsClientAreaAnimation()))
        {
            line += " Windows animation effects are off on this machine; this setting overrides "
                + "that for hosted pages, which is what stops a session playing as a set of stills.";
        }

        MotionLevelState.Text = line;
    }

    private static string Describe(CapabilityState state) => state switch
    {
        CapabilityState.Available available => $"Available — {available.Detail}",
        CapabilityState.Unavailable unavailable => $"Unavailable ({unavailable.Reason.Code}) — {unavailable.Reason.Detail}",
        CapabilityState.Degraded degraded => $"Degraded ({degraded.Reason.Code}) — survives: {degraded.SurvivingSemantics}; {degraded.Reason.Detail}",
        CapabilityState.PermissionRequired permission => $"PermissionRequired ({permission.Reason.Code}) — {permission.Reason.Detail}",
        CapabilityState.DependencyMissing missing => $"DependencyMissing ({missing.Dependency}) — {missing.Reason.Detail}",
        CapabilityState.Faulted faulted => $"Faulted ({faulted.Reason.Code}) — {faulted.Reason.Detail}",
        _ => state.GetType().Name,
    };
}
