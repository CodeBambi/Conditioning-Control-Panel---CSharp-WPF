using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DECK BATCH: the effects that paint over the screen.
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
/// <para><b>Three things the code said that the UI does not.</b> They shaped the copy below and are
/// worth stating once, here, rather than three times over in the cards:</para>
/// <list type="number">
/// <item><b>Nothing in this batch is level gated.</b> The settings regions are headed "Unlocks
/// Lv.10 / Lv.20 / Lv.50", the feature tooltips repeat it, and <c>ProgressionService.IsUnlocked</c>
/// encodes it - but that method has no callers, and the one gate that IS consulted,
/// <c>AppSettings.IsLevelUnlocked</c> (AppSettings.cs:6098), is <c>return true;</c>. So no card here
/// promises or withholds anything on a level, because the app does not.</item>
/// <item><b>Mind Wipe never touches the screen.</b> <c>MindWipeService</c> is an audio scheduler and
/// nothing else, and even its "visual effect" entry point (KeywordTriggerService.cs:1925) calls
/// <c>TriggerOnce</c>, which plays a clip. The SCREEN EFFECTS card spends a bullet saying "sound
/// only" rather than quietly listing it beside the wash and the blur, because a reader who switches
/// it on expecting a third overlay gets nothing and concludes the app is broken.</item>
/// <item><b>Only the blur is invisible to a screen capture.</b> <c>BrainDrainLayer</c> is the single
/// <c>ExcludeFromCapture =&gt; true</c> in the compositor (BrainDrainLayer.cs:100, against a
/// <c>BaseLayer</c> default of false), so the spiral and the pink wash both record. That asymmetry
/// is the catch on both cards that carry an overlay, phrased from the reader's side: what their
/// recording will and will not contain.</item>
/// </list>
/// </summary>
internal static class EmiBookDeckOverlays
{
    /// <summary>This batch's cards, in reading order within their tab.</summary>
    public static readonly EmiBookCard[] Cards =
    {
        // =================================================================================
        //  THE SPIRAL
        //
        //  Target "spiral" is the one entry in EmiTargets that does not navigate: FireSpiral
        //  (EmiTargets.cs:375) drops the overlay for six seconds on top of wherever the reader
        //  already is. The button is a demonstration rather than a door, so the copy never says
        //  "go and look at the settings" - it says what the thing is and what the dials do.
        //
        //  The Loom earns a bullet because the reader has no other way to learn it exists. It is
        //  a button on the Spiral feature card ("THE LOOM - weave your own spiral") whose saves
        //  land straight in the Spirals library (see LoomHostService), which is the same folder
        //  Randomize draws from. One sentence closes that loop; two would not fit.
        // =================================================================================
        new EmiBookCard(
            "spiral", 1, "emi_book_spiral",
            "THE SPIRAL",
            "a *spiral gif* turning over *every screen* you own.",
            new[] { "*opacity* runs *5 to 100%*, or pin it to *one monitor*",
                   "drop your own into the *Spirals folder*, then *pick* it",
                   "*Randomize* pulls a *different spiral* each time one comes up",
                   "*THE LOOM* weaves you a *new spiral* and saves it there" },
            // Checked rather than assumed: the spiral rides the ordinary overlay hosts, which
            // inherit BaseLayer.ExcludeFromCapture = false. Brain Drain is the only opt-out.
            "it shows up in a screen recording or a stream.",
            "spiral", null,
            "i could watch this all day. so could you.", "@_@"),

        // =================================================================================
        //  SCREEN EFFECTS
        //
        //  Three features on one card, so the gist has to carry the shape of all three before a
        //  bullet does any detail - hence "a wash, a blur, and a wipe you only hear": the reader
        //  knows by word eleven that the third one is not a picture.
        //
        //  Numbers are the SLIDER bounds a reader will actually meet, not the property clamps.
        //  Pink Filter clamps 0-50 in AppSettings but its slider starts at 5
        //  (PinkFilterFeatureControl.xaml:81), so the card says 5 to 50 - quoting 0 would promise
        //  an invisible-wash setting the UI does not offer.
        //
        //  The ramp bullet is deliberately about the RAMP card's links, not the feature cards'.
        //  PinkFilterLinkRamp, SpiralLinkRamp and BubblesLinkRamp are dead settings that nothing
        //  reads; RampLinkPinkFilterOpacity and RampLinkBrainDrain are the live pair, written by
        //  IntensityRampFeatureControl and acted on in MainWindow.StartStop.cs:569 and :594.
        // =================================================================================
        new EmiBookCard(
            "overlays", 1, "emi_book_overlays",
            "SCREEN EFFECTS",
            "a *pink wash*, a *blur*, and a *wipe* you only hear.",
            new[] { "*Pink Filter* tints every monitor, *5 to 50%*, in any *color*",
                   "*Brain Drain* hazes the screen: *strength 1-100*, plus *melting mode*",
                   "*Mind Wipe* is *sound only*, *1 to 180* stings an hour",
                   "they *stack*, and the *Intensity Ramp* can deepen both" },
            // The asymmetry, said from the reader's side. The blur is the app's only
            // WDA_EXCLUDEFROMCAPTURE surface (OverlayCaptureAffinity), and the "Show in
            // screenshots" toggle turns that off - but the default is what a card describes.
            "only the blur hides from a screen capture. the pink shows.",
            "braindrain", null,
            "soft screen, soft head. that is the order.", "=_="),

        // =================================================================================
        //  BUBBLES
        //
        //  Two features on one card, and they are two because the second is the first turned
        //  into a test. The bullets run rate and size, then the trigger surprise, then the
        //  counting game, then the lock on it - smallest commitment first.
        //
        //  "1 to 60 a minute" and not per hour: BubbleService.cs:213 computes the interval as
        //  60000 / frequency and logs "bubbles/min", while the Bubble Count game sitting right
        //  next to it IS per hour (1-10). Two neighbouring features with different units is
        //  exactly what a card gets wrong, so the unit travels with the number.
        //
        //  The catch is the failure a reader would otherwise file as a bug: with no videos in
        //  the assets folder, BubbleCountService logs "No videos found" (line ~215), completes
        //  the queued interaction and returns. Nothing is shown, nothing is said, and the
        //  setting stays switched on looking perfectly healthy.
        // =================================================================================
        new EmiBookCard(
            "bubbles", 1, "emi_book_bubbles",
            "BUBBLES",
            "bubbles *drift up* the screen. *pop* them for *XP*.",
            new[] { "*1 to 60* a minute, sized *50 to 150%* of normal",
                   "*Trigger Bubbles* makes up to *50%* of them fire an *effect*",
                   "*Bubble Count* plays a *video*, then asks *how many* floated past",
                   "its *strict lock* replays that video until your *count is right*" },
            "the counting game needs videos in your assets folder or it skips.",
            "bubbles", null,
            "pop pop pop. i counted. you missed one.", "(≧◡≦)"),
    };
}
