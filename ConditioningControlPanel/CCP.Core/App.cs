using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Roadmap;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel;

/// <summary>
/// Temporary static application stub for CCP.Core so copied model files can compile.
/// This mirrors the legacy WPF <c>CoreApp</c> service locator enough for Core build only;
/// heads should replace these static references with dependency injection.
/// </summary>
public static class CoreApp
{
    /// <summary>
    /// Global service provider. When set, typed properties resolve from DI first and fall
    /// back to their explicit backing fields so legacy WPF setters keep working.
    /// </summary>
    public static IServiceProvider? Services { get; set; }

    private static IAppSettingsService? _settings;
    public static IAppSettingsService? Settings
    {
        get => Services?.GetService<ISettingsService>()
            ?? Services?.GetService<IAppSettingsService>()
            ?? _settings;
        set => _settings = value;
    }

    private static ILogger? _logger;
    public static ILogger? Logger
    {
        get => Services?.GetService<ILogger<CoreAppLogger>>() ?? _logger;
        set => _logger = value;
    }

    private static ISkillTreeService? _skillTree;
    public static ISkillTreeService? SkillTree
    {
        get => Services?.GetService<ISkillTreeService>() ?? _skillTree;
        set => _skillTree = value;
    }

    private static IModService? _mods;
    public static IModService? Mods
    {
        get => Services?.GetService<IModService>() ?? _mods;
        set => _mods = value;
    }

    private static IProgressionService? _progression;
    public static IProgressionService? Progression
    {
        get => Services?.GetService<IProgressionService>() ?? _progression;
        set => _progression = value;
    }

    private static IRoadmapService? _roadmap;
    public static IRoadmapService? Roadmap
    {
        get => Services?.GetService<IRoadmapService>() ?? _roadmap;
        set => _roadmap = value;
    }

    public static string? TutorialBaseUrl { get; set; }

    /// <summary>Marker type used only to obtain a typed <see cref="ILogger"/> category.</summary>
    private class CoreAppLogger { }

    // Service references used by ported Avalonia mini-game windows; typed as object/dynamic
    // because the concrete implementations currently live in the legacy WPF head.
    public static object? Achievements { get; set; }
    public static object? BubbleCount { get; set; }
    public static object? LockCard { get; set; }
    public static object? InteractionQueue { get; set; }

    // Service references used by ported Avalonia dialogs; typed as object/dynamic
    // because the concrete implementations currently live in the legacy WPF head.
    public static object? AttentionCheck { get; set; }
    public static object? KeywordPresets { get; set; }
    public static object? KeywordTriggers { get; set; }
    public static object? CompanionPhrases { get; set; }
    public static IPromptValidator? PromptValidator { get; set; }
    public static object? ModerationLog { get; set; }

    // Auth, Chaos, avatar, bark, video and main-window references are now resolved
    // through the typed DI abstractions below and registered by each head.

    // Additional service stubs referenced by extracted Core services until they are fully ported.
    public static object? Overlay { get; set; }
    public static object? DeeperHost { get; set; }
    public static object? Quests { get; set; }
    public static object? Haptics { get; set; }

    private static IBubbleService? _bubbles;

    /// <summary>Ported bubble service. Assigned by cross-platform heads after DI is built.</summary>
    public static IBubbleService? Bubbles
    {
        get => Services?.GetService<IBubbleService>() ?? _bubbles;
        set => _bubbles = value;
    }

    private static Core.Services.SessionLog.ISessionLogService? _sessionLog;

    /// <summary>Ported session log service. Assigned by cross-platform heads after DI is built.</summary>
    public static Core.Services.SessionLog.ISessionLogService? SessionLog
    {
        get => Services?.GetService<Core.Services.SessionLog.ISessionLogService>() ?? _sessionLog;
        set => _sessionLog = value;
    }
}

public interface IAppSettingsService
{
    AppSettings Current { get; }
    void Save();
}


