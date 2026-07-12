



using System.Text;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Core.Services.Commands;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.CompanionTab partial.
/// Companion selection cards, prompts, and avatar UI settings.
/// </summary>
public partial class CompanionTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<CompanionTabViewModel>? _logger;
    private readonly IModService? _modService;
    private readonly ICompanionService? _companionService;
    private readonly ICommunityPromptService? _promptService;
    private readonly IAvatarWindowService? _avatarWindowService;
    private readonly ISecretStore? _secretStore;
    // AI-7: the live-actions feed singleton (null at design time). Bound via LiveActions below.
    private readonly IAiLiveActionsFeed? _liveActionsFeed;
    // Design-time-only fallback so LiveActions returns a real (empty) collection when no feed is injected.
    private readonly ObservableCollection<string> _designLiveActions = new();

    public CompanionTabViewModel() : base("companion", "Companion", "🤖")
    {
        _companions = new ObservableCollection<CompanionCardViewModel>();
        _installedPrompts = new ObservableCollection<CommunityPromptRowViewModel>();
        InitializeDesignData();
    }

    public CompanionTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<CompanionTabViewModel> logger,
        IModService modService,
        ICompanionService companionService,
        ICommunityPromptService promptService,
        IAvatarWindowService avatarWindowService,
        ISecretStore secretStore,
        IAiLiveActionsFeed? liveActionsFeed = null) : base("companion", "Companion", "🤖")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _modService = modService;
        _companionService = companionService;
        _promptService = promptService;
        _avatarWindowService = avatarWindowService;
        _secretStore = secretStore;
        _liveActionsFeed = liveActionsFeed;
        _companions = new ObservableCollection<CompanionCardViewModel>();
        _installedPrompts = new ObservableCollection<CommunityPromptRowViewModel>();
        SyncUi();
    }

    public override void OnSelected()
    {
        base.OnSelected();
        AttachEvents();
    }

    public override void OnDeselected()
    {
        base.OnDeselected();
        DetachEvents();
    }

    private void AttachEvents()
    {
        if (_companionService != null)
        {
            _companionService.CompanionSwitched += OnCompanionEvent;
            _companionService.XPAwarded += OnCompanionXpEvent;
            _companionService.LevelUp += OnCompanionLevelEvent;
            _companionService.XPDrained += OnCompanionXpDrainEvent;
        }

        // AI-7: keep the "Live actions" placeholder in sync with the singleton feed. The feed's Items
        // outlive this transient VM, so subscribe on select / unsubscribe on deselect (mirrors WPF
        // MainWindow.Patreon.cs:1776 CollectionChanged -> UpdateLiveActionsPlaceholder). The -== guard
        // makes repeated select/deselect idempotent.
        if (_liveActionsFeed != null)
        {
            _liveActionsFeed.Items.CollectionChanged -= OnLiveActionsChanged;
            _liveActionsFeed.Items.CollectionChanged += OnLiveActionsChanged;
            HasLiveActions = _liveActionsFeed.Items.Count > 0;
        }
    }

    private void DetachEvents()
    {
        if (_companionService != null)
        {
            _companionService.CompanionSwitched -= OnCompanionEvent;
            _companionService.XPAwarded -= OnCompanionXpEvent;
            _companionService.LevelUp -= OnCompanionLevelEvent;
            _companionService.XPDrained -= OnCompanionXpDrainEvent;
        }

        if (_liveActionsFeed != null)
            _liveActionsFeed.Items.CollectionChanged -= OnLiveActionsChanged;
    }

    private void OnCompanionEvent(object? sender, CompanionId e) => SyncUi();
    private void OnCompanionXpEvent(object? sender, (CompanionId Companion, double Amount, double Modifier) e) => SyncUi();
    private void OnCompanionLevelEvent(object? sender, (CompanionId Companion, int NewLevel) e) => SyncUi();
    private void OnCompanionXpDrainEvent(object? sender, double e) => SyncUi();

    private void OnLiveActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => HasLiveActions = (_liveActionsFeed?.Items.Count ?? 0) > 0;

    /// <summary>
    /// The singleton live-actions feed (AI-7): the most recent AI-driven effect actions, bound to the
    /// scrolling "Live actions" list on this tab. Returns the shared feed's Items at runtime; an
    /// empty local collection at design time. Mirrors WPF <c>App.AiLiveActions</c> (the static
    /// ObservableCollection bound in MainWindow.Patreon.cs:1774). Newest-last ordering matches WPF.
    /// </summary>
    public ObservableCollection<string> LiveActions => _liveActionsFeed?.Items ?? _designLiveActions;

    /// <summary>True when the feed has at least one action line (hides the empty placeholder).</summary>
    [ObservableProperty]
    private bool _hasLiveActions;

    [ObservableProperty]
    private ObservableCollection<CompanionCardViewModel> _companions;

    [ObservableProperty]
    private ObservableCollection<CommunityPromptRowViewModel> _installedPrompts;

    [ObservableProperty]
    private CompanionCardViewModel? _activeCompanion;

    [ObservableProperty]
    private string _activeCompanionName = "";

    [ObservableProperty]
    private string _activeCompanionLevelText = "";

    [ObservableProperty]
    private string _activeCompanionDescription = "";

    [ObservableProperty]
    private string _activeCompanionXpText = "";

    [ObservableProperty]
    private double _activeCompanionProgress;

    [ObservableProperty]
    private bool _avatarEnabled;

    [ObservableProperty]
    private bool _triggerModeEnabled;

    [ObservableProperty]
    private int _triggerIntervalSeconds = 60;

    [ObservableProperty]
    private int _idleIntervalSeconds = 120;

    [ObservableProperty]
    private int _bubbleDurationSeconds = 2;

    [ObservableProperty]
    private bool _isDetached;

    /// <summary>
    /// True when an OpenAI-compatible API key is present in the <see cref="ISecretStore"/>.
    /// Drives the non-secret status chip ("key set" / "no key set"); the key itself is never
    /// surfaced to the UI or bindings.
    /// </summary>
    [ObservableProperty]
    private bool _hasOpenAiKey;

    [ObservableProperty]
    private string _activePromptName = Loc.Get("label_default_built_in");

    [ObservableProperty]
    private string _customizePromptName = "";

    partial void OnAvatarEnabledChanged(bool value) => Save();
    partial void OnTriggerModeEnabledChanged(bool value) => Save();
    partial void OnTriggerIntervalSecondsChanged(int value) => Save();
    partial void OnIdleIntervalSecondsChanged(int value) => Save();
    partial void OnBubbleDurationSecondsChanged(int value) => Save();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger?.LogInformation("Refreshing Companion tab");
        SyncUi();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SwitchCompanionAsync(int companionIndex)
    {
        _logger?.LogInformation("Switch companion requested: {Index}", companionIndex);

        if (companionIndex < 0 || companionIndex >= CompanionDefinition.AllCompanions.Length)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_invalid_companion_selection"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        _companionService?.SwitchCompanion((CompanionId)companionIndex);
        SyncUi();

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task AssignPersonalityAsync(int companionIndex)
    {
        _logger?.LogInformation("Assign personality requested for companion {Index}", companionIndex);

        var filters = new[] { new FileFilter("JSON files", new[] { "json" }) };
        var files = await (_dialogService?.ShowOpenFileDialogAsync(
            Loc.Get("title_select_ai_personality"),
            filters) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

        if (files.Count == 0) return;

        var imported = await (_promptService?.ImportFromFileAsync(files[0]) ?? Task.FromResult<CommunityPrompt?>(null));
        if (imported == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_import_failed"),
                Loc.Get("msg_prompt_import_failed"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        // L3-12: gate assigning an explicit prompt (ships a SlutMode variant + SlutMode on)
        // before it becomes the companion's active personality. Abort the assignment on cancel.
        if (!await EnsureExplicitContentAcknowledgedAsync(imported.PromptSettings)) return;

        _settingsService?.Current?.SetCompanionPromptId(companionIndex, imported.Id);
        _settingsService?.Save();
        _companionService?.SwitchCompanion((CompanionId)companionIndex);
        SyncUi();

        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_prompt_assigned"),
            string.Format(Loc.Get("msg_prompt_assigned_to_companion_fmt"), imported.Name, CompanionDefinition.GetById(companionIndex).Name)) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ActivatePromptAsync(string? promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId)) return;
        _logger?.LogInformation("Activate community prompt: {PromptId}", promptId);

        // L3-12: gate explicit community prompts (ship a SlutMode variant + SlutMode on) before
        // activation. Abort on cancel so the explicit prompt is never activated unacknowledged.
        var probePrompt = _promptService?.GetInstalledPrompt(promptId);
        if (!await EnsureExplicitContentAcknowledgedAsync(probePrompt?.PromptSettings)) return;

        if (_promptService?.ActivatePrompt(promptId) == true)
        {
            SyncUi();
            return;
        }

        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_error"),
            Loc.Get("msg_prompt_activate_failed"),
            DialogSeverity.Warning) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task RemovePromptAsync(string? promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId)) return;
        _logger?.LogInformation("Remove community prompt: {PromptId}", promptId);

        var prompt = _promptService?.GetInstalledPrompt(promptId);
        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_remove_prompt"),
            string.Format(Loc.Get("msg_remove_prompt_confirm_0"), prompt?.Name ?? promptId)) ?? Task.FromResult(false));
        if (!confirm) return;

        _promptService?.RemovePrompt(promptId);
        SyncUi();
    }

    [RelayCommand]
    private async Task DeactivatePromptAsync()
    {
        _logger?.LogInformation("Deactivate community prompt requested");
        _promptService?.DeactivatePrompt();
        SyncUi();
        await Task.CompletedTask;
    }

    /// <summary>
    /// L3-12: CCBill AI Content Merchant Addendum gate. Before activating or assigning a prompt
    /// that ships a SlutMode variant while SlutMode is on, the user must clear the
    /// explicit-content acknowledgement dialog. Mirrors the WPF gate in
    /// MainWindow.CompanionTab.cs. The gate rules are inlined here because
    /// <c>ConditioningControlPanel.Services.ExplicitContentGate</c> lives in the WPF head and is
    /// not part of CCP.Core. Returns true when the gated action may proceed (not gated, already
    /// acknowledged, or the user accepted the dialog); false only when the user cancels.
    /// </summary>
    private async Task<bool> EnsureExplicitContentAcknowledgedAsync(CompanionPromptSettings? promptSettings)
    {
        var settings = _settingsService?.Current;
        if (settings == null) return true;

        // Synthesize the same probe preset the WPF gate uses, then apply RequiresAcknowledgement.
        var probe = new PersonalityPreset { PromptSettings = promptSettings };
        if (!GateRequiresAcknowledgement(probe, settings.SlutModeEnabled)) return true;

        var prevSettings = settings.CompanionPrompt;
        if (GateIsAlreadyAcknowledged(prevSettings)) return true;

        var owner = GetMainWindow();
        if (owner is null)
        {
            // No owner to host the modal gate — refuse the gated action rather than bypassing
            // the compliance acknowledgement.
            _logger?.LogWarning("Explicit-content gate skipped: no owner window available");
            return false;
        }

        var dialog = new ConditioningControlPanel.Avalonia.Dialogs.ExplicitContentAcknowledgementDialog();
        var accepted = await dialog.ShowDialog<bool?>(owner);
        if (accepted != true) return false;

        if (prevSettings != null)
        {
            GateMarkAcknowledged(prevSettings);
            _settingsService?.Save();
        }
        return true;
    }

    // Inlined mirror of ConditioningControlPanel.Services.ExplicitContentGate (WPF-only).
    private static bool GateRequiresAcknowledgement(PersonalityPreset? preset, bool slutModeOn)
    {
        if (preset == null) return false;
        if (preset.RequiresExplicitAcknowledgement) return true;
        if (slutModeOn && !string.IsNullOrWhiteSpace(preset.PromptSettings?.SlutModePersonality)) return true;
        return false;
    }

    private static bool GateIsAlreadyAcknowledged(CompanionPromptSettings? settings)
        => settings != null
           && settings.ExplicitContentAcknowledged
           && settings.ExplicitAcknowledgedVersion == CompanionPromptSettings.ExplicitAcknowledgementVersion;

    private static void GateMarkAcknowledged(CompanionPromptSettings settings)
    {
        settings.ExplicitContentAcknowledged = true;
        settings.ExplicitAcknowledgedVersion = CompanionPromptSettings.ExplicitAcknowledgementVersion;
    }

    private static global::Avalonia.Controls.Window? GetMainWindow()
        => (global::Avalonia.Application.Current?.ApplicationLifetime
            as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    [RelayCommand]
    private async Task CustomizePromptAsync()
    {
        _logger?.LogInformation("Customize companion prompt requested");
        var dialog = new ConditioningControlPanel.Avalonia.Dialogs.CompanionPromptEditorDialog();
        // ShowDialog throws ArgumentNullException on a null owner (L3-04): resolve the main window.
        var owner = GetMainWindow();
        if (owner is not null)
        {
            await dialog.ShowDialog<bool?>(owner);
        }
        SyncUi();
    }

    [RelayCommand]
    private async Task ToggleDetachAsync()
    {
        IsDetached = !IsDetached;
        _logger?.LogInformation("Companion tab detach toggled: {Detached}", IsDetached);
        _avatarWindowService?.SetDetached(IsDetached);
        await Task.CompletedTask;
    }

    /// <summary>
    /// OpenAI-compatible API key entry (AI-5). The key is written to the platform
    /// <see cref="ISecretStore"/> under exactly <c>"openai-api-key"</c> — the literal the Core
    /// <c>OpenAiService</c> reads (<c>CCP.Core/Services/AIService/OpenAiService.cs:67</c>, field
    /// <c>SecretKey</c>; consumed by <c>OpenAiService.GetApiKey</c> via <c>Encoding.UTF8.GetString</c>).
    /// It is NEVER written to <c>settings.json</c>, NEVER logged, and NEVER shown back after save
    /// (the entry field is always cleared and never pre-populated). Mirrors the WPF head's masked
    /// PasswordBox handler (<c>MainWindow/MainWindow.Patreon.cs:1387-1394</c>,
    /// <c>TxtOpenAiApiKey_PasswordChanged</c>) but persists via the cross-platform ISecretStore
    /// seam (DPAPI on Windows) instead of the Windows-only DPAPI settings blob, per the
    /// <c>OpenAiService</c> design notes.
    /// </summary>
    private const string OpenAiApiKeySecretKey = "openai-api-key";

    /// <summary>
    /// Stores the entered OpenAI API key in the <see cref="ISecretStore"/>. UTF-8 encoded to match
    /// <c>OpenAiService.GetApiKey</c>'s decode. An empty/whitespace entry is a no-op (not a clear);
    /// use <see cref="ClearOpenAiKey"/> to remove the stored secret.
    /// </summary>
    public void SaveOpenAiKey(string plainKey)
    {
        if (_secretStore == null)
        {
            _logger?.LogWarning("SaveOpenAiKey: ISecretStore unavailable (design-time?).");
            return;
        }

        var trimmed = (plainKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        try
        {
            _secretStore.Store(OpenAiApiKeySecretKey, Encoding.UTF8.GetBytes(trimmed));
            // Key value intentionally never logged — only the fact that it was stored.
            _logger?.LogInformation("OpenAI API key saved to ISecretStore.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SaveOpenAiKey: failed to store the OpenAI API key.");
        }
        RefreshOpenAiKeyStatus();
    }

    /// <summary>
    /// Deletes the stored OpenAI API key from the <see cref="ISecretStore"/>.
    /// </summary>
    public void ClearOpenAiKey()
    {
        if (_secretStore == null)
        {
            _logger?.LogWarning("ClearOpenAiKey: ISecretStore unavailable (design-time?).");
            return;
        }

        try
        {
            _secretStore.Delete(OpenAiApiKeySecretKey);
            _logger?.LogInformation("OpenAI API key cleared from ISecretStore.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ClearOpenAiKey: failed to delete the OpenAI API key.");
        }
        RefreshOpenAiKeyStatus();
    }

    /// <summary>
    /// Refreshes <see cref="HasOpenAiKey"/> from the secret store. Checks presence/length only —
    /// the key bytes are never decoded to a string or shown in the UI.
    /// </summary>
    private void RefreshOpenAiKeyStatus()
    {
        if (_secretStore == null) { HasOpenAiKey = false; return; }
        try
        {
            var bytes = _secretStore.Retrieve(OpenAiApiKeySecretKey);
            HasOpenAiKey = bytes != null && bytes.Length > 0;
        }
        catch
        {
            HasOpenAiKey = false;
        }
    }

    private void SyncUi()
    {
        try
        {
            var settings = _settingsService?.Current;
            if (settings == null)
            {
                InitializeDesignData();
                return;
            }

            AvatarEnabled = settings.AvatarEnabled;
            TriggerModeEnabled = settings.TriggerModeEnabled;
            TriggerIntervalSeconds = settings.TriggerIntervalSeconds;
            IdleIntervalSeconds = settings.IdleGiggleIntervalSeconds;
            BubbleDurationSeconds = (int)settings.BubbleDurationSeconds;

            RefreshCompanionCards();
            RefreshPrompts();
            RefreshOpenAiKeyStatus();
            // AI-7: seed the live-actions placeholder state from the (possibly pre-populated) feed.
            HasLiveActions = (_liveActionsFeed?.Items.Count ?? 0) > 0;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SyncCompanionTabUI failed");
        }
    }

    private void RefreshCompanionCards()
    {
        Companions.Clear();
        var colors = new[] { "#FF69B4", "#9370DB", "#50C878", "#FF6B6B", "#F5DEB3" };
        var activeId = (int?)_companionService?.ActiveCompanion ?? 0;

        for (int i = 0; i < CompanionDefinition.AllCompanions.Length; i++)
        {
            var def = CompanionDefinition.GetById(i);
            var progress = _companionService?.GetProgress((CompanionId)i);
            var isMax = progress?.IsMaxLevel ?? false;
            var level = progress?.Level ?? 1;
            var promptId = _settingsService?.Current?.GetCompanionPromptId(i);
            var assignedName = promptId != null
                ? _promptService?.GetInstalledPrompt(promptId)?.Name
                : null;

            Companions.Add(new CompanionCardViewModel
            {
                Index = i,
                Name = _modService?.MakeModAware(def.GetDisplayName(false)) ?? def.Name,
                LevelText = isMax ? "MAX" : $"Lv.{level}",
                ColorHex = colors[i % colors.Length],
                IsActive = i == activeId,
                IsSupported = true,
                AssignedPromptName = assignedName ?? ""
            });
        }

        ActiveCompanion = Companions.FirstOrDefault(c => c.IsActive);
        if (ActiveCompanion != null) UpdateActiveCompanionDetails(ActiveCompanion);
    }

    private void UpdateActiveCompanionDetails(CompanionCardViewModel card)
    {
        var def = CompanionDefinition.GetById(card.Index);
        var progress = _companionService?.GetProgress((CompanionId)card.Index);
        var isMax = progress?.IsMaxLevel ?? false;

        ActiveCompanionName = card.Name;
        ActiveCompanionLevelText = isMax
            ? " · MAX LEVEL"
            : $" · Level {progress?.Level ?? 1}";
        ActiveCompanionDescription = def.Description;
        ActiveCompanionXpText = isMax
            ? "Complete!"
            : $"{(progress?.CurrentXP ?? 0):F0} / {(progress?.XPForNextLevel ?? 0):F0} XP";
        ActiveCompanionProgress = isMax ? 100 : (progress?.LevelProgress ?? 0) * 100;
    }

    private void RefreshPrompts()
    {
        InstalledPrompts.Clear();
        var activePromptId = _settingsService?.Current?.ActiveCommunityPromptId;
        var installed = _promptService?.GetInstalledPrompts() ?? new List<CommunityPrompt>();

        CustomizePromptName = GetActivePromptDisplayName();
        ActivePromptName = GetActivePromptDisplayName();

        if (installed.Count == 0)
        {
            InstalledPrompts.Add(new CommunityPromptRowViewModel
            {
                Name = Loc.Get("label_no_prompts_installed"),
                IsPlaceholder = true
            });
            return;
        }

        foreach (var prompt in installed)
        {
            InstalledPrompts.Add(new CommunityPromptRowViewModel
            {
                Id = prompt.Id,
                Name = prompt.Name,
                Author = prompt.Author,
                IsActive = prompt.Id == activePromptId
            });
        }
    }

    private string GetActivePromptDisplayName()
    {
        var activePromptId = _settingsService?.Current?.ActiveCommunityPromptId;
        if (!string.IsNullOrEmpty(activePromptId))
        {
            return _promptService?.GetInstalledPrompt(activePromptId)?.Name
                ?? $"Prompt {activePromptId}";
        }

        if (_settingsService?.Current?.CompanionPrompt?.UseCustomPrompt == true)
        {
            return Loc.Get("label_custom_edited");
        }

        return Loc.Get("label_default_built_in");
    }

    private void Save()
    {
        try
        {
            var settings = _settingsService?.Current;
            if (settings == null) return;
            settings.AvatarEnabled = AvatarEnabled;
            settings.TriggerModeEnabled = TriggerModeEnabled;
            settings.TriggerIntervalSeconds = TriggerIntervalSeconds;
            settings.IdleGiggleIntervalSeconds = IdleIntervalSeconds;
            settings.BubbleDurationSeconds = BubbleDurationSeconds;
            _settingsService?.Save();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save companion settings");
        }
    }

    private void InitializeDesignData()
    {
        AvatarEnabled = true;
        TriggerModeEnabled = false;
        TriggerIntervalSeconds = 60;
        IdleIntervalSeconds = 120;
        BubbleDurationSeconds = 2;
        Companions.Clear();
        InstalledPrompts.Clear();
        ActivePromptName = Loc.Get("label_default_built_in");
    }
}

public partial class CompanionCardViewModel : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _levelText = "";

    [ObservableProperty]
    private string _colorHex = "#FF69B4";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isSupported = true;

    [ObservableProperty]
    private string _assignedPromptName = "";
}

public partial class CommunityPromptRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _author = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPlaceholder;
}
