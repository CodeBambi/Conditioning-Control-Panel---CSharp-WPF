using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Session;

/// <summary>
/// SP-117: the <b>Visuals</b> row's persisted dials — WPF's own
/// <c>Features/VisualsFeatureControl.xaml.cs:35-118</c>, which is a settings page and nothing else.
///
/// <para><b>This document has no <c>Enabled</c> member, and that is the whole census finding made
/// structural.</b> Every other module's document opens with its own on/off dial. Visuals has none,
/// because upstream has none: its rack entry passes <c>null</c> where every other row passes a dot
/// predicate (<c>Views/Tabs/StudioTabView.xaml.cs:496</c>), and the shipping panel says so in its
/// own markup — "no enable pill: Visuals is the one module with no master toggle at all"
/// (<c>Features/VisualsFeatureControl.xaml:12-14</c>). There is no <c>VisualsService</c> anywhere in
/// the shipping tree. <b>Visuals is not a module; it is the Flash Images module's DRAW dials.</b>
/// So this document holds no schedule, no pool and no enable — only the three numbers that decide
/// how a flash looks once <see cref="Effects.FlashImagesEffect"/> has decided that one happens.</para>
///
/// <para><b>Why three and not five.</b> Upstream's page carries five controls. Two of them cannot be
/// ported honestly and are ABSENT rather than present-and-inert (D93):</para>
/// <list type="bullet">
/// <item><b>Fade</b> (<c>AppSettings.FadeDuration</c>, <c>CCP.Core/Models/AppSettings.cs:892-897</c>)
/// is a <b>DEAD DIAL IN THE SHIPPING PRODUCT</b>. Every reference to it in the whole repository was
/// enumerated: the Visuals page writes it (<c>VisualsFeatureControl.xaml.cs:46,47,59,96</c>), the
/// model clamps it, and <c>CCP.Core/Models/Preset.cs</c> round-trips it (<c>:68,178,202,226,253,292,338,456</c>).
/// <b>Nothing reads it to draw anything.</b> The fade a user actually sees is a hard-coded constant —
/// <c>Services/Flash/FlashService.cs:2018</c>, <c>private const double FADE_PER_SEC = 2.4</c>, spent
/// at <c>:2073</c> and applied at <c>:2110-2117</c>. Porting the slider would ship a control that
/// moves nothing, which is exactly what §9 D7 refuses.</item>
/// <item><b>Link to audio</b> (<c>AppSettings.FlashAudioEnabled</c>, <c>:899-904</c>) means "when a
/// flash has a sound, play it and let the SOUND's length replace the duration"
/// (<c>FlashService.cs:1037-1042</c>). This port's flash makes no sound at all —
/// <see cref="Effects.FlashImagesEffect.Deliver"/> hands paths to a surface and raises an event,
/// and nothing in the module reaches <c>Audio/**</c>. A checkbox here would move nothing
/// either.</item>
/// </list>
///
/// <para><b>One document per module</b>, on the precedent SP-101 set and SP-105 restated
/// (<see cref="PinkFilterPresetDocument"/>, divergence D71): <c>Persistence/**</c> is outside this
/// packet's File Scope, and the substantive half of that argument is unchanged — the store's
/// Degraded load path takes the WHOLE document to defaults, so one hand-broken value in a shared
/// file would reset every other module's dials.</para>
///
/// <para>The clamps are WPF's, in the setters, exactly as WPF clamps them — so a hand-edited or
/// out-of-range file is corrected on load and rewritten corrected, which is what WPF does when its
/// own setter runs.</para>
/// </summary>
public sealed class VisualsPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_visuals.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF default (<c>CCP.Core/Models/AppSettings.cs:839</c>): 100 %, "normal size".</summary>
    public const int DefaultImageScalePercent = 100;

    /// <summary>WPF clamp <c>Math.Clamp(value, 50, 250)</c> (<c>AppSettings.cs:849</c>). The slider
    /// on the shipping page carries the same bounds (<c>VisualsFeatureControl.xaml:54</c>).</summary>
    public const int MinImageScalePercent = 50;

    /// <inheritdoc cref="MinImageScalePercent"/>
    public const int MaxImageScalePercent = 250;

    /// <summary>WPF default (<c>AppSettings.cs:853</c>): fully opaque.</summary>
    public const int DefaultFlashOpacityPercent = 100;

    /// <summary>
    /// WPF clamp <c>Math.Clamp(value, 10, 100)</c> (<c>AppSettings.cs:859</c>), same bounds as the
    /// shipping slider (<c>VisualsFeatureControl.xaml:70</c>).
    ///
    /// <para><b>The floor of TEN is load-bearing here in a way it is not upstream.</b> This port's
    /// overlay refuses to construct a request at opacity zero by design — an invisible surface the
    /// OS agrees is present and on top is the exact ghost the capability exists to make impossible
    /// (<c>Overlay/OverlaySurfaceRequest.cs:30-37</c>). WPF's own floor keeps every reachable value
    /// well clear of it, so the two rules agree and neither had to be bent.</para>
    /// </summary>
    public const int MinFlashOpacityPercent = 10;

    /// <inheritdoc cref="MinFlashOpacityPercent"/>
    public const int MaxFlashOpacityPercent = 100;

    /// <summary>WPF default (<c>AppSettings.cs:925</c>): 5 seconds.</summary>
    public const int DefaultFlashDurationSeconds = 5;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 30)</c> (<c>AppSettings.cs:929</c>), same bounds as
    /// the shipping slider (<c>VisualsFeatureControl.xaml:102</c>).</summary>
    public const int MinFlashDurationSeconds = 1;

    /// <inheritdoc cref="MinFlashDurationSeconds"/>
    public const int MaxFlashDurationSeconds = 30;

    private int _imageScalePercent = DefaultImageScalePercent;
    private int _flashOpacityPercent = DefaultFlashOpacityPercent;
    private int _flashDurationSeconds = DefaultFlashDurationSeconds;

    /// <summary>
    /// WPF's <c>ImageScale</c> (<c>AppSettings.cs:846-850</c>): how big a flash is drawn, as a
    /// percentage where 100 is "normal". It multiplies the aspect-preserving fit into a box that is
    /// 40 % of the monitor in each axis — <c>CalculateGeometry</c>
    /// (<c>Services/Flash/FlashService.cs:2290-2315</c>), already ported verbatim as
    /// <see cref="Effects.FlashGeometry.Size"/>, which has taken this value as a parameter since
    /// SP-100 and has been handed a constant ever since.
    /// </summary>
    public int ImageScalePercent
    {
        get => _imageScalePercent;
        set => _imageScalePercent = Math.Clamp(value, MinImageScalePercent, MaxImageScalePercent);
    }

    /// <summary>
    /// WPF's <c>FlashOpacity</c> (<c>AppSettings.cs:856-861</c>): how opaque a flash is drawn. WPF
    /// spends it as <c>maxAlpha = FlashOpacity / 100.0</c> on every heartbeat frame
    /// (<c>FlashService.cs:2072</c>); this port spends it once, at placement, as the layered
    /// window's <c>LWA_ALPHA</c> (<c>Overlay/OverlaySurfaceRequest.cs:61-68</c>). See D174 for what
    /// that costs.
    /// </summary>
    public int FlashOpacityPercent
    {
        get => _flashOpacityPercent;
        set => _flashOpacityPercent = Math.Clamp(value, MinFlashOpacityPercent, MaxFlashOpacityPercent);
    }

    /// <summary>
    /// WPF's <c>FlashDuration</c> (<c>AppSettings.cs:926-930</c>): how long a flash stays on screen,
    /// in seconds, before the one-second grace WPF adds to every window's lifetime
    /// (<c>FlashService.cs:1073</c>, <c>lifetimeMs = (int)(duration * 1000) + 1000</c>).
    ///
    /// <para>Upstream's own comment calls it the duration "when audio is disabled"
    /// (<c>AppSettings.cs:925</c>), because a flash with a sound takes the sound's length instead
    /// (<c>FlashService.cs:1042</c>). This port's flash is silent, so this is the duration
    /// ALWAYS — which is the same code path upstream takes with the audio link off.</para>
    /// </summary>
    public int FlashDurationSeconds
    {
        get => _flashDurationSeconds;
        set => _flashDurationSeconds = Math.Clamp(value, MinFlashDurationSeconds, MaxFlashDurationSeconds);
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