public interface ISkillTreeService
{
    bool HasSkill(string skillId);
    double GetTotalXpMultiplier();
    int TotalPointsSpent { get; }
    event EventHandler<string>? SkillUnlocked;
    event EventHandler? PinkRushStarted;
    Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId);

    /// <summary>
    /// Starts background timers/checks (weekly shield reset, time-of-day tracking).
    /// </summary>
    void Start();

    /// <summary>
    /// Stops background timers.
    /// </summary>
    void Stop();

    /// <summary>
    /// Manually trigger a Pink Rush bonus window (for smoke tests / debug). Does nothing if settings unavailable.
    /// </summary>
    void TriggerPinkRush();

    // Legacy stubs still referenced by Core services until those services are fully ported.
    bool UseStreakShield();
    bool UseOopsieInsurance();
    int GetDailyStreakBonus(int consecutiveDays);
    int GetDailyFreeRerolls();
    void AddConditioningTime(double minutes);

    /// <summary>
    /// Prune non-permanent skills at a season reset (keep only SkillDefinition.PermanentIds),
    /// clear seasonal flags, and tear down seasonal effects. Default no-op; real impl overrides.
    /// WPF parity: SkillTreeService.OnSeasonReset.
    /// </summary>
    void OnSeasonReset() { }

    /// <summary>Quest-reward XP multiplier from the "Better Quests" skill. Default 1.0. WPF: SkillTreeService.GetRerollBonusMultiplier.</summary>
    double GetRerollBonusMultiplier() => 1.0;

    /// <summary>Perfect-week bonus XP (7/14/30-day) awarded on a daily completion. Default 0. WPF: SkillTreeService.CheckPerfectWeekBonus.</summary>
    int CheckPerfectWeekBonus() => 0;
}

public interface IModService
{
    string GetModeDisplayName();
    string MakeModAware(string text);
    string GetAccentColorHex();
    string GetAccentLightColorHex();
    string GetAccentDarkColorHex();
    string GetSecondaryColorHex();
    string GetBackgroundColorHex();
    string GetPanelColorHex();
    string GetSurfaceColorHex();
    string GetFilterColorHex();
    string[] GetPhrases(string category);
    string GetPinkRushName();
    string GetPinkRushDescription();

    /// <summary>
    /// All installed mods, including built-ins and discovered user mods.
    /// </summary>
    IReadOnlyList<ModPackage> InstalledMods { get; }

    /// <summary>
    /// The currently active mod package.
    /// </summary>
    ModPackage ActiveMod { get; }

    /// <summary>
    /// Raised after the active mod changes.
    /// </summary>
    event EventHandler<ModPackage>? ActiveModChanged;

    /// <summary>
    /// Returns the active mod's video link catalog (name -> URL), or an empty dictionary if none.
    /// </summary>
    IReadOnlyDictionary<string, string> GetVideoLinks();

    /// <summary>
    /// Loads built-in and user-installed mods and selects the persisted active mod.
    /// </summary>
    void Initialize(string? activeModId);

    /// <summary>
    /// Extracts and installs a .ccpmod package into the user mods folder.
    /// </summary>
    Task<ModInstallResult> InstallModAsync(string ccpmodPath);

    /// <summary>
    /// Removes a user-installed mod. Built-in mods cannot be uninstalled.
    /// </summary>
    bool UninstallMod(string modId);

    /// <summary>
    /// Activates the mod with the given ID, persists the choice, and raises <see cref="ActiveModChanged"/>.
    /// </summary>
    bool ActivateMod(string modId);

    /// <summary>
    /// Exports the current configuration as a .ccpmod file.
    /// </summary>
    Task ExportCurrentAsModAsync(string outputPath, string modName, string author);

    /// <summary>
    /// Returns the attention-check failure message for the active mod.
    /// </summary>
    string GetAttentionCheckFailMessage();

    /// <summary>
    /// Returns the attention-check mercy message for the active mod.
    /// </summary>
    string GetAttentionCheckMercyMessage();

