using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Every ported module's shipped <c>Enabled</c> default, against the field initializer upstream
/// actually declares.
///
/// <para><b>Why this exists.</b> <see cref="SpiralPresetDocument.Enabled"/> is the one module
/// document in the tree that ships <c>true</c>, and ten siblings shipping <c>false</c> makes it look
/// like a typo. It is not: WPF declares <c>private bool _spiralEnabled = true;</c>. Nothing anywhere
/// held that fact, so a future reader "fixing the inconsistency" would have silently changed what a
/// fresh install does — and the only way to notice would have been to install the two products side
/// by side. Reading it out of the sweep found a second module where the port and upstream really do
/// disagree, which is the reason this is a SWEEP and not one assertion about the spiral.</para>
///
/// <para><b>Why the expected value is READ rather than typed.</b> A fact that asserted
/// <c>Assert.True(new SpiralPresetDocument().Enabled)</c> would pin the port against a number in
/// this file, and a number in this file is exactly as capable of being wrong as the one it guards.
/// The comparison is against <c>ConditioningControlPanel/Models/AppSettings.cs</c> in this
/// checkout, matched by FIELD NAME rather than by line, so it survives upstream moving the
/// declaration and reddens only when a declared DEFAULT changes on either side.</para>
///
/// <para><b>It cannot cry wolf.</b> The read-only WPF tree is pinned byte-identical to its
/// <c>main</c> baseline by <see cref="ReadOnlyWpfTreeGuardTests"/>, so upstream cannot move under
/// these facts inside this repository; and if a future upstream sync really did flip one of these
/// defaults, a red here is the correct answer rather than a nuisance — the port's default would
/// genuinely have stopped matching the product it is a port of.</para>
/// </summary>
public class PresetDefaultParityTests
{
    /// <summary>
    /// One row per ported module: the module's name, the <c>Enabled</c> default a freshly
    /// constructed document carries, and the upstream backing FIELD that decides what upstream's
    /// fresh install does.
    ///
    /// <para><b>Visuals is absent on purpose</b> and is not an omission: it has no <c>Enabled</c>
    /// member at all, because upstream's Visuals row has no master toggle
    /// (<c>Features/VisualsFeatureControl.xaml:12-14</c>). Its module's dial is Flash Images', which
    /// is the <c>flash-images</c> row below.</para>
    /// </summary>
    private static readonly (string Module, bool PortDefault, string UpstreamField)[] Modules =
    [
        ("bouncing-text", new BouncingTextPresetDocument().Enabled, "_bouncingTextEnabled"),
        ("brain-drain", new BrainDrainPresetDocument().Enabled, "_brainDrainEnabled"),
        ("bubble-count", new BubbleCountPresetDocument().Enabled, "_bubbleCountEnabled"),
        ("bubble-pop", new BubblePopPresetDocument().Enabled, "_bubblesEnabled"),
        ("flash-images", new SessionPresetDocument().FlashEnabled, "_flashEnabled"),
        ("intensity-ramp", new IntensityRampPresetDocument().Enabled, "_intensityRampEnabled"),
        ("lock-card", new LockCardPresetDocument().Enabled, "_lockCardEnabled"),
        ("mandatory-video", new MandatoryVideoPresetDocument().Enabled, "_mandatoryVideosEnabled"),
        ("mind-wipe", new MindWipePresetDocument().Enabled, "_mindWipeEnabled"),
        ("pink-filter", new PinkFilterPresetDocument().Enabled, "_pinkFilterEnabled"),
        ("pop-quiz", new PopQuizPresetDocument().Enabled, "_popQuizEnabled"),
        ("spiral", new SpiralPresetDocument().Enabled, "_spiralEnabled"),
        ("subliminal", new SubliminalPresetDocument().Enabled, "_subliminalEnabled"),
    ];

