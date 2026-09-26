using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// A leash with no server. It plays the server's rules closely enough to click the whole loop
/// through (offer, answer, punish with the day cap and the hidden rows, DND, tug, cut) and it is
/// what the render suite draws. Live only in a DEBUG build with <c>CCP_LEASH_DEMO=1</c>, or when a
/// test news one up. Never reached from a Release build.
/// </summary>
public sealed class FakeLeashService : ILeashService
{
    public static readonly LeashPerson Vex = new("u_vex", "Vex", null);
    public static readonly LeashPerson Mika = new("u_mika", "Mika", null);
    public static readonly LeashPerson Juno = new("u_juno", "Juno", null);

    private LeashSnapshot _snap;

    public FakeLeashService(LeashSnapshot? start = null) { _snap = start ?? LeashSnapshot.Empty; }

    public bool Available { get; set; } = true;

    public LeashSnapshot Snapshot
    {
        get => _snap;
        set { _snap = value ?? LeashSnapshot.Empty; SnapshotChanged?.Invoke(_snap); }
    }

    public event Action<LeashSnapshot>? SnapshotChanged;
    public event Action<LeashEvent>? EventArrived;

    public Punishment? GateDue => _snap.Me?.Pending.FirstOrDefault(p => p.Kind != PunishKind.Chaster);

    /// <summary>The day the leashed side would report (the preview draws it).</summary>
    public DayReport? LocalReport { get; set; }

    /// <summary>Every op the fake received, "op:arg", for the suite.</summary>
    public List<string> Calls { get; } = new();

    public void Raise(LeashEvent e) => EventArrived?.Invoke(e);

    private static string Today => DateTime.Now.ToString("yyyyMMdd");

    // ---- sample state -----------------------------------------------------------------

    public static DayReport SampleReport(bool chaster = true) => new(Today, 41, 2, 3, 6, chaster,
        chaster ? 3 * 86400 + 4 * 3600 : null, chaster ? 1350 : null, false, DateTimeOffset.UtcNow);

    public static IReadOnlyList<WeekDay> SampleWeek()
    {
        var marks = new[] { WeekMark.Did, WeekMark.Did, WeekMark.Idle, WeekMark.Punished, WeekMark.Did, WeekMark.Did, WeekMark.Today };
        var list = new List<WeekDay>();
        for (int i = 0; i < 7; i++)
            list.Add(new WeekDay(DateTime.Now.AddDays(i - 6).ToString("yyyyMMdd"), marks[i]));
        return list;
    }

    public static HeldLeash SampleHeld(LeashIntensity level = LeashIntensity.Standard, bool online = true, bool chaster = true)
        => new(Mika, online, level, DateTimeOffset.UtcNow.AddDays(-11), 12, null, SampleReport(chaster), SampleWeek(),
            Array.Empty<Punishment>(), new Assignment("a1", AssignKind.Minutes, 60, null, Today, AssignStatus.Open, DateTimeOffset.UtcNow),
            1);

    public static Punishment SamplePunishment(PunishKind kind = PunishKind.Lines, int size = 5)
        => new("p1", kind, size, kind == PunishKind.Video ? new LeashWatch("ht", "123456", "Deep pink") : null, Vex,
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(70));

    public static MyLeash SampleMine(bool pending = true)
        => new(Vex, LeashIntensity.Standard, DateTimeOffset.UtcNow.AddDays(-11), 12, null, LeashRemoteMode.Ask,
            pending ? new[] { SamplePunishment() } : Array.Empty<Punishment>(),
            new Assignment("a1", AssignKind.Minutes, 30, null, Today, AssignStatus.Open, DateTimeOffset.UtcNow), 1,
            new[]
            {
                new Sticker("good", Vex, DateTimeOffset.UtcNow.AddDays(-3)),
                new Sticker("heart", Vex, DateTimeOffset.UtcNow.AddDays(-2)),
                new Sticker("star", Vex, DateTimeOffset.UtcNow.AddDays(-1)),
            });

    /// <summary>The demo world: I hold Mika, Vex holds me, and Juno has offered.</summary>
    public static FakeLeashService Sample()
    {
        var s = new FakeLeashService(new LeashSnapshot(SampleMine(), new[] { SampleHeld() },
            new[] { new LeashOffer(Juno, DateTimeOffset.UtcNow.AddMinutes(-4), DateTimeOffset.UtcNow.AddDays(7)) }));
        s.LocalReport = SampleReport();
        return s;
    }

