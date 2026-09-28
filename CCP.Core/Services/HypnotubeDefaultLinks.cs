using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The legacy per-mode Hypnotube defaults, moved out of the WPF head's <c>BambiSprite</c> so the
    /// mod service's one-time Hypnotube migration can run in Core. Text unchanged.
    /// </summary>
    public static class HypnotubeDefaultLinks
    {
        /// <summary>
        /// Default hypnotube link URLs for the Sissy Hypno content mode, comma-separated.
        /// Shown in the companion tab link pool editor so users see the defaults and format.
        /// </summary>
        public static string DefaultSissyHypnoLinks { get; } = string.Join(",",
            "https://hypnotube.com/video/ultimate-sissy-mindfuck-106170.html",
            "https://hypnotube.com/video/femboy-heaven-ts-pmv-90699.html",
            "https://hypnotube.com/video/wife-helps-you-take-cock-91559.html",
            "https://hypnotube.com/video/up-and-down-95541.html",
            "https://hypnotube.com/video/neural-rewire-mommys-fap-roulette-devereux-115970.html",
            "https://hypnotube.com/video/girly-thoughts-vertical-loop-118644.html",
            "https://hypnotube.com/video/splitscreen-anal-trainer-4-by-dildoslut-111004.html",
            "https://hypnotube.com/video/anal-dream-sissygaljasmine-edition-90388.html",
            "https://hypnotube.com/video/bbc-stoner-goon-file-89975.html",
            "https://hypnotube.com/video/sissy-desires-103899.html",
            "https://hypnotube.com/video/a-touch-of-femboy-ts-pmv-110470.html",
            "https://hypnotube.com/video/say-yes-to-cock-hypnosis-112015.html",
            "https://hypnotube.com/video/pegging-dreams-full-110796.html",
            "https://hypnotube.com/video/hold-it-down-deepthroat-trainer-by-whore-factory-112708.html",
            "https://hypnotube.com/video/anal-slut-trainer-101540.html",
            "https://hypnotube.com/video/you-love-cock-105890.html",
            "https://hypnotube.com/video/deep-acceptance-113157.html",
            "https://hypnotube.com/video/eat-your-cum-116026.html",
            "https://hypnotube.com/video/trans-love-hypno-crimsonpmv-121310.html");

        /// <summary>
        /// Default hypnotube link URLs for the Bambi Sleep content mode, comma-separated.
        /// Shown in the companion tab link pool editor so users see the defaults and format.
        /// </summary>
        public static string DefaultBambiSleepLinks { get; } = string.Join(",",
            "https://hypnotube.com/video/naughty-bambi-109749.html",
            "https://hypnotube.com/video/bambi-bae-113979.html",
            "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html",
            "https://hypnotube.com/video/tiktok-loop-39245.html",
            "https://hypnotube.com/video/overload-46422.html",
            "https://hypnotube.com/video/bambi-tiktok-in-beat-52730.html",
            "https://hypnotube.com/video/bambi-tiktok-in-beat-longer-version-56194.html",
            "https://hypnotube.com/video/bambi-tiktok-good-girls-dont-cum-68081.html",
            "https://hypnotube.com/video/bambi-chastity-overload-75092.html",
            "https://hypnotube.com/video/mommys-in-control-full-76043.html",
            "https://hypnotube.com/video/bambi-loves-hentai-oneekitsune-78373.html",
            "https://hypnotube.com/video/bubblehead-forever-iplaywithdolls-79880.html",
            "https://hypnotube.com/video/dumb-bimbo-brainwash-80780.html",
            "https://hypnotube.com/video/bambi-tiktok-eager-slut-80971.html",
            "https://hypnotube.com/video/mindlocked-cock-zombie-87742.html",
            "https://hypnotube.com/video/bambi-tiktok-good-girl-academy-92527.html",
            "https://hypnotube.com/video/bambi-tiktok-chastity-trainer-96290.html",
            "https://hypnotube.com/video/bambi-slay-99609.html",
            "https://hypnotube.com/video/yes-brain-loop-113736.html",
            "https://hypnotube.com/video/bambi-uniform-bliss-3553.html",
            "https://hypnotube.com/video/bambi-bimbo-dreams-ep-1-8050.html",
            "https://hypnotube.com/video/day-1-11009.html",
            "https://hypnotube.com/video/day-2-11011.html",
            "https://hypnotube.com/video/day-4-11179.html",
            "https://hypnotube.com/video/day-5-11228.html",
            "https://hypnotube.com/video/bimbo-servitude-brainwash-33041.html",
            "https://hypnotube.com/video/bambi-uniform-oblivion-34010.html",
            "https://hypnotube.com/video/bambi-tiktok-7-42488.html",
            "https://hypnotube.com/video/bambi-tik-tok-mix-1-7-no-pauses-53860.html",
            "https://hypnotube.com/video/bambi-s-brain-melts-tiktok-56183.html",
            "https://hypnotube.com/video/bimbodoll-seduction-part-i-62493.html",
            "https://hypnotube.com/video/toms-dangerous-tik-tok-62552.html",
            "https://hypnotube.com/video/bimbodoll-awakened-obedience-62614.html",
            "https://hypnotube.com/video/bimbdoll-resistance-full-63079.html",
            "https://hypnotube.com/video/bambi-i-want-your-cum-64715.html",
            "https://hypnotube.com/video/bambi-day-7-remix-65691.html",
            "https://hypnotube.com/video/bambi-tiktok-wide-remix-by-analbambi-66055.html");

        /// <summary>
        /// Exact HypnoTube / BambiCloud titles mapped to URLs, as shown on the site. Moved from the WPF
        /// head's <c>AvatarTubeWindow.KnownVideoLinks</c> (which now starts from a copy of this) so
        /// every head's one-time Hypnotube migration names a known URL by its canonical title.
        /// Insertion order is part of the contract: reverse lookups keep the first name per URL.
        /// </summary>
        public static IReadOnlyDictionary<string, string> KnownVideoTitles { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Naughty Bambi", "https://hypnotube.com/video/naughty-bambi-109749.html" },
            { "Bambi Bae", "https://hypnotube.com/video/bambi-bae-113979.html" },
            { "Bambi's Naughty TikTok Collection", "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html" },
            { "TikTok Loop", "https://hypnotube.com/video/tiktok-loop-39245.html" },
            { "Overload", "https://hypnotube.com/video/overload-46422.html" },
            { "Bambi TikTok - In Beat", "https://hypnotube.com/video/bambi-tiktok-in-beat-52730.html" },
            { "Bambi TikTok - In Beat - Longer Version", "https://hypnotube.com/video/bambi-tiktok-in-beat-longer-version-56194.html" },
            { "Bambi TikTok - Good Girls Dont Cum", "https://hypnotube.com/video/bambi-tiktok-good-girls-dont-cum-68081.html" },
            { "Bambi Chastity Overload", "https://hypnotube.com/video/bambi-chastity-overload-75092.html" },
            { "Mommy's In Control Full", "https://hypnotube.com/video/mommys-in-control-full-76043.html" },
            { "Bambi Loves Hentai - OneeKitsune", "https://hypnotube.com/video/bambi-loves-hentai-oneekitsune-78373.html" },
            { "Bubblehead Forever - Iplaywithdolls", "https://hypnotube.com/video/bubblehead-forever-iplaywithdolls-79880.html" },
            { "Dumb Bimbo Brainwash", "https://hypnotube.com/video/dumb-bimbo-brainwash-80780.html" },
            { "Bambi TikTok Eager Slut", "https://hypnotube.com/video/bambi-tiktok-eager-slut-80971.html" },
            { "Mindlocked Cock Zombie", "https://hypnotube.com/video/mindlocked-cock-zombie-87742.html" },
            { "Bambi TikTok Good Girl Academy", "https://hypnotube.com/video/bambi-tiktok-good-girl-academy-92527.html" },
            { "Bambi TikTok Chastity Trainer", "https://hypnotube.com/video/bambi-tiktok-chastity-trainer-96290.html" },
            { "Bambi Slay", "https://hypnotube.com/video/bambi-slay-99609.html" },
            // Batch 2
            { "Yes Brain Loop", "https://hypnotube.com/video/yes-brain-loop-113736.html" },
            { "Bambi Uniform Bliss", "https://hypnotube.com/video/bambi-uniform-bliss-3553.html" },
            { "Bambi Bimbo Dreams Ep 1", "https://hypnotube.com/video/bambi-bimbo-dreams-ep-1-8050.html" },
            { "Day 1", "https://hypnotube.com/video/day-1-11009.html" },
            { "Day 2", "https://hypnotube.com/video/day-2-11011.html" },
            { "Day 4", "https://hypnotube.com/video/day-4-11179.html" },
            { "Day 5", "https://hypnotube.com/video/day-5-11228.html" },
            { "Bimbo Servitude Brainwash", "https://hypnotube.com/video/bimbo-servitude-brainwash-33041.html" },
            { "Bambi Uniform Oblivion", "https://hypnotube.com/video/bambi-uniform-oblivion-34010.html" },
            { "Bambi TikTok 7", "https://hypnotube.com/video/bambi-tiktok-7-42488.html" },
            { "Bambi Tik-Tok Mix 1 - 7 No Pauses", "https://hypnotube.com/video/bambi-tik-tok-mix-1-7-no-pauses-53860.html" },
            { "Bambi's Brain Melts TikTok", "https://hypnotube.com/video/bambi-s-brain-melts-tiktok-56183.html" },
            { "Bimbodoll Seduction - Part I", "https://hypnotube.com/video/bimbodoll-seduction-part-i-62493.html" },
            { "Toms Dangerous Tik Tok", "https://hypnotube.com/video/toms-dangerous-tik-tok-62552.html" },
            { "Bimbodoll Awakened Obedience", "https://hypnotube.com/video/bimbodoll-awakened-obedience-62614.html" },
            { "Bimbdoll Resistance Full", "https://hypnotube.com/video/bimbdoll-resistance-full-63079.html" },
            { "Bambi - I Want Your Cum", "https://hypnotube.com/video/bambi-i-want-your-cum-64715.html" },
            { "Bambi Day 7 Remix", "https://hypnotube.com/video/bambi-day-7-remix-65691.html" },
            { "Bambi Tiktok Wide Remix By Analbambi", "https://hypnotube.com/video/bambi-tiktok-wide-remix-by-analbambi-66055.html" },
            // Sissy Hypno pool
            { "Ultimate Sissy Mindfuck", "https://hypnotube.com/video/ultimate-sissy-mindfuck-106170.html" },
            { "Femboy Heaven - TS PMV", "https://hypnotube.com/video/femboy-heaven-ts-pmv-90699.html" },
            { "Wife Helps You Take Cock", "https://hypnotube.com/video/wife-helps-you-take-cock-91559.html" },
            { "Up and Down", "https://hypnotube.com/video/up-and-down-95541.html" },
            { "Neural Rewire - Mommys Fap Roulette - Devereux", "https://hypnotube.com/video/neural-rewire-mommys-fap-roulette-devereux-115970.html" },
            { "Girly Thoughts Vertical Loop", "https://hypnotube.com/video/girly-thoughts-vertical-loop-118644.html" },
            { "Splitscreen Anal Trainer 4 - By Dildoslut", "https://hypnotube.com/video/splitscreen-anal-trainer-4-by-dildoslut-111004.html" },
            { "Anal Dream - SissyGalJasmine Edition", "https://hypnotube.com/video/anal-dream-sissygaljasmine-edition-90388.html" },
            { "BBC Stoner Goon File", "https://hypnotube.com/video/bbc-stoner-goon-file-89975.html" },
            { "Sissy Desires", "https://hypnotube.com/video/sissy-desires-103899.html" },
            { "A Touch Of Femboy - TS PMV", "https://hypnotube.com/video/a-touch-of-femboy-ts-pmv-110470.html" },
            { "Say Yes To Cock Hypnosis", "https://hypnotube.com/video/say-yes-to-cock-hypnosis-112015.html" },
            { "Pegging Dreams - Full", "https://hypnotube.com/video/pegging-dreams-full-110796.html" },
            { "Hold it Down - Deepthroat Trainer By Whore Factory", "https://hypnotube.com/video/hold-it-down-deepthroat-trainer-by-whore-factory-112708.html" },
            { "Anal Slut Trainer", "https://hypnotube.com/video/anal-slut-trainer-101540.html" },
            { "You Love Cock", "https://hypnotube.com/video/you-love-cock-105890.html" },
            { "Deep Acceptance", "https://hypnotube.com/video/deep-acceptance-113157.html" },
            { "Eat Your Cum", "https://hypnotube.com/video/eat-your-cum-116026.html" },
            { "Trans Love Hypno - CrimsonPMV", "https://hypnotube.com/video/trans-love-hypno-crimsonpmv-121310.html" },

            // BambiCloud playlists (audio, extracted 2026-04-28)
            { "IQ Programming", "https://bambicloud.com/playlist/ff15f538-6e6b-433c-b68b-b4af5ee5d14d" },
            { "Attitude Programming", "https://bambicloud.com/playlist/c0effdad-6002-4269-a982-479d676c8d46" },
            { "Takeover Programming", "https://bambicloud.com/playlist/726403c2-567c-4c30-9f74-8fd750a82ef9" },
            { "Cockslut Programming", "https://bambicloud.com/playlist/10091e87-2243-4f75-85d1-912c39951bc4" },
            { "Uniform Programming", "https://bambicloud.com/playlist/39f0c016-abfb-4a53-a8d3-1c492a86635b" },
            { "Maid Programming", "https://bambicloud.com/playlist/d244e2d6-be21-4e5b-bab1-b1268ade85ce" },
            { "Deep Trance Programming", "https://bambicloud.com/playlist/648f16c8-865b-44e2-bba5-881fc499e0f7" },
            { "Personality Programming", "https://bambicloud.com/playlist/ba1cf73a-5f3e-4ef8-bbc6-67ce2dcae774" },
        };
    }
}
