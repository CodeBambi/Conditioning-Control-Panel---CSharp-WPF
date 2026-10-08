// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Controls/Companion/V2/ConversationPageVm.cs: one
// projection of the brain's shared conversation for Companion > Chat. Deviations, each because the
// piece is not on this head:
//   - the room is the head's CompanionRoomView zones (hero + engine viewmodels), not WPF's
//     CompanionRoomRuntimeVm; Hero commands (Show / Hide / Pop out / Voice) are the hero card's own;
//   - PerkCost is empty (App.Companion.ActivePerk is WPF-only);
//   - ConversationLinks.Tidy is WPF-only: a reply keeps its text as written, the watch button still
//     comes from CompanionLinkIndex.FindMentionedTitle (Core);
//   - activities come from CompanionEffects.Activities() (this head's launcher games + pages).
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Asks;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Serilog;
using Mode = ConditioningControlPanel.Views.Controls.Companion.CompanionProviderMode;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;

internal sealed record ConversationAction(string TurnId, string Id, string Label);

/// <summary>One ask card answer on the chat page. Colours come from the card's tone.</summary>
internal sealed record ConversationAskChoice(string CardId, string ChoiceId, string Label, bool Enabled,
    string Background, string BorderColor, string Foreground);

internal sealed record ConversationLine(string Id, string Speaker, string Text, string Time, bool IsUser)
{
    public ConversationAction[] Actions { get; init; } = Array.Empty<ConversationAction>();
    public ConversationAskChoice[] AskChoices { get; init; } = Array.Empty<ConversationAskChoice>();
    public string? AskCardId { get; init; }
    /// <summary>A sanctioned video the reply names; the page draws it as a watch button.</summary>
    public string? LinkTitle { get; init; }
    public string? LinkUrl { get; init; }
    public bool HasLink => !string.IsNullOrEmpty(LinkTitle) && !string.IsNullOrEmpty(LinkUrl);
    public bool HasAsk => AskChoices.Length > 0;
    public bool HasActions => Actions.Length > 0;

    /// <summary>Attaches the live ask card (question and answers). A card the service no longer knows draws as plain text.</summary>
    internal ConversationLine WithAsk(string? cardId)
    {
        if (CompanionAskService.Instance.Find(cardId) is not { } card) return this;
        var open = card.IsOpen(DateTime.UtcNow);
        return this with
        {
            AskCardId = card.Id,
            Text = card.Question,
            AskChoices = card.Choices.Select(c =>
            {
                var (bg, border, fg) = AskTones.Colours(c.Tone);
                return new ConversationAskChoice(card.Id, c.Id, card.ChosenId == c.Id ? "✓ " + c.Label : c.Label,
                    open, bg, border, fg);
            }).ToArray()
        };
    }
}

/// <summary>One projection of the brain's shared conversation. No transcript of its own is persisted.</summary>
internal sealed class ConversationPageVm : CompanionObservable
{
    private readonly CompanionHeroCardViewModel _hero;
    private readonly EngineRoomVm _engine;
    private ChatSession? _session;
    private CancellationTokenSource? _send;
    private string _draft = string.Empty;
    private string _notice = string.Empty;
    private bool _busy;
    private bool _canRetry;
    private bool _observing;
    private IMemoryStore? _memoryOwner;

    public event Action? AccountChanged;

    public ConversationPageVm(CompanionHeroCardViewModel hero, EngineRoomVm engine)
    {
        _hero = hero;
        _engine = engine;
    }

