using ConditioningControlPanel.Core.Services.Bark;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the BARK-1 slice-2 speak-delivery planner (<see cref="BarkSpeakPlanner"/>): the {0}
/// focused-app substitution (WPF BarkService.cs:1635-1641) and the delivery-kind routing
/// (WPF Speak :1595-1624 — mute-egg silent path, GigglePriority preempt vs queued Giggle).
/// Pure logic exercised directly; the Avalonia AvatarBarkSpeaker is a thin UI-marshal over these.
/// </summary>
public class BarkSpeakPlannerTests
{
    // ---- SubstituteFocusedApp (WPF BarkService.cs:1635-1641) ----

    [Fact]
    public void SubstituteFocusedApp_Leaves_Line_Without_Token_Untouched()
    {
        var result = BarkSpeakPlanner.SubstituteFocusedApp("hello there", "Chrome", null, null);
        Assert.Equal("hello there", result);
    }

    [Fact]
    public void SubstituteFocusedApp_Prefers_Classified_Service_Name()
    {
        var result = BarkSpeakPlanner.SubstituteFocusedApp("Still browsing {0}?", "Chrome", "Google Chrome", "Explorer");
        Assert.Equal("Still browsing Chrome?", result);
    }

    [Fact]
    public void SubstituteFocusedApp_Falls_Back_To_Detected_Name_When_Service_Empty()
    {
        var result = BarkSpeakPlanner.SubstituteFocusedApp("Still on {0}?", "", "Google Chrome", "Explorer");
        Assert.Equal("Still on Google Chrome?", result);
    }

    [Fact]
    public void SubstituteFocusedApp_Falls_Back_To_Foreground_Title_When_Awareness_Has_No_Name()
    {
        // Port-extra fallback (IForegroundWindowTitleProvider) — WPF stops at CurrentDetectedName.
        var result = BarkSpeakPlanner.SubstituteFocusedApp("Look at {0}", null, "  ", "Spotify Premium");
        Assert.Equal("Look at Spotify Premium", result);
    }

    [Fact]
    public void SubstituteFocusedApp_Uses_Neutral_Fallback_When_Nothing_Available()
    {
        // WPF :1639 — app falls back to "that".
        var result = BarkSpeakPlanner.SubstituteFocusedApp("Back to {0}!", null, null, null);
        Assert.Equal("Back to that!", result);
    }

    [Fact]
    public void SubstituteFocusedApp_Replaces_Every_Token_Occurrence()
    {
        var result = BarkSpeakPlanner.SubstituteFocusedApp("{0}, {0}, {0}", "Discord", null, null);
        Assert.Equal("Discord, Discord, Discord", result);
    }

    [Fact]
    public void SubstituteFocusedApp_Null_Or_Empty_Line_Is_Safe()
    {
        Assert.Equal(string.Empty, BarkSpeakPlanner.SubstituteFocusedApp(null, "X", null, null));
        Assert.Equal("", BarkSpeakPlanner.SubstituteFocusedApp("", "X", null, null));
    }

    [Fact]
    public void SubstituteFocusedApp_Does_Not_Touch_Key_Substitution_Tokens()
    {
        // {key} substitution is the engine's job (BarkEngine.ApplySubstitutions); the speaker only
        // owns {0}. A leftover {app} token must pass through untouched here.
        var result = BarkSpeakPlanner.SubstituteFocusedApp("{0} likes {app}", "Steam", null, null);
        Assert.Equal("Steam likes {app}", result);
    }

    // ---- PlanDelivery (WPF BarkService.cs:1595-1624) ----

    [Fact]
    public void PlanDelivery_Normal_NonPriority_Queues_Via_Giggle()
    {
        Assert.Equal(BarkDeliveryKind.Giggle,
            BarkSpeakPlanner.PlanDelivery(BarkClass.Normal, muted: false, priority: false));
    }

    [Fact]
    public void PlanDelivery_Normal_Priority_Preempts_Via_GigglePriority()
    {
        Assert.Equal(BarkDeliveryKind.GigglePriority,
            BarkSpeakPlanner.PlanDelivery(BarkClass.Normal, muted: false, priority: true));
    }

    [Fact]
    public void PlanDelivery_Safety_Priority_Preempts_Via_GigglePriority()
    {
        Assert.Equal(BarkDeliveryKind.GigglePriority,
            BarkSpeakPlanner.PlanDelivery(BarkClass.Safety, muted: false, priority: true));
    }

    [Fact]
    public void PlanDelivery_EasterEgg_Not_Muted_Routes_By_Priority_Not_MuteEgg()
    {
        // The mute-egg only triggers when muted; an audible egg still follows normal routing.
        Assert.Equal(BarkDeliveryKind.GigglePriority,
            BarkSpeakPlanner.PlanDelivery(BarkClass.EasterEgg, muted: false, priority: true));
        Assert.Equal(BarkDeliveryKind.Giggle,
            BarkSpeakPlanner.PlanDelivery(BarkClass.EasterEgg, muted: false, priority: false));
    }

    [Fact]
    public void PlanDelivery_EasterEgg_Muted_Is_Silent_MuteEgg_Regardless_Of_Priority()
    {
        // WPF :1595 — Class==EasterEgg && MasterVolume==0 → silent text-only Giggle, no audio.
        Assert.Equal(BarkDeliveryKind.SilentMuteEgg,
            BarkSpeakPlanner.PlanDelivery(BarkClass.EasterEgg, muted: true, priority: true));
        Assert.Equal(BarkDeliveryKind.SilentMuteEgg,
            BarkSpeakPlanner.PlanDelivery(BarkClass.EasterEgg, muted: true, priority: false));
    }

    [Fact]
    public void PlanDelivery_NonEgg_Muted_Is_Not_MuteEgg()
    {
        // Only EasterEgg gets the silent path; a muted Normal/Safety bark still routes normally.
        Assert.Equal(BarkDeliveryKind.Giggle,
            BarkSpeakPlanner.PlanDelivery(BarkClass.Normal, muted: true, priority: false));
        Assert.Equal(BarkDeliveryKind.GigglePriority,
            BarkSpeakPlanner.PlanDelivery(BarkClass.Safety, muted: true, priority: true));
    }

    // ---- Constants match WPF ----

    [Fact]
    public void SelfEchoMuteMs_Matches_WPF()
    {
        // WPF BarkService.cs:84 SelfEchoMuteMs = 8000.
        Assert.Equal(8000, BarkSpeakPlanner.SelfEchoMuteMs);
    }

    [Fact]
    public void FocusedAppFallback_Matches_WPF()
    {
        // WPF BarkService.cs:1639 — neutral fallback "that".
        Assert.Equal("that", BarkSpeakPlanner.FocusedAppFallback);
    }
}
