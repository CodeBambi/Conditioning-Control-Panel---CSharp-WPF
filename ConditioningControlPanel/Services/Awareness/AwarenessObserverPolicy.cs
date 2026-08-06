using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>Why a candidate never became a frame. Every value is a DROP, not a delay.</summary>
    public enum FrameDrop
    {
        /// <summary>Nothing was dropped.</summary>
        None = 0,

        /// <summary>No usable foreground window, or a blank title AND a blank process name.</summary>
        NoForeground,

        /// <summary>
        /// The title says private browsing. A hard drop, ahead of every list and every toggle:
        /// private mode is an explicit statement by the user and she does not see it, period.
        /// </summary>
        Incognito,

        /// <summary>The user's deny list matched. No frame, no ledger write, no reaction, ever.</summary>
        DenyListed,

        /// <summary>
        /// The privacy layer could not answer — settings unreadable, or the classifier threw. When the
        /// question "may I look at this?" has no answer, the answer is no.
        /// </summary>
        PolicyUnavailable,

        /// <summary>Adult cluster with adult RECORDING switched off: nothing is written and no frame is cut.</summary>
        AdultRecordingOff,

        /// <summary>
        /// The user pressed pause (<see cref="AwarenessPause"/>). Nothing is recorded and nothing is
        /// said until it expires — a pause that only muted her while still counting would be a lie
        /// with a button on it. Process-lifetime only; it does not survive a restart.
        /// </summary>
        Paused
    }

    /// <summary>Why a cut frame was not offered to the arbiter. The frame IS in the ledger; she can joke later.</summary>
    public enum DndGate
    {
        /// <summary>Speak away.</summary>
        None = 0,

        /// <summary>Adult cluster with adult REACTIONS switched off (recording may still be on).</summary>
        AdultReactionsOff,

        /// <summary>A mandatory video, lock card or DtRH run is on screen. She already has lines there.</summary>
        CcpSurface,

        /// <summary>A meeting app is in the foreground AND the microphone is live.</summary>
        Meeting,

        /// <summary>Fullscreen with recent input — playing or presenting. The payoff is the exit-fullscreen beat.</summary>
        Fullscreen,

        /// <summary>Sustained typing. Interrupting someone mid-sentence is the least funny thing she can do.</summary>
        TypingBurst
    }

    /// <summary>
    /// The privacy-relevant settings the observer reads, snapshotted per tick.
    ///
    /// <para>A record rather than direct settings reads so the whole policy layer is pure and
    /// testable, and so "settings unreadable" can be expressed as <c>null</c> — which the observer
    /// treats as <see cref="FrameDrop.PolicyUnavailable"/>.</para>
    /// </summary>
    public sealed record AwarenessPolicySettings(
        IReadOnlyList<string> DenyList,
        IReadOnlyList<string> TitleAllowList,
        bool AdultReactionsEnabled,
        bool AdultRecordingEnabled)
    {
        /// <summary>
        /// Reads the live settings, or null when they cannot be read at all. Entries are re-sanitised
        /// on the way out even though the setters already do it: a list mutated in place by UI code
        /// would otherwise reach the matcher unchecked.
        /// </summary>
        public static AwarenessPolicySettings? FromSettings()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return null;

                return new AwarenessPolicySettings(
                    // EFFECTIVE, not raw: the shipped protection is three group tokens that
                    // AwarenessPrivacyRules applies whether or not the one-time seed has run yet.
                    // Reading s.AwarenessDenyList directly is how the password-manager, banking and
                    // email-title groups silently stop blocking anything.
                    AwarenessPrivacyRules.EffectiveDenyList(s),
                    AwarenessText.SanitizeRuleList(s.AwarenessTitleAllowList),
                    s.AwarenessAdultReactionsEnabled,
                    s.AwarenessAdultRecordingEnabled);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>The privacy layer's answer about one foreground sample.</summary>
    /// <param name="PageTitleSanitized">
    /// Non-null ONLY when the app is on the user's title allow list. The shipped allow list is empty,
    /// so this is null for everyone by default.
    /// </param>
    public sealed record PrivacyVerdict(
        FrameDrop Drop,
        string AppId,
        string? Cluster,
        ActivityCategory Category,
        string ServiceName,
        string? PageTitleSanitized)
    {
        /// <summary>True when the sample may be recorded and considered.</summary>
        public bool Allowed => Drop == FrameDrop.None;

        /// <summary>A drop, carrying nothing about what was dropped.</summary>
        public static PrivacyVerdict Dropped(FrameDrop drop) =>
            new(drop, AwarenessText.UnknownId, null, ActivityCategory.Unknown, "", null);
    }

    /// <summary>Everything the do-not-disturb decision needs, as a flat value bag.</summary>
    public sealed record DndInput(
        bool IsFullscreen,
        int InputIdleSeconds,
        string ProcessName,
        bool MicrophoneInUse,
        bool IsTypingBurst,
        bool CcpSurfaceActive,
        bool IsAdultCluster,
        bool AdultReactionsEnabled);

    /// <summary>
    /// The observer's pure decision layer: identity resolution, the privacy drops, and the
    /// do-not-disturb matrix. Extracted from <see cref="AwarenessObserver"/> so all of it is unit
    /// tested without a desktop, a dispatcher or a running app.
    ///
    /// <para><b>Ordering is a privacy invariant, not a style choice.</b> The incognito and deny-list
    /// drops happen in <see cref="EvaluatePrivacy"/>, which the observer calls BEFORE any ledger write
    /// and before any frame is built. There is deliberately no code path in which a dropped sample can
    /// reach <see cref="ActivityLedger.NoteFocus"/>.</para>
    ///
    /// <para><b>Fail closed.</b> A missing settings object, a classifier that throws and an
    /// unresolvable window all produce a drop, never a "send it and hope".</para>
    /// </summary>
    public static class AwarenessObserverPolicy
    {
        /// <summary>Longest sanitised title placed on a frame, for an allow-listed app.</summary>
        public const int MaxTitleLength = 120;

        /// <summary>Real input idle under which fullscreen counts as "playing", not "left running".</summary>
        public const int FullscreenRecentInputSeconds = 30;

        /// <summary>
        /// Private-browsing markers, matched case-insensitively anywhere in the title. Hard-coded
        /// rather than configurable (doc 02 §6.1) and deliberately generous across locales: a false
        /// positive costs one joke, a false negative reads a session the user explicitly hid.
        /// </summary>
        private static readonly string[] IncognitoMarkers =
        {
            "incognito", "inprivate", "in private", "private browsing", "private window",
            "privatfenster", "privates fenster", "inkognito",
            "navigation privée", "navigation privee", "fenêtre privée", "fenetre privee",
            "modo incógnito", "modo incognito", "navegación privada", "navegacion privada",
            "navegação privada", "navegacao privada", "janela privada",
            "navigazione anonima", "finestra anonima",
            "privé-venster", "prive-venster", "privé venster",
            "prywatne", "okno prywatne", "приватное", "инкогнито",
            "無痕", "无痕", "隱私瀏覽", "シークレット", "プライベート", "프라이빗", "시크릿"
        };

        /// <summary>
        /// Foreground processes that mean "call in progress" when the microphone is also live. The mic
        /// is what makes it a meeting: Teams idling in the background is not a standup.
        /// </summary>
        private static readonly string[] MeetingProcesses =
        {
            "zoom", "teams", "msteams", "ms-teams", "meet", "webex", "gotomeeting", "bluejeans"
        };

        /// <summary>
        /// Browser processes. For these the TITLE is the identity (the site is the app); for everything
        /// else the PROCESS is, which is what kills doc 02 §1.6's substring lottery.
        /// </summary>
        private static readonly string[] BrowserProcesses =
        {
            "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "librewolf",
            "waterfox", "safari", "iexplore", "arc", "zen", "floorp", "chromium", "thorium"
        };

        private static readonly Regex EmailPattern =
            new(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex LongNumberPattern =
            new(@"\d{6,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // ===================== privacy =====================

        /// <summary>
        /// Turns a raw foreground sample into either a drop or a resolved, title-free identity.
        ///
        /// <para><b>This resolves identity; it does not decide privacy.</b> The decision — incognito,
        /// pause, deny list, whether a title may be carried — belongs to
        /// <see cref="AwarenessPrivacyRules.Evaluate(AwarenessSightRequest, AppSettings?, DateTime)"/>,
        /// which is the same call the consent dialog and the privacy panel describe to the user. A
        /// second implementation here would be a second dialect: the panel would show one set of rules
        /// and the observer would enforce another. Concretely, this method used to match the deny list
        /// literally, which silently never expanded the seeded group tokens
        /// (<c>password-managers</c>, <c>banking</c>, <c>email-clients</c>) — so the chips the panel
        /// displayed as active rules blocked nothing.</para>
        ///
        /// <para>Evaluation order, which is the contract:</para>
        /// <list type="number">
        /// <item>no policy (settings unreadable) → <see cref="FrameDrop.PolicyUnavailable"/>;</item>
        /// <item>no usable window → <see cref="FrameDrop.NoForeground"/>;</item>
        /// <item>incognito title → <see cref="FrameDrop.Incognito"/>, ahead of every list and before
        /// anything is classified, so a private window is never even resolved. Re-checked inside the
        /// shared rules; this is defence in depth, not the enforcing copy;</item>
        /// <item>identity cannot be resolved → <see cref="FrameDrop.PolicyUnavailable"/>;</item>
        /// <item>adult cluster with recording off → <see cref="FrameDrop.AdultRecordingOff"/>. Observer-
        /// only: the shared rules have no notion of a recording toggle;</item>
        /// <item>the shared privacy rules: pause, deny list, title allow list;</item>
        /// <item>otherwise allowed, with the title carried ONLY if the app is title-allow-listed.</item>
        /// </list>
        /// </summary>
        public static PrivacyVerdict EvaluatePrivacy(ForegroundSample? sample, AwarenessPolicySettings? policy) =>
            EvaluatePrivacy(sample, policy, DateTime.Now);

        /// <summary>
        /// The testable body. <paramref name="now"/> is handed to the shared
        /// <see cref="AwarenessPrivacyRules"/> so a test can drive the pause window without waiting for
        /// one. The deny and title-allow lists come from <paramref name="policy"/>.
        /// </summary>
        public static PrivacyVerdict EvaluatePrivacy(
            ForegroundSample? sample, AwarenessPolicySettings? policy, DateTime now)
        {
            if (policy == null) return PrivacyVerdict.Dropped(FrameDrop.PolicyUnavailable);
            if (sample == null) return PrivacyVerdict.Dropped(FrameDrop.NoForeground);

            var title = sample.Title ?? "";
            var process = (sample.ProcessName ?? "").ToLowerInvariant();

            if (title.Trim().Length == 0 && process.Length == 0)
                return PrivacyVerdict.Dropped(FrameDrop.NoForeground);

            // Incognito first. Before classification, before the lists, before anything is resolved:
            // the cheapest way to be certain nothing about a private session is ever computed.
            if (IsIncognitoTitle(title)) return PrivacyVerdict.Dropped(FrameDrop.Incognito);

            string appId, serviceName;
            string? cluster;
            ActivityCategory category;
            try
            {
                (appId, cluster, category, serviceName) = ResolveIdentity(title, process);
            }
            catch (Exception ex)
            {
                // AppClusterMap reads a mod-supplied override file; a malformed one must not be able
                // to widen anything. When the classifier cannot answer, the frame does not exist.
                App.Logger?.Debug("AwarenessObserver: identity resolution failed - {Error}", ex.Message);
                return PrivacyVerdict.Dropped(FrameDrop.PolicyUnavailable);
            }

            bool adult = string.Equals(cluster, AwarenessClusters.Adult, StringComparison.OrdinalIgnoreCase);
            if (adult && !policy.AdultRecordingEnabled)
                return PrivacyVerdict.Dropped(FrameDrop.AdultRecordingOff);

            // The one privacy call. Same matcher the privacy panel and the consent dialog describe:
            // pause, deny list (seeded groups expanded, cluster rules honoured) and the title allow
            // list — failing closed on anything it cannot answer. The lists come from the per-tick
            // policy snapshot rather than a fresh settings read; `settings` is still passed for the
            // rules that read it directly.
            var decision = AwarenessPrivacyRules.Evaluate(
                new AwarenessSightRequest(appId, serviceName, cluster, title),
                policy.DenyList, policy.TitleAllowList, now);

            if (!decision.Allowed) return PrivacyVerdict.Dropped(MapDrop(decision.Reason));

            return new PrivacyVerdict(FrameDrop.None, appId, cluster, category, serviceName,
                decision.TitleForWire);
        }

        /// <summary>Maps the shared privacy layer's reason onto the observer's drop enum.</summary>
        private static FrameDrop MapDrop(AwarenessDropReason reason) => reason switch
        {
            AwarenessDropReason.Incognito => FrameDrop.Incognito,
            AwarenessDropReason.DenyList => FrameDrop.DenyListed,
            AwarenessDropReason.Paused => FrameDrop.Paused,
            AwarenessDropReason.NoAppId => FrameDrop.NoForeground,
            AwarenessDropReason.NoTitle => FrameDrop.NoForeground,
            // Error, and anything added later that this switch has not been taught: fail closed.
            _ => FrameDrop.PolicyUnavailable
        };

        /// <summary>True when the title says the user is in a private-browsing window.</summary>
        public static bool IsIncognitoTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            var lower = title.ToLowerInvariant();
            foreach (var marker in IncognitoMarkers)
            {
                if (lower.Contains(marker, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// The only function in the feature that may put title text on a frame, and only for an app the
        /// user allow-listed. Email addresses and runs of six or more digits (order numbers, account
        /// numbers, card fragments) are removed before anything else happens, then the usual display
        /// sanitiser strips control characters and instruction-shaped lines, then it is capped.
        /// </summary>
        public static string? SanitizeAllowedTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            var stripped = EmailPattern.Replace(title, " ");
            stripped = LongNumberPattern.Replace(stripped, " ");

            var collapsed = new StringBuilder(stripped.Length);
            bool lastWasSpace = false;
            foreach (var ch in stripped)
            {
                bool space = char.IsWhiteSpace(ch);
                if (space && lastWasSpace) continue;
                collapsed.Append(space ? ' ' : ch);
                lastWasSpace = space;
            }

            var clean = AwarenessText.SanitizeDisplayName(collapsed.ToString().Trim(), MaxTitleLength);
            return clean.Length == 0 ? null : clean;
        }

        /// <summary>
        /// Resolves (appId, cluster, category, serviceName) from a title and a process name.
        ///
        /// <para>Trust order: a bespoke <see cref="AppClusterMap"/> app id resolved from the process,
        /// then — for browsers only, where the site genuinely is the app — one resolved from the title,
        /// then the legacy dictionary's display name (browsers only, for the same reason), then the
        /// process name itself. The point of leading with the process is that a title substring is a
        /// lottery: "on target for the deadline" is not shopping.</para>
        /// </summary>
        public static (string AppId, string? Cluster, ActivityCategory Category, string ServiceName)
            ResolveIdentity(string? title, string? processName)
        {
            var rawTitle = title ?? "";
            var process = AwarenessText.SanitizeId(processName);
            bool isBrowser = IsBrowserProcess(processName);

            var (clusterFromTitle, appFromTitle) = AppClusterMap.Classify(rawTitle);
            var (clusterFromProcess, appFromProcess) = AppClusterMap.Classify(processName ?? "");

            var cluster = isBrowser
                ? clusterFromTitle ?? clusterFromProcess
                : clusterFromProcess ?? clusterFromTitle;

            var bespoke = isBrowser
                ? appFromTitle ?? appFromProcess
                : appFromProcess ?? appFromTitle;

            var (category, _, dictionaryService, _) = WindowAwarenessService.CategorizeWindow(rawTitle);
            bool usefulService = dictionaryService.Length > 0 &&
                                 !string.Equals(dictionaryService, "browser", StringComparison.OrdinalIgnoreCase);

            // The legacy dictionaries match a bare substring anywhere in the title, so outside a browser
            // they are only believed when the process agrees ("discord"/"Discord"). That is the whole of
            // doc 02 §1.6: a Slack window titled "on target for the deadline" is not shopping, and a
            // wrong SERVICE NAME reaches the model just as surely as a wrong app id would.
            bool serviceTrusted = usefulService && (isBrowser || ServiceAgreesWithProcess(dictionaryService, process));

            string appId;
            if (bespoke != null) appId = AwarenessText.SanitizeId(bespoke);
            else if (isBrowser && usefulService) appId = AwarenessText.SanitizeId(dictionaryService);
            else if (process != AwarenessText.UnknownId) appId = process;
            else if (serviceTrusted) appId = AwarenessText.SanitizeId(dictionaryService);
            else appId = AwarenessText.UnknownId;

            var serviceName = serviceTrusted
                ? AwarenessText.SanitizeDisplayName(dictionaryService)
                : AwarenessText.SanitizeDisplayName(PrettifyProcess(process));

            if (!serviceTrusted) category = ActivityCategory.Unknown;
            if (category == ActivityCategory.Unknown) category = CategoryFromCluster(cluster);

            return (appId, cluster, category, serviceName);
        }

        /// <summary>Coarse category for a cluster the legacy dictionaries had nothing to say about.</summary>
        public static ActivityCategory CategoryFromCluster(string? cluster)
        {
            if (string.IsNullOrWhiteSpace(cluster)) return ActivityCategory.Unknown;
            if (cluster.StartsWith("game_", StringComparison.OrdinalIgnoreCase)) return ActivityCategory.Gaming;

            return cluster.ToLowerInvariant() switch
            {
                "site_doomscroll" => ActivityCategory.Social,
                "site_video" => ActivityCategory.Media,
                "site_music" => ActivityCategory.Media,
                AwarenessClusters.Adult => ActivityCategory.Media,
                "site_shopping" => ActivityCategory.Shopping,
                _ => ActivityCategory.Unknown
            };
        }

        /// <summary>True when the process is a web browser, where the title is the identity.</summary>
        public static bool IsBrowserProcess(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            var lower = processName.ToLowerInvariant();
            foreach (var browser in BrowserProcesses)
            {
                if (string.Equals(lower, browser, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Matches one allow/deny list against the fields it is allowed to see. Entries are plain
        /// case-insensitive substrings (<see cref="AwarenessText.SanitizeRuleEntry"/> already refused
        /// wildcards and one-character entries, either of which would collapse to "match everything").
        /// Pass a null <paramref name="title"/> for lists that must not be matched against titles.
        /// </summary>
        public static bool MatchesAny(
            IReadOnlyList<string>? rules,
            string? title,
            string? processName,
            string? appId,
            string? cluster,
            string? serviceName)
        {
            if (rules == null || rules.Count == 0) return false;

            var haystacks = new[]
            {
                title?.ToLowerInvariant(),
                processName?.ToLowerInvariant(),
                appId?.ToLowerInvariant(),
                cluster?.ToLowerInvariant(),
                serviceName?.ToLowerInvariant()
            };

            foreach (var rule in rules)
            {
                if (string.IsNullOrEmpty(rule)) continue;
                foreach (var haystack in haystacks)
                {
                    if (haystack != null && haystack.Contains(rule, StringComparison.Ordinal)) return true;
                }
            }

            return false;
        }

        // ===================== do not disturb =====================

        /// <summary>
        /// The DND matrix (doc 02 §4.2). Suppressors are ordered by how badly the user would mind being
        /// interrupted, and the first match wins so the <c>[AWARE]</c> line names one honest reason.
        ///
        /// <para>Everything gated here has ALREADY been written to the ledger. That is the design: the
        /// fullscreen gate is what makes "so how many hours was <i>that</i>?" possible when the game
        /// finally closes, and turning a suppression into material is better than losing it.</para>
        /// </summary>
        public static DndGate EvaluateDnd(DndInput input)
        {
            if (input == null) return DndGate.None;

            // The user's own switch outranks every heuristic below it.
            if (input.IsAdultCluster && !input.AdultReactionsEnabled) return DndGate.AdultReactionsOff;

            if (input.CcpSurfaceActive) return DndGate.CcpSurface;

            if (input.MicrophoneInUse && IsMeetingProcess(input.ProcessName)) return DndGate.Meeting;

            if (input.IsFullscreen && input.InputIdleSeconds < FullscreenRecentInputSeconds)
                return DndGate.Fullscreen;

            if (input.IsTypingBurst) return DndGate.TypingBurst;

            return DndGate.None;
        }

        /// <summary>True when the process is a conferencing app. Half of the meeting test; the mic is the other half.</summary>
        public static bool IsMeetingProcess(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            var lower = processName.ToLowerInvariant();
            foreach (var meeting in MeetingProcesses)
            {
                if (lower.Contains(meeting, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a dictionary display name and a process name are plausibly the same thing —
        /// "Discord"/"discord", "VS Code"/"code". Compared on the first word of the display name, with
        /// a three-character floor so a two-letter fragment cannot agree with everything.
        /// </summary>
        private static bool ServiceAgreesWithProcess(string serviceName, string processId)
        {
            if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(processId)) return false;
            if (processId == AwarenessText.UnknownId) return false;

            var lower = serviceName.ToLowerInvariant();
            foreach (var word in lower.Split(new[] { ' ', '-', '.', '/', '+' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length < 3) continue;
                if (processId.Contains(word, StringComparison.Ordinal)) return true;
                if (word.Contains(processId, StringComparison.Ordinal) && processId.Length >= 3) return true;
            }

            return false;
        }

        private static string PrettifyProcess(string processId)
        {
            if (string.IsNullOrWhiteSpace(processId) || processId == AwarenessText.UnknownId) return "";
            return char.ToUpperInvariant(processId[0]) + processId.Substring(1);
        }
    }
}
