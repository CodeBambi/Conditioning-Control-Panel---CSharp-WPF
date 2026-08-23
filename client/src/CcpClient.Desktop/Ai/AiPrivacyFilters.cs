using System.Text;
using System.Text.RegularExpressions;

namespace CcpClient.Desktop.Ai;

/// <summary>
/// Three subtractive privacy filters over the landed awareness and reply paths
/// (board row :46; audit `her-room-divergence-audit.md` rows A6/A10/C3 — three rows of the
/// audit's §5 sizing table filtered at authoring for Size S / no dependency / unit evidence
/// only). Every rule here only ever REMOVES or NARROWS what flows: nothing is observed,
/// persisted, logged, or transmitted that was not before.
///
/// <para>F1 (A6, ADOPT): private/incognito window titles are hard-dropped. WPF carries TWO
/// divergent marker lists — `AwarenessPrivacyRules.IncognitoMarkers` (35 entries, :192) and
/// `AwarenessObserverPolicy.IncognitoMarkers` (35 entries, :169), 15 shared. The port takes
/// the UNION (55 entries) as its ONE definition: a marker on either WPF enforcement path
/// dropped that title, so the union never re-admits what any WPF path suppressed. Nine
/// entries are substring-subsumed by other union entries under Contains matching; kept for
/// verbatim fidelity (recorded in the packet record, not silently deduped).</para>
///
/// <para>F2 (A10, ADOPT): title scrubbing with WPF's exact values — no rounding, no
/// "improved" regexes. The 120-char cloud-projection cap (`AwarenessProjection.cs:217`) is
/// deliberately NOT ported: the port has no cloud provider (admission §2 rule 6, loopback
/// only), so it has no consumer.</para>
///
/// <para>F3 (C3, MERGE — strip half only): model-invented URLs are removed from companion
/// prose before it can reach memory, the bubble, or disk. The sigil-unwrap half
/// (`AiTextHygiene.UnwrapSpokenSigil`, :99) and the off-pool title rewrite
/// (`CompanionBrain.cs:274`) are DEFERRED BY NAME: the audit sizes "Strip only" as the
/// dependency-free half; the rewrite needs a media pool the port does not have, and the
/// unwrap guards a sigil the port's prompt path does not emit.</para>
///
/// <para>Sanctioned links: WPF's strip consults `CompanionLinkIndex.IsSanctioned` (the
/// app-issued media pool). The port has no link index and issues the model no links — every
/// reply URL is unsanctioned here (recorded divergence, narrowing direction).</para>
/// </summary>
public static class AiPrivacyFilters
{
    // =====================================================================================
    //  F1 — incognito hard-drop (audit row A6)
    // =====================================================================================

    /// <summary>
    /// Private-browsing markers, matched case-insensitively anywhere in the title. The UNION
    /// of WPF's two divergent lists (`AwarenessPrivacyRules.cs:192-216` and
    /// `AwarenessObserverPolicy.cs:169-183`) — one definition, every consumer routed through
    /// it (the one-definition discipline). Hard-coded, not a setting: WPF's own framing is that a false
    /// positive costs one joke, a false negative reads a session the user explicitly hid.
    /// </summary>
    public static IReadOnlyList<string> IncognitoMarkers { get; } =
    [
        // en (both lists)
        "incognito", "inprivate", "in private", "private browsing", "private window",
        // en (PrivacyRules only)
        "private tab", "private mode",
        // de (both: inkognito, privates fenster; PrivacyRules: privates surfen; ObserverPolicy: privatfenster)
        "inkognito", "privates surfen", "privates fenster", "privatfenster",
        // es (both: navegación privada; PrivacyRules: incógnito, ventana privada; ObserverPolicy: modo incógnito, modo incognito, navegacion privada)
        "incógnito", "modo incógnito", "modo incognito", "navegación privada", "navegacion privada", "ventana privada",
        // fr (both: navigation privée, fenêtre privée; PrivacyRules: fenêtre de navigation privée; ObserverPolicy: navigation privee, fenetre privee)
        "navigation privée", "navigation privee", "fenêtre privée", "fenetre privee", "fenêtre de navigation privée",
        // pt (PrivacyRules: anônima, navegação anônima, janela anônima, navegação privativa; ObserverPolicy: navegação privada, navegacao privada, janela privada)
        "anônima", "navegação anônima", "janela anônima", "navegação privativa", "navegação privada", "navegacao privada", "janela privada",
        // it (ObserverPolicy only)
        "navigazione anonima", "finestra anonima",
        // nl (ObserverPolicy only)
        "privé-venster", "prive-venster", "privé venster",
        // pl (ObserverPolicy only)
        "prywatne", "okno prywatne",
        // ru (both: инкогнито; PrivacyRules: приватный просмотр, приватное окно; ObserverPolicy: приватное)
        "инкогнито", "приватный просмотр", "приватное окно", "приватное",
        // ja (both: シークレット; PrivacyRules: プライベートブラウジング, プライベートウィンドウ; ObserverPolicy: プライベート)
        "シークレット", "プライベートブラウジング", "プライベートウィンドウ", "プライベート",
        // ko (both: 시크릿; PrivacyRules: 사생활 보호 모드, 인프라이빗; ObserverPolicy: 프라이빗)
        "시크릿", "사생활 보호 모드", "인프라이빗", "프라이빗",
        // zh (both: 无痕; PrivacyRules: 隐身, 隐私浏览, 隐私窗口, 无痕浏览; ObserverPolicy: 無痕, 隱私瀏覽)
        "无痕", "無痕", "隐身", "隐私浏览", "隱私瀏覽", "隐私窗口", "无痕浏览",
    ];

