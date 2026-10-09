using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// WPF AccountService.LinkProviderV2Async without its dialogs: link the provider that just signed in
/// (its OAuth token) to the unified account already signed in. "Already linked to this account" is a
/// quiet success (Core <see cref="ProviderLinkResponseRules"/>); the different-user refusal is not.
/// The caller paints the outcome (Settings &gt; Account).
/// </summary>
internal static class AccountLink
{
    internal enum Outcome { NoAccount, NoToken, Linked, AlreadyLinked, Failed }

    /// <param name="provider">"patreon" or "discord".</param>
    /// <param name="v2">Tests only.</param>
    /// <param name="accessToken">Tests only: the provider token (default: the seeded provider's).</param>
    internal static async Task<(Outcome outcome, string? error)> LinkAsync(string provider, V2AuthService? v2 = null, string? accessToken = null)
    {
        var s = CoreSettings.Current;
        var unifiedId = s.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId)) return (Outcome.NoAccount, null);

        accessToken ??= provider == "patreon" ? AccountSeed.Patreon?.GetAccessToken() : AccountSeed.Discord?.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            Log.Warning("AccountLink: no access token available for linking {Provider}", provider);
            return (Outcome.NoToken, null);
        }

        try
        {
            var result = await (v2 ?? AccountSeed.NewV2()).LinkProviderAsync(unifiedId, provider, accessToken);
            var already = !result.Success && ProviderLinkResponseRules.IsAlreadyLinkedToThisAccount(result.Error);
            if (!result.Success && !already)
            {
                Log.Warning("AccountLink: failed to link {Provider}: {Error}", provider, result.Error);
                return (Outcome.Failed, result.Error);
            }

            if (provider == "discord") s.HasLinkedDiscord = true;
            else if (provider == "patreon") s.HasLinkedPatreon = true;
            // A fresh token comes back on a real link only (#455 rotation).
            if (!string.IsNullOrEmpty(result.AuthToken)) s.AuthToken = result.AuthToken;
            CoreSettings.Save();
            Log.Information("AccountLink: {Status} {Provider} to {UnifiedId}", already ? "already linked" : "linked", provider, unifiedId);
            return (already ? Outcome.AlreadyLinked : Outcome.Linked, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AccountLink: link failed for {Provider}", provider);
            return (Outcome.Failed, null);
        }
    }
}
