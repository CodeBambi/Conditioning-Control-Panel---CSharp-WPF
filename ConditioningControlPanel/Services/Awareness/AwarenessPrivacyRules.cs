using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>Why a candidate never became a frame. One value, one sentence, one log token.</summary>
    public enum AwarenessDropReason
    {
        /// <summary>Nothing was dropped — the frame may be cut.</summary>
        None = 0,

        /// <summary>No resolvable app identity. We do not know what we are looking at, so we do not look.</summary>
        NoAppId = 1,

        /// <summary>No window title at all, which means the incognito check could not run. Fail closed.</summary>
        NoTitle = 2,

        /// <summary>A private-browsing window. Hard drop, non-negotiable, not user-configurable.</summary>
        Incognito = 3,

        /// <summary>Matched the deny list (the user's entries or a seeded group).</summary>
        DenyList = 4,

        /// <summary>She is paused (<see cref="AwarenessPause"/>).</summary>
        Paused = 5,

        /// <summary>The privacy layer threw. There is exactly one safe answer to that.</summary>
        Error = 6
    }

    /// <summary>What the observer wants an answer about: one foreground window, as resolved so far.</summary>
    /// <param name="AppId">Resolved app id (<c>AppClusterMap</c> bespoke id, else the process name).</param>
    /// <param name="DisplayName">Human-facing service/app name, e.g. "YouTube". May be null.</param>
    /// <param name="Cluster">Cluster id, e.g. "site_doomscroll". Null when unclustered — that is normal.</param>
    /// <param name="RawTitle">The raw window title. Never stored, never projected; read and discarded here.</param>
    public readonly record struct AwarenessSightRequest(
        string? AppId,
        string? DisplayName,
        string? Cluster,
        string? RawTitle);

    /// <summary>
    /// The privacy layer's answer. <see cref="TitleForWire"/> is non-null only when the app was
    /// title-allow-listed by the user AND the title survived sanitising — everything else gets null,
    /// which is what keeps <c>ContextFrame.PageTitleSanitized</c> empty for the shipped default.
    /// </summary>
    public sealed record AwarenessPrivacyDecision(bool Allowed, AwarenessDropReason Reason, string? TitleForWire)
    {
        /// <summary>A drop, with the reason that produced it and no title.</summary>
        public static AwarenessPrivacyDecision Drop(AwarenessDropReason reason) => new(false, reason, null);

        /// <summary>True when a page title may accompany this frame.</summary>
        public bool TitleAllowed => TitleForWire != null;

        /// <summary>The <c>[AWARE]</c> log token for this decision. Never carries the title or the raw one.</summary>
        public string LogToken => Allowed
            ? (TitleAllowed ? "allow+title" : "allow")
            : "drop:" + Reason.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// What she is allowed to look at — the half of Awareness v2 that cannot be walked back after it
    /// ships (doc 02 §6).
    ///
    /// <para><b>The rules, in force order.</b> Every one of them is a drop, and a drop means no frame,
    /// no ledger write and no reaction:</para>
    /// <list type="number">
    ///   <item>the layer threw, or there is no app id, or there is no window title (the incognito check
    ///   could not run) → drop. Fail closed: the audit's registry fix and this deny-list design agree
    ///   on that;</item>
    ///   <item>private browsing — "InPrivate", "Incognito", "Private Browsing" and their shipped-language
    ///   equivalents (<see cref="IncognitoMarkers"/>) → drop. Hard-coded, not a setting: a private window
    ///   is an explicit statement, and a privacy guard the user can accidentally switch off is decoration;</item>
    ///   <item>she is paused → drop;</item>
    ///   <item>the deny list — the user's own entries plus whichever seeded groups are still in it →
    ///   drop.</item>
    /// </list>
    ///
    /// <para><b>Titles are opt-in, per app.</b> <see cref="Evaluate"/> returns a title only when the app
    /// is named in <c>AwarenessTitleAllowList</c> (ships EMPTY), the app is not an email client
    /// (subject lines are never eligible, whatever the list says), the cluster is not the adult one, and
    /// the title survives <see cref="SanitizeTitleForWire"/>. That is the inversion: today every page
    /// title goes to the cloud, and from v2 none does until the user names an app.</para>
    ///
    /// <para><b>Seeded groups, not hidden constants.</b> The recommended defaults are three tokens —
    /// <see cref="GroupPasswordManagers"/>, <see cref="GroupBanking"/>, <see cref="GroupEmailTitles"/> —
    /// seeded once into the user's own deny list (<see cref="EnsureSeeded"/>) so they are visible in the
    /// privacy panel and removable there. Until the seed has run, <see cref="Evaluate"/> applies them
    /// anyway, so the protection never depends on start-up ordering; once it has run, the user's list is
    /// authoritative and deleting a chip really deletes the rule.</para>
    ///
    /// <para><b>Untrusted input.</b> Every entry the user types is passed through
    /// <see cref="AwarenessText.SanitizeRuleEntry"/> by the settings setters — length-capped, control
    /// characters dropped, wildcard characters removed and anything that would collapse to
    /// "match everything" rejected. A deny list that silently matches nothing and an allow list that
    /// silently widens to every app are the same bug, and the second one leaks.</para>
    /// </summary>
    public static class AwarenessPrivacyRules
    {
        // =====================================================================================
        //  seeded groups
        // =====================================================================================

        /// <summary>Deny token for the password-manager family. Expands to <see cref="PasswordManagerTerms"/>.</summary>
        public const string GroupPasswordManagers = "@passwords";

        /// <summary>Deny token for the banking heuristic. Expands to <see cref="BankingTerms"/>.</summary>
        public const string GroupBanking = "@banking";

        /// <summary>
        /// Deny token for mail clients' TITLES. Unlike the other two this does not hide the app — the
        /// fact that Outlook is open is not a secret; the subject line in its title bar is.
        /// </summary>
        public const string GroupEmailTitles = "@email-titles";

        /// <summary>The three tokens <see cref="EnsureSeeded"/> writes into a fresh deny list.</summary>
        public static IReadOnlyList<string> SeededDenyList { get; } = new[]
        {
            GroupPasswordManagers, GroupBanking, GroupEmailTitles
        };

        /// <summary>
        /// Password managers and vaults. Matched against the app id, the display name and the raw title,
        /// because "1Password" reaches us as a process name on the desktop app and as a title fragment
        /// in a browser tab.
        /// </summary>
        public static IReadOnlyList<string> PasswordManagerTerms { get; } = new[]
        {
            "1password", "keepass", "keepassxc", "bitwarden", "lastpass", "dashlane", "nordpass",
            "roboform", "enpass", "keeper password", "keeper security", "protonpass", "proton pass",
            "authy", "vaultwarden", "padloc", "passbolt", "psono", "safeincloud", "sticky password",
            "truekey", "true key", "zoho vault", "pwsafe", "password safe", "credential manager"
        };

        /// <summary>
        /// The banking-in-title heuristic: an editable list of common bank, card and broker names plus a
        /// few generic banking words. Deliberately data-ish rather than clever — a regex for "money"
        /// would match half the internet, and the cost of a false positive here is one missed joke.
        /// </summary>
        public static IReadOnlyList<string> BankingTerms { get; } = new[]
        {
            // generic
            "online banking", "internet banking", "mobile banking", "net banking", "netbanking",
            "online-banking", "banque en ligne", "onlinebanking", "banking login", "bank statement",
            "account summary", "wire transfer", "sort code", "iban",
            // US / CA
            "chase", "bank of america", "wells fargo", "citibank", "citi.com", "capital one",
            "us bank", "u.s. bank", "pnc bank", "truist", "td bank", "usaa", "navy federal",
            "ally bank", "discover card", "american express", "amex", "schwab", "fidelity investments",
            "vanguard", "e*trade", "etrade", "robinhood", "coinbase", "rbc royal bank", "scotiabank",
            "bmo", "cibc", "desjardins",
            // UK / IE
            "barclays", "lloyds bank", "halifax", "natwest", "santander", "nationwide building",
            "monzo", "starling bank", "revolut", "hsbc", "tsb bank", "bank of ireland", "aib",
            // EU
            "sparkasse", "volksbank", "commerzbank", "deutsche bank", "ing-diba", "postbank",
            "raiffeisen", "bnp paribas", "credit agricole", "crédit agricole", "societe generale",
            "société générale", "caisse d'epargne", "la banque postale", "bbva", "caixabank",
            "unicredit", "intesa sanpaolo", "rabobank", "abn amro", "nordea", "swedbank",
            // APAC / LATAM
            "commonwealth bank", "westpac", "anz bank", "nab internet", "icici", "hdfc bank",
            "state bank of india", "dbs bank", "ocbc", "uob", "mufg", "mizuho", "rakuten bank",
            "kakaobank", "banco do brasil", "bradesco", "itau", "itaú", "nubank", "santander rio",
            // payments / tax
            "paypal", "stripe dashboard", "wise transfer", "hmrc", "irs.gov", "tax return"
        };

        /// <summary>
        /// Mail clients and webmail. Their app id stays visible; their titles never become eligible for
        /// the allow list while <see cref="GroupEmailTitles"/> is in the deny list.
        /// </summary>
        public static IReadOnlyList<string> EmailClientTerms { get; } = new[]
        {
            "outlook", "thunderbird", "gmail", "mail.google", "proton mail", "protonmail",
            "em client", "emclient", "mailbird", "postbox", "evolution mail", "kmail",
            "yahoo mail", "zoho mail", "fastmail", "tutanota", "hey.com", "roundcube",
            "windows mail", "apple mail", "spark mail", "bluemail", "gmx", "web.de", "mail.ru"
        };

        /// <summary>
        /// Private-browsing markers, including the shipped languages' real browser strings (Chrome's
        /// "Incognito", Edge's "InPrivate", Firefox's "Private Browsing" and their localisations).
        ///
        /// <para>Matched case-insensitively anywhere in the raw title. A false positive costs one joke;
        /// a false negative costs the promise printed on the consent dialog.</para>
        /// </summary>
        public static IReadOnlyList<string> IncognitoMarkers { get; } = new[]
        {
            // en
            "incognito", "inprivate", "in private", "private browsing", "private window",
            "private tab", "private mode",
            // de
            "inkognito", "privates surfen", "privates fenster",
            // es
            "incógnito", "incognito", "navegación privada", "ventana privada",
            // fr
            "navigation privée", "fenêtre de navigation privée", "fenêtre privée",
            // pt-BR
            "anônima", "navegação anônima", "janela anônima", "navegação privativa",
            // ru
            "инкогнито", "приватный просмотр", "приватное окно",
            // ja
            "シークレット", "プライベートブラウジング", "プライベートウィンドウ",
            // ko
            "시크릿", "사생활 보호 모드", "인프라이빗",
            // zh-CN
            "无痕", "隐身", "隐私浏览", "隐私窗口", "无痕浏览"
        };

        // =====================================================================================
        //  evaluation
        // =====================================================================================

        /// <summary>
        /// The one call the observer makes before anything is recorded or said. Reads the live settings
        /// itself so there is exactly one place these rules live.
        ///
        /// <para>Never throws: any failure inside becomes <see cref="AwarenessDropReason.Error"/> and a
        /// drop. "Send it and hope" is not one of the outcomes.</para>
        /// </summary>
        public static AwarenessPrivacyDecision Evaluate(AwarenessSightRequest request)
        {
            try
            {
                return Evaluate(request, App.Settings?.Current, DateTime.Now);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AWARE] privacy layer threw — dropping the frame");
                return AwarenessPrivacyDecision.Drop(AwarenessDropReason.Error);
            }
        }

        /// <summary>
        /// The testable body. <paramref name="settings"/> null reads as "nothing configured", which
        /// still applies the seeded groups and the hard-coded incognito rule.
        /// </summary>
        public static AwarenessPrivacyDecision Evaluate(AwarenessSightRequest request, AppSettings? settings, DateTime now) =>
            Evaluate(request, EffectiveDenyList(settings), settings?.AwarenessTitleAllowList, now);

        /// <summary>
        /// The rule body, with the two lists supplied by the caller.
        ///
        /// <para><b>Why the lists are a parameter.</b> "One dialect" is about the MATCHING — group-token
        /// expansion, what a rule is matched against, the incognito and pause and adult rules — not
        /// about who owns the array. The observer already snapshots the lists once per tick
        /// (<c>AwarenessPolicySettings.FromSettings</c>) rather than re-reading settings 40 times a
        /// minute, and it must be able to hand that snapshot in. Both callers reach the same matcher,
        /// which is the property that matters: the panel cannot display one set of rules while the
        /// observer enforces another.</para>
        ///
        /// <para><paramref name="denyList"/> is expected to be the EFFECTIVE list — see
        /// <see cref="EffectiveDenyList"/>, which applies the seeded groups until the user's own list
        /// exists. Passing the raw setting silently disables the password-manager, banking and email
        /// groups.</para>
        /// </summary>
        public static AwarenessPrivacyDecision Evaluate(
            AwarenessSightRequest request,
            IReadOnlyList<string>? denyList,
            IReadOnlyList<string>? titleAllowList,
            DateTime now)
        {
            try
            {
                var appId = AwarenessText.SanitizeId(request.AppId);
                if (appId == AwarenessText.UnknownId)
                    return AwarenessPrivacyDecision.Drop(AwarenessDropReason.NoAppId);

                // No title means the incognito test cannot run, and the incognito test is the one rule
                // that has no user-facing escape hatch. An unanswerable question is a drop.
                if (string.IsNullOrWhiteSpace(request.RawTitle))
                    return AwarenessPrivacyDecision.Drop(AwarenessDropReason.NoTitle);

                if (LooksIncognito(request.RawTitle))
                    return AwarenessPrivacyDecision.Drop(AwarenessDropReason.Incognito);

                if (AwarenessPause.IsPaused(now))
                    return AwarenessPrivacyDecision.Drop(AwarenessDropReason.Paused);

                var deny = denyList ?? Array.Empty<string>();
                var display = AwarenessText.SanitizeDisplayName(request.DisplayName);

                // The cluster is matched too, so a rule can silence a whole category ("site_doomscroll")
                // and not just one app. The title is matched for the same reason a group's terms are:
                // "Chase Online" arrives as a browser tab, not as a process called chase.exe.
                if (MatchesAny(deny, appId, display, request.RawTitle, request.Cluster))
                    return AwarenessPrivacyDecision.Drop(AwarenessDropReason.DenyList);

                return new AwarenessPrivacyDecision(true, AwarenessDropReason.None,
                    ResolveTitle(request, appId, display, deny, titleAllowList));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AWARE] privacy layer threw — dropping the frame");
                return AwarenessPrivacyDecision.Drop(AwarenessDropReason.Error);
            }
        }

        /// <summary>
        /// True when the raw title says the user opened a private window. Hard-coded on purpose; see the
        /// class remarks.
        /// </summary>
        public static bool LooksIncognito(string? rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return false;
            var lower = rawTitle.ToLowerInvariant();
            foreach (var marker in IncognitoMarkers)
            {
                if (lower.Contains(marker, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether this app's page title may be carried at all. False for every app until the user adds
        /// one, and false for mail clients and the adult cluster whatever the list says.
        /// </summary>
        public static bool IsTitleAllowed(string? appId, string? displayName, string? cluster, AppSettings? settings)
        {
            var id = AwarenessText.SanitizeId(appId);
            if (id == AwarenessText.UnknownId) return false;

            var allow = settings?.AwarenessTitleAllowList;
            if (allow == null || allow.Count == 0) return false;

            // The adult cluster sends its cluster id and nothing else, regardless of any allow list.
            if (string.Equals(cluster, AwarenessClusters.Adult, StringComparison.OrdinalIgnoreCase)) return false;

            var display = AwarenessText.SanitizeDisplayName(displayName);
            var deny = EffectiveDenyList(settings);
            if (deny.Any(e => TokenIs(e, GroupEmailTitles)) && MatchesAny(EmailClientTerms, id, display, null, null)) return false;

            return MatchesAny(allow, id, display, null, null);
        }

        /// <summary>
        /// The projection-safe form of a page title: emails and long digit runs removed, whitespace
        /// collapsed, control characters and role markers dropped (<see cref="AwarenessText"/>), capped
        /// at <see cref="MaxTitleLength"/>. Returns null when nothing usable survived.
        /// </summary>
        public static string? SanitizeTitleForWire(string? rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return null;

            var stripped = EmailPattern.Replace(rawTitle, " ");
            stripped = LongDigitsPattern.Replace(stripped, " ");

            var collapsed = new StringBuilder(stripped.Length);
            bool lastWasSpace = false;
            foreach (var ch in stripped)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastWasSpace && collapsed.Length > 0) collapsed.Append(' ');
                    lastWasSpace = true;
                    continue;
                }
                lastWasSpace = false;
                collapsed.Append(ch);
            }

            var clean = AwarenessText.SanitizeDisplayName(collapsed.ToString().Trim(), MaxTitleLength);
            return string.IsNullOrWhiteSpace(clean) ? null : clean;
        }

        /// <summary>Longest page title that may cross the wire when one is allowed at all.</summary>
        public const int MaxTitleLength = 80;

        // =====================================================================================
        //  seeding
        // =====================================================================================

        /// <summary>
        /// Writes the three recommended groups into the user's deny list, once, and records that it
        /// happened. Returns true when it actually seeded.
        ///
        /// <para>Called from the consent flow — the moment awareness is first switched on — so the
        /// defaults exist before anything is observed. It is idempotent and cheap, so any other entry
        /// point may call it too. After it has run, the user's list is the whole truth: removing a chip
        /// removes the rule, and this never puts it back.</para>
        /// </summary>
        public static bool EnsureSeeded(AppSettings? settings)
        {
            if (settings == null) return false;
            if (settings.AwarenessDenySeeded) return false;

            var merged = new List<string>(settings.AwarenessDenyList ?? new List<string>());
            foreach (var token in SeededDenyList)
            {
                if (!merged.Contains(token, StringComparer.OrdinalIgnoreCase)) merged.Add(token);
            }

            settings.AwarenessDenyList = merged;   // setter sanitises
            settings.AwarenessDenySeeded = true;
            App.Logger?.Information("Awareness: seeded the recommended deny groups ({Count} entries)",
                settings.AwarenessDenyList.Count);
            return true;
        }

        /// <summary>True when this entry is one of the seeded group tokens rather than a typed substring.</summary>
        public static bool IsGroupToken(string? entry) =>
            !string.IsNullOrWhiteSpace(entry) && entry.TrimStart().StartsWith("@", StringComparison.Ordinal) &&
            (TokenIs(entry, GroupPasswordManagers) || TokenIs(entry, GroupBanking) || TokenIs(entry, GroupEmailTitles));

        /// <summary>The loc key naming a deny entry in the panel — a friendly label for a group token.</summary>
        public static string ChipLabelKey(string? entry) => (entry ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            GroupPasswordManagers => "companion_awareness_deny_passwords",
            GroupBanking => "companion_awareness_deny_banking",
            GroupEmailTitles => "companion_awareness_deny_email",
            _ => string.Empty
        };

        /// <summary>
        /// The deny list actually applied: the user's entries, plus the seeded groups while the seed has
        /// not run yet. Never null.
        /// </summary>
        public static IReadOnlyList<string> EffectiveDenyList(AppSettings? settings)
        {
            var user = settings?.AwarenessDenyList;
            if (settings != null && settings.AwarenessDenySeeded)
                return user ?? (IReadOnlyList<string>)Array.Empty<string>();

            var merged = new List<string>(SeededDenyList);
            if (user != null)
            {
                foreach (var entry in user)
                {
                    if (!merged.Contains(entry, StringComparer.OrdinalIgnoreCase)) merged.Add(entry);
                }
            }
            return merged;
        }

        // =====================================================================================
        //  internals
        // =====================================================================================

        private static readonly Regex EmailPattern =
            new(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex LongDigitsPattern =
            new(@"\d{6,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static bool TokenIs(string? entry, string token) =>
            string.Equals(entry?.Trim(), token, StringComparison.OrdinalIgnoreCase);

        private static string? ResolveTitle(
            AwarenessSightRequest request, string appId, string display,
            IReadOnlyList<string> deny, IReadOnlyList<string>? allow)
        {
            if (allow == null || allow.Count == 0) return null;
            if (string.Equals(request.Cluster, AwarenessClusters.Adult, StringComparison.OrdinalIgnoreCase)) return null;
            if (deny.Any(e => TokenIs(e, GroupEmailTitles)) && MatchesAny(EmailClientTerms, appId, display, null, null)) return null;

            // Matched against the app's IDENTITY only — never the title, and never the cluster. A title
            // containing its own allow key would otherwise allow-list itself, and allowing a whole
            // cluster's titles is a far wider grant than "name an app you're fine with".
            if (!MatchesAny(allow, appId, display, null, null)) return null;

            return SanitizeTitleForWire(request.RawTitle);
        }

        /// <summary>
        /// Substring match of a rule list against an app id, a display name and (optionally) the raw
        /// title and the cluster. Group tokens expand to their term lists; everything else is a plain
        /// case-insensitive substring, which is what the panel's copy says it is.
        ///
        /// <para><paramref name="cluster"/> is supplied for the DENY list only, so one rule can silence
        /// a whole category. It is deliberately null for the title allow list — see
        /// <see cref="ResolveTitle"/>.</para>
        /// </summary>
        private static bool MatchesAny(
            IReadOnlyList<string>? rules, string? appId, string? displayName, string? rawTitle, string? cluster)
        {
            if (rules == null || rules.Count == 0) return false;

            var id = (appId ?? string.Empty).ToLowerInvariant();
            var name = (displayName ?? string.Empty).ToLowerInvariant();
            var title = (rawTitle ?? string.Empty).ToLowerInvariant();
            var group = (cluster ?? string.Empty).ToLowerInvariant();

            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule)) continue;

                if (IsGroupToken(rule))
                {
                    var terms = TermsFor(rule);
                    // A group's terms are matched against the title too: "Chase Online" arrives as a
                    // browser tab, not as a process called chase.exe.
                    if (MatchesTerms(terms, id, name, title)) return true;
                    continue;
                }

                var needle = rule.Trim().ToLowerInvariant();
                if (needle.Length < 2) continue;   // defence in depth; the setter already drops these
                if (id.Contains(needle, StringComparison.Ordinal)) return true;
                if (name.Length > 0 && name.Contains(needle, StringComparison.Ordinal)) return true;
                if (title.Length > 0 && title.Contains(needle, StringComparison.Ordinal)) return true;

                // Cluster ids are exact tokens ("site_doomscroll"), not free text: a substring match
                // here would let a two-character rule silence every cluster that contains it.
                if (group.Length > 0 && string.Equals(group, needle, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static bool MatchesTerms(IReadOnlyList<string> terms, string id, string name, string title)
        {
            foreach (var term in terms)
            {
                if (id.Contains(term, StringComparison.Ordinal)) return true;
                if (name.Length > 0 && name.Contains(term, StringComparison.Ordinal)) return true;
                if (title.Length > 0 && title.Contains(term, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static IReadOnlyList<string> TermsFor(string token) => token.Trim().ToLowerInvariant() switch
        {
            GroupPasswordManagers => PasswordManagerTerms,
            GroupBanking => BankingTerms,
            // The email group hides titles, not apps — see GroupEmailTitles. It contributes no app match.
            _ => Array.Empty<string>()
        };

        /// <summary>
        /// A human-readable one-liner for the <c>[AWARE]</c> log. Carries the reason and the app id only:
        /// the raw title never reaches a log line, which is the same promise the ledger keeps by not
        /// having a parameter for one.
        /// </summary>
        public static string LogLine(AwarenessSightRequest request, AwarenessPrivacyDecision decision) =>
            string.Create(CultureInfo.InvariantCulture,
                $"[AWARE] privacy app={AwarenessText.SanitizeId(request.AppId)} verdict={decision.LogToken}");
    }
}
