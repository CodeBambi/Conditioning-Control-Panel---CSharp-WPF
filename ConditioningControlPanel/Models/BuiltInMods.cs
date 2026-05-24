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
        public const string DronificationId = "builtin-dronification";

        public static ModManifest CCPDefault { get; } = CreateCCPDefault();
        public static ModManifest BambiSleep { get; } = CreateBambiSleep();
        public static ModManifest SissyHypno { get; } = CreateSissyHypno();
        public static ModManifest Dronification { get; } = CreateDronification();

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
                    ShowBambiCloudOption = true
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
                    ShowBambiCloudOption = false
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
                    ShowBambiCloudOption = false
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
                MinAppVersion = "6.0.0",
                Tags = new List<string> { "drone", "cyberpunk", "terminal", "sci-fi", "matrix" },
                PreviewImage = "preview.png",

                Theme = new ModTheme
                {
                    AccentColor = "#00FF41",
                    AccentLightColor = "#39FF14",
                    AccentDarkColor = "#008F11",
                    BackgroundColor = "#0D0D0D",
                    PanelColor = "#1A1A1A",
                    SurfaceColor = "#121212"
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
                    ShowBambiCloudOption = false
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
                    }
                },

                TextReplacements = new Dictionary<string, string>
                {
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
                    { "Bambi", "Unit" },
                    { "BAMBI", "UNIT" },
                    { "bimbo", "drone" },
                    { "BIMBO", "DRONE" },
                    { "pink", "green" },
                    { "Pink", "Green" },
                    { "PINK", "GREEN" }
                }
            };
        }
    }
}