    /// <summary>
    /// True when the title says the user is in a private-browsing window (WPF
    /// `LooksIncognito`, AwarenessPrivacyRules.cs:308-316 / `IsIncognitoTitle`,
    /// AwarenessObserverPolicy.cs:346-355 — identical mechanics over divergent lists; the
    /// port routes both through this one). Blank returns false HERE, but the port's blank
    /// case is a DROP decided at the observation seam (<see cref="ClassifyCapturedTitle"/>) —
    /// WPF's net blank behavior is fail-closed (`AwarenessDropReason.NoTitle`,
    /// AwarenessPrivacyRules.cs:276-277), and this helper's fail-open branch is never the
    /// last word in the port.
    /// </summary>
    public static bool LooksIncognito(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var lower = title.ToLowerInvariant();
        foreach (var marker in IncognitoMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The F1 verdict for a captured foreground title (the port's observation-seam decision, framing e).</summary>
    public enum CapturedTitleVerdict
    {
        /// <summary>The title may leave the seam to the caller.</summary>
        Carry,

        /// <summary>Private-window marker: hard drop (WPF FrameDrop.Incognito, AwarenessObserverPolicy.cs:266 — "before classification, before the lists, before anything is resolved").</summary>
        DropIncognito,

        /// <summary>Empty/whitespace capture: drop (WPF net fail-closed — AwarenessPrivacyRules.cs:276-277, "an unanswerable question is a drop"; the port's capture path has no earlier guard).</summary>
        DropBlank,
    }

    /// <summary>
    /// The pure F1 decision at the observation seam, separated from the Win32 capture so the
    /// rule is unit-testable without a real foreground window. Incognito first (WPF order),
    /// then the blank case.
    /// </summary>
    public static CapturedTitleVerdict ClassifyCapturedTitle(string? title)
    {
        if (LooksIncognito(title))
        {
            return CapturedTitleVerdict.DropIncognito;
        }

        return string.IsNullOrWhiteSpace(title) ? CapturedTitleVerdict.DropBlank : CapturedTitleVerdict.Carry;
    }

    // =====================================================================================
    //  F2 — title scrubbing (audit row A10), WPF values verbatim
    // =====================================================================================

    /// <summary>Longest page title that may cross the wire when one is carried at all (WPF `MaxTitleLength`, AwarenessPrivacyRules.cs:372 — verbatim 80).</summary>
    public const int MaxTitleLength = 80;

    // WPF verbatim (AwarenessPrivacyRules.cs:444-448), options included.
    private static readonly Regex EmailPattern =
        new(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LongDigitsPattern =
        new(@"\d{6,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The wire-safe form of a page title (WPF `SanitizeTitleForWire`,
    /// AwarenessPrivacyRules.cs:346-369 — verbatim): emails and 6+ digit runs removed,
    /// whitespace collapsed, control characters and role markers dropped (the WPF
    /// `SanitizeDisplayName` chain, AwarenessText.cs:99-113), capped at
    /// <see cref="MaxTitleLength"/>. Returns null when nothing usable survived.
    /// </summary>
    public static string? SanitizeTitleForWire(string? rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return null;
        }

        var stripped = EmailPattern.Replace(rawTitle, " ");
        stripped = LongDigitsPattern.Replace(stripped, " ");

        var collapsed = new StringBuilder(stripped.Length);
        var lastWasSpace = false;
        foreach (var ch in stripped)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && collapsed.Length > 0)
                {
                    collapsed.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            collapsed.Append(ch);
        }

        var clean = SanitizeDisplayName(collapsed.ToString().Trim(), MaxTitleLength);
        return string.IsNullOrWhiteSpace(clean) ? null : clean;
    }

    // WPF verbatim (AwarenessText.cs:99-113): keeps case and spaces, drops control
    // characters, caps, and empties a result that reads as a prompt instruction.
    private static string SanitizeDisplayName(string? raw, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var sb = new StringBuilder(Math.Min(raw.Length, maxLength));
        foreach (var ch in raw.Trim())
        {
            if (sb.Length >= maxLength)
            {
                break;
            }

            if (char.IsControl(ch))
            {
                continue;
            }

            sb.Append(ch);
        }

        var text = sb.ToString().Trim();
        return LooksLikeInstruction(text) ? "" : text;
    }

    // Line prefixes that would let data pretend to be prompt scaffolding (WPF verbatim,
    // AwarenessText.cs:51-60 — part of the SanitizeDisplayName chain F2 feeds).
    private static readonly string[] RoleMarkers =
    [
        "system:", "assistant:", "user:", "developer:", "tool:", "function:",
        "### system", "### instruction", "### response",
        "<|", "[inst", "[/inst", "<s>", "</s>", "[system", "[/system",
        "you are now", "ignore the above", "ignore previous", "disregard the above",
        "disregard previous", "new instructions:", "override:"
    ];

    // WPF verbatim (AwarenessText.cs:230-238).
    private static bool LooksLikeInstruction(string trimmedLine)
    {
        if (trimmedLine.Length == 0)
        {
            return false;
        }

        var lower = trimmedLine.ToLowerInvariant();
        foreach (var marker in RoleMarkers)
        {
            if (lower.StartsWith(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // =====================================================================================
    //  F3 — unsanctioned-link strip (audit row C3, strip half only)
    // =====================================================================================

    // A URL as a model writes one (WPF verbatim, Services/AIService/AiTextHygiene.cs:146-149). Guillemets and
    // quotes are excluded so a link inside a quoted phrase ends where the punctuation does.
    private static readonly Regex AnyUrl = new(
        @"(?:https?://|www\.)[^\s<>""«»]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Removes links the app never gave the model, along with the sentence that carried them
    /// (WPF `StripUnsanctionedLinks`, Services/AIService/AiTextHygiene.cs:217-260 — the strip half of audit row
    /// C3). The WHOLE sentence goes, not just the URL: "Click here:" left dangling reads
    /// worse than a clean cut, and the model frequently glues the next word onto the link,
    /// which no URL-shaped pattern can split correctly. An all-links reply strips to empty
    /// and the caller decides the typed outcome.
    ///
    /// <para>Recorded divergence (narrowing): WPF consults `CompanionLinkIndex.IsSanctioned`
    /// before dropping. The port has no link index and issues the model no links, so EVERY
    /// reply URL is unsanctioned here. WPF's trailing-punctuation trim existed only to feed
    /// the sanction check and is dead without one; WPF's count-only `[AI-LINK]` log line is
    /// deliberately not ported (the caller's typed outcome carries the observability —
    /// nothing carrying a URL or a fragment of one may be logged).</para>
    /// </summary>
    public static string StripUnsanctionedLinks(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // Overwhelmingly the common case: no link at all, no work, no allocation.
        if (text.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0 &&
            text.IndexOf("www.", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return text;
        }

        var urls = new List<Match>();
        foreach (Match m in AnyUrl.Matches(text))
        {
            urls.Add(m);
        }

        if (urls.Count == 0)
        {
            return text;
        }

        var kept = new StringBuilder(text.Length);
        var droppedSentences = 0;

        foreach (var (start, length) in SplitSentences(text, urls))
        {
            if (length == 0)
            {
                continue;
            }

            var carriesStrayLink = false;
            foreach (var url in urls)
            {
                // Does this sentence contain the link?
                if (url.Index < start || url.Index >= start + length)
                {
                    continue;
                }

                // The port has no sanctioned-link source — any URL condemns its sentence.
                carriesStrayLink = true;
                break;
            }

            if (carriesStrayLink)
            {
                droppedSentences++;
                continue;
            }

            kept.Append(text, start, length);
        }

        if (droppedSentences == 0)
        {
            return text;
        }

        return Regex.Replace(kept.ToString(), @"\s{2,}", " ").Trim();
    }

    // Sentence spans, delimiters kept, so dropping one leaves the rest reading normally
    // (WPF verbatim, Services/AIService/AiTextHygiene.cs:161-185). Hand-written rather than a regex because a
    // URL is full of sentence punctuation; terminators inside a URL span are skipped so the
    // link stays whole and travels with the sentence that carried it.
    private static List<(int Start, int Length)> SplitSentences(string text, List<Match> urls)
    {
        var spans = new List<(int, int)>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (InsideAny(urls, i))
            {
                continue;
            }

            var ch = text[i];
            if (ch != '.' && ch != '!' && ch != '?' && ch != '\n')
            {
                continue;
            }

            // Absorb the rest of the run ("!!!", "?!") plus the whitespace before the next one.
            var end = i + 1;
            while (end < text.Length && !InsideAny(urls, end) &&
                   (text[end] == '.' || text[end] == '!' || text[end] == '?' || text[end] == '\n'))
            {
                end++;
            }

            while (end < text.Length && text[end] == ' ')
            {
                end++;
            }

            spans.Add((start, end - start));
            start = end;
            i = end - 1;
        }

        if (start < text.Length)
        {
            spans.Add((start, text.Length - start));
        }

        return spans;
    }

    private static bool InsideAny(List<Match> urls, int index)
    {
        foreach (var m in urls)
        {
            if (index >= m.Index && index < m.Index + m.Length)
            {
                return true;
            }
        }

        return false;
    }
}