    /// <summary>
    /// The modules whose default is KNOWN to disagree with upstream's, each with its own fact
    /// below. Listing one here does not forgive it — the fact that names it asserts BOTH values, so
    /// the disagreement cannot be quietly widened, narrowed or repaired without a red here.
    /// </summary>
    private static readonly string[] KnownDisagreements = ["mandatory-video"];

    // ---------------------------------------------------------------------------------
    //  the spiral, which is the one that looks wrong and is not
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheSpiralModuleShipsOn_BecauseUpstreamShipsItOn_NotBecauseThePortChoseTo()
    {
        var upstream = UpstreamAppSettings.Bool("_spiralEnabled");

        // Both halves are asserted, and separately: "the port ships on" and "upstream ships on" are
        // different claims, and only the pair of them makes this parity rather than a coincidence.
        Assert.True(upstream, "WPF declares `private bool _spiralEnabled = true;` in Models/AppSettings.cs:2672");
        Assert.True(new SpiralPresetDocument().Enabled);
        Assert.Equal(upstream, new SpiralPresetDocument().Enabled);

        // And it is genuinely the odd one out, which is the observation that made this look like a
        // defect. Counting it here means the sweep below can never be read as "they are all true".
        var on = Modules.Count(m => m.PortDefault);
        Assert.Equal(2, on); // spiral and flash-images, and nothing else
    }

    // ---------------------------------------------------------------------------------
    //  the sweep
    // ---------------------------------------------------------------------------------

    [Fact]
    public void EveryPortedModulesEnabledDefault_AgreesWithUpstreamsOwnFieldInitializer()
    {
        // Never vacuous: an emptied table would pass an empty-disagreement assertion trivially.
        Assert.Equal(13, Modules.Length);

        var disagreements = new List<string>();
        foreach (var (module, portDefault, upstreamField) in Modules)
        {
            var upstream = UpstreamAppSettings.Bool(upstreamField);
            if (portDefault != upstream && !KnownDisagreements.Contains(module))
            {
                disagreements.Add(
                    $"{module}: the port ships Enabled={portDefault} but WPF declares "
                    + $"{upstreamField} = {upstream.ToString().ToLowerInvariant()} in Models/AppSettings.cs");
            }
        }

        Assert.Empty(disagreements);
    }

    [Fact]
    public void EveryKnownDisagreement_StillDisagrees_SoTheAllowListCannotRot()
    {
        Assert.NotEmpty(KnownDisagreements);

        var repaired = new List<string>();
        foreach (var module in KnownDisagreements)
        {
            var row = Modules.Single(m => m.Module == module);
            if (row.PortDefault == UpstreamAppSettings.Bool(row.UpstreamField))
            {
                repaired.Add($"{module}: now AGREES with {row.UpstreamField} — drop it from KnownDisagreements");
            }
        }

        Assert.Empty(repaired);
    }

    // ---------------------------------------------------------------------------------
    //  the second disagreement, found by the sweep rather than by looking for it
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// <b>Mandatory Video ships OFF here and ON upstream.</b> WPF declares
    /// <c>private bool _mandatoryVideosEnabled = true;</c> (<c>Models/AppSettings.cs:985</c>) and its
    /// scheduler re-reads that flag on every tick (<c>Services/Video/VideoService.cs:2218</c>), so a
    /// fresh upstream install has the row armed; the port's document leaves it at the implicit
    /// <c>false</c> and its own remarks assert "Ships OFF", which is a claim about upstream that
    /// upstream contradicts.
    ///
    /// <para><b>Not repaired here.</b> Flipping it turns full-screen video on by default for every
    /// fresh port install, which is a product decision about a module this packet was not given, and
    /// it moves a rack dot several headless facts read. It is pinned instead, with both values, so
    /// the decision is taken deliberately rather than discovered again by accident.</para>
    /// </summary>
    [Fact]
    public void MandatoryVideo_ShipsOffHereAndOnUpstream_AndThatIsRecordedRatherThanPapered()
    {
        Assert.False(new MandatoryVideoPresetDocument().Enabled);
        Assert.True(UpstreamAppSettings.Bool("_mandatoryVideosEnabled"));
    }

