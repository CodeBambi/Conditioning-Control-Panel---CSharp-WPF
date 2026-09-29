using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Friends;

// The things a player does from the drawer. Every send is checked against the grammar before it
// leaves (a refusal the wire would give costs nothing here), and every change to the list is
// followed by a fresh state so the drawer never draws a guess.
public sealed partial class FriendsService
{
    public Task<SendResult> PokeAsync(string friendId, string pokeId)
    {
        if (!PokeSet.IsValid(pokeId)) return Task.FromResult(SendResult.Refused);
        var now = _now();
        if (_lastPoke.TryGetValue(friendId, out var last) && now - last < TimeSpan.FromSeconds(PokeSet.CooldownSeconds))
            return Task.FromResult(SendResult.TooFast);
        return SendAsync(friendId, SendKind.Poke, pokeId, null, null, null, sent => { if (sent) _lastPoke[friendId] = now; });
    }

    public Task<SendResult> InviteAsync(string friendId, string destination, string? code)
    {
        // Remote is still wire grammar (an old client may send one) but never offered or sent (owner, 2026-09-28).
        if (!InviteDestination.IsSendable(destination)) return Task.FromResult(SendResult.Refused);
        bool wantsCode = destination == InviteDestination.Goon;
        if (destination == InviteDestination.Chess)
        {
            if (!InviteDestination.IsChallengeId(code)) return Task.FromResult(SendResult.Refused);
        }
        else if (wantsCode)
        {
            if (code == null || !FriendsApi.IsJoinCode(code)) return Task.FromResult(SendResult.Refused);
        }
        else code = null;
        return SendAsync(friendId, SendKind.Invite, null, destination, code, null, null);
    }

    public Task<SendResult> SendWatchAsync(string friendId, WatchRef watch)
    {
        if (watch == null || !watch.IsValid()) return Task.FromResult(SendResult.Refused);
        return SendAsync(friendId, SendKind.Watch, null, null, null, CleanTitle(watch), null);
    }

    /// <summary>The server stores a title of 40 characters from a small alphabet and refuses anything
    /// else; a title is decoration, so an unfit one is trimmed or dropped rather than refused.</summary>
    internal static WatchRef CleanTitle(WatchRef w)
    {
        if (string.IsNullOrEmpty(w.Title)) return w with { Title = null };
        var kept = new string(w.Title.Where(c => char.IsAsciiLetterOrDigit(c) || c == ' ' || c == '.' || c == ',' || c == '\'' || c == '-').ToArray()).Trim();
        if (kept.Length > 40) kept = kept.Substring(0, 40).TrimEnd();
        return w with { Title = kept.Length == 0 ? null : kept };
    }

    private async Task<SendResult> SendAsync(string friendId, SendKind kind, string? poke, string? destination, string? code,
        WatchRef? watch, Action<bool>? after)
    {
        if (!Available || string.IsNullOrEmpty(friendId)) return SendResult.TryLater;
        var result = await _api.SendAsync(friendId, kind, poke, destination, code, watch);
        after?.Invoke(result == SendResult.Sent);
        if (result == SendResult.Sent)
        {
            var friend = Snapshot.Friends.FirstOrDefault(f => f.Id == friendId)
                ?? new Friend(friendId, "", null, 0, false, FriendPresence.None, false);
            try { Sent?.Invoke(kind, friend); }
            catch (Exception ex) { App.Logger?.Debug("Friends sent handler failed: {E}", ex.Message); }
        }
        return result;
    }

    public async Task<AddResult> AddByCodeAsync(string code)
    {
        var wire = FriendsApi.NormaliseCode(code);
        if (wire == null) return AddResult.NotFound;
        if (!Available) return AddResult.TryLater;
        if (wire == Snapshot.MyCode) return AddResult.Self;
        var r = await _api.RequestAsync(wire);
        if (r is AddResult.Sent or AddResult.Accepted) await RefreshAsync();
        return r;
    }

    public Task<ActResult> AcceptAsync(string requesterId) => ActThenRefresh("accept", requesterId);
    public Task<ActResult> DeclineAsync(string requesterId) => ActThenRefresh("decline", requesterId);
    public Task<ActResult> CancelRequestAsync(string targetId) => ActThenRefresh("cancel", targetId);
    public Task<ActResult> RemoveAsync(string friendId) => ActThenRefresh("remove", friendId);
    public Task<ActResult> BlockAsync(string friendId) => ActThenRefresh("block", friendId);
    public Task<ActResult> UnblockAsync(string friendId) => ActThenRefresh("unblock", friendId);
    public Task<ActResult> SetSquelchAsync(string friendId, bool on) => ActThenRefresh("squelch", friendId, new JObject { ["on"] = on });

    public async Task<ActResult> ReportAsync(string friendId, string reason)
    {
        if (!ReportReason.IsValid(reason) || string.IsNullOrEmpty(friendId)) return ActResult.Refused;
        if (!Available) return ActResult.TryLater;
        return await _api.ActForResultAsync("report", friendId, new JObject { ["reason"] = reason });
    }

    private async Task<ActResult> ActThenRefresh(string op, string id, JObject? extra = null)
    {
        if (string.IsNullOrEmpty(id)) return ActResult.Refused;
        if (!Available) return ActResult.TryLater;
        var r = await _api.ActForResultAsync(op, id, extra);
        if (r == ActResult.Done) await RefreshAsync();
        return r;
    }
}
