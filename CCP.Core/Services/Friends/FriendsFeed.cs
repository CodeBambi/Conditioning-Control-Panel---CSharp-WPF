using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Friends.Feed;

// Its own namespace: the WPF tree in this repo still carries ConditioningControlPanel.Services.Friends.FriendsFeed
// (App-bound), and a test assembly that sees both would find the name twice. The WPF copy retires with WPF.

/// <summary>One stored line of the friends feed: what happened, and whether it has been shown.</summary>
public sealed record FeedEntry(FriendEvent Event, bool Read)
{
    public string Key => Event.Key;
}

/// <summary>
/// THE FRIENDS FEED ("What happened", Wave 1, 2026-09-29), hung off the head (WPF App.FriendsFeed; Avalonia FriendsFeedHost.Feed).
/// Keeps what the friends service says happened (<see cref="IFriendsService.Happened"/>): the
/// newest <see cref="Cap"/> lines, newest first, one per <see cref="FriendEvent.Key"/>, and which of
/// them nobody has looked at yet (the badges on the chips and the tray read <see cref="Unread"/>).
///
/// <para><b>Per account.</b> The lines belong to the account signed in when they happened and are
/// kept in one file per account (<see cref="FriendsFeedFile"/>). The account is read on every call,
/// the way the friends service does it, so a sign-out empties the feed and a different account only
/// ever sees its own lines.</para>
///
/// <para>Pure over an account, a load and a save, so the suite holds it without a disk. Every
/// member is safe to call from any thread; <see cref="Changed"/> is raised on the thread that made
/// the change, which in the app is the dispatcher (the service raises Happened there).</para>
/// </summary>
public sealed class FriendsFeed
{
    /// <summary>The feed never holds more lines than this; the oldest goes first.</summary>
    public const int Cap = 200;

    private readonly Func<string?> _account;
    private readonly Func<string, IReadOnlyList<FeedEntry>> _load;
    private readonly Action<string, IReadOnlyList<FeedEntry>> _save;
    private readonly object _gate = new();

    /// <summary>Newest first.</summary>
    private readonly List<FeedEntry> _lines = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private string? _for;
    private bool _loaded;
    private IFriendsService? _attached;

    /// <summary>A line landed or lines were read. Not raised for a change of account: the
    /// surfaces already repaint on the service's snapshot, which empties on a change.</summary>
    public event Action? Changed;

    public FriendsFeed(
        Func<string?> account,
        Func<string, IReadOnlyList<FeedEntry>>? load = null,
        Action<string, IReadOnlyList<FeedEntry>>? save = null)
    {
        _account = account;
        _load = load ?? (_ => Array.Empty<FeedEntry>());
        _save = save ?? ((_, _) => { });
    }

    /// <summary>The app's own feed: the account the friends service polls for, the file store,
    /// listening to <paramref name="service"/>. Never throws; the disk is only touched on use.</summary>
    public static FriendsFeed CreateForApp(IFriendsService? service, Func<string?> account)
    {
        var feed = new FriendsFeed(
            account,
            FriendsFeedFile.Load,
            FriendsFeedFile.Save);
        feed.Attach(service);
        return feed;
    }

    /// <summary>Every line for the signed-in account, newest first. Empty when signed out.</summary>
    public IReadOnlyList<FeedEntry> Lines
    {
        get
        {
            lock (_gate)
            {
                SyncLocked();
                return _lines.ToList();
            }
        }
    }

    /// <summary>How many lines nobody has been shown yet.</summary>
    public int Unread
    {
        get
        {
            lock (_gate)
            {
                SyncLocked();
                var n = 0;
                foreach (var l in _lines) if (!l.Read) n++;
                return n;
            }
        }
    }

    /// <summary>Stores a line. False when it is not kept: signed out, already here (same key),
    /// a kind the feed never tells, or older than every line of a full feed.</summary>
    public bool Add(FriendEvent? e)
    {
        if (e == null || !FriendsFeedRules.Keep(e)) return false;
        string account;
        List<FeedEntry> copy;
        lock (_gate)
        {
            SyncLocked();
            if (_for == null || _keys.Contains(e.Key)) return false;
            var entry = new FeedEntry(e with { AtUtc = FriendsFeedRules.Utc(e.AtUtc) }, false);
            // Newest first; a line as new as one already here goes above it (it arrived later).
            var at = 0;
            while (at < _lines.Count && _lines[at].Event.AtUtc > entry.Event.AtUtc) at++;
            _lines.Insert(at, entry);
            _keys.Add(entry.Key);
            while (_lines.Count > Cap)
            {
                _keys.Remove(_lines[^1].Key);
                _lines.RemoveAt(_lines.Count - 1);
            }
            if (!_keys.Contains(entry.Key)) return false;
            account = _for;
            copy = _lines.ToList();
        }
        Persist(account, copy);
        RaiseChanged();
        return true;
    }

    /// <summary>Marks these lines read (they were shown). Returns how many changed.</summary>
    public int MarkRead(IEnumerable<string>? keys)
    {
        if (keys == null) return 0;
        var set = new HashSet<string>(keys.Where(k => k != null), StringComparer.Ordinal);
        if (set.Count == 0) return 0;
        return MarkWhere(l => set.Contains(l.Key));
    }

    /// <summary>Marks every line read. Returns how many changed.</summary>
    public int MarkAllRead() => MarkWhere(_ => true);

