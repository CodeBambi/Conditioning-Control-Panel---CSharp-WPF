using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Brain;
using Serilog;

namespace ConditioningControlPanel.Services.Companion.Asks;

/// <summary>
/// UI glue for ask cards: gathers what the user can open right now, paces unprompted cards,
/// puts one card on both surfaces (tube bubble + chat page) and performs the answer after
/// checking access again. The rules live in <see cref="AskPlanner"/> and <see cref="AskCatalog"/>.
/// <para>Head-owned pieces are the static seams below; every head seeds them, unseeded is "not here".
/// Timers tick on the thread pool and hop to the UI thread through <see cref="CoreDispatch"/>.</para>
/// </summary>
internal sealed class CompanionAskService
{
    /// <summary>The head's CompanionBrain (WPF/Avalonia App.Brain).</summary>
    internal static volatile Func<CompanionBrain?>? BrainProvider;
    /// <summary>Tube: say the card's question with its answers under it (UI thread).</summary>
    internal static volatile Action<AskCard>? ShowCardSurface;
    /// <summary>Tube: say an application line, unbadged (UI thread).</summary>
    internal static volatile Action<string>? SaySurface;
    /// <summary>Anything that owns the user right now (session, video, lock card, lockdown, ...).</summary>
    internal static volatile Func<bool>? BusyProvider;
    /// <summary>Sessions the companion may offer (WPF MainWindow.CompanionSessionOptions).</summary>
    internal static volatile Func<IReadOnlyList<AskOption>>? SessionOptions;
    /// <summary>Start a session by id; false when it could not (WPF MainWindow.StartSessionFromCompanion).</summary>
    internal static volatile Func<string, bool>? StartSession;
    /// <summary>Open a sanctioned watch link (WPF CompanionLinkLauncher.Open).</summary>
    internal static volatile Action<string?>? OpenLink;

    internal static CompanionAskService Instance { get; } = new();

    private static readonly AskKind[] Unprompted = { AskKind.Watch, AskKind.Game, AskKind.Session, AskKind.Quests, AskKind.GetToKnow };
    private static readonly TimeSpan WatchFeedbackDelay = TimeSpan.FromMinutes(10);