    /// <summary>
    /// <b>Its rate default disagreed too, for a subtler reason — and unlike the flag above, that one
    /// was repaired.</b> The port took 2 from the slider's markup
    /// (<c>Features/VideoFeatureControl.xaml:79</c>, <c>Value="2"</c>), but that literal is
    /// overwritten the moment the control loads — <c>SliderPerHour.Value = s.VideosPerHour;</c>
    /// (<c>Features/VideoFeatureControl.xaml.cs:54</c>) — so what a fresh upstream install actually
    /// runs at is the SETTINGS default, <c>private int _videosPerHour = 6;</c>
    /// (<c>Models/AppSettings.cs:992</c>). <b>A XAML literal is a design-time placeholder, not a
    /// default</b>; that is the general lesson, and this fact is where it is written down.
    ///
    /// <para><b>Why this one was corrected and the flag was not.</b> The rate carries no product
    /// decision with it — six clips an hour is simply what upstream schedules, and the module still
    /// ships disarmed, so nothing appears on screen that did not before. The flag turns a
    /// full-monitor surface on for every fresh install, which is why it stays recorded rather than
    /// repaired.</para>
    /// </summary>
    [Fact]
    public void MandatoryVideosRateDefault_IsUpstreamsSettingsDefault_NotTheSliderLiteralItOverwrites()
    {
        // Typed from AppSettings.cs:992 rather than read from the constant under test. Asserting the
        // port constant against itself would pass at every value, which is the whole failure mode
        // this fact exists to catch. (The port side is bound to a local only to keep xUnit2000 from
        // demanding the constant sit in the 'expected' slot, which would read backwards here.)
        var port = MandatoryVideoSchedule.DefaultPerHour;
        Assert.Equal(6, UpstreamAppSettings.Int("_videosPerHour"));
        Assert.Equal(UpstreamAppSettings.Int("_videosPerHour"), port);

        // And it is no longer sitting on the markup literal it was taken from.
        Assert.NotEqual(SliderLiteralThatIsNotADefault, port);
    }

    /// <summary>The slider markup literal the port once mistook for a default
    /// (<c>Features/VideoFeatureControl.xaml:79</c>, <c>Value="2"</c>).</summary>
    private const int SliderLiteralThatIsNotADefault = 2;

