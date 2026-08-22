using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Progression;

/// <summary>
/// The persisted graded-run award record (`graded_run_awards.json`).
///
/// <para>Upstream's equivalent state lives on <c>AchievementProgress</c>
/// (<c>CCP.Core/Models/AchievementProgress.cs:13</c> for the unlocked set, <c>:169</c> for the
/// perfected categories) and is written to
/// <c>%APPDATA%/ConditioningControlPanel/achievements.json</c>
/// (<c>Services/Progression/AchievementService.cs:71-74</c>).</para>
///
/// <para><b>THE ONE THING THIS DOCUMENT EXISTS TO GET RIGHT.</b> Upstream declares the distinct
/// set as <c>public HashSet&lt;string&gt; PerfectedQuizCategories { get; set; } = new();</c>
/// (<c>AchievementProgress.cs:169</c>) — a DEFAULT comparer, i.e.
/// <c>EqualityComparer&lt;string&gt;.Default</c>: ordinal, case-sensitive, whitespace-sensitive.
/// That is the defect this port refuses to import (see <see cref="GradedRunAwards"/>). Both sets
/// here therefore RE-WRAP whatever they are handed with an explicitly named comparer, so the
/// comparer survives JSON binding no matter which strategy <c>System.Text.Json</c> picks for a
/// collection property — and a file that already holds <c>"Sissy"</c> and <c>"sissy"</c> collapses
/// to one entry on load instead of counting twice forever.</para>
/// </summary>
public sealed class GradedRunAwardsDocument
{
    /// <summary>The schema version this build writes.</summary>
    public const int CurrentSchemaVersion = 1;

    private HashSet<string> _perfectedCategories = new(GradedRunAwards.CategoryComparer);
    private HashSet<string> _awardedIds = new(GradedRunAwards.AwardIdComparer);

    /// <summary>
    /// The DISTINCT categories a top-marks run has cleared — upstream's
    /// <c>PerfectedQuizCategories</c> (<c>AchievementProgress.cs:169</c>), but compared with
    /// <see cref="GradedRunAwards.CategoryComparer"/> rather than the default.
    /// </summary>
    public HashSet<string> PerfectedCategories
    {
        get => _perfectedCategories;
        set => _perfectedCategories = Rewrap(value, GradedRunAwards.CategoryComparer);
    }

