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

internal sealed record ConversationAction(string TurnId, string Id, string Label);
/// <summary>One ask card answer on the chat page. Colours come from the card's tone.</summary>
internal sealed record ConversationAskChoice(string CardId, string ChoiceId, string Label, bool Enabled,
    string Background, string BorderColor, string Foreground);
internal sealed record ConversationLine(string Id, string Speaker, string Text, string Time, bool IsUser)
{
    public ConversationAction[] Actions { get; init; } = Array.Empty<ConversationAction>();
    public ConversationAskChoice[] AskChoices { get; init; } = Array.Empty<ConversationAskChoice>();
    public string? AskCardId { get; init; }
    // A sanctioned video the reply names; the page draws it as a watch button (old chat's chip).
    public string? LinkTitle { get; init; }
    public string? LinkUrl { get; init; }
    public bool HasLink => !string.IsNullOrEmpty(LinkTitle) && !string.IsNullOrEmpty(LinkUrl);
    public bool HasAsk => AskChoices.Length > 0;

    /// <summary>Attaches the live ask card (question and answers). A card the service no longer knows draws as plain text.</summary>
    internal ConversationLine WithAsk(string? cardId)
    {
        if (Services.Companion.Asks.CompanionAskService.Instance.Find(cardId) is not { } card) return this;
        var open = card.IsOpen(DateTime.UtcNow);
        return this with
        {
            AskCardId = card.Id,
            Text = card.Question,
            AskChoices = card.Choices.Select(c =>
            {
                var (bg, border, fg) = Services.Companion.Asks.AskTones.Colours(c.Tone);
                return new ConversationAskChoice(card.Id, c.Id, card.ChosenId == c.Id ? "✓ " + c.Label : c.Label,
                    open, bg, border, fg);
            }).ToArray()
        };
    }
}

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
    private IMemoryStore? _memoryOwner;
    public event Action? AccountChanged;
    public ConversationPageVm(CompanionRoomRuntimeVm room) => _room = room;
    public CompanionRoomRuntimeVm Room => _room;
    public ObservableCollection<ConversationLine> Turns { get; } = new();
    public string Draft { get => _draft; set { Set(ref _draft, value ?? string.Empty); Raise(nameof(CanSend)); } }
    public string Notice { get => _notice; private set { Set(ref _notice, value); Raise(nameof(HasNotice)); Raise(nameof(Status)); Raise(nameof(Face)); Raise(nameof(StatusColor)); } }
    public bool HasNotice => Notice.Length > 0;
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(CanCompose)); Raise(nameof(CanSend)); Raise(nameof(Status)); Raise(nameof(Face)); Raise(nameof(StatusColor)); } }
    public bool CanCompose => !Busy;
    public bool CanSend => !Busy && !string.IsNullOrWhiteSpace(Draft);
    public bool CanRetry { get => _canRetry; private set => Set(ref _canRetry, value); }
    // The active mod's own companion name; CCP Default's manifest says the neutral "Companion".
    public string Name => App.Mods?.GetCompanionName() ?? _room.Hero.Name;
    public string PerkCost => App.Companion?.ActivePerk == CompanionBonusType.XPDrain ? "Leech: -3 XP/s" : string.Empty;
    public string Familiarity => App.Settings?.Current?.CompanionPrompt?.ChatMemoryEnabled != false &&
        App.Brain?.Memory is MemoryStore memory
        ? Loc.Get("companion_v2_familiarity_" + Services.Companion.ConversationRelationship.Stage(
            Services.Companion.ConversationRelationship.Turns(memory.Relationships, App.Mods?.ActiveModId)))
        : string.Empty;
    public string Flavor => App.Personality?.GetActivePreset()?.Description ?? _room.Hero.Flavor;
    public string Face => Busy ? "..." : HasNotice ? "o_o" : NeedsStart ? "-_-" : "^_^";
    public string StatusColor => HasNotice || NeedsSignIn || DailyExhausted ? "#FFCD83" : NeedsStart ? "#BFAEC9" : "#F2A8D4";
    public string Allowance => _room.Engine.Provider == CompanionProviderMode.Cloud && App.Ai?.DailyRequestsRemaining is int count && count >= 0
        ? Loc.GetF("companion_engine_status_ready_fmt", count) : string.Empty;
    public bool DailyExhausted => _room.Engine.Provider == CompanionProviderMode.Cloud && App.Ai?.DailyRequestsRemaining == 0;
    public bool NeedsStart => _room.Engine.Provider == CompanionProviderMode.Off;
    public bool NeedsSignIn => _room.Engine.Provider == CompanionProviderMode.Cloud && !_room.Engine.IsLoggedIn;
    public bool HasNoTurns => Turns.Count == 0;
    public bool AsksEnabled
    {
        get => App.Settings?.Current?.CompanionAsksEnabled != false;
        set
        {
            if (App.Settings?.Current is not { } settings || settings.CompanionAsksEnabled == value) return;
            settings.CompanionAsksEnabled = value;
            try { App.Settings.Save(); } catch (Exception ex) { App.Logger?.Debug("Asks toggle not saved: {E}", ex.Message); }
            Raise(nameof(AsksEnabled));
        }
    }
    public string VoiceLabel => Loc.Get(_room.Hero.IsMuted ? "companion_v2_unmute" : "companion_v2_mute");
    public string StartLabel => Loc.Get(NeedsSignIn ? "companion_v2_signin" : "companion_v2_start");
    public string Status => Busy ? Loc.Get("companion_v2_replying") : HasNotice ? Loc.Get("companion_v2_reply_failed") : NeedsStart ? Loc.Get("companion_v2_off")
        : NeedsSignIn ? Loc.Get("companion_v2_signin_status") : DailyExhausted ? Loc.Get("companion_v2_error_limit")
        : !_room.Engine.IsHealthy && !string.IsNullOrEmpty(_room.Engine.StatusLine) ? _room.Engine.StatusLine : Loc.Get("companion_v2_ready");
    public bool ShowStart => NeedsStart || NeedsSignIn;
    public string Privacy => Loc.Get(App.Settings?.Current?.CompanionPrompt?.AiProvider is AiProviderType.Local or AiProviderType.OpenAiCompatible
        ? "companion_v2_privacy_endpoint" : "companion_v2_privacy_cloud");

    public void Refresh()
    {
        App.Brain?.EnsureCurrentAccount();
        var owner = App.Brain?.Memory;
        if (!ReferenceEquals(owner, _memoryOwner))
        {
            var changed = _memoryOwner != null;
            _memoryOwner = owner;
            Stop();
            Draft = string.Empty;
            Notice = string.Empty;
            CanRetry = false;
            if (changed) AccountChanged?.Invoke();
        }
        _room.MemoryVm.Sync();
        _room.SyncBrain();
        if (_observing) Attach(App.Brain?.Session);
        Reconcile();
        foreach (var name in new[] { nameof(NeedsStart), nameof(NeedsSignIn), nameof(ShowStart), nameof(StartLabel), nameof(Status), nameof(Privacy), nameof(Name), nameof(Flavor), nameof(Face), nameof(StatusColor), nameof(Allowance), nameof(Familiarity), nameof(PerkCost) }) Raise(name);
    }
    public void Resume()
    {
        if (!_observing)
        {
            _observing = true;
            App.UnifiedIdentityChanged += IdentityChanged;
            _room.EngineVm.PropertyChanged += RoomChanged;
            _room.HeroVm.PropertyChanged += RoomChanged;
            Services.Companion.Asks.CompanionAskService.Instance.CardChanged += AskChanged;
        }
        Refresh();
    }
    public void Detach()
    {
        _observing = false;
        App.UnifiedIdentityChanged -= IdentityChanged;
        _room.EngineVm.PropertyChanged -= RoomChanged;
        _room.HeroVm.PropertyChanged -= RoomChanged;
        Services.Companion.Asks.CompanionAskService.Instance.CardChanged -= AskChanged;
        Attach(null);
    }
    private void IdentityChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) Refresh();
        else dispatcher.BeginInvoke(Refresh, DispatcherPriority.Normal);
    }
    private void RoomChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var name in new[] { nameof(ShowStart), nameof(StartLabel), nameof(Status), nameof(Privacy), nameof(Name), nameof(Flavor), nameof(Face), nameof(StatusColor), nameof(Allowance), nameof(Familiarity), nameof(PerkCost), nameof(VoiceLabel) }) Raise(name);
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
            Turns.Add((Line(t, Name, Services.Companion.CompanionLinkIndex.FindMentionedTitle) with
                { Actions = t.ActivityIds.Select(Services.Companion.CompanionActivities.Find)
                    .Where(a => a?.Allowed == true).Select(a => new ConversationAction(t.Id, a!.Id,
                        Services.Companion.CompanionActivities.ButtonLabel(a.Label))).ToArray() }).WithAsk(t.AskCardId));
        Raise(nameof(HasNoTurns));
    }
    private void AskChanged(Services.Companion.Asks.AskCard card)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        void Update()
        {
            for (int i = 0; i < Turns.Count; i++)
                if (Turns[i].AskCardId == card.Id) Turns[i] = Turns[i].WithAsk(card.Id);
        }
        if (dispatcher.CheckAccess()) Update(); else dispatcher.BeginInvoke(new Action(Update), DispatcherPriority.Normal);
    }
    public void AnswerAsk(ConversationAskChoice choice) =>
        Services.Companion.Asks.CompanionAskService.Instance.Answer(choice.CardId, choice.ChoiceId);
    /// <summary>
    /// One transcript line. Only a model reply gets a watch link: a title in the user's own message
    /// is them talking, and an app reply never named a video. The visible text loses the dead
    /// "[Title]" brackets or "[PLAY THE VIDEO]" placeholder the button now stands in for.
    /// </summary>
    internal static ConversationLine Line(CompanionTurn t, string name,
        Func<string?, Services.Companion.CompanionLinkIndex.Entry?> findTitle)
    {
        var isUser = t.Kind == TurnKind.UserChat;
        var link = !isUser && t.Kind == TurnKind.AssistantChat && !t.IsApplicationReply ? findTitle(t.Text) : null;
        var text = t.Kind == TurnKind.AssistantChat && !t.IsApplicationReply
            ? Services.Companion.ConversationLinks.Tidy(t.Text, link?.Title)
            : t.Text;
        return new(t.Id, isUser ? Loc.Get("companion_v2_you") : name, text, t.Utc.ToLocalTime().ToString("t"), isUser)
        { LinkTitle = link?.Title, LinkUrl = link?.Url };
    }
    public void OpenLink(ConversationLine line)
    {
        if (!line.HasLink || !Services.Companion.CompanionLinkIndex.IsSanctioned(line.LinkUrl)) return;
        CompanionLinkLauncher.Open(line.LinkUrl);
    }
    public void OpenActivity(ConversationAction action)
    {
        App.Brain?.EnsureCurrentAccount();
        if (!ReferenceEquals(_memoryOwner, App.Brain?.Memory)
            || _session?.Turns.Any(t => t.Id == action.TurnId && t.ActivityIds.Contains(action.Id)) != true) return;
        try
        {
            if (Services.Companion.CompanionActivities.Find(action.Id)?.TryOpen() == true) return;
        }
        catch (Exception ex) { App.Logger?.Warning("Companion activity failed: {Type}", ex.GetType().Name); }
        Notice = Loc.Get("companion_v2_activity_unavailable");
        CanRetry = false;
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
        Refresh();
        var text = Draft.Trim();
        if (Busy || text.Length == 0) return;
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
            var owner = brain!.Memory;
            var result = await brain.ChatAsync(text, cancellation.Token);
            brain.EnsureCurrentAccount();
            if (!ReferenceEquals(owner, brain.Memory)) return;
            if (result.Refusal != null)
            {
                // A policy refusal must not leave the prohibited draft available for resending.
                Draft = string.Empty;
                Notice = Loc.Get("companion_v2_refused");
                App.AvatarWindow?.ShowModerationRefusalBubble(result.Refusal.Source);
            }
            else if (result.IsAiGenerated || result.IsApplicationReply)
            {
                Draft = string.Empty;
                SpeakThroughTube(result.Text, result.IsAiGenerated);
                Services.Companion.Asks.CompanionAskService.Instance.OfferForRequest(text);
            }
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
    /// <summary>
    /// A reply typed on this page is also said through the avatar's speech bubble when the tube is
    /// showing, the same GigglePriority a tube chat ends with. This page's send never passes through
    /// the tube's own input box, so the reply is spoken exactly once.
    /// </summary>
    private static void SpeakThroughTube(string? text, bool aiGenerated)
    {
        if (!ShouldSpeak(text, App.Settings?.Current?.AvatarEnabled == true)) return;
        var avatar = App.AvatarWindow;
        if (avatar == null) return;
        try
        {
            avatar.RunOnAvatar(() =>
            {
                if (!avatar.IsVisible) return;
                avatar.GigglePriority(text!, aiGenerated: aiGenerated);
            });
        }
        catch (Exception ex) { App.Logger?.Debug("Companion page reply not spoken: {Type}", ex.GetType().Name); }
    }
    internal static bool ShouldSpeak(string? text, bool avatarEnabled) => avatarEnabled && !string.IsNullOrWhiteSpace(text);
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