    /// <summary>
    /// Returns the active mod's preferred affirmation term (e.g. "Subject").
    /// </summary>
    string GetAffirmation();

    /// <summary>
    /// Returns the active mod's tube-layout avatar scale multiplier.
    /// </summary>
    double GetAvatarScale();

    /// <summary>
    /// Returns the active mod's horizontal avatar offset in attached mode.
    /// </summary>
    int GetAvatarOffsetX();

    /// <summary>
    /// Returns the active mod's vertical avatar offset in attached mode.
    /// </summary>
    int GetAvatarOffsetY();

    /// <summary>
    /// Returns the active mod's horizontal avatar offset in detached mode.
    /// </summary>
    int GetAvatarDetachedOffsetX();

    /// <summary>
    /// Returns the active mod's vertical avatar offset in detached mode.
    /// </summary>
    int GetAvatarDetachedOffsetY();

    /// <summary>
    /// Returns whether the active mod supports the given legacy avatar set number.
    /// A null <see cref="ModManifest.SupportedAvatarSets"/> means all sets are supported.
    /// </summary>
    bool IsAvatarSetSupported(int setNumber);

    /// <summary>
    /// Returns the active mod's custom avatar sets, if any.
    /// </summary>
    IReadOnlyList<ConditioningControlPanel.Models.CustomAvatarSet> GetCustomAvatarSets();
}

public interface IProgressionService
{
    void AddXP(int amount, XPSource source);
    double GetSessionXPMultiplier(int playerLevel);
    double GetXPForLevel(int level);
    double GetTotalXP(int level, double currentXP);
    double GetCurrentLevelXP(int level, double totalXP);
    event EventHandler<int>? LevelUp;
}

public interface IKeywordTriggerPresetService
{
    bool IsInstalled(string presetId);
    bool InstallPreset(string presetId);
    bool UninstallPreset(string presetId);
    KeywordTriggerPreset? CloneToCustom(string presetId);
    IReadOnlyList<KeywordTriggerPreset> VisiblePresets { get; }
    event EventHandler? PresetsChanged;
}

public interface IKeywordTriggerService
{
    bool IsRunning { get; }
    bool NeedsOcrConfirmation { get; }

    void Start();
    void Stop();

    /// <summary>Process a low-level virtual-key press from the platform input hook.</summary>
    void OnKeyPressed(int vkCode);

    /// <summary>Check free-form text (e.g. clipboard) for keyword matches.</summary>
    void CheckText(string text);

    /// <summary>Process OCR word hits from a screen scan.</summary>
    void CheckOcrWords(List<OcrWordHit> words);

    /// <summary>Fires a synthetic trigger for tutorial/demo purposes.</summary>
    void FireDemoTrigger(string keyword, string source = "Tutorial");

    /// <summary>Imports legacy CustomTriggers entries into keyword triggers.</summary>
    List<KeywordTrigger> ImportFromCustomTriggers();

    void PreviewAudioClip(string filePath, int volume);

    /// <summary>
    /// Temporarily mute a phrase so the keyword/OCR pipeline ignores it for <paramref name="muteMs"/>.
    /// Used as the bark self-echo guard so a spoken bark line cannot trip awareness/OCR off its own
    /// bubble text (WPF KeywordTriggers.MuteKeywordEcho, BarkService.cs:1627). Default no-op so
    /// heads/fakes that have no keyword pipeline keep compiling and degrade safely.
    /// </summary>
    void MuteKeywordEcho(string text, int muteMs) { }

    event EventHandler<KeywordTrigger>? TriggerFired;
}

public interface ICompanionPhraseService
{
    IEnumerable<string> GetCategoryNames();

    /// <summary>
    /// Returns all built-in + custom companion phrases with enable/audio status resolved.
    /// </summary>
    IReadOnlyList<CompanionPhrase> GetAllPhrases();

