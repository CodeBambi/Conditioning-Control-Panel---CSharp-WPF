using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion.Asks;

public sealed record AskVideo(string Title, string Url);

/// <summary>A destination the user could open. <see cref="Allowed"/> is the live access check (tier, passes, unlocks).</summary>
public sealed record AskOption(string Id, string Label, Func<bool> Allowed)
{
    public bool IsAllowed { get { try { return Allowed(); } catch { return false; } } }
}

/// <summary>Everything the catalogue may offer, gathered by the glue. Tests pass fakes.</summary>
public sealed class AskSources
{
    public IReadOnlyList<AskVideo> Videos { get; init; } = Array.Empty<AskVideo>();
    public IReadOnlyCollection<string> LovedVideos { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> MehVideos { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AskOption> Games { get; init; } = Array.Empty<AskOption>();
    public IReadOnlyList<AskOption> Sessions { get; init; } = Array.Empty<AskOption>();
    public AskOption? Quests { get; init; }
    public IReadOnlyCollection<string> KnownTopics { get; init; } = Array.Empty<string>();
    /// <summary>Localisation lookup (key to text). Format arguments use {0}.</summary>
    public Func<string, string> Text { get; init; } = key => key;
}

/// <summary>
/// Builds cards from what the user can open right now. A kind with no valid action returns null,
/// so nothing locked is ever offered. Click time checks again in the glue.
/// </summary>
public static class AskCatalog
{
    public static readonly string[] Topics = { "colour", "time", "drop" };
    public const int QuestionVariants = 3;

    public static bool IsMeh(AskSources s, string title) =>
        s.MehVideos.Any(m => string.Equals(m, title, StringComparison.OrdinalIgnoreCase));

    private static bool IsLoved(AskSources s, string title) =>
        s.LovedVideos.Any(m => string.Equals(m, title, StringComparison.OrdinalIgnoreCase));

    private static string Line(AskSources s, string stem, Random rng, params object[] args)
    {
        var text = s.Text($"{stem}_{rng.Next(1, QuestionVariants + 1)}");
        return args.Length == 0 ? text : SafeFormat(text, args);
    }

    private static string SafeFormat(string text, object[] args)
    {
        try { return string.Format(text, args); } catch (FormatException) { return text + " " + string.Join(" ", args); }
    }

    private static AskChoice Choice(AskSources s, string id, string key, AskTone tone, AskAction action, string? target = null) =>
        new(id, s.Text(key), tone, action, target);

    public static AskCard? Build(AskKind kind, AskSources s, Random rng, DateTime nowUtc, string? excludeUrl = null) => kind switch
    {
        AskKind.Watch => BuildWatch(s, rng, nowUtc, excludeUrl),
        AskKind.Game => BuildGame(s, rng, nowUtc),
        AskKind.Session => BuildSession(s, rng, nowUtc),
        AskKind.Quests => BuildQuests(s, rng, nowUtc),
        AskKind.GetToKnow => BuildGetToKnow(s, rng, nowUtc),
        _ => null
    };

    /// <summary>The next video to offer: never a "meh" one, loved ones first when there are any.</summary>
    public static AskVideo? PickVideo(AskSources s, Random rng, string? excludeUrl = null)
    {
        var pool = s.Videos.Where(v => !string.IsNullOrWhiteSpace(v.Url) && !IsMeh(s, v.Title)
            && !string.Equals(v.Url, excludeUrl, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (pool.Length == 0) return null;
        var loved = pool.Where(v => IsLoved(s, v.Title)).ToArray();
        if (loved.Length > 0 && rng.NextDouble() < 0.6) return loved[rng.Next(loved.Length)];
        return pool[rng.Next(pool.Length)];
    }

    public static IReadOnlyList<AskChoice> WatchChoices(AskSources s, AskVideo video) => new[]
    {
        Choice(s, "watch", "companion_ask_watch_yes", AskTone.Yes, AskAction.Watch, video.Url),
        Choice(s, "another", "companion_ask_another", AskTone.Neutral, AskAction.Another),
        Choice(s, "no", "companion_ask_not_now", AskTone.No, AskAction.Decline),
    };

    public static string WatchQuestion(AskSources s, AskVideo video, Random rng) =>
        Line(s, "companion_ask_watch_q", rng, video.Title);

    private static AskCard? BuildWatch(AskSources s, Random rng, DateTime now, string? excludeUrl)
    {
        var video = PickVideo(s, rng, excludeUrl);
        if (video == null) return null;
        return new AskCard(AskKind.Watch, WatchQuestion(s, video, rng), WatchChoices(s, video), now, video.Title, video.Url);
    }

    private static AskCard? BuildGame(AskSources s, Random rng, DateTime now)
    {
        var games = s.Games.Where(g => g.IsAllowed).ToArray();
        if (games.Length == 0) return null;
        var game = games[rng.Next(games.Length)];
        return new AskCard(AskKind.Game, Line(s, "companion_ask_game_q", rng, game.Label), new[]
        {
            Choice(s, "play", "companion_ask_game_yes", AskTone.Yes, AskAction.Game, game.Id),
            Choice(s, "no", "companion_ask_not_now", AskTone.No, AskAction.Decline),
        }, now, game.Label);
    }

    private static AskCard? BuildSession(AskSources s, Random rng, DateTime now)
    {
        var sessions = s.Sessions.Where(o => o.IsAllowed).OrderBy(_ => rng.Next()).Take(2).ToArray();
        if (sessions.Length == 0) return null;
        var choices = sessions.Select((o, i) => new AskChoice("session" + i, o.Label,
            i == 0 ? AskTone.Yes : AskTone.Neutral, AskAction.Session, o.Id)).ToList();
        choices.Add(Choice(s, "no", "companion_ask_not_now", AskTone.No, AskAction.Decline));
        return new AskCard(AskKind.Session, Line(s, "companion_ask_session_q", rng), choices, now);
    }

    private static AskCard? BuildQuests(AskSources s, Random rng, DateTime now)
    {
        if (s.Quests is not { IsAllowed: true } quests) return null;
        return new AskCard(AskKind.Quests, Line(s, "companion_ask_quests_q", rng), new[]
        {
            Choice(s, "quests", "companion_ask_quests_yes", AskTone.Yes, AskAction.Quests, quests.Id),
            Choice(s, "later", "companion_ask_later", AskTone.Neutral, AskAction.Later),
        }, now);
    }

    /// <summary>"Did you like X?" after something finished. Subject is what it was about.</summary>
    public static AskCard BuildFeedback(AskSources s, string subject, string? subjectUrl, Random rng, DateTime now) =>
        new(AskKind.Feedback, Line(s, "companion_ask_feedback_q", rng, subject), new[]
        {
            Choice(s, "loved", "companion_ask_loved", AskTone.Yes, AskAction.Loved),
            Choice(s, "meh", "companion_ask_meh", AskTone.No, AskAction.Meh),
            Choice(s, "skip", "companion_ask_skip", AskTone.Neutral, AskAction.Skip),
        }, now, subject, subjectUrl);

    private static AskCard? BuildGetToKnow(AskSources s, Random rng, DateTime now)
    {
        var open = Topics.Where(t => !s.KnownTopics.Contains(t)).ToArray();
        if (open.Length == 0) return null;
        var topic = open[rng.Next(open.Length)];
        AskChoice Answer(string value, AskTone tone) =>
            Choice(s, value, $"companion_ask_know_{topic}_{value}", tone, AskAction.Answer, value);
        AskChoice Else() => Choice(s, "else", "companion_ask_something_else", AskTone.Neutral, AskAction.FocusChat);
        IReadOnlyList<AskChoice> choices = topic switch
        {
            // The joke: pink is always the answer, so it goes first and wears pink.
            "colour" => new[] { Answer("pink", AskTone.Pink), Answer("purple", AskTone.Neutral), Else() },
            "time" => new[] { Answer("morning", AskTone.Neutral), Answer("night", AskTone.Pink) },
            _ => new[] { Answer("video", AskTone.Pink), Answer("audio", AskTone.Neutral), Else() },
        };
        return new AskCard(AskKind.GetToKnow, s.Text($"companion_ask_know_{topic}_q"), choices, now, topic);
    }

    /// <summary>The short stock reaction to a get-to-know answer.</summary>
    public static string Reaction(AskSources s, string topic, string value) => s.Text($"companion_ask_know_{topic}_{value}_reply");
}