    private readonly AskPlanner _planner = new();
    private readonly Random _rng = new();
    private readonly Dictionary<string, AskCard> _cards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ourTurns = new(StringComparer.Ordinal);
    private Timer? _timer;
    private volatile bool _stopped;
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
        _stopped = false;
        var every = TimeSpan.FromSeconds(60);
        _timer = new Timer(_ => PostUnlessStopped(() =>
        {
            try { Tick(); } catch (Exception ex) { Log.Debug("Ask tick failed: {E}", ex.Message); }
        }), null, every, every);
    }

    /// <summary>On exit, before the dispatcher goes: the pool timer outlives it, and after shutdown
    /// CoreDispatch runs a posted tick in place (same rule as DescentCountdownService).</summary>
    internal void Stop()
    {
        _stopped = true;
        _timer?.Dispose();
        _timer = null;
    }

    private void PostUnlessStopped(Action action)
    {
        if (!_stopped && CoreDispatch.HasUiThread) CoreDispatch.Post(() => { if (!_stopped) action(); });
    }

    private static CompanionBrain? Brain => BrainProvider?.Invoke();
    private static bool Enabled => CoreSettings.Current.CompanionAsksEnabled != false;
    private static bool CompanionOn => CoreSettings.Current.AvatarEnabled && Brain?.Session != null;

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
        if (BuildFor(kind) is { } card) Show(card, unprompted: false);
    }

    private AskCard? BuildFor(AskKind kind)
    {
        var card = AskCatalog.Build(kind, Sources(), _rng, DateTime.UtcNow);
        if (card == null && kind == AskKind.Game) card = AskCatalog.Build(AskKind.Session, Sources(), _rng, DateTime.UtcNow);
        return card;
    }

    private static AskKind? RequestedKind(string? userText) =>
        string.IsNullOrWhiteSpace(userText) ? null
        : ConversationDelivery.WantsMedia(userText) ? AskKind.Watch
        : ConversationDelivery.WantsActivity(userText) ? AskKind.Game : null;

    /// <summary>True when <see cref="OfferForRequest"/> would put a card up for this line right now (the
    /// head has something to build it from), so the model may be told one follows.</summary>
    internal bool CanOfferFor(string? userText) =>
        RequestedKind(userText) is AskKind k && Enabled && CompanionOn && !IsBusy() && BuildFor(k) != null;

    /// <summary>
    /// The user's own chat line asked for ideas (ConversationDelivery.WantsMedia / WantsActivity):
    /// the matching card follows the reply once it has had a moment on screen.
    /// </summary>
    internal void OfferForRequest(string? userText)
    {
        if (RequestedKind(userText) is not AskKind k) return;
        _ = Task.Delay(RequestDelay).ContinueWith(_ => PostUnlessStopped(() =>
        {
            try { ShowNow(k); } catch (Exception ex) { Log.Debug("Ask request failed: {E}", ex.Message); }
        }), TaskScheduler.Default);
    }

    internal static TimeSpan RequestDelay { get; set; } = TimeSpan.FromSeconds(6);   // settable for tests only

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
        ShowCardSurface?.Invoke(card);
        CardChanged?.Invoke(card);
    }

    private void AppendTurn(TurnKind kind, string text, string? cardId = null)
    {
        var session = Brain?.Session;
        if (session == null || string.IsNullOrWhiteSpace(text)) return;
        var turn = CompanionTurn.Create(kind, text) with { IsApplicationReply = kind == TurnKind.AssistantChat, AskCardId = cardId };
        _ourTurns.Add(turn.Id);
        session.Append(turn);
    }

    private void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AppendTurn(TurnKind.AssistantChat, text);
        if (CoreSettings.Current.AvatarEnabled) SaySurface?.Invoke(text);
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
                ShowCardSurface?.Invoke(card);
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
                        OpenLink?.Invoke(choice.Target);
                        if (card.Subject != null) NoteFinished(card.Subject, choice.Target, WatchFeedbackDelay);
                    }
                    break;
                case AskAction.Game:
                case AskAction.Quests:
                    opened = CompanionBrain.ActivitiesProvider?.Invoke().FirstOrDefault(a => a.Id == choice.Target)?.TryOpen() == true;
                    break;
                case AskAction.Session:
                    opened = StartSession?.Invoke(choice.Target ?? "") == true;
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
            Log.Warning("Ask card action failed: {Type}", ex.GetType().Name);
        }
        if (!opened) Say(Loc.Get("companion_v2_activity_unavailable"));
    }

    private static void RecordFeedback(AskCard card, bool loved)
    {
        var subject = card.Subject;
        if (string.IsNullOrWhiteSpace(subject)) return;
        var settings = CoreSettings.Current;
        if (card.SubjectUrl != null)
        {
            var into = loved ? settings.CompanionAskLovedVideos : settings.CompanionAskMehVideos;
            var other = loved ? settings.CompanionAskMehVideos : settings.CompanionAskLovedVideos;
            other.RemoveAll(t => string.Equals(t, subject, StringComparison.OrdinalIgnoreCase));
            if (!into.Contains(subject, StringComparer.OrdinalIgnoreCase)) into.Add(subject);
            try { CoreSettings.Save(); } catch { }
        }
        AddFact(loved ? $"Loved \"{subject}\"" : $"Did not enjoy \"{subject}\"");
    }

    private void RecordAnswer(string topic, string value, AskSources sources)
    {
        var settings = CoreSettings.Current;
        if (!settings.CompanionAskKnownTopics.Contains(topic))
        {
            settings.CompanionAskKnownTopics.Add(topic);
            try { CoreSettings.Save(); } catch { }
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
        try { Brain?.Memory?.AddFact(text, MemoryFactKind.Preference, 0.6, MemoryFact.SourceApp); }
        catch (Exception ex) { Log.Debug("Ask fact not stored: {E}", ex.Message); }
    }

    private DateTime? LastUserChatUtc()
    {
        var turns = Brain?.Session?.Turns;
        return turns?.LastOrDefault(t => t.Kind == TurnKind.UserChat && !_ourTurns.Contains(t.Id))?.Utc;
    }

    private static bool IsBusy()
    {
        try { return BusyProvider?.Invoke() == true; }
        catch { return true; }
    }

    private static AskSources Sources()
    {
        var activities = CompanionBrain.ActivitiesProvider?.Invoke() ?? Array.Empty<CompanionActivity>();
        var settings = CoreSettings.Current;
        return new AskSources
        {
            // The active mod's own pool only. CompanionLinkIndex also sanctions the Bambi catalogue
            // and knowledge-base links, so offering from it put Bambi videos in CCP Default.
            Videos = (CoreMods.Service?.GetVideoLinks() ?? new Dictionary<string, string>())
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => new AskVideo(kv.Key, kv.Value)).ToArray(),
            LovedVideos = settings.CompanionAskLovedVideos.ToArray(),
            MehVideos = settings.CompanionAskMehVideos.ToArray(),
            Games = activities.Where(a => a.Id.StartsWith("game.", StringComparison.Ordinal))
                .Select(a => new AskOption(a.Id, a.Label, () => a.Allowed)).ToArray(),
            Sessions = SessionOptions?.Invoke() ?? Array.Empty<AskOption>(),
            Quests = activities.FirstOrDefault(a => a.Id == "page.quests") is { } q ? new AskOption(q.Id, q.Label, () => q.Allowed) : null,
            QuestsOpen = ConversationDelivery.QuestsOpen(),
            KnownTopics = settings.CompanionAskKnownTopics.ToArray(),
            Text = Loc.Get,
        };
    }
}