    /// <summary>
    /// Copies an audio file into the companion audio folder and returns the stored filename.
    /// </summary>
    string? CopyAudioToFolder(string sourcePath, string phraseText);

    /// <summary>
    /// Absolute path to the folder that contains companion voice-line audio files.
    /// </summary>
    string VoiceLineFolder { get; }
}

public interface IInteractionQueueService
{
    /// <summary>Whether any fullscreen interaction is currently active.</summary>
    bool IsBusy { get; }

    /// <summary>Try to start an interaction. Returns true if started immediately; false if queued or discarded.</summary>
    bool TryStart(string interactionType, Action triggerAction, bool queue = true);

    /// <summary>Mark the current interaction as complete and trigger the next queued one.</summary>
    void Complete(string interactionType);

    /// <summary>
    /// Release the interaction slot ONLY if <paramref name="interactionType"/> is the interaction
    /// currently active. Safe to call from abnormal teardown (panic key, ForceCleanup, session
    /// switch) that may have already released the slot — it can never clear a BubbleCount/LockCard
    /// that has since taken over. Atomic under the queue lock (no TOCTOU). Returns true if the slot
    /// was held by this type and released; false otherwise (no-op). Mirrors WPF
    /// <c>InteractionQueueService.CompleteIfCurrent</c> (v6.2.9 #14 / port #5). Default no-op for
    /// heads/tests that don't override it.
    /// </summary>
    bool CompleteIfCurrent(string interactionType) => false;

    /// <summary>Force clear the current interaction and any queued items.</summary>
    void ForceReset();

    /// <summary>Extend the stuck-detection timeout for the current interaction.</summary>
    void ExtendTimeout(TimeSpan duration);
}

public interface IBubbleCountService
{
    bool IsRunning { get; }
    bool IsBusy { get; }

    void Start();
    void Stop();
    void TriggerGame(bool forceTest = false);
    void RefreshSchedule();
    void ResetBusyState();
}

public interface IAttentionCheckService
{
    bool IsRunning { get; }
    event Action? OnPass;
    event Action? OnFail;

    void Start();
    void Stop();
    void FireNow();
}

public interface IModerationLog
{
    void RecordEdit(string fieldName, int count, string source);

    /// <summary>
    /// Records a moderation hit for AI input/output. <paramref name="source"/> is one of
    /// <c>input</c>, <c>output</c>, or <c>edit</c>; <paramref name="modelHint"/> identifies
    /// the provider/model (e.g. <c>cloud-quiz</c> or <c>local:&lt;model&gt;</c>).
    /// </summary>
    void Record(ProhibitedCategory category, string source, string modelHint);
}

#region Typed service abstractions for cross-platform heads

public interface IAuthProvider
{
    string ProviderName { get; }
    bool IsLoggedIn { get; }
    bool HasPremiumAccess { get; }
    Task StartOAuthFlowAsync();
    string? GetAccessToken();
    void Logout();
    string? UnifiedUserId { get; set; }
    string? DisplayName { get; set; }
}

public interface IChaosService
{
    bool IsRunning { get; }
    bool IsManuallyPaused { get; }
    double LastRunScore { get; }
    void ShowLoadoutSidebar();
    void CloseLoadoutSidebar();
    void NotifyLoadoutChanged();
    void StartRun(object cfg);
    void StartRunFromSidebar();
    void ToggleManualPause();
    void RequestStop();
    void CloseWarrenPhase();
    void OpenWarrenAt(string tag);
    void UnequipFromSidebar(string id);
    void UseToyById(string id);
    /// <summary>Hard teardown for app exit / main-window close: stop everything, clear the run,
    /// close the HUD + overlay, and CLEAR the crash sentinel so a clean shutdown mid-run never
    /// false-positives a crash at the next launch. No results, no payout. Safe when idle.
    /// DIM no-op default so existing implementers/fakes keep compiling (WPF ChaosModeService.cs:3085).</summary>
    void ForceShutdown() { }
}