    // ---------------------------------------------------------------------------------
    //  the numeric sweep, which is what the XAML-literal lesson generalises into
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// One row per ported SCALAR default whose upstream counterpart is a literal field initializer in
    /// <c>Models/AppSettings.cs</c>. Same shape and same reason as <see cref="Modules"/>: the
    /// expected value is READ out of upstream by field name, so each row is evidence rather than a
    /// number retyped beside the number it guards.
    ///
    /// <para><b>Five ported defaults are absent from this table on purpose, and none of them is
    /// unchecked.</b> <c>BubbleCountAnswer.DefaultAttempts</c> is a window field with no setting
    /// behind it at all (<c>Windows/BubbleCountResultWindow.xaml.cs:24</c>);
    /// <c>FlashFrameDelay.Default</c> and <c>SpiralFrameDelay.Default</c> are codec fallbacks for a
    /// clip that carries no usable frame delay (<c>Services/Media/AnimatedWebp.cs:210-211</c>,
    /// <c>Services/Notifications/OverlayService.cs:1594</c>) rather than dials;
    /// <c>BubblePopField.DefaultSizePercent</c> tracks a named constant instead of a literal —
    /// upstream declares <c>_bubblesSize = Services.BubbleSizing.UserPercentDefault</c>
    /// (<c>Models/AppSettings.cs:2796</c>), and that constant is 100
    /// (<c>Services/BubbleSizing.cs:60</c>), which is what the port carries; and
    /// <c>BrainDrainPresetDocument.DefaultVolumePercent</c> is a recorded divergence standing in for
    /// an app-wide master volume this port has no dial for. Sourcing a default from markup or from a
    /// service field is a defect only when the settings model says something else — where upstream
    /// has no settings-model equivalent, the other source is the only truth there is.</para>
    /// </summary>
    private static readonly (string Dial, int PortDefault, string UpstreamField)[] Scalars =
    [
        ("bouncing-text/opacity-percent", BouncingTextPresetDocument.DefaultOpacityPercent, "_bouncingTextOpacity"),
        ("bouncing-text/size-percent", BouncingTextPresetDocument.DefaultSizePercent, "_bouncingTextSize"),
        ("bouncing-text/speed", BouncingTextPresetDocument.DefaultSpeed, "_bouncingTextSpeed"),
        ("bubble-count/per-hour", BubbleCountSchedule.DefaultPerHour, "_bubbleCountFrequency"),
        ("bubble-pop/per-minute", BubblePopField.DefaultPerMinute, "_bubblesFrequency"),
        ("flash-images/duration-seconds", VisualsPresetDocument.DefaultFlashDurationSeconds, "_flashDuration"),
        ("flash-images/opacity-percent", VisualsPresetDocument.DefaultFlashOpacityPercent, "_flashOpacity"),
        ("intensity-ramp/duration-minutes", IntensityRampPresetDocument.DefaultDurationMinutes, "_rampDurationMinutes"),
        ("lock-card/per-hour", LockCardSchedule.DefaultPerHour, "_lockCardFrequency"),
        ("lock-card/repeats", LockCardSchedule.DefaultRepeats, "_lockCardRepeats"),
        ("mandatory-video/max-seconds", MandatoryVideoPresetDocument.DefaultMaxSeconds, "_videoMaxDurationSeconds"),
        ("mandatory-video/per-hour", MandatoryVideoSchedule.DefaultPerHour, "_videosPerHour"),
        ("mind-wipe/per-hour", MindWipeSchedule.DefaultPerHour, "_mindWipeFrequency"),
        ("mind-wipe/volume-percent", MindWipePresetDocument.DefaultVolumePercent, "_mindWipeVolume"),
        ("pink-filter/opacity-percent", PinkFilterPresetDocument.DefaultOpacityPercent, "_pinkFilterOpacity"),
        ("pop-quiz/per-hour", PopQuizSchedule.DefaultPerHour, "_popQuizFrequency"),
        ("spiral/opacity-percent", SpiralPresetDocument.DefaultOpacityPercent, "_spiralOpacity"),
        ("subliminal/duration-frames", SubliminalPresetDocument.DefaultDurationFrames, "_subliminalDuration"),
        ("subliminal/opacity-percent", SubliminalPresetDocument.DefaultOpacityPercent, "_subliminalOpacity"),
        ("subliminal/per-minute", SubliminalPresetDocument.DefaultPerMinute, "_subliminalFrequency"),
        ("visuals/image-scale-percent", VisualsPresetDocument.DefaultImageScalePercent, "_imageScale"),
    ];

    /// <summary>
    /// The scalar dials whose default is KNOWN to disagree with upstream's, each with its own fact
    /// below. Listing one here does not forgive it: the fact that names it asserts BOTH values.
    /// </summary>
    private static readonly string[] KnownScalarDisagreements = ["brain-drain/intensity-percent"];

    [Fact]
    public void EveryPortedScalarDefault_AgreesWithUpstreamsOwnFieldInitializer()
    {
        // Never vacuous: an emptied table would pass an empty-disagreement assertion trivially.
        Assert.Equal(21, Scalars.Length);

        var disagreements = new List<string>();
        foreach (var (dial, portDefault, upstreamField) in Scalars)
        {
            var upstream = UpstreamAppSettings.Int(upstreamField);
            if (portDefault != upstream)
            {
                disagreements.Add(
                    $"{dial}: the port ships {portDefault} but WPF declares "
                    + $"{upstreamField} = {upstream} in Models/AppSettings.cs");
            }
        }

        Assert.Empty(disagreements);
    }

