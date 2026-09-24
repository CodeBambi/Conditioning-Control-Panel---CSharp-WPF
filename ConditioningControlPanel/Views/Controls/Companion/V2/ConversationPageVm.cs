using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Views.Controls.Companion.V2;

internal sealed record ConversationLine(string Id, string Speaker, string Text, string Time, bool IsUser);

/// <summary>One projection of the brain's shared conversation. No transcript of its own is persisted.</summary>
internal sealed class ConversationPageVm : CompanionObservable
{
    private readonly CompanionRoomRuntimeVm _room;
    private ChatSession? _session;
    private CancellationTokenSource? _send;
    private string _draft = string.Empty;
    private string _notice = string.Empty;
    private bool _busy;
    private bool _canRetry;
    private bool _observing;
    public ConversationPageVm(CompanionRoomRuntimeVm room) => _room = room;
    public CompanionRoomRuntimeVm Room => _room;
    public ObservableCollection<ConversationLine> Turns { get; } = new();
    public string Draft { get => _draft; set => Set(ref _draft, value ?? string.Empty); }
    public string Notice { get => _notice; private set { Set(ref _notice, value); Raise(nameof(HasNotice)); Raise(nameof(Face)); Raise(nameof(StatusColor)); } }
    public bool HasNotice => Notice.Length > 0;
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(CanCompose)); Raise(nameof(Status)); Raise(nameof(Face)); Raise(nameof(StatusColor)); } }
    public bool CanCompose => !Busy;
    public bool CanRetry { get => _canRetry; private set => Set(ref _canRetry, value); }
    private bool HouseCharacter => string.IsNullOrWhiteSpace(App.Mods?.ActiveMod?.Id) ||
        string.Equals(App.Mods.ActiveMod.Id, BuiltInMods.CCPDefaultId, StringComparison.OrdinalIgnoreCase);
    public string Name => HouseCharacter
        ? (App.Settings?.Current?.ActiveCompanionId ?? 0) == 0 ? "EMI" : App.Companion?.ActiveCompanionDef.Name ?? _room.Hero.Name
        : App.Mods?.GetCompanionName() ?? _room.Hero.Name;
    public bool IsEmi => HouseCharacter && (App.Settings?.Current?.ActiveCompanionId ?? 0) == 0 &&
        ((App.Settings?.Current?.SelectedAvatarSet ?? 1) <= 3 || App.Settings?.Current?.SelectedAvatarSet == 8);
    public string PerkCost => App.Companion?.ActivePerk == CompanionBonusType.XPDrain ? "Leech: -3 XP/s" : string.Empty;
    public string Familiarity => App.Settings?.Current?.CompanionPrompt?.ChatMemoryEnabled != false &&
        App.Brain?.Memory is MemoryStore memory
        ? Loc.Get("companion_v2_familiarity_" + Services.Companion.ConversationRelationship.Stage(
            Services.Companion.ConversationRelationship.Turns(memory.Relationships, App.Mods?.ActiveModId)))
        : string.Empty;
    public string Flavor => App.Personality?.GetActivePreset()?.Description ?? _room.Hero.Flavor;
    public string Face => Busy ? "..." : HasNotice ? "o_o" : NeedsStart ? "-_-" : "^_^";
    public string StatusColor => HasNotice || NeedsSignIn || DailyExhausted ? "#FFCD83" : NeedsStart ? "#BFAEC9" : "#80E4C5";
    public string Allowance => _room.Engine.Provider == CompanionProviderMode.Cloud && App.Ai?.DailyRequestsRemaining is int count && count >= 0
        ? Loc.GetF("companion_engine_status_ready_fmt", count) : string.Empty;
    public bool DailyExhausted => _room.Engine.Provider == CompanionProviderMode.Cloud && App.Ai?.DailyRequestsRemaining == 0;
    public bool NeedsStart => _room.Engine.Provider == CompanionProviderMode.Off;
    public bool NeedsSignIn => _room.Engine.Provider == CompanionProviderMode.Cloud && !_room.Engine.IsLoggedIn;
    public bool HasNoTurns => Turns.Count == 0;
    public string VoiceLabel => Loc.Get(_room.Hero.IsMuted ? "companion_v2_unmute" : "companion_v2_mute");
    public string StartLabel => Loc.Get(NeedsSignIn ? "companion_v2_signin" : "companion_v2_start");
    public string Status => Busy ? Loc.Get("companion_v2_replying") : NeedsStart ? Loc.Get("companion_v2_off")
        : NeedsSignIn ? Loc.Get("companion_v2_signin_status") : DailyExhausted ? Loc.Get("companion_v2_error_limit")
        : !_room.Engine.IsHealthy && !string.IsNullOrEmpty(_room.Engine.StatusLine) ? _room.Engine.StatusLine : Loc.Get("companion_v2_ready");
    public bool ShowStart => NeedsStart || NeedsSignIn;
    public string Privacy => Loc.Get(App.Settings?.Current?.CompanionPrompt?.AiProvider is AiProviderType.Local or AiProviderType.OpenAiCompatible
        ? "companion_v2_privacy_endpoint" : "companion_v2_privacy_cloud");

    public void Refresh()
    {
        _room.SyncBrain();
        if (_observing) Attach(App.Brain?.Session);
        Reconcile();
        foreach (var name in new[] { nameof(NeedsStart), nameof(NeedsSignIn), nameof(ShowStart), nameof(StartLabel), nameof(Status), nameof(Privacy), nameof(Name), nameof(IsEmi), nameof(Flavor), nameof(Face), nameof(StatusColor), nameof(Allowance), nameof(Familiarity), nameof(PerkCost) }) Raise(name);
    }
    public void Resume()
    {
        if (!_observing)
        {
            _observing = true;
            _room.EngineVm.PropertyChanged += RoomChanged;
            _room.HeroVm.PropertyChanged += RoomChanged;
        }
        Refresh();
    }
    public void Detach()
    {
        _observing = false;
        _room.EngineVm.PropertyChanged -= RoomChanged;
        _room.HeroVm.PropertyChanged -= RoomChanged;
        Attach(null);
    }
    private void RoomChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var name in new[] { nameof(ShowStart), nameof(StartLabel), nameof(Status), nameof(Privacy), nameof(Name), nameof(IsEmi), nameof(Flavor), nameof(Face), nameof(StatusColor), nameof(Allowance), nameof(Familiarity), nameof(PerkCost), nameof(VoiceLabel) }) Raise(name);
    }
    private void Attach(ChatSession? session)
    {
        if (ReferenceEquals(session, _session)) return;
        if (_session != null) _session.TurnsChanged -= Changed;
        _session = session;
        if (_session != null) _session.TurnsChanged += Changed;
    }
    private void Changed(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(Reconcile, DispatcherPriority.Normal);
    }
    internal static bool Shows(TurnKind kind) => kind is TurnKind.UserChat or TurnKind.AssistantChat;
    private void Reconcile()
    {
        var next = (_session?.Turns ?? Array.Empty<CompanionTurn>()).Where(t => Shows(t.Kind)).ToArray();
        // Update only the changed suffix, preserving scroll position and selectable text.
        var common = 0;
        while (common < Turns.Count && common < next.Length && Turns[common].Id == next[common].Id) common++;
        while (Turns.Count > common) Turns.RemoveAt(Turns.Count - 1);
        foreach (var t in next.Skip(common))
            Turns.Add(new(t.Id, t.Kind == TurnKind.UserChat ? Loc.Get("companion_v2_you") : Name,
                t.Text, t.Utc.ToLocalTime().ToString("t"), t.Kind == TurnKind.UserChat));
        Raise(nameof(HasNoTurns));
    }
    public void Start()
    {
        if (NeedsStart)
            _room.EngineVm.Provider = (App.Settings?.Current?.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud) switch
            {
                AiProviderType.Local => CompanionProviderMode.LocalOllama,
                AiProviderType.OpenAiCompatible => CompanionProviderMode.Custom,
                _ => CompanionProviderMode.Cloud
            };
        Refresh();
        if (NeedsSignIn) _room.Engine.LoginCommand.Execute(null);
    }
    public void Stop() => _send?.Cancel();
    public async Task SendAsync()
    {
        var text = Draft.Trim();
        if (Busy || text.Length == 0) return;
        Refresh();
        if (ShowStart) { Start(); return; }
        var brain = App.Brain;
        if (!CompanionBrain.ShouldRoute(brain)) { Notice = Loc.Get("companion_v2_unavailable"); return; }
        using var cancellation = new CancellationTokenSource();
        _send = cancellation;
        Busy = true;
        CanRetry = false;
        Notice = string.Empty;
        try
        {
            var result = await brain!.ChatAsync(text, cancellation.Token);
            if (result.Refusal != null)
            {
                // A policy refusal must not leave the prohibited draft available for resending.
                Draft = string.Empty;
                Notice = Loc.Get("companion_v2_refused");
                App.AvatarWindow?.ShowModerationRefusalBubble(result.Refusal.Source);
            }
            else if (result.IsAiGenerated) Draft = string.Empty;
            else
            {
                Notice = Loc.Get(FailureNoticeKey(result.Failure));
                CanRetry = result.Retryable || result.Failure == AiFailureKind.Cancelled;
            }
        }
        catch (OperationCanceledException) { Notice = Loc.Get("companion_v2_cancelled"); CanRetry = true; }
        catch (Exception ex)
        {
            App.Logger?.Warning("Companion conversation failed: {Type}", ex.GetType().Name);
            Notice = Loc.Get("companion_v2_unavailable");
            CanRetry = true;
        }
        finally { _send = null; Busy = false; Refresh(); }
    }
    internal static string FailureNoticeKey(AiFailureKind? failure) => failure switch
    {
        AiFailureKind.SignInRequired => "companion_v2_error_signin",
        AiFailureKind.DailyLimit => "companion_v2_error_limit",
        AiFailureKind.BudgetLimit => "companion_v2_error_budget",
        AiFailureKind.Offline => "companion_v2_error_offline",
        AiFailureKind.Busy => "companion_v2_busy",
        AiFailureKind.Cancelled => "companion_v2_cancelled",
        AiFailureKind.InvalidResponse => "companion_v2_error_invalid",
        _ => "companion_v2_unavailable"
    };
    public void NewChat()
    {
        if (Busy) return;
        App.Brain?.ForgetThread();
        Notice = string.Empty;
        CanRetry = false;
        Refresh();
    }
}