    /// <summary>
    /// The award ids already granted — upstream's <c>UnlockedAchievements</c>
    /// (<c>AchievementProgress.cs:13</c>). ORDINAL on purpose: these are code literals, never
    /// user or page data, so folding case here would hide a typo rather than a producer
    /// disagreement. Ids that this build does not know are PRESERVED rather than dropped, which
    /// is upstream's behaviour too — <c>AchievementProgress.IsUnlocked</c> (<c>:212</c>) is a
    /// plain <c>Contains</c> over whatever was persisted.
    /// </summary>
    public HashSet<string> AwardedIds
    {
        get => _awardedIds;
        set => _awardedIds = Rewrap(value, GradedRunAwards.AwardIdComparer);
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    private static HashSet<string> Rewrap(IEnumerable<string>? incoming, IEqualityComparer<string> comparer) =>
        incoming is null ? new HashSet<string>(comparer) : new HashSet<string>(incoming, comparer);
}

/// <summary>
/// What one graded run did. <paramref name="Category"/> is the NORMALISED category the consumer
/// saw (empty when the run carried none), reported even for a run that awarded nothing so the
/// diagnostic line describes what was actually considered.
/// </summary>
public sealed record GradedRunAwardOutcome(
    IReadOnlyList<string> Awarded,
    string Category,
    bool CategoryWasNew,
    int DistinctCategories);

/// <summary>
/// The graded-run award consumer — upstream's <c>GamificationBridge.OnQuizCompleted</c>
/// perfect branch (<c>Services/GamificationBridge.cs:598-609</c>), ported as the OUTCOME rather
/// than the mechanism.
///
/// <para><b>WHAT IS PORTED.</b> <c>top_of_the_class</c> at the 90% bar and <c>honor_roll</c> over
/// <see cref="HonorRollCategories"/> DISTINCT categories — census §6.1's buildable unit, named as
/// exactly those two. The 90% arithmetic itself is NOT here: it stays where upstream keeps it, on
/// the producer (<c>Services/Quiz/IntakeHostService.cs:55</c> and <c>:434</c>, ported at
/// <c>Features/Intake/IntakeQuizRun.cs</c>), and this consumer receives the verdict as a flag
/// exactly as <c>QuizCompletedEventArgs.Perfect</c> does.</para>
///
/// <para><b>WHAT IS DELIBERATELY NOT PORTED.</b> The passed branch
/// (<c>GamificationBridge.cs:584-596</c>) in full: <c>teachers_pet</c> at 25 passes (<c>:41</c>,
/// <c>:588-589</c>) and <c>held_back</c> at a 3-run fail streak (<c>:42</c>, <c>:592-595</c>),
/// together with the two counters they read (<c>:586-587</c>, <c>:592</c>). <c>held_back</c> is
/// unreachable here by construction — the port's only producer emits the literal
/// <c>passed: true</c> (<c>IntakeHostService.cs:433</c>) because <i>"an intake has no fail state
/// to be held back by"</i> (<c>IntakeHostService.cs:418-420</c>, and again at
/// <c>GamificationBridge.cs:574-575</c>) — so a fail-streak counter would be an integer nothing
/// could ever increment. Both are named residue in <c>client/docs/trainer-card-census.md</c> §6.2.</para>
///
/// <para><b>THE DEFECT THIS PORT REFUSES TO IMPORT (divergence D226).</b> Upstream's handler is
/// <i>"source-agnostic on purpose"</i> (<c>GamificationBridge.cs:566-567</c>) and adds the
/// category RAW — <c>p.PerfectedQuizCategories.Add(e.Category)</c> (<c>:602</c>) — into a default
/// <c>HashSet&lt;string&gt;</c> (<c>AchievementProgress.cs:169</c>), while its two producers
/// disagree about normalisation: the intake normalises
/// (<c>IntakeHostService.cs:427-429</c>) and the classic quiz does not —
/// <c>var categoryId = catDef?.Id ?? result.Category.ToString();</c>
/// (<c>Windows/QuizWindow.xaml.cs:540</c>), whose fallback arm is the PascalCase enum
/// (<c>CCP.Core/Models/Quiz/QuizCategory.cs:6-13</c>). So <c>"sissy"</c> and <c>"Sissy"</c> fill
/// two of three slots and <c>honor_roll</c> fires a category EARLY — permanently, because the set
/// is persisted. Latent rather than reproduced: the classic launcher is
/// <c>Visibility="Collapsed"</c> (<c>Views/Tabs/GradedIntakeTabView.xaml:140</c>).</para>
///
/// <para><b>SO THE PORT NORMALISES AT THE CONSUMER, IN TWO LAYERS THAT CATCH DIFFERENT EDITS.</b>
/// (1) <see cref="NormalizeCategory"/> runs on every category from every caller, which catches a
/// future producer shaped like <c>QuizWindow</c>. (2) <see cref="CategoryComparer"/> is the set's
/// comparer, which catches a value that never passed the entry point at all — a hand-edited
/// <c>graded_run_awards.json</c>, or a file written by another build. Producer-side normalisation
/// is safe only while exactly ONE producer is reachable, and that is precisely the property
/// upstream's own comment says the handler does not want to depend on.</para>
///
/// <para><b>NO ENTITLEMENT GATE (divergence D228).</b> All four graded-run achievements are
/// <c>IsExclusive = true</c> (<c>CCP.Core/Models/Achievement.cs:670,680,690,700</c>) and upstream
/// awards them through <c>TryUnlockExclusive</c>, which refuses unless
/// <c>App.Patreon?.HasPremiumAccess == true</c> (<c>AchievementService.cs:1107</c>, gate at
/// <c>:1116-1120</c>). This port has no entitlement AUTHORITY at all, and
/// <c>Entitlement/EntitlementOutcome.cs:7-17</c> forbids reading that absence as a refusal:
/// <i>"you are not a patron" and "I could not tell" … must never collapse into each other</i>.
/// Gating on the absent authority would both break that rule and make this whole path
/// unreachable in every build of this port — the dead-letter outcome upstream's own #870 comments
/// exist to complain about. Revisit the day a real entitlement authority lands, and again the day
/// any surface RENDERS these awards.</para>
/// </summary>
public sealed class GradedRunAwards
{
    /// <summary>Awarded once for a run at or above the top-marks bar (<c>GamificationBridge.cs:600</c>).</summary>
    public const string TopOfTheClassId = "top_of_the_class";