    private int MarkWhere(Func<FeedEntry, bool> pick)
    {
        string account;
        List<FeedEntry> copy;
        var n = 0;
        lock (_gate)
        {
            SyncLocked();
            if (_for == null) return 0;
            for (var i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Read || !pick(_lines[i])) continue;
                _lines[i] = _lines[i] with { Read = true };
                n++;
            }
            if (n == 0) return 0;
            account = _for;
            copy = _lines.ToList();
        }
        Persist(account, copy);
        RaiseChanged();
        return n;
    }

    /// <summary>Listens to a friends service. One at a time; attaching another lets go of the first.</summary>
    public void Attach(IFriendsService? service)
    {
        if (ReferenceEquals(service, _attached)) return;
        Detach();
        if (service == null) return;
        service.Happened += OnHappened;
        _attached = service;
    }

    public void Detach()
    {
        if (_attached == null) return;
        try { _attached.Happened -= OnHappened; } catch { }
        _attached = null;
    }

    private void OnHappened(FriendEvent e)
    {
        try { Add(e); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] feed add failed: {E}", ex.Message); }
    }

    /// <summary>Loads the signed-in account's lines when the account changed since the last call.</summary>
    private void SyncLocked()
    {
        string? now;
        try { now = _account(); } catch { now = null; }
        if (string.IsNullOrEmpty(now)) now = null;
        if (_loaded && now == _for) return;
        _loaded = true;
        _for = now;
        _lines.Clear();
        _keys.Clear();
        if (now == null) return;

        IReadOnlyList<FeedEntry>? stored;
        try { stored = _load(now); }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Friends] feed unreadable: {E}", ex.Message);
            stored = null;
        }
        if (stored == null) return;
        var clean = stored
            .Where(l => l?.Event != null && FriendsFeedRules.Keep(l.Event))
            .Select(l => l with { Event = l.Event with { AtUtc = FriendsFeedRules.Utc(l.Event.AtUtc) } })
            .OrderByDescending(l => l.Event.AtUtc);
        foreach (var l in clean)
        {
            if (_lines.Count >= Cap) break;
            if (_keys.Add(l.Key)) _lines.Add(l);
        }
    }

    private void Persist(string account, IReadOnlyList<FeedEntry> lines)
    {
        try { _save(account, lines); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] feed not saved: {E}", ex.Message); }
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] feed handler failed: {E}", ex.Message); }
    }
}

/// <summary>
/// The feed on disk: one small JSON file per account under <c>friends_feed/</c> in the user data
/// folder, named by a hash of the account id. The file also names its account, and a file that
/// names another one reads as empty, so one account never sees another's lines. Written whole, to a
/// temp file first, on every change (200 short lines at most). Kinds are stored by name, so a new
/// kind in <see cref="FriendEventKind"/> never shifts an old file, and a name this build does not
/// know is dropped on load.
/// </summary>
public static class FriendsFeedFile
{
    public const int Version = 1;

    /// <summary>Where the files live. Swappable for the suite.</summary>
    internal static Func<string> Folder { get; set; } = () => Path.Combine(CorePaths.UserData, "friends_feed");

    /// <summary>The file for one account: the first 16 hex of the id's SHA-256, so the id itself
    /// never becomes a path.</summary>
    public static string PathFor(string account)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(account ?? ""));
        var hex = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        return Path.Combine(Folder(), hex + ".json");
    }

    public static IReadOnlyList<FeedEntry> Load(string account)
    {
        var path = PathFor(account);
        if (!File.Exists(path)) return Array.Empty<FeedEntry>();
        return Parse(File.ReadAllText(path), account);
    }

    public static void Save(string account, IReadOnlyList<FeedEntry> lines)
    {
        var path = PathFor(account);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(account, lines));
        File.Move(tmp, path, overwrite: true);
    }

    public static string Serialize(string account, IReadOnlyList<FeedEntry> lines)
    {
        var arr = new JArray();
        foreach (var l in lines)
        {
            var e = l.Event;
            arr.Add(new JObject
            {
                ["kind"] = e.Kind.ToString(),
                ["friend"] = e.FriendId,
                ["name"] = e.FriendName,
                ["at"] = FriendsFeedRules.Utc(e.AtUtc).ToString("o", CultureInfo.InvariantCulture),
                ["key"] = e.Key,
                ["dest"] = e.Destination,
                ["detail"] = e.Detail,
                ["read"] = l.Read,
            });
        }
        var root = new JObject { ["v"] = Version, ["account"] = account, ["lines"] = arr };
        return root.ToString(Formatting.None);
    }

    /// <summary>The lines in <paramref name="json"/> if it belongs to <paramref name="account"/>;
    /// empty for anything unreadable or someone else's. Never throws.</summary>
    public static IReadOnlyList<FeedEntry> Parse(string? json, string account)
    {
        var list = new List<FeedEntry>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        JObject? root;
        try
        {
            root = JsonConvert.DeserializeObject<JObject>(json,
                new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
        }
        catch { return list; }
        if (root == null || Str(root["account"]) != account) return list;
        if (root["lines"] is not JArray arr) return list;
        foreach (var t in arr)
        {
            if (t is not JObject o) continue;
            var kindName = Str(o["kind"]);
            var friend = Str(o["friend"]);
            var key = Str(o["key"]);
            if (kindName == null || friend == null || key == null) continue;
            if (!Enum.TryParse<FriendEventKind>(kindName, ignoreCase: false, out var kind)
                || !Enum.IsDefined(kind) || int.TryParse(kindName, out _)) continue;
            if (!DateTime.TryParse(Str(o["at"]), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)) continue;
            var read = o["read"]?.Type == JTokenType.Boolean && (bool)o["read"]!;
            list.Add(new FeedEntry(
                new FriendEvent(kind, friend, Str(o["name"]), DateTime.SpecifyKind(at, DateTimeKind.Utc), key,
                    Str(o["dest"]), Str(o["detail"])),
                read));
        }
        return list;
    }

    private static string? Str(JToken? t) =>
        t != null && t.Type == JTokenType.String && ((string?)t) is { Length: > 0 } s ? s : null;
}
