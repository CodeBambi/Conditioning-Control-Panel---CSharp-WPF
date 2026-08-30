using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// One card in her book: a demo, a title, a gist, up to four nudges, one catch and at most one
/// button. The catch is NOT a nudge and never renders as one - it has its own strip at the foot of
/// the card, which is what keeps the bullet list at the owner's ceiling of four items while still
/// leaving room for the honest bit.
/// </summary>
/// <param name="Id">Stable card id. Also the <see cref="EmiDemoPainter.Id"/> and the bookmark
/// value, so it must never be renamed once shipped.</param>
/// <param name="Tab">0 START, 1 TOOLS, 2 DEEPER. Indexes <see cref="EmiBookCards.TabKeys"/>.</param>
/// <param name="KeyStem">Localization stem, <c>emi_book_&lt;stem&gt;_*</c>.</param>
/// <param name="TitleEn">English title, and the fallback when the key is missing.</param>
/// <param name="GistEn">English gist. Ten words maximum; this is the one line that must land. May
/// carry <c>*asterisk*</c> emphasis, parsed by <see cref="EmiBookText"/>.</param>
/// <param name="NudgesEn">One to four nudges, ten words each, each one a thing you can DO rather
/// than a thing that is true. May carry <c>*asterisk*</c> emphasis.</param>
/// <param name="CatchEn">The one honest limitation. Never optional, never softened.</param>
/// <param name="Target">An <see cref="EmiTargets"/> id for TAKE ME THERE, or null.</param>
/// <param name="Tour">A <c>TutorialType</c> name for WALK ME THROUGH IT, or null. Never an ordinal.</param>
/// <param name="MarginEn">Her line in the margin. Lowercase, one thought, 60 characters maximum.</param>
/// <param name="MarginFace">A kaomoji from the shipped set in <c>desk-lines.json</c>.</param>
public sealed record EmiBookCard(
    string Id,
    int Tab,
    string KeyStem,
    string TitleEn,
    string GistEn,
    IReadOnlyList<string> NudgesEn,
    string CatchEn,
    string? Target,
    string? Tour,
    string MarginEn,
    string MarginFace)
{
    /// <summary>The card's visible title.</summary>
    public string Title => EmiBookCards.L(KeyStem + "_title", TitleEn);

    /// <summary>The pink line.</summary>
    public string Gist => EmiBookCards.L(KeyStem + "_gist", GistEn);

    /// <summary>The catch, already carrying its label.</summary>
    public string Catch =>
        EmiBookCards.L("emi_book_catch_label", "the catch:") + " " + EmiBookCards.L(KeyStem + "_catch", CatchEn);

    /// <summary>The plain nudges, localized, in order.</summary>
    public IReadOnlyList<string> Nudges =>
        NudgesEn.Select((n, i) => EmiBookCards.L($"{KeyStem}_nudge{i + 1}", n)).ToList();
}

/// <summary>
/// THE BOOK'S CONTENT.
///
/// <para><b>Fourteen cards replace fourteen chapters.</b> Same territory, about a tenth of the
/// words. The ceiling is 40 words of body text per card, defended by
/// <c>EmiBookCards_Tests.Every_card_stays_under_the_word_ceiling</c>: a card has to be consumable
/// in one glance and one loop of its demo, and forty words is what a person reads while a six
/// second animation plays twice.</para>
///
/// <para><b>Wave A ships four</b> - the two the owner named, plus the two that frame them. The
/// DEEPER tab therefore has nothing behind it yet and paints greyed, exactly as the codex's locked
/// volumes IV to VI did, so the shape of the book is honest from day one.</para>
///
/// <para><b>Four items, and the catch is not one of them.</b> The owner's ceiling
/// (2026-08-30): "you can use more space but never more than 4 items per card". So a card carries
/// at most four bullets, and the catch was lifted OUT of that list into its own strip - it was
/// costing a bullet to say a thing that is not an action. Key words carry <c>*asterisk*</c>
/// emphasis and render bold and pink, because a card is read in about two seconds by somebody who
/// is mid-task and the words that carry the meaning have to be findable without reading the
/// sentence.</para>
///
/// <para><b>Every catch was verified against code, not against the website.</b> The lockdown card
/// will say five clicks because <c>MainWindow.Lab.cs:910</c> requires five within 1000 ms each;
/// <c>guide-lockdown.html:157</c> still tells people one tap. Only Brain Drain is excluded from
/// screen capture (<c>BrainDrainLayer.ExcludeFromCapture</c> overrides a <c>BaseLayer</c> default of
/// false), so the flashes and subliminals cards both admit that they show up in a recording.</para>
///
/// <para><b>Localization.</b> Card text is UI copy and goes through <c>emi_book_*</c> keys, English
/// in <c>en.json</c>, translated into all nine language files (2026-08-31, owner call). Her margin lines are never localized, like every other line she says.</para>
/// </summary>
public static class EmiBookCards
{
    /// <summary>Tab labels, left to right. Index matches <see cref="EmiBookCard.Tab"/>.</summary>
    public static readonly IReadOnlyList<string> TabKeys = new[] { "start", "tools", "deeper" };

    /// <summary>English tab labels, and the fallback when a key is missing.</summary>
    public static readonly IReadOnlyList<string> TabNamesEn = new[] { "START", "TOOLS", "DEEPER" };

