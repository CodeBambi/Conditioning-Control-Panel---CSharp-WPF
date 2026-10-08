using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DECK BATCH: the big machines.
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
/// <para><b>What "checked against the code" cost this batch.</b> Three of the obvious wordings were
/// wrong and are called out at the card that carries them: the corner overlays are not
/// unconditionally "always on" (a session's own corner GIF evicts them), Deeper's effect palette is
/// six entries rather than the three the tab's own pitch line names, and the companion is NOT behind
/// a subscription wall at all - <c>AiService.EffectiveDailyLimit</c> hands a signed-in free account a
/// real daily allowance and sells the tiers a bigger one. Writing "premium only" there would have
/// been the euphemism running the other way.</para>
/// </summary>
internal static class EmiBookDeckMachines
{
    /// <summary>This batch's cards, in reading order within their tab.</summary>
    public static readonly EmiBookCard[] Cards =
    {
        // =================================================================================
        //  CORNER GIFS  -  TOOLS
        // =================================================================================
        //
        // No Target and no Tour, and both halves of that are forced rather than chosen. There
        // is no EmiTargets entry for the standalone corner overlays and no TutorialType for
        // them either, and a card must never offer a button that resolves to nothing - so the
        // route is spelled out in nudge 1 instead, naming the Studio's Spiral card, which is
        // the one and only door: SpiralFeatureControl.BtnCornerGifs_Click is the sole
        // construction of CornerGifWindow anywhere in the tree.
        //
        // THE CATCH IS NOT THE ONE YOU EXPECT. "Always on" is what the button itself promises
        // (SpiralFeatureControl.xaml:155, "pin a GIF to a screen corner") and it holds right
        // up until a session or a 28-day program day raises ITS corner GIF, at which point
        // CornerGifMedia.AllowStandaloneCornerGif returns false for every slot and the user's
        // own overlays come down until the session's one goes away. That is a person watching
        // their overlay vanish with their own switch still on, so that is the catch.
        //
        // The crash-recovery force-disable (CornerGifService.ResolveRestoreAction: a run that
        // ended with an overlay live turns every slot OFF at the next launch) is the other
        // honest limitation and lost the coin toss - it fires once, after a crash, and the app
        // already says so out loud through App.Notifications, whereas the session eviction is
        // silent and routine.
        new EmiBookCard(
            "corner-gifs", 1, "emi_book_corner_gifs",
            "CORNER GIFS",
            "one *looping gif*, pinned to a *screen corner*, always on.",
            new[] { "the Studio's *Spiral* card opens *two* pinnable slots",
                   "*size* runs 100 to 800 px, *opacity* 5 to 100",
                   "pick *any corner* and *any gif*, or leave it empty for the *spiral*",
                   "*click-through* and *always on top*: it never steals a click" },
            "a session that raises its own corner GIF wins. your slots stand down.",
            null, null,
            "something in the corner of your eye. on purpose.", "(◔_◔)"),

        // =================================================================================
        //  DEEPER  -  DEEPER
        // =================================================================================
        //
        // The hardest card in the book, because Deeper is the only feature here that is a TOOL
        // FOR MAKING THINGS rather than a thing. So the gist answers "what goes in and what
        // comes out" instead of "what does it do": you bring a video or an audio file, and you
        // get a timeline with your own effects nailed to it. Nudges 1 and 4 are deliberately
        // the two ENDS of that - where the material comes from, and what you can hand somebody
        // afterwards - because a person who has never opened the tab does not need the middle
        // explained, they need to know it is a pipeline with their own file at one end.
        //
        // Tour = "Deeper", the TutorialType member name (TutorialService.cs:126), never the
        // ordinal. Seven other Deeper* members sit in that enum, four of them the interactive
        // on-rails walkthroughs, and the plain overview one is the only correct choice for a
        // reader who has not opened the tab: every other one targets the EDITOR window and
        // would run its coachmarks against a window that is not up.
        //
        // "toy buzz" rather than "haptics" for the same reason the owner's own example says
        // "gifs and images" rather than "media assets". The palette is six entries - Haptic,
        // Flash, Bubble, Subliminal, Overlay, Speak (CCP.Core/Models/Deeper/TimelineItem.cs EffectTypes,
        // and the editor's + Effect dropdown lists all six) - and four of them fit the line.
        // The two dropped are the two a newcomer has no picture for.
        //
        // The catch is the app's own word, not a hedge of mine. DeeperTabView paints a BETA
        // badge whose tooltip is en.json "deeper_beta_notice": rough edges, breaking changes
        // between versions, the occasional crash. A card that softened that while the tab it
        // points at says it in full would be the book lying about the app.
        new EmiBookCard(
            "deeper", 2, "emi_book_deeper",
            "DEEPER",
            "*any* video or audio, with your *effects* on a *timeline*.",
            new[] { "point it at a *local file* or a *HypnoTube* link",
                   "*+ Effect* pins *flashes*, *subliminals*, *overlays*, *toy buzz* to a timestamp",
                   "*webcam* rules fire on a *blink*, a *gaze*, a *look-away*",
                   "*export* bakes it into a copy of the *mp4* you can hand over" },
            "Deeper is in beta. expect breaking changes between versions, and crashes.",
            null, "Deeper",
            "i have watched a lot of videos. none watched back.", "(◉_◉)"),

        // =================================================================================
        //  THE COMPANION  -  DEEPER
        // =================================================================================
        //
        // Target "companion" is a real EmiTargets id (Nav("companion")), Always available and
        // Never locked, so TAKE ME THERE cannot bounce off a gate.
        //
        // THE SUBSCRIPTION GATE, CHECKED RATHER THAN ASSUMED. PatreonService.HasAiAccess and
        // HasPremiumAccess are the SAME expression (lines 127 and 134 - tier 1, or whitelist,
        // or the cached grace window, or SubscribeStar), so "AI access" is not a separate
        // entitlement from premium and never has been. And neither of them is what gates chat:
        // AiService.IsAvailable is `App.HasCloudIdentity || HasAiAccess`, so a signed-in free
        // account passes, and the tier only moves EffectiveDailyLimit - 100 requests a day
        // free, 1000 on tier 1, 2000 on Lab. The comment above that property states the policy
        // outright ("AI is not the perk - usage is", owner, 2026-08-13). So the catch names a
        // LOGIN and a NUMBER, which is what actually stops somebody, rather than "premium
        // only", which would simply be false.
        //
        // Nudge 4 is the one thing a reader would never guess and would otherwise file as a
        // bug: the cloud model CANNOT drive effects, only a local Ollama one can (en.json
        // "tooltip_lab_ai_effects" and "lab_ai_effects_needs_local_body"). It earns a bullet by
        // being a real capability that is switched off in the configuration most people are in.
        new EmiBookCard(
            "companion", 2, "emi_book_companion",
            "THE COMPANION",
            "a *character* who talks back, *reacts*, and remembers you.",
            new[] { "*Ctrl+T* from any app, and she answers in her *tube*",
                   "rewrite her *personality*, *rules* and *knowledge base* yourself",
                   "her *memory* list shows every fact. *pin*, *edit* or *wipe* one",
                   "point her at a *local Ollama* model and she can *fire effects*" },
            "cloud chat needs a login: 100 replies a day free, 1000 on tier 1.",
            "companion", null,
            "she gets a whole tube. i get a desk. i am fine.", "¬_¬"),
    };
}
