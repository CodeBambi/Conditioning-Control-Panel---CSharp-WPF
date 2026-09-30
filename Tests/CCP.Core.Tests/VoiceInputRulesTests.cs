using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The She's Listening rules shared by both heads (WPF AutonomyService.Voice.cs /
/// MainWindow.SheListening.cs delegate to them).</summary>
public class VoiceInputRulesTests
{
    private static AppSettings Armed() => new()
    {
        MicConsentGiven = true, SpeechWakeWordEnabled = true, SpeechPushToTalkEnabled = true, SpeechWakeWords = "hey bambi",
    };

    [Fact]
    public void Modes_run_only_when_entitled_consented_and_speech_is_available()
    {
        Assert.Equal((true, true), VoiceInputRules.ModesToRun(Armed(), entitled: true, speechAvailable: true));
        Assert.Equal((false, false), VoiceInputRules.ModesToRun(Armed(), entitled: false, speechAvailable: true));
        Assert.Equal((false, false), VoiceInputRules.ModesToRun(Armed(), entitled: true, speechAvailable: false));
        var noConsent = Armed(); noConsent.MicConsentGiven = false;
        Assert.Equal((false, false), VoiceInputRules.ModesToRun(noConsent, true, true));
        var noWords = Armed(); noWords.SpeechWakeWords = " , ;";
        Assert.Equal((false, true), VoiceInputRules.ModesToRun(noWords, true, true));
    }

    [Fact]
    public void Armed_needs_consent_and_a_mode()
    {
        Assert.True(VoiceInputRules.MicIsArmed(Armed()));
        var s = Armed(); s.SpeechWakeWordEnabled = false; s.SpeechPushToTalkEnabled = false;
        Assert.False(VoiceInputRules.MicIsArmed(s));
        s = Armed(); s.MicConsentGiven = false;
        Assert.False(VoiceInputRules.MicIsArmed(s));
    }

    [Fact]
    public void Wake_variants_keep_the_canonical_phrase_first_without_duplicates()
    {
        var words = VoiceInputRules.ExpandWakeVariants(VoiceInputRules.WakeWords("hey bambi; Hey Bambi\nhello"));
        Assert.Equal(new[] { "hey bambi", "hello", "hey bamby", "hey bambie", "hey bambee", "hey bombi", "hey bambit" }, words);
    }

    [Fact]
    public void Sensitivity_dial_is_inverse_and_round_trips()
    {
        Assert.Equal(0.045, VoiceInputRules.SensToThreshold(0), 6);
        Assert.Equal(0.004, VoiceInputRules.SensToThreshold(100), 6);
        Assert.Equal(0.004, VoiceInputRules.SensToThreshold(250), 6);
        Assert.Equal(37, VoiceInputRules.ThresholdToSens(VoiceInputRules.SensToThreshold(37)), 6);
    }
}