    /// <summary>Localized tab label for a tab index.</summary>
    public static string TabName(int tab)
    {
        if (tab < 0 || tab >= TabKeys.Count) return string.Empty;
        return L("emi_book_tab_" + TabKeys[tab], TabNamesEn[tab]);
    }

    /// <summary>
    /// The cards, in reading order. Order is load-bearing: the demos alone, in sequence, are the
    /// story for somebody who never reads a word of it.
    /// </summary>
    private static readonly EmiBookCard[] Core =
    {
        new EmiBookCard(
            "the-ccp", 0, "emi_book_the_ccp",
            "THE CCP",
            "*flashes*, *videos*, whispers and overlays, over your normal desktop.",
            new[] { "every tool gets its own *tab* and its own switches",
                   "press *Start* and it runs over whatever you are doing",
                   "a *session* drives the whole set and ramps it over time",
                   "using it pays *XP*, and every *level* pays a *skill point*" },
            "with an empty assets folder nothing shows. add media, or go online.",
            null, "GettingStarted",
            "my desk, your switches. flip something.", "(¬‿¬)"),

        new EmiBookCard(
            "the-panic-key", 0, "emi_book_the_panic_key",
            "THE PANIC KEY",
            "one press and *everything on screen stops* at once.",
            new[] { "it is *Esc* until you click the box and *rebind* it",
                   "one press kills *flashes, videos, overlays, games*",
                   "during a *strict lock* video it is the only way out",
                   "press it again with *nothing running* and the app quits" },
            "No Panic and Lockdown can switch it off. you choose those yourself.",
            "settings", null,
            "the one key i never joke about.", "._."),

        new EmiBookCard(
            "flashes", 1, "emi_book_flashes",
            "FLASHES",
            "your *gifs and images*, thrown at the screen on a timer.",
            new[] { "pick *how often* they land, up to 180 an hour",
                   "sliders for *size*, *opacity* and how long each one stays",
                   "*click* one to pop it. *hydra* mode spawns two more",
                   "*online* mode pulls fresh stills from your *subreddits*" },
            "they show up in a screen recording or a stream.",
            "flashes", null,
            "i get the best seat in the house for these.", "(｡♥‿♥｡)"),

        new EmiBookCard(
            "subliminals", 1, "emi_book_subliminals",
            "SUBLIMINALS",
            "your *trigger phrases*, flashed a couple of frames at a time.",
            new[] { "a stock pool ships. open the *editor* to write your own",
                   "tune the *rate*, the *frame count* and the opacity",
                   "pick your *colors* in the visual settings",
                   "a *whisper* can speak each phrase as it flashes" },
            "they show up in a screen recording, same as flashes.",
            "subliminals", null,
            "two frames is plenty when you read as fast as me.", "(⌐■_■)"),
    };

    /// <summary>
    /// THE WHOLE DECK, in reading order.
    ///
    /// <para>The cards live in per-batch files (<c>EmiBookDeck.*.cs</c>) so that a wave of them can
    /// be written in parallel without six people editing one array. This concatenates the batches
    /// and then sorts by tab. <c>OrderBy</c> is a STABLE sort, which is the whole trick: the order
    /// within a tab is the order the batches appear below, and then each batch's own order, so the
    /// reading order stays something a person chose rather than something a file layout produced.
    /// </para>
    ///
    /// <para>Grouping by tab is not cosmetic. The pager walks the book in a straight line while the
    /// dots only count the current tab, so an interleaved deck would show a pager step that changes
    /// the tab underneath the reader and resets the dot count mid-chapter -
    /// <c>EmiBookCardsTests.Cards_are_grouped_by_tab_in_reading_order</c> holds the line.</para>
    /// </summary>
    public static readonly IReadOnlyList<EmiBookCard> All = Compose();

    private static IReadOnlyList<EmiBookCard> Compose()
    {
        var all = new List<EmiBookCard>(Core);
        all.AddRange(EmiBookDeckSetup.Cards);
        all.AddRange(EmiBookDeckDesk.Cards);
        all.AddRange(EmiBookDeckOverlays.Cards);
        all.AddRange(EmiBookDeckMachines.Cards);
        all.AddRange(EmiBookDeckControl.Cards);
        all.AddRange(EmiBookDeckPlaces.Cards);
        return all.OrderBy(c => c.Tab).ToList();
    }

    /// <summary>Index of a card id, or -1.</summary>
    public static int IndexOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return -1;
        for (int i = 0; i < All.Count; i++)
            if (string.Equals(All[i].Id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>The first card on a tab, or -1 when that tab is empty (DEEPER, in wave A).</summary>
    public static int FirstOnTab(int tab)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Tab == tab) return i;
        return -1;
    }

    /// <summary>True when a tab has at least one card behind it.</summary>
    public static bool TabHasCards(int tab) => FirstOnTab(tab) >= 0;

    /// <summary>
    /// Localization with an English fallback baked in. The book must render on a build whose
    /// language file predates it: a missing key shows the English string, never the raw key.
    /// </summary>
    public static string L(string key, string fallback)
    {
        try
        {
            var s = Localization.Loc.Get(key);
            if (string.IsNullOrWhiteSpace(s) || string.Equals(s, key, StringComparison.Ordinal))
                return fallback;
            return s;
        }
        catch
        {
            return fallback;
        }
    }
}
