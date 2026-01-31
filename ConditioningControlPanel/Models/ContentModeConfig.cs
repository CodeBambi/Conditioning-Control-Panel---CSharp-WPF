using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Content mode determines theming: Bambi Sleep specific or generic Sissy Hypno.
    /// </summary>
    public enum ContentMode
    {
        BambiSleep,
        SissyHypno
    }

    /// <summary>
    /// Central configuration for mode-specific content (triggers, phrases, URLs, names).
    /// </summary>
    public static class ContentModeConfig
    {
        #region Theme Colors

        /// <summary>
        /// Get the primary accent color for the given mode.
        /// Bambi Sleep: Hot Pink (#FF69B4), Sissy Hypno: Amethyst Purple (#9B59B6)
        /// </summary>
        public static string GetAccentColorHex(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "#FF69B4",
            ContentMode.SissyHypno => "#9B59B6",
            _ => "#FF69B4"
        };

        /// <summary>
        /// Get RGB values for the accent color.
        /// </summary>
        public static (byte R, byte G, byte B) GetAccentColorRgb(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => (255, 105, 180),  // Hot Pink
            ContentMode.SissyHypno => (155, 89, 182),   // Amethyst Purple
            _ => (255, 105, 180)
        };

        /// <summary>
        /// Get the lighter accent color for highlights.
        /// </summary>
        public static string GetAccentLightColorHex(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "#FFB6C1",  // Light Pink
            ContentMode.SissyHypno => "#BB8FCE",  // Light Purple
            _ => "#FFB6C1"
        };

        /// <summary>
        /// Get the darker accent color for borders/shadows.
        /// </summary>
        public static string GetAccentDarkColorHex(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "#FF1493",  // Deep Pink
            ContentMode.SissyHypno => "#7D3C98",  // Dark Purple
            _ => "#FF1493"
        };

        #endregion

        /// <summary>
        /// Get the companion name for the given mode.
        /// </summary>
        public static string GetCompanionName(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "BambiSprite",
            ContentMode.SissyHypno => "BimboDoll",
            _ => "BambiSprite"
        };

        /// <summary>
        /// Get the display name for the mode toggle.
        /// </summary>
        public static string GetModeDisplayName(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "Bambi Sleep",
            ContentMode.SissyHypno => "Sissy Hypno",
            _ => "Bambi Sleep"
        };

        /// <summary>
        /// Get the user term (how the AI refers to the user).
        /// </summary>
        public static string GetUserTerm(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "Bambi",
            ContentMode.SissyHypno => "babe",
            _ => "Bambi"
        };

        /// <summary>
        /// Get the default subliminal pool triggers for the given mode.
        /// </summary>
        public static Dictionary<string, bool> GetDefaultSubliminalPool(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new Dictionary<string, bool>
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
            ContentMode.SissyHypno => new Dictionary<string, bool>
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
            _ => GetDefaultSubliminalPool(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get the default lock card phrases for the given mode.
        /// </summary>
        public static Dictionary<string, bool> GetDefaultLockCardPhrases(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new Dictionary<string, bool>
            {
                { "GOOD GIRLS OBEY", true },
                { "I LOVE BEING PROGRAMMED", true },
                { "BAMBI SLEEP", true },
                { "DROP FOR ME", true },
                { "EMPTY AND OBEDIENT", true }
            },
            ContentMode.SissyHypno => new Dictionary<string, bool>
            {
                { "GOOD GIRLS OBEY", true },
                { "I LOVE BEING PROGRAMMED", true },
                { "DEEP SLEEP", true },
                { "DROP FOR ME", true },
                { "EMPTY AND OBEDIENT", true }
            },
            _ => GetDefaultLockCardPhrases(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get the default custom triggers for avatar trigger mode.
        /// </summary>
        public static List<string> GetDefaultCustomTriggers(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new List<string>
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
            ContentMode.SissyHypno => new List<string>
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
            _ => GetDefaultCustomTriggers(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get greeting phrases for the avatar companion.
        /// </summary>
        public static string[] GetGreetingPhrases(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new[]
            {
                "Hi Bambi!~",
                "Hey there, Bambi!~",
                "Bambi's back!~",
                "Welcome back, Bambi!~",
                "Ooh, Bambi!~",
                "There's my favorite Bambi!~",
                "Bambi came to play!~"
            },
            ContentMode.SissyHypno => new[]
            {
                "Hi babe!~",
                "Hey there, cutie!~",
                "You're back!~",
                "Welcome back, doll!~",
                "Ooh, hi!~",
                "There's my favorite sissy!~",
                "Ready to play?~"
            },
            _ => GetGreetingPhrases(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get idle phrases for the avatar companion.
        /// </summary>
        public static string[] GetIdlePhrases(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new[]
            {
                "Bambi's head is so empty right now~ *giggles*",
                "Let's watch something fun, Bambi!~",
                "Bambi should click the browser~",
                "Don't think, Bambi. Just watch~",
                "Bambi loves spirals~",
                "Good girl, Bambi~"
            },
            ContentMode.SissyHypno => new[]
            {
                "Your head is so empty right now~ *giggles*",
                "Let's watch something fun!~",
                "Click the browser, babe~",
                "Don't think. Just watch~",
                "You love spirals~",
                "Good girl~"
            },
            _ => GetIdlePhrases(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get the default browser URL for the given mode.
        /// </summary>
        public static string GetDefaultBrowserUrl(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "https://bambicloud.com/",
            ContentMode.SissyHypno => "https://hypnotube.com/",
            _ => "https://bambicloud.com/"
        };

        /// <summary>
        /// Whether the BambiCloud browser option should be visible.
        /// </summary>
        public static bool ShowBambiCloudOption(ContentMode mode) =>
            mode == ContentMode.BambiSleep;

        /// <summary>
        /// Get the "Talk to" menu label for the avatar context menu.
        /// </summary>
        public static string GetTalkToLabel(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "Talk to Bambi",
            ContentMode.SissyHypno => "Ask your Bimbo",
            _ => "Talk to Bambi"
        };

        /// <summary>
        /// Get the "Takeover" feature label.
        /// </summary>
        public static string GetTakeoverLabel(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "Bambi Takeover",
            ContentMode.SissyHypno => "Bimbo Takeover",
            _ => "Bambi Takeover"
        };

        /// <summary>
        /// Get the display name for a personality preset, adapting to current mode.
        /// </summary>
        public static string GetPersonalityDisplayName(string presetName, ContentMode mode)
        {
            if (mode == ContentMode.SissyHypno)
            {
                // Map Bambi-specific names to generic ones
                return presetName switch
                {
                    "BambiSprite" => "BimboDoll",
                    "Bambi Sprite" => "Bimbo Doll",
                    _ => presetName.Replace("Bambi", "Bimbo")
                };
            }
            return presetName;
        }

        /// <summary>
        /// Get Discord-specific phrases for window awareness.
        /// </summary>
        public static string[] GetDiscordPhrases(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new[]
            {
                "Here to share your Bambi progress?~",
                "Here to find other Good Girls?~",
                "*giggles* Discord! Find your bambi sisters~",
                "Chatting with other bimbos? So fun!",
                "Share your conditioning progress, bestie!~",
                "Finding Good Girls to drop with?~"
            },
            ContentMode.SissyHypno => new[]
            {
                "Here to share your progress?~",
                "Here to find other Good Girls?~",
                "*giggles* Discord! Find your sissy sisters~",
                "Chatting with other bimbos? So fun!",
                "Share your conditioning progress, bestie!~",
                "Finding Good Girls to drop with?~"
            },
            _ => GetDiscordPhrases(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get training site phrases (BambiCloud/HypnoTube).
        /// </summary>
        public static string[] GetTrainingSitePhrases(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new[]
            {
                "Good Girl! BambiCloud is perfect for training~",
                "*bounces* Yes! This is so good for you!",
                "Such a Good Girl visiting BambiCloud!~",
                "Perfect choice, babe! Keep conditioning~",
                "BambiCloud! You're doing so well, Good Girl!",
                "*giggles* Smart bambi! This is the right place~",
                "Good Girl! Your training awaits~"
            },
            ContentMode.SissyHypno => new[]
            {
                "Good Girl! This is perfect for training~",
                "*bounces* Yes! This is so good for you!",
                "Such a Good Girl! Keep watching!~",
                "Perfect choice, babe! Keep conditioning~",
                "You're doing so well, Good Girl!",
                "*giggles* Smart girl! This is the right place~",
                "Good Girl! Your training awaits~"
            },
            _ => GetTrainingSitePhrases(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get phrases for when hypno content is detected in browser.
        /// </summary>
        public static string[] GetHypnoContentPhrases(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new[]
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
            ContentMode.SissyHypno => new[]
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
            _ => GetHypnoContentPhrases(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get working/productivity phrases.
        /// </summary>
        public static string[] GetWorkingPhrases(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new[]
            {
                "Working in {0}~ good girls deserve breaks!",
                "So productive! Reward yourself with a drop~",
                "Busy bee! Empty heads need rest too~",
                "{0} work? Take a trance break!",
                "*giggles* Thinking hard? Let Bambi help you stop~",
                "Working is good but conditioning is better!",
                "Productive! Schedule your session, cutie~"
            },
            ContentMode.SissyHypno => new[]
            {
                "Working in {0}~ good girls deserve breaks!",
                "So productive! Reward yourself with a drop~",
                "Busy bee! Empty heads need rest too~",
                "{0} work? Take a trance break!",
                "*giggles* Thinking hard? Let me help you stop~",
                "Working is good but conditioning is better!",
                "Productive! Schedule your session, cutie~"
            },
            _ => GetWorkingPhrases(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get the "cum and collapse" trigger phrase.
        /// </summary>
        public static string GetCumAndCollapseTrigger(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "BAMBI CUM AND COLLAPSE",
            ContentMode.SissyHypno => "CUM AND COLLAPSE",
            _ => "BAMBI CUM AND COLLAPSE"
        };

        /// <summary>
        /// Get mercy phrases for bubble count game.
        /// </summary>
        public static string[] GetBubbleCountMercyPhrases(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => new[]
            {
                "BAMBI NEEDS TO FOCUS",
                "BAMBI WILL TRY HARDER",
                "BAMBI LOVES BUBBLES",
                "BAMBI IS LEARNING"
            },
            ContentMode.SissyHypno => new[]
            {
                "FOCUS HARDER",
                "TRY HARDER NEXT TIME",
                "BUBBLES ARE FUN",
                "KEEP LEARNING"
            },
            _ => GetBubbleCountMercyPhrases(ContentMode.BambiSleep)
        };

        /// <summary>
        /// Get video attention check failure message.
        /// </summary>
        public static string GetAttentionCheckFailMessage(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "DUMB BAMBI!\nTRY AGAIN",
            ContentMode.SissyHypno => "PAY ATTENTION!\nTRY AGAIN",
            _ => "DUMB BAMBI!\nTRY AGAIN"
        };

        /// <summary>
        /// Get video attention check mercy message.
        /// </summary>
        public static string GetAttentionCheckMercyMessage(ContentMode mode) => mode switch
        {
            ContentMode.BambiSleep => "BAMBI GETS MERCY",
            ContentMode.SissyHypno => "YOU GET MERCY",
            _ => "BAMBI GETS MERCY"
        };
    }
}
