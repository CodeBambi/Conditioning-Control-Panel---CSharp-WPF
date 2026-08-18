using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Session;

/// <summary>
/// SP-106: the Spiral Overlay module's persisted dials — the port's counterpart of the
/// <c>#region Spiral Overlay</c> block WPF reads out of <c>App.Settings.Current</c>
/// (<c>CCP.Core/Models/AppSettings.cs:2642-2682</c>).
///
/// <para><b>One document per module</b>, on the precedent SP-101 set and SP-105 applied a second
/// time (<see cref="SubliminalPresetDocument"/>, <see cref="PinkFilterPresetDocument"/>,
/// divergences D71/D80): <c>Persistence/**</c> is outside this packet's File Scope too, and the
/// substantive half of the argument is unchanged — the store's Degraded load path takes the WHOLE
/// document to defaults, so one hand-broken value in a shared file would reset every other module's
/// dials.</para>
///
/// <para>The clamps are WPF's, in the setters, exactly as WPF clamps them.</para>
/// </summary>
public sealed class SpiralPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_spiral.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF default (<c>AppSettings.cs:2670</c>): 10 %.</summary>
    public const int DefaultOpacityPercent = 10;

    /// <summary>WPF clamp <c>Math.Clamp(value, 0, 100)</c> (<c>AppSettings.cs:2674</c>). <b>Zero is
    /// inside the range</b> and the module has to answer for it, exactly as the tint does
    /// (<see cref="Effects.SpiralPresentation.IsInvisible"/>).</summary>
    public const int MinOpacityPercent = 0;

    /// <summary>WPF clamp <c>Math.Clamp(value, 0, 100)</c> (<c>AppSettings.cs:2674</c>). The ceiling
    /// is 100 here and 50 for the pink tint, because the spiral's own ×0.1 reduction
    /// (<c>OverlayService.cs:1692-1693</c>) already keeps it subtle.</summary>
    public const int MaxOpacityPercent = 100;

    private int _opacityPercent = DefaultOpacityPercent;
    private string _path = string.Empty;

    /// <summary>
    /// The module's own on/off dial — WPF <c>AppSettings.SpiralEnabled</c>, <b>default true</b>
    /// (<c>AppSettings.cs:2644</c>).
    ///
    /// <para><b>It is the only one of the four ported modules that ships ON</b>, and that is
    /// upstream's, not a choice made here. It costs nothing on a fresh install because the module
    /// also needs a spiral to draw (<see cref="Effects.SpiralLibrary"/>) and a fresh install has
    /// none — which is WPF's own second condition, <c>settings.SpiralEnabled &amp;&amp;
    /// !string.IsNullOrEmpty(spiralPath)</c> (<c>OverlayService.cs:377</c>).</para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// WPF's <c>SpiralOpacity</c> (<c>AppSettings.cs:2670-2675</c>): the dial in percent, default
    /// 10, clamped <c>[0, 100]</c> on every write. What reaches the surface is this ×0.1 — see
    /// <see cref="Effects.SpiralPresentation.Opacity"/>, and WPF's own words at the line that
    /// computes it ("Very subtle opacity - 90% reduction", <c>OverlayService.cs:1692-1693</c>).
    /// </summary>
    public int OpacityPercent
    {
        get => _opacityPercent;
        set => _opacityPercent = Math.Clamp(value, MinOpacityPercent, MaxOpacityPercent);
    }

    /// <summary>
    /// WPF's <c>SpiralPath</c> (<c>AppSettings.cs:2652-2657</c>): the user's chosen spiral file,
    /// <b>empty by default</b>, where empty means "look in the library". Held as the raw string
    /// because that is what upstream persists, and because the file may since have been deleted —
    /// WPF re-tests <c>File.Exists</c> on every resolve (<c>OverlayService.cs:302-304</c>) and so
    /// does <see cref="Effects.SpiralLibrary.Resolve"/>. Never null, WPF's own <c>value ?? ""</c>.
    /// </summary>
    public string Path
    {
        get => _path;
        set => _path = value ?? string.Empty;
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
