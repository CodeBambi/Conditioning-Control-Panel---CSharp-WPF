using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// SessionEngine ramps flash opacity, frequency and scale every second of a session. Those used to
/// be written into the user's own persisted settings, so an app kill or a crash mid-session froze a
/// ramp's maximum into settings.json for good - the same shape of bug as the pink filter's "the
/// screen keeps getting more pink and stays that way" (#471/#476), whose ramp was moved off this
/// path for exactly this reason. RestoreSettings only heals a CLEAN stop, and a crash is the one
/// case that matters.
///
/// The overlay has to satisfy two things that pull against each other: FlashService must read the
/// ramped value (it reads the plain property, everywhere), and the file must keep the user's own.
/// The JSON key must also be byte-identical to what shipped, or every existing settings.json loses
/// three settings on upgrade.
/// Pure data class, so no WPF Application is required.
/// </summary>
public class SessionFlashRampOverlayTests
{
    [Fact]
    public void WithNoSessionRunning_TheGettersReturnTheUsersOwnValues()
    {
        var settings = new AppSettings { FlashOpacity = 70, FlashFrequency = 30, ImageScale = 120 };

        Assert.Equal(70, settings.FlashOpacity);
        Assert.Equal(30, settings.FlashFrequency);
        Assert.Equal(120, settings.ImageScale);
    }

    [Fact]
    public void AParkedRamp_IsWhatReadersSee()
    {
        // FlashService reads these properties directly - if the overlay did not surface here, the
        // session would simply stop ramping.
        var settings = new AppSettings { FlashOpacity = 70, FlashFrequency = 30, ImageScale = 120 };

        settings.SetSessionFlashRamp(opacity: 100, frequency: 180, imageScale: 200);

        Assert.Equal(100, settings.FlashOpacity);
        Assert.Equal(180, settings.FlashFrequency);
        Assert.Equal(200, settings.ImageScale);
    }

    [Fact]
    public void AParkedRamp_IsNeverWrittenToDisk()
    {
        // The whole point. Serialize mid-ramp - which is what a crash is racing - and the file
        // still describes the user's settings, not the session's.
        var settings = new AppSettings { FlashOpacity = 70, FlashFrequency = 30, ImageScale = 120 };
        settings.SetSessionFlashRamp(opacity: 100, frequency: 180, imageScale: 200);

        var json = JObject.Parse(JsonConvert.SerializeObject(settings));

        Assert.Equal(70, (int)json["FlashOpacity"]!);
        Assert.Equal(30, (int)json["FlashFrequency"]!);
        Assert.Equal(120, (int)json["ImageScale"]!);
    }

    [Fact]
    public void ClearingTheRamp_HandsTheValuesBack()
    {
        var settings = new AppSettings { FlashOpacity = 70, FlashFrequency = 30, ImageScale = 120 };
        settings.SetSessionFlashRamp(100, 180, 200);

        settings.ClearSessionFlashRamp();

        Assert.Equal(70, settings.FlashOpacity);
        Assert.Equal(30, settings.FlashFrequency);
        Assert.Equal(120, settings.ImageScale);
    }

    [Fact]
    public void ClearingARampThatWasNeverParked_IsHarmless()
    {
        // StopSession clears unconditionally, including on a session that never ramped anything.
        var settings = new AppSettings { FlashOpacity = 70 };

        settings.ClearSessionFlashRamp();

        Assert.Equal(70, settings.FlashOpacity);
    }

    [Fact]
    public void ARampCanOverrideOneValueWithoutTouchingTheOthers()
    {
        // A session that ramps opacity but sets no scale must leave the user's scale alone.
        var settings = new AppSettings { FlashOpacity = 70, FlashFrequency = 30, ImageScale = 120 };

        settings.SetSessionFlashRamp(opacity: 95, frequency: null, imageScale: null);

        Assert.Equal(95, settings.FlashOpacity);
        Assert.Equal(30, settings.FlashFrequency);
        Assert.Equal(120, settings.ImageScale);
    }

    [Fact]
    public void UserEditsDuringARamp_LandOnThePersistedValue()
    {
        // The settings sliders keep writing the user's own value even while a session ramps over
        // the top of it, exactly like the pink filter's slider does.
        var settings = new AppSettings { FlashOpacity = 70 };
        settings.SetSessionFlashRamp(opacity: 100, frequency: null, imageScale: null);

        settings.FlashOpacity = 55;

        Assert.Equal(100, settings.FlashOpacity);   // the session still owns what renders

        settings.ClearSessionFlashRamp();
        Assert.Equal(55, settings.FlashOpacity);    // and the edit was never lost
    }

    [Fact]
    public void TheJsonKeysAreUnchanged_SoExistingSettingsFilesStillLoad()
    {
        // Moving the [JsonProperty] onto the backing field must not rename anything: a key change
        // here silently resets three settings for every existing install.
        var json = "{\"FlashOpacity\":42,\"FlashFrequency\":77,\"ImageScale\":210}";

        var settings = JsonConvert.DeserializeObject<AppSettings>(json)!;

        Assert.Equal(42, settings.FlashOpacity);
        Assert.Equal(77, settings.FlashFrequency);
        Assert.Equal(210, settings.ImageScale);
    }

    [Fact]
    public void APersistedValue_SurvivesAFullRoundTrip()
    {
        var original = new AppSettings { FlashOpacity = 61, FlashFrequency = 91, ImageScale = 175 };

        var restored = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(original))!;

        Assert.Equal(61, restored.FlashOpacity);
        Assert.Equal(91, restored.FlashFrequency);
        Assert.Equal(175, restored.ImageScale);
    }
}
