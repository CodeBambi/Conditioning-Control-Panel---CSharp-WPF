using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>PhrasePoolCustody lifecycle and its contract with the real Core ModService (#906, D1).</summary>
[Collection(SessionStatics.Name)]
public sealed class PhrasePoolCustodyTests : IDisposable
{
    private static readonly string[] PoolNames =
        { nameof(AppSettings.SubliminalPool), nameof(AppSettings.BouncingTextPool), nameof(AppSettings.LockCardPhrases) };

    private readonly Func<string, string?>? _oldAware = CoreMods.MakeModAwareProvider;
    private readonly Func<string?>? _oldModId = CoreMods.ActiveModIdProvider;
    private readonly Action? _oldReapply = CoreMods.ReapplyActiveModPoolsProvider;
    private readonly Func<SettingsService?>? _oldSettings = CoreSettings.ServiceProvider;
    private SettingsService? _svc;

    public PhrasePoolCustodyTests() => PhrasePoolCustody.Seed();

    public void Dispose()
    {
        PhrasePoolCustody.Active?.Release();
        CoreSession.NoteUserPhrasePoolEdit = null;
        CoreSession.ReapplyPhrasePoolOverrides = null;
        CoreSession.UserPhrasePoolsWhileOverriding = null;
        CoreMods.MakeModAwareProvider = _oldAware;
        CoreMods.ActiveModIdProvider = _oldModId;
        CoreMods.ReapplyActiveModPoolsProvider = _oldReapply;
        if (_svc != null) { _svc.SaveImmediate(); _svc.SealForReset(); }
        CoreSettings.ServiceProvider = _oldSettings;
        if (_svc != null)
            foreach (var f in Directory.GetFiles(CorePaths.UserData, "settings*")) File.Delete(f);
    }

    private static Dictionary<string, bool> D(params string[] keys) => keys.ToDictionary(k => k, _ => true);

    private static SessionSettings Prescribing(bool sub = true, bool bounce = true, bool lockCard = true) => new()
    {
        SubliminalEnabled = true, BouncingTextEnabled = true, LockCardEnabled = true,
        SubliminalPhrases = sub ? new() { "zzsub" } : new(),
        BouncingTextPhrases = bounce ? new() { "zzbounce" } : new(),
        LockCardPhrases = lockCard ? new() { "zzlock" } : new(),
    };

    private static string PoolsJson(AppSettings s) => JsonConvert.SerializeObject(
        new { s.SubliminalPool, s.BouncingTextPool, s.LockCardPhrases, s.SubliminalPoolByMod, s.BouncingTextPoolByMod, s.LockCardPhrasesByMod });

    private static AppSettings Plain() => new()
    {
        SubliminalPool = D("s1", "s2"), BouncingTextPool = D("b1"), LockCardPhrases = D("l1"),
    };

    [Fact]
    public void Begin_InPlace_NoPoolINPC_ModAwareSubAndLockOnly()
    {
        CoreMods.MakeModAwareProvider = t => "M:" + t;
        var s = Plain();
        var (sub, bounce, lockP) = (s.SubliminalPool, s.BouncingTextPool, s.LockCardPhrases);
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        PhrasePoolCustody.Begin(s, Prescribing(), "m");

        Assert.DoesNotContain(raised, PoolNames.Contains);
        Assert.Same(sub, s.SubliminalPool); Assert.Same(bounce, s.BouncingTextPool); Assert.Same(lockP, s.LockCardPhrases);
        Assert.Equal(new Dictionary<string, bool> { ["s1"] = false, ["s2"] = false, ["M:zzsub"] = true }, s.SubliminalPool);
        Assert.Equal(new Dictionary<string, bool> { ["b1"] = false, ["zzbounce"] = true }, s.BouncingTextPool);
        Assert.Equal(new Dictionary<string, bool> { ["l1"] = false, ["M:zzlock"] = true }, s.LockCardPhrases);
        Assert.Equal(D("s1", "s2"), CoreSession.UserPhrasePoolsWhileOverriding!()!.Value.Subliminal);
    }

    [Fact]
    public void Begin_NoPhrases_NotOverriding()
    {
        var s = Plain();
        var before = PoolsJson(s);
        PhrasePoolCustody.Begin(s, Prescribing(false, false, false), "m");
        Assert.NotNull(PhrasePoolCustody.Active);
        Assert.Null(CoreSession.UserPhrasePoolsWhileOverriding!());
        Assert.Equal(before, PoolsJson(s));
    }

    [Fact]
    public void Delegates_NoSession_NoOpNull()
    {
        Assert.Null(PhrasePoolCustody.Active);
        CoreSession.NoteUserPhrasePoolEdit!(nameof(AppSettings.SubliminalPool));
        CoreSession.ReapplyPhrasePoolOverrides!();
        Assert.Null(CoreSession.UserPhrasePoolsWhileOverriding!());

        var custody = PhrasePoolCustody.Begin(Plain(), Prescribing(), "m");
        custody.Release();
        Assert.Null(CoreSession.UserPhrasePoolsWhileOverriding!());
    }

    [Fact]
    public void End_RestoresByteIdentical_ActiveNull()
    {
        var s = Plain();
        var before = JsonConvert.SerializeObject(s, Formatting.Indented);
        var custody = PhrasePoolCustody.Begin(s, Prescribing(), CoreMods.ActiveModId);
        custody.Release();
        custody.Restore(s);
        Assert.Null(PhrasePoolCustody.Active);
        Assert.Equal(before, JsonConvert.SerializeObject(s, Formatting.Indented));
    }

    [Fact]
    public void End_InPlace_ByModUntouched()
    {
        var s = Plain();
        s.SubliminalPoolByMod = new() { ["m"] = D("s1") };
        var byMod = JsonConvert.SerializeObject(s.SubliminalPoolByMod);
        var custody = PhrasePoolCustody.Begin(s, Prescribing(), CoreMods.ActiveModId);
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var sub = s.SubliminalPool;
        custody.Release();
        custody.Restore(s);
        Assert.DoesNotContain(raised, PoolNames.Contains);
        Assert.Same(sub, s.SubliminalPool);
        Assert.Equal(byMod, JsonConvert.SerializeObject(s.SubliminalPoolByMod));
    }

    // ---- With the real ModService: Bambi (A) and Sissy (B) each carry marker pools in ByMod. ----

    private const string A = BuiltInMods.BambiSleepId, B = BuiltInMods.SissyHypnoId;

    private ModService Launch(bool freshProfile = true, bool backupB = true)
    {
        Directory.CreateDirectory(CorePaths.UserData);
        if (freshProfile)
        {
            if (_svc != null) { _svc.SaveImmediate(); _svc.SealForReset(); }
            JObject Pools(string p)
            {
                var o = new JObject { [A] = JObject.FromObject(D("A" + p)) };
                if (backupB) o[B] = JObject.FromObject(D("B" + p));
                return o;
            }
            File.WriteAllText(Path.Combine(CorePaths.UserData, "settings.json"), new JObject
            {
                ["ActiveModId"] = A,
                ["SubliminalPoolByMod"] = Pools("sub"), ["BouncingTextPoolByMod"] = Pools("bounce"), ["LockCardPhrasesByMod"] = Pools("lock"),
            }.ToString());
        }
        else { _svc!.SaveImmediate(); _svc.SealForReset(); }
        var svc = _svc = new SettingsService();
        CoreSettings.ServiceProvider = () => svc;
        var mods = new ModService();
        mods.Initialize(svc.Current.ActiveModId);
        CoreMods.MakeModAwareProvider = mods.MakeModAware;
        CoreMods.ActiveModIdProvider = () => mods.ActiveModId;
        CoreMods.ReapplyActiveModPoolsProvider = mods.ReapplyActiveModPools;
        return mods;
    }

    private AppSettings S => _svc!.Current;

    private PhrasePoolCustody Start(SessionSettings? session = null) =>
        PhrasePoolCustody.Begin(S, session ?? Prescribing(), CoreMods.ActiveModId);

    private void End(PhrasePoolCustody c) { c.Release(); c.Restore(S); }

    /// <summary>Owns the mod's own marker (e.g. "Bsub") and none of the other mod's.</summary>
    private static void IsMods(string mod, IDictionary<string, bool> pool, string p)
    {
        var (mine, other) = mod == A ? ("A" + p, "B" + p) : ("B" + p, "A" + p);
        Assert.True(pool.TryGetValue(mine, out var on) && on, $"{p}: missing {mine}");
        Assert.False(pool.ContainsKey(other), $"{p}: leaked {other}");
        Assert.DoesNotContain(pool.Keys, k => k.StartsWith("zz"));
    }

    private static void AllMods(string mod, AppSettings s)
    {
        IsMods(mod, s.SubliminalPoolByMod![mod], "sub");
        IsMods(mod, s.BouncingTextPoolByMod![mod], "bounce");
        IsMods(mod, s.LockCardPhrasesByMod![mod], "lock");
    }

    [Fact]
    public void MidSessionEdit_SurvivesEnd_AndBackupHasNoSessionPhrases()
    {
        Launch();
        var c = Start();
        S.SubliminalPool = new Dictionary<string, bool>(S.SubliminalPool) { ["mine"] = true }; // editor save
        Assert.True(S.SubliminalPoolByMod![A]["mine"]);
        AllMods(A, S);
        End(c);
        Assert.True(S.SubliminalPool["mine"]);
        IsMods(A, S.SubliminalPool, "sub");
    }

    [Fact]
    public void ModSwitch_ReassertsOnlyPrescribed()
    {
        var mods = Launch();
        Start(Prescribing(bounce: false, lockCard: false));
        var prescribed = new Dictionary<string, bool>(S.SubliminalPool);
        mods.ActivateMod(B);
        Assert.Equal(prescribed, S.SubliminalPool);
        IsMods(B, S.BouncingTextPool, "bounce");
        IsMods(B, S.LockCardPhrases, "lock");
    }

    [Fact]
    public void ModSwitch_OutgoingBackupIsUserPool()
    {
        var mods = Launch();
        Start();
        mods.ActivateMod(B);
        AllMods(A, S);
    }

    /// <summary>Guards the D1 pair: the Reapply re-base and the reconcile-after-all-pools in Restore. Either
    /// alone makes it pass, so it fails only when both are reverted (WPF's behaviour); no isolated test needed.</summary>
    [Fact]
    public void ModSwitch_End_AllThreePoolsAreIncomingMods()
    {
        var mods = Launch();
        var c = Start();
        mods.ActivateMod(B);
        End(c);
        IsMods(B, S.SubliminalPool, "sub");
        IsMods(B, S.BouncingTextPool, "bounce");
        IsMods(B, S.LockCardPhrases, "lock");
    }

    [Fact]
    public void ModSwitchThenEdit_IncomingBackupClean()
    {
        var mods = Launch();
        Start();
        mods.ActivateMod(B);
        S.BouncingTextPool = new Dictionary<string, bool>(S.BouncingTextPool) { ["mine"] = true };
        Assert.True(S.BouncingTextPoolByMod![B]["mine"]);
        AllMods(B, S);
    }

    [Fact]
    public void DoubleSwitch_BackupOfBIsB()
    {
        var mods = Launch();
        Start();
        mods.ActivateMod(B);
        mods.ActivateMod(A);
        AllMods(B, S);
        AllMods(A, S);
    }

    [Fact]
    public void CrashMidSession_RelaunchRestoresFromByMod()
    {
        Launch();
        Start().Release(); // the process dies: no Restore, flat pools still hold the session's phrases
        Assert.True(S.SubliminalPool.Any(k => k.Key.StartsWith("zz") && k.Value));
        Launch(freshProfile: false);
        IsMods(A, S.SubliminalPool, "sub");
        IsMods(A, S.BouncingTextPool, "bounce");
        IsMods(A, S.LockCardPhrases, "lock");
    }

    /// <summary>D1a: no ByMod[B], so RestorePoolsFromSettings falls back to (and self-heals ByMod[B] from)
    /// the flat pools - which mid-session are the session's. It must take the user's instead: ByMod[B]
    /// comes out exactly as the same switch with no session running.</summary>
    [Fact]
    public void ModSwitch_ToModWithoutBackup_BackupIsUserPools()
    {
        string BackupB() => JsonConvert.SerializeObject(new { a = S.SubliminalPoolByMod![B], b = S.BouncingTextPoolByMod![B], c = S.LockCardPhrasesByMod![B] });

        string Flat() => JsonConvert.SerializeObject(new { S.SubliminalPool, S.BouncingTextPool, S.LockCardPhrases });

        var reference = Launch(backupB: false);
        reference.ActivateMod(B);
        var noSession = BackupB();
        reference.ReapplyActiveModPools();
        var noSessionFlat = Flat();

        var mods = Launch(backupB: false);
        var c = Start();
        var prescribed = (new Dictionary<string, bool>(S.SubliminalPool), new Dictionary<string, bool>(S.BouncingTextPool), new Dictionary<string, bool>(S.LockCardPhrases));
        mods.ActivateMod(B);
        Assert.Equal(noSession, BackupB());
        Assert.Equal(prescribed.Item1, S.SubliminalPool);
        Assert.Equal(prescribed.Item2, S.BouncingTextPool);
        Assert.Equal(prescribed.Item3, S.LockCardPhrases);

        End(c);
        Assert.Equal(noSessionFlat, Flat()); // what the end reconcile gives with no session
        Assert.True(S.SubliminalPool["Asub"]);
        mods.ActivateMod(A);
        foreach (var pool in new[] { S.SubliminalPoolByMod![B], S.BouncingTextPoolByMod![B], S.LockCardPhrasesByMod![B] })
            Assert.DoesNotContain(pool.Keys, k => k.StartsWith("zz"));
    }
}