    // ---- holder side ------------------------------------------------------------------

    public Task<LeashSendResult> OfferAsync(string friendId)
    {
        Calls.Add("offer:" + friendId);
        if (_snap.Holding.Count >= 5) return R(LeashSendStatus.Full);
        if (_snap.Holding.Any(h => h.Who.Id == friendId)) return R(LeashSendStatus.Already);
        _offered.Add(friendId);
        return R(LeashSendStatus.Sent);
    }

    private readonly HashSet<string> _offered = new();

    /// <summary>Offers this client has sent and nobody has answered (the chip reads "offered").</summary>
    public bool HasOffered(string friendId) => _offered.Contains(friendId);

    public Task ReleaseAsync(string leashedId)
    {
        Calls.Add("release:" + leashedId);
        Snapshot = _snap with { Holding = _snap.Holding.Where(h => h.Who.Id != leashedId).ToList() };
        return Task.CompletedTask;
    }

    public Task<LeashSendResult> AssignAsync(string leashedId, AssignKind kind, int size, LeashWatch? watch = null)
    {
        Calls.Add($"assign:{leashedId}:{kind}:{size}");
        var h = Find(leashedId);
        if (h == null) return R(LeashSendStatus.Refused);
        if (LeashUiRules.DndOn(h.DndUntil, DateTimeOffset.UtcNow)) return Task.FromResult(new LeashSendResult(LeashSendStatus.Dnd, h.DndUntil));
        bool replaced = h.Assignment is { Status: AssignStatus.Open };
        Replace(h with { Assignment = new Assignment("a" + Calls.Count, kind, size, watch, Today, AssignStatus.Open, DateTimeOffset.UtcNow) });
        return R(replaced ? LeashSendStatus.Replaced : LeashSendStatus.Sent);
    }

    public Task<LeashSendResult> PunishAsync(string leashedId, PunishKind kind, int size, LeashWatch? watch = null)
    {
        Calls.Add($"punish:{leashedId}:{kind}:{size}");
        var h = Find(leashedId);
        if (h == null) return R(LeashSendStatus.Refused);
        if (!LeashUiRules.Allowed(kind, h.Intensity)) return R(LeashSendStatus.NotAllowed);
        if (LeashUiRules.DndOn(h.DndUntil, DateTimeOffset.UtcNow)) return Task.FromResult(new LeashSendResult(LeashSendStatus.Dnd, h.DndUntil));
        if (h.PunishedToday >= 3) return R(LeashSendStatus.Cap);
        bool queued = h.Pending.Count > 0;
        var p = new Punishment("p" + Calls.Count, kind, size, watch, Vex, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(72));
        var pending = kind == PunishKind.Chaster ? h.Pending : h.Pending.Append(p).ToList();
        Replace(h with { Pending = pending, PunishedToday = h.PunishedToday + 1 });
        return R(queued && kind != PunishKind.Chaster ? LeashSendStatus.Queued : LeashSendStatus.Sent);
    }

    public Task<LeashSendResult> RewardAsync(string leashedId, RewardKind kind, string? stickerOrPoke = null, int? size = null)
    {
        Calls.Add($"reward:{leashedId}:{kind}:{stickerOrPoke ?? size?.ToString() ?? ""}");
        return R(Find(leashedId) == null ? LeashSendStatus.Refused : LeashSendStatus.Sent);
    }

    private DateTimeOffset _lastTug = DateTimeOffset.MinValue;

    public Task<LeashSendResult> TugAsync(string leashedId)
    {
        Calls.Add("tug:" + leashedId);
        var h = Find(leashedId);
        if (h == null) return R(LeashSendStatus.Refused);
        if (LeashUiRules.DndOn(h.DndUntil, DateTimeOffset.UtcNow)) return Task.FromResult(new LeashSendResult(LeashSendStatus.Dnd, h.DndUntil));
        if (DateTimeOffset.UtcNow - _lastTug < TimeSpan.FromSeconds(10)) return R(LeashSendStatus.TooFast);
        _lastTug = DateTimeOffset.UtcNow;
        return R(LeashSendStatus.Sent);
    }

    // ---- leashed side -----------------------------------------------------------------

