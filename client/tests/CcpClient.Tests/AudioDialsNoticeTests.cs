using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The AUDIO row's sentences. They are pinned because they are the only thing on that panel that
/// can distinguish a working volume dial from a dead one: silence, a clip played too quietly to
/// hear, an endpoint that was never opened and an endpoint that refused all look identical from
/// outside the process, so what the panel SAYS is the product.
///
/// <para><b>Three distinctions are load-bearing and each has its own fact below.</b> "Nothing has
/// been asked yet" is not "unavailable"; "remembered but not connected" is not "you have the system
/// default"; and "the app cued a clip" is not "you heard something". A sentence that collapsed any
/// of the three would be a lie a user could not check.</para>
/// </summary>
public sealed class AudioDialsNoticeTests
{
    // ---------- the two dials ----------

    [Fact]
    public void TheMasterDial_NamesItsOneReaderAndWhenItReads_AndTreatsZeroAsARealSetting()
    {
        // There is exactly one reader in this build: the companion's spoken lines take the value
        // when their window is BUILT (Features/Dtrh/DtrhHostWindow.axaml.cs:268 ->
        // Companion/BarkPipeline.cs:613). Saying when it reads is the difference between a user
        // believing the slider is broken and knowing it applies to the next window.
        var live = AudioDialsNotices.DescribeMaster(64);
        Assert.Contains("64%", live, StringComparison.Ordinal);
        Assert.Contains("the companion's spoken lines", live, StringComparison.Ordinal);
        Assert.Contains("reaches the next one", live, StringComparison.Ordinal);

        // Zero is upstream's own real state — "ALL audio will be silent" (Services/AudioService.cs:
        // 535-536) — and the port surfaces text with no voice (BarkPipeline.cs:365). The sentence
        // must not read as an error.
        var muted = AudioDialsNotices.DescribeMaster(0);
        Assert.Contains("real setting", muted, StringComparison.Ordinal);
        Assert.Contains("as text", muted, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVideoDial_SaysItStoresAPreferenceNothingReadsYet_RatherThanImplyingPlayback()
    {
        // VideoVolume is persisted (Models/AppSettings.cs:1134) and has NO consumer in this build:
        // the mandatory-video row plays no soundtrack, and its own document says why
        // (Session/MandatoryVideoPresetDocument.cs:17-19). The dial belongs because the setting is
        // real; the sentence is what stops it claiming an effect it does not have.
        var text = AudioDialsNotices.DescribeVideo(50);
        Assert.Contains("50%", text, StringComparison.Ordinal);
        Assert.Contains("nothing in this build plays a video soundtrack yet", text, StringComparison.Ordinal);
        Assert.Contains("what the app remembers rather than what you hear", text, StringComparison.Ordinal);
    }

    // ---------- the endpoint choice ----------

    [Fact]
    public void TheChoiceLine_KeepsNotAskedYet_ConnectedAndGoneAsThreeDifferentSentences()
    {
        // NOT ASKED is not ABSENT. Nothing is enumerated until the panel is opened, and a panel
        // that reported "not connected" about a device nobody looked for would be inventing a
        // machine state (AudioParticipant.DeviceOutcome's own null-is-not-unavailable rule, applied
        // to the list instead of the device).
        var unasked = AudioDialsNotices.DescribeChoice("Studio Monitors", connected: null);
        Assert.Contains("Nothing has asked this machine", unasked, StringComparison.Ordinal);
        Assert.DoesNotContain("not in the list", unasked, StringComparison.Ordinal);

        var connected = AudioDialsNotices.DescribeChoice("Studio Monitors", connected: true);
        Assert.Contains("it is connected", connected, StringComparison.Ordinal);

        // GONE keeps the choice AND names the fallback, which is what the arbitration really does
        // with an absent name (Audio/SoundArbitration.cs:325-328, WPF AudioService.cs:292-293).
        var gone = AudioDialsNotices.DescribeChoice("Studio Monitors", connected: false);
        Assert.Contains("not in the list of endpoints", gone, StringComparison.Ordinal);
        Assert.Contains("falls back to the system default", gone, StringComparison.Ordinal);
        Assert.Contains("kept rather than quietly replaced", gone, StringComparison.Ordinal);

        // The empty choice is upstream's system default (Models/AppSettings.cs:1238-1240) and says
        // what picking one is FOR, in upstream's own streaming terms.
        foreach (var none in new[] { AudioDialsNotices.DescribeChoice(null, null), AudioDialsNotices.DescribeChoice("", true) })
        {
            Assert.Contains("the default", none, StringComparison.Ordinal);
            Assert.Contains("private headset", none, StringComparison.Ordinal);
        }
    }

    // ---------- what the operating system last said ----------

    [Fact]
    public void TheDeviceLine_SaysNotAskedYetWhenNothingHasBeen_AndNeverCallsItUnavailable()
    {
        var text = AudioDialsNotices.DescribeDeviceOutcome(null, attempts: 0);
        Assert.Contains("Nothing has been asked of the operating system yet", text, StringComparison.Ordinal);
        Assert.Contains("Nothing has brought a device up this launch", text, StringComparison.Ordinal);

        // The two words that must never appear on this branch: a launch that played nothing is the
        // participant's designed state, not a failure.
        Assert.DoesNotContain("No audio output", text, StringComparison.Ordinal);
        Assert.DoesNotContain("would not open", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeviceLine_QuotesTheTypedOutcome_AndSaysAFailedOneIsREMEMBERED()
    {
        var ready = AudioDialsNotices.DescribeDeviceOutcome(new SoundOutcome.Ready("Speakers (USB)"), 1);
        Assert.Contains("Speakers (USB)", ready, StringComparison.Ordinal);
        Assert.Contains("Device attempts this launch: 1", ready, StringComparison.Ordinal);

        // A FAILED init is remembered rather than retried per consumer (AudioParticipant.EnsureDevice:
        // retrying there would put a native device attempt on every window that opens while an
        // endpoint is down). The panel must SAY that, because the honest instruction to a user is
        // "press Test", not "restart".
        var failed = AudioDialsNotices.DescribeDeviceOutcome(new SoundOutcome.Failed("MA_DEVICE_NOT_STARTED"), 2);
        Assert.Contains("MA_DEVICE_NOT_STARTED", failed, StringComparison.Ordinal);
        Assert.Contains("remembered rather than retried", failed, StringComparison.Ordinal);
        Assert.Contains("pressing Test re-probes it", failed, StringComparison.Ordinal);

        // And the no-endpoints answer names the cooldown re-probe, which is where recovery really
        // lives (WPF #779, AudioService.cs:163-166).
        var unavailable = AudioDialsNotices.DescribeDeviceOutcome(
            new SoundOutcome.Unavailable("no render endpoints"), 1);
        Assert.Contains("No audio output: no render endpoints", unavailable, StringComparison.Ordinal);
        Assert.Contains("re-probes after the cooldown", unavailable, StringComparison.Ordinal);
    }

    // ---------- the test button ----------

    [Fact]
    public void TheTestRefusal_NamesTheFoldersAndSaysNoDeviceWasBroughtUp()
    {
        // Upstream's own branch (Services/AudioService.cs:600-607, "WARNING: No test sound files
        // found to play"), reached far more often here because this build ships no sound resources
        // at all. The refusal must be actionable — a user cannot fix "no test sound" without being
        // told where one goes.
        var text = AudioDialsNotices.DescribeTestRefusal(@"C:\data\assets\sounds\mindwipe or C:\data\assets\sounds\braindrain");
        Assert.Contains(@"C:\data\assets\sounds\mindwipe", text, StringComparison.Ordinal);
        Assert.Contains(@"C:\data\assets\sounds\braindrain", text, StringComparison.Ordinal);
        Assert.Contains(".mp3, .wav or .ogg", text, StringComparison.Ordinal);

        // And the refusal's own cost, stated because it is the reason the clip is looked for before
        // the device is asked for: nothing else on the machine was disturbed.
        Assert.Contains("nothing was opened", text, StringComparison.Ordinal);
        Assert.Contains("no audio device was brought up", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTestReport_NamesTheFixedHalfGainAndTheMasterItIgnores_AndClaimsNothingAboutHearing()
    {
        // The gain is upstream's literal, not a number chosen here: `_soundFile.Volume = 0.5f;
        // // Fixed 50% for test — bypasses curve` (Services/AudioService.cs:625).
        Assert.Equal(0.5f, AudioDialsNotices.TestGain);

        var text = AudioDialsNotices.DescribeTest(
            new SoundOutcome.Ready("Speakers (USB)"),
            new SoundOutcome.Started(SoundChannel.Sfx, 0),
            "chime.mp3",
            master: 0);

        Assert.Contains("Speakers (USB)", text, StringComparison.Ordinal);
        Assert.Contains("chime.mp3", text, StringComparison.Ordinal);
        Assert.Contains("fixed 50%", text, StringComparison.Ordinal);

        // The whole point of the fixed gain: a muted app can still prove its endpoint, and the
        // report quotes the master so a user who hears the test but nothing else knows why.
        Assert.Contains("ignores", text, StringComparison.Ordinal);
        Assert.Contains("yours is 0%", text, StringComparison.Ordinal);

        // THE LINE THIS FILE EXISTS FOR. The strongest claim available is that the cue was
        // ACCEPTED — which is the verb the report uses — and whether a speaker was on, plugged in,
        // muted at the endpoint or held in exclusive mode by something else is outside this process
        // entirely (verification-harness.md, audio evidence class: `render-metered` is not
        // `audible-verified`, and no automated step on any platform discharges the second).
        Assert.Contains("Cued", text, StringComparison.Ordinal);
        Assert.Contains("this app cannot see any of that", text, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("working", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTestReport_CarriesEveryTypedFailureThroughInsteadOfSayingItWorked()
    {
        var noDevice = AudioDialsNotices.DescribeTest(
            new SoundOutcome.Unavailable("no render endpoints"),
            new SoundOutcome.Unavailable("audio suppressed"),
            "chime.mp3",
            master: 40);
        Assert.Contains("Device: none — no render endpoints", noDevice, StringComparison.Ordinal);
        Assert.Contains("Nothing played: audio suppressed", noDevice, StringComparison.Ordinal);

        var dropped = AudioDialsNotices.DescribeTest(
            new SoundOutcome.Ready("Speakers"),
            new SoundOutcome.Dropped(SoundChannel.Sfx, SoundDropReason.PoolOverflow),
            "chime.mp3",
            master: 40);
        Assert.Contains("dropped (PoolOverflow)", dropped, StringComparison.Ordinal);

        var brokenPlayer = AudioDialsNotices.DescribeTest(
            new SoundOutcome.Ready("Speakers"),
            new SoundOutcome.Failed("InvalidOperationException: decoder"),
            "chime.mp3",
            master: 40);
        Assert.Contains("Playback failed: InvalidOperationException: decoder", brokenPlayer, StringComparison.Ordinal);

        // And the catch-all, which upstream also has (MainWindow/MainWindow.UiUpdates.cs:1081-1086):
        // a diagnostic that died silently would be the worst failure for the one button whose job is
        // to tell the truth about sound.
        var thrown = AudioDialsNotices.DescribeTestFailure(new InvalidOperationException("driver gone"));
        Assert.Contains("InvalidOperationException", thrown, StringComparison.Ordinal);
        Assert.Contains("driver gone", thrown, StringComparison.Ordinal);
    }
}
