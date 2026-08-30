using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DECK BATCH: the ones that take control.
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
/// <para><b>Why these three are written differently.</b> Every other card in the book sells a tool.
/// These three hand the machine to something that is not the reader: to a word list that watches the
/// screen, to a timer that will not stop, and to a schedule (or a stranger) that presses the buttons.
/// On a card like that the catch is the most important line on the panel, so each catch below is the
/// one sentence somebody would be angry not to have read first - and none of the four nudges is
/// allowed to be a mood. They are controls, numbers and button labels, because a person deciding
/// whether to switch one of these on needs the mechanism, not the atmosphere.</para>
///
/// <para><b>Where the code disagreed with the app's own words</b>, the code won. Three places, all
/// three of them load-bearing:</para>
/// <list type="bullet">
/// <item>The lockdown escape is FIVE clicks, each within 1000 ms of the last, and then a typed
/// phrase - <c>MainWindow.Lab.cs:910-926</c> plus <c>LockdownService.TryExitWithPhrase</c>. The
/// public site's lockdown guide still says one tap. The card says five, and names the phrase,
/// because a safety valve nobody can find is not a safety valve.</item>
/// <item>Takeover is NOT level gated. <c>AppSettings.cs:5315</c> heads its region "Unlocks Lv.100"
/// and the property's own doc comment says "Requires level 100", but
/// <c>AutonomyService.CanStart()</c> asks only for the setting, consent and premium (or the daily
/// free day). No card may repeat a requirement the engine does not enforce.</item>
/// <item>Nothing in the Awareness path uploads anything - neither <c>ScreenOcrService</c> nor
/// <c>KeywordTriggerService</c> holds an <c>HttpClient</c>, and the recognition itself is Windows'
/// own local <c>OcrEngine</c>. So the awareness catch is about WHAT is read, not about where it
/// goes, which is the honest version of the worry rather than the scary one.</item>
/// </list>
/// </summary>
internal static class EmiBookDeckControl
{
    /// <summary>This batch's cards, in reading order within their tab.</summary>
    public static readonly EmiBookCard[] Cards =
    {
        // =================================================================================
        //  THE AWARENESS ENGINE
        //
        //  Disambiguation first, because the app carries two things called "awareness" and a card
        //  that blurred them would be worse than no card. THIS one is the Triggers tab: keyword
        //  triggers plus the screen OCR scan (label_awareness_engine, "Set up keyword triggers, OCR
        //  scanning, presets, and reactions"). The OTHER one is the companion's Window Awareness,
        //  which watches the foreground window title and sends a summary to the AI, and which says
        //  so itself in awareness_consent_never_note: "The Triggers tab's keyword scanning is a
        //  separate feature with its own switch." Nothing below describes that feature.
        //
        //  The catch is the screen scan, and it is deliberately not the privacy scare. The scan
        //  really does copy every monitor (ScreenOcrService.CaptureAndRecognizeAsync ->
        //  ScanAllScreensAsync over App.GetAllScreensCached), which is the thing a person has to
        //  weigh before arming it while a work call is on the second display. It also never leaves
        //  the machine, which is why the sentence is "reads" and not "sends".
        // =================================================================================
        new EmiBookCard(
            "awareness", 2, "emi_book_awareness",
            "THE AWARENESS ENGINE",
            "give it words. it watches your *typing* and your *screen*.",
            new[] { "install the *Bimbo*, *Puppy*, *Trance* or *Chastity* pack, or write your own",
                   "the *OCR* scan re-reads every monitor every *2 to 10 seconds*",
                   "one hit can *flash an image*, *buzz a toy* or *mind wipe* the screen",
                   "*fence* it to named apps. the master switch resets every *launch*" },
            "the screen scan reads everything else on those monitors too.",
            "awareness", null,
            "i only look for the words you gave me.", "(◔_◔)"),

        // =================================================================================
        //  LOCKDOWN
        //
        //  THE ESCAPE GESTURE, verified rather than repeated. MainWindow.Lab.cs:910 is
        //  TxtLockdownTimer_Click: the click counter resets whenever more than 1000 ms has passed
        //  since the previous click, and the hidden exit box appears only at a count of five. The
        //  box then wants a phrase, and LockdownService.TryExitWithPhrase compares it against the
        //  literal "let me out". So the gesture is five clicks a second apart at most, then a typed
        //  phrase - not the one tap the website's lockdown guide still describes.
        //
        //  Naming the phrase on the card was a deliberate call. Every other door out of a lockdown
        //  is a gamble (the Emergency Exit's labyrinth game sends the user back 100% of the time,
        //  EmergencyExitHostService.SendBackChance), the panic key is off by default while it runs,
        //  and the app's own primer calls the phrase "the real safety valve". A safety valve that
        //  is a secret is not one, and this book is where the app tells the truth about itself.
        //
        //  The catch agrees with the activation dialog rather than with the LOCKDOWN ACTIVE screen.
        //  MainWindow.Lab.cs:83-85 promises "You CANNOT close the application (minimizing still
        //  works)" and "Ctrl+Alt+Del -> Task Manager as a safety valve", while
        //  tooltip_you_are_in_lockdown_mode_there_is_no_escape shouts "THERE IS NO ESCAPE". The
        //  dialog is the true one; the shouting is in character. A card cannot be in character
        //  about this, so it says the OS still wins.
        //
        //  It also has to agree with the-panic-key's catch ("No Panic and Lockdown can switch it
        //  off"), which it does: LockdownDisablePanicKey defaults true (AppSettings.cs:3127) and
        //  Activate() writes PanicKeyEnabled = false. It is a Safety the user can untick, so the
        //  nudge says "default on" rather than "always".
        // =================================================================================
        new EmiBookCard(
            "lockdown", 2, "emi_book_lockdown",
            "LOCKDOWN",
            "*5 minutes* to *4 hours* where the app refuses to close.",
            new[] { "*Safeties* default on: strict lock, no *panic key*, no *Alt+F4*",
                   "*Possession* makes the app's own UI misbehave on purpose",
                   "*EMERGENCY EXIT* rolls one of *four minigames*. lose and the clock *restarts full*",
                   "*five clicks* on the timer, under a *second* apart, then type *let me out*" },
            "Task Manager still ends it. this was never a real cage.",
            "lockdown", null,
            "the timer is the one thing you cannot switch off.", "o_o"),

        // =================================================================================
        //  TAKEOVER  (+ Remote Control, in one nudge)
        //
        //  These two are NOT the same feature and the card does not pretend otherwise. Takeover is
        //  AutonomyService: a local schedule that fires effects at you on its own clock, inside a
        //  permission list you tick. Remote Control is RemoteControlService: a second person, on a
        //  phone, holding a session code and a 4-digit PIN. Different operator, different failure
        //  mode, different tab. What earns them one card is the seam between them - the Full remote
        //  tier can send "start_autonomy" (RemoteControlService.cs:1234), so a controller can arm
        //  the local schedule and walk away. Takeover is the stronger half and gets the card;
        //  Remote Control gets nudge four and deserves its own card in a later wave.
        //
        //  Note what the card does NOT say. AppSettings.cs:5315 heads the region "Autonomy Mode
        //  (Unlocks Lv.100)" and the property doc says "Requires level 100 and explicit consent",
        //  but AutonomyService.CanStart() never reads PlayerLevel - it wants AutonomyModeEnabled,
        //  AutonomyConsentGiven and premium (or DailyFree "takeover"). Level 100 is not a real
        //  requirement, so it is not on the card.
        //
        //  The catch is the one thing the consent dialog gets wrong by omission. It promises "You
        //  can stop her at any time by clicking the Stop button" (MainWindow.Autonomy.cs:156), and
        //  then MainWindow.Autonomy.cs:137-143 refuses exactly that while a lockdown is running
        //  (#514) - as does the enable checkbox at :43-52. Two features that are individually
        //  reversible are not reversible together, and that is precisely the sort of thing somebody
        //  should learn from a book rather than from a message box at the wrong moment.
        // =================================================================================
        new EmiBookCard(
            "takeover", 2, "emi_book_takeover",
            "TAKEOVER",
            "the app *acts on its own*, inside limits you set.",
            new[] { "*intensity 1 to 10*, and a *cooldown* of 10 to 300 seconds",
                   "tick what she may fire: *flashes*, *video*, *bubbles*, *lock cards*",
                   "she waits until you are *idle*, or rolls a *random* clock",
                   "*Remote Control* hands the same dials to a person with your *code*" },
            "Stop ends it any time. during a lockdown, it does not.",
            "takeover", null,
            "the buttons still get pressed. just not by you.", "-_-"),
    };
}