    public Task<bool> AnswerAsync(string holderId, bool accept, LeashIntensity intensity)
    {
        Calls.Add($"answer:{holderId}:{accept}:{intensity}");
        var offer = _snap.Offers.FirstOrDefault(o => o.From.Id == holderId);
        var offers = _snap.Offers.Where(o => o.From.Id != holderId).ToList();
        if (offer == null) { Snapshot = _snap with { Offers = offers }; return Task.FromResult(false); }
        MyLeash? me = _snap.Me;
        if (accept)
            me = new MyLeash(offer.From, intensity, DateTimeOffset.UtcNow, 1, null, LeashRemoteMode.Ask,
                Array.Empty<Punishment>(), null, 0, _snap.Me?.Stickers ?? Array.Empty<Sticker>());
        Snapshot = _snap with { Me = me, Offers = offers };
        return Task.FromResult(true);
    }

    public Task CutAsync()
    {
        Calls.Add("cut");
        Snapshot = _snap with { Me = null };
        return Task.CompletedTask;
    }

    public Task SetIntensityAsync(LeashIntensity intensity)
    {
        Calls.Add("intensity:" + intensity);
        if (_snap.Me is { } me)
            Snapshot = _snap with { Me = me with { Intensity = intensity, Pending = me.Pending.Where(p => LeashUiRules.Allowed(p.Kind, intensity)).ToList() } };
        return Task.CompletedTask;
    }

    public Task SetDndAsync(LeashDnd dnd)
    {
        Calls.Add("dnd:" + dnd);
        if (_snap.Me is { } me)
        {
            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? until = dnd switch
            {
                LeashDnd.OneHour => now.AddHours(1),
                LeashDnd.FourHours => now.AddHours(4),
                LeashDnd.Today => LeashUiRules.LocalMidnight(now),
                _ => null,
            };
            Snapshot = _snap with { Me = me with { DndUntil = until } };
        }
        return Task.CompletedTask;
    }

    public Task SetRemoteModeAsync(LeashRemoteMode mode)
    {
        Calls.Add("remote:" + mode);
        if (_snap.Me is { } me) Snapshot = _snap with { Me = me with { RemoteMode = mode } };
        return Task.CompletedTask;
    }

    public Task CompleteAsync(string pid)
    {
        Calls.Add("complete:" + pid);
        if (_snap.Me is { } me) Snapshot = _snap with { Me = me with { Pending = me.Pending.Where(p => p.Pid != pid).ToList() } };
        return Task.CompletedTask;
    }

    public Task<bool> PardonAsync(string pid)
    {
        Calls.Add("pardon:" + pid);
        if (_snap.Me is not { } me || me.Pardons <= 0) return Task.FromResult(false);
        Snapshot = _snap with { Me = me with { Pardons = me.Pardons - 1, Pending = me.Pending.Where(p => p.Pid != pid).ToList() } };
        return Task.FromResult(true);
    }

    // ---- helpers ----------------------------------------------------------------------

    private HeldLeash? Find(string id) => _snap.Holding.FirstOrDefault(h => h.Who.Id == id);

    private void Replace(HeldLeash h)
        => Snapshot = _snap with { Holding = _snap.Holding.Select(x => x.Who.Id == h.Who.Id ? h : x).ToList() };

    private static Task<LeashSendResult> R(LeashSendStatus s) => Task.FromResult(new LeashSendResult(s));
}

/// <summary>A task runner that finishes each task one step per <see cref="Step"/> call (the demo
/// ticks it on a timer; the suite calls it by hand).</summary>
public sealed class FakeLeashTaskRunner : ILeashTaskRunner
{
    private Punishment? _p;
    private int _done;

    public bool IsRunning => _p != null;
    public string? RunningPid => _p?.Pid;
    public event Action<string, int, int>? Progress;
    public event Action<string>? Completed;

    public bool Start(Punishment punishment)
    {
        if (_p != null || punishment.Kind == PunishKind.Chaster) return false;
        _p = punishment;
        _done = 0;
#if DEBUG
        if (LeashLocator.Demo != null) StartDemoClock();
#endif
        return true;
    }

    public void Cancel() { _p = null; _done = 0; }

    /// <summary>One step of work. Lines finish in Size steps, everything else in 3.</summary>
    public void Step()
    {
        if (_p is not { } p) return;
        int total = p.Kind == PunishKind.Lines ? p.Size : 3;
        _done++;
        Progress?.Invoke(p.Pid, _done, total);
        if (_done < total) return;
        _p = null;
        Completed?.Invoke(p.Pid);
    }

#if DEBUG
    private void StartDemoClock()
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        t.Tick += (_, _) => { if (_p == null) { t.Stop(); return; } Step(); };
        t.Start();
    }
#endif
}
