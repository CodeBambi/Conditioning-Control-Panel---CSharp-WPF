using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Reading the two different 409s that <c>POST /v2/auth/link</c> can answer with (ccp-server
    /// proxy/server.js, the /v2/auth/link handler):
    ///
    /// <list type="bullet">
    /// <item><c>"Patreon already linked to this account"</c> - the record we are signed in as
    /// ALREADY carries this patreon_id. Nothing failed: the OAuth tokens are stored before the link
    /// call, so by the time this arrives the desktop is reconnected. It is the normal answer for a
    /// patron repairing a dead grant and must read as a success.</item>
    /// <item><c>"Patreon account already linked to a different user"</c> - this identity belongs to
    /// somebody else's record. A real refusal, and the user has to be told.</item>
    /// </list>
    ///
    /// <para>The two strings share most of their words, which is why this lives in one tested place
    /// rather than an inline Contains that could be made to swallow the wrong one.</para>
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

            // Checked first and on its own: the different-user refusal must never be mistaken for
            // the harmless one, whatever else the sentence picks up later.
            if (error.Contains("different user", StringComparison.OrdinalIgnoreCase)) return false;
            if (error.Contains("different account", StringComparison.OrdinalIgnoreCase)) return false;

            return error.Contains("already linked to this account", StringComparison.OrdinalIgnoreCase);
        }
    }
}
