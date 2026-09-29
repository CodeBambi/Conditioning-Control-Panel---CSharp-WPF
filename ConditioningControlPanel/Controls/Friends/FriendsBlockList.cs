using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>One account this player blocked: who, what they were called then, and when.</summary>
public sealed record BlockedEntry(string Account, string Id, string Name, DateTimeOffset At);

/// <summary>
/// What this PC remembers of the accounts this player blocked. Since FRIENDS-RECEIPTS v1 the
/// server lists them back (<c>state.blocked</c>, <see cref="FriendsSnapshot.Blocked"/>) and that
/// list is the truth; this file is only (a) the fallback for a server that predates it and (b) a
/// one-time move (<see cref="MigrateIfDue"/>): the blocks the server lacks are sent once, then this
/// PC's entries for that account are dropped. Kept per signed-in account so a shared PC never shows
/// one account's blocks to another. Pure over a load and a save, so the suite holds it without a disk.
/// </summary>
public sealed class FriendsBlockList
{
    /// <summary>The list never grows past this; the oldest block goes first (it stays blocked).</summary>
    public const int Cap = 200;

    private readonly Func<IReadOnlyList<BlockedEntry>> _load;
    private readonly Action<IReadOnlyList<BlockedEntry>> _save;

    public FriendsBlockList(Func<IReadOnlyList<BlockedEntry>> load, Action<IReadOnlyList<BlockedEntry>> save)
    {
        _load = load;
        _save = save;
    }

    /// <summary>This account's blocks, newest first.</summary>
    public IReadOnlyList<BlockedEntry> For(string? account)
    {
        if (string.IsNullOrEmpty(account)) return Array.Empty<BlockedEntry>();
        return Read().Where(e => e.Account == account).OrderByDescending(e => e.At).ToList();
    }

    /// <summary>Remembers a block. A second block of the same id refreshes its name and time.</summary>
    public void Add(string? account, string id, string? name, DateTimeOffset at)
    {
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(id)) return;
        var all = Read().Where(e => !(e.Account == account && e.Id == id)).ToList();
        all.Add(new BlockedEntry(account, id, string.IsNullOrWhiteSpace(name) ? "?" : name.Trim(), at));
        while (all.Count > Cap) all.Remove(all.OrderBy(e => e.At).First());
        _save(all);
    }

    /// <summary>Forgets a block (after the server said the unblock went through).</summary>
    public void Remove(string? account, string id)
    {
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(id)) return;
        var all = Read().ToList();
        if (all.RemoveAll(e => e.Account == account && e.Id == id) > 0) _save(all);
    }

    private IReadOnlyList<BlockedEntry> Read()
    {
        try { return _load() ?? Array.Empty<BlockedEntry>(); }
        catch { return Array.Empty<BlockedEntry>(); }
    }

    // ---- the one-time move to the server's list ----

    private static readonly HashSet<string> MovedAccounts = new(StringComparer.Ordinal);
    private static bool _moving;

    /// <summary>The landing tick calls this. Once the server lists blocks for the signed-in account,
    /// the blocks this PC remembers and the server lacks are sent once, and the account's local
    /// entries go: from then on the file is never read for that account. An old server (no list)
    /// leaves everything as it was.</summary>
    internal static async void MigrateIfDue(IFriendsService svc)
    {
        if (_moving || svc == null) return;
        try
        {
            var server = svc.Snapshot?.Blocked;
            if (server == null) return;
            var account = Account();
            if (string.IsNullOrEmpty(account) || MovedAccounts.Contains(account)) return;
            if (Shared.For(account).Count == 0) { MovedAccounts.Add(account); return; }
            _moving = true;
            if (await MigrateAsync(svc, Shared, account, server)) MovedAccounts.Add(account);
        }
        catch (Exception ex) { ConditioningControlPanel.App.Logger?.Debug("[Friends] blocked move: {E}", ex.Message); }
        finally { _moving = false; }
    }

    /// <summary>The move itself (the suite drives it): each remembered block the server's list lacks
    /// is sent; every entry the server now has, or will never take, is forgotten; one that hit a
    /// network fault stays for the next try. True when nothing is left to move.</summary>
    internal static async Task<bool> MigrateAsync(IFriendsService svc, FriendsBlockList list, string account,
        IReadOnlyList<BlockedFriend> server)
    {
        var onServer = new HashSet<string>(server.Select(b => b.Id), StringComparer.Ordinal);
        bool left = false;
        foreach (var e in list.For(account))
        {
            if (!onServer.Contains(e.Id))
            {
                ActResult r;
                try { r = await svc.BlockAsync(e.Id); }
                catch { r = ActResult.TryLater; }
                if (r is ActResult.TryLater or ActResult.TooFast) { left = true; continue; }
            }
            list.Remove(account, e.Id);
        }
        return !left;
    }

    // ---- the app's own store: one small file beside the settings ----

    /// <summary>The app's list, a JSON file in the user data folder. Swappable for the suite.</summary>
    internal static FriendsBlockList Shared { get; set; } = new(LoadFile, SaveFile);

    /// <summary>The signed-in account the list is kept under. Swappable for the suite.</summary>
    internal static Func<string?> Account { get; set; } = () =>
    {
        try { return Services.BackRoom.BackRoomApi.AppIdentity()?.UnifiedId; }
        catch { return null; }
    };

    private static string FilePath => Path.Combine(ConditioningControlPanel.App.UserDataPath, "friends_blocked.json");

    private static IReadOnlyList<BlockedEntry> LoadFile()
    {
        try
        {
            if (!File.Exists(FilePath)) return Array.Empty<BlockedEntry>();
            return JsonConvert.DeserializeObject<List<BlockedEntry>>(File.ReadAllText(FilePath)) ?? new List<BlockedEntry>();
        }
        catch (Exception ex)
        {
            ConditioningControlPanel.App.Logger?.Debug("[Friends] blocked list unreadable: {E}", ex.Message);
            return Array.Empty<BlockedEntry>();
        }
    }

    private static void SaveFile(IReadOnlyList<BlockedEntry> list)
    {
        try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(list)); }
        catch (Exception ex) { ConditioningControlPanel.App.Logger?.Debug("[Friends] blocked list not saved: {E}", ex.Message); }
    }
}
