using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Turns the SPLIT IDENTITY account shape from a log line into a fix the player can accept.
///
/// <para>The shape: one provider's sign-in (Patreon or Discord) minted its own unified record
/// at some point, so this install runs as record A (<c>Settings.UnifiedId</c>) while the
/// server's <c>&lt;provider&gt;_index</c> points at record B. The validate guards in
/// <see cref="PatreonService"/> and <see cref="DiscordService"/> catch it (the server hands
/// back a token for a record that is not ours) and, until 2026-09-02, only logged
/// "needs a server-side merge". The player saw nothing at all: the app kept working on the
/// local grace, and the one surface that asks the server for a tier, the Arcademy bank,
/// answered "does not serve this account" to a paying patron (SissyBambi0301, #ask-support).</para>
///
/// <para>Now the guards report here, the main window attaches itself once it is up, and the
/// player is asked once per launch whether to merge record B into record A. The server route
/// (<c>/v2/auth/merge-provider</c>) does the merge on proof of both halves (our session
/// token for A, the live provider token for B) and refuses anything that is not that exact
/// shape, so a "No" or a refusal leaves everything as it was.</para>
/// </summary>
public static class SplitIdentityService
{
    private static readonly object Gate = new();
    private static string? _provider;        // "patreon" | "discord"
    private static string? _otherUnifiedId;  // record B, where the provider index points
    private static string? _localUnifiedId;  // record A, this session
    private static Window? _owner;
    private static bool _busy;
    private static string? _handledPair;     // "<provider>:<other>" already offered this launch

    /// <summary>A mismatch has been reported and not yet offered to the player.</summary>
    public static bool HasPending { get { lock (Gate) return _provider != null; } }

    /// <summary>
    /// Called by the validate guards the moment the server resolves a provider to a record
    /// other than this session's. Idempotent per launch per pair; the first report wins.
    /// </summary>
    public static void NoteMismatch(string provider, string? serverUnifiedId, string? localUnifiedId)
    {
        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(serverUnifiedId) || string.IsNullOrEmpty(localUnifiedId))
            return;

        Window? owner;
        lock (Gate)
        {
            var pair = $"{provider}:{serverUnifiedId}";
            if (string.Equals(_handledPair, pair, StringComparison.Ordinal)) return;
            if (_provider != null) return;
            _provider = provider;
            _otherUnifiedId = serverUnifiedId;
            _localUnifiedId = localUnifiedId;
            owner = _owner;
        }
        App.Logger?.Information("[SplitIdentity] {Provider} resolves to {Other} while this session is {Local}; will offer a merge",
            provider, serverUnifiedId, localUnifiedId);
        if (owner != null) Schedule(owner);
    }

    /// <summary>Main window is up; anything already pending is offered now, later reports as they come.</summary>
    public static void AttachOwner(Window owner)
    {
        bool pending;
        lock (Gate)
        {
            _owner = owner;
            pending = _provider != null;
        }
        if (pending) Schedule(owner);
    }

    private static void Schedule(Window owner)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        _ = dispatcher.InvokeAsync(async () =>
        {
            try
            {
                // Same breathing room the pending-registration dialog takes, so the prompt
                // lands on a painted window rather than under the splash.
                await Task.Delay(2500);
                await OfferMergeAsync(owner);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[SplitIdentity] offer failed");
                lock (Gate) _busy = false;
            }
        });
    }

    private static async Task OfferMergeAsync(Window owner)
    {
        string provider, other, local;
        lock (Gate)
        {
            if (_busy || _provider == null) return;
            _busy = true;
            provider = _provider;
            other = _otherUnifiedId!;
            local = _localUnifiedId!;
        }

        try
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;

            // The session may have moved on (logout, relog) between the report and now.
            if (!string.Equals(App.Settings?.Current?.UnifiedId, local, StringComparison.Ordinal))
            {
                App.Logger?.Information("[SplitIdentity] session is no longer {Local}; dropping the offer", local);
                return;
            }

            var accessToken = provider == "patreon" ? App.Patreon?.GetAccessToken() : App.Discord?.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                App.Logger?.Warning("[SplitIdentity] no {Provider} access token on hand; cannot offer", provider);
                return;
            }

            var label = provider == "patreon" ? "Patreon" : "Discord";
            var v2 = new V2AuthService();

            // Dry run first: it proves both halves server-side and hands back the two names
            // and levels the prompt needs. Nothing is written.
            var probe = await v2.MergeProviderAsync(local, provider, accessToken, dryRun: true);
            if (!probe.Success)
            {
                App.Logger?.Warning("[SplitIdentity] dry run refused ({Status} {Code}): {Error}", probe.StatusCode, probe.ErrorCode, probe.Error);
                // A rail (409) is a shape only support can untangle; say so once. Anything
                // transient (429/5xx, no network) stays quiet and comes back next launch.
                if (probe.StatusCode == 409)
                {
                    MessageBox.Show(owner, Loc.GetF("split_identity_failed", probe.Error ?? probe.ErrorCode ?? "?"),
                        Loc.Get("split_identity_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }
            if (probe.AlreadySame)
            {
                App.Logger?.Information("[SplitIdentity] server already resolves {Provider} to this session; nothing to merge", provider);
                return;
            }

            var otherName = probe.Loser?.DisplayName ?? other;
            var otherLevel = probe.Loser?.Level ?? 0;
            var localName = probe.Canonical?.DisplayName ?? App.UserDisplayName ?? local;
            var localLevel = probe.Canonical?.Level ?? 0;

            var body = Loc.GetF("split_identity_body", label, otherName, otherLevel, localName, localLevel);
            var answer = MessageBox.Show(owner, body, Loc.Get("split_identity_title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                App.Logger?.Information("[SplitIdentity] player declined the merge of {Other} into {Local}", other, local);
                return;
            }

            var done = await v2.MergeProviderAsync(local, provider, accessToken, dryRun: false);
            if (!done.Success)
            {
                App.Logger?.Warning("[SplitIdentity] merge refused ({Status} {Code}): {Error}", done.StatusCode, done.ErrorCode, done.Error);
                MessageBox.Show(owner, Loc.GetF("split_identity_failed", done.Error ?? done.ErrorCode ?? "?"),
                    Loc.Get("split_identity_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            App.Logger?.Information("[SplitIdentity] merged {Other} into {Local} (merge_id={MergeId}, support_id={SupportId})",
                other, local, done.MergeId, done.SupportId);

            // The provider index now points at us: a re-validate lands the tier and a token
            // that matches this record, and the profile pull brings the merged ledger down.
            try
            {
                if (App.Patreon?.IsAuthenticated == true) await App.Patreon.ValidateSubscriptionAsync(forceRefresh: true);
                if (App.Discord?.IsAuthenticated == true) await App.Discord.ValidateAndRefreshUserAsync(forceRefresh: true);
                if (App.ProfileSync != null) await App.ProfileSync.LoadProfileAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[SplitIdentity] post-merge refresh failed (the merge itself succeeded)");
            }

            MessageBox.Show(owner, Loc.GetF("split_identity_merged", localName, otherName),
                Loc.Get("split_identity_title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            lock (Gate)
            {
                _handledPair = $"{provider}:{other}";
                _provider = null;
                _otherUnifiedId = null;
                _localUnifiedId = null;
                _busy = false;
            }
        }
    }
}
