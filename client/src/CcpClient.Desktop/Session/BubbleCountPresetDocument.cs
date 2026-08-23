using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The Bubble Count module's persisted dials — the port's counterpart of WPF's
/// <c>BubbleCountEnabled</c> / <c>BubbleCountFrequency</c> / <c>BubbleCountDifficulty</c>.
///
/// <para><b>One document per module</b>, on the per-module precedent every module since the first has
/// applied (D71/D80): the store's Degraded load path takes the WHOLE document to defaults, so one
/// hand-broken value in a shared file would reset every other module's dials.</para>
///
/// <para><b>WPF's fourth dial, <c>BubbleCountStrictLock</c>, is ABSENT rather than
/// present-and-inert</b>, and that is a decision rather than an omission. Upstream's strict lock
/// does exactly two things: it removes Escape from the game and the result window
/// (<c>Windows/BubbleCountWindow.xaml.cs:1413-1419</c>,
/// <c>Windows/BubbleCountResultWindow.xaml.cs:171-178</c>), and it turns a wrong final answer into
/// the WRONG! WATCH AGAIN retry/mercy loop (<c>Services/BubbleCountService.cs:305-336</c>). This
/// port has NEITHER: the retry loop replays whole clips through a subsystem that is not ported, and
/// Escape is always live here because upstream's own fall-open rule evaluates that way in a build
/// with no panic-key hook (D112, <c>LockCardWindow.xaml.cs:632</c>). A switch that moved nothing is
/// the dead dial §9 D7 refuses, so it is not on the panel and not in this file.</para>
///
/// <para><b>Also not here:</b> XP scaling and the level-50 unlock. An XP ledger DOES exist now
/// (<c>Features/Progression/ProgressionLedger</c>) but this module has no completion payout to hand
/// it, and the level-50 requirement gates nobody in either product
/// (<c>Features/Progression/LevelUnlocks</c>). Also absent: the
/// content-pack video source and its decryption (<c>BubbleCountService.cs:551-575</c>), and the
/// mercy phrase pool (<c>BubbleCountResultWindow.xaml.cs:311-315</c>) — each a subsystem rather
/// than a dial.</para>
/// </summary>
public sealed class BubbleCountPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_bubblecount.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    private int _perHour = BubbleCountSchedule.DefaultPerHour;
    private BubbleCountDifficulty _difficulty = BubbleCountDifficulty.Medium;

    /// <summary>
    /// The module's own on/off dial — WPF's <c>BubbleCountEnabled</c>, the flag its rack row binds
    /// its dot to (<c>Views/Tabs/StudioTabView.xaml.cs:502-503</c>), the one its scheduler re-reads
    /// on every tick (<c>Services/BubbleCountService.cs:85</c>) and the one a fired-but-disabled
    /// trigger re-reads before it opens anything (<c>:141</c>, the #872 fix). Ships OFF.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How many games an hour — WPF's <c>BubbleCountFrequency</c>, clamped 1..10 at the point of use
    /// (<c>Services/BubbleCountService.cs:88</c>, <c>Math.Max(1, Math.Min(10, …))</c>).
    /// </summary>
    public int PerHour
    {
        get => _perHour;
        set => _perHour = Math.Clamp(value, BubbleCountSchedule.MinPerHour, BubbleCountSchedule.MaxPerHour);
    }

    /// <summary>
    /// How many bubbles a clip carries — WPF's <c>BubbleCountDifficulty</c>, cast straight to its own
    /// three-value enum (<c>Services/BubbleCountService.cs:243</c>,
    /// <c>(Difficulty)settings.BubbleCountDifficulty</c>). A value outside the three falls to
    /// <see cref="BubbleCountDifficulty.Medium"/>, which is upstream's own <c>_ =&gt; 5</c> default
    /// arm (<c>Windows/BubbleCountWindow.xaml.cs:1144</c>) rather than a clamp this port invented.
    /// </summary>
    public BubbleCountDifficulty Difficulty
    {
        get => _difficulty;
        set => _difficulty = Enum.IsDefined(value) ? value : BubbleCountDifficulty.Medium;
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
