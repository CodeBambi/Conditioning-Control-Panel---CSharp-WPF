// PORTED verbatim from WPF 7.1.5 ConditioningControlPanel/Services/Lobby/LobbyModel.cs (pure: the merge,
// the summary line and the gates). The head draws; nothing here touches a UI type.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>The games the Lobby lists. One row shape for all three.</summary>
public enum LobbyGame { Chess, Goon, Remote }

/// <summary>Open = waiting for a player (Join). Playing = a game under way (Watch only where the
/// game supports spectating; none does today, so no button).</summary>
public enum LobbyRowState { Open, Playing }

/// <summary>
/// One Lobby row. <see cref="Key"/> is what a Join sends: a chess <c>p_</c> id off
/// <c>GET /v2/pbp/lobby</c>, a Goon room code off <c>/v2/goon/open</c>, or a Remote subject's
/// unified id off <c>/v2/directory/list</c>. Null on rows nobody can join (Playing, presence).
/// No host free text rides here except what the existing directory already shows.
/// </summary>
public sealed record LobbyRow
{
    public LobbyGame Game { get; init; }
    public LobbyRowState State { get; init; }
    public string? Key { get; init; }
    public string HostName { get; init; } = "";
    /// <summary>Playing chess: the other seat's name ("white vs black").</summary>
    public string? OpponentName { get; init; }
    public string? Avatar { get; init; }
    public int Level { get; init; }
    public bool Friend { get; init; }
    /// <summary>Seconds the table has waited (Open) or run (Playing); 0 when unknown.</summary>
    public int AgeSec { get; init; }

    // chess
    public int InitialMs { get; init; }
    public int IncrementMs { get; init; }
    public int Moves { get; init; }
    // goon
    public bool Song { get; init; }
    public int CardSec { get; init; }
    public bool Pictures { get; init; }
    // remote
    public string? RemoteTier { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>No game supports spectating yet (chess, Goon, Remote). Kept as a field so the
    /// row template already has the seat for it.</summary>
    public bool CanWatch { get; init; }

    public bool CanJoin => State == LobbyRowState.Open && !string.IsNullOrEmpty(Key);
}

/// <summary>A chess lobby answer from <c>GET /v2/pbp/lobby</c>, reduced to what the Lobby shows.</summary>
public sealed record PbpOpenSeat(string Id, string Name, int InitialMs, int IncrementMs, long SinceMs);
public sealed record PbpPlayingGame(string White, string Black, int InitialMs, int IncrementMs, long StartedMs, int Moves);
public sealed record PbpLobbyReply(IReadOnlyList<PbpOpenSeat> Open, IReadOnlyList<PbpPlayingGame> Playing, bool Ok)
{
    public static readonly PbpLobbyReply Empty = new(Array.Empty<PbpOpenSeat>(), Array.Empty<PbpPlayingGame>(), false);
}

/// <summary>A Remote directory entry as the Lobby reads it (a copy of <see cref="DirectoryEntry"/>
/// taken on the UI thread, so the merge never touches a live ObservableCollection).</summary>
public sealed record RemoteSeat(string UnifiedId, string Name, int Level, string Tier, IReadOnlyList<string> Tags, bool Claimed);

/// <summary>The three lists, plus what the host buttons need.</summary>
public sealed record LobbySnapshot(
    IReadOnlyList<LobbyRow> Open,
    IReadOnlyList<LobbyRow> Playing,
    IReadOnlyList<LobbyRow> Friends,
    bool SignedIn)
{
    public static readonly LobbySnapshot Empty = new(Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), false);

    /// <summary>Every joinable table, friends first: the launcher dropdown and its "N open".</summary>
    public IReadOnlyList<LobbyRow> Joinable =>
        Friends.Where(r => r.CanJoin).Concat(Open.Where(r => r.CanJoin)).ToList();

    public int OpenCount => Joinable.Count;
    public bool IsEmpty => Open.Count == 0 && Playing.Count == 0 && Friends.Count == 0;
}

/// <summary>
/// THE MERGE. Pure: the three game answers plus the friends list in, one <see cref="LobbySnapshot"/>
/// out. Friend rows go to list 3 only (no row is drawn twice); newest tables first;
/// a friend who is in a game with no listed table still shows as "playing" with no button.
/// </summary>
public static class LobbyMerge
{
    public const int MaxPerList = 40;

