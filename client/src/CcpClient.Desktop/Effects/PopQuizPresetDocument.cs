using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// The Pop Quiz module's persisted dials — the port's counterpart of WPF's <c>PopQuizEnabled</c> and
/// <c>PopQuizFrequency</c> (<c>Models/AppSettings.cs:3573-3589</c>), the pair its panel binds
/// (<c>Views/Tabs/GradedIntakeTabView.xaml:268-292</c>) and the pair <c>StartEngine</c> reads
/// (<c>MainWindow/MainWindow.StartStop.cs:255</c>).
///
/// <para><b>Two dials, because upstream has two.</b> This module's whole settings region is those
/// two properties and nothing else; there is no strict mode, no colour, no repeat count and no
/// phrase list to carry.</para>
///
/// <para><b>The question pool is NOT in here, and that is upstream's shape rather than a gap.</b>
/// The twenty-five questions are a <c>static readonly</c> array in the service
/// (<c>Services/Quiz/PopQuizService.cs:23-100</c>) with no settings key, no editor and no mod hook —
/// unlike the Lock Card's phrases, which upstream really does persist and edit
/// (<c>AppSettings.cs:3379-3383</c>, <c>Features/LockCardFeatureControl.xaml.cs:203-210</c>). Making
/// them editable here would be a feature the shipping product does not have. See
/// <see cref="PopQuizQuestions"/>.</para>
///
/// <para><b>Also not here, and each one is refused rather than stubbed:</b> the per-SESSION window
/// (<c>Models/Session.cs:913-916</c> carries <c>PopQuizEnabled</c>/<c>StartMinute</c>/<c>EndMinute</c>/
/// <c>Frequency</c>) — dead in the shipping engine, which reads the user-level toggle instead and
/// says so at <c>Services/Session/SessionEngine.cs:1406</c>, <i>"Pop quiz is a user-level toggle
/// (AppSettings), not per-session"</i>, and which the program presets do not touch either
/// (<c>Services/Program/BuiltInPrograms.cs:430</c>: <i>"PopQuiz*, MiniGameEnabled and BrainDrain*
/// are dead in the engine and are not touched"</i>). And the <c>audioOnly</c> gate at
/// <c>MainWindow.StartStop.cs:255</c>, which belongs to a session mode this port does not have.</para>
///
/// <para><b>Where this file sits.</b> Every sibling preset document lives under
/// <c>Session/</c>; this one sits beside its own module because <c>Session/</c> is outside this
/// packet's file scope. Nothing about the store cares — <c>PersistenceStore&lt;T&gt;</c> is generic
/// over the model — and the port already keeps a persisted document with its feature rather than
/// with the others (<c>Features/Progression/ProgressionLedger.cs</c>'s
/// <c>ProgressionDocument</c>).</para>
/// </summary>
public sealed class PopQuizPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_popquiz.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    private int _perHour = PopQuizSchedule.DefaultPerHour;

    /// <summary>
    /// The module's own on/off dial — WPF's <c>PopQuizEnabled</c>, which its checkbox writes
    /// (<c>MainWindow/MainWindow.Lab.cs:632-636</c>), which <c>StartEngine</c> gates on
    /// (<c>MainWindow/MainWindow.StartStop.cs:255</c>) and which the service re-reads inside every
    /// tick before it shows anything (<c>Services/Quiz/PopQuizService.cs:173</c>). Ships OFF
    /// (<c>Models/AppSettings.cs:3575</c>, <c>_popQuizEnabled = false</c>).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Questions per hour — WPF's <c>PopQuizFrequency</c>, clamped
    /// <c>Math.Clamp(value, 1, 100)</c> (<c>Models/AppSettings.cs:3586</c>). See
    /// <see cref="PopQuizSchedule.MaxPerHour"/> for why the ceiling is a hundred and not the ten the
    /// stale comment beside it claims.
    /// </summary>
    public int PerHour
    {
        get => _perHour;
        set => _perHour = Math.Clamp(value, PopQuizSchedule.MinPerHour, PopQuizSchedule.MaxPerHour);
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted
    /// model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
