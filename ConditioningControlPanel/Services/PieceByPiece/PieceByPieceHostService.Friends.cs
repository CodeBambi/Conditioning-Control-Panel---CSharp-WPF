using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// Chess from the friends drawer. Two intents, one frame each way:
/// <list type="bullet">
/// <item>host -&gt; page: <c>{ type: 'pbp:friend', mode: 'challenge', friendId }</c> - challenge
/// this friend (the server takes a friend's unified id as a target, friends both ways only);
/// the page answers <c>{ type: 'pbp:friend-challenge', friendId, challengeId }</c> with the
/// challenge id, or <c>challengeId: null</c> when it could not make one. The drawer then sends
/// that id as a friends invite (<c>destination: chess</c>).</item>
/// <item>host -&gt; page: <c>{ type: 'pbp:friend', mode: 'accept', challengeId }</c> - the
/// friend's side: take that challenge up and go straight to the board.</item>
/// <item>host -&gt; page: <c>{ type: 'pbp:friend', mode: 'spectate', matchId }</c> - the Lobby's
/// Watch on a public match, to a board that is already up. A fresh board gets the match id as
/// <c>spectateMatchId</c> on its <c>pbp:settings</c> frame instead (never both).</item>
/// </list>
/// The intent waits for the page's identity frame on a fresh launch, and goes out at once to a
/// board that is already up.
/// </summary>
internal static partial class PieceByPieceHostService
{
    private static JObject? _friendIntent;
    private static string? _spectateInit;
    private static TaskCompletionSource<string?>? _friendChallenge;
    private static string? _friendChallengeFor;

    /// <summary>
    /// Open the board (or use the open one) and challenge <paramref name="friendId"/>. Resolves
    /// with the server's challenge id, or null when the page could not make one in time. One
    /// at a time: a second call while one is out answers the same task.
    /// </summary>
    public static Task<string?> ChallengeFriendAsync(string friendId, TimeSpan timeout)
    {
        if (string.IsNullOrEmpty(friendId)) return Task.FromResult<string?>(null);
        if (_friendChallenge is { Task.IsCompleted: false } live && _friendChallengeFor == friendId) return live.Task;
        _friendChallenge?.TrySetResult(null);

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _friendChallenge = tcs;
        _friendChallengeFor = friendId;
        Intend(new JObject { ["type"] = "pbp:friend", ["mode"] = "challenge", ["friendId"] = friendId });
        if (_host == null) { tcs.TrySetResult(null); return tcs.Task; }

        _ = Task.Delay(timeout).ContinueWith(_ => tcs.TrySetResult(null), TaskScheduler.Default);
        return tcs.Task;
    }

    /// <summary>The friend's side of a chess invite: open the board on that challenge.</summary>
    public static void JoinFriendChallenge(string challengeId)
    {
        if (!InviteDestination.IsChallengeId(challengeId)) return;
        Intend(new JObject { ["type"] = "pbp:friend", ["mode"] = "accept", ["challengeId"] = challengeId });
    }

    /// <summary>The Lobby's Join on a chess open table: open the board and sit at the table of
    /// that <c>p_</c> id (the page runs its own lobby.join, refusals included).</summary>
    public static void JoinOpenTable(string target)
    {
        if (!IsTableId(target)) return;
        Intend(new JObject { ["type"] = "pbp:friend", ["mode"] = "join", ["target"] = target });
    }

    /// <summary>The Lobby's Watch on a public chess match: open the board as a spectator of
    /// <paramref name="matchId"/> (the page runs the watch routes; the server refuses a seat or a
    /// match that is not watchable). A fresh board takes it as the settings frame's
    /// <c>spectateMatchId</c>; a board already up takes a <c>pbp:friend</c> spectate frame.</summary>
    public static void HostSpectate(string matchId)
    {
        if (!PbpWatchRules.IsMatchId(matchId)) return;
        if (_host != null)
        {
            Intend(new JObject { ["type"] = "pbp:friend", ["mode"] = "spectate", ["matchId"] = matchId });
            return;
        }
        _friendIntent = null;
        _spectateInit = matchId;
        Launch();
        if (_host == null) _spectateInit = null;
    }

    /// <summary>The fresh board's spectate target, handed out once (the settings frame).</summary>
    private static string? TakeSpectateInit()
    {
        var id = _spectateInit;
        _spectateInit = null;
        return id;
    }

    /// <summary>page -&gt; host <c>pbp:setting</c>: the page's own toggles that the host stores.</summary>
    private static void OnPageSetting(JObject o)
    {
        if (!PbpWatchRules.TryRead(o, out var key, out var value)) return;
        var s = App.Settings?.Current;
        if (!PbpWatchRules.Apply(s, key, value)) return;
        try { App.Settings?.Save(); } catch (Exception ex) { App.Logger?.Debug("PieceByPiece: setting save: {E}", ex.Message); }
        App.Logger?.Information("PieceByPiece: {Key} = {Value}", key, value);
    }

    /// <summary>The Lobby's "Host a chess table": open the board and list a table at the page's
    /// default clock, waiting for someone to sit down.</summary>
    public static void HostOpenTable() => Intend(new JObject { ["type"] = "pbp:friend", ["mode"] = "host" });

    /// <summary>A chess lobby row id: the server's opaque <c>p_</c> id, letters, digits, _ and -.</summary>
    internal static bool IsTableId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.StartsWith("p_", System.StringComparison.Ordinal)
        && id.Skip(2).All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-') && id.Length > 2;

    private static void Intend(JObject intent)
    {
        bool fresh = _host == null;
        _friendIntent = intent;
        Launch();
        if (_host == null) { _friendIntent = null; return; }
        // A board already up and talking takes it now; a fresh one after its identity frame.
        if (!fresh && _identityPosted) PostFriendIntent();
    }

    private static void PostFriendIntent()
    {
        var intent = _friendIntent;
        if (intent == null || _host == null) return;
        _friendIntent = null;
        try { _host.Post(intent); }
        catch (Exception ex) { App.Logger?.Debug("PieceByPiece: friend intent post failed: {E}", ex.Message); }
    }

    private static void OnFriendChallenge(JObject o)
    {
        var id = (string?)o["challengeId"];
        var tcs = _friendChallenge;
        if (tcs == null) return;
        tcs.TrySetResult(InviteDestination.IsChallengeId(id) ? id : null);
    }

    private static void DropFriendIntent()
    {
        _friendIntent = null;
        _spectateInit = null;
        _friendChallenge?.TrySetResult(null);
        _friendChallenge = null;
        _friendChallengeFor = null;
    }
}