    public static LobbySnapshot Build(
        PbpLobbyReply? chess,
        OpenTablesReply? goon,
        IReadOnlyList<RemoteSeat>? remote,
        IReadOnlyList<Friend>? friends,
        bool signedIn,
        long nowMs)
    {
        if (!signedIn) return LobbySnapshot.Empty;
        var friendList = friends ?? Array.Empty<Friend>();
        var friendIds = new HashSet<string>(friendList.Select(f => f.Id), StringComparer.Ordinal);
        var friendNames = new HashSet<string>(friendList.Where(f => !string.IsNullOrEmpty(f.Name)).Select(f => f.Name), StringComparer.Ordinal);

        var rows = new List<LobbyRow>();

        if (chess != null)
        {
            foreach (var s in chess.Open)
                rows.Add(new LobbyRow
                {
                    Game = LobbyGame.Chess, State = LobbyRowState.Open, Key = s.Id, HostName = s.Name,
                    InitialMs = s.InitialMs, IncrementMs = s.IncrementMs,
                    AgeSec = AgeSec(nowMs, s.SinceMs),
                    // The chess wire hands opaque p_ ids, never a unified id: a friend is only
                    // recognisable by an exact, unique display name. Server follow-up: a friend flag.
                    Friend = friendNames.Contains(s.Name),
                });
            foreach (var p in chess.Playing)
                rows.Add(new LobbyRow
                {
                    Game = LobbyGame.Chess, State = LobbyRowState.Playing, HostName = p.White, OpponentName = p.Black,
                    InitialMs = p.InitialMs, IncrementMs = p.IncrementMs, Moves = p.Moves,
                    AgeSec = AgeSec(nowMs, p.StartedMs),
                    Friend = friendNames.Contains(p.White) || friendNames.Contains(p.Black),
                });
        }

        if (goon != null)
            foreach (var t in goon.Tables)
                rows.Add(new LobbyRow
                {
                    Game = LobbyGame.Goon, State = LobbyRowState.Open, Key = t.Code, HostName = t.Name,
                    Avatar = t.Avatar, Level = t.Level, Friend = t.Friend, AgeSec = t.WaitingSec,
                    Song = t.Song, CardSec = t.CardSec, Pictures = t.Pictures,
                });

        if (remote != null)
            foreach (var r in remote)
                rows.Add(new LobbyRow
                {
                    Game = LobbyGame.Remote,
                    State = r.Claimed ? LobbyRowState.Playing : LobbyRowState.Open,
                    Key = r.Claimed ? null : r.UnifiedId,
                    HostName = r.Name, Level = r.Level, RemoteTier = r.Tier, Tags = r.Tags,
                    Friend = friendIds.Contains(r.UnifiedId),
                });

        // Friends in a game the lists above do not show (a chess match off the open list, a
        // Goon match under way, a claimed Remote session): one presence row, no button.
        foreach (var f in friendList)
        {
            if (!f.Online) continue;
            LobbyGame? game = f.Presence.Activity switch
            {
                PresenceActivity.Chess => LobbyGame.Chess,
                PresenceActivity.Goon or PresenceActivity.GoonHosting => LobbyGame.Goon,
                PresenceActivity.Remote => LobbyGame.Remote,
                _ => null,
            };
            if (game == null) continue;
            if (HasFriendRow(rows, game.Value, f, goon)) continue;
            rows.Add(new LobbyRow
            {
                Game = game.Value, State = LobbyRowState.Playing, HostName = f.Name, Avatar = f.AvatarUrl, Friend = true,
            });
        }

        var friendRows = rows.Where(r => r.Friend)
            .OrderBy(r => r.State == LobbyRowState.Open ? 0 : 1)
            .ThenBy(r => r.AgeSec)
            .ThenBy(r => r.HostName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxPerList).ToList();
        var open = rows.Where(r => !r.Friend && r.State == LobbyRowState.Open)
            .OrderBy(r => r.AgeSec)
            .ThenBy(r => r.Game)
            .ThenBy(r => r.HostName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxPerList).ToList();
        var playing = rows.Where(r => !r.Friend && r.State == LobbyRowState.Playing)
            .OrderBy(r => r.Game)
            .ThenBy(r => r.AgeSec)
            .ThenBy(r => r.HostName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxPerList).ToList();
        return new LobbySnapshot(open, playing, friendRows, true);
    }

