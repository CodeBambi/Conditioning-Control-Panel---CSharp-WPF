using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// How one flash is DRAWN: the three numbers WPF reads out of <c>App.Settings.Current</c> at the
/// moment a flash comes due, taken together as one immutable reading.
///
/// <para><b>Why one reading and not three reads.</b> WPF takes them once per FLASH, not once per
/// window: <c>ShowImages</c> reads <c>FlashDuration</c> once at the top
/// (<c>Services/Flash/FlashService.cs:1028-1034</c>) and hands the resulting <c>lifetimeMs</c> down
/// to every window of that flash (<c>:1073</c>), and <c>LoadImagesUntilAsync</c> reads
/// <c>ImageScale</c> once (<c>:655-656</c>) and applies it to every image it decodes. This port
/// staggers a flash's surfaces by 300 ms (<c>:1112</c>), so reading per surface instead would let a
/// dial moved mid-flash — by the user, or by the Intensity Ramp on its 2-second cadence — give one
/// flash's pictures two different sizes. Capturing the reading at <see cref="FlashSurfacePresenter.Show"/>
/// keeps the outcome upstream's.</para>
///
/// <para><b>It VALIDATES rather than clamps, and that is deliberate.</b>
/// <see cref="VisualsPresetDocument"/> already clamps on every write, exactly as WPF's setters do,
/// so the product path can never construct an illegal one. A second clamp here would silently
/// absorb a document that had stopped clamping — the defect it would hide is precisely the one the
/// document's own facts exist to catch. This is the same boundary rule
/// <see cref="Overlay.OverlaySurfaceRequest"/> states for opacity: not a capability STATE, because
/// it describes no platform; a caller bug, so an exception at the boundary.</para>
/// </summary>
public readonly record struct FlashDraw
{
    /// <param name="scalePercent">WPF's <c>ImageScale</c>, 50..250.</param>
    /// <param name="opacityPercent">WPF's <c>FlashOpacity</c>, 10..100.</param>
    /// <param name="durationSeconds">WPF's <c>FlashDuration</c>, 1..30 seconds.</param>
    public FlashDraw(int scalePercent, int opacityPercent, int durationSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            scalePercent, VisualsPresetDocument.MinImageScalePercent, nameof(scalePercent));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            scalePercent, VisualsPresetDocument.MaxImageScalePercent, nameof(scalePercent));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            opacityPercent, VisualsPresetDocument.MinFlashOpacityPercent, nameof(opacityPercent));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            opacityPercent, VisualsPresetDocument.MaxFlashOpacityPercent, nameof(opacityPercent));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            durationSeconds, VisualsPresetDocument.MinFlashDurationSeconds, nameof(durationSeconds));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            durationSeconds, VisualsPresetDocument.MaxFlashDurationSeconds, nameof(durationSeconds));

        ScalePercent = scalePercent;
        OpacityPercent = opacityPercent;
        DurationSeconds = durationSeconds;
    }

    /// <summary>WPF's shipped defaults (<c>AppSettings.cs:839</c>, <c>:853</c>, <c>:925</c>) — what
    /// a build with no Visuals document draws with, and what every flash drew between SP-100 and
    /// SP-117.</summary>
    public static FlashDraw Defaults { get; } = new(
        VisualsPresetDocument.DefaultImageScalePercent,
        VisualsPresetDocument.DefaultFlashOpacityPercent,
        VisualsPresetDocument.DefaultFlashDurationSeconds);

    /// <summary>The scale handed to <see cref="FlashGeometry.Size"/>.</summary>
    public int ScalePercent { get; }

    /// <summary>The opacity, in percent, as the user's dial holds it.</summary>
    public int OpacityPercent { get; }

    /// <summary>How long a flash stays up, in seconds, before the grace.</summary>
    public int DurationSeconds { get; }

    /// <summary>The opacity as the overlay request wants it — WPF's own
    /// <c>settings.FlashOpacity / 100.0</c> (<c>FlashService.cs:2072</c>). Never zero: the
    /// document's floor is 10 %.</summary>
    public double Opacity => OpacityPercent / 100.0;

    /// <summary>
    /// How long one surface stays on screen: WPF's <c>lifetimeMs = (int)(duration * 1000) + 1000</c>
    /// (<c>FlashService.cs:1073</c>). The grace is
    /// <see cref="FlashSurfacePresenter.LifetimeGraceMilliseconds"/> and is not a dial.
    /// </summary>
    public TimeSpan Lifetime =>
        TimeSpan.FromSeconds(DurationSeconds)
        + TimeSpan.FromMilliseconds(FlashSurfacePresenter.LifetimeGraceMilliseconds);

    /// <summary>The reading a document currently holds.</summary>
    public static FlashDraw From(VisualsPresetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new FlashDraw(
            document.ImageScalePercent, document.FlashOpacityPercent, document.FlashDurationSeconds);
    }
}