public interface IAvatarWindowService
{
    bool IsMuted { get; }
    bool IsVisible { get; }

    /// <summary>True while the avatar is mid text-speech (queue/timers active). Default false;
    /// the real window service overrides. Used by the #463 keyword-line busy-retry.</summary>
    bool IsSpeaking => false;

    /// <summary>True while the avatar is playing linked speech audio. Default false; overridden.</summary>
    bool IsSpeakingAudio => false;

    /// <summary>Position-preserving pause of the avatar's spoken voice line for DTRH world-freeze
    /// (WPF ApplyWorldFreeze, DtrhHostService.cs:566-573). Default no-op so head services that cannot
    /// pause inherit safe behavior; the real window service overrides. Does NOT stop the line.</summary>
    void PauseSpokenAudio() { }

    /// <summary>Resume the avatar's spoken voice line after a world-freeze pause. Default no-op; overridden.</summary>
    void ResumeSpokenAudio() { }
    void ShowTube();
    void HideTube();
    void SetMuteAvatar(bool muted);
    void SetChaosRunActive(bool active);
    void SetDetached(bool detached);
    void SetPose(int poseNumber);
    void OpenChatWindow();
    void Giggle(string? text = null);
    void GigglePriority(string text, bool playSound = true, bool aiGenerated = false,
        string? phraseAudioPath = null, bool barkVoice = false);
}

public interface IBarkService
{
    void NotifyAvatarClicked();
    void NotifyChaosDollhouseFirstOpen();
    void NotifyChaosRevealFlash(string id);
    void NotifyChaosResultsShown(double score, double best, double delta, bool pb,
                                 int defused, int detonated, int bestCombo, string difficulty);
    void NotifyChaosRankUp(string rankName);
    void NotifyChaosGiftGiven();
    void NotifyChaosDraftAutopick();
    void NotifyChaosRunStarted(string difficulty);
    /// <summary>A one-time "first X" drops bonus fired over the DtRH meta bridge (mirrors WPF
    /// <c>App.Bark?.NotifyChaosFirstTime</c>). Default no-op body so existing implementers/fakes keep
    /// compiling; heads may override to surface a bark. ctx: the first-time id (e.g. "first_taste").</summary>
    void NotifyChaosFirstTime(string id) { }

    // ---- S5 draft/wave choreography barks (WPF Services/Companion/BarkService.cs:247-290).
    //      Default no-op bodies so existing implementers/fakes keep compiling. ----
    /// <summary>The run escalated into a new wave. ctx: wave (WPF BarkService.cs:247).</summary>
    void NotifyChaosWaveEscalated(int wave) { }
    /// <summary>The field was cleared at a wave boundary. ctx: the wave just cleared (WPF BarkService.cs:290).</summary>
    void NotifyChaosWaveCleared(int wave) { }
    /// <summary>A boon was drafted. ctx: boon name (WPF BarkService.cs:268).</summary>
    void NotifyChaosBoonPicked(string boon) { }
    /// <summary>A curse was drafted (fired instead of BoonPicked for sins) (WPF BarkService.cs:270).</summary>
    void NotifyChaosCursePicked(string boon, string rarity, double runMultBonus) { }
    /// <summary>The boon draft was skipped (null pick, +1 shield) (WPF BarkService.cs:273).</summary>
    void NotifyChaosBoonSkipped(int shieldsNow) { }
    /// <summary>The act advanced (edge-detected) (WPF BarkService.cs:287).</summary>
    void NotifyChaosActChanged(int act, int wave) { }
    void NotifyChaosFocusLow();
    void NotifyChaosGoldFirst();
    void NotifyChaosDuoDemo();

