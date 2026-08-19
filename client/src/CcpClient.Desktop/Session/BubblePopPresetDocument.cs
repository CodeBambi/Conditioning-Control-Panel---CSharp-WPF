using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Session;

/// <summary>
/// SP-113: the Bubble Pop module's persisted dials — the port's counterpart of WPF's
/// <c>BubblesEnabled</c> / <c>BubblesFrequency</c> / <c>BubblesSize</c> / <c>BubbleSpeedBoost</c>
/// (<c>Models/AppSettings.cs</c> <c>#region Bubbles</c> at <c>:2202</c>).
///
/// <para><b>One document per module</b>, on the precedent SP-101 set and every module since has
/// applied (D71/D80): the store's Degraded load path takes the WHOLE document to defaults, so one
/// hand-broken value in a shared file would reset every other module's dials. The clamps are
/// upstream's, in the setters, exactly where upstream puts them.</para>
///
/// <para><b>What is NOT here, and why each is absent rather than present-and-inert (D93's rule).</b>
/// <c>BubbleSharedHost</c> ("Solid mode", <c>:2224</c>) selects between upstream's shared-host and
/// per-window render paths; this port has only the per-window path, so a switch would move nothing.
/// <c>BubblesVolume</c> (<c>:2230</c>) drives a pop sound this port does not play.
/// <c>BubblesClickable</c> (<c>:2242</c>) turns bubbles into scenery during a session — a target
/// this capability would refuse to place non-routably rather than place click-through, and the
/// honest port of "unclickable ambient decoration" is not a pointer target at all.
/// <c>BubblesLinkRamp</c> (<c>:2236</c>) links the spawn rate to the session ramp, which is
/// <c>IntensityRampEffect</c>'s wiring and a row of its own. <c>BubbleTriggersEnabled</c> and its
/// four companions (<c>:2250-2280</c>) are the Trigger Bubbles feature — a pop that fires a flash,
/// a spiral or a video — which reaches four other subsystems and is a packet, not a checkbox.
/// <c>DualMonitorEnabled</c> is the port's standing single-display limit (D66).</para>
/// </summary>
public sealed class BubblePopPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_bubblepop.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    private int _perMinute = BubblePopField.DefaultPerMinute;
    private int _sizePercent = BubblePopField.DefaultSizePercent;
    private int _speedBoostPercent = BubblePopField.MinSpeedBoostPercent;

    /// <summary>
    /// The module's own on/off dial — WPF's <c>BubblesEnabled</c> (<c>Models/AppSettings.cs:2204</c>),
    /// which ships OFF and is level-gated upstream at Lv.20. The port has no progression system, so
    /// the level gate is absent rather than a check that always passes.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Spawns a minute — WPF's <c>BubblesFrequency</c>, clamped 1..60
    /// (<c>Models/AppSettings.cs:2210</c>), fed straight to <c>60000.0 / frequency</c>
    /// (<c>Services/BubbleService.cs:189</c>).
    /// </summary>
    public int PerMinute
    {
        get => _perMinute;
        set => _perMinute = Math.Clamp(value, BubblePopField.MinPerMinute, BubblePopField.MaxPerMinute);
    }

    /// <summary>
    /// The user's bubble-size dial as a percentage — WPF's <c>BubblesSize</c>, clamped 50..150
    /// (<c>Services/BubbleSizing.cs:52,59</c>). The absolute pixel rails live in
    /// <see cref="BubblePopField.SizeFor"/>, exactly as upstream keeps them in
    /// <c>BubbleSizing.Scale</c> rather than in the setting.
    /// </summary>
    public int SizePercent
    {
        get => _sizePercent;
        set => _sizePercent = Math.Clamp(value, BubblePopField.MinSizePercent, BubblePopField.MaxSizePercent);
    }

    /// <summary>
    /// Extra travel speed as a percentage — WPF's <c>BubbleSpeedBoost</c>, clamped 0..500 and applied
    /// as <c>speed × (1 + boost/100)</c> (<c>Models/AppSettings.cs:2262</c>,
    /// <c>Services/BubbleService.cs:2831-2836</c>).
    /// </summary>
    public int SpeedBoostPercent
    {
        get => _speedBoostPercent;
        set => _speedBoostPercent = Math.Clamp(
            value, BubblePopField.MinSpeedBoostPercent, BubblePopField.MaxSpeedBoostPercent);
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
