using System;
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
/// </list>
/// The intent waits for the page's identity frame on a fresh launch, and goes out at once to a
/// board that is already up.
/// </summary>
internal static partial class PieceByPieceHostService
{
    private static JObject? _friendIntent;
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
        _friendChallenge?.TrySetResult(null);
        _friendChallenge = null;
        _friendChallengeFor = null;
    }
}
