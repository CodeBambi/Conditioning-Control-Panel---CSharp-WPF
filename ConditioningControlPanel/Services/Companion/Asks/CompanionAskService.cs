using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Services.Companion.Asks;

/// <summary>
/// UI glue for ask cards: gathers what the user can open right now, paces unprompted cards,
/// puts one card on both surfaces (tube bubble + chat page) and performs the answer after
/// checking access again. The rules live in <see cref="AskPlanner"/> and <see cref="AskCatalog"/>.
/// </summary>
internal sealed class CompanionAskService
{
    internal static CompanionAskService Instance { get; } = new();

    private static readonly AskKind[] Unprompted = { AskKind.Watch, AskKind.Game, AskKind.Session, AskKind.Quests, AskKind.GetToKnow };
    private static readonly TimeSpan WatchFeedbackDelay = TimeSpan.FromMinutes(10);

    private readonly AskPlanner _planner = new();
    private readonly Random _rng = new();
    private readonly Dictionary<string, AskCard> _cards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ourTurns = new(StringComparer.Ordinal);
    private DispatcherTimer? _timer;
    private AskCard? _current;
    private (string Subject, string? Url, DateTime DueUtc)? _pendingFeedback;

    /// <summary>A card was shown, answered, swapped or expired. Raised on the UI thread.</summary>
    internal event Action<AskCard>? CardChanged;
    /// <summary>"Something else": the surface that can should focus its chat box.</summary>
    internal event Action? FocusChatRequested;

    internal AskCard? Find(string? id) => id != null && _cards.TryGetValue(id, out var card) ? card : null;

