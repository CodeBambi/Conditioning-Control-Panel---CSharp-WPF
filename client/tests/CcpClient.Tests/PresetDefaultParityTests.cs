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
    /// <b>And its rate default disagrees too, for a subtler reason.</b> The port takes 2 from the
    /// slider's markup (<c>Features/VideoFeatureControl.xaml:79</c>, <c>Value="2"</c>), but that
    /// literal is overwritten the moment the control loads — <c>SliderPerHour.Value =
    /// s.VideosPerHour;</c> (<c>Features/VideoFeatureControl.xaml.cs:54</c>) — so what a fresh
    /// upstream install actually shows is the SETTINGS default, <c>private int _videosPerHour = 6;</c>
    /// (<c>Models/AppSettings.cs:992</c>). A XAML literal is a design-time placeholder, not a
    /// default; that is the general lesson, and this fact is where it is written down.
    /// </summary>
    [Fact]
    public void MandatoryVideosRateDefault_ComesFromASliderLiteralUpstreamOverwritesOnLoad()
    {
        Assert.Equal(2, MandatoryVideoSchedule.DefaultPerHour);
        Assert.Equal(6, UpstreamAppSettings.Int("_videosPerHour"));
    }
}
