using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>This head's construction of the Core <see cref="ChasterService"/> (WPF
/// Services/Chaster/ChasterServiceApp.CreateForApp): the same options, read from CoreSettings,
/// with the token in <see cref="SecretChasterTokenStore"/>.
/// <para>Sandbox rule, the same as the catalogue and the updater: a CCP_USERDATA_DIR profile
/// (tests, Keincheck) never reaches the real proxy or api.chaster.app. Only an honoured loopback
/// <see cref="EnvVar"/> is called there; both hosts are rewritten onto it, path and query kept.</para>
/// </summary>
internal static class ChasterHead
{
    internal const string EnvVar = "CCP_CHASTER_API_URL";

    /// <summary>WPF App.Chaster. Null until startup built it (and on the headless render path).</summary>
    internal static ChasterService? Service { get; set; }

    internal static ChasterService Create(string? userDataDir, string? overrideUrl, HttpMessageHandler? inner = null)
    {
        Func<ChasterOptions> options = () =>
        {
            var s = CoreSettings.Current;
            return new ChasterOptions(s.ChasterTabEnabled, s.ChasterLockId, new HashSet<string>(s.ChasterPrices ?? new List<string>(), StringComparer.Ordinal),
                TabLimits.FromMinutes(LimitChange.Effective(s.ChasterDayLimit, DateTime.UtcNow),
                    LimitChange.Effective(s.ChasterBacklogLimit, DateTime.UtcNow)),
                // ponytail: RemoteOpen is always false - Remote Control's service is not on this head yet.
                RemoteOpen: false,
                PanicArmed: s.PanicKeyEnabled,
                RelockPastEnd: s.ChasterRelockPastEnd,
                Paused: s.ChasterPaused,
                PriceOverrides: new Dictionary<string, int>(s.ChasterPriceOverrides ?? new Dictionary<string, int>(), StringComparer.Ordinal));
        };
        // ponytail: no MinutesOn (the feature day log is not on this head) and no LadderApi/raffle
        // (the ladder slice); both are optional on the service and read as "nobody can tell".
        return new ChasterService(
            new ChasterClient(new Route(Target(userDataDir, overrideUrl), inner ?? new HttpClientHandler()),
                $"ConditioningControlPanel/{CoreReleaseContent.AppVersion}"),
            new SecretChasterTokenStore(),
            Path.Combine(CorePaths.UserData, "chaster_tab.json"),
            options);
    }

    /// <summary>WPF ChasterHooks.Attach, for the events this head raises: quests (and the dailies
    /// board) and level-ups. Every call is inert until the tab is on and the row is priced.
    /// ponytail: no program_done / program_skipped (ProgramService is not constructed on this head)
    /// and no RemoteOpen; hook them here when those services arrive. Escape: Lockdown tripwires that
    /// EscapeKinds.CostsChaster (WPF ChasterHooks.cs:73).</summary>
    /// Returns the detach (the static LevelUp outlives any one service); a second Attach of the same service is a no-op,
    /// as WPF's _attached guard makes it.
    internal static Action Attach(ChasterService chaster, QuestService? quests)
    {
        if (!Attached.Add(chaster)) return () => { };
        EventHandler<QuestCompletedEventArgs>? done = null, board = null;
        EventHandler? refreshed = null;
        if (quests != null)
        {
            quests.QuestCompleted += done = (_, e) => Safe(() => chaster.Note(e.QuestType == Models.QuestType.Weekly ? "quest_weekly" : "quest"));
            quests.QuestCompleted += board = (_, _) => Safe(() => chaster.NoteQuestBoard(OpenDailies(quests)));
            quests.QuestsRefreshed += refreshed = (_, _) => Safe(() => chaster.NoteQuestBoard(OpenDailies(quests)));
            Safe(() => chaster.NoteQuestBoard(OpenDailies(quests)));
        }
        Action<int> levelUp = _ => Safe(() => chaster.Note("levelup"));
        ProgressionBank.LevelUp += levelUp;
        var lockdown = LockdownService.Current;
        Action<Services.Possession.EscapeAttempt> escape = a => Safe(() => { if (Services.Possession.EscapeKinds.CostsChaster(a.Kind)) chaster.Note("escape"); });
        if (lockdown != null) lockdown.EscapeAttempted += escape;
        return () =>
        {
            ProgressionBank.LevelUp -= levelUp;
            if (lockdown != null) lockdown.EscapeAttempted -= escape;
            if (quests != null) { quests.QuestCompleted -= done; quests.QuestCompleted -= board; quests.QuestsRefreshed -= refreshed; }
            Attached.Remove(chaster);
        };
    }

    private static readonly HashSet<ChasterService> Attached = new();

    private static int OpenDailies(QuestService quests) => quests.Progress?.DailyQuests?.Count(q => q != null && !q.IsCompleted) ?? 0;

    private static void Safe(Action book)
    {
        try { book(); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] hook"); }
    }

    /// <summary>Where calls go: the loopback override, else nowhere in a sandbox (null), else
    /// the real hosts (the base itself).</summary>
    internal static Uri? Target(string? userDataDir, string? overrideUrl) =>
        LoopbackUrl.IsHonoured(overrideUrl, out var u) ? u
        : string.IsNullOrEmpty(userDataDir) ? new Uri(ChasterClient.ProxyBase) : null;

    /// <summary>The consent page URL the browser gets, under the same rule. Null: do not open it.</summary>
    internal static string? BrowserUrl(string url) =>
        Target(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"), Environment.GetEnvironmentVariable(EnvVar)) is { } t
            ? Rewrite(new Uri(url), t).ToString() : null;

    private static Uri Rewrite(Uri u, Uri target) =>
        target.Host == new Uri(ChasterClient.ProxyBase).Host ? u
            : new UriBuilder(u) { Scheme = target.Scheme, Host = target.Host, Port = target.Port }.Uri;

    private sealed class Route(Uri? target, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (target is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = r });
            r.RequestUri = Rewrite(r.RequestUri!, target);
            return base.SendAsync(r, ct);
        }
    }
}
