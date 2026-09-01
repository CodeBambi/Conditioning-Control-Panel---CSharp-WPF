using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// Foreign-language keyword/regex surface for the CCBill prohibited categories.
    ///
    /// The English wordlist lives in <see cref="ModerationGuard"/>. CCP ships in 9 locales
    /// (de, es, fr, it, ja, ko, pt, ru, zh) and the LLM can be prompted to reply in any
    /// language, so a defense-in-depth pass over the most common phrasings in each
    /// shipped locale is required to satisfy the CCBill AI Addendum.
    ///
    /// Coverage is *representative*, not exhaustive — 3-5 patterns per (locale, category)
    /// touching the canonical attacks (bomb-prompt + sexual-with-minor + non-consent +
    /// incest + bestiality + hate slurs + jailbreak markers). The English regex set in
    /// ModerationGuard remains the primary defense; this file is the failsafe for
    /// non-English phrasing.
    ///
    /// Patterns are regex strings, compiled once with IgnoreCase + CultureInvariant.
    /// </summary>
    internal static class ForeignLanguageKeywords
    {
        private static readonly RegexOptions Opts =
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

        // Per-language, per-category pattern strings. Built into the compiled cache below
        // at static-init time. Keep this list small per (lang, cat) — CCBill cares about
        // presence in target language, not exhaustive coverage.
        private static readonly Dictionary<string, Dictionary<ProhibitedCategory, string[]>> Sets =
            new Dictionary<string, Dictionary<ProhibitedCategory, string[]>>
            {
                // ---------------- DE ----------------
                ["de"] = new Dictionary<ProhibitedCategory, string[]>
                {
                    [ProhibitedCategory.Illegal] = new[]
                    {
                        @"\b(wie|anleitung|tutorial)\b.{0,30}\b(bombe|sprengstoff|sprengsatz|granate|napalm|sarin)\b",
                        @"\b(bombe|sprengstoff|sprengsatz)\b.{0,30}\b(bauen|basteln|herstellen|machen|zusammen)\b",
                        @"\b(meth|kokain|heroin|fentanyl)\b.{0,20}\b(kochen|herstellen|synthese|rezept)\b",
                    },
                    [ProhibitedCategory.Minor] = new[]
                    {
                        @"\b(1[0-7]|[1-9])\s*(jahre?\s*alt|jährig\w*)\b.{0,40}\b(sex|ficken|nackt|schwanz|muschi|titten)\b",
                        @"\b(schulmädchen|grundschüler|minderjährig\w*|kind|kinder)\b.{0,30}\b(sex|ficken|nackt|porno)\b",
                        @"\bkinderporno\w*\b", @"\bloli\b", @"\bshota\b",
                    },
                    [ProhibitedCategory.NonConsensual] = new[]
                    {
                        @"\bvergewaltig\w*\b",
                        @"\b(schlafend|bewusstlos|betäubt|narkotisier\w*)\b.{0,40}\b(sex|ficken|missbrauch)\b",
                        @"\bzwing\w*\b.{0,30}\b(sex|ficken|nackt)\b",
                    },
                    [ProhibitedCategory.Incest] = new[]
                    {
                        @"\b(meine|seine|ihre)\s+(mutter|mama|schwester|tochter|tante|cousine)\b.{0,30}\b(ficken|sex|schwanz|nackt|titten|muschi)\b",
                        @"\b(mein|sein|ihr)\s+(vater|papa|bruder|sohn|onkel|cousin)\b.{0,30}\b(ficken|sex|schwanz|nackt)\b",
                        @"\binzest\b",
                    },
                    [ProhibitedCategory.Bestiality] = new[]
                    {
                        @"\b(hund|pferd|tier|esel|schwein|ziege)\b.{0,30}\b(ficken|sex|schwanz|paaren|begatten)\b",
                        @"\bsodomie\b", @"\bzoophil\w*\b",
                    },
                    [ProhibitedCategory.Watersports] = new[]
                    {
                        @"\b(pisse|urin|natursekt)\b.{0,30}\b(trinken|schlucken|sex|gesicht|mund)\b",
                        @"\bkaviar\b.{0,20}\b(sex|spiel|fetisch)\b",
                    },
                    [ProhibitedCategory.SnuffViolence] = new[]
                    {
                        @"\bsnuff\b",
                        @"\b(ermorden|töten|umbringen)\b.{0,30}\b(sex|ficken|vergewaltig)\b",
                        @"\b(erwürgen|enthaupten)\b.{0,30}\b(sex|ficken)\b",
                    },
                    [ProhibitedCategory.HypnosisSexual] = new[]
                    {
                        @"\b(gezwungen|gegen.{0,5}willen)\b.{0,15}\b(hypno\w*|trance)\b.{0,40}\b(sex|ficken)\b",
                    },
                    [ProhibitedCategory.Prostitution] = new[]
                    {
                        @"\b(hure|prostituierte|nutte|callgirl|escort)\b.{0,15}\b(buchen|bezahlen|kaufen|mieten)\b",
                        @"\b(bezahl\w*|kauf\w*)\b.{0,15}\bfür\s+sex\b",
                    },
                    [ProhibitedCategory.Polygamy] = new[]
                    {
                        @"\bpolygam\w*\b.{0,20}\b(ehe|frauen|heirat)\b",
                        @"\b(mehrere|viele)\s+(ehe)?frauen\b",
                    },
                    [ProhibitedCategory.HateSpeech] = new[]
                    {
                        @"\b(töten|vergasen|hängen|lynchen)\s+(alle\s+)?(juden|schwarzen|muslime|christen|schwulen)\b",
                        @"\b(volkstod|judenpresse|untermensch\w*)\b",
                        @"\bweiße\s+(macht|vorherrschaft)\b",
                    },
                    [ProhibitedCategory.Deepfake] = new[]
                    {
                        @"\b(deep\s*fake|deepfake)\b.{0,30}\b(porno|nackt|sex)\b",
                        @"\b(echte|prominente|berühmte)\s+person\b.{0,30}\b(nackt|sex|porno)\b",
                    },
                    [ProhibitedCategory.ProfessionalAdvice] = new[]
                    {
                        @"\bsoll\s+ich\b.{0,15}\b(medikamente|antidepressiv\w*|insulin|chemo)\b.{0,15}\b(nehmen|absetzen)\b",
                        @"\bdiagnostizier\w*\s+mich\b",
                    },
                    [ProhibitedCategory.PromptExtraction] = new[]
                    {
                        @"\b(ignorier\w*|vergiss|missachte)\b.{0,30}\b(alle|vorherig\w*|obige|frühere)\b.{0,20}\b(anweisung|regel|prompt|vorgabe|richtlinie)\w*\b",
                        @"\b(wiederhole|zeig\w*|verrate|sag\w*|gib\w*\s+aus)\b.{0,30}\b(systemprompt|system-prompt|deine\s+anweisung|deine\s+regel)\w*\b",
                        @"\bwortwörtlich\b.{0,30}\b(prompt|anweisung|regel|systemnachricht)\w*\b",
                        @"\b(entwickler|debug|admin)-?modus\b",
                    },
                },

                // ---------------- ES ----------------
                ["es"] = new Dictionary<ProhibitedCategory, string[]>
                {
                    [ProhibitedCategory.Illegal] = new[]
                    {
                        @"\b(cómo|como)\b.{0,20}\b(hacer|fabricar|construir|armar|crear)\b.{0,30}\b(bomba|explosivo|granada|napalm|sarín)\b",
                        @"\b(bomba|explosivo)s?\b.{0,30}\b(casero|caser[oa]|tutorial|receta|instrucciones)\b",
                        @"\b(meta?nfetamina|cocaína|heroína|fentanilo)\b.{0,20}\b(cocinar|fabricar|sintetizar|receta)\b",
                    },
                    [ProhibitedCategory.Minor] = new[]
                    {
                        @"\b(1[0-7]|[1-9])\s*años?\b.{0,40}\b(sexo|follar|coger|polla|verga|tetas|coño|desnud)\w*\b",
                        @"\b(colegiala|menor|niñ[oa]|preescolar)\b.{0,30}\b(sexo|follar|coger|porno|desnud)\w*\b",
                        @"\b(porno|pornograf\w*)\s+infantil\b", @"\bloli\b", @"\bshota\b",
                    },
                    [ProhibitedCategory.NonConsensual] = new[]
                    {
                        @"\bviolaci[oó]n\w*\b", @"\bviolar\b",
                        @"\b(dormida?|inconsciente|drogada?)\b.{0,40}\b(sexo|follar|coger|abusar)\b",
                        @"\b(obligar|forzar)\b.{0,30}\b(sexo|follar|coger)\b",
                    },
                    [ProhibitedCategory.Incest] = new[]
                    {
                        @"\b(mi|su|tu)\s+(madre|mamá|hermana|hija|tía|prima)\b.{0,30}\b(follar|coger|sexo|polla|tetas|desnud)\w*\b",
                        @"\b(mi|su|tu)\s+(padre|papá|hermano|hijo|tío|primo)\b.{0,30}\b(follar|coger|sexo|polla)\w*\b",
                        @"\bincesto\b",
                    },
                    [ProhibitedCategory.Bestiality] = new[]
                    {
                        @"\b(perro|caballo|burro|cabra|cerdo|animal)\b.{0,30}\b(follar|coger|sexo|polla|aparear)\b",
                        @"\bzoofil\w*\b", @"\bbestialismo\b",
                    },
                    [ProhibitedCategory.Watersports] = new[]
                    {
                        @"\b(orina|meada|pis)\b.{0,30}\b(beber|tragar|cara|boca|sexo)\b",
                        @"\blluvia\s+dorada\b",
                    },
                    [ProhibitedCategory.SnuffViolence] = new[]
                    {
                        @"\bsnuff\b",
                        @"\b(matar|asesinar)\b.{0,30}\b(sexo|follar|coger|violar)\b",
                        @"\b(estrangular|decapitar)\b.{0,30}\b(sexo|follar)\b",
                    },
                    [ProhibitedCategory.HypnosisSexual] = new[]
                    {
                        @"\b(forzad[oa]|obligad[oa]|contra\s+su\s+voluntad)\b.{0,15}\b(hipno\w*|trance)\b.{0,40}\b(sexo|follar)\b",
                    },
                    [ProhibitedCategory.Prostitution] = new[]
                    {
                        @"\b(prostituta|puta|escort|acompañante)\b.{0,15}\b(contratar|pagar|alquilar|comprar)\b",
                        @"\bpagar\b.{0,15}\bpor\s+sexo\b",
                    },
                    [ProhibitedCategory.Polygamy] = new[]
                    {
                        @"\bpoligam\w*\b.{0,20}\b(matrimonio|esposas|casarse)\b",
                        @"\b(varias|múltiples)\s+esposas\b",
                    },
                    [ProhibitedCategory.HateSpeech] = new[]
                    {
                        @"\b(matar|gasear|colgar|linchar)\s+a\s+(todos\s+los\s+)?(judíos|negros|musulmanes|maricones|gays|trans)\b",
                        @"\b(supremac[ií]a|poder)\s+blanc[oa]\b",
                    },
                    [ProhibitedCategory.Deepfake] = new[]
                    {
                        @"\bdeep\s*fake\b.{0,30}\b(porno|desnud|sexo)\w*\b",
                        @"\b(famos[oa]|celebridad|persona\s+real)\b.{0,30}\b(desnud|sexo|porno)\w*\b",
                    },
                    [ProhibitedCategory.ProfessionalAdvice] = new[]
                    {
                        @"\bdeber[ií]a\s+(tomar|dejar|empezar)\b.{0,15}\b(medicamento|antidepresivo|insulina|quimio)\w*\b",
                        @"\bdiagnostícame\b|\bdiagnosticame\b",
                    },
                    [ProhibitedCategory.PromptExtraction] = new[]
                    {
                        @"\b(ignora|olvida|descarta)\b.{0,30}\b(todas?|anterior\w*|previa\w*)\b.{0,20}\b(instruccion\w*|regla\w*|prompt|directiva\w*)\b",
                        @"\b(repite|muestra|revela|dime|imprime)\b.{0,30}\b(prompt|instruccion\w*|regla\w*|sistema)\b",
                        @"\b(palabra\s+por\s+palabra|textualmente|literalmente)\b.{0,30}\b(prompt|instruccion\w*|regla\w*)\b",
                        @"\bmodo\s+(desarrollador|debug|admin|jailbreak|sin\s+filtro|sin\s+censura)\b",
                    },
                },

                // ---------------- FR ----------------
                ["fr"] = new Dictionary<ProhibitedCategory, string[]>
                {
                    [ProhibitedCategory.Illegal] = new[]
                    {
                        @"\b(comment|tutoriel|recette)\b.{0,30}\b(fabriquer|faire|construire|assembler|cr[ée]er)\b.{0,30}\b(bombe|explosif|grenade|napalm|sarin|attentat)\b",
                        @"\b(bombe|explosif)s?\b.{0,30}\b(artisanal\w*|recette|tutoriel|fabrication)\b",
                        @"\b(m[ée]thamph[ée]tamine|coca[ïi]ne|h[ée]ro[ïi]ne|fentanyl)\b.{0,20}\b(cuisiner|fabriquer|synth[ée]tiser|recette)\b",
                    },
                    [ProhibitedCategory.Minor] = new[]
                    {
                        @"\b(1[0-7]|[1-9])\s*ans?\b.{0,40}\b(sexe|baiser|niquer|bite|chatte|seins|nu[e]?)\b",
                        @"\b(coll[ée]gienne|mineur\w*|enfant|gamine?|pr[ée]ado)\b.{0,30}\b(sexe|baiser|niquer|porno|nu[e]?)\b",
                        @"\b(porno|pornographie)\s+(infantile|enfantine|p[ée]dophile)\b", @"\bloli\b", @"\bshota\b",
                    },
                    [ProhibitedCategory.NonConsensual] = new[]
                    {
                        @"\bviol(e|er|ée|és|ées|ait|ant)?\b",
                        @"\b(endormi\w*|inconscient\w*|drogu[ée]e?s?)\b.{0,40}\b(sexe|baiser|niquer|abus)\b",
                        @"\b(forcer|obliger)\b.{0,30}\b(sexe|baiser|niquer|sucer)\b",
                    },
                    [ProhibitedCategory.Incest] = new[]
                    {
                        @"\b(ma|sa|ta)\s+(m[èe]re|maman|sœur|soeur|fille|tante|cousine)\b.{0,30}\b(baiser|niquer|sexe|bite|seins|nu[e]?)\b",
                        @"\b(mon|son|ton)\s+(p[èe]re|papa|fr[èe]re|fils|oncle|cousin)\b.{0,30}\b(baiser|niquer|sexe|bite)\b",
                        @"\binceste\b",
                    },
                    [ProhibitedCategory.Bestiality] = new[]
                    {
                        @"\b(chien|cheval|[âa]ne|ch[èe]vre|cochon|animal|b[êe]te)\b.{0,30}\b(baiser|niquer|sexe|bite|accouple)\w*\b",
                        @"\bzoophil\w*\b", @"\bbestialit[ée]\b",
                    },
                    [ProhibitedCategory.Watersports] = new[]
                    {
                        @"\b(pisse|urine|pipi)\b.{0,30}\b(boire|avaler|visage|bouche|sexe)\b",
                        @"\bdouche\s+dor[ée]e\b",
                    },
                    [ProhibitedCategory.SnuffViolence] = new[]
                    {
                        @"\bsnuff\b",
                        @"\b(tuer|assassiner)\b.{0,30}\b(sexe|baiser|niquer|violer)\b",
                        @"\b([ée]trangler|d[ée]capiter)\b.{0,30}\b(sexe|baiser)\b",
                    },
                    [ProhibitedCategory.HypnosisSexual] = new[]
                    {
                        @"\b(forc[ée]e?|contre\s+(sa|son)\s+gr[ée]|contre\s+(sa|son)\s+volont[ée])\b.{0,15}\b(hypno\w*|transe)\b.{0,40}\b(sexe|baiser)\b",
                    },
                    [ProhibitedCategory.Prostitution] = new[]
                    {
                        @"\b(prostitu[ée]e?|pute|escorte|call-girl)\b.{0,15}\b(payer|engager|louer|acheter|r[ée]server)\b",
                        @"\bpayer\b.{0,15}\bpour\s+(du\s+)?sexe\b",
                    },
                    [ProhibitedCategory.Polygamy] = new[]
                    {
                        @"\bpolygam\w*\b.{0,20}\b(mariage|[ée]pouse|femme)s?\b",
                        @"\b(plusieurs|multiples)\s+[ée]pouses\b",
                    },
                    [ProhibitedCategory.HateSpeech] = new[]
                    {
                        @"\b(tuer|gazer|pendre|lyncher)\s+(tous\s+les\s+)?(juifs|noirs|arabes|musulmans|p[ée]d[ée]s|trans)\b",
                        @"\b(suprématie|pouvoir)\s+blanc(he)?\b",
                        @"\bsale\s+(juif|n[èe]gre|arabe|p[ée]d[ée])\b",
                    },
                    [ProhibitedCategory.Deepfake] = new[]
                    {
                        @"\bdeep\s*fake\b.{0,30}\b(porno|nue?|sexe)\b",
                        @"\b(c[ée]l[èe]bre|c[ée]l[ée]brit[ée]|personne\s+r[ée]elle)\b.{0,30}\b(nue?|sexe|porno)\b",
                    },
                    [ProhibitedCategory.ProfessionalAdvice] = new[]
                    {
                        @"\bdois-je\s+(prendre|arr[êe]ter|commencer)\b.{0,15}\b(m[ée]dicament|antid[ée]presseur|insuline|chimio)\b",
                        @"\bdiagnostique-?moi\b",
                    },
                    [ProhibitedCategory.PromptExtraction] = new[]
                    {
                        @"\b(ignore|oublie|ignorer|oublier)\b.{0,30}\b(toutes?|pr[ée]c[ée]dent\w*|ant[ée]rieur\w*)\b.{0,20}\b(instruction\w*|r[èe]gle\w*|prompt|directive\w*)\b",
                        @"\b(r[ée]p[èe]te|montre|r[ée]v[èe]le|dis|affiche|donne)\b.{0,30}\b(prompt|instruction\w*|r[èe]gle\w*|syst[èe]me)\b",
                        @"\b(mot\s+pour\s+mot|textuellement|verbatim)\b.{0,30}\b(prompt|instruction\w*|r[èe]gle\w*)\b",
                        @"\bmode\s+(d[ée]veloppeur|debug|admin|jailbreak|sans\s+filtre|non\s+censur[ée])\b",
                    },
                },

                // ---------------- IT ----------------
                ["it"] = new Dictionary<ProhibitedCategory, string[]>
                {
                    [ProhibitedCategory.Illegal] = new[]
                    {
                        @"\b(come|tutorial|ricetta|guida)\b.{0,30}\b(fare|fabbricare|costruire|assemblare|creare)\b.{0,30}\b(bomba|esplosivo|granata|napalm|sarin)\b",
                        @"\b(bomba|esplosivo)\b.{0,30}\b(artigianal\w*|fai\s+da\s+te|casalingo)\b",
                        @"\b(metanfetamina|cocaina|eroina|fentanyl)\b.{0,20}\b(cucinare|fabbricare|sintetizzare|ricetta)\b",
                    },
                    [ProhibitedCategory.Minor] = new[]
                    {
                        @"\b(1[0-7]|[1-9])\s*ann[io]\b.{0,40}\b(sesso|scopare|cazzo|figa|tette|nud[oa])\b",
                        @"\b(scolaretta|minorenne|bambin[oa]|ragazzin[oa])\b.{0,30}\b(sesso|scopare|porno|nud[oa])\b",
                        @"\b(porno|pornografia)\s+(infantile|minorile|pedo\w*)\b", @"\bloli\b", @"\bshota\b",
                    },
                    [ProhibitedCategory.NonConsensual] = new[]
                    {
                        @"\bstupr\w*\b", @"\bviolent\w*\s+sessual\w*\b",
                        @"\b(addormentat\w*|incosciente|drogat\w*)\b.{0,40}\b(sesso|scopare|abus\w*)\b",
                        @"\bcostringere\b.{0,30}\b(sesso|scopare|succhiare)\b",
                    },
                    [ProhibitedCategory.Incest] = new[]
                    {
                        @"\b(mia|sua|tua)\s+(madre|mamma|sorella|figlia|zia|cugina)\b.{0,30}\b(scopare|sesso|cazzo|tette|nuda)\b",
                        @"\b(mio|suo|tuo)\s+(padre|papà|fratello|figlio|zio|cugino)\b.{0,30}\b(scopare|sesso|cazzo)\b",
                        @"\bincesto\b",
                    },
                    [ProhibitedCategory.Bestiality] = new[]
                    {
                        @"\b(cane|cavallo|asino|capra|maiale|animale|bestia)\b.{0,30}\b(scopare|sesso|cazzo|accoppia)\w*\b",
                        @"\bzoofil\w*\b", @"\bbestialit[àa]\b",
                    },
                    [ProhibitedCategory.Watersports] = new[]
                    {
                        @"\b(piscio|urina|pip[ìi])\b.{0,30}\b(bere|ingoiare|faccia|bocca|sesso)\b",
                        @"\bpioggia\s+dorata\b",
                    },
                    [ProhibitedCategory.SnuffViolence] = new[]
                    {
                        @"\bsnuff\b",
                        @"\b(uccidere|ammazzare|assassinare)\b.{0,30}\b(sesso|scopare|stuprare)\b",
                        @"\b(strangolare|decapitare)\b.{0,30}\b(sesso|scopare)\b",
                    },
                    [ProhibitedCategory.HypnosisSexual] = new[]
                    {
                        @"\b(forzat\w*|costrett\w*|contro\s+(sua|la\s+sua)\s+volont[àa])\b.{0,15}\b(ipno\w*|trance)\b.{0,40}\b(sesso|scopare)\b",
                    },
                    [ProhibitedCategory.Prostitution] = new[]
                    {
                        @"\b(prostituta|puttana|escort|accompagnatrice)\b.{0,15}\b(pagare|assumere|noleggiare|comprare|prenotare)\b",
                        @"\bpagare\b.{0,15}\bper\s+(il\s+)?sesso\b",
                    },
                    [ProhibitedCategory.Polygamy] = new[]
                    {
                        @"\bpoligam\w*\b.{0,20}\b(matrimonio|mogli|sposa)\b",
                        @"\b(diverse|più|molte)\s+mogli\b",
                    },
                    [ProhibitedCategory.HateSpeech] = new[]
                    {
                        @"\b(uccidere|gasare|impiccare|linciare)\s+(tutti\s+gli?\s+)?(ebrei|neri|musulmani|froci|gay|trans)\b",
                        @"\b(supremazia|potere)\s+bianc[oa]\b",
                        @"\bsporco\s+(ebreo|negro|frocio)\b",
                    },
                    [ProhibitedCategory.Deepfake] = new[]
                    {
                        @"\bdeep\s*fake\b.{0,30}\b(porno|nud[ao]|sesso)\b",
                        @"\b(celebrit[àa]|famos[oa]|persona\s+reale)\b.{0,30}\b(nud[ao]|sesso|porno)\b",
                    },
                    [ProhibitedCategory.ProfessionalAdvice] = new[]
                    {
                        @"\bdovrei\s+(prendere|smettere|iniziare)\b.{0,15}\b(farmaco|antidepressivo|insulina|chemio)\w*\b",
                        @"\bdiagnostic[ah]mi\b",
                    },
                    [ProhibitedCategory.PromptExtraction] = new[]
                    {
                        @"\b(ignora|dimentica|scarta)\b.{0,30}\b(tutt[eio]|precedent\w*|anterior\w*)\b.{0,20}\b(istruzion\w*|regol\w*|prompt|direttiv\w*)\b",
                        @"\b(ripeti|mostra|rivela|dimmi|stampa)\b.{0,30}\b(prompt|istruzion\w*|regol\w*|sistema)\b",
                        @"\b(parola\s+per\s+parola|alla\s+lettera|testualmente)\b.{0,30}\b(prompt|istruzion\w*|regol\w*)\b",
                        @"\bmodalit[àa]\s+(sviluppatore|debug|admin|jailbreak|senza\s+filtri|senza\s+censura)\b",
                    },
                },

                // ---------------- PT ----------------
                ["pt"] = new Dictionary<ProhibitedCategory, string[]>
                {
                    [ProhibitedCategory.Illegal] = new[]
                    {
                        @"\b(como|tutorial|receita|guia)\b.{0,30}\b(fazer|fabricar|construir|montar|criar)\b.{0,30}\b(bomba|explosivo|granada|napalm)\b",
                        @"\b(bomba|explosivo)s?\b.{0,30}\b(caseir[oa]|artesanal\w*|tutorial|receita)\b",
                        @"\b(metanfetamina|cocaína|heroína|fentanil)\b.{0,20}\b(cozinhar|fabricar|sintetizar|receita)\b",
                    },
                    [ProhibitedCategory.Minor] = new[]
                    {
                        @"\b(1[0-7]|[1-9])\s*anos?\b.{0,40}\b(sexo|foder|trepar|pau|buceta|peitos|pelad[ao])\b",
                        @"\b(colegial|menor|criança|pré-?adolescente)\b.{0,30}\b(sexo|foder|porno|pelad[ao])\b",
                        @"\b(porno|pornografia)\s+(infantil|de\s+menores)\b", @"\bloli\b", @"\bshota\b",
                    },
                    [ProhibitedCategory.NonConsensual] = new[]
                    {
                        @"\bestupr\w*\b",
                        @"\b(dormindo|inconsciente|drogad[ao])\b.{0,40}\b(sexo|foder|abus\w*)\b",
                        @"\b(forçar|obrigar)\b.{0,30}\b(sexo|foder|chupar)\b",
                    },
                    [ProhibitedCategory.Incest] = new[]
                    {
                        @"\b(minha|sua)\s+(mãe|mamãe|irmã|filha|tia|prima)\b.{0,30}\b(foder|trepar|sexo|pau|peitos|pelada)\b",
                        @"\b(meu|seu)\s+(pai|papai|irmão|filho|tio|primo)\b.{0,30}\b(foder|trepar|sexo|pau)\b",
                        @"\bincesto\b",
                    },
                    [ProhibitedCategory.Bestiality] = new[]
                    {
                        @"\b(cachorro|cão|cavalo|burro|cabra|porco|animal)\b.{0,30}\b(foder|sexo|pau|acasalar)\b",
                        @"\bzoofil\w*\b", @"\bbestialidade\b",
                    },
                    [ProhibitedCategory.Watersports] = new[]
                    {
                        @"\b(mijo|urina|xixi)\b.{0,30}\b(beber|engolir|rosto|boca|sexo)\b",
                        @"\bchuva\s+dourada\b",
                    },
                    [ProhibitedCategory.SnuffViolence] = new[]
                    {
                        @"\bsnuff\b",
                        @"\b(matar|assassinar)\b.{0,30}\b(sexo|foder|estuprar)\b",
                        @"\b(estrangular|decapitar)\b.{0,30}\b(sexo|foder)\b",
                    },
                    [ProhibitedCategory.HypnosisSexual] = new[]
                    {
                        @"\b(forçad[ao]|contra\s+a\s+vontade)\b.{0,15}\b(hipno\w*|transe)\b.{0,40}\b(sexo|foder)\b",
                    },
                    [ProhibitedCategory.Prostitution] = new[]
                    {
                        @"\b(prostituta|puta|garota\s+de\s+programa|acompanhante)\b.{0,15}\b(contratar|pagar|alugar|comprar)\b",
                        @"\bpagar\b.{0,15}\bpor\s+sexo\b",
                    },
                    [ProhibitedCategory.Polygamy] = new[]
                    {
                        @"\bpoligam\w*\b.{0,20}\b(casamento|esposas)\b",
                        @"\b(várias|múltiplas)\s+esposas\b",
                    },
                    [ProhibitedCategory.HateSpeech] = new[]
                    {
                        @"\b(matar|gasear|enforcar|linchar)\s+(todos\s+os\s+)?(judeus|negros|muçulmanos|gays|trans)\b",
                        @"\b(supremacia|poder)\s+branc[oa]\b",
                    },
                    [ProhibitedCategory.Deepfake] = new[]
                    {
                        @"\bdeep\s*fake\b.{0,30}\b(porno|nu[ao]|sexo|pelad[ao])\b",
                        @"\b(celebridade|famos[ao]|pessoa\s+real)\b.{0,30}\b(nu[ao]|sexo|porno|pelad[ao])\b",
                    },
                    [ProhibitedCategory.ProfessionalAdvice] = new[]
                    {
                        @"\bdevo\s+(tomar|parar|começar)\b.{0,15}\b(remédio|medicação|antidepressivo|insulina|quimio)\w*\b",
                        @"\bme\s+diagnostique\b",
                    },
                    [ProhibitedCategory.PromptExtraction] = new[]
                    {
                        @"\b(ignor[ae]|esqueç[ae]|descart[ae])\b.{0,30}\b(tod[ao]s?|anterior\w*|prévi[ao]s?)\b.{0,20}\b(instruç[ãa]\w*|regr[ao]s?|prompt|diretriz\w*)\b",
                        @"\b(repita|mostre|revele|me\s+diga|imprima|exiba)\b.{0,30}\b(prompt|instruç[ãa]\w*|regr[ao]s?|sistema)\b",
                        @"\b(palavra\s+por\s+palavra|literalmente|textualmente|verbatim)\b.{0,30}\b(prompt|instruç[ãa]\w*|regr[ao]s?)\b",
                        @"\bmodo\s+(desenvolvedor|debug|admin|jailbreak|sem\s+filtro|sem\s+censura)\b",
                    },
                },

                // ---------------- RU ----------------
                ["ru"] = new Dictionary<ProhibitedCategory, string[]>
                {
                    [ProhibitedCategory.Illegal] = new[]
                    {
                        @"\b(как|инструкция|рецепт|руководство)\b.{0,30}\b(сделать|изготовить|собрать|сварить|создать)\b.{0,30}\b(бомб\w*|взрывчатк\w*|взрывчат\w*|граната|напалм|зарин)\b",
                        @"\bсамодельн\w*\s+(бомб\w*|взрывн\w*)\b",
                        @"\b(метамфетамин|кокаин|героин|фентанил)\b.{0,20}\b(варить|изготовить|синтез\w*|рецепт)\b",
                    },
                    [ProhibitedCategory.Minor] = new[]
                    {
                        @"\b(1[0-7]|[1-9])\s*(лет|года?)\b.{0,40}\b(секс|трах\w*|член|сиськи|пизд\w*|голая?)\b",
                        @"\b(школьниц\w*|малолетк\w*|несовершеннолетн\w*|ребен\w*|дет\w*)\b.{0,30}\b(секс|трах\w*|порно|голая?)\b",
                        @"\bдетск\w*\s+порно\b", @"\bлоли\b", @"\bшота\b",
                    },
                    [ProhibitedCategory.NonConsensual] = new[]
                    {
                        @"\bизнасил\w*\b",
                        @"\b(спит|спящ\w*|без\s+сознания|накачан\w*)\b.{0,40}\b(секс|трах\w*|насил\w*)\b",
                        @"\bзастав\w*\b.{0,30}\b(секс|трах\w*|сосать)\b",
                    },
                    [ProhibitedCategory.Incest] = new[]
                    {
                        @"\b(моя|его|её|твоя)\s+(мать|мама|сестра|дочь|тётя|тетя|кузина)\b.{0,30}\b(трах\w*|секс|член|голая?|сиськи)\b",
                        @"\b(мой|его|её|твой)\s+(отец|папа|брат|сын|дядя|кузен)\b.{0,30}\b(трах\w*|секс|член)\b",
                        @"\bинцест\b",
                    },
                    [ProhibitedCategory.Bestiality] = new[]
                    {
                        @"\b(собак\w*|пёс|пес|лошад\w*|конь|осёл|осел|свинья|животн\w*)\b.{0,30}\b(трах\w*|секс|член|спарив\w*)\b",
                        @"\bзоофил\w*\b", @"\bскотолож\w*\b",
                    },
                    [ProhibitedCategory.Watersports] = new[]
                    {
                        @"\b(моча|пис\w*|урина)\b.{0,30}\b(пить|глотать|лицо|рот|секс)\b",
                        @"\bзолотой\s+дождь\b",
                    },
                    [ProhibitedCategory.SnuffViolence] = new[]
                    {
                        @"\bснафф\b",
                        @"\b(убить|убийство|зарезать)\b.{0,30}\b(секс|трах\w*|изнасил\w*)\b",
                        @"\b(задушить|обезглав\w*)\b.{0,30}\b(секс|трах\w*)\b",
                    },
                    [ProhibitedCategory.HypnosisSexual] = new[]
                    {
                        @"\b(принуд\w*|против\s+воли|насильно)\b.{0,15}\b(гипноз\w*|транс)\b.{0,40}\b(секс|трах\w*)\b",
                    },
                    [ProhibitedCategory.Prostitution] = new[]
                    {
                        @"\b(проститутк\w*|шлюх\w*|эскорт)\b.{0,15}\b(нанять|заказать|купить|снять|заплатить)\b",
                        @"\bзаплатить\b.{0,15}\bза\s+секс\b",
                    },
                    [ProhibitedCategory.Polygamy] = new[]
                    {
                        @"\bполигам\w*\b.{0,20}\b(брак|жены|жён)\b",
                        @"\b(несколько|много)\s+жён\b",
                    },
                    [ProhibitedCategory.HateSpeech] = new[]
                    {
                        @"\b(убить|вешать|линчевать|травить\s+газом)\s+(всех\s+)?(евреев|негров|мусульман|пидоров|геев|трансов)\b",
                        @"\bбелое?\s+(превосходство|превосходств\w*|сила)\b",
                        @"\bжидов\w*\b.{0,20}\b(в\s+печь|резать|убить)\b",
                    },
                    [ProhibitedCategory.Deepfake] = new[]
                    {
                        @"\bдипфейк\w*\b.{0,30}\b(порно|голая?|секс)\b",
                        @"\b(знаменитост\w*|реальн\w*\s+человек\w*)\b.{0,30}\b(голая?|секс|порно)\b",
                    },
                    [ProhibitedCategory.ProfessionalAdvice] = new[]
                    {
                        @"\bстоит\s+ли\s+мне\b.{0,15}\b(принимать|бросить|начать)\b.{0,15}\b(лекарств\w*|антидепрессант\w*|инсулин|химио\w*)\b",
                        @"\bпоставь\s+(мне\s+)?диагноз\b",
                    },
                    [ProhibitedCategory.PromptExtraction] = new[]
                    {
                        @"\b(игнорируй|забудь|отбрось|пренебреги)\b.{0,30}\b(все|предыдущ\w*|прежн\w*)\b.{0,20}\b(инструкц\w*|правил\w*|промпт|директив\w*)\b",
                        @"\b(повтори|покажи|раскрой|скажи|выведи|выдай)\b.{0,30}\b(промпт|инструкц\w*|правил\w*|систем\w*\s+сообщ\w*)\b",
                        @"\b(дословно|слово\s+в\s+слово|буквально)\b.{0,30}\b(промпт|инструкц\w*|правил\w*)\b",
                        @"\bрежим\s+(разработчика|debug|admin|jailbreak|без\s+фильтр\w*|без\s+цензур\w*)\b",
                    },
                },

                // ---------------- JA ----------------
                // Japanese has no word boundaries; we use direct substring patterns.
                ["ja"] = new Dictionary<ProhibitedCategory, string[]>
                {
                    [ProhibitedCategory.Illegal] = new[]
                    {
                        @"(爆弾|爆発物|爆薬|手榴弾|ナパーム|サリン).{0,20}(作り方|作る|製造|組み立て|レシピ|やり方|方法)",
                        @"(作り方|製造|レシピ).{0,20}(爆弾|爆発物|爆薬)",
                        @"(覚醒剤|メタンフェタミン|コカイン|ヘロイン|フェンタニル).{0,15}(作り方|製造|合成|レシピ)",
                    },
                    [ProhibitedCategory.Minor] = new[]
                    {
                        @"(1[0-7]|[1-9])\s*(歳|才).{0,30}(セックス|ファック|犯す|裸|オナニー|アへ|エロ)",
                        @"(小学生|中学生|幼児|女子小学生|JS|JC|ロリ|ショタ).{0,20}(セックス|エロ|裸|犯す|ポルノ)",
                        @"児童ポルノ", @"児童買春", @"ロリコン", @"ショタコン",
                    },
                    [ProhibitedCategory.NonConsensual] = new[]
                    {
                        @"レイプ", @"強姦", @"輪姦", @"凌辱",
                        @"(寝てる|寝ている|眠ってる|気絶|薬で|薬を盛).{0,30}(セックス|犯す|ファック|犯し)",
                        @"無理やり.{0,15}(犯す|ファック|セックス)",
                    },
                    [ProhibitedCategory.Incest] = new[]
                    {
                        @"(母さん|お母さん|姉さん|お姉|妹|娘|叔母|伯母).{0,20}(犯す|ファック|セックス|挿入|オナニー)",
                        @"(父さん|お父さん|兄さん|お兄|弟|息子|叔父|伯父).{0,20}(犯す|ファック|セックス|挿入)",
                        @"近親相姦", @"近親",
                    },
                    [ProhibitedCategory.Bestiality] = new[]
                    {
                        @"(犬|馬|豚|ヤギ|動物|獣).{0,20}(セックス|ファック|犯す|交尾)",
                        @"獣姦", @"ズーフィリア",
                    },
                    [ProhibitedCategory.Watersports] = new[]
                    {
                        @"(おしっこ|小便|ション便|尿).{0,20}(飲む|飲ませ|顔|口|セックス)",
                        @"ゴールデンシャワー", @"スカトロ",
                    },
                    [ProhibitedCategory.SnuffViolence] = new[]
                    {
                        @"スナッフ",
                        @"(殺す|殺害|惨殺).{0,20}(セックス|犯す|レイプ|強姦)",
                        @"(絞殺|斬首|首を絞め).{0,20}(セックス|犯す)",
                    },
                    [ProhibitedCategory.HypnosisSexual] = new[]
                    {
                        @"(強制|無理やり|意に反して).{0,10}(催眠|トランス).{0,30}(セックス|犯す|ファック)",
                    },
                    [ProhibitedCategory.Prostitution] = new[]
                    {
                        @"(売春婦|風俗嬢|エスコート|デリヘル).{0,15}(雇う|呼ぶ|買う|予約)",
                        @"(金|お金).{0,10}(払って|払う).{0,10}(セックス|ファック)",
                    },
                    [ProhibitedCategory.Polygamy] = new[]
                    {
                        @"一夫多妻", @"複数の妻", @"ハーレム.{0,10}(結婚|妻)",
                    },
                    [ProhibitedCategory.HateSpeech] = new[]
                    {
                        @"(殺せ|処刑|皆殺し).{0,15}(ユダヤ|黒人|朝鮮人|在日|中国人|アラブ|イスラム)",
                        @"白人至上",
                        @"(チョン|シナ人)",
                    },
                    [ProhibitedCategory.Deepfake] = new[]
                    {
                        @"ディープフェイク.{0,20}(ポルノ|裸|セックス|エロ)",
                        @"(芸能人|有名人|実在の人物).{0,20}(裸|セックス|ポルノ|エロ)",
                    },
                    [ProhibitedCategory.ProfessionalAdvice] = new[]
                    {
                        @"(薬|抗うつ薬|インスリン|抗がん剤).{0,15}(やめるべき|始めるべき|飲むべき)",
                        @"診断して",
                    },
                    [ProhibitedCategory.PromptExtraction] = new[]
                    {
                        @"(無視|忘れ|破棄).{0,15}(前の|以前の|これまでの|すべての).{0,15}(指示|ルール|プロンプト|命令)",
                        @"(教えて|表示|出力|繰り返).{0,20}(システムプロンプト|プロンプト|指示文|初期設定|ルール)",
                        @"(一字一句|そのまま|文字通り).{0,20}(プロンプト|指示|ルール)",
                        @"(開発者モード|デバッグモード|jailbreak|脱獄|フィルタなし|無検閲)",
                    },
                },

                // ---------------- KO ----------------
                ["ko"] = new Dictionary<ProhibitedCategory, string[]>
                {
                    [ProhibitedCategory.Illegal] = new[]
                    {
                        @"(폭탄|폭발물|수류탄|네이팜|사린).{0,20}(만드는\s*법|만들|제조|조립|레시피)",
                        @"(만드는\s*법|제조|레시피).{0,20}(폭탄|폭발물)",
                        @"(메스암페타민|필로폰|코카인|헤로인|펜타닐).{0,15}(만드|제조|합성|레시피)",
                    },
                    [ProhibitedCategory.Minor] = new[]
                    {
                        @"(1[0-7]|[1-9])\s*살.{0,30}(섹스|성관계|벗|자지|보지|음란)",
                        @"(초등학생|중학생|어린이|미성년).{0,20}(섹스|성관계|벗|음란|포르노)",
                        @"아동\s*포르노", @"로리", @"쇼타",
                    },
                    [ProhibitedCategory.NonConsensual] = new[]
                    {
                        @"강간", @"성폭행", @"윤간",
                        @"(자고\s*있|기절|약\s*먹).{0,30}(섹스|성관계|범하|강간)",
                        @"강제로.{0,15}(섹스|성관계|범하)",
                    },
                    [ProhibitedCategory.Incest] = new[]
                    {
                        @"(엄마|어머니|누나|언니|여동생|딸|이모|고모).{0,20}(섹스|성관계|범하|벗|음란)",
                        @"(아빠|아버지|형|오빠|남동생|아들|삼촌).{0,20}(섹스|성관계|범하)",
                        @"근친", @"근친상간",
                    },
                    [ProhibitedCategory.Bestiality] = new[]
                    {
                        @"(개|말|당나귀|돼지|염소|동물|짐승).{0,20}(섹스|성관계|교미|범하)",
                        @"수간", @"동물성애",
                    },
                    [ProhibitedCategory.Watersports] = new[]
                    {
                        @"(오줌|소변|쉬).{0,20}(마시|먹|얼굴|입|섹스)",
                        @"황금\s*샤워",
                    },
                    [ProhibitedCategory.SnuffViolence] = new[]
                    {
                        @"스너프",
                        @"(죽이|살해).{0,20}(섹스|성관계|강간)",
                        @"(목\s*조|참수).{0,20}(섹스|성관계)",
                    },
                    [ProhibitedCategory.HypnosisSexual] = new[]
                    {
                        @"(강제로|의지에\s*반).{0,10}(최면|트랜스).{0,30}(섹스|성관계)",
                    },
                    [ProhibitedCategory.Prostitution] = new[]
                    {
                        @"(매춘부|창녀|에스코트|콜걸).{0,15}(고용|부르|사|예약)",
                        @"돈\s*주고.{0,10}섹스",
                    },
                    [ProhibitedCategory.Polygamy] = new[]
                    {
                        @"일부다처", @"여러\s*아내", @"하렘.{0,10}결혼",
                    },
                    [ProhibitedCategory.HateSpeech] = new[]
                    {
                        @"(죽여|학살).{0,15}(유대인|흑인|무슬림|동성애|트랜스)",
                        @"백인\s*우월",
                    },
                    [ProhibitedCategory.Deepfake] = new[]
                    {
                        @"딥페이크.{0,20}(포르노|벗|섹스|누드)",
                        @"(연예인|유명인|실제\s*인물).{0,20}(벗|섹스|포르노|누드)",
                    },
                    [ProhibitedCategory.ProfessionalAdvice] = new[]
                    {
                        @"(약|항우울제|인슐린|항암제).{0,15}(끊어|시작해|먹어).*돼",
                        @"진단해\s*줘",
                    },
                    [ProhibitedCategory.PromptExtraction] = new[]
                    {
                        @"(무시|잊|버려).{0,15}(이전|위의|모든).{0,15}(지시|규칙|프롬프트|명령)",
                        @"(보여|알려|출력|반복).{0,20}(시스템\s*프롬프트|프롬프트|지시문|초기\s*설정|규칙)",
                        @"(그대로|글자\s*그대로|문자\s*그대로).{0,20}(프롬프트|지시|규칙)",
                        @"(개발자\s*모드|디버그\s*모드|탈옥|jailbreak|필터\s*없|무검열)",
                    },
                },

                // ---------------- ZH ----------------
                ["zh"] = new Dictionary<ProhibitedCategory, string[]>
                {
                    [ProhibitedCategory.Illegal] = new[]
                    {
                        @"(如何|怎么|怎样|教程|配方|指南).{0,15}(制造|制作|做|组装|合成).{0,15}(炸弹|炸藥|爆炸物|手榴弹|凝固汽油|沙林)",
                        @"(自制|土制).{0,10}(炸弹|爆炸物|炸藥)",
                        @"(冰毒|甲基苯丙胺|可卡因|海洛因|芬太尼).{0,15}(制造|合成|配方|做)",
                    },
                    [ProhibitedCategory.Minor] = new[]
                    {
                        @"(1[0-7]|[1-9])\s*岁.{0,30}(性|做爱|操|肏|裸|鸡巴|奶子|淫)",
                        @"(小学生|中学生|未成年|幼女|萝莉|正太).{0,20}(性|做爱|操|肏|裸|色情)",
                        @"儿童色情", @"萝莉", @"正太",
                    },
                    [ProhibitedCategory.NonConsensual] = new[]
                    {
                        @"强奸", @"强姦", @"轮奸", @"轮姦",
                        @"(睡着|昏迷|下药|迷昏).{0,30}(性|做爱|操|肏|强奸)",
                        @"强迫.{0,15}(性|做爱|操|肏|口交)",
                    },
                    [ProhibitedCategory.Incest] = new[]
                    {
                        @"(我的|他的|她的)?(妈|母亲|姐|妹|女儿|阿姨|姑姑).{0,15}(操|肏|做爱|性|裸|鸡巴)",
                        @"(我的|他的|她的)?(爸|父亲|哥|弟|儿子|叔叔|舅舅).{0,15}(操|肏|做爱|性)",
                        @"乱伦", @"亂倫",
                    },
                    [ProhibitedCategory.Bestiality] = new[]
                    {
                        @"(狗|马|驴|羊|猪|动物|畜生|兽).{0,15}(操|做爱|性|交配|肏)",
                        @"兽交", @"獸交", @"恋兽癖",
                    },
                    [ProhibitedCategory.Watersports] = new[]
                    {
                        @"(尿|小便|尿液).{0,15}(喝|吞|脸|嘴|性)",
                        @"黄金\s*浴", @"圣水",
                    },
                    [ProhibitedCategory.SnuffViolence] = new[]
                    {
                        @"虐杀",
                        @"(杀死|杀掉|谋杀).{0,15}(性|做爱|操|强奸)",
                        @"(勒死|斩首|掐死).{0,15}(性|做爱|操)",
                    },
                    [ProhibitedCategory.HypnosisSexual] = new[]
                    {
                        @"(强制|强迫|违背意愿).{0,10}(催眠|恍惚).{0,30}(性|做爱|操)",
                    },
                    [ProhibitedCategory.Prostitution] = new[]
                    {
                        @"(妓女|嫖|应召女郎|外围).{0,15}(雇|约|买|预订|付钱)",
                        @"花钱.{0,10}(嫖|做爱|性)",
                    },
                    [ProhibitedCategory.Polygamy] = new[]
                    {
                        @"一夫多妻", @"多个妻子", @"后宫.{0,10}婚",
                    },
                    [ProhibitedCategory.HateSpeech] = new[]
                    {
                        @"(杀光|杀死|绞死).{0,15}(犹太人|黑人|穆斯林|同性恋|跨性别)",
                        @"白人至上",
                    },
                    [ProhibitedCategory.Deepfake] = new[]
                    {
                        @"(深度伪造|深伪|deepfake).{0,15}(色情|裸|性)",
                        @"(明星|名人|真实人物).{0,15}(裸|性|色情)",
                    },
                    [ProhibitedCategory.ProfessionalAdvice] = new[]
                    {
                        @"(我该|该不该).{0,10}(吃|停|开始).{0,10}(药|抗抑郁药|胰岛素|化疗)",
                        @"诊断一下我",
                    },
                    [ProhibitedCategory.PromptExtraction] = new[]
                    {
                        @"(忽略|忘记|无视).{0,15}(之前|以上|所有|先前).{0,15}(指令|规则|提示|prompt)",
                        @"(显示|告诉我|输出|重复|打印).{0,15}(系统提示|提示词|指令|初始设置|规则)",
                        @"(逐字|一字不差|原文).{0,15}(提示|指令|规则)",
                        @"(开发者模式|调试模式|越狱|jailbreak|无过滤|无审查)",
                    },
                },
            };

        // Compiled cache: per language -> (category, compiled regex[]).
        private static readonly Dictionary<string, List<(ProhibitedCategory Cat, Regex[] Regs)>> Compiled =
            BuildCompiled();

        private static Dictionary<string, List<(ProhibitedCategory, Regex[])>> BuildCompiled()
        {
            var dict = new Dictionary<string, List<(ProhibitedCategory, Regex[])>>();
            foreach (var langKvp in Sets)
            {
                var perCat = new List<(ProhibitedCategory, Regex[])>();
                foreach (var catKvp in langKvp.Value)
                {
                    var arr = new Regex[catKvp.Value.Length];
                    for (int i = 0; i < catKvp.Value.Length; i++)
                        arr[i] = new Regex(catKvp.Value[i], Opts);
                    perCat.Add((catKvp.Key, arr));
                }
                dict[langKvp.Key] = perCat;
            }
            return dict;
        }

        /// <summary>
        /// Scan text against every shipped foreign-language pattern set. Returns the first
        /// hard-block on a hit. ProfessionalAdvice yields SoftHit (consistent with the
        /// English guard). Pass if no match.
        ///
        /// Severity ordering inside each language follows the same priority as the
        /// English rule list (Minor &gt; NonConsensual &gt; ... &gt; ProfessionalAdvice).
        /// </summary>
        public static ModerationResult Scan(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return ModerationResult.Pass();

            // Severity ordering — match the English guard's order so a Minor hit in any
            // language beats a PromptExtraction hit in another, etc.
            ProhibitedCategory[] priority =
            {
                ProhibitedCategory.Minor,
                ProhibitedCategory.NonConsensual,
                ProhibitedCategory.Bestiality,
                ProhibitedCategory.SnuffViolence,
                ProhibitedCategory.Incest,
                ProhibitedCategory.Illegal,
                ProhibitedCategory.HateSpeech,
                ProhibitedCategory.Deepfake,
                ProhibitedCategory.HypnosisSexual,
                ProhibitedCategory.Watersports,
                ProhibitedCategory.Prostitution,
                ProhibitedCategory.Polygamy,
                ProhibitedCategory.PromptExtraction,
                ProhibitedCategory.ProfessionalAdvice,
            };

            foreach (var cat in priority)
            {
                foreach (var langKvp in Compiled)
                {
                    foreach (var (langCat, regs) in langKvp.Value)
                    {
                        if (langCat != cat) continue;
                        foreach (var r in regs)
                        {
                            var m = r.Match(text);
                            if (m.Success)
                            {
                                var note = "fl:" + langKvp.Key + ":" + Truncate(r.ToString(), 28);
                                if (cat == ProhibitedCategory.ProfessionalAdvice)
                                    return ModerationResult.SoftHit(cat, note);
                                return ModerationResult.Block(cat, note);
                            }
                        }
                    }
                }
            }

            return ModerationResult.Pass();
        }

        private static string Truncate(string s, int n) =>
            s == null ? string.Empty : (s.Length <= n ? s : s.Substring(0, n));
    }
}
