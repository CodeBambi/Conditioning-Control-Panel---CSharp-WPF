using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Reading the two different 409s that <c>POST /v2/auth/link</c> can answer with.
    ///
    /// <para>The proxy refuses a link for two opposite reasons and says so in plain English
    /// (ccp-server proxy/server.js, the /v2/auth/link handler):</para>
    /// <list type="bullet">
    /// <item><c>"Patreon already linked to this account"</c> - the record we are signed in as
    /// ALREADY carries this patreon_id. Nothing failed. The OAuth tokens were stored before the
    /// link call, so by the time this 409 arrives the desktop is reconnected. This is the normal
    /// answer for a patron repairing a dead grant, and it must read as a success.</item>
    /// <item><c>"Patreon account already linked to a different user"</c> - this Patreon identity
    /// belongs to somebody else's record. A real refusal, and the user has to be told.</item>
    /// </list>
    ///
    /// <para>The two strings share most of their words, which is exactly why this lives in one
    /// tested place instead of an inline Contains: a substring check written the obvious way from
    /// either end can be made to swallow the wrong one.</para>
    /// </summary>
    public static class ProviderLinkResponseRules
    {
        /// <summary>
        /// True when the refusal means "you are already linked, to yourself" and the caller should
        /// treat the attempt as a quiet success. False for every other error, including the
        /// different-user conflict, a merged-account tombstone, a bare HTTP code and null.
        /// </summary>
        public static bool IsAlreadyLinkedToThisAccount(string? error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;

            // Checked first and on its own: "linked to a different user" is the refusal that must
            // never be mistaken for the harmless one, whatever else the sentence picks up later.
            if (error.Contains("different user", StringComparison.OrdinalIgnoreCase)) return false;
            if (error.Contains("different account", StringComparison.OrdinalIgnoreCase)) return false;

            return error.Contains("already linked to this account", StringComparison.OrdinalIgnoreCase);
        }
    }
}
