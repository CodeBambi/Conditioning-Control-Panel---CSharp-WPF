using System.Text;

namespace CcpClient.Desktop.Ai;

/// <summary>
/// F4 (audit row A4, ADOPT): the per-app page-title allow-list. <b>It ships EMPTY, and an empty
/// list carries NO title for anyone</b> — which NARROWS what the port did before this row, where a
/// granted awareness consent let the (scrubbed) title of every app through
/// (<see cref="AiAwarenessContextPackaging"/>). WPF's own setting says the same thing about
/// itself: "Ships EMPTY, which inverts today's behaviour: page titles currently go to the cloud
/// for every app" (Models/AppSettings.cs:4216-4218). Read the narrowing as the row, not as a
/// regression.
///
/// <para><b>The hard constraint, and it is the whole reason this row was buildable without an
/// owner answer.</b> The app identifier matched here is the <see cref="AiAwarenessContext.App"/>
/// field the CALLER supplies. It is never an observed process name, and nothing in this type reads
/// one: there is no process handle, no window handle, and no capability probe anywhere in it, so
/// the constraint is structural rather than a comment. Observing which application is in the
/// foreground is a new observation class the owner has not admitted (audit row A1 / owner question
/// Q2); a filter built against an observed process name would have crossed that boundary while
/// looking like a privacy narrowing.</para>
///
/// <para><b>Matching is identity-only</b> — WPF `ResolveTitle`, AwarenessPrivacyRules.cs:461-464:
/// "Matched against the app's IDENTITY only — never the title, and never the cluster. A title
/// containing its own allow key would otherwise allow-list itself, and allowing a whole cluster's
/// titles is a far wider grant than 'name an app you're fine with'." Plain case-insensitive
/// substring, exactly what WPF's `MatchesAny` does for a non-group rule
/// (AwarenessPrivacyRules.cs:501-505).</para>
///
/// <para><b>Deliberately not ported here:</b> WPF's group tokens (`@passwords`, `@banking`,
/// `@email-titles`) and the adult-cluster override (AwarenessPrivacyRules.cs:328-336). Both need
/// app identity or a cluster classification the port does not have; both are the audit's row A8,
/// whose seed CONTENTS are owner-supplied policy. An empty mechanism is honest; invented policy
/// values are not.</para>
///
/// <para>Session-scoped, never persisted — the same posture as the awareness consent and the
/// cooldown values beside it (a persisted entry would read as a decided consent granularity,
/// owner question Q5).</para>
/// </summary>
public sealed class AiTitleAllowList
{
    /// <summary>Longest allow-list entry (WPF `AwarenessText.MaxRuleLength`, :39 — verbatim 64).</summary>
    public const int MaxEntryLength = 64;

    /// <summary>How many entries the list may hold (WPF `AwarenessText.MaxRuleEntries`, :41-42 — verbatim 200: "beyond this it is not a list, it is a policy").</summary>
    public const int MaxEntries = 200;

    private readonly object _gate = new();
    private readonly List<string> _entries = [];

    /// <summary>The named apps, in the order the user named them. EMPTY on construction — the shipped default. Snapshot copy under the gate.</summary>
    public IReadOnlyList<string> Entries
    {
        get { lock (_gate) { return _entries.ToList(); } }
    }

    /// <summary>How many apps are named. Zero means no title travels for anything.</summary>
    public int Count
    {
        get { lock (_gate) { return _entries.Count; } }
    }

    /// <summary>
    /// Sanitises one user-typed entry, returning null when it must be dropped (WPF
    /// `AwarenessText.SanitizeRuleEntry`, :174-198 — verbatim): trimmed, control characters
    /// dropped, `*`/`%`/`?` dropped (they are neither wildcards nor literals here), lowercased,
    /// capped at <see cref="MaxEntryLength"/>, then rejected if under two characters ("one
    /// character matches half the machine", :188), if it reads as a prompt instruction, or if it
    /// holds no letter or digit at all. WPF's reason is the one that matters on THIS side of the
    /// pair: "a deny list that quietly means 'deny everything' and a title allow list that quietly
    /// means 'send every title' are the same class of bug, and the second one leaks" (:170-172).
    /// </summary>
    public static string? SanitizeEntry(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var sb = new StringBuilder(Math.Min(raw.Length, MaxEntryLength));
        foreach (var ch in raw.Trim())
        {
            if (sb.Length >= MaxEntryLength)
            {
                break;
            }

            if (char.IsControl(ch) || ch is '*' or '%' or '?')
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        var entry = sb.ToString().Trim();
        if (entry.Length < 2 || AiPrivacyFilters.LooksLikeInstruction(entry))
        {
            return null;
        }

        foreach (var ch in entry)
        {
            if (char.IsLetterOrDigit(ch))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Names one app. Returns false when the entry was rejected by <see cref="SanitizeEntry"/>,
    /// when it duplicates one already named (case-insensitive, WPF `SanitizeRuleList` :211-216),
    /// or when the list is already at <see cref="MaxEntries"/> (:214). A rejected add is a typed
    /// no-op, never a throw — the surface reports it.
    /// </summary>
    public bool Add(string? raw)
    {
        var entry = SanitizeEntry(raw);
        if (entry is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (_entries.Count >= MaxEntries || _entries.Contains(entry, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            _entries.Add(entry);
            return true;
        }
    }

    /// <summary>Un-names one app. Returns false when it was not named.</summary>
    public bool Remove(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        lock (_gate)
        {
            var index = _entries.FindIndex(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            _entries.RemoveAt(index);
            return true;
        }
    }

    /// <summary>
    /// Empties the list, so no title travels for anything. This is what selecting the dial's
    /// middle stop does (WPF: "'Broad strokes' is a promise that no page title travels, so
    /// selecting it empties the allow list", AwarenessPrivacyRuntimeVm.cs:97-101).
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>
    /// Whether this app's page title may be carried at all. False for every app until the user
    /// names one (WPF `IsTitleAllowed` :323-339 / `ResolveTitle` :453-466, both of which return
    /// early on a null-or-empty list). <paramref name="app"/> is the caller-supplied
    /// <see cref="AiAwarenessContext.App"/> field — see the type remarks for why it can never be
    /// anything else.
    /// </summary>
    public bool AllowsTitleFor(string? app)
    {
        if (string.IsNullOrWhiteSpace(app))
        {
            return false;
        }

        var candidate = app.ToLowerInvariant();
        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                // Entries are already sanitised to >= 2 characters; the length guard is WPF's own
                // defence in depth (AwarenessPrivacyRules.cs:502 — "the setter already drops these").
                if (entry.Length >= 2 && candidate.Contains(entry, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
