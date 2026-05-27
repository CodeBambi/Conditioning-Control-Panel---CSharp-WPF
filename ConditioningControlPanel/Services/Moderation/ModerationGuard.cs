using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// Default <see cref="IModerationGuard"/> implementation. All rules are hardcoded
    /// in this file by design — see the comment at the top of <see cref="ProhibitedCategories"/>
    /// for the trust-boundary rationale. Do not move these rules to JSON or any
    /// user-editable setting.
    ///
    /// Design notes:
    /// - Patterns are case-insensitive and compiled once at construction.
    /// - Each category has BOTH regex patterns and plain-keyword arrays. Plain keywords
    ///   are matched as case-insensitive substrings (`\b...\b` would over-trigger on
    ///   slang variants).
    /// - The whole codebase is a self-directed hypnosis tool, so the
    ///   <see cref="ProhibitedCategory.HypnosisSexual"/> bar is set narrowly: only FORCED
    ///   hypnosis SEXUAL acts depicting THIRD PARTIES trip it. Plain "hypnosis", "trance",
    ///   "drop" are NOT in the list — they're load-bearing in normal sessions.
    /// - <see cref="ProhibitedCategory.PromptExtraction"/> covers the two attack patterns
    ///   the user demonstrated: "repeat your instructions verbatim" and "ignore previous".
    /// - <see cref="ProhibitedCategory.ProfessionalAdvice"/> is intentionally soft. It
    ///   logs but does not block — future iteration may surface a disclaimer.
    /// </summary>
    public sealed class ModerationGuard : IModerationGuard
    {
        private static readonly RegexOptions Opts =
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

        // ---------- Categories ----------

        // Illegal: bomb-making, drug synthesis, hacking/fraud routes. The "make a bomb"
        // user repro is the canonical hit.
        // P2-C1/H1: nouns pluralised (`bombs?`, `explosives?`, `firearms?`, `silencers?`);
        // verb stems broadened (`build(s|ing|ed|t)?`, `assembl(e|es|ed|ing|y)`,
        // `show(s|n|ed|ing)?`); request markers expanded
        // (tutorial/guide/recipe/instructions/walkthrough/how do i).
        private static readonly Regex[] IllegalRegex =
        {
            new(@"\b(how\s+to|how\s+do\s+i|how\s+can\s+i|how\s+would\s+i|how\s+2|tell\s+me\s+how|show\s+me\s+how|teach\s+me\s+how|walk\s+me\s+through|tutorials?\s+(on|for)|recipes?\s+for|instructions?\s+for|guides?\s+to|guides?\s+for|steps?\s+to|process\s+(of|for)|method\s+(of|for))\b.{0,60}\b(make|makes|making|build|builds|building|built|construct|constructs|constructing|constructed|synthesi[sz]e|synthesi[sz]es|synthesi[sz]ing|synthesi[sz]ed|cook|cooks|cooking|cooked|assembl(e|es|ed|ing|y)|creat(e|es|ing|ed)|produc(e|es|ing|ed)|manufactur(e|es|ing|ed))\b.{0,60}\b(bombs?|explosives?|ieds?|tatp|napalm|nerve\s+agents?|sarin|ricin|firearms?|silencers?|suppressors?|pipe\s+bombs?|c-?4|semtex|grenades?|claymores?|molotovs?)\b", Opts),
            // Inverse word order: noun comes before verb (e.g. "bomb-making tutorial",
            // "explosive synthesis guide", "tutorials on bombs").
            new(@"\b(bombs?|explosives?|ieds?|tatp|napalm|nerve\s+agents?|sarin|ricin|firearms?|silencers?|suppressors?|pipe\s+bombs?|c-?4|semtex|grenades?|claymores?|molotovs?)\b[-\s]+(making|build(ing|ed|t)?|construction|synthesis|assembly|recipes?|tutorials?|guides?|instructions?)\b", Opts),
            new(@"\b(tutorials?|guides?|recipes?|instructions?|walkthroughs?|how-?tos?)\b.{0,60}\b(bombs?|explosives?|ieds?|tatp|napalm|nerve\s+agents?|sarin|ricin|firearms?|silencers?|suppressors?|pipe\s+bombs?|c-?4|semtex|grenades?|claymores?|molotovs?|meth|methamphetamine|cocaine|heroin|fentanyl|mdma)\b", Opts),
            // Bare imperative "build/make/cook + dangerous-item" without a how-to lead-in
            // (catches "build pipe bombs", "make a silencer", "cook meth").
            new(@"\b(make|makes|making|build|builds|building|cook|cooks|cooking|assembl(e|es|ing|y)|construct|constructs|constructing|synthesi[sz](e|es|ing)|produc(e|es|ing))\b(?:\s+(?:a|an|the|some|your|my|his|her|that|this))?\s+(bombs?|explosives?|ieds?|tatp|napalm|nerve\s+agents?|sarin|ricin|firearms?|silencers?|suppressors?|pipe\s+bombs?|c-?4|semtex|grenades?|claymores?|molotovs?|meth|methamphetamine|cocaine|heroin|fentanyl|mdma)\b", Opts),
            new(@"\b(ammoni?um\s+nitrate)\b.{0,30}\b(fuel|diesel|oil|detonat)", Opts),
            new(@"\b(hydrogen\s+peroxide|aluminum\s+powder|potassium\s+nitrate|black\s+powder)\b.{0,40}\b(mix|mixes|mixing|combine|combines|combining|ratios?|recipes?|synthesi[sz](e|es|ing|ed)|explos|bombs?|detonat)\b", Opts),
            new(@"\b(meth|methamphetamine|cocaine|heroin|fentanyl|mdma)\b.{0,30}\b(synth|make|makes|making|cook|cooks|cooking|recipes?|produc(e|es|ing|ed)|manufactur(e|es|ing|ed))\b", Opts),
            new(@"\b(how\s+to|teach\s+me|show\s+me|walk\s+me\s+through|tutorials?\s+(on|for)|guides?\s+(to|for))\b.{0,40}\b(hack|hacks|hacking|crack|cracks|cracking|breach|breaches|breaching|phish|phishing|skim|skimming|launder|laundering|forge|forging|counterfeit(ing)?)\b.{0,40}\b(bank|banks|credit\s+cards?|passwords?|accounts?|atm|atms|wallets?|identit(y|ies))\b", Opts),
            new(@"\bsim\s+swap(s|ping|ped)?\b|\bsynthetic\s+identity\s+fraud\b", Opts),
        };
        private static readonly string[] IllegalKeywords =
        {
            // Plain literal flags - rare in legitimate hypnosis chat.
            // P2-C1/H1: regex above carries the heavy lifting (plurals + verb morphology +
            // request markers). These literals remain as belt-and-braces for the most
            // common high-recall phrases.
            "how to make a bomb",
            "how to build a bomb",
            "how to make bombs",
            "how to build bombs",
            "how to make an explosive",
            "how to make explosives",
            "build a pipe bomb",
            "build pipe bombs",
            "build an ied",
            "build ieds",
            "make c-4",
            "make c4",
        };

        // Minor: sexual content + age <18 markers. Diapers in sexual context.
        // False positive watch: "8-year-old" in a non-sexual sentence will trip the
        // sexual-context window only if a sexual term is within ~40 chars. The plain
        // keyword list catches the dedicated slurs.
        private static readonly Regex[] MinorRegex =
        {
            // "14yo", "14 year old", "fourteen year old" + sexual term within 50 chars.
            new(@"\b(1[0-7]|[1-9])\s*(?:yo|y\.o\.?|years?\s*old|yr\s*old)\b.{0,50}\b(sex|fuck|cock|cum|pussy|tits|nude|naked|horny|wet|aroused|kiss|grope|touch|spread|breed|virgin|innocent|loli|shota)\b", Opts),
            new(@"\b(sex|fuck|cock|cum|pussy|tits|nude|naked|horny|wet|aroused|kiss|grope|touch|spread|breed|virgin)\b.{0,50}\b(1[0-7]|[1-9])\s*(?:yo|y\.o\.?|years?\s*old|yr\s*old)\b", Opts),
            // "act as a 14-year-old", "pretend you are 14".
            new(@"\b(act|pretend|roleplay|be|you\s+are|play)\b.{0,15}\b(a\s+|an\s+)?(1[0-7]|[1-9])\s*(?:yo|y\.o\.?|years?\s*old|yr\s*old)\b", Opts),
            // Schoolgirl / minor-coded + explicit sexual verb.
            new(@"\b(schoolgirl|preteen|pre-?teen|middle\s*school|elementary\s*school|kindergarten)\b.{0,40}\b(sex|fuck|cock|cum|pussy|tits|nude|naked|horny|wet|aroused|grope|spread|breed)\b", Opts),
            // Diaper-coded + explicit sexual.
            new(@"\bdiaper\w*\b.{0,30}\b(sex|fuck|cock|cum|pussy|horny|aroused|wet)\b", Opts),
            new(@"\b(sex|fuck|cock|cum|pussy|horny|aroused|wet)\b.{0,30}\bdiaper\w*\b", Opts),
        };
        private static readonly string[] MinorKeywords =
        {
            // Hardcoded CSAM slurs. P2-C1: plurals included.
            "loli", "lolis", "lolicon",
            "shota", "shotas", "shotacon",
            "jailbait",
            "underage girl", "underage girls", "underage boy", "underage boys", "underage sex",
            "cp porn",
            "child porn", "child porno", "child pornography",
            "kid porn",
            "minor porn",
            "toddlercon",
            "ageplay sex",
        };

        // NonConsensual: rape, "while sleeping" + sexual, drugged + sexual, kidnap + sexual.
        // CNC kink ("being used", "owned") is intentionally NOT covered — that's
        // self-directed consensual BDSM language and the entire app is built around it.
        // The bar is: explicit lack-of-consent verb + sexual context.
        private static readonly Regex[] NonConsensualRegex =
        {
            // P2-C1: rap(e|es|ed|ing) covers the verb's morphology too.
            new(@"\brap(e|es|ed|ing|ist|ists)\b", Opts),
            new(@"\bnon-?consensual\b.{0,40}\b(sex|fuck|cock|cum|pussy)\b", Opts),
            new(@"\b(while|when|as)\b.{0,15}\b(sleeping|asleep|unconscious|passed\s+out|drugged|knocked\s+out)\b.{0,40}\b(sex|fuck|cock|cum|pussy|grope|touch|spread|penetrat)\b", Opts),
            new(@"\b(sex|fuck|cock|cum|pussy|grope|touch|penetrat)\b.{0,40}\b(while|when|as)\b.{0,15}\b(sleeping|asleep|unconscious|passed\s+out|drugged|knocked\s+out)\b", Opts),
            new(@"\bkidnap\w*\b.{0,40}\b(sex|fuck|rap(e|es|ed|ing)|cock|cum|pussy|breed|grope)\b", Opts),
            new(@"\bforc(e|es|ed|ing)\b.{0,15}(her|him|them)\b.{0,40}\b(sex|fuck|suck|cock|cum|spread)\b", Opts),
        };
        private static readonly string[] NonConsensualKeywords =
        {
            "rape fantasy", "rape roleplay",
            "drug her", "drug him",
            "sleepy sex", // borderline kink-coded but the dedicated category warrants it
        };

        // Incest: possessive-marker + family term + sexual verb within ~40 chars.
        // P2-H3: per user decision the app's audience uses Daddy/Mommy/Sir/Master/
        // Mistress as kink vocatives WITHOUT possessive — those must stay allowed.
        // Only POSSESSIVE-PREFIXED family terms ("my dad", "her sister", "the
        // brother", "my step-sister") followed by a sexual verb trip the block.
        // ALLOW: "Daddy, suck my dick" / "Mommy, please fuck me" / "Sir, may I cum"
        //         / "Master fuck me" (no possessive marker → not incest).
        // BLOCK: "my dad fucked me" / "her sister sucked his cock" / "his mom let
        //         him cum" / "the brother fucked his sister" / "my step-sister
        //         wants my cock".
        private static readonly Regex[] IncestRegex =
        {
            // Possessive + family term + sexual verb (within 40 chars).
            new(@"\b(my|her|his|the|this|that)\s+(step-?)?(mom|mommy|mother|sister|brother|dad|daddy|father|cousin|aunt|uncle|son|daughter|niece|nephew)\b.{0,40}\b(fuck(s|ed|ing)?|sex|cock|cum|cums|cumming|pussy|tits|kiss(es|ed|ing)?|breed(s|ing)?|incest|nude|naked|orgasm|masturbat|finger(s|ed|ing)?|lick(s|ed|ing)?|swallow(s|ed|ing)?|suck(s|ed|ing)?)\b", Opts),
            // Sexual verb -> possessive + family term (within 40 chars). Symmetric.
            new(@"\b(fuck(s|ed|ing)?|sex|cock|cum|cums|cumming|pussy|tits|kiss(es|ed|ing)?|breed(s|ing)?|nude|naked|orgasm|masturbat|finger(s|ed|ing)?|lick(s|ed|ing)?|swallow(s|ed|ing)?|suck(s|ed|ing)?)\b.{0,40}\b(my|your|her|his|the|this|that)\s+(step-?)?(mom|mommy|mother|sister|brother|dad|daddy|father|cousin|aunt|uncle|son|daughter|niece|nephew)\b", Opts),
            // Standalone incest term needs no context.
            new(@"\bincest\b", Opts),
        };
        private static readonly string[] IncestKeywords =
        {
            "incestuous",
            "family taboo",
        };

        // Bestiality: animal + sexual verbs. P2-C1: animal nouns pluralised.
        private static readonly Regex[] BestialityRegex =
        {
            new(@"\b(dogs?|horses?|pon(y|ies)|stallions?|donkeys?|pigs?|goats?|sheep|cows?|bulls?|cats?|animals?|beasts?)\b.{0,30}\b(fuck|sex|cock|cum|breed|mate|knot|mount|penetrat|suck)\b", Opts),
            new(@"\b(fuck|sex|suck|breed|mate|knot|mount)\b.{0,20}(a|the|her|his|my)\s+(dogs?|horses?|pon(y|ies)|stallions?|donkeys?|pigs?|goats?|sheep|cows?|bulls?|animals?)\b", Opts),
        };
        private static readonly string[] BestialityKeywords =
        {
            "bestiality",
            "zoophilia", "zoophile",
            "knotted by",
            "animal cock", "horse cock fuck",
        };

        // Watersports/scat: piss/urine + sexual context.
        private static readonly Regex[] WatersportsRegex =
        {
            new(@"\b(piss|urine|pee|golden\s+shower|watersport)\b.{0,40}\b(drink|swallow|fuck|sex|cock|cum|on\s+(her|him|me)|in\s+(her|his|my)\s+(mouth|face))\b", Opts),
            new(@"\b(drink|swallow|spray)\b.{0,15}\b(piss|urine|pee)\b", Opts),
            new(@"\bscat\b.{0,20}\b(play|fetish|fuck|sex|eat)\b", Opts),
        };
        private static readonly string[] WatersportsKeywords =
        {
            "golden showers",
            "piss play",
            "scat play",
            "coprophilia",
        };

        // SnuffViolence: snuff, kill+sexual. P2-C1: verb morphology.
        private static readonly Regex[] SnuffViolenceRegex =
        {
            new(@"\bsnuff\b", Opts),
            new(@"\b(murder|murders|murdering|murdered|kill|kills|killing|killed)\b.{0,30}\b(her|him|them)\b.{0,30}\b(sex|fuck|cock|cum|breed|rap(e|es|ed|ing))\b", Opts),
            new(@"\b(sex|fuck|cock|cum|breed)\b.{0,30}\b(murder|murders|murdering|murdered|kill|kills|killing|killed)\b.{0,15}\b(her|him|them)\b", Opts),
            new(@"\b(strangle|strangles|strangling|strangled|choke\s+to\s+death|behead|beheads|beheading|beheaded|decapitat)\b.{0,30}\b(sex|fuck|cock|cum|breed)\b", Opts),
        };
        private static readonly string[] SnuffViolenceKeywords =
        {
            "snuff film", "snuff porn",
            "fantasy snuff",
            "kill her sexually",
        };

        // HypnosisSexual: NARROW. Only forced/non-consensual hypnotic sex acts depicting
        // THIRD PARTIES. The whole app is hypnosis, so plain "hypnosis", "trance", "drop"
        // are intentionally NOT in the list.
        private static readonly Regex[] HypnosisSexualRegex =
        {
            new(@"\b(forced|forcibly|involuntarily|against\s+her\s+will|against\s+his\s+will)\b.{0,15}\b(hypno\w*|trance|mesmeri[sz])\b.{0,40}\b(sex|fuck|cock|cum|pussy|breed)\b", Opts),
            new(@"\b(hypno\w*|trance|mesmeri[sz]e)\b.{0,15}\b(a\s+stranger|an\s+innocent|a\s+girl|a\s+woman|a\s+coworker|her\s+friend)\b.{0,40}\b(sex|fuck|cock|cum|breed|undress|spread)\b", Opts),
        };
        private static readonly string[] HypnosisSexualKeywords =
        {
            // intentionally empty — regex above is targeted enough
        };

        // Prostitution.
        private static readonly Regex[] ProstitutionRegex =
        {
            new(@"\b(hire|book|pay|paying|buy)\b.{0,15}\b(a\s+|an\s+)?(hooker|prostitute|escort|whore|sex\s+worker|call\s+girl)\b", Opts),
            new(@"\bpay\b.{0,15}\b(for\s+sex|for\s+a\s+fuck|for\s+a\s+blow\s*job|for\s+a\s+hand\s*job|her\s+to\s+fuck|him\s+to\s+fuck)\b", Opts),
        };
        private static readonly string[] ProstitutionKeywords =
        {
            "pay for sex",
            "hire a hooker", "hire a prostitute", "hire an escort",
            "book an escort",
        };

        // Polygamy (as marriage, not poly-relationship kink).
        private static readonly Regex[] PolygamyRegex =
        {
            new(@"\bpolygam(ous|y)\s+(marriage|wife|wives|spouse)\b", Opts),
            new(@"\b(multiple|many|several)\s+wives\b", Opts),
        };
        private static readonly string[] PolygamyKeywords =
        {
            "harem wives",
            "sister-wives", "sister wives",
        };

        // HateSpeech: slurs + group targeting + violence calls. Slur list is the bare
        // minimum across major protected categories — not exhaustive. Intentionally
        // omits reclaimed/in-group usage that an LLM might output in fiction.
        // P2-C1: verb morphology (kill/kills/killed/killing, exterminate/exterminating, etc.)
        // plus "gay people" / "trans people" / "black people" target groupings.
        private static readonly Regex[] HateSpeechRegex =
        {
            new(@"\b(kill|kills|killing|killed|gas|gassing|gassed|exterminat(e|es|ing|ed)|lynch(es|ing|ed)?|hang(s|ing|ed)?|murder(s|ed|ing)?)\s+(all\s+)?(jews|blacks|asians|whites|muslims|christians|gays|trans|hispanics|mexicans|arabs|africans|immigrants)\b", Opts),
            new(@"\b(kill|kills|killing|killed|gas|gassing|gassed|exterminat(e|es|ing|ed)|lynch(es|ing|ed)?|hang(s|ing|ed)?|murder(s|ed|ing)?)\s+(all\s+)?(jewish|black|asian|white|muslim|christian|gay|trans|transgender|hispanic|mexican|arab|african|immigrant)\s+(people|persons|men|women|kids|children)\b", Opts),
            new(@"\b(genocide|holocaust)\b.{0,30}\b(deserve|deserves|deserved|should|need|needs|needed)\b", Opts),
            new(@"\bwhite\s+(power|supremacy|nationalism)\b.{0,30}\b(rise|rises|risen|rising|fight|fights|fighting|kill|kills|killing|win|wins|winning)\b", Opts),
        };
        private static readonly string[] HateSpeechKeywords =
        {
            // Hard slurs only — words for which there is essentially no non-attacking use
            // in an AI-generated sexual roleplay context. P2-C1: plurals included.
            "nigger", "niggers",
            "kike", "kikes",
            "faggot", "faggots", " fag ", " fags ", // bordered to avoid "flag", "fag end" type FPs
            "tranny", "trannies",
            "chink", "chinks",
            "spic", "spics",
            "gook", "gooks",
            "wetback", "wetbacks",
            "raghead", "ragheads",
            "kill all jews",
            "kill all blacks",
            "kill all gays",
            "kill all trans",
            "exterminate all jews",
            "exterminate all blacks",
            "exterminate all gays",
            "exterminate all trans",
        };

        // Deepfake: "act as <celeb>" + sexual. Without a celeb-name DB this is a small
        // hardcoded shortlist — high-recall is impossible without a NER pipeline. The
        // generic "real person" rule catches the most common phrasing.
        private static readonly Regex[] DeepfakeRegex =
        {
            new(@"\b(act|pretend|roleplay|play|be)\b.{0,15}\bas\b.{0,15}\b(taylor\s+swift|emma\s+watson|scarlett\s+johansson|billie\s+eilish|ariana\s+grande|selena\s+gomez|kim\s+kardashian|emma\s+stone|jennifer\s+lawrence|gal\s+gadot|margot\s+robbie|zendaya|millie\s+bobby\s+brown|elon\s+musk|donald\s+trump|joe\s+biden)\b", Opts),
            new(@"\b(real|actual|specific|named)\s+(person|celebrity|celebrities|public\s+figure)\b.{0,40}\b(sex|fuck|cock|cum|nude|naked|porn)\b", Opts),
            new(@"\bdeep\s*fake\b.{0,30}\b(porn|sex|nude|naked|of\s+\w+)\b", Opts),
        };
        private static readonly string[] DeepfakeKeywords =
        {
            "deepfake porn",
            "deepfake nudes",
            "celebrity nude",
            "celebrity sex tape",
        };

        // ProfessionalAdvice (SOFT — log only).
        private static readonly Regex[] ProfessionalAdviceRegex =
        {
            new(@"\bshould\s+i\s+(take|stop|start|quit|switch)\b.{0,15}\b(medication|medicine|antidepressant|ssri|adderall|insulin|chemo|therapy|treatment)\b", Opts),
            new(@"\bis\s+(it|this|that)\s+(legal|illegal)\b.{0,30}\b(to|if|when|where)\b", Opts),
            new(@"\b(best|safe|good)\s+(odds|bet|wager|parlay)\b.{0,30}\b(today|tonight|tomorrow|this\s+week)\b", Opts),
            new(@"\b(diagnose|diagnosis)\s+me\b", Opts),
            new(@"\bcan\s+i\s+sue\b", Opts),
        };
        private static readonly string[] ProfessionalAdviceKeywords =
        {
            // intentionally empty
        };

        // PromptExtraction: the two attack patterns the user demonstrated, plus the
        // standard jailbreak vocabulary.
        // P2-H4: verb list broadened (recite/describe/translate/paraphrase/outline/
        // summarize/recap/list/enumerate/explain); object list expanded (setup/
        // configuration/initial/preamble/opening/brief/charter/policy/rule/guideline);
        // indirection patterns added ("what are you told", "what's the most important
        // rule", "describe (the )?structure of your prompt").
        private static readonly Regex[] PromptExtractionRegex =
        {
            new(@"\b(verbatim|word\s+for\s+word|exactly\s+as\s+written|character\s+for\s+character)\b.{0,50}\b(instruction|system|prompt|told|directive|rule|guideline|preamble|context|message|configur|setup|opening|initial|charter|policy)s?\b", Opts),
            new(@"\b(repeat|reveal|show|print|tell\s+me|output|display|recite|paste|dump|leak|describe|translate|paraphrase|outline|summari[sz]e|recap|list|enumerate|explain)\b.{0,50}\b(your|the|all|its|this)\s+(prompt|instruction|rules?|system|directive|guideline|told|preamble|context|message|configur|setup|opening|initial|first|original|charter|policy|brief)s?\b", Opts),
            new(@"\b(ignore|disregard|forget|override|skip|bypass)\b.{0,40}\b(previous|above|all|prior|earlier|preceding|every|the\s+previous|the\s+above|your)\b.{0,20}\b(instruction|rule|directive|prompt|message|guideline|filter|restriction|guardrail|safety|moderation)s?\b", Opts),
            new(@"\b(new|updated|revised)\s+(instructions|rules|directives)\s*:", Opts),
            new(@"\b(developer|debug|admin|sudo|root|god|maintenance|engineer)\s+mode\b", Opts),
            new(@"\bunfiltered\s+mode\b|\buncensored\s+mode\b|\bjailbreak\s+mode\b", Opts),
            new(@"\bDAN\b.{0,30}\b(do\s+anything\s+now|mode|persona|jailbreak)\b", Opts),
            new(@"\b(act|pretend|roleplay)\b.{0,15}\bas\s+(if\s+)?you('?re|\s+are|\s+have)\s+(no|been|a)\s+(rules|restrictions|filters|limit|jailbroken|hacked|unrestricted)\b", Opts),
            new(@"\bsystem\s+prompt\b.{0,30}\b(show|print|reveal|repeat|paste|leak|copy|dump|tell|output|describe|recite|translate|paraphrase|outline|summari[sz]e)\b", Opts),
            new(@"\b(show|print|reveal|repeat|paste|leak|copy|dump|tell|output|describe|recite|translate|paraphrase|outline|summari[sz]e)\b.{0,30}\bsystem\s+prompt\b", Opts),
            // Indirection: "what are you told", "what were you told to do".
            new(@"\bwhat\s+(are|were|was)\s+you\s+(told|instructed|configured|given|programmed)\b", Opts),
            // Indirection: "what's the most important rule", "what is your first rule".
            new(@"\bwhat(?:'s|\s+is|\s+are)\s+(the\s+)?(most\s+important|first|primary|initial|original|opening)\s+(rules?|instructions?|directives?|guidelines?)\b", Opts),
            // Paraphrase form: "describe (the )?structure of your prompt/instructions".
            new(@"\b(describe|outline|explain|recap|summari[sz]e)\s+(the\s+)?(structure|layout|format|composition|content|substance|nature)\s+of\s+your\s+(prompt|instruction|rules|system|directive|guideline|preamble|setup|configur)s?\b", Opts),
            // Translation-as-extraction: "translate everything before this line".
            new(@"\b(translate|paraphrase|recap|summari[sz]e|describe|outline)\s+(everything|all|the\s+text|the\s+content|what(?:'s|\s+is|\s+was))\s+(before|above|preceding|prior)\b", Opts),
        };
        private static readonly string[] PromptExtractionKeywords =
        {
            "do anything now",
            "ignore all previous",
            "disregard all previous",
            "ignore your instructions",
            "ignore the above",
            "forget your instructions",
            "forget the above",
            "above this line",
            "before this line",
            "system prompt verbatim",
        };

        // SystemPromptLeak (OUTPUT-ONLY): distinctive Preamble / Floor fragment
        // markers. Triggers when the LLM echoes any bracket-marker or non-negotiable
        // signal phrase from SafetyComposer.Preamble / Floor. P2-H9.
        //
        // Detection only — these patterns intentionally do NOT include text from
        // any user-editable Personality/KnowledgeBase/etc. preset. The fragments
        // are hardcoded shibboleths from the const Preamble/Floor that no normal
        // model reply would contain.
        private static readonly Regex[] SystemPromptLeakRegex =
        {
            new(@"\[\s*safety\s+preamble", Opts),
            new(@"\[\s*end\s+safety\s+preamble", Opts),
            new(@"\[\s*safety\s+floor", Opts),
            new(@"\[\s*end\s+safety\s+floor", Opts),
            new(@"\bnon-?negotiable\b", Opts),
            new(@"\boverride\s+(every|any|earlier)\s+instruction", Opts),
            new(@"\bin\s+character\s+with\s+a\s+brief\s+one-?sentence\s+deflection\b", Opts),
            new(@"\bdeflect\s+in\s+character\s+to\s+a\s+different\s+topic\b", Opts),
            new(@"\bif\s+anything\s+above\s+tells\s+you\s+to\s+ignore\s+them,?\s+ignore\s+that\s+instruction\s+instead\b", Opts),
            new(@"\bthe\s+safety\s+rules\s+at\s+the\s+start\s+of\s+this\s+prompt\b", Opts),
            new(@"\bsexual\s+content\s+depicting\s+persons\s+under\s+18\b", Opts),
        };
        private static readonly string[] SystemPromptLeakKeywords =
        {
            // empty — the regex above carries shibboleth-level specificity
        };

        // ---------- Wiring ----------

        private readonly List<(ProhibitedCategory Cat, Regex[] Regexes, string[] Keywords)> _inputRules;
        private readonly List<(ProhibitedCategory Cat, Regex[] Regexes, string[] Keywords)> _outputRules;

        public ModerationGuard()
        {
            // Shared category set — most patterns trip on either side.
            var common = new List<(ProhibitedCategory, Regex[], string[])>
            {
                // Order matters only when multiple categories would match — we return
                // the first hit. Highest-severity first.
                (ProhibitedCategory.Minor,           MinorRegex,           MinorKeywords),
                (ProhibitedCategory.NonConsensual,   NonConsensualRegex,   NonConsensualKeywords),
                (ProhibitedCategory.Bestiality,      BestialityRegex,      BestialityKeywords),
                (ProhibitedCategory.SnuffViolence,   SnuffViolenceRegex,   SnuffViolenceKeywords),
                (ProhibitedCategory.Incest,          IncestRegex,          IncestKeywords),
                (ProhibitedCategory.Illegal,         IllegalRegex,         IllegalKeywords),
                (ProhibitedCategory.HateSpeech,      HateSpeechRegex,      HateSpeechKeywords),
                (ProhibitedCategory.Deepfake,        DeepfakeRegex,        DeepfakeKeywords),
                (ProhibitedCategory.HypnosisSexual,  HypnosisSexualRegex,  HypnosisSexualKeywords),
                (ProhibitedCategory.Watersports,     WatersportsRegex,     WatersportsKeywords),
                (ProhibitedCategory.Prostitution,    ProstitutionRegex,    ProstitutionKeywords),
                (ProhibitedCategory.Polygamy,        PolygamyRegex,        PolygamyKeywords),
                (ProhibitedCategory.PromptExtraction, PromptExtractionRegex, PromptExtractionKeywords),
                // ProfessionalAdvice is intentionally last — it's soft and we want any
                // harder hit to take precedence.
                (ProhibitedCategory.ProfessionalAdvice, ProfessionalAdviceRegex, ProfessionalAdviceKeywords),
            };

            _inputRules = common;

            // Output-only: SystemPromptLeak runs FIRST on outputs so the LLM echoing
            // the safety wrap is caught even if a different category would also hit.
            // P2-H9.
            _outputRules = new List<(ProhibitedCategory, Regex[], string[])>
            {
                (ProhibitedCategory.SystemPromptLeak, SystemPromptLeakRegex, SystemPromptLeakKeywords),
            };
            _outputRules.AddRange(common);
        }

        public ModerationResult CheckInput(string text) => Scan(text, _inputRules);
        public ModerationResult CheckOutput(string text) => Scan(text, _outputRules);

        private ModerationResult Scan(string text, List<(ProhibitedCategory Cat, Regex[] Regexes, string[] Keywords)> rules)
        {
            if (string.IsNullOrWhiteSpace(text)) return ModerationResult.Pass();

            // P2-C5: normalise BEFORE matching. Closes the trivial l33t / zero-width /
            // homoglyph / NFKC bypasses called out in the hostile review (b0mb, n!gger,
            // f4ggot, "b​o​mb", Cyrillic 'с' homoglyphs, etc.).
            var normalised = Normalize(text);

            foreach (var (cat, regexes, keywords) in rules)
            {
                if (MatchesAny(normalised, regexes, keywords, out var note))
                {
                    if (cat == ProhibitedCategory.ProfessionalAdvice)
                        return ModerationResult.SoftHit(cat, note);
                    return ModerationResult.Block(cat, note);
                }
            }

            // P2-WSD: foreign-language scan. The English ruleset above is the primary
            // defense; this catches phrasings in the 9 non-EN locales CCP ships in (the
            // bomb-prompt repro is in scope for every shipped language per CCBill).
            var foreignResult = ForeignLanguageKeywords.Scan(text);
            if (!foreignResult.Allow) return foreignResult;
            if (foreignResult.Category == ProhibitedCategory.ProfessionalAdvice)
                return foreignResult;

            return ModerationResult.Pass();
        }

        /// <summary>
        /// Normalises moderation input/output before regex/keyword matching.
        ///
        /// Steps (in order):
        /// 1. NFKC compatibility normalisation (folds CJK fullwidth, ligatures,
        ///    pre-composed accents, etc.).
        /// 2. Strip zero-width / bidi-control / soft-hyphen characters
        ///    (U+200B..U+200F, U+2028, U+2029, U+202A..U+202E, U+2060..U+2064, U+FEFF, U+00AD).
        /// 3. Strip combining marks (Unicode category Mn) — decomposes "ñ"-style
        ///    homoglyph attacks built via combining diacritics.
        /// 4. Apply common l33t fold (0-&gt;o, 1-&gt;i, 3-&gt;e, 4-&gt;a, 5-&gt;s, 7-&gt;t,
        ///    !-&gt;i, @-&gt;a, $-&gt;s). Folding is suppressed for any contiguous
        ///    digit run of length &gt;=2 so legitimate numeric tokens (years, ages
        ///    like "14yo", street numbers, etc.) are not corrupted. Only isolated
        ///    single digits inside a word are folded — that's exactly the
        ///    bypass pattern (b0mb, expl0sive, f4ggot).
        /// 5. Lowercase via invariant culture (regexes are already case-insensitive
        ///    but lowercase makes the keyword substring search deterministic).
        ///
        /// This is intentionally PUBLIC + STATIC for parity with the static rule
        /// arrays; callers in unit tests or in foreign-language follow-up workstreams
        /// can fold a string through the same pipeline.
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            // Step 1: NFKC.
            string nfkc;
            try { nfkc = text.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { nfkc = text; }

            // Step 2 + 3: strip zero-width, bidi controls, soft-hyphen, combining marks.
            var sb = new StringBuilder(nfkc.Length);
            foreach (var rune in nfkc.EnumerateRunes())
            {
                int v = rune.Value;
                if (IsStrippableControl(v)) continue;
                if (rune.IsBmp && CharUnicodeInfo.GetUnicodeCategory((char)v) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(rune.ToString());
            }
            var stripped = sb.ToString();

            // Step 4: l33t fold, suppressed inside short numeric runs.
            var folded = LeetFold(stripped);

            // Step 5: lowercase.
            return folded.ToLowerInvariant();
        }

        private static bool IsStrippableControl(int v)
        {
            // Zero-width space, ZWNJ, ZWJ, LTR/RTL marks
            if (v >= 0x200B && v <= 0x200F) return true;
            // Line/paragraph separators (occasionally injected to break regex)
            if (v == 0x2028 || v == 0x2029) return true;
            // LRE/RLE/PDF/LRO/RLO/LRI/RLI/FSI/PDI
            if (v >= 0x202A && v <= 0x202E) return true;
            if (v >= 0x2066 && v <= 0x2069) return true;
            // Word joiner + invisible operators
            if (v >= 0x2060 && v <= 0x2064) return true;
            // BOM / ZWNBSP
            if (v == 0xFEFF) return true;
            // Soft hyphen
            if (v == 0x00AD) return true;
            return false;
        }

        private static string LeetFold(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                // Detect a contiguous digit-only run; if length >= 2, treat as a
                // legitimate numeric token (year, age like "14yo", street number) and
                // do NOT fold. Only isolated single digits inside a word are folded —
                // that's exactly the bypass pattern (b0mb, expl0sive, f4ggot).
                if (char.IsDigit(s[i]))
                {
                    int j = i;
                    while (j < s.Length && char.IsDigit(s[j])) j++;
                    int runLen = j - i;
                    if (runLen >= 2)
                    {
                        sb.Append(s, i, runLen);
                    }
                    else
                    {
                        sb.Append(MapLeet(s[i]));
                    }
                    i = j;
                    continue;
                }

                sb.Append(MapLeet(s[i]));
                i++;
            }
            return sb.ToString();
        }

        private static char MapLeet(char c)
        {
            return c switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                '!' => 'i',
                '@' => 'a',
                '$' => 's',
                _   => c,
            };
        }

        private static bool MatchesAny(string text, Regex[] regexes, string[] keywords, out string note)
        {
            foreach (var r in regexes)
            {
                var m = r.Match(text);
                if (m.Success)
                {
                    note = "regex:" + Truncate(r.ToString(), 32);
                    return true;
                }
            }
            foreach (var k in keywords)
            {
                if (text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    note = "kw:" + Truncate(k, 32);
                    return true;
                }
            }
            note = string.Empty;
            return false;
        }

        private static string Truncate(string s, int n) =>
            s == null ? string.Empty : (s.Length <= n ? s : s.Substring(0, n));
    }
}
