using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion.Asks;

/// <summary>What the companion is asking about. The app authors every card; the model never does.</summary>
public enum AskKind { Watch, Game, Session, Quests, Feedback, GetToKnow }

/// <summary>Button colour: green = do it, red = no, lilac = third option, pink = the answer.</summary>
public enum AskTone { Yes, No, Neutral, Pink }

/// <summary>What a click does. The service re-checks access before anything opens.</summary>
public enum AskAction { Watch, Another, Decline, Later, Game, Session, Quests, Answer, FocusChat, Loved, Meh, Skip }

/// <summary>Button colours per tone, shared by the tube bubble and the chat page.</summary>
public static class AskTones
{
    public static (string Background, string Border, string Foreground) Colours(AskTone tone) => tone switch
    {
        AskTone.Yes => ("#2E7D5B", "#7FE0B2", "#FFFFFF"),
        AskTone.No => ("#8E2F48", "#F08AA6", "#FFFFFF"),
        AskTone.Pink => ("#C82D86", "#FF9AD5", "#FFFFFF"),
        _ => ("#4A3A66", "#C5A4EE", "#FFF8FF"),
    };
}

public sealed record AskChoice(string Id, string Label, AskTone Tone, AskAction Action, string? Target = null);

public enum AskState { Open, Answered, Expired }

/// <summary>
/// One question with 2-3 answers, shown under the tube bubble and in the chat page at once.
/// Answering on either surface resolves it for both. Plain data, no WPF.
/// </summary>
public sealed class AskCard
{
    public static readonly TimeSpan Life = TimeSpan.FromMinutes(3);

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public AskKind Kind { get; }
    public string Question { get; private set; }
    /// <summary>The thing the card is about (a video title, a session name, a topic), if any.</summary>
    public string? Subject { get; private set; }
    public string? SubjectUrl { get; private set; }
    public IReadOnlyList<AskChoice> Choices { get; private set; }
    public DateTime CreatedUtc { get; }
    public DateTime ExpiresUtc { get; private set; }
    public AskState State { get; private set; } = AskState.Open;
    public string? ChosenId { get; private set; }

    public AskCard(AskKind kind, string question, IReadOnlyList<AskChoice> choices, DateTime nowUtc,
        string? subject = null, string? subjectUrl = null)
    {
        if (choices == null || choices.Count == 0) throw new ArgumentException("A card needs a choice.", nameof(choices));
        Kind = kind;
        Question = question;
        Choices = choices.Take(3).ToArray();
        Subject = subject;
        SubjectUrl = subjectUrl;
        CreatedUtc = nowUtc;
        ExpiresUtc = nowUtc + Life;
    }

    public bool IsOpen(DateTime nowUtc)
    {
        if (State == AskState.Open && nowUtc >= ExpiresUtc) State = AskState.Expired;
        return State == AskState.Open;
    }

    public AskChoice? Find(string choiceId) => Choices.FirstOrDefault(c => c.Id == choiceId);

    /// <summary>Resolves the card with <paramref name="choiceId"/>. False when it is already closed or the id is unknown.</summary>
    public bool Resolve(string choiceId, DateTime nowUtc)
    {
        if (!IsOpen(nowUtc) || Find(choiceId) == null) return false;
        State = AskState.Answered;
        ChosenId = choiceId;
        return true;
    }

    /// <summary>"Another one": the same card, new subject, the clock restarts.</summary>
    public bool Swap(string question, IReadOnlyList<AskChoice> choices, string? subject, string? subjectUrl, DateTime nowUtc)
    {
        if (!IsOpen(nowUtc) || choices == null || choices.Count == 0) return false;
        Question = question;
        Choices = choices.Take(3).ToArray();
        Subject = subject;
        SubjectUrl = subjectUrl;
        ExpiresUtc = nowUtc + Life;
        return true;
    }

    public static bool IsDecline(AskAction action) => action is AskAction.Decline or AskAction.Later;
}