    private static bool HasFriendRow(List<LobbyRow> rows, LobbyGame game, Friend f, OpenTablesReply? goon)
    {
        if (game == LobbyGame.Goon && goon != null && GoonOpenTables.ForFriend(goon, f.Id, f.Name) != null) return true;
        return rows.Any(r => r.Game == game && r.Friend
                             && (string.Equals(r.HostName, f.Name, StringComparison.Ordinal)
                                 || string.Equals(r.OpponentName, f.Name, StringComparison.Ordinal)));
    }

    private static int AgeSec(long nowMs, long sinceMs)
    {
        if (sinceMs <= 0 || nowMs <= sinceMs) return 0;
        return (int)Math.Min(int.MaxValue, (nowMs - sinceMs) / 1000);
    }
}

/// <summary>
/// The settings line under a row. Pure; <paramref name="loc"/> is the string lookup (Loc.Get in
/// the app, identity in tests). Parts join with a middle dot; nothing here is host free text.
/// </summary>
public static class LobbySummary
{
    public static string For(LobbyRow row, Func<string, string> loc)
    {
        var parts = new List<string>();
        switch (row.Game)
        {
            case LobbyGame.Chess:
                if (row.InitialMs > 0) parts.Add(Clock(row.InitialMs, row.IncrementMs));
                if (row.State == LobbyRowState.Playing && row.Moves > 0)
                    parts.Add(string.Format(CultureInfo.InvariantCulture, loc("lobby_moves"), row.Moves));
                break;
            case LobbyGame.Goon:
                if (row.Song) parts.Add(loc("lobby_goon_song"));
                if (row.CardSec > 0) parts.Add(string.Format(CultureInfo.InvariantCulture, loc("lobby_goon_cards"), row.CardSec));
                if (row.Pictures) parts.Add(loc("lobby_goon_pictures"));
                break;
            case LobbyGame.Remote:
                if (!string.IsNullOrEmpty(row.RemoteTier)) parts.Add(loc("lobby_remote_tier_" + row.RemoteTier));
                if (row.Tags.Count > 0) parts.Add(string.Join(", ", row.Tags.Take(3)));
                break;
        }
        return string.Join(" · ", parts);
    }

    /// <summary>"10+0", "5+3", "15+10": minutes plus increment seconds, the chess menu's labels.</summary>
    public static string Clock(int initialMs, int incrementMs)
    {
        var min = Math.Max(0, initialMs) / 60000.0;
        var inc = Math.Max(0, incrementMs) / 1000;
        var minText = Math.Abs(min - Math.Round(min)) < 0.001
            ? ((int)Math.Round(min)).ToString(CultureInfo.InvariantCulture)
            : min.ToString("0.#", CultureInfo.InvariantCulture);
        return minText + "+" + inc.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// What each host and join button may do, read from each game's EXISTING gate. Locked never
/// means hidden: the button shows with lock livery and a click goes to the TierGate refusal.
/// </summary>
public readonly record struct LobbyGates(
    bool SignedIn,
    bool ChessHost, bool ChessJoin,
    bool GoonHost, bool GoonJoin,
    bool RemoteHost, bool RemoteJoin)
{
    /// <summary>
    /// <paramref name="goonHosting"/> = <c>GoonHostService.HostingAllowed()</c> (patrons);
    /// <paramref name="remoteHosting"/> = premium or the ? box's "remote" free day (the old
    /// "Become a subject" rule). Chess is free with an account; every join needs an account.
    /// </summary>
    public static LobbyGates From(bool signedIn, bool goonHosting, bool remoteHosting) => new(
        signedIn,
        ChessHost: signedIn, ChessJoin: signedIn,
        GoonHost: signedIn && goonHosting, GoonJoin: signedIn,
        RemoteHost: signedIn && remoteHosting, RemoteJoin: signedIn);

    public bool CanHost(LobbyGame g) => g switch
    {
        LobbyGame.Chess => ChessHost,
        LobbyGame.Goon => GoonHost,
        _ => RemoteHost,
    };

    public bool CanJoin(LobbyGame g) => g switch
    {
        LobbyGame.Chess => ChessJoin,
        LobbyGame.Goon => GoonJoin,
        _ => RemoteJoin,
    };
}