/// <summary>
/// <b>Visuals</b> — WPF's sixth EFFECTS rack row (<c>Views/Tabs/StudioTabView.xaml.cs:496</c>), and
/// the only one in the whole rack that is <b>not a module</b>.
///
/// <para><b>It is not an <see cref="Session.ISessionEffect"/>, and refusing to make it one is the
/// point.</b> It has no enable, no schedule, no firing, no surface of its own and no lifecycle: the
/// shipping page is five load/save pairs over <c>App.Settings.Current</c> and nothing else
/// (<c>Features/VisualsFeatureControl.xaml.cs:35-118</c>), and there is no <c>VisualsService</c> in
/// the shipping tree at all. Dressing it as a session effect would have given the port a module
/// with an <c>Enabled</c> flag upstream does not have, a dot upstream deliberately omits
/// (<c>:494-496</c>: "A dot that cannot be wired honestly is omitted"), and an arm result about
/// nothing. What it is instead is a small owner of three dials belonging to
/// <see cref="FlashImagesEffect"/>, which is exactly what upstream's page is.</para>
///
/// <para><b>Direction, and it is the same rule the ramp's dials hold</b>
/// (<see cref="IIntensityDial"/>): this calls nothing and nothing calls back into it. It owns a
/// store, hands out readings, and takes writes. The flash presenter PULLS a reading when it draws;
/// this never pushes.</para>
/// </summary>
public sealed class VisualsDials
{
    /// <summary>WPF's rack key for this row (<c>StudioTabView.xaml.cs:496</c>).</summary>
    public const string RowId = "visuals";

    /// <summary>The row's label as the shipping app shows it (<c>StudioTabView.xaml.cs:496</c>,
    /// <c>section_visuals</c>).</summary>
    public const string DisplayTitle = "Visuals";

    private readonly PersistenceStore<VisualsPresetDocument> _preset;

    public VisualsDials(PersistenceStore<VisualsPresetDocument> preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _preset = preset;
    }

    /// <summary>Raised after any dial moves, so the panel that shows them can repaint. It carries
    /// no value: a reader reads <see cref="Draw"/>, which is the one place the numbers live.</summary>
    public event Action? Changed;

    /// <summary>The document, for a panel that wants the numbers without going through the
    /// reading.</summary>
    public VisualsPresetDocument Preset => _preset.Current;

    /// <summary>The reading the next flash will be drawn with.</summary>
    public FlashDraw Draw() => FlashDraw.From(_preset.Current);

    /// <summary>Move the size dial. Clamped by the document, WPF's clamp.</summary>
    public void SetImageScalePercent(int percent) => Move(p => p.ImageScalePercent = percent);

    /// <summary>
    /// Move the opacity dial. Clamped by the document, WPF's clamp.
    ///
    /// <para>This is also the dial WPF's Intensity Ramp drives — <c>RampLinkFlashOpacity</c>
    /// (<c>MainWindow/MainWindow.StartStop.cs:506-510</c>) — so <see cref="FlashOpacityDial"/>
    /// routes through here rather than round it, for the reason
    /// <see cref="SpiralOpacityDial"/> gives: the ramp and the panel's own slider must converge on
    /// one behaviour rather than two.</para>
    /// </summary>
    public void SetFlashOpacityPercent(int percent) => Move(p => p.FlashOpacityPercent = percent);

    /// <summary>Move the duration dial. Clamped by the document, WPF's clamp.</summary>
    public void SetFlashDurationSeconds(int seconds) => Move(p => p.FlashDurationSeconds = seconds);

    private void Move(Action<VisualsPresetDocument> mutation)
    {
        _preset.Mutate(mutation);
        Changed?.Invoke();
    }
}
