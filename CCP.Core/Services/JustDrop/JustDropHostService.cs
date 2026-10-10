using System;
using Serilog;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// The head-free half of WPF 7.1.5 Services/JustDrop/JustDropHostService.cs: where the Just Drop
    /// window opens and which single request carries the app's credential.
    ///
    /// <para><b>The sign-in handoff.</b> The app holds a ccp-server token; the site wants its own
    /// session. The window's FIRST navigation is <c>/api/auth/desktop-session</c>, which takes the
    /// token as a HEADER (never the url, so it stays out of history and the Referer), mints the
    /// session server-side and redirects to <c>next</c>. <see cref="AuthHeaderFor"/> answers for that
    /// one path on that one origin and for nothing else the page or anything it embeds asks for.</para>
    ///
    /// <para>The window itself is the head's (CCP.Avalonia Views/Games/GameWindow.JustDrop.cs), which
    /// seeds <see cref="LaunchShopProvider"/> / <see cref="LaunchReplayProvider"/>.</para>
    /// </summary>
    internal static class JustDropHostService
    {
        /// <summary>The dashboard app. Bare cclabs.app is the marketing site and serves none of this.</summary>
        public const string SiteHost = "app.cclabs.app";

        internal const string SiteOrigin = "https://" + SiteHost;

        /// <summary>The shop: browse, order, wallet.</summary>
        internal const string ShopPath = "/dashboard/express";

        /// <summary>The bare player: no wallet and no payout, so a replay is free by construction.</summary>
        internal const string PlayerPath = "/express/play";

        internal const string HandoffPath = "/api/auth/desktop-session";

        public const string AuthHeaderName = "X-CCP-Auth-Token";

        /// <summary>Head seams. Unseeded: nothing opens (logged).</summary>
        public static volatile Action? LaunchShopProvider;
        public static volatile Action<string>? LaunchReplayProvider;
        public static volatile Func<bool>? IsActiveProvider;
        public static volatile Action? CloseActiveProvider;

        public static bool IsActive
        {
            get { try { return IsActiveProvider?.Invoke() == true; } catch { return false; } }
        }

        /// <summary>Open the shop: windowed, the panel stays where it is (this is browsing).</summary>
        public static void LaunchShop()
        {
            var open = LaunchShopProvider;
            if (open == null) { Log.Debug("JustDropHost: no window on this head"); return; }
            open();
        }

        /// <summary>Replay a delivered order in the bare player.</summary>
        public static void LaunchReplay(string orderCode)
        {
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                Log.Warning("JustDropHost: replay asked for with no order code");
                return;
            }
            var open = LaunchReplayProvider;
            if (open == null) { Log.Debug("JustDropHost: no window on this head"); return; }
            open(orderCode.Trim());
        }

        /// <summary>Close the window if one is open. Safe to call when there is none.</summary>
        public static void CloseActive()
        {
            try { CloseActiveProvider?.Invoke(); }
            catch (Exception ex) { Log.Debug("JustDropHost.CloseActive: {E}", ex.Message); }
        }

        internal static string ReplayPath(string orderCode) =>
            $"{PlayerPath}?order={Uri.EscapeDataString(orderCode.Trim())}";

        /// <summary>
        /// Where the window opens. With an account in hand that is the sign-in handoff carrying the
        /// real destination in <c>next</c>; signed out it is the destination itself and the site asks
        /// who is calling. The token is never part of the url.
        /// </summary>
        internal static string BuildStartUrl(string nextPath) =>
            BuildStartUrl(nextPath, SafeUnifiedId(), SafeAuthToken());

        internal static string BuildStartUrl(string nextPath, string? unifiedId, string? authToken)
        {
            if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(authToken))
            {
                Log.Information("JustDropHost: no account state; opening signed-out");
                return SiteOrigin + nextPath;
            }
            return $"{SiteOrigin}{HandoffPath}" +
                   $"?unified_id={Uri.EscapeDataString(unifiedId!)}" +
                   $"&next={Uri.EscapeDataString(nextPath)}";
        }

        /// <summary>
        /// The app's token for <paramref name="request"/>, or null. Only the handoff path on the
        /// site's own https origin gets it: no other request this page makes ever carries the credential.
        /// </summary>
        internal static string? AuthHeaderFor(Uri? request) => AuthHeaderFor(request, SafeAuthToken());

        internal static string? AuthHeaderFor(Uri? request, string? authToken)
        {
            if (string.IsNullOrEmpty(authToken) || request is not { IsAbsoluteUri: true }) return null;
            if (request.Scheme != Uri.UriSchemeHttps || !request.IsDefaultPort) return null;
            if (!string.Equals(request.Host, SiteHost, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.IsNullOrEmpty(request.UserInfo)) return null;
            return string.Equals(request.AbsolutePath, HandoffPath, StringComparison.Ordinal) ? authToken : null;
        }

        /// <summary>The window's navigation rule (WPF PrimaryHost): the site's own https origin only.</summary>
        internal static bool IsSiteUrl(Uri? url) =>
            url is { IsAbsoluteUri: true } && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort
            && string.Equals(url.Host, SiteHost, StringComparison.OrdinalIgnoreCase);

        /// <summary>The ccp-server token. Empty when signed out. Never logged, never put in a url.</summary>
        private static string SafeAuthToken()
        {
            try { return CoreSettings.Current.AuthToken ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>The account the token belongs to; the server cross-validates the pair.</summary>
        private static string SafeUnifiedId()
        {
            try { return CoreAccount.UnifiedUserId ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
