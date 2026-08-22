using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Persistence;

/// <summary>
/// The persisted preset a conditioning session reads when the user presses START —
/// the port's counterpart of the dials WPF's <c>StartEngine</c> reads out of
/// <c>App.Settings.Current</c> (<c>MainWindow/MainWindow.StartStop.cs:163,181,200,...</c>).
///
/// <para>A NEW dedicated document, <c>session_preset.json</c> in the shared &lt;dataDir&gt; —
/// additive by construction, the <see cref="AssetSelectionDocument"/> precedent (no schema
/// bump anywhere else, no absent-member case, a Missing outcome is fresh defaults). It is
/// deliberately NOT a member of <see cref="DemoSettings"/>: that model's own doc says it is
/// "not a feature model", and this is the first one that is.</para>
///
/// <para><b>Every value here is a dial some effect actually consumes.</b> WPF's flash block
/// carries a dozen more (<c>FlashOpacity</c>, <c>ImageScale</c>, <c>FlashDuration</c>,
/// <c>FadeDuration</c>, <c>FlashClickable</c>, <c>HydraLimit</c>, <c>FlashAvoidCenter</c>, …
/// <c>CCP.Core/Models/AppSettings.cs:749-960</c>) and every one of them describes how a flash
/// is DRAWN. The port draws no flash (see <see cref="Effects.FlashImagesEffect"/>), so
/// persisting them would write settings nothing reads — the storage form of the greyed dial
/// this port refuses to render. They arrive with the surface that honours them.</para>
///
/// <para>The clamps are WPF's, in the setters, exactly as WPF clamps them — so a
/// hand-edited or out-of-range file is corrected on load and rewritten corrected, which is
/// what WPF does when its own setter runs.</para>
/// </summary>
public sealed class SessionPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_preset.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    private int _flashesPerHour = DefaultFlashesPerHour;
    private int _imagesPerFlash = DefaultImagesPerFlash;

    /// <summary>WPF default (<c>CCP.Core/Models/AppSettings.cs:763</c>).</summary>
    public const int DefaultFlashesPerHour = 10;

    /// <summary>WPF default (<c>CCP.Core/Models/AppSettings.cs:831</c>).</summary>
    public const int DefaultImagesPerFlash = 5;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 180)</c> (<c>AppSettings.cs:769</c>).</summary>
    public const int MinFlashesPerHour = 1;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 180)</c> (<c>AppSettings.cs:769</c>).</summary>
    public const int MaxFlashesPerHour = 180;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 20)</c> (<c>AppSettings.cs:835</c>).</summary>
    public const int MinImagesPerFlash = 1;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 20)</c> (<c>AppSettings.cs:835</c>).</summary>
    public const int MaxImagesPerFlash = 20;

    /// <summary>
    /// The Flash Images module's own on/off dial — WPF <c>AppSettings.FlashEnabled</c>,
    /// <b>default true</b> (<c>AppSettings.cs:751-756</c>). This is the flag WPF's rack row
    /// dot reads (<c>Views/Tabs/StudioTabView.xaml.cs:484-485</c>) and the flag its
    /// right-click quick-toggle flips (<c>MainWindow/MainWindow.Presets.cs:1250</c>).
    /// </summary>
    public bool FlashEnabled { get; set; } = true;

    /// <summary>
    /// Flashes per HOUR. WPF's member is <c>FlashFrequency</c>; the port spells out the unit
    /// because WPF's own comment has to ("Flashes per hour (1-180)", <c>AppSettings.cs:763</c>)
    /// and because the scheduler's whole formula is <c>3600.0 / this</c>
    /// (<c>Services/Flash/FlashService.cs:549-550</c>). Clamped on every write, WPF's clamp.
    /// </summary>
    public int FlashesPerHour
    {
        get => _flashesPerHour;
        set => _flashesPerHour = Math.Clamp(value, MinFlashesPerHour, MaxFlashesPerHour);
    }

    /// <summary>
    /// Images drawn per flash. WPF's member is <c>SimultaneousImages</c>
    /// (<c>AppSettings.cs:831-836</c>); "simultaneous" describes windows on screen, which the
    /// port has none of, so it is named for what it still means here: the size of the draw.
    /// Clamped on every write, WPF's clamp.
    /// </summary>
    public int ImagesPerFlash
    {
        get => _imagesPerFlash;
        set => _imagesPerFlash = Math.Clamp(value, MinImagesPerFlash, MaxImagesPerFlash);
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
