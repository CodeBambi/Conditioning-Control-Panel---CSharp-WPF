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
        if (!InviteDestination.IsValid(destination)) return Task.FromResult(SendResult.Refused);
        bool wantsCode = destination == InviteDestination.Goon || destination == InviteDestination.Remote;
        if (wantsCode)
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

    public Task AcceptAsync(string requesterId) => ActThenRefresh("accept", requesterId);
    public Task DeclineAsync(string requesterId) => ActThenRefresh("decline", requesterId);
    public Task CancelRequestAsync(string targetId) => ActThenRefresh("cancel", targetId);
    public Task RemoveAsync(string friendId) => ActThenRefresh("remove", friendId);
    public Task BlockAsync(string friendId) => ActThenRefresh("block", friendId);
    public Task UnblockAsync(string friendId) => ActThenRefresh("unblock", friendId);
    public Task SetSquelchAsync(string friendId, bool on) => ActThenRefresh("squelch", friendId, new JObject { ["on"] = on });

    public async Task ReportAsync(string friendId, string reason)
    {
        if (!ReportReason.IsValid(reason) || !Available || string.IsNullOrEmpty(friendId)) return;
        await _api.ActAsync("report", friendId, new JObject { ["reason"] = reason });
    }

    private async Task ActThenRefresh(string op, string id, JObject? extra = null)
    {
        if (!Available || string.IsNullOrEmpty(id)) return;
        if (await _api.ActAsync(op, id, extra)) await RefreshAsync();
    }
}
