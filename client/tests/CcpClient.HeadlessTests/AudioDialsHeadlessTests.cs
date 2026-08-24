using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The AUDIO rack row — master volume, video volume, the output-endpoint picker and Test audio —
/// driven from a cold composition-root boot with no command-line arguments and no substituted
/// seams.
///
/// <para><b>What these facts are for.</b> A volume slider is the easiest control in any application
/// to ship dead: it moves, it persists, it looks exactly like a working one, and nothing about the
/// picture tells a user whether it reaches anything. The seams behind this row were landed and
/// UNREACHABLE for four waves — the arbitration and its preferred-device entry lived inside the DTRH
/// host window — so every fact here is about the WIRING that makes a rack row reach the app-wide
/// audio owner, and about the three refusals that wiring is obliged to keep.</para>
///
/// <para><b>NOTHING HERE OPENS A DEVICE, and two of the facts are exactly that claim.</b> Opening
/// the panel enumerates endpoints and starts nothing; pressing Test with no clip refuses before
/// asking for one. Both are checked against <c>AudioParticipant.DeviceInitAttempts</c>, which is
/// the participant's own evidence counter rather than a number this suite keeps.</para>
///
/// <para><b>What they do NOT prove, said plainly because audio is a real device.</b> No fact in
/// this file shows that any endpoint was opened, that a sample reached a mixer, or that a human
/// heard anything. The clip path is never exercised here at all — a fresh data directory has no
/// clips, which is the state every real first run is in. Sound reaching the Windows audio engine is
/// <c>AudioCapabilityTests</c>' <c>render-metered</c> class, and a person hearing it is
/// <c>audible-verified</c>, which no automated step on any platform discharges
/// (<c>client/docs/verification-harness.md</c>, audio evidence class).</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): logical tree, control state and
/// real input routing. No composited pixel is claimed — the headed <c>audio-dial</c> captures are
/// what claim those.</para>
/// </summary>
public class AudioDialsHeadlessTests : HeadlessTest
{
    private sealed record Boot(ApplicationHost Host, MainWindow Window)
    {
        public CcpClient.Desktop.Audio.AudioParticipant Audio => Window.Audio;

        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);
    }

    private async Task<Boot> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-audio-dials-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
        };
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
        return new Boot(host, window);
    }

    /// <summary>The LOGICAL tree, for the session-lock sweep's reason: sixteen of the rack's
    /// seventeen panels are hidden at any moment, and a fact that could only see what is on screen
    /// would pass with the row never opened.</summary>
    private static T Dial<T>(StudioPage page, string name) where T : Control =>
        page.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' on the Studio page");

    private static void Click(Window window, Control control, MouseButton button = MouseButton.Left)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, button, RawInputModifiers.None);
        window.MouseUp(center, button, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static void OpenTheAudioRow(MainWindow window)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = window.GetLogicalDescendants().OfType<RadioButton>().First(r => r.Name == "RowAudio");
        Click(window, row);
        Assert.True(row.IsChecked);
    }

    // =====================================================================================
    //  the row, and the dials it opens on
    // =====================================================================================

    /// <summary>
    /// The row opens a panel carrying upstream's four controls, on upstream's own fresh-install
    /// values: master 32 and video 50 (<c>Models/AppSettings.cs:1127</c>, <c>:1134</c>) — LITERALS
    /// here rather than the product's constants, because a fact that read them back through
    /// <c>AudioSettingsDocument</c> would move with any edit to it and pin nothing.
    /// </summary>
    [AvaloniaFact]
    public async Task TheAudioRowOpensUpstreamsFourControls_AtUpstreamsOwnDefaults()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheAudioRow(window);

        Assert.True(Dial<StackPanel>(boot.Studio, "AudioModulePanel").IsVisible);
        Assert.Equal(32, (int)Math.Round(Dial<Slider>(boot.Studio, "AudioMasterSlider").Value));
        Assert.Equal(50, (int)Math.Round(Dial<Slider>(boot.Studio, "AudioVideoSlider").Value));
        Assert.Equal("32%", Dial<TextBlock>(boot.Studio, "AudioMasterValue").Text);
        Assert.Equal("50%", Dial<TextBlock>(boot.Studio, "AudioVideoValue").Text);

        // Both sliders are upstream's own range and the clamp behind them is upstream's
        // Math.Clamp(value, 0, 100) (:1131, :1138). A slider that could ask for 150 would be asking
        // the document for a value it silently rewrites, and the label would then disagree with it.
        foreach (var name in new[] { "AudioMasterSlider", "AudioVideoSlider" })
        {
            var slider = Dial<Slider>(boot.Studio, name);
            Assert.Equal(0, slider.Minimum);
            Assert.Equal(100, slider.Maximum);
        }

        // Entry 0 is the system default and is what a fresh install is on — upstream's own
        // encoding, where the empty string means the default endpoint (:1238-1240) and index 0
        // carries it (MainWindow/MainWindow.UiUpdates.cs:1124).
        var picker = Dial<ComboBox>(boot.Studio, "AudioDevicePicker");
        Assert.Equal("System default", picker.Items[0]);
        Assert.Equal(0, picker.SelectedIndex);
        Assert.Null(boot.Audio.OutputDeviceName);

        // And the fourth control, which is the only one whose point is that a human hears
        // something.
        Assert.Equal("Test audio", Dial<Button>(boot.Studio, "AudioTestButton").Content);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// <b>Opening the panel asks the machine what endpoints it has and OPENS NOTHING.</b> This is
    /// the seam's central design rule reaching the surface that could most easily break it: a picker
    /// that brought a device up to populate itself would seize a render endpoint for a user who
    /// plays nothing, which is exactly what <c>AudioParticipant</c>'s device-free phase 3 exists to
    /// prevent.
    ///
    /// <para>Checked against the participant's own counters rather than against anything this suite
    /// keeps: <c>DeviceInitAttempts</c> stays 0 and <c>DeviceOutcome</c> stays NULL, which is "not
    /// asked" and is deliberately not the same value as "unavailable".</para>
    /// </summary>
    [AvaloniaFact]
    public async Task OpeningThePanelListsEndpointsAndOpensNoDevice()
    {
        var boot = await BootAsync();

        Assert.Equal(0, boot.Audio.DeviceInitAttempts);
        Assert.Null(boot.Audio.DeviceOutcome);

        OpenTheAudioRow(boot.Window);

        // The list was really read — the picker always carries the System default entry, and
        // anything beyond it is whatever this machine reports. The COUNT is a property of the
        // machine and is deliberately not asserted; that nothing was OPENED is not.
        Assert.NotEmpty(Dial<ComboBox>(boot.Studio, "AudioDevicePicker").Items);
        Assert.Equal(0, boot.Audio.DeviceInitAttempts);
        Assert.Null(boot.Audio.DeviceOutcome);

        // And the panel says so in words, because a silent "nothing yet" is indistinguishable from
        // a panel that failed to render.
        Assert.Contains(
            "Nothing has been asked of the operating system yet",
            Dial<TextBlock>(boot.Studio, "AudioDeviceState").Text!,
            StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  the dials reach the app-wide document
    // =====================================================================================

    /// <summary>
    /// Moving either slider writes the APP-WIDE audio document — the one
    /// <c>AudioParticipant</c> owns and the one the companion's voice path reads
    /// (<c>Features/Dtrh/DtrhHostWindow.axaml.cs:268</c>) — rather than a copy the page keeps. The
    /// value label follows in the same gesture, which is upstream's own handler shape
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:1011</c>, <c>:1026</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task MovingTheDialsWritesTheAppWideDocument_AndTheLabelsFollow()
    {
        var boot = await BootAsync();
        OpenTheAudioRow(boot.Window);

        Dial<Slider>(boot.Studio, "AudioMasterSlider").Value = 71;
        boot.Window.UpdateLayout();
        Assert.Equal(71, boot.Audio.MasterVolume);
        Assert.Equal("71%", Dial<TextBlock>(boot.Studio, "AudioMasterValue").Text);

        Dial<Slider>(boot.Studio, "AudioVideoSlider").Value = 12;
        boot.Window.UpdateLayout();
        Assert.Equal(12, boot.Audio.VideoVolume);
        Assert.Equal("12%", Dial<TextBlock>(boot.Studio, "AudioVideoValue").Text);

        // ZERO IS A REAL SETTING and survives the round trip, because upstream treats it as one:
        // MasterVolume == 0 means "ALL audio will be silent" in its own diagnostic
        // (Services/AudioService.cs:535-536) and text-only barks in this port
        // (Companion/BarkPipeline.cs:365). A dial that snapped 0 back to a default would be
        // un-muting a user who muted the app.
        Dial<Slider>(boot.Studio, "AudioMasterSlider").Value = 0;
        boot.Window.UpdateLayout();
        Assert.Equal(0, boot.Audio.MasterVolume);
        Assert.Contains(
            "That is a real setting",
            Dial<TextBlock>(boot.Studio, "AudioMasterState").Text!,
            StringComparison.Ordinal);

        // Neither dial touched the device. Volumes are readings; applying them is the play site's
        // job (AudioParticipant's own note, WPF Services/AudioService.cs:643).
        Assert.Equal(0, boot.Audio.DeviceInitAttempts);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// <b>The video dial says out loud that nothing reads it yet.</b> The setting is real and
    /// app-wide (<c>Models/AppSettings.cs:1134</c>) and it is persisted with the other two, but this
    /// build plays no video soundtrack at all — its own document says so
    /// (<c>Session/MandatoryVideoPresetDocument.cs:17-19</c>). A dial that implied it changed
    /// playback would be the dead dial §9 D7 refuses; a dial that says which it is, is not.
    /// </summary>
    [AvaloniaFact]
    public async Task TheVideoDialSaysItStoresAPreferenceRatherThanChangingPlayback()
    {
        var boot = await BootAsync();
        OpenTheAudioRow(boot.Window);

        var video = Dial<TextBlock>(boot.Studio, "AudioVideoState").Text!;
        Assert.Contains("nothing in this build plays a video soundtrack yet", video, StringComparison.Ordinal);
        Assert.Contains("what the app remembers rather than what you hear", video, StringComparison.Ordinal);

        // And the master dial's line is the opposite claim, made only because there IS a reader and
        // it names WHEN it reads — a slider whose effect arrives at the next window is worth saying
        // out loud rather than leaving a user to conclude it is broken.
        var master = Dial<TextBlock>(boot.Studio, "AudioMasterState").Text!;
        Assert.Contains("the companion's spoken lines", master, StringComparison.Ordinal);
        Assert.Contains("reaches the next one", master, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  the endpoint that is no longer there
    // =====================================================================================

    /// <summary>
    /// <b>A remembered endpoint that is gone degrades honestly instead of throwing or lying.</b>
    /// The port persists a device NAME and never an id, because a miniaudio id is a
    /// process-lifetime native pointer and passing a stored one back to the driver is this build's
    /// process-fatal crash class (<c>Audio/SoundFlowAudioBackend.cs</c> header) — so a name that is
    /// no longer in the machine's list is an ordinary, survivable state rather than an exception.
    ///
    /// <para>What the surface does with it is upstream's behaviour plus one sentence upstream does
    /// not have: the picker falls back to entry 0 (<c>MainWindow.UiUpdates.cs:1117-1124</c>), the
    /// stored choice is NOT rewritten, and the notice says both — so the choice comes back when the
    /// device does.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task ARememberedEndpointThatIsGone_FallsBackAndSaysSo_WithoutDiscardingTheChoice()
    {
        var boot = await BootAsync();

        // A name no machine has. Written into the document the way a previous run would have left
        // it, before the panel has ever been opened.
        const string Gone = "Headphones That Are Not Plugged In (Test)";
        boot.Audio.Settings.Mutate(document => document.OutputDeviceName = Gone);

        OpenTheAudioRow(boot.Window);

        var picker = Dial<ComboBox>(boot.Studio, "AudioDevicePicker");
        Assert.Equal(0, picker.SelectedIndex);
        Assert.DoesNotContain(Gone, picker.Items.Cast<object?>().Select(i => i?.ToString()));

        // THE CHOICE IS STILL STORED. This is the half upstream drops silently, and it is what
        // makes the fallback survivable rather than destructive.
        Assert.Equal(Gone, boot.Audio.OutputDeviceName);

        var choice = Dial<TextBlock>(boot.Studio, "AudioChoiceState").Text!;
        Assert.Contains(Gone, choice, StringComparison.Ordinal);
        Assert.Contains("not in the list of endpoints", choice, StringComparison.Ordinal);
        Assert.Contains("falls back to the system default", choice, StringComparison.Ordinal);

        // Reading the list did not open anything, here either.
        Assert.Equal(0, boot.Audio.DeviceInitAttempts);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  Test audio, and the refusal that is the whole point of it
    // =====================================================================================

    /// <summary>
    /// <b>Test audio with no clip refuses BY NAME, names the folders, and opens no device.</b>
    ///
    /// <para>A fresh data directory has no clips, which is the state every real first run is in:
    /// this build ships no sound resources of its own (the payload rule — upstream's three test
    /// candidates are application resources at <c>Services/AudioService.cs:590-594</c>), so the
    /// pools the two audio modules draw from are empty. Upstream's own diagnostic ends with
    /// <i>"WARNING: No test sound files found to play"</i> in exactly this case (<c>:604</c>); the
    /// port names the two folders a user can act on instead of a resource directory they do not
    /// have.</para>
    ///
    /// <para><b>And the refusal costs nothing.</b> The clip is looked for before the device is asked
    /// for, so a test that cannot play anything does not seize a render endpoint to say so —
    /// <c>DeviceInitAttempts</c> is still 0 afterwards. That inverts upstream's order deliberately,
    /// because this panel already carries the device's own last typed outcome permanently.</para>
    ///
    /// <para>The gesture is awaited rather than waited for: the button hands off to an awaitable
    /// and this drives that same one path (no sleep, no poll, no clock).</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TestAudioWithNoClip_RefusesByNameAndBringsUpNoDevice()
    {
        var boot = await BootAsync();
        OpenTheAudioRow(boot.Window);

        // Before: the panel describes the gesture rather than claiming anything about it.
        Assert.Contains(
            "Not tested yet",
            Dial<TextBlock>(boot.Studio, "AudioTestState").Text!,
            StringComparison.Ordinal);

        await boot.Studio.TestAudioAsync();
        boot.Window.UpdateLayout();

        var report = Dial<TextBlock>(boot.Studio, "AudioTestState").Text!;
        Assert.Contains("Nothing to play, so nothing was opened", report, StringComparison.Ordinal);
        Assert.Contains(boot.Window.Session.MindWipe.ClipFolder, report, StringComparison.Ordinal);
        Assert.Contains(boot.Window.Session.BrainDrain.ClipFolder, report, StringComparison.Ordinal);
        Assert.Contains(".mp3", report, StringComparison.Ordinal);

        // THE REFUSAL TOOK NOTHING. Checked on the participant's own counter, which is the whole
        // reason that counter exists.
        Assert.Equal(0, boot.Audio.DeviceInitAttempts);
        Assert.Null(boot.Audio.DeviceOutcome);

        await boot.Host.ShutdownAsync();
    }
}