    internal void Start()
    {
        if (_timer != null) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(60) };
            _timer.Tick += (_, _) => { try { Tick(); } catch (Exception ex) { App.Logger?.Debug("Ask tick failed: {E}", ex.Message); } };
            _timer.Start();
        }, DispatcherPriority.Normal);
    }

    private static bool Enabled => App.Settings?.Current?.CompanionAsksEnabled != false;
    private static bool CompanionOn => App.Settings?.Current?.AvatarEnabled == true && App.Brain?.Session != null;

    private void Tick()
    {
        var now = DateTime.UtcNow;
        if (_current != null && !_current.IsOpen(now)) { var gone = _current; _current = null; CardChanged?.Invoke(gone); }
        if (_current != null) return;
        var gate = new AskGate(Enabled, CompanionOn, IsBusy(), LastUserChatUtc());
        if (!_planner.MayAsk(gate)) return;

        var sources = Sources();
        AskCard? card = null;
        if (_pendingFeedback is { } fb && now >= fb.DueUtc && _planner.KindReady(AskKind.Feedback))
        {
            _pendingFeedback = null;
            card = AskCatalog.BuildFeedback(sources, fb.Subject, fb.Url, _rng, now);
        }
        for (int tries = 0; card == null && tries < 4; tries++)
        {
            if (_planner.PickKind(Unprompted, _rng) is not AskKind kind) return;
            card = AskCatalog.Build(kind, sources, _rng, now);
        }
        if (card != null) Show(card, unprompted: true);
    }

    /// <summary>The user asked for ideas: show the matching card at once, pacing aside.</summary>
    internal void ShowNow(AskKind kind)
    {
        if (!Enabled || !CompanionOn || IsBusy()) return;
        var card = AskCatalog.Build(kind, Sources(), _rng, DateTime.UtcNow);
        if (card == null && kind == AskKind.Game) card = AskCatalog.Build(AskKind.Session, Sources(), _rng, DateTime.UtcNow);
        if (card != null) Show(card, unprompted: false);
    }

    /// <summary>
    /// The user's own chat line asked for ideas (ConversationDelivery.WantsMedia / WantsActivity):
    /// the matching card follows the reply once it has had a moment on screen.
    /// </summary>
    internal void OfferForRequest(string? userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return;
        AskKind? kind = ConversationDelivery.WantsMedia(userText) ? AskKind.Watch
            : ConversationDelivery.WantsActivity(userText) ? AskKind.Game : null;
        if (kind is not AskKind k) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(() =>
        {
            var delay = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = RequestDelay };
            delay.Tick += (_, _) => { delay.Stop(); try { ShowNow(k); } catch (Exception ex) { App.Logger?.Debug("Ask request failed: {E}", ex.Message); } };
            delay.Start();
        }, DispatcherPriority.Normal);
    }

    private static readonly TimeSpan RequestDelay = TimeSpan.FromSeconds(6);

    /// <summary>Something finished (a session, a video): ask about it later when it is quiet.</summary>
    internal void NoteFinished(string subject, string? url = null, TimeSpan? delay = null)
    {
        if (string.IsNullOrWhiteSpace(subject)) return;
        _pendingFeedback = (subject.Trim(), url, DateTime.UtcNow + (delay ?? TimeSpan.FromMinutes(1)));
    }

    private void Show(AskCard card, bool unprompted)
    {
        if (_current is { } old && old.IsOpen(DateTime.UtcNow)) return;
        _current = card;
        _cards[card.Id] = card;
        if (_cards.Count > 30) foreach (var stale in _cards.Values.OrderBy(c => c.CreatedUtc).Take(_cards.Count - 30).ToArray()) _cards.Remove(stale.Id);
        _planner.NoteShown(unprompted);
        AppendTurn(TurnKind.AssistantChat, card.Question, card.Id);
        App.AvatarWindow?.RunOnAvatar(() => App.AvatarWindow?.ShowAskCard(card));
        CardChanged?.Invoke(card);
    }

    private void AppendTurn(TurnKind kind, string text, string? cardId = null)
    {
        var session = App.Brain?.Session;
        if (session == null || string.IsNullOrWhiteSpace(text)) return;
        var turn = CompanionTurn.Create(kind, text) with { IsApplicationReply = kind == TurnKind.AssistantChat, AskCardId = cardId };
        _ourTurns.Add(turn.Id);
        session.Append(turn);
    }

    private void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AppendTurn(TurnKind.AssistantChat, text);
        var avatar = App.AvatarWindow;
        if (avatar != null && App.Settings?.Current?.AvatarEnabled == true)
            avatar.RunOnAvatar(() => { if (avatar.IsVisible) avatar.GigglePriority(text, aiGenerated: false); });
    }

    /// <summary>One answer from either surface. Resolves the card for both, then acts after re-checking access.</summary>
    internal void Answer(string cardId, string choiceId)
    {
        var now = DateTime.UtcNow;
        if (Find(cardId) is not { } card || card.Find(choiceId) is not { } choice || !card.IsOpen(now)) return;
        var sources = Sources();

        if (choice.Action == AskAction.Another)
        {
            var next = AskCatalog.PickVideo(sources, _rng, card.SubjectUrl);
            if (next == null) return;
            if (card.Swap(AskCatalog.WatchQuestion(sources, next, _rng), AskCatalog.WatchChoices(sources, next), next.Title, next.Url, now))
            {
                App.AvatarWindow?.RunOnAvatar(() => App.AvatarWindow?.ShowAskCard(card));
                CardChanged?.Invoke(card);
            }
            return;
        }

        if (!card.Resolve(choiceId, now)) return;
        if (ReferenceEquals(_current, card)) _current = null;
        AppendTurn(TurnKind.UserChat, choice.Label);
        _planner.NoteAnswer(card.Kind, AskCard.IsDecline(choice.Action) || choice.Action == AskAction.Skip);
        CardChanged?.Invoke(card);

        bool opened = true;
        try
        {
            switch (choice.Action)
            {
                case AskAction.Watch:
                    opened = CompanionLinkIndex.IsSanctioned(choice.Target);
                    if (opened)
                    {
                        CompanionLinkLauncher.Open(choice.Target);
                        if (card.Subject != null) NoteFinished(card.Subject, choice.Target, WatchFeedbackDelay);
                    }
                    break;
                case AskAction.Game:
                case AskAction.Quests:
                    opened = CompanionActivities.Find(choice.Target ?? "")?.TryOpen() == true;
                    break;
                case AskAction.Session:
                    opened = App.MainWindowRef?.StartSessionFromCompanion(choice.Target ?? "") == true;
                    break;
                case AskAction.Loved:
                case AskAction.Meh:
                    RecordFeedback(card, choice.Action == AskAction.Loved);
                    break;
                case AskAction.Answer:
                    RecordAnswer(card.Subject ?? "", choice.Target ?? "", sources);
                    break;
                case AskAction.FocusChat:
                    FocusChatRequested?.Invoke();
                    break;
            }
        }
        catch (Exception ex)
        {
            opened = false;
            App.Logger?.Warning("Ask card action failed: {Type}", ex.GetType().Name);
        }
        if (!opened) Say(Loc.Get("companion_v2_activity_unavailable"));
    }

    private static void RecordFeedback(AskCard card, bool loved)
    {
        var subject = card.Subject;
        if (string.IsNullOrWhiteSpace(subject)) return;
        var settings = App.Settings?.Current;
        if (settings != null && card.SubjectUrl != null)
        {
            var into = loved ? settings.CompanionAskLovedVideos : settings.CompanionAskMehVideos;
            var other = loved ? settings.CompanionAskMehVideos : settings.CompanionAskLovedVideos;
            other.RemoveAll(t => string.Equals(t, subject, StringComparison.OrdinalIgnoreCase));
            if (!into.Contains(subject, StringComparer.OrdinalIgnoreCase)) into.Add(subject);
            try { App.Settings?.Save(); } catch { }
        }
        AddFact(loved ? $"Loved \"{subject}\"" : $"Did not enjoy \"{subject}\"");
    }

    private void RecordAnswer(string topic, string value, AskSources sources)
    {
        var settings = App.Settings?.Current;
        if (settings != null && !settings.CompanionAskKnownTopics.Contains(topic))
        {
            settings.CompanionAskKnownTopics.Add(topic);
            try { App.Settings?.Save(); } catch { }
        }
        var fact = topic switch
        {
            "colour" => $"Favourite colour: {value}",
            "time" => $"More of a {value} person",
            _ => $"Favourite way to drop: {value}",
        };
        AddFact(fact);
        Say(AskCatalog.Reaction(sources, topic, value));
    }

    private static void AddFact(string text)
    {
        try { App.Brain?.Memory?.AddFact(text, MemoryFactKind.Preference, 0.6, MemoryFact.SourceApp); }
        catch (Exception ex) { App.Logger?.Debug("Ask fact not stored: {E}", ex.Message); }
    }

    private DateTime? LastUserChatUtc()
    {
        var turns = App.Brain?.Session?.Turns;
        return turns?.LastOrDefault(t => t.Kind == TurnKind.UserChat && !_ourTurns.Contains(t.Id))?.Utc;
    }

    /// <summary>Anything that owns the user right now: session, game, video, lock card, takeover, lockdown, modal.</summary>
    private static bool IsBusy()
    {
        try
        {
            if (App.StartupLadder?.IsQuiet == true || App.IsSessionRunning) return true;
            if (App.MainWindowRef?.CompanionSessionRunning == true) return true;
            if (App.Video?.IsPlaying == true) return true;
            if (App.BrowserMedia?.IsPlaying == true || App.BrowserMedia?.IsTakeover == true) return true;
            if (App.Lockdown?.IsActive == true) return true;
            if (App.Mantra?.IsActive == true) return true;
            if (App.RemoteControl?.ControllerConnected == true) return true;
            if (Application.Current?.Windows.OfType<Window>().Any(w => w.IsVisible && w is LockCardWindow) == true) return true;
            return false;
        }
        catch { return true; }
    }

    private static AskSources Sources()
    {
        var activities = CompanionActivities.Current();
        var settings = App.Settings?.Current;
        return new AskSources
        {
            Videos = CompanionLinkIndex.CurrentEntries().Select(e => new AskVideo(e.Title, e.Url)).ToArray(),
            LovedVideos = settings?.CompanionAskLovedVideos.ToArray() ?? Array.Empty<string>(),
            MehVideos = settings?.CompanionAskMehVideos.ToArray() ?? Array.Empty<string>(),
            Games = activities.Where(a => a.Id.StartsWith("game.", StringComparison.Ordinal))
                .Select(a => new AskOption(a.Id, a.Label, () => a.Allowed)).ToArray(),
            Sessions = App.MainWindowRef?.CompanionSessionOptions() ?? Array.Empty<AskOption>(),
            Quests = activities.FirstOrDefault(a => a.Id == "page.quests") is { } q ? new AskOption(q.Id, q.Label, () => q.Allowed) : null,
            KnownTopics = settings?.CompanionAskKnownTopics.ToArray() ?? Array.Empty<string>(),
            Text = Loc.Get,
        };
    }
}