    /// <summary>Awarded at <see cref="HonorRollCategories"/> distinct cleared categories (<c>:605</c>).</summary>
    public const string HonorRollId = "honor_roll";

    /// <summary>
    /// <c>HonorRollCategories = 3</c> — <c>GamificationBridge.cs:40</c>, <i>"top marks in 3
    /// different categories"</i>, restated at <c>:572-573</c> and in the achievement's own
    /// requirement text (<c>Achievement.cs:686</c>).
    /// </summary>
    public const int HonorRollCategories = 3;

    /// <summary>
    /// THE NAMED COMPARER. Upstream's set is <c>new()</c> — ordinal (<c>AchievementProgress.cs:169</c>).
    /// This is the deliberate divergence, and it is load-bearing: flip it to
    /// <see cref="StringComparer.Ordinal"/> and a category that reaches the set without passing
    /// <see cref="NormalizeCategory"/> counts twice, which is exactly how upstream's
    /// <c>honor_roll</c> fires early.
    /// </summary>
    public static readonly StringComparer CategoryComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Award ids are code literals, so they compare ordinal (upstream's set is ordinal too).</summary>
    public static readonly StringComparer AwardIdComparer = StringComparer.Ordinal;

    /// <summary>
    /// The CLOSED set this build can award. Two, not four: <c>teachers_pet</c> and
    /// <c>held_back</c> need the passed branch and its two persisted counters, which are named
    /// residue (census §6.2). Widening this list without building the state behind it would ship
    /// an award nothing can earn.
    /// </summary>
    public static readonly IReadOnlyList<string> AwardableIds = [TopOfTheClassId, HonorRollId];

    private readonly PersistenceStore<GradedRunAwardsDocument> _store;
    private readonly Action<string> _log;

    public GradedRunAwards(PersistenceStore<GradedRunAwardsDocument> store, Action<string> log)
    {
        _store = store;
        _log = log;
    }

    private GradedRunAwardsDocument State => _store.Current;

    /// <summary>
    /// Upstream's <c>AchievementProgress.IsUnlocked</c> (<c>:212</c>): a plain membership test
    /// over whatever is persisted, including ids this build does not know.
    /// </summary>
    public bool IsAwarded(string id) => State.AwardedIds.Contains(id);

    /// <summary>The distinct cleared categories — the count <c>GamificationBridge.cs:603</c> compares.</summary>
    public int DistinctPerfectedCategories => State.PerfectedCategories.Count;

    /// <summary>
    /// The consumer-side normalisation: the SAME call upstream's intake producer makes
    /// (<c>IntakeHostService.cs:427-429</c>, <c>run.Niche.Trim().ToLowerInvariant()</c>), moved to
    /// the point every producer must pass through.
    ///
    /// <para>Whitespace becomes EMPTY here rather than a fallback niche. That is deliberate and it
    /// differs from upstream by one case (divergence D227): <c>:602</c> tests the raw category
    /// with <c>IsNullOrEmpty</c>, so a whitespace-only category would enter upstream's set as a
    /// whitespace entry. Through the port's own producer this can never fire —
    /// <c>IntakeGraded.Category</c> already substitutes <c>IntakeNiche.Fallback</c> for whitespace
    /// — so it is a property of the consumer's contract for FUTURE producers, not a live
    /// behavioural difference on the shipped path.</para>
    /// </summary>
    public static string NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? string.Empty : category.Trim().ToLowerInvariant();

    /// <summary>
    /// One graded run, source-agnostic: a top-marks verdict and a category, with no interest in
    /// which surface produced them (<c>GamificationBridge.cs:566-567</c>).
    ///
    /// <para>Ported clause for clause from <c>:598-609</c>, and the CLAUSE ORDER IS BEHAVIOUR
    /// because <c>&amp;&amp;</c> short-circuits:</para>
    /// <list type="number">
    /// <item><c>:598</c> — everything is inside the perfect branch. A run below the bar records
    /// nothing at all here.</item>
    /// <item><c>:600</c> — <c>top_of_the_class</c> fires FIRST and UNCONDITIONALLY, before the
    /// category is looked at, so a run with no category still earns it.</item>
    /// <item><c>:602</c> clause 1 — an empty category is skipped.</item>
    /// <item><c>:602</c> clause 2 — <c>Add</c> runs next, and <c>honor_roll</c> is attempted ONLY
    /// on the run that GROWS the set. A perfect run in an already-cleared category short-circuits
    /// here and never reaches the count, even when the count is already at the threshold.</item>
    /// <item><c>:603</c> clause 3 — <c>&gt;=</c>, so a fourth distinct category satisfies it too.</item>
    /// <item><c>:609</c> — upstream's <c>MarkDirty()</c> flags a 30 s save timer. This store has
    /// no timer, so the OUTCOME of "mark dirty" is "the change reaches disk": a run that changed
    /// something saves immediately (upstream's <c>TryUnlock</c> does the same at
    /// <c>AchievementService.cs:872</c>), and a run that changed nothing writes nothing. Upstream
    /// never has that second case, because its passed branch always at least moves a counter.</item>
    /// </list>
    /// </summary>
    public GradedRunAwardOutcome RecordGradedRun(bool topMarks, string? category)
    {
        var normalized = NormalizeCategory(category);

        if (!topMarks)
        {
            return new GradedRunAwardOutcome([], normalized, false, DistinctPerfectedCategories);
        }

        var awarded = new List<string>();

        if (TryAward(TopOfTheClassId))
        {
            awarded.Add(TopOfTheClassId);
        }

        var categoryWasNew = false;
        if (normalized.Length > 0)
        {
            categoryWasNew = AddCategory(normalized);
            if (categoryWasNew && DistinctPerfectedCategories >= HonorRollCategories && TryAward(HonorRollId))
            {
                awarded.Add(HonorRollId);
            }
        }

        if (awarded.Count > 0 || categoryWasNew)
        {
            _ = _store.Save();
        }

        return new GradedRunAwardOutcome(awarded, normalized, categoryWasNew, DistinctPerfectedCategories);
    }

    /// <summary>
    /// Membership is tested BEFORE the mutation so a repeat run leaves the store clean — the
    /// same order as upstream's <c>TryUnlockExclusive</c>, whose first statement is
    /// <c>if (_progress.IsUnlocked(achievementId)) return false;</c>
    /// (<c>AchievementService.cs:1115</c>).
    /// </summary>
    private bool TryAward(string id)
    {
        if (IsAwarded(id))
        {
            return false;
        }

        _store.Mutate(d => d.AwardedIds.Add(id));
        _log($"graded-run awards: {id} awarded (GamificationBridge.cs:598-605)");
        return true;
    }

    /// <summary>
    /// <c>PerfectedQuizCategories.Add</c> (<c>:602</c>), with the membership test taken through
    /// THE SAME SET, so <see cref="CategoryComparer"/> decides it and a pre-existing
    /// differently-cased entry is found rather than duplicated.
    /// </summary>
    private bool AddCategory(string normalized)
    {
        if (State.PerfectedCategories.Contains(normalized))
        {
            return false;
        }

        _store.Mutate(d => d.PerfectedCategories.Add(normalized));
        return true;
    }
}