    // ---- Q11 chaos gameplay barks (WPF Services/Companion/BarkService.cs:275-318).
    //      Default no-op bodies so existing implementers/fakes keep compiling. ----
    /// <summary>T-minus ~10s of a chaos run: the hole is closing (once per run) (WPF BarkService.cs:292 "ChaosEndingSoon").</summary>
    void NotifyChaosEndingSoon() { }
    /// <summary>A darter was caught. ctx: points, combo, quick (WPF BarkService.cs:275 "ChaosDarterCaught").</summary>
    void NotifyChaosDarterCaught(double points, int combo, bool quick) { }
    /// <summary>A freeze bubble was caught. ctx: points, combo (WPF BarkService.cs:278 "ChaosFreezeCaught").</summary>
    void NotifyChaosFreezeCaught(double points, int combo) { }
    /// <summary>A combo milestone (every 10). ctx: combo, difficulty (WPF BarkService.cs:281 "ChaosComboMilestone").</summary>
    void NotifyChaosComboMilestone(int combo, string difficulty) { }
    /// <summary>A high combo threshold was crossed (edge-detected). ctx: combo, threshold (WPF BarkService.cs:284 "ChaosComboBig").</summary>
    void NotifyChaosComboBig(int combo, double threshold) { }
    /// <summary>The Tease's first-ever appearance (debut spawn) (WPF BarkService.cs:311 "ChaosTeaseDebut").</summary>
    void NotifyChaosTeaseDebut() { }
    /// <summary>A Tease expired untouched — the DENIED bonus paid. ctx: denied_count (WPF BarkService.cs:313 "ChaosTeaseDenied").</summary>
    void NotifyChaosTeaseDenied(int deniedCount) { }
    /// <summary>The player touched a Tease — payload + streak halve (WPF BarkService.cs:316 "ChaosTeaseClicked").</summary>
    void NotifyChaosTeaseClicked() { }
    /// <summary>5+ Teases denied in a single run. ctx: denied_count (WPF BarkService.cs:318 "ChaosTeaseDeniedStreak").</summary>
    void NotifyChaosTeaseDeniedStreak(int deniedCount) { }

    // ---- S2c-2a bubble-economy / lesson / run-end barks (WPF Services/Companion/BarkService.cs).
    //      Default no-op bodies so existing implementers/fakes keep compiling (S2b-1 precedent). ----
    /// <summary>A benign treat bubble was popped. ctx: variant, payload, combo.</summary>
    void NotifyChaosBenignPopped(string variant, string payload, int combo) { }
    /// <summary>A live bubble was defused in time. ctx: combo, variant, difficulty.</summary>
    void NotifyChaosBubbleDefused(int combo, string variant, string difficulty) { }
    /// <summary>A live bubble detonated (fuse expired undefused). ctx: variant, strength, runDetonations, combo, difficulty.</summary>
    void NotifyChaosBubbleDetonated(string variant, double strength, double runDetonations, int combo, string difficulty) { }
    /// <summary>A detonation was absorbed by a shield. ctx: variant, strength, runDetonations, combo, difficulty, shields.</summary>
    void NotifyChaosBubbleDetonatedAbsorbed(string variant, double strength, double runDetonations, int combo, string difficulty, int shields) { }
    /// <summary>First-ever defuse (teach beat).</summary>
    void NotifyChaosDefuseFirst() { }
    /// <summary>Defused with no focus meter (teach beat).</summary>
    void NotifyChaosDefuseNoFocus() { }
    /// <summary>Defused by releasing (teach beat).</summary>
    void NotifyChaosDefuseRelease() { }
    /// <summary>A bubble was detonated by a direct click (teach beat).</summary>
    void NotifyChaosClickDetonate() { }
    /// <summary>A lesson card was completed. ctx: lesson id.</summary>
    void NotifyChaosLessonComplete(string id) { }
    /// <summary>Run complete voice cue. ctx: finalXp, difficulty (WPF DtrhHostService.cs:448).</summary>
    void NotifyChaosRunCompleted(int finalXp, string difficulty) { }
}

public interface IVideoInfo
{
    bool IsPlaying { get; }
}

public interface IMainWindowService
{
    object? MainWindow { get; }
}

#endregion
