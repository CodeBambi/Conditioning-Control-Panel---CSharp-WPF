namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DECK BATCH: the setup a new user does first.
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
/// <para><b>What the three cards below had to be written around.</b> Two claims that the UI and the
/// docs still make turned out to be false in the code, and both were dropped rather than repeated:
/// </para>
///
/// <list type="bullet">
/// <item><b>Levels do not unlock anything.</b> <c>AppSettings.IsLevelUnlocked</c> is a stub that
/// returns <c>true</c> unconditionally ("Feature level gating has been removed - every feature is
/// available from level 1"). The "Unlocks Lv.NN" region headers, the dead Brain Drain
/// <c>IsLevelUnlocked(70)</c> checks and the "Unlocks at Level N" tooltip strings were cleaned
/// out in the v6.8.7 UI-truth pass, so the shipped UI now agrees with these cards. The
/// PROGRESSION card says what a level actually pays - one skill point - and never promises a
/// door.</item>
/// <item><b>The session engine is a clock, not a mind.</b> The root <c>CLAUDE.md</c> still calls it
/// "AI-powered session management with OpenRouter integration"; <c>SessionEngine.cs</c> is a
/// one-second <c>DispatcherTimer</c> that lerps opacities and fires deferred starts, with no
/// network call anywhere in it. The SESSIONS card says timeline, minutes and ramp, and nothing that
/// could be read as "it decides".</item>
/// </list>
/// </summary>
internal static class EmiBookDeckSetup
{
    /// <summary>This batch's cards, in reading order within their tab.</summary>
    public static readonly EmiBookCard[] Cards =
    {
        // =================================================================================
        //  YOUR MEDIA
        // =================================================================================
        //
        // The first question a fresh install actually raises. THE CCP already spends its catch on
        // "with an empty assets folder nothing shows", so this card owes the reader the two ways
        // out of that, in the order a person tries them: put files in, or turn the wire on.
        //
        // "reddit" and not "scrolller" in the gist on purpose. Scrolller is the index the fetch
        // actually goes through, but every user-facing string in the app says Reddit
        // (label_media_source_hint, the consent MessageBox, tooltip_remote_mix) and a card that
        // introduced a second brand name for the same pool would read as a third media source.
        //
        // Nudge one is four drop types and not "drag your files in" because the ZIP is how a
        // content pack is installed - the Assets tab's own line is "After downloading a pack, just
        // drag and drop the ZIP file into this window". Packs therefore cost no bullet of their own.
        //
        // The catch carries the consent gate rather than a nudge, because consent is not a thing
        // you DO, it is a thing that happens TO you the first time you touch the online chip - and
        // the second half of it is the part nobody reads in the dialog. Both halves are literal:
        // MainWindow.Assets.cs:2340 refuses any non-local source until HasRemoteMediaConsent, and
        // AppSettings.cs:3887-3891 records the owner decision that there is no NSFW filter at all
        // because the catalog is entirely adult.
        new EmiBookCard(
            "your-media", 0, "emi_book_your_media",
            "YOUR MEDIA",
            "everything it shows comes from *your folder* or from *reddit*.",
            new[] { "drag *images*, *videos*, pack *zips* or *folders* onto the window",
                   "or point it at a *custom assets folder* you already have",
                   "nothing of your own? set the *source* to *online* and pick *subreddits*",
                   "*mixed* blends both on a *Remote share* slider, *5 to 95%*" },
            "it asks once before fetching anything remote, and nothing is curated.",
            null, null,
            "i have seen your folder. we should talk.", "^_~"),

        // =================================================================================
        //  SESSIONS
        // =================================================================================
        //
        // The hardest card in the batch to keep honest, because the vocabulary is overloaded four
        // ways (docs/primers/SESSION_PRESET_PRIMER.md §0) and one of the four meanings is actively
        // misleading here: a PRESET is a settings snapshot and starts nothing at all. So the gist
        // leads with "timeline" and the word preset never appears, even though the tab is called
        // Presets and the "sessions" target id routes to it via Nav("presets").
        //
        // Every number is the built-in set as shipped, not a range somebody rounded: Morning Drift
        // 30 min / 400 XP, Gamer Girl 45 / 800, The Distant Doll 45 / 400, Good Girls Don't Cum
        // 60 / 1200 (Models/Session.cs:140-447). The three "coming soon" placeholders in
        // GetAllSessions are IsAvailable = false and are deliberately not counted.
        //
        // "some a ramp" and not "and a ramp", which is what the first draft said. Only FOUR of the
        // thirteen features carry SupportsRamping = true (flash, pink_filter, spiral, brain_drain,
        // Models/FeatureDefinition.cs) and every one of those ramps a single opacity or intensity
        // slider. Promising a ramp on all thirteen would have been the card teaching an editor
        // control that nine of the palette entries do not have.
        //
        // The catch is the pause penalty and not the more obvious "it overwrites your settings",
        // because the overwrite is undone (RestoreSettings, which is nudge four) and the penalty is
        // not. 100 XP per pause, every pause, uncapped: SessionEngine.cs:97.
        new EmiBookCard(
            "sessions", 0, "emi_book_sessions",
            "SESSIONS",
            "one *timeline*, driving every tool for you, *minute by minute*.",
            new[] { "*four* ship built in, *30 to 60 minutes*, *400 to 1200 XP*",
                   "the *editor* drags any of *13* tools onto your own *timeline*",
                   "each gets a *start minute*, an *end minute*, and some a *ramp*",
                   "your own settings are *put back* the second it *ends*" },
            "every pause costs you 100 XP, and you never get them back.",
            "sessions", null,
            "i can watch a clock. it is one of my talents.", "(◔_◔)"),

        // =================================================================================
        //  PROGRESSION
        // =================================================================================
        //
        // Written around the dead unlock system (see the class doc above). What is left after that
        // is still four real things, so the card says those four and stops: a skill point per level
        // (SkillTreeService.PointsPerLevel = 1, awarded in OnLevelUp), the three-seat daily board
        // plus one weekly, the 69 entries in Achievement.All, and the level multiplier
        // that makes the same session worth more later than it was at level 1
        // (SessionEngine.cs:396-403).
        //
        // The reroll nudge deliberately states no COUNT. The daily pool is "1 base + 2 for Patreon"
        // plus skill-tree bonuses (QuestService's Reroll region), so any number on the card would be
        // wrong for somebody. What is true for everybody is the rule the button actually enforces:
        // RerollDailyQuest refuses a completed seat, so it is the unfinished ones that can move.
        //
        // The catch is the login gate and not the seasonal level wipe, though both are real. The
        // wipe is scheduled, announced and recapped by SeasonRecapService; the gate is silent -
        // AddXP returns before it touches anything when App.IsLoggedIn is false and there is no
        // offline username, and the only trace is a Debug line. Somebody who never signs in earns
        // literally nothing and no surface in the app says so, which is the exact shape of thing
        // this strip exists for.
        new EmiBookCard(
            "progression", 0, "emi_book_progression",
            "XP AND LEVELS",
            "the things you run pay *XP*. enough *XP* is a *level*.",
            new[] { "each level pays *one skill point* into the *skill tree*",
                   "*three daily quests* and *one weekly*. an unfinished one can *reroll*",
                   "*69 achievements*, and an opt-in *Discord* post for each one",
                   "a finished *session* pays its XP times a *level multiplier*" },
            "nothing pays XP unless you are signed in, or offline with a name.",
            "progression", null,
            "i keep the tally. you keep the trophies.", "(≧◡≦)"),
    };
}
