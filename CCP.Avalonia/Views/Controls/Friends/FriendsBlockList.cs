// PORTED from ConditioningControlPanel/Controls/Friends/FriendsBlockList.cs (7.1.5): what this PC remembers
// of the accounts this player blocked. The server's list (state.blocked, FriendsSnapshot.Blocked) is the
// truth; this file is only (a) the fallback for a server that predates it and (b) a one-time move
// (MigrateIfDue): once the server lists blocks, this PC's entries for that account are dropped, never
// sent again. Kept per signed-in account so a shared PC never shows one account's blocks to another.
// Same file name as WPF (friends_blocked.json beside the settings).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Friends;

/// <summary>One account this player blocked: who, what they were called then, and when.</summary>
public sealed record BlockedEntry(string Account, string Id, string Name, DateTimeOffset At);

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

    /// <summary>Once the server lists blocks for the signed-in account, the account's local entries
    /// go: from then on the file is never read for that account. An old server leaves everything.
    /// WPF calls this from the landing tick; this head calls it from the drawer's snapshot.</summary>
    internal static void MigrateIfDue(IFriendsService? svc)
    {
        if (_moving || svc == null) return;
        try
        {
            var server = svc.Snapshot?.Blocked;
            if (server == null) return;
            var account = Account();
            if (string.IsNullOrEmpty(account) || MovedAccounts.Contains(account)) return;
            _moving = true;
            // Nothing is sent: a remembered block the server lacks was taken off there (an Unblock
            // on another PC); sending it again would block that player again on every device.
            foreach (var e in Shared.For(account)) Shared.Remove(account, e.Id);
            MovedAccounts.Add(account);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] blocked move: {E}", ex.Message); }
        finally { _moving = false; }
    }

    /// <summary>The suite drives the move directly. Always true.</summary>
    internal static Task<bool> MigrateAsync(FriendsBlockList list, string account)
    {
        foreach (var e in list.For(account)) list.Remove(account, e.Id);
        return Task.FromResult(true);
    }

    /// <summary>The app's list, a JSON file in the user data folder. Swappable for the suite.</summary>
    internal static FriendsBlockList Shared { get; set; } = new(LoadFile, SaveFile);

    /// <summary>The signed-in account the list is kept under. Swappable for the suite.</summary>
    internal static Func<string?> Account { get; set; } = () => CoreAccount.UnifiedUserId;

    private static string FilePath => Path.Combine(CorePaths.UserData, "friends_blocked.json");

    private static IReadOnlyList<BlockedEntry> LoadFile()
    {
        try
        {
            if (!File.Exists(FilePath)) return Array.Empty<BlockedEntry>();
            return JsonConvert.DeserializeObject<List<BlockedEntry>>(File.ReadAllText(FilePath)) ?? new List<BlockedEntry>();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Friends] blocked list unreadable: {E}", ex.Message);
            return Array.Empty<BlockedEntry>();
        }
    }

    private static void SaveFile(IReadOnlyList<BlockedEntry> list)
    {
        try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(list)); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] blocked list not saved: {E}", ex.Message); }
    }
}
