using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Defines the two built-in mods (BambiSleep and SissyHypno) as ModManifest objects.
    /// Data is extracted 1:1 from ContentModeConfig to ensure identical behavior.
    /// </summary>
    public static class BuiltInMods
    {
        public const string BambiSleepId = "builtin-bambisleep";
        public const string SissyHypnoId = "builtin-sissyhypno";

        public static ModManifest BambiSleep { get; } = CreateBambiSleep();
        public static ModManifest SissyHypno { get; } = CreateSissyHypno();

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
                    TakeoverLabel = "Bambi Takeover"
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
                    TakeoverLabel = "Bimbo Takeover"
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
    }
}
