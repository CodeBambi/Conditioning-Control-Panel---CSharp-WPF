using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DECK BATCH: the places to go.
///
/// <para>One batch of cards. The deck is split into files so that several people can write cards at
/// once without ever meeting in a diff - <see cref="EmiBookCards.All"/> concatenates the batches and
/// then sorts by tab, which is a STABLE sort, so the order inside a tab is the order the batches are
/// listed in and then the order of this array. Nothing here decides which tab a card lands on except
/// the card's own Tab field.</para>
///
/// <para>The rules a card lives by are in <see cref="EmiBookCards"/>: four bullets at most, the
/// catch is not one of them, key words wear <c>*asterisks*</c>, and every claim is checked against
/// the code rather than against the website.</para>
///
/// <para><b>WHAT MAKES THIS BATCH DIFFERENT.</b> Every other card in the book describes an EFFECT
/// that runs over the desktop the reader is already looking at. These three describe DESTINATIONS -
/// whole surfaces that take the screen away and give it back later. That changes the job of the
/// copy: a flash card can afford to list its sliders because the reader can see the thing it is
/// talking about, while a destination card is trying to make somebody spend an evening somewhere
/// they have never been. So each of these picks the two or three facts that would make a stranger
/// curious and leaves the rest of the surface undescribed on purpose. The Arcademy alone has ten
/// classes, a campus map, two currencies, a prize counter, a locker, a records office, a student ID
/// and an annex; four bullets that tried to cover that would be an index, and nobody has ever been
/// talked into anything by an index.</para>
///
/// <para><b>THE GATES ARE THE HARD PART, AND THEY ARE ALL DIFFERENT.</b> Three cards, four
/// different bars between them, and the words for them are not interchangeable:
/// <list type="bullet">
/// <item>"tier 2" is <c>TierGate.RequiresLab</c> -&gt; <c>PatreonService.HasLabAccess</c>
/// (PatreonService.cs:172). The Arcademy and Down the Rabbit Hole sit behind it
/// (EmiTargets.cs:279-283 and :316-318).</item>
/// <item>"tier 1" is <c>TierGate.RequiresPremium</c> -&gt; <c>HasPremiumAccess</c>
/// (PatreonService.cs:134). In this codebase "premium" MEANS tier 1, and <c>HasAiAccess</c>
/// resolves to exactly the same thing despite its name (TierGate.cs:40-44), so no card in this
/// batch says "premium" without a number beside it - the word is ambiguous in the source and would
/// be worse in a manual.</item>
/// <item>the weekly Intake pass opens the quiz for a free account that has not spent it
/// (<c>ExclusiveFeature</c>'s Gate for "gradedintake", IntakePassService.cs:137).</item>
/// <item>joining a Goon room is free for everybody; only MINTING one is tier 2
/// (GoonHostService.cs:903-916, "JOINING is free for everyone and is never gated here").</item>
/// </list>
/// Getting any of those one rung wrong turns the book into a thing that lies about money, which is
/// the one kind of lie a help page never recovers from.</para>
/// </summary>
internal static class EmiBookDeckPlaces
{
    /// <summary>This batch's cards, in reading order within their tab.</summary>
    public static readonly EmiBookCard[] Cards =
    {
        // =================================================================================
        //  THE ARCADEMY
        // =================================================================================
        //
        // THE SHAPE OF THE CARD: a place, one night of it, and the money. Not a list of ten games.
        // The timetable deals exactly four classes a night (core/timetable.js:68,
        // CLASSES_PER_DAY = 4) out of the ten that are open, so "ten" and "four" standing together
        // say "there is more here than you will see tonight" in six words, which is the single
        // most persuasive fact about the place and the only one worth a whole bullet.
        //
        // WHY "TEN CLASSES" AND NOT ELEVEN. games/registry.js:52-68 lists eleven modules and
        // OPEN_SEMESTERS (:140) has all three semesters open, but RETIRED_GAMES (:152) pulls
        // misdirection out of the deal entirely - "no stub, no board row, no campus room". Ten is
        // what a player can actually be dealt, so ten is the number the book prints.
        //
        // WHY HOMEROOM GETS A BULLET TO ITSELF. It is the one class every player sits and the only
        // one with a social hook: shell/campus.js:84 describes it as "One word, six chances. The
        // whole school sits the same word today." Same word for everybody is the fact that makes a
        // person open the app again tomorrow, and it costs nine words to say.
        //
        // THE MONEY, IN THE ROOM'S OWN WORDS. ArcademyHostService.cs:1606 "Every graded class pays
        // tickets" and :1608 "Tokens only. Your first S of the day drops one in the tray." Two
        // currencies on two different rhythms - a good week against a good night - and the card
        // stands them side by side without explaining either, which is exactly what the room
        // itself does (shell/prizecounter.js header). shell/locker.js:4 finishes the sentence:
        // "The Prize Counter sells. This is where the things go afterwards."
        //
        // THE CATCH IS DOUBLE AND BOTH HALVES ARE LOAD-BEARING. The tier 2 bar is the one that
        // stops a reader at the door (EmiTargets.cs:281-282, LabOk -> TierGate.RequiresLab). The
        // XP rule is the one that would read as a BUG if it were not printed somewhere first:
        // ArcademyHostService.cs:3560 pays XP only when firstToday, and the shell says so out loud
        // at :1850-1851 ("First pass of the day pays XP." / "Retakes pay no XP - pride only."). A
        // player who grinds one class for an hour and watches the XP stay at zero needs to have
        // met that rule before it happens to them.
        new EmiBookCard(
            "arcademy", 2, "emi_book_arcademy",
            "THE ARCADEMY",
            "a *campus* of ten classes, with its own money.",
            new[] { "a fresh *timetable* deals you *four* of them a night",
                   "*homeroom* is one word, six chances, same word for everybody",
                   "graded classes pay *tickets*; the day's first *S* pays a token",
                   "spend both at the *prize counter*. what you buy lands in your *locker*" },
            "tier 2 only, and only your first pass of a class pays XP.",
            "arcademy", null,
            "my whole wardrobe lives in a locker there.", "(¬‿¬)"),

        // =================================================================================
        //  THE OTHER GAMES
        // =================================================================================
        //
        // ONE CARD, FOUR GAMES, AND THE JOB IS DISCOVERY RATHER THAN INSTRUCTION. Nobody learns
        // four whole games off one card and this one does not try. Each bullet is a name plus the
        // smallest true sentence that would send somebody looking, because the thing these four
        // have in common is that a reader does not currently know they are there at all.
        //
        // JUST DROP IS DELIBERATELY NOT NAMED, and this is the claim that took the most checking.
        // JustDropService.DoorAvailable is the server's verdict AND'd with a local kill switch;
        // ServerEnabled defaults to false and is "deliberately not persisted anywhere"
        // (JustDropService.cs:74-88), so it fails CLOSED on a fresh install and most accounts have
        // no door at all. The vault shelf reaches the same conclusion and HIDES that card rather
        // than veiling it ("a hide, not a veil, because a veil means buy this and this one is not
        // for sale yet"). A manual that named a door most of its readers do not have would be
        // describing somebody else's copy of the app.
        //
        // WHAT EACH ONE ACTUALLY IS, taken from the code rather than from the store page:
        //   DtRH   a three.js roguelite - you fall down a 3D tube, pop bubbles for currency, draft
        //          boons and spend the proceeds in a hub (Resources/web/dtrh/CLAUDE.md, "What DTRH
        //          is, in one paragraph"). Launched by DtrhHostService.Launch, EmiTargets.cs:318.
        //   FYP    "an endless feed cut from your own library" is the app's own tagline
        //          (en.json:820) and FypAssetManifest.cs is what makes it true - it enumerates the
        //          user's active preset into the manifest the feed consumes. Its display name is
        //          "For You" (en.json:819), which is what the card uses; "FYP" appears nowhere a
        //          reader can see it.
        //   INTAKE the quiz does not stop at a grade. IntakeHostService.cs's protocol block routes
        //          quiz-result to the C# QuizSessionGenerator, which "drafts a session" and
        //          answers session-drafted { ok, name, path }. Finishing it holding a session you
        //          did not write is the payoff worth the bullet; "weekly evaluation, grades are
        //          forever" (en.json:826) is the mood and would have told the reader nothing.
        //   GOON   a windowed duel surface against another person over /v2/goon/*
        //          (GoonHostService.cs header).
        //
        // THE CATCH IS WHERE THE HONESTY LIVES. Three of the four want a pledge and saying so is
        // not optional. The Intake exception is real and specific rather than a softener: a free
        // account with an unspent weekly pass opens that door legitimately (IntakePassService.cs:24
        // and :137), which is why the vault shelf gives Graded Intake a livery tier of 0 instead of
        // a padlock. The Goon exception rides in nudge four instead, because "joining is free" is a
        // thing a reader can DO and the catch strip is for the things they cannot.
        new EmiBookCard(
            "the-games", 2, "emi_book_the_games",
            "THE OTHER GAMES",
            "four more *whole games*, each behind its own door.",
            new[] { "*Down the Rabbit Hole*: fall down a 3D shaft, draft *boons*",
                   "*For You* cuts an endless feed out of your own library",
                   "*Graded Intake* grades you, then writes a *session* from it",
                   "the *Goon Game* duels a real person. joining is free" },
            "the other three want a pledge, though one Intake pass a week is free.",
            "dtrh", null,
            "four doors, and i have opinions about all of them.", "(⌐■_■)"),

        // =================================================================================
        //  THE VELVET VAULT
        // =================================================================================
        //
        // THE ONE CARD IN THE BOOK THAT COULD TURN INTO AN ADVERT, so the structure is built to
        // stop that happening: two bullets on what money buys, two on what it does not. A reader
        // who is never going to pay has to close this card feeling better about the app rather
        // than worse, and a reader who is thinking about paying has to leave with a NUMBER rather
        // than a mood.
        //
        // TITLE. The tab header is "Exclusives" with the subtitle "The Velvet Vault -
        // supporter-only features & content" (en.json:808-809), and the nav button itself reads
        // "Premium" (tab_exclusives, en.json:797). The Velvet Vault is the name the shelf gives
        // itself and the only one of the three that is not also a generic word.
        //
        // NUDGE 1, AND WHY THESE FOUR NAMES. Every shelf entry in ExclusiveFeature.All carries a
        // livery Tier, and that field is explicitly "a price tag, not an entitlement check". For
        // You, Remote Control, Takeover and Awareness are all Tier = 1, and they are also exactly
        // the DailyFreeService rotation pool (DailyFreeService.cs:40), so naming them once buys
        // the next bullet as well. Blink Trainer, She's Listening, Haptics and Lockdown are tier 1
        // too and are simply not named: the bullet says "takes the shelf" rather than listing
        // eight, because a list of eight is a receipt and nobody reads a receipt for pleasure.
        //
        // NUDGE 2 IS THE ONE A PERSON ABOUT TO SPEND MONEY IS MOST LIKELY TO MISS. Tier 1 does NOT
        // open the Arcademy. It is a second bar (PatreonService.cs:172; PatreonModels.cs:14-17 has
        // Level1 at $5 and Level2 at $10) and the biggest room in the app is behind it. Leaving
        // that out would be the exact overpromise this card exists to avoid, and the shelf itself
        // cannot say it - the Arcademy has no card on the shelf at all.
        //
        // NUDGE 3 IS THE FREE SIDE, AND IT IS A REAL THING RATHER THAN A CONSOLATION.
        // DailyFreeService rotates one paid feature open for every account every day on a
        // date-seeded wheel with a three-day no-repeat window (:151-167), and the dashboard's
        // Daily Gift tile is the surface it lands on (MainWindow.Presets.cs:1176 retitles that
        // tile to the FREE TODAY string; en.json:798-799 holds both).
        //
        // NUDGE 4 IS THE UNDERSELL GUARD. Flashes, videos, subliminals, the overlays and the
        // session engine are all registered Always-available and NEVER locked in the target
        // catalogue - sessions, flashes and videos at EmiTargets.cs:293-295, subliminals and
        // bubbles at :324-325, the spiral, pink filter, Brain Drain and Mind Wipe at :328-332, all
        // of them carrying Always as their third argument and Never as their fourth. The paid
        // shelf is genuinely an extra floor rather than the building, and a card
        // that let a reader believe otherwise would be selling by omission - which is still
        // selling.
        //
        // THE CATCH IS THE ONLINE CHECK, NOT THE PRICE. The price is on the bullets already, and a
        // price is not a catch. Entitlement is validated against the server and cached:
        // GraceDays = 14 (PatreonService.cs:33), the stamp is re-extended by every validation that
        // comes back tier 1 or better (:716), and access holds while HasCachedPremiumAccess is
        // inside that window (AppSettings.cs:2654). So an offline fortnight is fine and the
        // fifteenth day is not, and that is the fact a supporter actually needs from this card.
        //
        // HER MARGIN LINE SELLS NOTHING. She lives in the app, she does not work for its shop, and
        // the driest true thing she can say here is that the glass has her on the reader's side of
        // it.
        new EmiBookCard(
            "vault", 2, "emi_book_vault",
            "THE VELVET VAULT",
            "the *supporter shelf*, and exactly what a pledge opens.",
            new[] { "*tier 1* takes the shelf: For You, Remote, Takeover, Awareness",
                   "*tier 2* is a second bar, and the *Arcademy* is behind it",
                   "the *Daily Gift* opens one paid feature for everybody each day",
                   "*flashes*, *videos*, *subliminals*, overlays and sessions are all free" },
            "it checks your pledge online. offline, the doors stay open 14 days, then shut.",
            "vault", null,
            "i'm on the free side of that glass too, you know.", "-_-"),
    };
}
