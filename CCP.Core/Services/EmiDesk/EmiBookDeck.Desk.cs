using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DECK BATCH: her desk, and the video surface.
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
/// </summary>
internal static class EmiBookDeckDesk
{
    /// <summary>This batch's cards, in reading order within their tab.</summary>
    public static readonly EmiBookCard[] Cards =
    {
        // =================================================================================
        //  THE DESK  -  the only card whose subject is the thing reading it
        // =================================================================================
        //
        // NO BUTTON, DELIBERATELY. Target is null because the thing this card describes is the
        // thing already on screen: a TAKE ME THERE that walks the reader to the avatar they are
        // currently looking at is a joke the second time and a dead control the first. Tour is null
        // for the same reason - there is no desk walkthrough to point at, and borrowing a
        // neighbouring tour to fill the slot would be a lie that only fails at the click.
        //
        // VOCABULARY. "door" is this codebase's internal word for a nav destination and must never
        // surface in copy she speaks. The user-facing word for the left strip is "the rail":
        // en.json tells a new user "The chip at the foot of the rail calls EMI out, and sends her
        // back again", and tooltip_emi_desk_enabled says "Off hides the chip in the rail". Nudge 1
        // borrows that shipped sentence's own nouns, so a reader who saw the tutorial recognises
        // the control instead of hunting for a second one.
        //
        // WHY THE CHIP LEADS AND THE CHORD FOLLOWS. Ctrl+Alt+E is a global hotkey, and a global
        // hotkey is the one control that can be silently unavailable - another process registers
        // the combination first and the app cannot take it back. The chip cannot fail that way.
        // Leading with the control that always works, and offering the chord as the faster second
        // route, is the ordering that survives the failure case.
        //
        // WHY THE PANEL'S LABELS AND NOT SETTINGS'. Two surfaces configure her and they do not
        // agree on wording: the Settings tab calls the spice control "How far she goes" while
        // EmiOptionsWindow - the panel the gear actually opens - calls it "How daring". The nudge
        // names the gear, so the nudge has to speak the gear's dialect or the reader will scan the
        // panel for words that are not on it. Same reason for "her size" and "let her ask", and for
        // "her cards" over "the ring", which is an internal name that reaches no visible string.
        //
        // THE CATCH. This is the most expensive wrong belief a person can hold about her: a pixel
        // avatar that answers, comments and offers things reads as a language model, and it is not
        // one. Nothing under her service or window folders touches OpenRouter, App.Ai or any model
        // client; every line is dealt from Resources/emi/desk-lines.json. Saying so once costs a
        // sentence and saves every "why is her AI so dumb" support ticket.

        new EmiBookCard(
            "the-desk", 0, "emi_book_the_desk",
            "THE DESK",
            "your *desktop companion*, and the one holding *this book*.",
            new[] { "the *chip* in the rail calls her out, and *Ctrl+Alt+E*",
                   "*right-click* her for *her cards*: six shortcuts you can *pin*",
                   "the *gear* holds *her size*, *how daring*, *let her ask*",
                   "*drag* her anywhere. the *x* on her sends her away" },
            "every line she says is dealt from a written file, not an AI.",
            null, null,
            "a whole card about me. i had nothing to do with it.", "^_~"),

        // =================================================================================
        //  MANDATORY VIDEOS  -  the tool that takes the screen and does not hand it back
        // =================================================================================
        //
        // NUMBERS COME FROM THE MODEL, NOT THE SLIDER. Every figure here is a clamp in
        // AppSettings, because that is the value the runtime actually obeys and the sliders do not
        // all agree with it. VideosPerHour clamps 1..20 and the slider matches, so "1 to 20" is
        // safe to print. AttentionDensity clamps 1..10, and the video loop reads it as a TOTAL
        // COUNT of targets for the clip - "up to 10 targets" is literally what gets spawned, which
        // is why the nudge says targets rather than "density", a word that describes nothing the
        // user experiences.
        //
        // ONE NUMBER IS DELIBERATELY MISSING. AttentionLifespan clamps 1..30 in the model but its
        // slider stops at 15, so 16 to 30 seconds is unreachable from the UI. Printing either bound
        // would be false for half the readers, so the card prints neither and the mismatch goes in
        // the report instead.
        //
        // WHY THE FORCED REPLAY IS A NUDGE AND NOT A FOOTNOTE. Failing an attention check does not
        // replay the clip you failed: the loop fails you when hits are short of spawns, sets the
        // repeat flag, and then asks for the NEXT video, a different file. A user who expected the
        // same clip again reads a fresh one as a bug in the picker, so it has to be in the body.
        //
        // WHY THIS CATCH AND NOT STRICT LOCK. The obvious catch - that a strict lock leaves only
        // the panic key - is already the third nudge on THE PANIC KEY. Repeating it would burn the
        // one honest slot on something the reader was told two cards ago. The one-in-ten replay
        // roll on a PASSED check is the fact nothing else in the book carries, and the one that
        // otherwise reads as a broken pass check: you did everything right and it starts over.

        new EmiBookCard(
            "videos", 1, "emi_book_videos",
            "MANDATORY VIDEOS",
            "*full screen* video, on its own *schedule*, over everything else.",
            new[] { "*1 to 20* an hour, pulled from your *videos* folder",
                   "*strict lock* removes *skip* and *close* until the clip ends",
                   "*attention checks* drop up to *10* targets you must *click*",
                   "*miss one* and it makes you watch a *different video*" },
            "even a clean pass has a one in ten chance of a replay.",
            "videos", null,
            "click the little words. i am counting.", "0_0"),

        // =================================================================================
        //  WHISPERS  -  the subliminal you hear
        // =================================================================================
        //
        // WHY THIS IS ITS OWN CARD. SUBLIMINALS spends its fourth nudge on "a whisper can speak
        // each phrase as it flashes" and then has no room to say how, which leaves the reader with
        // an audio feature they cannot find, cannot load and cannot silence. The three controls sit
        // in three different places - a checkbox, a slider, a mute - and they need the space.
        //
        // NOTHING IS SYNTHESIZED, AND THAT IS THE WHOLE CATCH. There is no text-to-speech anywhere
        // in the app. A whisper is a file lookup: the service builds the path out of the phrase
        // itself, phrase + extension, and returns null when nothing matches - silently. So a user
        // who writes their own phrases in the subliminal editor gets flashes with no sound and no
        // error, and will read that as broken audio. The catch names the mechanism so that the fix
        // is obvious: name a file after the phrase. HelpContentService still claims a "synthesized
        // voice"; the code wins.
        //
        // "21 CLIPS" IS A DISK COUNT AND WILL DRIFT. It is the file count of Resources/sub_audio
        // (21 mp3, verified 2026-08-30). It is worth the risk because "a stock pool ships" - the
        // phrasing SUBLIMINALS uses - tells a reader nothing about whether they need to go and
        // record anything. If that folder is ever repacked, this number has to move with it.
        //
        // 20% IS THE SHIPPED DEFAULT, NOT A CEILING. DuckingLevel defaults to 80, and both the
        // model's own comment and the settings tooltip gloss 80% as "reduce other audio to 20%". A
        // reader deciding whether to leave music playing needs a magnitude, and the default is the
        // magnitude they will actually get.
        //
        // NUDGE 4 IS A SAFETY LINE. SubAudioMuted is a separate field from SubAudioEnabled on
        // purpose: a session can hold the feature switch, so the mute is the control that still
        // answers when something else is driving. That is the one thing a person needs at the exact
        // moment they need it, so it is stated as a capability rather than left as trivia.

        new EmiBookCard(
            "whispers", 1, "emi_book_whispers",
            "WHISPERS",
            "*whispered audio*, matched to the *trigger phrase* it flashes.",
            new[] { "*21 clips* ship. add an *mp3* named after your phrase",
                   "*audio whispers* switches them on, one slider sets the *volume*",
                   "*ducking* drops every other app to *20%* while it plays",
                   "*mute whispers* silences them *instantly*, even inside a locked session" },
            "no voice is generated. an unmatched phrase whispers nothing at all.",
            null, null,
            "that whispering is not me. i use a bubble.", "(◕‿◕)"),
    };
}
