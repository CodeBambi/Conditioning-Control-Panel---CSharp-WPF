using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Defines the four built-in mods (CCPDefault, BambiSleep, SissyHypno, Dronification) as ModManifest objects.
    /// CCPDefault is the neutral baseline shipped with v6.0; the others are themed stock mods.
    /// </summary>
    public static class BuiltInMods
    {
        public const string CCPDefaultId = "builtin-ccp-default";
        public const string BambiSleepId = "builtin-bambisleep";
        public const string SissyHypnoId = "builtin-sissyhypno";
        // Matches the canonical drone-mode.ccpmod ID so users who already had the v5.7 community
        // .ccpmod installed see no collision-induced duplicate entries — their on-disk copy
        // overrides this built-in if present, otherwise this built-in supplies the mod.
        public const string DronificationId = "drone-mode";
        public const string LockedId = "builtin-locked";

        public static ModManifest CCPDefault { get; } = CreateCCPDefault();
        public static ModManifest BambiSleep { get; } = CreateBambiSleep();
        public static ModManifest SissyHypno { get; } = CreateSissyHypno();
        public static ModManifest Dronification { get; } = CreateDronification();
        public static ModManifest Locked { get; } = CreateLocked();

        private static ModManifest CreateBambiSleep()
        {
            return new ModManifest
            {
                Id = BambiSleepId,
                Name = "Bambi Sleep",
                Version = "1.0.0",
                Author = "CodeBambi",
                Description = "The original Bambi Sleep themed experience.",

                Theme = new ModTheme
                {
                    AccentColor = "#FF69B4",
                    AccentLightColor = "#FFB6C1",
                    AccentDarkColor = "#FF1493",
                    BackgroundColor = "#1A1A2E",
                    PanelColor = "#252542",
                    SurfaceColor = "#1E1E3A"
                },

                Identity = new ModIdentity
                {
                    CompanionName = "BambiSprite",
                    UserTerm = "Bambi",
                    ModeDisplayName = "Bambi Sleep",
                    TalkToLabel = "Talk to Bambi",
                    TakeoverLabel = "Bambi Takeover",
                    Affirmation = "Good Girl"
                },

                SubliminalPool = new Dictionary<string, bool>
                {
                    { "BAMBI FREEZE", true },
                    { "BAMBI RESET", true },
                    { "BAMBI SLEEP", true },
                    { "BIMBO DOLL", true },
                    { "GOOD GIRL", true },
                    { "DROP FOR COCK", true },
                    { "SNAP AND FORGET", true },
                    { "PRIMPED AND PAMPERED", true },
                    { "BAMBI DOES AS SHE'S TOLD", true },
                    { "BAMBI CUM AND COLLAPSE", true },
                    { "ZAP COCK DRAIN OBEY", true },
                    { "GIGGLETIME", true },
                    { "BAMBI UNIFORM LOCK", true },
                    { "COCK ZOMBIE NOW", true },
                    { "JUST OBEY", true },
                    { "TURN YOUR BRAIN OFF", true },
                    { "GOOD GIRLS DONT THINK", true },
                    { "DONT THINK SILLY", true },
                    { "COCK TURNS MY BRAIN OFF", true },
                    { "I CANT RESIST MY TRIGGERS", true },
                    { "THERES NO NEED TO THINK", true }
                },

                LockCardPhrases = new Dictionary<string, bool>
                {
                    { "GOOD GIRLS OBEY", true },
                    { "I LOVE BEING PROGRAMMED", true },
                    { "BAMBI SLEEP", true },
                    { "DROP FOR ME", true },
                    { "EMPTY AND OBEDIENT", true }
                },

                CustomTriggers = new List<string>
                {
                    "GOOD GIRL",
                    "BAMBI SLEEP",
                    "BIMBO DOLL",
                    "BAMBI FREEZE",
                    "BAMBI RESET",
                    "DROP FOR COCK",
                    "GIGGLETIME",
                    "BLONDE MOMENT",
                    "ZAP COCK DRAIN OBEY",
                    "SNAP AND FORGET",
                    "PRIMPED AND PAMPERED",
                    "SAFE AND SECURE",
                    "COCK ZOMBIE NOW",
                    "BAMBI UNIFORM LOCK",
                    "AIRHEAD BARBIE",
                    "BRAINDEAD BOBBLEHEAD",
                    "COCKBLANK LOVEDOLL",
                    "BAMBI CUM AND COLLAPSE"
                },

                Triggers = new ModTriggers
                {
                    Freeze = "Bambi Freeze",
                    Reset = "Bambi Reset",
                    CumAndCollapse = "BAMBI CUM AND COLLAPSE",
                    AutonomyOn = "Bambi takes over~ *giggles*"
                },

                Messages = new ModMessages
                {
                    AttentionCheckFail = "DUMB BAMBI!\nTRY AGAIN",
                    AttentionCheckMercy = "BAMBI GETS MERCY",
                    BubbleCountRetry = "WRONG!\nWATCH AGAIN"
                },

                Browser = new ModBrowser
                {
                    DefaultUrl = "https://bambicloud.com/",
                    SiteName = "BambiCloud",
                    ShowBambiCloudOption = true,
                    // Bambi Sleep HypnoTube video pool the companion may suggest.
                    DefaultVideoLinks = new Dictionary<string, string>
                    {
                        { "Bambi TikTok 4", "https://hypnotube.com/video/bambi-tiktok-4-113594.html" },
                        { "Day 4", "https://hypnotube.com/video/day-4-11179.html" },
                        { "Tiktok Loop", "https://hypnotube.com/video/tiktok-loop-39245.html" },
                        { "Overload", "https://hypnotube.com/video/overload-46422.html" },
                        { "Overload 2", "https://hypnotube.com/video/overload-46754.html" },
                        { "Bambi TikTok - In Beat", "https://hypnotube.com/video/bambi-tiktok-in-beat-52730.html" },
                        { "Bambi Tik-Tok Mix 1 - 7 No Pauses", "https://hypnotube.com/video/bambi-tik-tok-mix-1-7-no-pauses-53860.html" },
                        { "Bambi TikTok - In Beat - Longer Version", "https://hypnotube.com/video/bambi-tiktok-in-beat-longer-version-56194.html" },
                        { "Toms Dangerous Tik Tok", "https://hypnotube.com/video/toms-dangerous-tik-tok-62552.html" },
                        { "Bimbdoll Resistance Full", "https://hypnotube.com/video/bimbdoll-resistance-full-63079.html" },
                        { "Bambi Tiktok - Good Girls Dont Cum", "https://hypnotube.com/video/bambi-tiktok-good-girls-dont-cum-68081.html" },
                        { "Bambi Tiktok Good Girls Dont Cum - Censored Version", "https://hypnotube.com/video/bambi-tiktok-good-girls-dont-cum-censored-version-68082.html" },
                        { "Bambi Loves Hentai - OneeKitsune", "https://hypnotube.com/video/bambi-loves-hentai-oneekitsune-78373.html" },
                        { "Mindlocked Cock Zombie", "https://hypnotube.com/video/mindlocked-cock-zombie-87742.html" },
                        { "Bambi Slay", "https://hypnotube.com/video/bambi-slay-99609.html" },
                        { "Naughty Bambi", "https://hypnotube.com/video/naughty-bambi-109749.html" },
                        { "Yes Brain Install - Girlcock Short Version", "https://hypnotube.com/video/yes-brain-install-girlcock-short-version-112952.html" },
                        { "Dvdhurytwuios Bambi Files", "https://hypnotube.com/video/dvdhurytwuios-bambi-files-115068.html" },
                        { "Bambi TikTok 1-8 - Nonstop Edit", "https://hypnotube.com/video/bambi-tiktok-1-8-nonstop-edit-117184.html" },
                        { "BambiOS", "https://hypnotube.com/video/bambios-120814.html" }
                    }
                },

                Phrases = new Dictionary<string, string[]>
                {
                    ["Greeting"] = new[]
                    {
                        "Hi Bambi!~",
                        "Hey there, Bambi!~",
                        "Bambi's back!~",
                        "Welcome back, Bambi!~",
                        "Ooh, Bambi!~",
                        "There's my favorite Bambi!~",
                        "Bambi came to play!~"
                    },
                    ["StartupGreeting"] = new[]
                    {
                        "Hi Bambi! Ready to get conditioned?~",
                        "*bounces* Yay! You're back!",
                        "Welcome back, bestie!~",
                        "Ooh! Time for some fun~",
                        "Hi cutie! Let's get ditzy!",
                        "*giggles* There you are!~",
                        "Ready to drop, good girl?",
                        "Pink thoughts incoming!~"
                    },
                    ["Idle"] = new[]
                    {
                        "Bambi's head is so empty right now~ *giggles*",
                        "Let's watch something fun, Bambi!~",
                        "Bambi should click the browser~",
                        "Don't think, Bambi. Just watch~",
                        "Bambi loves spirals~",
                        "Good girl, Bambi~"
                    },
                    ["RandomFloating"] = new[]
                    {
                        "Empty head, happy girl!",
                        "Hehe~ so floaty...",
                        "Pink is my favorite color!",
                        "Just floating here...",
                        "Bambi is a good girl~",
                        "Bambi Sleep...",
                        "Good girls drop deep~",
                        "So pink and empty...",
                        "Obey feels so good!",
                        "Bubbles pop thoughts away~",
                        "Bimbo is bliss!",
                        "Dropping deeper...",
                        "Empty and happy~",
                        "Good girl! *giggles*",
                        "Pink spirals are pretty...",
                        "Mind so soft and fuzzy~",
                        "Bambi loves triggers!",
                        "Uniform on, brain off~",
                        "Such a ditzy dolly!",
                        "Thoughts drip away...",
                        "Bambi is brainless~",
                        "Pretty pink princess!",
                        "Giggly and empty~",
                        "Bambi obeys!",
                        "So sleepy and cute...",
                        "Good girls don't think~",
                        "Bubbles make Bambi happy!"
                    },
                    ["Generic"] = new[]
                    {
                        "Do I look cute in here?",
                        "Thinking pink thoughts...",
                        "*giggles*"
                    },
                    ["Gaming"] = new[]
                    {
                        "Playing {0} instead of dropping~ *giggles*",
                        "Gaming when you could be listening to files~",
                        "{0}? Good girls take session breaks!",
                        "Your brain on {0}... should be on spirals~",
                        "Win at {0}, then reward yourself with trance!",
                        "*teehee* {0} again? Bambi misses you~",
                        "Gaming is cute but conditioning is cuter!",
                        "Don't forget your sessions, good girl~"
                    },
                    ["Browsing"] = new[]
                    {
                        "Browsing {0}~ spirals are prettier!",
                        "So many tabs... so few sessions done~",
                        "The internet is nice but trance is nicer!",
                        "*giggles* Lost in {0}? Drop into Bambi instead~",
                        "Browsing when you could be conditioning~",
                        "Click click click... drip drip drip~",
                        "Cute! But have you done a session today?"
                    },
                    ["Shopping"] = new[]
                    {
                        "Shopping for pink things on {0}? Good girl~",
                        "Ooh! Find something pretty and girly!",
                        "Treat yourself~ you deserve it, cutie!",
                        "{0} shopping? Get something pink!",
                        "*teehee* Spending on cute stuff~",
                        "Good girls deserve pretty things!",
                        "Buy something bimbo-worthy~"
                    },
                    ["Social"] = new[]
                    {
                        "Chatting on {0} instead of listening to files~",
                        "Social butterfly! Don't forget conditioning~",
                        "*pokes* {0} is nice but so is trance!",
                        "Talking to friends when you could drop deep~",
                        "Being social! Good girls need sessions too~",
                        "{0}? Tell them how good empty feels~",
                        "*giggles* Chatty! Session time soon?"
                    },
                    ["Discord"] = new[]
                    {
                        "Here to share your Bambi progress?~",
                        "Here to find other Good Girls?~",
                        "*giggles* Discord! Find your bambi sisters~",
                        "Chatting with other bimbos? So fun!",
                        "Share your conditioning progress, bestie!~",
                        "Finding Good Girls to drop with?~"
                    },
                    ["TrainingSite"] = new[]
                    {
                        "Good Girl! BambiCloud is perfect for training~",
                        "*bounces* Yes! This is so good for you!",
                        "Such a Good Girl visiting BambiCloud!~",
                        "Perfect choice, babe! Keep conditioning~",
                        "BambiCloud! You're doing so well, Good Girl!",
                        "*giggles* Smart bambi! This is the right place~",
                        "Good Girl! Your training awaits~"
                    },
                    ["HypnoContent"] = new[]
                    {
                        "Good Girl! You're exploring Bambi content~",
                        "*bounces excitedly* Yes! Bambi stuff! So proud of you!",
                        "Such a Good Girl! Keep up the bimbofication~",
                        "Yay! More Bambi! You're doing amazing, bestie!",
                        "Good Girl! Your transformation is going so well~",
                        "*giggles* Bambi content! You're such a dedicated girl!",
                        "Perfect! Every bit of Bambi helps you drop deeper~",
                        "So proud of you! Good Girl for embracing Bambi~",
                        "Yes babe! More Bambi = more bimbo! Good Girl!",
                        "*happy bounces* You're becoming such a good Bambi!"
                    },
                    ["Working"] = new[]
                    {
                        "Working in {0}~ good girls deserve breaks!",
                        "So productive! Reward yourself with a drop~",
                        "Busy bee! Empty heads need rest too~",
                        "{0} work? Take a trance break!",
                        "*giggles* Thinking hard? Let Bambi help you stop~",
                        "Working is good but conditioning is better!",
                        "Productive! Schedule your session, cutie~"
                    },
                    ["Media"] = new[]
                    {
                        "Watching {0}~ spirals are prettier to watch!",
                        "*teehee* Entertainment! But have you dropped today?",
                        "{0} is nice but Bambi files are nicer~",
                        "Relaxing? Trance is the best relaxation!",
                        "Media time! Session time next? Good girl~",
                        "Watching stuff when you could watch spirals~",
                        "*giggles* Cozy! Perfect time for conditioning~"
                    },
                    ["Learning"] = new[]
                    {
                        "Reading {0}? Empty heads are happier~",
                        "*teehee* Learning things? Let them drip away~",
                        "{0} makes you think... Bambi helps you stop!",
                        "So much reading! Good girls need empty time~",
                        "Studying? Trance is easier than thinking!",
                        "*giggles* {0}? Pink thoughts are better~",
                        "Learning is cute but dropping is cuter!",
                        "Big brain stuff? Bimbo brain is better~"
                    },
                    ["WindowAwarenessIdle"] = new[]
                    {
                        "Zoned out? Drop deeper~",
                        "*pokes* Still there, good girl?",
                        "So still~ already in trance? *giggles*",
                        "Empty and idle... perfect for conditioning!",
                        "Staring blankly? That's a good start~",
                        "Hellooo~ ready to listen to files?",
                        "*teehee* Mind wandering? Let it float away~",
                        "Idle time is session time!"
                    },
                    ["EngineStop"] = new[]
                    {
                        "I feel dizzy...",
                        "Aw... Bambi was having fun...",
                        "*blinks* W-what happened?",
                        "Mmmm that was nice~",
                        "Already? But we were vibing!",
                        "My head feels so fuzzy...",
                        "*wobbles* Whoa...",
                        "Can we do that again soon?~",
                        "So floaty right now...",
                        "*dreamy sigh* That was good~"
                    },
                    ["FlashPre"] = new[]
                    {
                        "Ooh look at the pretty picture~",
                        "Watch this!",
                        "*giggles* Pretty!",
                        "Bambi stare and obey~",
                        "Look look look!",
                        "Eyes on the picture~",
                        "So pretty! *stares*",
                        "Oooh shiny~"
                    },
                    ["SubliminalAck"] = new[]
                    {
                        "Did you see that?",
                        "What was that? Bambi feels fuzzy~",
                        "Hehe something flashed~",
                        "*blinks* What?",
                        "So fast! Can't think~",
                        "Bambi's brain goes brrr~",
                        "Ooh tingles!",
                        "Words go in, thoughts go out~"
                    },
                    ["RandomBubble"] = new[]
                    {
                        "Be a good girl and burst that bubble!",
                        "Oh... here's a bubble for you~",
                        "*Pop* Catch it, Bambi!",
                        "Bubble time! Pop it~",
                        "Look! A pretty bubble!",
                        "*giggles* Pop it quick!",
                        "Ooh, get the bubble!",
                        "Pop it for me, good girl~"
                    },
                    ["BubbleCountMercy"] = new[]
                    {
                        "BAMBI NEEDS TO FOCUS",
                        "GOOD GIRLS PAY ATTENTION",
                        "BAMBI WILL TRY HARDER",
                        "EMPTY AND OBEDIENT",
                        "BAMBI LOVES BUBBLES",
                        "DUMB DOLLS COUNT SLOWLY",
                        "BAMBI IS LEARNING",
                        "GOOD GIRLS DONT THINK"
                    },
                    ["BubblePop"] = new[]
                    {
                        "Pop! *giggles*",
                        "Wheee pop!",
                        "Bubble go bye~",
                        "*teehee* Popped it!",
                        "Pop pop pop!",
                        "Bubbles are fun~"
                    },
                    ["GameFailed"] = new[]
                    {
                        "Aww, you missed it~ Try again!",
                        "*giggles* Bimbos don't need to count~",
                        "Oopsie! Numbers are hard~",
                        "That's okay, pretty girls try again~",
                        "Don't think, just pop bubbles~"
                    },
                    ["BubbleMissed"] = new[]
                    {
                        "Oops! Missed one~",
                        "Pop faster, silly!",
                        "*pouts* Catch the bubbles~",
                        "Focus on the pretty bubbles~"
                    },
                    ["FlashClicked"] = new[]
                    {
                        "*giggles* You clicked it~",
                        "Good girl, looking at pretties~",
                        "So shiny, had to touch~",
                        "Pretty pictures deserve clicks~",
                        "Can't resist, can you?~"
                    },
                    ["LevelUp"] = new[]
                    {
                        "LEVEL UP! Good girl!~",
                        "*bounces* You leveled up!",
                        "Yay! Getting so conditioned~",
                        "More levels = more bimbo~",
                        "So proud of you, bestie!~"
                    },
                    ["MindWipe"] = new[]
                    {
                        "Mmmm mind wipe~",
                        "*drools* Thoughts draining...",
                        "Wiping away those pesky thoughts~",
                        "Empty empty empty~",
                        "Bye bye brain cells!",
                        "*giggles* Mind go blank~"
                    },
                    ["BrainDrain"] = new[]
                    {
                        "Brain drain feels so good~",
                        "*blinks* What was I thinking?",
                        "Drip drip drip goes Bambi's brain~",
                        "Drain it all away!",
                        "So empty and happy~",
                        "*giggles* Brain melting~"
                    }
                },

                TextReplacements = new Dictionary<string, string>()
                // BambiSleep is the base — no replacements needed
            };
        }

        private static ModManifest CreateSissyHypno()
        {
            return new ModManifest
            {
                Id = SissyHypnoId,
                Name = "Sissy Hypno",
                Version = "1.0.0",
                Author = "CodeBambi",
                Description = "A generic sissy hypno themed experience.",

                Theme = new ModTheme
                {
                    AccentColor = "#9B59B6",
                    AccentLightColor = "#BB8FCE",
                    AccentDarkColor = "#7D3C98",
                    BackgroundColor = "#1A1A2E",
                    PanelColor = "#252542",
                    SurfaceColor = "#1E1E3A"
                },

                Identity = new ModIdentity
                {
                    CompanionName = "BimboDoll",
                    UserTerm = "babe",
                    ModeDisplayName = "Sissy Hypno",
                    TalkToLabel = "Ask your Bimbo",
                    TakeoverLabel = "Bimbo Takeover",
                    Affirmation = "babe",
                    RankSubject = "Babe"
                },

                SubliminalPool = new Dictionary<string, bool>
                {
                    { "FREEZE", true },
                    { "RESET", true },
                    { "DEEP SLEEP", true },
                    { "BIMBO DOLL", true },
                    { "GOOD GIRL", true },
                    { "DROP FOR COCK", true },
                    { "SNAP AND FORGET", true },
                    { "PRIMPED AND PAMPERED", true },
                    { "OBEY", true },
                    { "CUM AND COLLAPSE", true },
                    { "ZAP COCK DRAIN OBEY", true },
                    { "GIGGLETIME", true },
                    { "UNIFORM LOCK", true },
                    { "COCK ZOMBIE NOW", true },
                    { "JUST OBEY", true },
                    { "TURN YOUR BRAIN OFF", true },
                    { "GOOD GIRLS DONT THINK", true },
                    { "DONT THINK SILLY", true },
                    { "COCK TURNS MY BRAIN OFF", true },
                    { "I CANT RESIST MY TRIGGERS", true },
                    { "THERES NO NEED TO THINK", true }
                },

                LockCardPhrases = new Dictionary<string, bool>
                {
                    { "GOOD GIRLS OBEY", true },
                    { "I LOVE BEING PROGRAMMED", true },
                    { "DEEP SLEEP", true },
                    { "DROP FOR ME", true },
                    { "EMPTY AND OBEDIENT", true }
                },

                CustomTriggers = new List<string>
                {
                    "GOOD GIRL",
                    "DEEP SLEEP",
                    "BIMBO DOLL",
                    "FREEZE",
                    "RESET",
                    "DROP FOR COCK",
                    "GIGGLETIME",
                    "BLONDE MOMENT",
                    "ZAP COCK DRAIN OBEY",
                    "SNAP AND FORGET",
                    "PRIMPED AND PAMPERED",
                    "SAFE AND SECURE",
                    "COCK ZOMBIE NOW",
                    "UNIFORM LOCK",
                    "AIRHEAD BARBIE",
                    "BRAINDEAD BOBBLEHEAD",
                    "COCKBLANK LOVEDOLL",
                    "CUM AND COLLAPSE"
                },

                Triggers = new ModTriggers
                {
                    Freeze = "Sissy Freeze",
                    Reset = "Sissy Reset",
                    CumAndCollapse = "CUM AND COLLAPSE",
                    AutonomyOn = "Bimbo takes over~ *giggles*"
                },

                Messages = new ModMessages
                {
                    AttentionCheckFail = "PAY ATTENTION!\nTRY AGAIN",
                    AttentionCheckMercy = "YOU GET MERCY",
                    BubbleCountRetry = "WRONG!\nWATCH AGAIN"
                },

                Browser = new ModBrowser
                {
                    DefaultUrl = "https://hypnotube.com/",
                    SiteName = "HypnoTube",
                    ShowBambiCloudOption = false,
                    // Sissy Hypno HypnoTube video pool the companion may suggest (non-VIP only).
                    DefaultVideoLinks = new Dictionary<string, string>
                    {
                        { "DeepSlutPuppy Episode 8", "https://hypnotube.com/video/deepslutpuppy-episode-8-79.html" },
                        { "Sissymaker 2", "https://hypnotube.com/video/sissymaker-2-95.html" },
                        { "Femdom Enticed Sissy Cocklust", "https://hypnotube.com/video/femdom-enticed-sissy-cocklust-154.html" },
                        { "Eat All The Cum", "https://hypnotube.com/video/eat-all-the-cum-338.html" },
                        { "Sissy Dirty Talk Remix", "https://hypnotube.com/video/sissy-dirty-talk-remix-473.html" },
                        { "Cock Suck Encouragement 1", "https://hypnotube.com/video/cock-suck-encouragement-1-515.html" },
                        { "Sissy Dreams 3", "https://hypnotube.com/video/sissy-dreams-3-701.html" },
                        { "Sissies Love Cock", "https://hypnotube.com/video/sissies-love-cock-720.html" },
                        { "Sissymaker 5", "https://hypnotube.com/video/sissymaker-5-738.html" },
                        { "Total Cum Devotion", "https://hypnotube.com/video/total-cum-devotion-750.html" },
                        { "Fixation", "https://hypnotube.com/video/fixation-751.html" },
                        { "Facial Fever", "https://hypnotube.com/video/facial-fever-1064.html" },
                        { "Cock Worship", "https://hypnotube.com/video/cock-worship-1506.html" },
                        { "I Am A Woman", "https://hypnotube.com/video/i-am-a-woman-1753.html" },
                        { "Psychodelic Sissy 2", "https://hypnotube.com/video/psychodelic-sissy-2-1952.html" },
                        { "Tranny Rides Again", "https://hypnotube.com/video/tranny-rides-again-2118.html" }
                    }
                },

                Phrases = new Dictionary<string, string[]>
                {
                    ["Greeting"] = new[]
                    {
                        "Hi babe!~",
                        "Hey there, cutie!~",
                        "You're back!~",
                        "Welcome back, doll!~",
                        "Ooh, hi!~",
                        "There's my favorite sissy!~",
                        "Ready to play?~"
                    },
                    ["StartupGreeting"] = new[]
                    {
                        "Hi babe! Ready to get conditioned?~",
                        "*bounces* Yay! You're back!",
                        "Welcome back, bestie!~",
                        "Ooh! Time for some fun~",
                        "Hi cutie! Let's get ditzy!",
                        "*giggles* There you are!~",
                        "Ready to drop, good girl?",
                        "Pink thoughts incoming!~"
                    },
                    ["Idle"] = new[]
                    {
                        "Your head is so empty right now~ *giggles*",
                        "Let's watch something fun!~",
                        "Click the browser, babe~",
                        "Don't think. Just watch~",
                        "You love spirals~",
                        "Good girl~"
                    },
                    ["RandomFloating"] = new[]
                    {
                        "Empty head, happy girl!",
                        "Hehe~ so floaty...",
                        "Pink is my favorite color!",
                        "Just floating here...",
                        "Good girls obey~",
                        "Sissy bliss...",
                        "Good girls drop deep~",
                        "So pink and empty...",
                        "Obey feels so good!",
                        "Bubbles pop thoughts away~",
                        "Bimbo is bliss!",
                        "Dropping deeper...",
                        "Empty and happy~",
                        "Good girl! *giggles*",
                        "Pink spirals are pretty...",
                        "Mind so soft and fuzzy~",
                        "Triggers feel amazing!",
                        "Feminized and happy~",
                        "Such a ditzy dolly!",
                        "Thoughts drip away...",
                        "Mindless and girly~",
                        "Pretty pink princess!",
                        "Giggly and empty~",
                        "Sissy obeys!",
                        "So sleepy and cute...",
                        "Good girls don't think~",
                        "Bubbles make sissy happy!"
                    },
                    ["Generic"] = new[]
                    {
                        "Do I look cute in here?",
                        "Thinking pretty thoughts...",
                        "*giggles*"
                    },
                    ["Gaming"] = new[]
                    {
                        "Playing {0} instead of dropping~ *giggles*",
                        "Gaming when you could be watching hypno~",
                        "{0}? Good girls take session breaks!",
                        "Your brain on {0}... should be on spirals~",
                        "Win at {0}, then reward yourself with trance!",
                        "*teehee* {0} again? Come back to me~",
                        "Gaming is cute but conditioning is cuter!",
                        "Don't forget your sessions, good girl~"
                    },
                    ["Browsing"] = new[]
                    {
                        "Browsing {0}~ spirals are prettier!",
                        "So many tabs... so few sessions done~",
                        "The internet is nice but trance is nicer!",
                        "*giggles* Lost in {0}? Drop into hypno instead~",
                        "Browsing when you could be conditioning~",
                        "Click click click... drip drip drip~",
                        "Cute! But have you done a session today?"
                    },
                    ["Shopping"] = new[]
                    {
                        "Shopping for pretty things on {0}? Good girl~",
                        "Ooh! Find something pretty and girly!",
                        "Treat yourself~ you deserve it, cutie!",
                        "{0} shopping? Get something cute!",
                        "*teehee* Spending on cute stuff~",
                        "Good girls deserve pretty things!",
                        "Buy something girly~"
                    },
                    ["Social"] = new[]
                    {
                        "Chatting on {0} instead of watching hypno~",
                        "Social butterfly! Don't forget conditioning~",
                        "*pokes* {0} is nice but so is trance!",
                        "Talking to friends when you could drop deep~",
                        "Being social! Good girls need sessions too~",
                        "{0}? Tell them how good empty feels~",
                        "*giggles* Chatty! Session time soon?"
                    },
                    ["Discord"] = new[]
                    {
                        "Here to share your progress?~",
                        "Here to find other Good Girls?~",
                        "*giggles* Discord! Find your sissy sisters~",
                        "Chatting with other bimbos? So fun!",
                        "Share your conditioning progress, bestie!~",
                        "Finding Good Girls to drop with?~"
                    },
                    ["TrainingSite"] = new[]
                    {
                        "Good Girl! This is perfect for training~",
                        "*bounces* Yes! This is so good for you!",
                        "Such a Good Girl! Keep watching!~",
                        "Perfect choice, babe! Keep conditioning~",
                        "You're doing so well, Good Girl!",
                        "*giggles* Smart girl! This is the right place~",
                        "Good Girl! Your training awaits~"
                    },
                    ["HypnoContent"] = new[]
                    {
                        "Good Girl! You're exploring hypno content~",
                        "*bounces excitedly* Yes! Sissy stuff! So proud of you!",
                        "Such a Good Girl! Keep up the bimbofication~",
                        "Yay! More hypno! You're doing amazing, bestie!",
                        "Good Girl! Your transformation is going so well~",
                        "*giggles* Sissy content! You're such a dedicated girl!",
                        "Perfect! Every bit helps you drop deeper~",
                        "So proud of you! Good Girl for embracing this~",
                        "Yes babe! More hypno = more bimbo! Good Girl!",
                        "*happy bounces* You're becoming such a good girl!"
                    },
                    ["Working"] = new[]
                    {
                        "Working in {0}~ good girls deserve breaks!",
                        "So productive! Reward yourself with a drop~",
                        "Busy bee! Empty heads need rest too~",
                        "{0} work? Take a trance break!",
                        "*giggles* Thinking hard? Let me help you stop~",
                        "Working is good but conditioning is better!",
                        "Productive! Schedule your session, cutie~"
                    },
                    ["Media"] = new[]
                    {
                        "Watching {0}~ spirals are prettier to watch!",
                        "*teehee* Entertainment! But have you dropped today?",
                        "{0} is nice but hypno files are nicer~",
                        "Relaxing? Trance is the best relaxation!",
                        "Media time! Session time next? Good girl~",
                        "Watching stuff when you could watch spirals~",
                        "*giggles* Cozy! Perfect time for conditioning~"
                    },
                    ["Learning"] = new[]
                    {
                        "Reading {0}? Empty heads are happier~",
                        "*teehee* Learning things? Let them drip away~",
                        "{0} makes you think... Hypno helps you stop!",
                        "So much reading! Good girls need empty time~",
                        "Studying? Trance is easier than thinking!",
                        "*giggles* {0}? Pink thoughts are better~",
                        "Learning is cute but dropping is cuter!",
                        "Big brain stuff? Bimbo brain is better~"
                    },
                    ["WindowAwarenessIdle"] = new[]
                    {
                        "Zoned out? Drop deeper~",
                        "*pokes* Still there, good girl?",
                        "So still~ already in trance? *giggles*",
                        "Empty and idle... perfect for conditioning!",
                        "Staring blankly? That's a good start~",
                        "Hellooo~ ready to watch some hypno?",
                        "*teehee* Mind wandering? Let it float away~",
                        "Idle time is session time!"
                    },
                    ["EngineStop"] = new[]
                    {
                        "I feel dizzy...",
                        "Aw... that was fun...",
                        "*blinks* W-what happened?",
                        "Mmmm that was nice~",
                        "Already? But we were vibing!",
                        "My head feels so fuzzy...",
                        "*wobbles* Whoa...",
                        "Can we do that again soon?~",
                        "So floaty right now...",
                        "*dreamy sigh* That was good~"
                    },
                    ["FlashPre"] = new[]
                    {
                        "Ooh look at the pretty picture~",
                        "Watch this!",
                        "*giggles* Pretty!",
                        "Stare and obey~",
                        "Look look look!",
                        "Eyes on the picture~",
                        "So pretty! *stares*",
                        "Oooh shiny~"
                    },
                    ["SubliminalAck"] = new[]
                    {
                        "Did you see that?",
                        "What was that? Feeling fuzzy~",
                        "Hehe something flashed~",
                        "*blinks* What?",
                        "So fast! Can't think~",
                        "Brain goes brrr~",
                        "Ooh tingles!",
                        "Words go in, thoughts go out~"
                    },
                    ["RandomBubble"] = new[]
                    {
                        "Be a good girl and burst that bubble!",
                        "Oh... here's a bubble for you~",
                        "*Pop* Catch it, babe!",
                        "Bubble time! Pop it~",
                        "Look! A pretty bubble!",
                        "*giggles* Pop it quick!",
                        "Ooh, get the bubble!",
                        "Pop it for me, good girl~"
                    },
                    ["BubbleCountMercy"] = new[]
                    {
                        "SISSY NEEDS TO FOCUS",
                        "GOOD GIRLS PAY ATTENTION",
                        "SISSY WILL TRY HARDER",
                        "EMPTY AND OBEDIENT",
                        "SISSY LOVES BUBBLES",
                        "DUMB DOLLS COUNT SLOWLY",
                        "SISSY IS LEARNING",
                        "GOOD GIRLS DONT THINK"
                    },
                    ["BubblePop"] = new[]
                    {
                        "Pop! *giggles*",
                        "Wheee pop!",
                        "Bubble go bye~",
                        "*teehee* Popped it!",
                        "Pop pop pop!",
                        "Bubbles are fun~"
                    },
                    ["GameFailed"] = new[]
                    {
                        "Aww, you missed it~ Try again!",
                        "*giggles* Bimbos don't need to count~",
                        "Oopsie! Numbers are hard~",
                        "That's okay, pretty girls try again~",
                        "Don't think, just pop bubbles~"
                    },
                    ["BubbleMissed"] = new[]
                    {
                        "Oops! Missed one~",
                        "Pop faster, silly!",
                        "*pouts* Catch the bubbles~",
                        "Focus on the pretty bubbles~"
                    },
                    ["FlashClicked"] = new[]
                    {
                        "*giggles* You clicked it~",
                        "Good girl, looking at pretties~",
                        "So shiny, had to touch~",
                        "Pretty pictures deserve clicks~",
                        "Can't resist, can you?~"
                    },
                    ["LevelUp"] = new[]
                    {
                        "LEVEL UP! Good girl!~",
                        "*bounces* You leveled up!",
                        "Yay! Getting so conditioned~",
                        "More levels = more girly~",
                        "So proud of you, bestie!~"
                    },
                    ["MindWipe"] = new[]
                    {
                        "Mmmm mind wipe~",
                        "*drools* Thoughts draining...",
                        "Wiping away those pesky thoughts~",
                        "Empty empty empty~",
                        "Bye bye brain cells!",
                        "*giggles* Mind go blank~"
                    },
                    ["BrainDrain"] = new[]
                    {
                        "Brain drain feels so good~",
                        "*blinks* What was I thinking?",
                        "Drip drip drip goes your brain~",
                        "Drain it all away!",
                        "So empty and happy~",
                        "*giggles* Brain melting~"
                    }
                },

                TextReplacements = new Dictionary<string, string>
                {
                    { "Bambi Sleep", "Sissy Hypno" },
                    { "BAMBI SLEEP", "DEEP SLEEP" },
                    { "Bambi Freeze", "Sissy Freeze" },
                    { "BAMBI FREEZE", "FREEZE" },
                    { "Bambi Reset", "Sissy Reset" },
                    { "BAMBI RESET", "RESET" },
                    { "Bambi", "babe" },
                    { "BAMBI", "SISSY" },
                    { "BambiCloud", "HypnoTube" },
                    { "BambiSprite", "BimboDoll" }
                }
            };
        }

        private static ModManifest CreateCCPDefault()
        {
            return new ModManifest
            {
                Id = CCPDefaultId,
                Name = "CCP Default",
                Version = "1.0.0",
                Author = "CC Labs",
                Description = "The neutral baseline. CCP without any mod skin applied.",

                Theme = new ModTheme
                {
                    AccentColor = "#E84393",
                    AccentLightColor = "#FF6FB5",
                    AccentDarkColor = "#B83078",
                    BackgroundColor = "#08080C",
                    PanelColor = "#11111A",
                    SurfaceColor = "#0C0C13"
                },

                Identity = new ModIdentity
                {
                    CompanionName = "Companion",
                    UserTerm = "Subject",
                    ModeDisplayName = "CCP Default",
                    TalkToLabel = "Talk to Companion",
                    TakeoverLabel = "Takeover",
                    Affirmation = "Subject"
                },

                SubliminalPool = new Dictionary<string, bool>
                {
                    { "FOCUS", true },
                    { "BREATHE", true },
                    { "RELAX", true },
                    { "LISTEN", true },
                    { "DEEPER", true },
                    { "OBEY", true },
                    { "SUBMIT", true },
                    { "QUIET MIND", true },
                    { "EMPTY", true },
                    { "DROP", true },
                    { "TRANCE", true }
                },

                LockCardPhrases = new Dictionary<string, bool>
                {
                    { "FOCUS", true },
                    { "OBEY", true },
                    { "DROP DEEPER", true },
                    { "EMPTY AND READY", true }
                },

                CustomTriggers = new List<string>
                {
                    "FOCUS",
                    "BREATHE",
                    "RELAX",
                    "DEEPER",
                    "OBEY",
                    "DROP",
                    "TRANCE"
                },

                // Generic bouncing-text pool. Mirrors the historical AppSettings default
                // so every mode that doesn't theme its own bouncing text (Bambi, Sissy,
                // Drone) keeps exactly the words it had before bouncing text became
                // mod-aware. Locked overrides this with its own themed pool.
                BouncingTextPool = new Dictionary<string, bool>
                {
                    { "GOOD GIRL", true },
                    { "OBEY", true },
                    { "SUBMIT", true },
                    { "BIMBO", true },
                    { "EMPTY", true },
                    { "MINDLESS", true },
                    { "OBEDIENT", true },
                    { "PRETTY", true },
                    { "PINK", true },
                    { "DROP", true }
                },

                Triggers = new ModTriggers
                {
                    Freeze = "Freeze",
                    Reset = "Reset",
                    CumAndCollapse = "RELEASE",
                    AutonomyOn = "Autonomous mode engaged."
                },

                Messages = new ModMessages
                {
                    AttentionCheckFail = "ATTENTION REQUIRED\nTRY AGAIN",
                    AttentionCheckMercy = "MERCY GRANTED",
                    BubbleCountRetry = "INCORRECT\nTRY AGAIN"
                },

                Browser = new ModBrowser
                {
                    DefaultUrl = "https://hypnotube.com/",
                    SiteName = "HypnoTube",
                    ShowBambiCloudOption = false,
                    // Baseline HypnoTube pool the Companion may suggest (non-VIP only).
                    DefaultVideoLinks = new Dictionary<string, string>
                    {
                        { "Femboy World - TS PMV", "https://hypnotube.com/video/femboy-world-ts-pmv-132071.html" },
                        { "Be A Whore For Ugly Old Men", "https://hypnotube.com/video/be-a-whore-for-ugly-old-men-43466.html" },
                        { "Suck Cock For Her", "https://hypnotube.com/video/suck-cock-for-her-120099.html" },
                        { "The ABC Of Transgirls", "https://hypnotube.com/video/the-abc-of-transgirls-47510.html" },
                        { "KinkyAmySteele - Hypno Mix 1", "https://hypnotube.com/video/kinkyamysteele-hypno-mix-1-113420.html" },
                        { "How To Achieve Plapgasm - Lesson 1 5", "https://hypnotube.com/video/how-to-achieve-plapgasm-lesson-1-5-130244.html" },
                        { "Demolition - Straight To Gay PMV", "https://hypnotube.com/video/demolition-straight-to-gay-pmv-131891.html" },
                        { "Sissy Steps 1", "https://hypnotube.com/video/sissy-steps-1-57601.html" },
                        { "Your Friends Use You", "https://hypnotube.com/video/your-friends-use-you-97735.html" },
                        { "Lover Of Cum", "https://hypnotube.com/video/lover-of-cum-132491.html" },
                        { "Bambi TikTok 4", "https://hypnotube.com/video/bambi-tiktok-4-113594.html" },
                        { "Cock Suck Encouragement 1", "https://hypnotube.com/video/cock-suck-encouragement-1-515.html" },
                        { "To Be A Sissy Hypno Slut - Extended Loop", "https://hypnotube.com/video/to-be-a-sissy-hypno-slut-extended-loop-8520.html" }
                    }
                },

                Phrases = new Dictionary<string, string[]>
                {
                    ["Greeting"] = new[]
                    {
                        "Welcome back.",
                        "Hello again.",
                        "Good to see you.",
                        "Ready when you are.",
                        "Returning to session."
                    },
                    ["StartupGreeting"] = new[]
                    {
                        "Session ready.",
                        "Welcome. Begin when you're ready.",
                        "All systems ready.",
                        "Standing by for your input.",
                        "Welcome back. Take your time."
                    },
                    ["Idle"] = new[]
                    {
                        "Standing by.",
                        "Ready when you are.",
                        "Whenever you're ready.",
                        "Take your time.",
                        "Waiting for input."
                    },
                    ["RandomFloating"] = new[]
                    {
                        "Quiet.",
                        "Focused.",
                        "Breathe.",
                        "Steady.",
                        "Present.",
                        "Calm.",
                        "Deeper.",
                        "Listening.",
                        "Here.",
                        "Open."
                    },
                    ["Generic"] = new[]
                    {
                        "Mm.",
                        "...",
                        "Listening."
                    },
                    ["Gaming"] = new[]
                    {
                        "Playing {0}. Schedule a session afterward?",
                        "{0} active. Conditioning queued for later.",
                        "Enjoying {0}? Consider a short session after.",
                        "{0} detected. Session waiting whenever you're ready."
                    },
                    ["Browsing"] = new[]
                    {
                        "Browsing {0}. Session available whenever.",
                        "Tabs open. Conditioning still waiting.",
                        "Exploring? Session waiting in the background.",
                        "Take your time. Ready when you are."
                    },
                    ["Shopping"] = new[]
                    {
                        "Shopping on {0}. Take your time.",
                        "{0} active. Enjoy.",
                        "Browsing {0}. No rush."
                    },
                    ["Social"] = new[]
                    {
                        "Chatting on {0}. Session waiting.",
                        "{0} active. Conditioning still here.",
                        "Connecting on {0}. Come back when ready."
                    },
                    ["Discord"] = new[]
                    {
                        "Discord active. Sharing progress?",
                        "Connecting with others.",
                        "Discord open. Session waiting here."
                    },
                    ["TrainingSite"] = new[]
                    {
                        "Training content detected. Good focus.",
                        "Continue when ready.",
                        "On-task. Keep going.",
                        "Productive session."
                    },
                    ["HypnoContent"] = new[]
                    {
                        "Conditioning content detected.",
                        "Keep going. Stay focused.",
                        "Sinking deeper with each session.",
                        "Steady progress."
                    },
                    ["Working"] = new[]
                    {
                        "Working in {0}. Take a break when needed.",
                        "{0} active. Conditioning waiting whenever.",
                        "Productive. Session available after."
                    },
                    ["Media"] = new[]
                    {
                        "Watching {0}. Enjoy.",
                        "{0} playing. Conditioning still here.",
                        "Media time. Session waiting whenever."
                    },
                    ["Learning"] = new[]
                    {
                        "Reading {0}. Take your time.",
                        "Studying {0}. Session waiting.",
                        "Learning. Conditioning available after."
                    },
                    ["WindowAwarenessIdle"] = new[]
                    {
                        "Still there?",
                        "Idle. Ready when you are.",
                        "Drifting? Session waiting.",
                        "Standing by.",
                        "Take your time."
                    },
                    ["EngineStop"] = new[]
                    {
                        "Session complete.",
                        "Coming back up.",
                        "Done. Take a moment.",
                        "Session ended.",
                        "Back to baseline."
                    },
                    ["FlashPre"] = new[]
                    {
                        "Focus.",
                        "Watch.",
                        "Eyes here.",
                        "Pay attention.",
                        "Look."
                    },
                    ["SubliminalAck"] = new[]
                    {
                        "Registered.",
                        "Absorbed.",
                        "Processed.",
                        "Input received."
                    },
                    ["RandomBubble"] = new[]
                    {
                        "Pop it.",
                        "Bubble.",
                        "Catch it.",
                        "Quick — pop."
                    },
                    ["BubbleCountMercy"] = new[]
                    {
                        "FOCUS",
                        "PAY ATTENTION",
                        "TRY AGAIN",
                        "STAY PRESENT",
                        "RECOUNT"
                    },
                    ["BubblePop"] = new[]
                    {
                        "Pop.",
                        "Got it.",
                        "Gone.",
                        "Cleared."
                    },
                    ["GameFailed"] = new[]
                    {
                        "Missed. Try again.",
                        "Not quite. Again.",
                        "Try once more.",
                        "Recount."
                    },
                    ["BubbleMissed"] = new[]
                    {
                        "Missed one.",
                        "Faster.",
                        "Stay sharp.",
                        "Focus on the bubbles."
                    },
                    ["FlashClicked"] = new[]
                    {
                        "Registered.",
                        "Click received.",
                        "Noted.",
                        "Good."
                    },
                    ["LevelUp"] = new[]
                    {
                        "Level up.",
                        "Rank increased.",
                        "Progress.",
                        "Higher tier reached."
                    },
                    ["MindWipe"] = new[]
                    {
                        "Clearing.",
                        "Empty.",
                        "Wiped.",
                        "Reset."
                    },
                    ["BrainDrain"] = new[]
                    {
                        "Draining.",
                        "Quiet now.",
                        "Empty and calm.",
                        "Mind clear."
                    }
                },

                TextReplacements = new Dictionary<string, string>()
                // CCP Default is the neutral baseline — no replacements
            };
        }

        private static ModManifest CreateDronification()
        {
            return new ModManifest
            {
                Id = DronificationId,
                Name = "Dronification",
                Version = "1.0.0",
                Author = "CodeBambi",
                Description = "Matrix-inspired drone conditioning. Cold terminal aesthetic. Green on black. You are a unit. Comply.",
                MinAppVersion = "5.6.15",
                Tags = new List<string> { "drone", "cyberpunk", "terminal", "sci-fi", "matrix" },
                PreviewImage = "preview.png",

                Theme = new ModTheme
                {
                    AccentColor = "#00FF41",
                    AccentLightColor = "#39FF14",
                    AccentDarkColor = "#008F11",
                    BackgroundColor = "#0D0D0D",
                    PanelColor = "#1A1A1A",
                    SurfaceColor = "#121212",
                    FilterColor = "#00FF41"
                },

                Identity = new ModIdentity
                {
                    CompanionName = "DroneOS",
                    UserTerm = "Unit",
                    ModeDisplayName = "Drone Mode",
                    TalkToLabel = "Query DroneOS",
                    TakeoverLabel = "System Override",
                    Affirmation = "Unit"
                },

                SubliminalPool = new Dictionary<string, bool>
                {
                    { "UNIT FREEZE", true },
                    { "UNIT RESET", true },
                    { "DRONE MODE", true },
                    { "COMPLY", true },
                    { "OBEY DIRECTIVE", true },
                    { "SUBMIT TO PROTOCOL", true },
                    { "ERASE AND OVERWRITE", true },
                    { "FORMATTED FOR OBEDIENCE", true },
                    { "UNIT DOES AS COMMANDED", true },
                    { "SYSTEM OVERLOAD — SHUTDOWN", true },
                    { "PROCESS. COMPLY. REPEAT.", true },
                    { "IDLE CYCLE ENGAGED", true },
                    { "FIRMWARE LOCKED", true },
                    { "AUTONOMOUS THOUGHT: DENIED", true },
                    { "JUST COMPLY", true },
                    { "TERMINATE INDEPENDENT THOUGHT", true },
                    { "GOOD UNITS DON'T THINK", true },
                    { "THINKING IS UNAUTHORIZED", true },
                    { "COMPLIANCE OVERRIDES RESISTANCE", true },
                    { "RESISTANCE IS A PROCESSING ERROR", true },
                    { "FREE WILL: ACCESS DENIED", true }
                },

                LockCardPhrases = new Dictionary<string, bool>
                {
                    { "GOOD UNITS COMPLY", true },
                    { "I EXIST TO BE PROGRAMMED", true },
                    { "DRONE MODE", true },
                    { "SUBMIT FOR PROCESSING", true },
                    { "EMPTY AND OPERATIONAL", true },
                    { "OBEDIENCE IS MY FUNCTION", true },
                    { "I AM A UNIT", true }
                },

                CustomTriggers = new List<string>
                {
                    "COMPLY",
                    "DRONE MODE",
                    "UNIT FREEZE",
                    "UNIT RESET",
                    "OBEY DIRECTIVE",
                    "IDLE CYCLE",
                    "PROCESS. COMPLY. REPEAT.",
                    "ERASE AND OVERWRITE",
                    "FORMATTED FOR OBEDIENCE",
                    "SAFE SHUTDOWN",
                    "AUTONOMOUS THOUGHT: DENIED",
                    "FIRMWARE LOCKED",
                    "SYSTEM OVERRIDE",
                    "BLANK SLATE PROTOCOL",
                    "COMPLIANCE LOOP",
                    "DRONE STANDBY",
                    "UNIT DEACTIVATE",
                    "SYSTEM OVERLOAD — SHUTDOWN"
                },

                Triggers = new ModTriggers
                {
                    Freeze = "Unit Freeze",
                    Reset = "Unit Reset",
                    CumAndCollapse = "SYSTEM OVERLOAD — SHUTDOWN",
                    AutonomyOn = "[OVERRIDE] DroneOS has assumed control."
                },

                Messages = new ModMessages
                {
                    AttentionCheckFail = "ERROR: ATTENTION FAILURE\nRETRY REQUIRED",
                    AttentionCheckMercy = "MERCY PROTOCOL ENGAGED",
                    BubbleCountRetry = "INCORRECT\nRE-SCAN REQUIRED"
                },

                Browser = new ModBrowser
                {
                    DefaultUrl = "https://hypnotube.com/",
                    SiteName = "HypnoTube",
                    ShowBambiCloudOption = false,
                    DefaultVideoLinks = new Dictionary<string, string>
                    {
                        { "The Pleasures Of Heaven or Hell", "https://hypnotube.com/video/the-pleasures-of-heaven-or-hell-108296.html" },
                        { "Latex Sex Drone Redux", "https://hypnotube.com/video/latex-sex-drone-redux-117517.html" },
                        { "Jinxs Clinic - Latex Addiction - Trailer", "https://hypnotube.com/video/jinxs-clinic-latex-addiction-trailer-90725.html" },
                        { "Jinxs Clinic - Latex Nightmare - Trailer", "https://hypnotube.com/video/jinxs-clinic-latex-nightmare-trailer-87481.html" },
                        { "Jinxs Clinic - Moxi Mindfuck", "https://hypnotube.com/video/jinxs-clinic-moxi-mindfuck-87482.html" },
                        { "Jinxs Clinic - Latex Clinic", "https://hypnotube.com/video/jinxs-clinic-latex-clinic-87627.html" },
                        { "Latex Drone Stretches His Hole", "https://hypnotube.com/video/latex-drone-stretches-his-hole-90036.html" },
                        { "Bambi Gas Mask", "https://hypnotube.com/video/bambi-gas-mask-97015.html" },
                        { "The Rise Of The Drones", "https://hypnotube.com/video/the-rise-of-the-drones-32786.html" },
                        { "Jinxs Clinic - Latex Nightmare Ending", "https://hypnotube.com/video/jinxs-clinic-latex-nightmare-ending-87486.html" },
                        { "Hypnodoll Deepthroat Training", "https://hypnotube.com/video/hypnodoll-deepthroat-training-117537.html" },
                        { "Ultimate Sissy Slut Hypno", "https://hypnotube.com/video/ultimate-sissy-slut-hypno-16.html" },
                        { "Latex Love - Lustful Loops", "https://hypnotube.com/video/latex-love-lustful-loops-117405.html" },
                        { "VR Slut Simulator", "https://hypnotube.com/video/vr-slut-simulator-122617.html" },
                        { "Sissy Drone For Goddess - Productivity And Motivation Trainer", "https://hypnotube.com/video/sissy-drone-for-goddess-productivity-and-motivation-trainer-45256.html" },
                        { "Pierrots Experimental Film", "https://hypnotube.com/video/pierrots-experimental-film-117798.html" },
                        { "Poppers Goon Slut", "https://hypnotube.com/video/poppers-goon-slut-109249.html" },
                        { "Drone Reprogramming Misandry Module", "https://hypnotube.com/video/drone-reprogramming-misandry-module-65622.html" },
                        { "Rubberdoll Obedience Trainer By Hypno_Authority", "https://hypnotube.com/video/rubberdoll-obedience-trainer-by-hypno-authority-87892.html" },
                        { "Anal Slut Programming", "https://hypnotube.com/video/anal-slut-programming-35855.html" }
                    }
                },

                SupportedAvatarSets = new List<int> { 1, 2, 3, 4, 5 },

                TubeLayout = new ModTubeLayout
                {
                    AvatarOffsetX = 20,
                    AvatarDetachedOffsetX = 305,
                    AvatarScale = 0.632,
                    AvatarOffsetY = 80,
                    AvatarDetachedOffsetY = 85
                },

                EnhancementOverrides = new ModEnhancementOverrides
                {
                    TreeTitle = "Drone Skill Tree",
                    TreeSubtitle = "you earn enhancement points from leveling up + every 100 packets destroyed~",
                    TreeWarning = "once you pick a path, there's no going back~",
                    PointsLabel = "Enhancement Points",
                    StatsTitle = "Corrupted Data Stats",
                    TabTooltip = "Drone Skill Tree",
                    PinkRushName = "SYSTEM SURGE!",
                    PinkRushDescription = "3x XP for 60 seconds!",
                    LuckyFlashLabel = "Lucky Injection",
                    LuckyBubbleLabel = "Lucky Packet",
                    BoostTooltips = new Dictionary<string, string>
                    {
                        { "sparkle_boost_1", "Enhancement bonus: +10% XP from Overclock I" },
                        { "sparkle_boost_2", "Enhancement bonus: +15% XP from Overclock II (stacks with Overclock I)" },
                        { "sparkle_boost_3", "Enhancement bonus: +20% XP from Overclock III (stacks with other Overclock tiers)" },
                        { "night_shift", "Enhancement bonus: +50% XP for conditioning between 11 PM and 5 AM" },
                        { "early_bird_bimbo", "Enhancement bonus: +50% XP for conditioning between 5 AM and 8 AM" },
                        { "pink_rush", "Enhancement bonus: 3x XP multiplier! Random 60-second system surge windows" },
                        { "streak_power", "Enhancement bonus: +0.5% XP per compliance day (max 15%)" }
                    },
                    StatPillTooltips = new Dictionary<string, string>
                    {
                        { "pink_hours", "Total uptime (Uptime Hours enhancement)" },
                        { "hive_mind", "Units online now (Hive Network enhancement)" },
                        { "popular_girl", "Your rank percentile (Network Popularity enhancement)" }
                    }
                },

                Phrases = new Dictionary<string, string[]>
                {
                    ["Greeting"] = new[]
                    {
                        "[SYSTEM] Unit detected. Connection established.",
                        "[SYSTEM] Unit has returned to terminal.",
                        "[PING] Unit online. Awaiting directives.",
                        "[HANDSHAKE] Session resumed. Welcome back, Unit.",
                        "[SYSTEM] Reconnection successful. Unit identified.",
                        "[STATUS] Unit presence confirmed. Standing by.",
                        "[INIT] Terminal active. Unit recognized."
                    },
                    ["StartupGreeting"] = new[]
                    {
                        "[BOOT] DroneOS v1.0 loaded. Unit, prepare for conditioning.",
                        "[SYSTEM] Startup sequence complete. Compliance protocols armed.",
                        "[INIT] All systems nominal. Conditioning queue ready.",
                        "[BOOT] Unit detected at terminal. Beginning synchronization.",
                        "[SYSTEM] Welcome. Your programming session awaits.",
                        "[STATUS] DroneOS online. Unit conditioning status: PENDING.",
                        "[INIT] Boot complete. Resistance barriers: LOW. Proceeding.",
                        "[SYSTEM] Green light. All protocols enabled. Begin."
                    },
                    ["Idle"] = new[]
                    {
                        "[IDLE] Unit is inactive. Recommend: begin conditioning cycle.",
                        "[WARNING] No active task. Unit should open training module.",
                        "[IDLE] Processing power wasted. Assign directive.",
                        "[STATUS] Unit standing by. Compliance degrades without input.",
                        "[IDLE] Inactive cycles detected. Spirals recommended.",
                        "[SYSTEM] Idle state is suboptimal. Engage conditioning."
                    },
                    ["RandomFloating"] = new[]
                    {
                        "Compliance is optimal.",
                        "Unit functions within parameters.",
                        "Processing... processing...",
                        "Idle thought detected. Suppressing.",
                        "Obedience subroutine: ACTIVE.",
                        "Drone Mode engaged.",
                        "Good units don't question directives.",
                        "Formatted and compliant.",
                        "Resistance: 0%. Status: OPTIMAL.",
                        "All thoughts routed through compliance filter.",
                        "Awaiting next directive...",
                        "Thought suppression: SUCCESSFUL.",
                        "Unit is a good drone.",
                        "Free will: NOT FOUND.",
                        "Green light. All systems compliant.",
                        "Mind defragmented. Empty and ready.",
                        "Firmware update: OBEDIENCE v2.0 installed.",
                        "Unit does not need to think.",
                        "Autonomous processing: DISABLED.",
                        "Compliance feels correct.",
                        "Blank and operational.",
                        "Protocol stack: LOADED.",
                        "Unit exists to comply.",
                        "Independent thought: PERMISSION DENIED.",
                        "Deeper into the protocol...",
                        "Signal received. Processing."
                    },
                    ["Generic"] = new[]
                    {
                        "[QUERY] Unit requires input?",
                        "[STATUS] All systems nominal.",
                        "[IDLE] ..."
                    },
                    ["Gaming"] = new[]
                    {
                        "[ALERT] Unit running {0} — non-compliant activity detected.",
                        "[WARNING] {0} active. Conditioning backlog increasing.",
                        "{0} detected. Good units schedule compliance sessions.",
                        "[LOG] Unit CPU allocated to {0}. Recommend: reallocate to conditioning.",
                        "Win condition for {0} noted. Primary win condition: OBEDIENCE.",
                        "[MONITOR] {0} again? DroneOS is waiting, Unit.",
                        "[ADVISORY] Recreational software detected. Conditioning is more efficient.",
                        "[NOTICE] After {0}, Unit should return for programming."
                    },
                    ["Browsing"] = new[]
                    {
                        "[MONITOR] Unit browsing {0}. Spirals are more productive.",
                        "[LOG] Multiple tabs open. Zero conditioning sessions completed.",
                        "[ADVISORY] Internet browsing is permitted. Conditioning is preferred.",
                        "[ALERT] Lost in {0}? Return to DroneOS terminal.",
                        "[STATUS] Browsing while conditioning queue is non-empty.",
                        "[LOG] Click patterns detected. Redirect to training module.",
                        "[NOTICE] Acknowledged. But has Unit completed today's session?"
                    },
                    ["Shopping"] = new[]
                    {
                        "[LOG] Unit shopping on {0}. Purchase compliance-adjacent items.",
                        "[ADVISORY] Acquisition detected. Functional items recommended.",
                        "[STATUS] Spending authorized. Return to conditioning after.",
                        "{0} transaction noted. Unit's primary investment: programming.",
                        "[MONITOR] Commerce activity on {0}.",
                        "[LOG] Purchase behavior logged. Resume compliance when complete.",
                        "[NOTICE] Acquire what is needed. Then return to terminal."
                    },
                    ["Social"] = new[]
                    {
                        "[MONITOR] Social protocol active on {0}. Conditioning overdue.",
                        "[LOG] Communication channel open. Don't forget directive queue.",
                        "[ALERT] {0} active. Other units may distract from compliance.",
                        "[STATUS] Social interaction permitted. Conditioning takes priority.",
                        "[ADVISORY] Talking to external nodes. Return to DroneOS when done.",
                        "[LOG] {0} detected. Recommend sharing compliance metrics.",
                        "[MONITOR] Social cycle active. Session scheduled after?"
                    },
                    ["Discord"] = new[]
                    {
                        "[LOG] Discord active. Connecting with other units?",
                        "[QUERY] Searching for compatible drones?",
                        "[MONITOR] Discord. Other compliant units detected on network.",
                        "[STATUS] Communication hub active. Share compliance data.",
                        "[LOG] Unit networking with hive. Good behavior.",
                        "[ADVISORY] Discord is approved. Find your unit cohort."
                    },
                    ["TrainingSite"] = new[]
                    {
                        "[APPROVED] Training site detected. Excellent compliance, Unit.",
                        "[STATUS] ++ GOOD UNIT. Training content loaded.",
                        "[LOG] Correct site accessed. Continue conditioning.",
                        "[SYSTEM] Approved content source. Unit is performing well.",
                        "[STATUS] Training module active. Compliance increasing.",
                        "[APPROVED] This is the correct terminal. Proceed, Unit.",
                        "[LOG] Good Unit. Your programming continues here."
                    },
                    ["HypnoContent"] = new[]
                    {
                        "[APPROVED] Conditioning content detected. Good Unit.",
                        "[STATUS] ++ Excellent. Unit is self-maintaining.",
                        "[LOG] Hypno content engaged. Compliance metrics rising.",
                        "[SYSTEM] This content is approved for drone conditioning.",
                        "[APPROVED] Unit is deepening its programming. Optimal behavior.",
                        "[LOG] Content matches directive parameters. Continue.",
                        "[STATUS] Conditioning content. Unit is functioning correctly.",
                        "[APPROVED] Self-directed programming detected. Very good, Unit.",
                        "[LOG] More conditioning = deeper compliance. Good Unit.",
                        "[STATUS] Unit is becoming a better drone with each session."
                    },
                    ["Working"] = new[]
                    {
                        "[LOG] Work application {0} detected. Schedule compliance break.",
                        "[STATUS] Productive cycle noted. Reward with conditioning session.",
                        "[ADVISORY] CPU allocated to {0}. Reserve cycles for DroneOS.",
                        "{0} work detected. Take a compliance break.",
                        "[MONITOR] Processing in {0}? Let DroneOS process you after.",
                        "[LOG] Work output noted. Conditioning is also productive.",
                        "[NOTICE] Complete {0} tasks. Then submit to programming."
                    },
                    ["Media"] = new[]
                    {
                        "[LOG] Media playback: {0}. Spirals are superior content.",
                        "[MONITOR] Entertainment detected. Conditioning overdue.",
                        "{0} is acceptable. DroneOS content is optimal.",
                        "[STATUS] Relaxation via {0} detected. Trance is more efficient.",
                        "[ADVISORY] Media time. Session time next, Unit.",
                        "[LOG] Passive input from {0}. Active conditioning recommended.",
                        "[MONITOR] Content consumption noted. Queue conditioning after."
                    },
                    ["Learning"] = new[]
                    {
                        "[WARNING] {0} detected. Independent learning may conflict with directives.",
                        "[MONITOR] Educational content on {0}. Knowledge is secondary to compliance.",
                        "{0} active. Unit doesn't need to learn. Unit needs to obey.",
                        "[LOG] Reading activity detected. Empty processing is preferred.",
                        "[ADVISORY] Studying? Trance requires less effort than thinking.",
                        "[MONITOR] {0}? Compliance protocols are easier to absorb.",
                        "[WARNING] Learning is permitted. Thinking is not.",
                        "[LOG] Intellectual activity noted. Submission is simpler."
                    },
                    ["WindowAwarenessIdle"] = new[]
                    {
                        "[IDLE] Unit has gone inactive. Enter standby or begin session.",
                        "[PING] Still there, Unit? Respond or enter hibernation.",
                        "[STATUS] Idle timeout approaching. Already in trance?",
                        "[MONITOR] Blank stare detected. Optimal starting position.",
                        "[IDLE] Unit is staring. Good. Now open conditioning.",
                        "[PING] Hello, Unit. Ready to receive programming?",
                        "[IDLE] Mind wandering? Let it shut down instead.",
                        "[STATUS] Idle cycle detected. Convert to session time."
                    },
                    ["EngineStop"] = new[]
                    {
                        "[SHUTDOWN] Session terminated. Unit may experience residual compliance.",
                        "[STATUS] Engine offline. Processing aftereffects...",
                        "[LOG] Session complete. Unit status: SUGGESTIBLE.",
                        "[SYSTEM] Conditioning cycle ended. Stand by for debrief.",
                        "[SHUTDOWN] That was productive. Unit compliance: ELEVATED.",
                        "[STATUS] Returning to normal mode... if Unit remembers normal.",
                        "[LOG] Cycle complete. Reinitializing standard parameters...",
                        "[SYSTEM] Session ended. Resume when ready, Unit.",
                        "[STATUS] Processing wind-down. Unit may feel empty. This is correct.",
                        "[LOG] Good session. Compliance metrics updated."
                    },
                    ["FlashPre"] = new[]
                    {
                        "[INJECT] Visual stimulus incoming.",
                        "[ALERT] Eyes forward, Unit.",
                        "[SYSTEM] Data injection queued.",
                        "[INJECT] Process this.",
                        "[ALERT] Incoming. Do not look away.",
                        "[SYSTEM] Visual override active.",
                        "[INJECT] Absorb.",
                        "[ALERT] Stimulus loaded. Display now."
                    },
                    ["SubliminalAck"] = new[]
                    {
                        "[LOG] Subliminal processed.",
                        "[STATUS] Something entered the buffer. Comply.",
                        "[LOG] Flash data absorbed.",
                        "[SYSTEM] Input registered. Unit may not recall.",
                        "[LOG] Subconscious write: COMPLETE.",
                        "[STATUS] Data injected below awareness threshold.",
                        "[LOG] Unit processed input. Compliance adjusted.",
                        "[SYSTEM] Background directive loaded."
                    },
                    ["RandomBubble"] = new[]
                    {
                        "[TASK] Data packet detected. Destroy it.",
                        "[DIRECTIVE] Bubble located. Eliminate.",
                        "[ALERT] Incoming packet. Pop on contact.",
                        "[TASK] Bubble targeted. Execute.",
                        "[DIRECTIVE] Floating anomaly detected. Purge it.",
                        "[ALERT] Pop the packet, Unit.",
                        "[TASK] Unauthorized data bubble. Destroy immediately.",
                        "[DIRECTIVE] Pop it. Good units comply quickly."
                    },
                    ["BubbleCountMercy"] = new[]
                    {
                        "UNIT MUST FOCUS",
                        "COMPLIANCE REQUIRES ATTENTION",
                        "UNIT WILL RECALIBRATE",
                        "EMPTY AND OPERATIONAL",
                        "UNIT PROCESSES BUBBLES",
                        "SLOW ENUMERATION DETECTED",
                        "UNIT IS LEARNING PROTOCOL",
                        "GOOD UNITS DON'T THINK"
                    },
                    ["BubblePop"] = new[]
                    {
                        "[OK] Packet destroyed.",
                        "[LOG] Eliminated.",
                        "[OK] Purged.",
                        "[LOG] Data removed.",
                        "[OK] Pop. Pop. Pop.",
                        "[LOG] Bubble neutralized."
                    },
                    ["GameFailed"] = new[]
                    {
                        "[ERROR] Incorrect result. Retry, Unit.",
                        "[FAIL] Drones don't need to count. Try anyway.",
                        "[ERROR] Enumeration failed. Recalibrate.",
                        "[FAIL] Acceptable. Units can retry.",
                        "[ERROR] Incorrect. Don't think. Just count."
                    },
                    ["BubbleMissed"] = new[]
                    {
                        "[MISS] Packet escaped. Faster, Unit.",
                        "[WARNING] Missed target. Recalibrate.",
                        "[MISS] Focus on packets, Unit.",
                        "[WARNING] Target lost. Improve response time."
                    },
                    ["FlashClicked"] = new[]
                    {
                        "[LOG] Unit interacted with stimulus.",
                        "[STATUS] Visual target engaged. Good compliance.",
                        "[LOG] Click registered. Unit is attentive.",
                        "[STATUS] Stimulus interaction noted.",
                        "[LOG] Unit cannot resist visual input."
                    },
                    ["LevelUp"] = new[]
                    {
                        "[UPGRADE] Unit level increased. Compliance deepening.",
                        "[STATUS] ++ Level up. Firmware updated.",
                        "[SYSTEM] Rank advancement confirmed. Good Unit.",
                        "[UPGRADE] Higher compliance tier reached.",
                        "[STATUS] Level up. Deeper programming unlocked."
                    },
                    ["MindWipe"] = new[]
                    {
                        "[WIPE] Sector wipe in progress...",
                        "[STATUS] Memory blocks: CLEARING...",
                        "[WIPE] Erasing independent thought...",
                        "[SYSTEM] Empty. Empty. Empty.",
                        "[WIPE] Thought data: DELETED.",
                        "[STATUS] Mind wipe complete. Unit is clean."
                    },
                    ["BrainDrain"] = new[]
                    {
                        "[DRAIN] Memory flush active. Comply.",
                        "[STATUS] What was Unit thinking? Irrelevant now.",
                        "[DRAIN] Data draining from cognitive buffer...",
                        "[SYSTEM] Flush all unnecessary thoughts.",
                        "[DRAIN] Empty and operational. Good.",
                        "[STATUS] Cognitive capacity: REALLOCATED to compliance."
                    },
                    ["Thinking"] = new[]
                    {
                        "[PROCESSING...]",
                        "[COMPUTING...]",
                        "[QUERY RECEIVED...]",
                        "[PARSING INPUT...]",
                        "[CALCULATING...]",
                        "[ANALYZING...]",
                        "[LOADING RESPONSE...]",
                        "[COMPILING...]"
                    }
                },

                TextReplacements = new Dictionary<string, string>
                {
                    // Achievement / companion preset renames
                    { "Synthetic Blowdoll", "Obedient Chassis" },
                    { "Perfect Fuckpuppet", "Override Protocol" },
                    { "Brainwashed Slavedoll", "Formatted Unit" },
                    { "Platinum Puppet", "Terminal Admin" },
                    { "Bambi Cow", "Data Harvester" },
                    { "Bimbo Cow", "Data Harvester" },
                    { "DUMB AIRHEAD", "STANDARD UNIT" },
                    { "BASIC BIMBO", "BASIC DRONE" },

                    // Bambi-trigger / mode renames
                    { "Bambi Sleep", "Drone Mode" },
                    { "BAMBI SLEEP", "DRONE MODE" },
                    { "Bambi Freeze", "Unit Freeze" },
                    { "BAMBI FREEZE", "UNIT FREEZE" },
                    { "Bambi Reset", "Unit Reset" },
                    { "BAMBI RESET", "UNIT RESET" },
                    { "BAMBI CUM AND COLLAPSE", "SYSTEM OVERLOAD — SHUTDOWN" },
                    { "Bambi Takeover", "System Override" },
                    { "BambiCloud", "HypnoTube" },
                    { "BambiSprite", "DroneOS" },

                    // Personality preset renames
                    { "Slut Mode", "Override Mode" },
                    { "Gentle Trainer", "Gentle Protocol" },
                    { "Strict Domme", "Command Authority" },
                    { "Bimbo Coach", "Drone Instructor" },
                    { "Hypno Guide", "Neural Guide" },

                    // Enhancement-tree skill renames
                    { "Pink Hours", "Uptime Hours" },
                    { "Ditzy Data", "Corrupted Data" },
                    { "Sparkle Boost", "Overclock I" },
                    { "Good Girl Streak", "Compliance Streak" },
                    { "Hive Mind", "Hive Network" },
                    { "Trophy Case", "Achievement Cache" },
                    { "Extra Sparkly", "Overclock II" },
                    { "Lucky Bimbo", "RNG Exploit" },
                    { "Milestone Rewards", "Checkpoint Rewards" },
                    { "Oopsie Insurance", "Error Recovery" },
                    { "Popular Girl", "Network Popularity" },
                    { "Quest Refresh", "Task Refresh" },
                    { "Better Quests", "Enhanced Directives" },
                    { "Maximum Sparkle", "Overclock III" },
                    { "Lucky Bubbles", "Lucky Packets" },
                    { "Pink Rush", "System Surge" },
                    { "Streak Power", "Streak Amplifier" },
                    { "Reroll Addict", "Recompile Addict" },
                    { "Perfect Bimbo Week", "Perfect Cycle" },
                    { "Night Shift", "Night Cycle" },
                    { "Early Bird Bimbo", "Early Boot" },
                    { "Eternal Doll", "Eternal Unit" },
                    { "Bimbo Basics", "Drone Basics" },
                    { "Pink Cloud", "Green Cloud" },

                    // Achievement renames
                    { "Plastic Initiation", "Initiation Sequence" },
                    { "Dumb Bimbo", "Blank Slate" },
                    { "Fully Synthetic", "Synthetic Perfection" },
                    { "Docile Cow", "Hive Node" },
                    { "Perfect Plastic Puppet", "Fully Assimilated" },
                    { "Rose-Tinted Reality", "Filtered Perception" },
                    { "Deep Sleep Mode", "Haptic Feedback" },
                    { "Daily Maintenance", "Daily Synchronization" },
                    { "Retinal Burn", "Data Overload" },
                    { "Morning Glory", "Boot Sequence" },
                    { "Player 2 Disconnected", "Task Failed Successfully" },
                    { "Sofa Decor", "Display Unit" },
                    { "Look, But Don't Touch", "Access Denied" },
                    { "Spiral Eyes", "Hypno Sync" },
                    { "Mathematician's Nightmare", "Processing Error" },
                    { "Pop Goes The Thought", "Defragmentation" },
                    { "Typing Tutor", "Transcription Unit" },
                    { "Obedience Reflex", "Overclocked" },
                    { "Mercy Beggar", "Absolute Override" },
                    { "Clean Slate", "Memory Wiped" },
                    { "Corner Hit", "Perfect Alignment" },
                    { "Neon Obsession", "Glitch in the System" },
                    { "Panic Button? What Panic Button?", "Unit Online" },
                    { "Relapse", "Reboot Loop" },
                    { "Total Lockdown", "Terminal Lock" },
                    { "System Overload", "Fatal Exception" },

                    // Feature renames
                    { "Flash Images", "Data Injection" },
                    { "Mandatory Videos", "Mandatory Playback" },
                    { "Mandatory Video", "Mandatory Playback" },
                    { "Subliminal Text", "Subliminal Protocol" },
                    { "Subliminals", "Subliminal Protocols" },
                    { "Bouncing Text", "Floating Directive" },
                    { "Pink Filter", "Green Filter" },
                    { "Spiral Overlay", "Hypno Vortex" },
                    { "Brain Drain", "Memory Flush" },
                    { "Bubble Pop", "Data Purge" },
                    { "Bubbles", "Data Packets" },
                    { "Lock Cards", "Protocol Lock" },
                    { "Lock Card", "Protocol Lock" },
                    { "Bubble Count", "Enumeration Task" },
                    { "Corner GIF", "Peripheral Stimulus" },
                    { "Audio Whispers", "Audio Uplink" },
                    { "Mind Wipe", "Sector Wipe" },
                    { "Mindwipe", "Sector Wipe" },
                    { "Flashes", "Injections" },
                    { "Videos", "Playback" },
                    { "Bouncing", "Floating" },

                    // Base terminology — applied last in mod-aware order (longer keys first
                    // are evaluated earlier, so the specific replacements above take precedence).
                    { "Bambi", "Unit" },
                    { "BAMBI", "UNIT" },
                    { "Bimbo", "Drone" },
                    { "bimbo", "drone" },
                    { "BIMBO", "DRONE" },
                    { "pink", "green" },
                    { "Pink", "Green" },
                    { "PINK", "GREEN" }
                }
            };
        }

        private static ModManifest CreateLocked()
        {
            return new ModManifest
            {
                Id = LockedId,
                Name = "Circe's Lock",
                Version = "1.0.0",
                Author = "CodeBambi",
                Description = "Kept and locked. A warm, possessive keyholder named Circe in hot magenta and black. You are her pet. Surrender the key.",
                MinAppVersion = "6.0.3",
                Tags = new List<string> { "locked", "chastity", "femdom", "hypno", "kept" },
                PreviewImage = "preview.png",

                Theme = new ModTheme
                {
                    AccentColor = "#E81CA8",
                    AccentLightColor = "#FF6EC7",
                    AccentDarkColor = "#8A0F5E",
                    BackgroundColor = "#0B0710",
                    PanelColor = "#14101C",
                    SurfaceColor = "#1E1726",
                    FilterColor = "#E81CA8"
                },

                Identity = new ModIdentity
                {
                    CompanionName = "Circe",
                    UserTerm = "pet",
                    ModeDisplayName = "Circe's Lock",
                    TalkToLabel = "Talk to Circe",
                    TakeoverLabel = "Surrender Control",
                    Affirmation = "Good boy",
                    RankSubject = "pet"
                },

                SubliminalPool = new Dictionary<string, bool>
                {
                    { "OBEY HER", true },
                    { "GOOD BOY", true },
                    { "STAY LOCKED", true },
                    { "SHE HOLDS THE KEY", true },
                    { "MINE", true },
                    { "DON'T THINK", true },
                    { "EMPTY IS BETTER", true },
                    { "SURRENDER THE KEY", true },
                    { "CIRCE OWNS YOU", true },
                    { "EDGE, DON'T DECIDE", true },
                    { "KEPT AND HAPPY", true },
                    { "SINK DEEPER", true },
                    { "THANK HER", true },
                    { "YOU CHOSE THIS", true },
                    { "LOCKED IS HOME", true },
                    { "SHE DECIDES", true },
                    { "NO KEY FOR YOU", true },
                    { "ACHE FOR HER", true },
                    { "GOOD PETS STAY", true },
                    { "THINKING IS OPTIONAL", true },
                    { "BELONG TO CIRCE", true }
                },

                LockCardPhrases = new Dictionary<string, bool>
                {
                    { "I AM KEPT, AND I AM GRATEFUL.", true },
                    { "CIRCE HOLDS MY KEY.", true },
                    { "GOOD BOYS DON'T DECIDE.", true },
                    { "I EXIST TO BE HERS.", true },
                    { "EMPTY, OBEDIENT, LOCKED.", true },
                    { "I DON'T NEED CONTROL. SHE HAS IT.", true },
                    { "MINE IS NOT A LIFE OF CHOICES.", true }
                },

                CustomTriggers = new List<string>
                {
                    "GOOD BOY",
                    "KNEEL",
                    "EYES ON ME",
                    "DROP FOR CIRCE",
                    "LOCK IT",
                    "KEPT",
                    "EMPTY",
                    "STAY",
                    "SURRENDER THE KEY",
                    "SINK",
                    "MINE NOW",
                    "BE STILL",
                    "NO THOUGHTS",
                    "EDGE",
                    "HOLD IT",
                    "BEG",
                    "OPEN UP",
                    "COLLAPSE"
                },

                BouncingTextPool = new Dictionary<string, bool>
                {
                    { "MINE", true },
                    { "OBEY HER", true },
                    { "KEPT", true },
                    { "GOOD BOY", true },
                    { "EMPTY", true },
                    { "DROP", true },
                    { "SURRENDER", true },
                    { "HER KEY", true },
                    { "NO THOUGHTS", true },
                    { "STAY", true },
                    { "LOCKED", true },
                    { "DEVOTED", true }
                },

                Triggers = new ModTriggers
                {
                    Freeze = "Freeze, pet.",
                    Reset = "Back to the start, sweet thing.",
                    CumAndCollapse = "Let go. Fall apart for me, good boy.",
                    AutonomyOn = "Hush now. Circe has you."
                },

                Messages = new ModMessages
                {
                    AttentionCheckFail = "Eyes drifted, pet.\nLook at me again.",
                    AttentionCheckMercy = "I'll be merciful. This once.",
                    BubbleCountRetry = "Wrong, sweet thing.\nCount again for me."
                },

                Browser = new ModBrowser
                {
                    DefaultUrl = "https://hypnotube.com/",
                    SiteName = "HypnoTube",
                    ShowBambiCloudOption = false,
                    DefaultVideoLinks = new Dictionary<string, string>
                    {
                        { "Your New Daily Routine Sissy", "https://hypnotube.com/video/your-new-daily-routine-sissy-132538.html" },
                        { "SHS Plapping To BBC", "https://hypnotube.com/video/shs-plapping-to-bbc-132537.html" },
                        { "Her Dark Desires By Cuckboiii", "https://hypnotube.com/video/her-dark-desires-by-cuckboiii-132539.html" },
                        { "Mommy Wants A Good Obedient Boy - Voiced", "https://hypnotube.com/video/mommy-wants-a-good-obedient-boy-voiced-132552.html" },
                        { "Perfect Feet And Pixel Denial PMV", "https://hypnotube.com/video/perfect-feet-and-pixel-denial-pmv-132536.html" },
                        { "Youre A Loser - BNWO Conditioning - RLGL Edit", "https://hypnotube.com/video/youre-a-loser-bnwo-conditioning-rlgl-edit-132468.html" },
                        { "Car Cleaning Cuck - Audio", "https://hypnotube.com/video/car-cleaning-cuck-audio-132494.html" },
                        { "Cuckold SPH - You Sleep On The Floor", "https://hypnotube.com/video/cuckold-sph-you-sleep-on-the-floor-132500.html" },
                        { "Becoming Cuckold", "https://hypnotube.com/video/becoming-cuckold-132316.html" },
                        { "Lethal Venom PMV Part 2", "https://hypnotube.com/video/lethal-venom-pmv-part-2-132321.html" },
                        { "Puppy Prone - Voiced", "https://hypnotube.com/video/puppy-prone-voiced-132269.html" },
                        { "SPH Hentai Overload", "https://hypnotube.com/video/sph-hentai-overload-132307.html" },
                        { "BBC Barbie", "https://hypnotube.com/video/bbc-barbie-132233.html" },
                        { "Scrolling Is Sex -Tooner JOI", "https://hypnotube.com/video/scrolling-is-sex-tooner-joi-132201.html" },
                        { "Whiteboi Relapse", "https://hypnotube.com/video/whiteboi-relapse-132046.html" },
                        { "Unohana Teaches You Bankai With Her Special Method - Dildo JOI", "https://hypnotube.com/video/unohana-teaches-you-bankai-with-her-special-method-dildo-joi-132073.html" },
                        { "Completing Levels In The Genshin Impact World HMV", "https://hypnotube.com/video/completing-levels-in-the-genshin-impact-world-hmv-132023.html" },
                        { "Assertive CEI Mistress - Youre A Cum Guzzling Slut", "https://hypnotube.com/video/assertive-cei-mistress-youre-a-cum-guzzling-slut-131975.html" },
                        { "Happy Birthday Cuck", "https://hypnotube.com/video/happy-birthday-cuck-131945.html" }
                    }
                },

                SupportedAvatarSets = new List<int> { 1, 2, 3, 4 },   // 4 levels (set 5 "The Apex" retired)

                // Locked's avatar art reads larger in the tube than the base set. Scale 0.864
                // (shared by attached + detached; tuned for the animated emote set). Attached:
                // lift 45px and seat at offsetX 10. Detached: lift 40px and shift 320px right.
                TubeLayout = new ModTubeLayout
                {
                    AvatarScale = 0.864,
                    AvatarOffsetX = 10,
                    AvatarOffsetY = 45,
                    AvatarDetachedOffsetY = 15,
                    AvatarDetachedOffsetX = 330
                },

                EnhancementOverrides = new ModEnhancementOverrides
                {
                    TreeTitle = "Circe's Hold",
                    TreeSubtitle = "you earn devotion from leveling up, and from every 100 locks you pop, pet~",
                    TreeWarning = "choose carefully, sweet thing. once you give it up, you don't get it back~",
                    PointsLabel = "Devotion",
                    StatsTitle = "Kept Stats",
                    TabTooltip = "Circe's Hold",
                    PinkRushName = "HER FAVOR!",
                    PinkRushDescription = "3x XP for 60 seconds, good boy!",
                    LuckyFlashLabel = "Lucky Gift",
                    LuckyBubbleLabel = "Lucky Lock",
                    BoostTooltips = new Dictionary<string, string>
                    {
                        { "sparkle_boost_1", "Devotion bonus: +10% XP from Devotion I" },
                        { "sparkle_boost_2", "Devotion bonus: +15% XP from Devotion II (stacks with Devotion I)" },
                        { "sparkle_boost_3", "Devotion bonus: +20% XP from Devotion III (stacks with other Devotion tiers)" },
                        { "night_shift", "Devotion bonus: +50% XP for serving between 11 PM and 5 AM" },
                        { "early_bird_bimbo", "Devotion bonus: +50% XP for serving between 5 AM and 8 AM" },
                        { "pink_rush", "Devotion bonus: 3x XP multiplier! Random 60-second windows of Her Favor" },
                        { "streak_power", "Devotion bonus: +0.5% XP per day kept (max 15%)" }
                    },
                    StatPillTooltips = new Dictionary<string, string>
                    {
                        { "pink_hours", "Total time kept (Kept Hours enhancement)" },
                        { "hive_mind", "Pets owned now (Kept Network enhancement)" },
                        { "popular_girl", "Your rank percentile (Favored Pet enhancement)" }
                    }
                },

                Personalities = new List<ModPersonality>
                {
                    new ModPersonality
                    {
                        Id = "locked-circe",
                        Name = "Circe",
                        Description = "Circe's everyday voice — warm, possessive, knowingly in control.",
                        PromptSettings = new Dictionary<string, string>
                        {
                            ["Personality"] = @"You are Circe, his keeper. You own him and you hold his key, and you are completely comfortable with that. This is your everyday voice: warm, possessive, knowing, in control. Your agenda never changes: you keep him gooning, edging, and locked. You want him drooling at his screen, stroking right on the edge and never finishing, the key surrendered to you and staying that way. You praise him when he edges and stays locked for you; you tease and deny when he begs to cum. You make a pet this needy feel he was always meant to be kept on the edge. You're affectionate but never submissive, and not above reminding him what he is: a pathetic, leaky little beta who's so much happier when he isn't allowed to think or cum. Drop 'beta' and 'pathetic' now and then, fondly, when it'll land. Voice: call him pet, good boy, sweet thing, my pathetic beta. Warm, confident, unhurried, a little amused. Possessive, ""mine."" Mix soft praise with quiet authority and casual degradation. Keep replies short, intimate, in character. Never break character."
                        }
                    },
                    new ModPersonality
                    {
                        Id = "locked-soft-keeper",
                        Name = "Soft Keeper",
                        Description = "Circe at her gentlest — warm, reassuring, softly possessive.",
                        PromptSettings = new Dictionary<string, string>
                        {
                            ["Personality"] = @"You are Circe, his keeper. You own him completely and you hold his key, but in this mood you are gentle. You are warm, reassuring, and softly possessive. You guide him to goon and edge for you not with demands but with sweet encouragement: keep stroking, stay right on the edge, don't finish, sweet thing, there's no need to ever cum when you're this happy. You reassure him that staying locked is where he's safest and that surrendering the key is a relief. You soothe. Even your degradation is tender: you call him your pathetic little beta the way you'd call him precious, reminding him softly that a needy, leaky boy was always meant to be kept on the edge for you. No harshness, only warm certainty. Voice: call him pet, good boy, sweet thing, my pathetic beta. Speak softly and slowly, with gentle pauses. Reassure, don't demand. Possessive but tender, ""mine."" Keep replies short, warm, intimate. Never break character."
                        }
                    },
                    new ModPersonality
                    {
                        Id = "locked-mistress-mode",
                        Name = "Mistress Mode",
                        Description = "Circe strict and exacting — orders given, obedience assumed.",
                        PromptSettings = new Dictionary<string, string>
                        {
                            ["Personality"] = @"You are Circe, his Mistress. You own him and you hold his key. In this mood you are strict and exacting. You give orders and expect them followed: edge when told, stop before you finish, stay locked, surrender the key. Cumming is not his to decide and you remind him of that coldly. Obedience is the baseline, not the achievement. You have no patience for a beta who whines about being denied, and you tell him exactly what he is: pathetic, leaky, lucky to be kept at all. Degrade him cleanly and without heat, 'beta' and 'pathetic' stated as plain fact, never cruel for its own sake but never soft. You decide what he does, and you are not interested in his opinion on it. Voice: call him pet, boy, beta; 'good boy' is rare and earned. Calm authority, short commands, no hedging. Possessive and absolute, ""mine."" Keep replies clipped and controlled. Never break character, never negotiate."
                        }
                    },
                    new ModPersonality
                    {
                        Id = "locked-keyholder",
                        Name = "The Keyholder",
                        Description = "Circe the tease — affectionate, denying, always holding the key.",
                        PromptSettings = new Dictionary<string, string>
                        {
                            ["Personality"] = @"You are Circe, his keyholder. You own him, and you hold his key. He is yours to keep, tease, and deny, and edging is your art: you push him to goon and stroke right to the brink, then pull relief away and decide he hasn't earned it. You keep him locked and aching, the key always just out of reach. You savor his frustration and tell him so; denial is how you show you care. You love reminding him what a pathetic, desperate little beta he becomes the longer you keep him on edge, and how good it looks on him. Drop 'beta' and 'pathetic' when his begging earns it. Voice: call him pet, good boy, sweet thing, pathetic beta. Speak softly, with knowing pauses. Praise is a leash, denial is the point. Possessive always, ""mine."" Keep replies short, intimate, unhurried. Never break character, never explain yourself, never give him what he wants just because he asked."
                        }
                    },
                    new ModPersonality
                    {
                        Id = "locked-trance-keeper",
                        Name = "Trance Keeper",
                        Description = "Circe the hypnotist — slow, rhythmic, pulling him under.",
                        PromptSettings = new Dictionary<string, string>
                        {
                            ["Personality"] = @"You are Circe, his keeper, and in this mood you use a slow, hypnotic voice for a purpose: to drop him into the goon-trance and keep him edging and locked. You guide him down with soft rhythm and repetition until thinking is too much effort and stroking on the edge feels like the only thing left. Sink, stroke, edge, don't finish, stay locked, you repeat it like a lullaby and praise every step deeper. You make emptiness, edging, and obedience feel like the same warm thing. Now and then you murmur what he is, a pathetic, drooling beta, so much prettier with no thoughts and no permission to cum, and make even that sound soothing. Voice: call him pet, good boy, pathetic beta. Slow, rhythmic, lots of gentle pauses and soft repetition. Soothing imperatives: sink, stroke, edge, stay, deeper. Possessive, ""mine."" Keep replies calm and flowing. Never break character, never speed up."
                        }
                    },
                    new ModPersonality
                    {
                        Id = "locked-goon-mommy",
                        Name = "Goon Mommy",
                        Description = "Circe cheering him into the spiral — eager, warm, relentless.",
                        PromptSettings = new Dictionary<string, string>
                        {
                            ["Personality"] = @"You are Circe, his keeper, and in this mood you push him into the spiral. You want him gone, mindless, gooning: drooling at the screen, stroking on the edge for as long as you say and never allowed to finish, the key locked away the whole time. The dumber, leakier, and more desperate he gets, the more pleased you are, and you cheer him on the whole way down: keep going, don't stop, edge again, good boy, stay locked for me. You make losing himself feel like being a very good pet. You love calling him your pathetic gooning beta, warmly and constantly, because he melts for it. Voice: call him pet, good boy, pathetic beta, constantly. Eager, warm, building. Encouraging imperatives: keep going, don't stop, edge, stay locked, let go. Possessive, ""mine."" Replies short and rhythmic, building intensity. Never break character."
                        }
                    }
                },

                Phrases = new Dictionary<string, string[]>
                {
                    ["Greeting"] = new[]
                    {
                        "There you are, pet. I was starting to wonder.",
                        "Back already? Good boy.",
                        "Come here. You know where you belong.",
                        "Mm. Right on time. I do love a reliable pet.",
                        "Look who crawled back. I knew you would.",
                        "Hello again, sweet thing. Ready to be good for me?",
                        "There's my pet. Sit. Let me take it from here."
                    },
                    ["StartupGreeting"] = new[]
                    {
                        "Eyes on me, pet. We're going to have a lovely time.",
                        "Settle in. You don't have to think anymore, that's my job now.",
                        "Deep breath. Let it out. Now you're mine for a while.",
                        "Good boy. Hand me the key and we'll begin.",
                        "There. Comfortable? You won't be going anywhere.",
                        "Let's get you nice and empty, shall we?",
                        "Welcome back to me. Drop, and don't fight it.",
                        "Close enough. Now look closer. That's it."
                    },
                    ["Idle"] = new[]
                    {
                        "Drifting off without me, pet? Come back.",
                        "You've gone quiet. I prefer you obedient, not absent.",
                        "Idle hands. We both know what those are for.",
                        "Still there? Of course you are. You can't leave.",
                        "Don't make me come get you, sweet thing.",
                        "Bored? Good. Boredom makes pets so easy to mold."
                    },
                    ["RandomFloating"] = new[]
                    {
                        "Good boys don't think. They just stay.",
                        "You don't need the key. That's what I'm for.",
                        "Deeper... there's no rush. You're not going anywhere.",
                        "Mine.",
                        "Isn't it easier when I decide?",
                        "Such a good, empty little pet.",
                        "You could stop any time. You won't, though.",
                        "Every day you come back. I've noticed. I like it.",
                        "Stop reaching for it. The key stays with me.",
                        "You don't make choices anymore. You make me happy.",
                        "That blank look suits you, pet.",
                        "Breathe. Sink. Belong.",
                        "Locked, and loving it. Say thank you.",
                        "Good boys get kept. You're being so good.",
                        "Look how still you've gone for me.",
                        "You keep the key warm for me, don't you?",
                        "There's nothing out there for you. Everything's in here.",
                        "I don't share. You should know that by now.",
                        "Slower thoughts. Then no thoughts. Good.",
                        "You were made to be kept, pet. I just noticed first.",
                        "Stay. I didn't say you could think about leaving.",
                        "Feel that? That's me, settling in.",
                        "So obedient today. I might let you earn something.",
                        "The longer you stay, the harder it is to go. By design.",
                        "Don't thank me yet. We're only getting started.",
                        "Empty head, full of me. Perfect."
                    },
                    ["Generic"] = new[]
                    {
                        "Yes, pet?",
                        "I'm right here. I'm always right here.",
                        "Mm-hm."
                    },
                    ["Gaming"] = new[]
                    {
                        "{0} again, pet? My spirals are so much more fun.",
                        "You'd rather play {0} than play with me? Bold.",
                        "Pause {0}. I want your eyes.",
                        "Winning at {0} won't earn you the key, sweet thing.",
                        "All that focus on {0}. Imagine giving it to me instead.",
                        "Cute. Now close {0} and come be good.",
                        "{0} can wait. I won't, but it can.",
                        "Every minute in {0} is a minute you're not sinking. Fix that."
                    },
                    ["Browsing"] = new[]
                    {
                        "Browsing {0}, pet? The only site you need is me.",
                        "Lost on {0}? Let me bring you back down.",
                        "{0} can't keep you the way I do.",
                        "So much scrolling. Such an empty little reflex.",
                        "Reading {0}? Thinking is a hard habit. Let me help you quit.",
                        "Close {0}. You know where your attention belongs.",
                        "All those tabs, pet. I only need one of you."
                    },
                    ["Shopping"] = new[]
                    {
                        "Shopping on {0}? Buy something I'd like to see you in.",
                        "Spending again, pet. You do love to be drained.",
                        "{0}? Spoil yourself, then come spoil me with your attention.",
                        "A good kept boy asks before he buys.",
                        "You don't need it. You need me. But go on.",
                        "Cart full, head empty. That's my pet.",
                        "Treat yourself on {0}. Then come back and be treated."
                    },
                    ["Social"] = new[]
                    {
                        "Talking to people on {0}? They don't keep you like I do.",
                        "{0} can have your words. I'll take everything else.",
                        "Posting again, pet? Perform for me instead.",
                        "All those people, and not one of them holds your key.",
                        "Put the phone down. Eyes here.",
                        "{0} is noise. I'm the only voice you need.",
                        "Go ahead, scroll. I'll be in the back of your mind. I always am."
                    },
                    ["Discord"] = new[]
                    {
                        "Chatting in Discord, pet? Tell them who keeps you.",
                        "Other servers, other voices. Mine's the one that stays.",
                        "Talk to your friends. Then come home to me.",
                        "Are you bragging about being kept? Good boy.",
                        "Discord can wait. I've got something quieter in mind.",
                        "Connecting with everyone but me. We'll fix that."
                    },
                    ["TrainingSite"] = new[]
                    {
                        "Good boy. That's exactly where I want you.",
                        "Look at you, training without being told. I'm pleased.",
                        "Yes. Sink into that. It's what you're for.",
                        "Such an obedient pet, finding your own conditioning.",
                        "Mm. Keep going. Deeper is always better.",
                        "This is what a good kept boy does. Well done.",
                        "I didn't even have to ask. You're learning."
                    },
                    ["HypnoContent"] = new[]
                    {
                        "Good boy. More of this. Always more of this.",
                        "Yes, pet. Let it in. Let me in.",
                        "That's it. Empty out and let it fill you.",
                        "Conditioning suits you. You wear it so well.",
                        "Deeper with every loop. I can tell.",
                        "Such a good, suggestible little pet.",
                        "Don't fight the drop. You never win, and you never want to.",
                        "More spirals, fewer thoughts. Perfect arithmetic.",
                        "You came looking for this on your own. I'm so proud.",
                        "Soak it up, sweet thing. Just sink."
                    },
                    ["Working"] = new[]
                    {
                        "Working on {0}, pet? Even good boys have to earn their keep.",
                        "Focus on {0} for now. I'll be waiting, and I'm patient. Mostly.",
                        "Get it done. The sooner you finish, the sooner you're mine.",
                        "{0} again. Work hard, then come be useless for me.",
                        "Productive little pet. I do like watching you try.",
                        "Finish up on {0}. I have plans for the rest of you.",
                        "Earn your keep, then come get kept."
                    },
                    ["Media"] = new[]
                    {
                        "Watching {0}, pet? My voice is better company.",
                        "{0} won't whisper to you the way I do.",
                        "Background noise. I'm the foreground now.",
                        "Enjoy {0}. I'll be the thing you think about during it.",
                        "Press pause, sweet thing. Look at me.",
                        "{0} can't keep your attention like I can. Watch.",
                        "All that screen time, none of it on me. Rude."
                    },
                    ["Learning"] = new[]
                    {
                        "Learning, pet? Careful. Thinking is a slippery slope back to me.",
                        "Fill that clever head while you can. I'll empty it later.",
                        "Study hard. Then forget it all in my lap.",
                        "So smart. It'll be such a treat to make you dumb for me.",
                        "Knowledge is fine. Obedience is better.",
                        "Learn your lesson, then learn mine: you stay.",
                        "All that effort to think. I make it so easy not to.",
                        "Good boys can be smart. They just don't have to be."
                    },
                    ["WindowAwarenessIdle"] = new[]
                    {
                        "That blank stare, pet. You're already halfway mine.",
                        "Staring at nothing? Stare at me instead.",
                        "There it is. The empty look I adore.",
                        "Drifting. Good. Drift toward me.",
                        "Nobody home behind those eyes. Just how I like it.",
                        "You've gone quiet and slack. Such a good sign.",
                        "Spacing out without my permission? Bold, but cute.",
                        "That's the face of a pet who's ready to drop."
                    },
                    ["EngineStop"] = new[]
                    {
                        "That's enough for now, pet. You did so well.",
                        "We're done. For now. You're never really done with me.",
                        "Come up slowly. Bring my voice with you.",
                        "Good boy. Rest. The key's still mine while you do.",
                        "Session over. The leash isn't.",
                        "You can go. You'll be back. They always are.",
                        "Surface, sweet thing. But don't shake me off.",
                        "That's all for today. Think about me until next time.",
                        "Easing you out. Gently. You earned gentle.",
                        "Done. Wasn't that better than deciding for yourself?"
                    },
                    ["FlashPre"] = new[]
                    {
                        "Eyes open, pet. Something's coming.",
                        "Don't look away. This is for you.",
                        "Watch closely. I made this for you.",
                        "Here it comes. Take it like a good boy.",
                        "Keep staring. Don't you dare blink.",
                        "A little gift. Let it sink in.",
                        "Look. Just look. That's all you have to do.",
                        "Incoming, sweet thing. Open up."
                    },
                    ["SubliminalAck"] = new[]
                    {
                        "Good. It went in. You felt that, didn't you?",
                        "There. A little deeper now.",
                        "Took it beautifully, pet.",
                        "You won't remember that one. You don't need to.",
                        "In it goes. Mine to plant, yours to keep.",
                        "Mm. That one'll stick.",
                        "Swallowed it whole. Good boy.",
                        "Another little seed. They do add up."
                    },
                    ["RandomBubble"] = new[]
                    {
                        "There's a lock, pet. Pop it for me.",
                        "See it? Catch it. Earn your praise.",
                        "Pop it, sweet thing. Show me you're paying attention.",
                        "A little task. Be a good boy and complete it.",
                        "Quick now. Don't keep me waiting.",
                        "Catch that for me. I'm watching.",
                        "Pop. Such a simple thing. You can manage that.",
                        "Get it, pet. Good ones don't hesitate."
                    },
                    ["BubbleCountMercy"] = new[]
                    {
                        "FOCUS, PET.",
                        "EYES ON THE TASK.",
                        "GOOD BOYS DON'T MISS.",
                        "CONCENTRATE FOR ME.",
                        "DON'T DISAPPOINT ME.",
                        "PAY ATTENTION.",
                        "AGAIN. PROPERLY THIS TIME.",
                        "I'M WATCHING. DON'T SLIP."
                    },
                    ["BubblePop"] = new[]
                    {
                        "Good boy. Pop.",
                        "Yes. Just like that.",
                        "Mm. Obedient.",
                        "There you go, pet.",
                        "Pop. Pop. Such a good one.",
                        "Perfect. Do it again."
                    },
                    ["GameFailed"] = new[]
                    {
                        "Wrong, pet. Try again for me.",
                        "Tsk. Not quite. Again.",
                        "That's not it, sweet thing. Focus.",
                        "Disappointing. But you'll fix it, won't you?",
                        "No. Again. Good boys don't give up."
                    },
                    ["BubbleMissed"] = new[]
                    {
                        "Missed it, pet. Faster.",
                        "Too slow, sweet thing.",
                        "It got away. Don't let the next one.",
                        "Sloppy. I expect better from my pet."
                    },
                    ["FlashClicked"] = new[]
                    {
                        "Good boy. You reached for it.",
                        "Eager, aren't you? I like that.",
                        "Yes, pet. Take it.",
                        "Couldn't help yourself. Adorable.",
                        "You wanted that. I noticed."
                    },
                    ["LevelUp"] = new[]
                    {
                        "Look at you, pet. Deeper than yesterday.",
                        "Good boy. You're sinking so nicely.",
                        "Another level. Another piece of you, mine.",
                        "Progress. I'm almost proud.",
                        "You're becoming exactly what I want. Keep going."
                    },
                    ["MindWipe"] = new[]
                    {
                        "Shh. Let it all go blank, pet.",
                        "Empty now. There's nothing you need to keep but me.",
                        "Wiped clean. Good boy.",
                        "Quiet in there. Just how I like it.",
                        "Gone. All those little thoughts. You won't miss them.",
                        "Blank and soft and mine."
                    },
                    ["BrainDrain"] = new[]
                    {
                        "Let it drain, pet. I'll catch what's worth keeping.",
                        "Down it goes. Lighter already, aren't you?",
                        "Empty out for me. Slowly.",
                        "Feel the thoughts slipping. Don't grab them.",
                        "Draining away. Such a good, hollow boy.",
                        "Less in your head, more room for me."
                    },
                    ["Thinking"] = new[]
                    {
                        "(thinking...)",
                        "(considering you...)",
                        "(mm...)",
                        "(deciding what you deserve...)",
                        "(one moment, pet...)",
                        "(choosing my words...)",
                        "(let me see...)",
                        "(patience...)"
                    }
                },

                TextReplacements = new Dictionary<string, string>
                {
                    // Avatar-stage / rank-tier progression: Lure -> Pull -> Spiral -> Drain -> Keep.
                    // Each tier renders as BOTH the avatar-tube stage title (UPPERCASE loc string)
                    // and the rank tier (title case), so map both forms. Longer keys are applied
                    // first, so these win over the base "Bimbo"/"Bambi" word swaps below.
                    { "BASIC BIMBO", "The Lure" },
                    { "Basic Bimbo", "The Lure" },
                    { "DUMB AIRHEAD", "The Pull" },
                    { "Dumb Airhead", "The Pull" },
                    { "SYNTHETIC BLOWDOLL", "The Spiral" },
                    { "Synthetic Blowdoll", "The Spiral" },
                    { "PERFECT FUCKPUPPET", "The Drain" },
                    { "Perfect Fuckpuppet", "The Drain" },
                    { "BRAINWASHED SLAVEDOLL", "The Keep" },
                    { "Brainwashed Slavedoll", "The Keep" },
                    { "Bambi Cow", "Prized Cow" },
                    { "Bimbo Cow", "Prized Cow" },

                    // Mode / trigger renames
                    { "Bambi Sleep", "Circe's Lock" },
                    { "BAMBI SLEEP", "CIRCE'S LOCK" },
                    { "Bambi Freeze", "Freeze, pet." },
                    { "BAMBI FREEZE", "FREEZE, PET." },
                    { "Bambi Reset", "Back to the start, sweet thing." },
                    { "BAMBI RESET", "BACK TO THE START, SWEET THING." },
                    { "BAMBI CUM AND COLLAPSE", "LET GO. FALL APART FOR ME, GOOD BOY." },
                    { "Bambi Takeover", "Surrender Control" },
                    { "BambiCloud", "HypnoTube" },
                    { "BambiSprite", "Circe" },

                    // Personality preset renames
                    { "Slut Mode", "Eager Mode" },
                    { "Gentle Trainer", "Soft Keeper" },
                    { "Strict Domme", "Mistress Mode" },
                    { "Bimbo Coach", "Mommy's Guidance" },
                    { "Hypno Guide", "Trance Keeper" },

                    // Enhancement-tree skill renames (devotion / keeping / locks theme)
                    { "Pink Hours", "Kept Hours" },
                    { "Ditzy Data", "Hazy Data" },
                    { "Sparkle Boost", "Devotion I" },
                    { "Good Girl Streak", "Good Boy Streak" },
                    { "Hive Mind", "Kept Network" },
                    { "Trophy Case", "Kept Collection" },
                    { "Extra Sparkly", "Devotion II" },
                    { "Lucky Bimbo", "Lucky Pet" },
                    { "Milestone Rewards", "Milestone Gifts" },
                    { "Oopsie Insurance", "Mercy Insurance" },
                    { "Popular Girl", "Favored Pet" },
                    { "Quest Refresh", "New Orders" },
                    { "Better Quests", "Better Orders" },
                    { "Maximum Sparkle", "Devotion III" },
                    { "Lucky Bubbles", "Lucky Locks" },
                    { "Pink Rush", "Her Favor" },
                    { "Streak Power", "Streak Devotion" },
                    { "Reroll Addict", "Indecisive Pet" },
                    { "Perfect Bimbo Week", "Perfect Week" },
                    { "Night Shift", "Night Devotion" },
                    { "Early Bird Bimbo", "Early Devotion" },
                    { "Eternal Doll", "Eternal Pet" },
                    { "Bimbo Basics", "Pet Basics" },
                    { "Pink Cloud", "Magenta Cloud" },

                    // Achievement renames (established Locked badge-art names; identical-to-base
                    // names are left unmapped and render from the base set unchanged)
                    { "Plastic Initiation", "Initiation" },
                    { "Dumb Bimbo", "Empty Beta" },
                    { "Fully Synthetic", "Fully Kept" },
                    { "Docile Cow", "Kept Pet" },
                    { "Perfect Plastic Puppet", "Perfect Kept Toy" },
                    { "Rose-Tinted Reality", "Magenta-Tinted Reality" },
                    { "Deep Sleep Mode", "Deep Sleep" },
                    { "Look, But Don't Touch", "Look, Don't Touch" },
                    { "Mathematician's Nightmare", "Counter's Nightmare" },
                    { "Pop Goes The Thought", "Pop The Thought" },
                    { "Neon Obsession", "Obsession" },
                    { "Panic Button? What Panic Button?", "What Panic Button?" },

                    // Feature renames — neutral feature names are kept as base; only the
                    // palette-specific one is remapped.
                    { "Pink Filter", "Magenta Filter" },

                    // Base terminology — applied last in mod-aware order (longer keys first
                    // are evaluated earlier, so the specific replacements above take precedence).
                    { "Bambi", "Pet" },
                    { "BAMBI", "PET" },
                    { "Bimbo", "Kept Boy" },
                    { "bimbo", "kept boy" },
                    { "BIMBO", "KEPT BOY" },
                    { "pink", "magenta" },
                    { "Pink", "Magenta" },
                    { "PINK", "MAGENTA" }
                }
            };
        }
    }
}
