using System;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>The Core <see cref="FriendsService"/> on this head (WPF FriendsServiceApp). Sandbox rule as
/// <see cref="ChasterHead"/>: with CCP_USERDATA_DIR set only a loopback <see cref="EnvVar"/> is called;
/// with none the service is not built and the drawer reads signed out. Exception: the owner-approved
/// online desk sandbox (CCP_SANDBOX_ONLINE=1, as CorePaths) reaches the real proxy, so a signed-in desk
/// run shows its friends (2026-10-09: the drawer said "Friends need an account" while signed in).</summary>
internal static class FriendsHead
{
    internal const string EnvVar = "CCP_FRIENDS_API_URL";

    /// <summary>WPF App.Friends. Null until startup built it.</summary>
    internal static IFriendsService? Service { get; set; }

    /// <summary>WPF BackRoomApi.AppIdentity: null offline or without an id and a token.</summary>
    internal static (string UnifiedId, string Token)? Identity()
    {
        try { var s = CoreSettings.Current; return s.OfflineMode || string.IsNullOrWhiteSpace(s.UnifiedId) || string.IsNullOrWhiteSpace(s.AuthToken) ? null : (s.UnifiedId!, s.AuthToken!); }
        catch { return null; }
    }

    internal static string? BaseUrl(string? userDataDir, string? overrideUrl) =>
        BaseUrl(userDataDir, overrideUrl, SandboxOnline());

    internal static string? BaseUrl(string? userDataDir, string? overrideUrl, bool sandboxOnline) =>
        LoopbackUrl.IsHonoured(overrideUrl, out var u) ? u.ToString().TrimEnd('/')
        : string.IsNullOrEmpty(userDataDir) || sandboxOnline ? "https://codebambi-proxy.vercel.app" : null;

    /// <summary>The owner-approved online desk sandbox (CorePaths reads the same switch).</summary>
    internal static Func<bool> SandboxOnline { get; set; } = () =>
    {
        try { return Environment.GetEnvironmentVariable("CCP_SANDBOX_ONLINE") == "1"; } catch { return false; }
    };

    /// <summary>The invites wire rides the same proxy and door; a sandbox without a loopback url gets none.</summary>
    internal static void SeedInvites(string? userDataDir, string? overrideUrl)
    {
        var url = BaseUrl(userDataDir, overrideUrl);
        Services.Invites.InviteApi.DefaultIdentity = Identity;
        Services.Invites.InviteApi.DefaultBaseUrl = () => url;
        ContentPackService.DefaultBaseUrl = () => url;   // the pack catalogue + signed install ride the same proxy rule
    }

    internal static FriendsService? Create(string? userDataDir, string? overrideUrl)
    {
        if (BaseUrl(userDataDir, overrideUrl) is not { } url) return null;
        // ponytail: no Contract D hook (WPF MergedAccountRecovery.TryHandle is head-only); add it when that moves.
        return new FriendsService(new FriendsApi(null, Identity, url), () => Identity()?.UnifiedId, AnyWindowActive);
    }

    /// <summary>WPF FriendsServiceApp.AnyAppWindowActive.</summary>
    private static bool AnyWindowActive() =>
        global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
        && d.Windows.Any(w => w.IsActive);

    /// <summary>The poll's timer (WPF FriendsServiceApp.Timer): background priority, UI thread.</summary>
    internal sealed class Timer : IUiTimer
    {
        private readonly DispatcherTimer _t = new(DispatcherPriority.Background);

        public TimeSpan Interval { get => _t.Interval; set => _t.Interval = value; }
        public event EventHandler? Tick { add => _t.Tick += value; remove => _t.Tick -= value; }
        public void Start() => _t.Start();
        public void Stop() => _t.Stop();

        public void OnUiThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
        }
    }
}