    /// <summary>
    /// The subliminal card's three colours, which are the sweep's only string-valued defaults.
    /// </summary>
    [Fact]
    public void TheSubliminalCardsColours_AreUpstreamsOwnFieldInitializers()
    {
        // Bound to locals for the xUnit2000 reason noted above.
        string background = SubliminalPresetDocument.DefaultBackgroundColour;
        string text = SubliminalPresetDocument.DefaultTextColour;
        string outline = SubliminalPresetDocument.DefaultOutlineColour;

        Assert.Equal(UpstreamAppSettings.String("_subBackgroundColor"), background);
        Assert.Equal(UpstreamAppSettings.String("_subTextColor"), text);
        Assert.Equal(UpstreamAppSettings.String("_subBorderColor"), outline);
    }

    // ---------------------------------------------------------------------------------
    //  the third disagreement, and the one the XAML lesson would have missed
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// <b>Brain Drain's intensity is 50 here and 20 upstream — and the trap is the same one as the
    /// video slider's, wearing different clothes.</b> The port took 50 from the SERVICE's own field
    /// initializer, <c>private double _intensity = 50; // 50% default intensity</c>
    /// (<c>Services/LockCard/BrainDrainService.cs:21</c>). That field is overwritten before the
    /// timer ever ticks: <c>Start</c> calls <c>UpdateSettings()</c>
    /// (<c>Services/LockCard/BrainDrainService.cs:229</c>), which assigns
    /// <c>Intensity = App.Settings.Current.BrainDrainIntensity;</c> (<c>:274</c>) — and that setting
    /// declares <c>private int _brainDrainIntensity = 20;</c>
    /// (<c>Models/AppSettings.cs:3860</c>). <b>A service field initializer is a design-time
    /// placeholder for exactly the same reason a XAML literal is</b>, so the sweep that found the
    /// video rate had to be run against non-markup sources too.
    ///
    /// <para><b>Not repaired here, and the reason is not timidity.</b> Upstream contradicts ITSELF
    /// on this number across three sites: the settings model says 20, the service field says 50, and
    /// the shareable preset model says 50 as well
    /// (<c>Models/Preset.cs:144</c>, <c>public int BrainDrainIntensity { get; set; } = 50;</c>),
    /// which is written straight back into settings whenever a preset is applied (<c>:414</c>). The
    /// repair rule for the video rate was "a plain number whose only consequence is matching
    /// upstream", and that precondition fails when upstream has two numbers. Pinned with both, so
    /// the decision is taken deliberately rather than discovered a third time.</para>
    /// </summary>
    [Fact]
    public void BrainDrainIntensity_ShipsAtTheServiceFieldNotTheSetting_AndThatIsRecordedRatherThanPapered()
    {
        // Typed from AppSettings.cs:3860, not read from the constant under test.
        var port = BrainDrainSchedule.DefaultIntensity;
        Assert.Equal(20, UpstreamAppSettings.Int("_brainDrainIntensity"));
        Assert.Equal(50, port);
        Assert.NotEqual(UpstreamAppSettings.Int("_brainDrainIntensity"), port);
    }

    [Fact]
    public void EveryKnownScalarDisagreement_IsAbsentFromTheSweep_SoNeitherCanHideTheOther()
    {
        Assert.NotEmpty(KnownScalarDisagreements);

        // The allow-list and the sweep table must stay disjoint: a dial that appeared in both would
        // be asserted to agree and excused for disagreeing at the same time.
        foreach (var dial in KnownScalarDisagreements)
        {
            Assert.DoesNotContain(Scalars, row => row.Dial == dial);
        }
    }
}