    /// <summary>The hero card's viewmodel: the header's portrait and the four switches bind to it.</summary>
    public CompanionHeroCardViewModel Hero => _hero;
    public EngineRoomVm Engine => _engine;
    public ObservableCollection<ConversationLine> Turns { get; } = new();
    public string Draft { get => _draft; set { Set(ref _draft, value ?? string.Empty); Raise(nameof(CanSend)); Raise(nameof(DraftEmpty)); } }
    public bool DraftEmpty => _draft.Length == 0;
    public string Notice { get => _notice; private set { Set(ref _notice, value); Raise(nameof(HasNotice)); Raise(nameof(Status)); Raise(nameof(StatusColor)); } }
    public bool HasNotice => Notice.Length > 0;
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(CanCompose)); Raise(nameof(CanSend)); Raise(nameof(Status)); Raise(nameof(StatusColor)); } }
    public bool CanCompose => !Busy;
    public bool CanSend => !Busy && !string.IsNullOrWhiteSpace(Draft);
    public bool CanRetry { get => _canRetry; private set => Set(ref _canRetry, value); }
    // The active mod's own companion name; CCP Default's manifest says the neutral "Companion".
    public string Name => _hero.Name;
    public string PerkCost => string.Empty;
    public bool HasPerkCost => PerkCost.Length > 0;
    public string Familiarity => CoreSettings.Current?.CompanionPrompt?.ChatMemoryEnabled != false
        && App.Brain?.Memory is MemoryStore memory
        ? Loc.Get("companion_v2_familiarity_" + ConversationRelationship.Stage(
            ConversationRelationship.Turns(memory.Relationships, CoreMods.ActiveModId)))
        : string.Empty;
    public string Flavor => _hero.Flavor;
    public string StatusColor => HasNotice || NeedsSignIn || DailyExhausted ? "#FFCD83" : NeedsStart ? "#BFAEC9" : "#F2A8D4";
    public string Allowance => _engine.Provider == Mode.Cloud && App.Ai?.DailyRequestsRemaining is int count && count >= 0
        ? Loc.GetF("companion_engine_status_ready_fmt", count) : string.Empty;
    public bool DailyExhausted => _engine.Provider == Mode.Cloud && App.Ai?.DailyRequestsRemaining == 0;
    public bool NeedsStart => _engine.Provider == Mode.Off;
    public bool NeedsSignIn => _engine.Provider == Mode.Cloud && !_engine.IsLoggedIn;
    public bool HasNoTurns => Turns.Count == 0;
    public bool HasTurns => Turns.Count > 0;
    public string VoiceLabel => Loc.Get(_hero.IsMuted ? "companion_v2_unmute" : "companion_v2_mute");
    // The tube's on switch (AvatarEnabled). The page hides the old room, whose eye toggle and Wake
    // button were that switch, so the header carries it (7.1.1 CompanionOnSwitchTests).
    public bool CompanionShown => _hero.IsCompanionShown;
    public bool CompanionHidden => !CompanionShown;
    public string StartLabel => Loc.Get(NeedsSignIn ? "companion_v2_signin" : "companion_v2_start");
    public string Status => Busy ? Loc.Get("companion_v2_replying") : HasNotice ? Loc.Get("companion_v2_reply_failed") : NeedsStart ? Loc.Get("companion_v2_off")
        : NeedsSignIn ? Loc.Get("companion_v2_signin_status") : DailyExhausted ? Loc.Get("companion_v2_error_limit")
        : !_engine.IsHealthy && !string.IsNullOrEmpty(_engine.StatusLine) ? _engine.StatusLine : Loc.Get("companion_v2_ready");
    public bool ShowStart => NeedsStart || NeedsSignIn;
    public string Privacy => Loc.Get(CoreSettings.Current?.CompanionPrompt?.AiProvider is AiProviderType.Local or AiProviderType.OpenAiCompatible
        ? "companion_v2_privacy_endpoint" : "companion_v2_privacy_cloud");

    private static readonly string[] RoomNames =
    {
        nameof(NeedsStart), nameof(NeedsSignIn), nameof(ShowStart), nameof(StartLabel), nameof(Status), nameof(Privacy),
        nameof(Name), nameof(Flavor), nameof(StatusColor), nameof(Allowance), nameof(Familiarity), nameof(PerkCost),
        nameof(VoiceLabel), nameof(CompanionShown), nameof(CompanionHidden),
    };

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
        _hero.Sync();
        _engine.Sync();
        if (_observing) Attach(App.Brain?.Session);
        Reconcile();
        foreach (var name in RoomNames) Raise(name);
    }

    public void Resume()
    {
        if (!_observing)
        {
            _observing = true;
            CoreAccount.UnifiedIdentityChanged += IdentityChanged;
            _engine.PropertyChanged += RoomChanged;
            _hero.PropertyChanged += RoomChanged;
            CompanionAskService.Instance.CardChanged += AskChanged;
        }
        Refresh();
    }

    public void Detach()
    {
        _observing = false;
        CoreAccount.UnifiedIdentityChanged -= IdentityChanged;
        _engine.PropertyChanged -= RoomChanged;
        _hero.PropertyChanged -= RoomChanged;
        CompanionAskService.Instance.CardChanged -= AskChanged;
        Attach(null);
    }

    private void IdentityChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Normal);

    private void RoomChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var name in RoomNames) Raise(name);
    }

    private void Attach(ChatSession? session)
    {
        if (ReferenceEquals(session, _session)) return;
        if (_session != null) _session.TurnsChanged -= Changed;
        _session = session;
        if (_session != null) _session.TurnsChanged += Changed;
    }

    private void Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Reconcile, DispatcherPriority.Normal);

    internal static bool Shows(TurnKind kind) => kind is TurnKind.UserChat or TurnKind.AssistantChat;

    private void Reconcile()
    {
        var next = (_session?.Turns ?? Array.Empty<CompanionTurn>()).Where(t => Shows(t.Kind)).ToArray();
        // Update only the changed suffix, preserving scroll position and selectable text.
        var common = 0;
        while (common < Turns.Count && common < next.Length && Turns[common].Id == next[common].Id) common++;
        while (Turns.Count > common) Turns.RemoveAt(Turns.Count - 1);
        var activities = next.Skip(common).Any(t => t.ActivityIds.Length > 0) ? CompanionEffects.Activities() : null;
        foreach (var t in next.Skip(common))
            Turns.Add((Line(t, Name, CompanionLinkIndex.FindMentionedTitle) with
            {
                Actions = activities == null ? Array.Empty<ConversationAction>() : t.ActivityIds
                    .Select(id => activities.FirstOrDefault(a => a.Id == id))
                    .Where(a => a?.Allowed == true)
                    .Select(a => new ConversationAction(t.Id, a!.Id, a.Label)).ToArray()
            }).WithAsk(t.AskCardId));
        Raise(nameof(HasNoTurns));
        Raise(nameof(HasTurns));
    }

    private void AskChanged(AskCard card)
    {
        void Update()
        {
            for (int i = 0; i < Turns.Count; i++)
                if (Turns[i].AskCardId == card.Id) Turns[i] = Turns[i].WithAsk(card.Id);
        }
        if (Dispatcher.UIThread.CheckAccess()) Update(); else Dispatcher.UIThread.Post(Update, DispatcherPriority.Normal);
    }

    public void AnswerAsk(ConversationAskChoice choice) => CompanionAskService.Instance.Answer(choice.CardId, choice.ChoiceId);

    /// <summary>One transcript line. Only a model reply gets a watch link: a title in the user's own
    /// message is them talking, and an app reply never named a video.</summary>
    internal static ConversationLine Line(CompanionTurn t, string name, Func<string?, CompanionLinkIndex.Entry?> findTitle)
    {
        var isUser = t.Kind == TurnKind.UserChat;
        var link = !isUser && t.Kind == TurnKind.AssistantChat && !t.IsApplicationReply ? findTitle(t.Text) : null;
        return new(t.Id, isUser ? Loc.Get("companion_v2_you") : name, t.Text, t.Utc.ToLocalTime().ToString("t"), isUser)
        { LinkTitle = link?.Title, LinkUrl = link?.Url };
    }

    public void OpenLink(ConversationLine line)
    {
        if (!line.HasLink || !CompanionLinkIndex.IsSanctioned(line.LinkUrl)) return;
        _ = Platform.AppUpdater.OpenUrl(AvatarTubeWindow.Live, line.LinkUrl!);
    }

    public void OpenActivity(ConversationAction action)
    {
        App.Brain?.EnsureCurrentAccount();
        if (!ReferenceEquals(_memoryOwner, App.Brain?.Memory)
            || _session?.Turns.Any(t => t.Id == action.TurnId && t.ActivityIds.Contains(action.Id)) != true) return;
        try
        {
            if (CompanionEffects.Activities().FirstOrDefault(a => a.Id == action.Id)?.TryOpen() == true) return;
        }
        catch (Exception ex) { Log.Warning("Companion activity failed: {Type}", ex.GetType().Name); }
        Notice = Loc.Get("companion_v2_activity_unavailable");
        CanRetry = false;
    }

    public void Start()
    {
        if (NeedsStart)
            _engine.Provider = (CoreSettings.Current?.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud) switch
            {
                AiProviderType.Local => Mode.LocalOllama,
                AiProviderType.OpenAiCompatible => Mode.Custom,
                _ => Mode.Cloud
            };
        Refresh();
        if (NeedsSignIn) StartSignIn?.Invoke();
    }

    /// <summary>The page's sign-in door (the engine's LoginCommand is null on this head).</summary>
    public Action? StartSignIn { get; set; }

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
                AvatarTubeWindow.Live?.ShowModerationRefusalBubble(result.Refusal.Source);
            }
            else if (result.IsAiGenerated || result.IsApplicationReply)
            {
                Draft = string.Empty;
                SpeakThroughTube(result.Text, result.IsAiGenerated);
                CompanionAskService.Instance.OfferForRequest(text);
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
            Log.Warning("Companion conversation failed: {Type}", ex.GetType().Name);
            Notice = Loc.Get("companion_v2_unavailable");
            CanRetry = true;
        }
        finally { _send = null; Busy = false; Refresh(); }
    }

    /// <summary>A reply typed on this page is also said through the avatar's speech bubble when the
    /// tube is showing. This page's send never passes through the tube's own input box, so the reply
    /// is spoken exactly once.</summary>
    private static void SpeakThroughTube(string? text, bool aiGenerated)
    {
        if (!ShouldSpeak(text, CoreSettings.Current?.AvatarEnabled == true)) return;
        var avatar = AvatarTubeWindow.Live;
        if (avatar == null) return;
        try
        {
            avatar.RunOnAvatar(() =>
            {
                if (!avatar.IsVisible) return;
                avatar.GigglePriority(text!, aiGenerated: aiGenerated);
            });
        }
        catch (Exception ex) { Log.Debug("Companion page reply not spoken: {Type}", ex.GetType().Name); }
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
