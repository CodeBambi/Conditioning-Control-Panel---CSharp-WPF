#!/usr/bin/env node
// intra.mjs — INTRA-CLIENT citation rot detector (board row "Audit uncited intra-client source
// references and correct stale behavior claims").
//
// WHY THIS EXISTS
// The port's prose cites `File.ext:NNN` in two directions. UPSTREAM citations — into the read-only
// WPF tree — are already watched by detect.mjs and client/docs/upstream-citation-inventory.json.
// INTRA-CLIENT citations point at the port's OWN files, and nothing watched them at all: they rot
// the moment client code moves, silently, and the tree keeps compiling. Measured on this checkout
// before this file existed, the rot was already load-bearing in three named shapes — a dead
// document's section anchors cited from 28 source files, ~13 prose claims that a list is empty
// which has not been empty since the Lovense admission, and citations whose cited line no longer
// carries the member they name.
//
// WHY THIS IS A SEPARATE TOOL AND NOT A FOURTH MODE OF detect.mjs
// One reason decides it, and it is the EXIT CONTRACT. detect.mjs is a REVIEW LIST by construction:
// its header (detect.mjs:75-83) and self-test facts F10, F23 and F27 all say a populated list still
// exits 0, because a changed upstream file is not automatically a defect and a guard that cries wolf
// gets disabled. This detector must do the opposite — the board row asks it to EXIT NON-ZERO on new
// rot — and it can, because an intra-client citation is checkable against a file in the same
// repository at the same commit: a cited line either carries the member or it does not. Bolting an
// exits-non-zero mode into a file whose exit contract is pinned three times over to always-zero
// would leave one script with two contradictory contracts, and the next reader would have to guess
// which one applies to the mode they invoked.
//
// Three smaller reasons point the same way: the corpus is different (client/{src,tests,docs,tools,
// spikes} rather than client/src + client/docs), the resolution universe is different (real files in
// this checkout rather than a committed inventory), and there is NO git in this file at all — no
// window, no baseline, no endpoint, so none of detect.mjs's could-not-run conditions apply here.
//
// WHAT IS REUSED RATHER THAN RE-INVENTED, and it is deliberately everything that can be:
//   findRepoRoot()          the client/CcpClient.sln anchor, cwd-relative, hard-fails (detect.mjs:505)
//   walkFiles()             the tree walk and its SKIP_DIRS set (detect.mjs:364)
//   indexByBasename()       the basename index, sorted candidates (detect.mjs:381)
//   precedingPathPrefix()   the backwards path-prefix walk (detect.mjs:322)
//   namesFirstAttemptTree() the CCP.*/tests boundary, derived there from the shipping csproj's own
//                           DefaultItemExcludes (detect.mjs:336) — so that boundary is drawn once
//   DetectorError           the could-not-run type
// All six are imported, not copied, so a fix to any of them reaches both tools. The two that were
// module-private (walkFiles, indexByBasename) gained an `export` keyword and nothing else.
//
// THE ONE THING NOT REUSED, AND WHY. detect.mjs's CITATION_TOKEN (:295) matches `.cs` and `.xaml`
// only, because the WPF tree is a C#/XAML tree. The port cites its own `.axaml`, `.md`, `.mjs`,
// `.ps1`, `.csproj` and `.props` files by line as well, so REFERENCE below widens the extension
// alternation. The NAME half — `[A-Za-z0-9_][A-Za-z0-9_.-]*` — is character-for-character
// CITATION_TOKEN's, so the two tools agree about where a filename starts and ends, and
// `DtrhHostWindow.axaml.cs` is one token in both rather than two.
//
// THE DIRECTION SPLIT, WHICH IS THE WHOLE REASON THIS TOOL CAN BE A RED TEST
// Every reference is resolved against TWO indexes: files under client/, and files everywhere else
// in the repository (ConditioningControlPanel/, docs/, Tests/, Tools/ and the root files). A
// basename that lives only outside client/ is OUTSIDE-CLIENT and is NEVER reported — that is
// detect.mjs's territory, and reporting it here would duplicate its rows. Measured on this
// checkout: 11 basenames exist in BOTH trees, 8 of them cited, so the two directions separate
// almost completely. Those 8 are the only place a guess would be needed, and this tool does not
// guess. A citation whose basename lives in both trees is decided by the AUTHOR'S OWN TEXT, in two
// forms and no others — the PATH PREFIX (`goon/bridge.js:106`, `Helpers/RampCurves.cs:53`) and the
// port's existing `WPF`/`upstream` marker word immediately before the citation. Where the text
// chooses neither, the row is AMBIGUOUS-BASENAME and nothing is picked. The first run of this
// detector found 72 such citations, every one of which meant the WPF file; they are qualified now.
//
// THE CORPUS AND THE RESOLUTION UNIVERSE ARE NOT THE SAME SET, and the difference is client/spikes.
// References are READ from all five client roots, including the spikes. They RESOLVE against four:
// each spike is a standalone throwaway project carrying its own Program.cs, App.axaml.cs,
// MainWindow.axaml and capture.ps1, and on this checkout every within-client basename collision —
// 29 of them — was one spike shadowing a product file nobody would confuse it with. The cost is
// counted rather than hidden: a citation INTO a spike file now resolves nowhere and is never
// checked.
//
// PREFIXES ARE USED, WHERE detect.mjs DISCARDS THEM, AND THAT IS NOT AN INCONSISTENCY.
// detect.mjs keys a committed inventory by basename, so a prefix has nothing to resolve against and
// is deliberately dropped (:293-295). Here the real tree IS the index, so `Features/Dtrh/
// DtrhGate.cs:63` can be resolved by SUFFIX against the one real path that ends that way, which is
// strictly better information. The fallback matters as much as the rule: the port also writes
// SHORTHAND prefixes that were never real paths (`client/Ai/AiAwarenessService.cs` for
// `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs`), so a prefix that suffix-matches NOTHING
// falls back to the basename and is COUNTED as a shorthand rather than reported as rot. Treating a
// shorthand prefix as a missing file would have produced dozens of false rows on day one.
//
// THE FOUR CLASSES, AND EACH STATES SOMETHING CERTAINLY TRUE
//   WRONG-LINE          the file resolves, and one of three things is false about the cited span:
//                       it runs past the file's last line (arithmetic), it does not carry a SYMBOL
//                       the citation names beside it, or it does not carry a QUOTATION the
//                       citation puts beside it. The third is the one a resolving range cannot
//                       catch by itself — a sentence can be rewritten, moved 150 lines or reversed
//                       outright while `File.cs:15` still points at a line that exists.
//   UNRESOLVABLE        the citation's own prefix says `client/` and no file of that name exists
//                       anywhere in this repository. A name that resolves nowhere and names NO
//                       tree has no direction at all — 174 references on the first run were of
//                       that shape and only 12 named the client — so those are counted by name
//                       and never rowed. Guessing a direction for them is the cry-wolf shape.
//   AMBIGUOUS-BASENAME  the basename resolves at more than one path, or in both trees, and the
//                       citation's own text chooses none of them. No candidate is picked, ever.
//   DEAD-ANCHOR         a `.md` citation carrying a `§N` or `Dnnn` anchor whose target document
//                       exists but no longer contains that anchor. This is the class the
//                       wpf-surface-reachability.md deletion created: commit 1bdf998e4 cut that
//                       file from 1,906 lines to 95 and took its whole §1-§14 / D1-D325 register
//                       with it, and 28 client source files kept citing the anchors. Without this
//                       class every one of those reads as RESOLVED, because the document itself
//                       still exists — the file-level check is satisfied and the claim is still
//                       false. That is exactly the "silently unresolvable" shape the row forbids.
//
// EXIT CODE CONTRACT — THREE CODES, AND THE THIRD IS WHY THIS CAN RED AT ALL
//   0  the detector ran and found no rot.
//   1  the detector ran and found rot. Every row is printed as `citer:line: ...` so the failure is
//      actionable without opening anything.
//   2  the detector COULD NOT RUN honestly (no repo root, a fixture-source path that does not
//      exist, an unreadable corpus root). Errors go to stderr and NO report is printed.
//   Separating 1 from 2 is load-bearing here in a way it is not in detect.mjs: that tool never
//   fails on rows, so it can spend exit 1 on could-not-run. This one does, so a broken detector
//   must not be indistinguishable from a dirty tree. 75/70/127/126/130 stay clear of both, because
//   client/tools/gate/with-slot.mjs:36-42 reserves them for wrapper-only failures.
//
// PORTABILITY
//   Node 20+, zero npm dependencies, no package.json, no lockfile, no shell, NO SUBPROCESS AND NO
//   GIT. Only node:fs, node:path, node:url. Every path is normalized to forward slashes before
//   comparison. There is no wall-clock wait, no retry loop and no sleep anywhere in this file.
//
// WHAT THIS FILE DELIBERATELY DOES NOT DO — the coverage block prints a number for each
//   - It does not resolve BARE `:NNN` continuations, for the reason detect.mjs measured and
//     recorded at commit e3aee3e21: the nearest-preceding-citation-token heuristic mis-binds. The
//     count is derived and printed on every run.
//   - It does not resolve a COMMA continuation (`AiAwarenessService.cs:440,472` — the `,472` half).
//     The first line of such a reference IS checked; the rest are counted and skipped.
//   - It checks a SYMBOL only where the citation itself names one, in the one adjacency form the
//     port actually writes (see QUOTED_NEIGHBOUR), and a QUOTATION only where the citation puts a
//     double-quoted run of at least QUOTE_MIN_LENGTH characters immediately beside it (see
//     QUOTED_AFTER / QUOTED_PAREN_BEFORE). A bare `path:line` is checked for existence and range
//     and nothing more: no needle knows what a bare citation intended.
//   - The quoted check is SUPPRESSED on the documents named in LEDGER_DOCUMENTS, which record
//     dated decisions rather than describe the tree; the number it hides there is printed.
//   - It cannot see inside the two CITATION FIXTURE SOURCES, which write fake citations as test
//     data. They are excluded BY NAME, the exclusion refuses to run when one of the named paths
//     stops existing, and the number of references hidden is printed. Real citations inside those
//     two files are unwatched: that is the price, and it is stated rather than implied.
//   - A `§Named` anchor (`wpf-surface-reachability.md §Privacy`) is counted and NOT checked. Only
//     numbered `§N` / `§N.M` and `Dnnn` anchors are resolvable without judgment.
//   - It says nothing about whether a citation was CORRECT the day it was written.
//   - IT DOES NOT VERIFY A HISTORICAL MARKER. `X @ 7527243e7` says the claim is about that commit
//     rather than about the working tree, and this file runs no git, so the SHA's SHAPE is checked
//     and its content is not. That is the one escape hatch in the tool, it is why the marker must
//     be a git object name rather than a word, and the coverage block prints how many references
//     are in that state so the number cannot grow quietly. 79 carry it today, 64 of them anchors
//     into the register commit 1bdf998e4 deleted.

import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

import {
  DetectorError,
  findRepoRoot,
  indexByBasename,
  namesFirstAttemptTree,
  precedingPathPrefix,
  walkFiles,
} from "./detect.mjs";

// ------------------------------------------------------------------- vocabulary

/** The four row classes. A typed vocabulary, not free strings (precedent: detect.mjs:237). */
export const CLASS = Object.freeze({
  WRONG_LINE: "WRONG-LINE",
  DEAD_ANCHOR: "DEAD-ANCHOR",
  UNRESOLVABLE: "UNRESOLVABLE",
  AMBIGUOUS: "AMBIGUOUS-BASENAME",
});

const CLASS_ORDER = [CLASS.WRONG_LINE, CLASS.DEAD_ANCHOR, CLASS.UNRESOLVABLE, CLASS.AMBIGUOUS];

/** Sub-reasons, so a fact can assert the SPECIFIC check that tripped rather than merely that
 *  something tripped (precedent: detect.mjs:274, client/tools/verify/self-test.ps1:38-42). */
export const REASON = Object.freeze({
  PAST_END: "past the end of the file",
  SYMBOL_ABSENT: "the cited line does not carry the symbol",
  QUOTE_ABSENT: "the cited lines do not carry the quoted text",
  NO_SUCH_FILE: "no file of that name exists anywhere in this repository",
  MANY_IN_CLIENT: "the basename resolves at more than one path under client/",
  BOTH_TREES: "the basename exists under client/ AND outside it, and the citation names neither",
  ANCHOR_GONE: "the document exists but no longer carries that anchor",
});

// -------------------------------------------------------------------- the corpus

/** Where references are READ FROM. detect.mjs scans client/src + client/docs only and says so as a
 *  named blind spot (:198-205: "IT DOES NOT SEE CITATIONS INTO THE PORT'S OWN TOOLING"). The rot
 *  this tool exists for lives partly in client/tests and client/tools, so all five roots are in. */
export const CORPUS_ROOTS = ["src", "tests", "docs", "tools", "spikes"];

/** Where an intra-client citation may RESOLVE TO. Note what is missing: client/spikes/**.
 *
 *  THE SPIKES ARE READ FOR CITATIONS BUT ARE NOT A CITATION TARGET, and the reason is measured
 *  rather than aesthetic. Each spike is a standalone throwaway project with its own Program.cs,
 *  App.axaml.cs, MainWindow.axaml and capture.ps1. On this checkout EVERY within-client basename
 *  collision — all 29 of them — comes from one spike (CcpSpike.WebView) shadowing a product file
 *  nobody would confuse it with: `MainWindow.axaml:386-389` cited from RackPresentationTests means
 *  the product's rack, and saying "this could be the WebView spike's window" is noise, not review.
 *  The cost is stated and counted: a citation INTO a spike file now resolves nowhere, joins the
 *  unattributed counter, and is never checked. */
const RESOLUTION_ROOTS = ["src", "tests", "docs", "tools"];

/** Extensions of files that CARRY prose. Deliberately narrower than REFERENCE's own extension set:
 *  a .json or .csproj file can be CITED but holds no comment to cite from. */
const CORPUS_EXT = /\.(?:cs|axaml|md|mjs|ps1)$/i;

/** THE ONE BLIND SPOT THIS TOOL CHOOSES, and it is named rather than pattern-matched.
 *  A self-test for a citation detector necessarily writes FAKE citations as fixture data —
 *  `Q.cs:900-902`, `Dup.cs:77`, `CCP.Foo/Twin.cs:12`. Scanning them produces a row per fixture and
 *  nothing else, which is the cry-wolf shape that gets a guard disabled. Two paths, listed by name
 *  so the list cannot quietly grow, and runIntraDetector REFUSES TO RUN if either stops existing —
 *  an exclusion pointing at nothing is an exclusion that has silently stopped excluding. */
export const FIXTURE_SOURCES = Object.freeze([
  "client/tools/citations/self-test.mjs",
  "client/tools/citations/intra-self-test.mjs",
]);

/** Where references are RESOLVED against, outside client/. Named roots rather than a walk of the
 *  repository root, because `.claude/worktrees/` holds whole additional checkouts of this
 *  repository and walking into them would resolve a citation against another lane's tree. */
const OUTSIDE_ROOTS = ["ConditioningControlPanel", "docs", "Tests", "Tools"];

// ------------------------------------------------------------------- the patterns

/** A file-qualified citation carrying a line or line span: `Name.cs:426`, `Name.md:12-19`.
 *
 *  The NAME half is character-for-character detect.mjs's CITATION_TOKEN (:295) — same leading
 *  character class, same greedy body, same exclusion of "/" and "\" so a path-qualified citation
 *  yields its basename and precedingPathPrefix() recovers the rest. Only the EXTENSION alternation
 *  differs, and it is ordered longest-first for readability rather than for correctness (the
 *  mandatory ":" forces the backtrack either way).
 *
 *  The trailing (?!\d) stops a longer number being split, the same guard detect.mjs's
 *  BARE_LINE_REFERENCE carries (:872). */
export const REFERENCE =
  /([A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:csproj|axaml|props|json|html|mjs|sln|ps1|cs|md|js|sh|py)):(\d+)(?:-(\d+))?(?!\d)/g;

/** A BARE `:NNN` continuation — the form that names no file. Character-for-character
 *  detect.mjs:872, because the two tools must agree about what a bare reference is or their two
 *  printed counts would describe different things. Counted, never resolved. */
const BARE_LINE_REFERENCE = /(?<![A-Za-z0-9_./\\-]):(\d+)(?:-(\d+))?(?!\d)/g;

/** A COMMA continuation: the `,472` of `AiAwarenessService.cs:440,472`. Counted, never resolved —
 *  binding it would require deciding that the preceding token owns it, which is the same
 *  nearest-preceding heuristic detect.mjs measured and rejected. */
const COMMA_CONTINUATION = /(?<=:\d),(\d+)(?:-(\d+))?(?!\d)/g;

/** A markdown document named in prose, with or without a line number. The anchor scan starts here
 *  rather than from REFERENCE because the anchored form usually carries NO line:
 *  `wpf-surface-reachability.md §10 D24 @ 7527243e7` — an example this file's own detector reads,
 *  which is why it carries a real historical marker rather than a decorative one. */
const DOC_TOKEN = /([A-Za-z0-9_][A-Za-z0-9_.-]*\.md)\b/g;

/** THE ANCHOR LEAD, AND IT IS THE REFUSAL THAT KEEPS THIS CLASS HONEST.
 *  Between a document name and its anchor the port writes only markup and punctuation:
 *  `</c>`, backticks, bold stars, a paren, a comma, whitespace, and — because an XML doc comment
 *  wraps — a newline followed by `///`. Nothing else. A lead that admitted PROSE would let
 *  `capability-inventory.md:69 ... D243` bind an anchor from a different sentence, which is a
 *  false row in the one class whose rows must all be true. */
const ANCHOR_LEAD = /^(?:<\/c>|<c>|<\/?i>|<\/?b>|\/\/\/|\/\/|[`*_,;:()[\]\s])*/;

/** A numbered section anchor or a divergence-register anchor. `§Named` matches nothing here on
 *  purpose: only a numbered anchor is resolvable without judgment, and the named ones are counted. */
const ANCHOR = /^(§\s?\d+(?:\.\d+)*|D\d+\b)/;

/** How far past a document name the anchor scan may look. ANCHOR_LEAD already stops at the first
 *  prose character, so this is the second bound and not the first: it caps how much wrapped markup
 *  a single citation may span. Measured on this checkout, the widest real gap is 38 characters
 *  (`</c>` + newline + `    /// (` in a wrapped XML doc comment); 120 leaves room without letting
 *  a lead of pure punctuation run into the following paragraph. */
const ANCHOR_WINDOW = 120;

/** THE SYMBOL FORM, and it is the only one this tool claims to understand.
 *  Derived from the port's own prose rather than invented: the shape that actually occurs is a
 *  code-quoted identifier sitting immediately AFTER the citation, separated only by table pipes,
 *  commas or brackets —
 *      | `Effects/MandatoryVideoEffect.cs:416` | `RefreshSchedule()` |
 *      (`Haptics/HapticSettingsDocument.cs:49`, `Enabled`)
 *      (<c>Haptics/HapticSinkFactory.cs:42</c>, <c>AdmittedRoutes</c>)
 *  A quoted fragment that is not IDENTIFIER-SHAPED — a sentence, a code line, a quoted string — is
 *  not a symbol and is never checked. That refusal is what keeps the class's fire rate honest:
 *  a prose quotation beside a citation says nothing about which line carries it. */
const QUOTED_NEIGHBOUR =
  /^(?:`|<\/c>)?[\s,;|)\]]*(?:`([A-Za-z_][A-Za-z0-9_.]*)(?:\(\))?`|<c>([A-Za-z_][A-Za-z0-9_.]*)(?:\(\))?<\/c>)/;

/** A neighbour that is itself a FILENAME is not a symbol, and this is not a nicety: the identifier
 *  shape above admits dots, so the second half of `` `A.cs:1`, `B.cs:2` `` would otherwise be read
 *  as the symbol `B.cs` and its "member" checked as `cs`. The extension set is REFERENCE's own. */
const NEIGHBOUR_IS_A_FILENAME = /\.(?:csproj|axaml|props|json|html|mjs|sln|ps1|cs|md|js|sh|py)$/i;

// --------------------------------------------------------------- the quoted needle
//
// THE SECOND NEEDLE, AND THE ROT IT EXISTS FOR.
// QUOTED_NEIGHBOUR above checks a citation that names a SYMBOL. It refuses a quoted SENTENCE, and
// on the day it was written that refusal was right: nothing had measured whether a prose quotation
// beside a citation could be checked without crying wolf. It has now been measured, and the answer
// is that it can — 41 candidates on this checkout, and at the tolerance below every single row it
// produced was real. The rot it catches is the shape the range check structurally cannot: a
// citation whose RANGE STILL RESOLVES while the sentence it quotes has been rewritten, moved 150
// lines, or reversed outright. The clean example is `Ai/AiCommandExecutor.cs:15`, which a census
// entry cited as saying that NO effect backends exist in the greenfield client. That line now
// opens "EFFECT BACKENDS EXIST, and this comment used to say they did not" — so the citation
// resolves, sits in range, names a real file, and is false. Nothing before this check could see it.

/** How far past the cited range the quotation may be found, in lines.
 *
 *  ONE, AND THE NUMBER IS THE KNEE OF A MEASURED CURVE RATHER THAN A GUESS. Sweeping the 41
 *  candidates on this checkout over the bleed:
 *      +/-0  16 match   +/-1  26   +/-2  26   +/-3  26   +/-5  27   +/-10  27   +/-25  29   all 33
 *  The jump is entirely at the first line and then it is FLAT for two more. That shape says what
 *  the one line of slack is for: prose cites the line the quotation's SUBJECT sits on rather than
 *  the line its first word does, and a quotation of a wrapped comment routinely runs one line past
 *  the range. Both are imprecision, not rot, and firing on them is the cry-wolf shape.
 *  Everything the curve adds ABOVE one line is text that MOVED, by 4 lines to 154: the census's
 *  `App.axaml.cs:302` quotation had drifted to :323, and its `Views/Pages/StudioPage.axaml:869`
 *  one to :1023. Those are exactly the rows this check is for, so widening past one line does not
 *  buy tolerance — it buys silence. */
const QUOTE_BLEED = 1;

/** Shortest quoted run treated as a QUOTATION. Below this a `"…"` beside a citation is a
 *  scare-quoted WORD (`"open"`, `"quests"`, `"grace"`) — the author is naming a term, not claiming
 *  the cited lines carry that text, and holding them to a verbatim match reads their punctuation as
 *  a promise they did not make. The count of runs dropped by this floor is printed on every run. */
export const QUOTE_MIN_LENGTH = 12;

/** THE QUOTATION IMMEDIATELY AFTER THE CITATION, which is the form the corpus overwhelmingly
 *  writes — 40 of the 41 candidates measured. Three real ones, all of them live today:
 *      client/src/CcpClient.Desktop/Features/Intake/IntakeNiche.cs:5-6 "Greenfield has NO mod system"
 *      `Effects/BubbleCountGame.cs:132` ("the port has one surface on the primary display")
 *      Effects/BrainDrainEffect.cs:9-40 — "Brain Drain — the AUDIO half, and only the audio half"
 *
 *  THE LEAD ADMITS MARKUP, PUNCTUATION AND A WRAPPED COMMENT LEADER — NOTHING ELSE. No prose word,
 *  which is what stops a quotation in the next sentence binding to this citation. The `///` and
 *  `//` are there for the same reason ANCHOR_LEAD carries them: an XML doc comment wraps, and a
 *  citation at the end of one line with its quotation at the start of the next is one claim.
 *  AND IT ADMITS NO `"`, which is the refusal that keeps this whole check out of C# string
 *  literals: in `Assert.Equal("Foo.cs:12", "bar")` the closing quote of the first literal is the
 *  first character the lead sees, so the second literal is never read as a quotation of the file.
 *  Without that one exclusion the corpus sweep returned garbage like `") && l.Contains("`. */
const QUOTED_AFTER = /^(?:<\/c>|<\/code>|\/\/\/|\/\/|[`*_\s,;|()[\]:—-])*["“]([^"”\n]+)["”]/;

/** THE QUOTATION BEFORE A PARENTHESISED CITATION — `"the quoted text" (Foo.cs:12)` — and the
 *  parenthesis is MANDATORY, which is the whole refusal.
 *
 *  The obvious looser rule, "a quotation ending just before the citation", is wrong on this corpus
 *  and measurably so. The census writes LISTS (line numbers dropped here so this illustration is
 *  not itself a citation):
 *      IntakeNiche.cs "Greenfield has NO mod system …", IntakeMediaManifest.cs "The mod bubble
 *      sprite is null …", Companion/BarkRules.cs "The mod-override merge …"
 *  Every citation in that list has a quotation ending just before it — the PREVIOUS citation's.
 *  Two of the three bare-before candidates on this checkout were exactly that misbind, and both
 *  would have been false rows. Requiring the citation to open a parenthesis the quotation closes
 *  against costs one true candidate and removes the whole misbinding class. */
const QUOTED_PAREN_BEFORE = /["“]([^"”\n]+)["”]\s*\((?:`|<c>)?$/;

/** THE ONE THING BOTH SIDES ARE PUT THROUGH BEFORE THEY ARE COMPARED, and every rule in it was
 *  forced by a real pair that a plain substring test called rot when it was not:
 *
 *    XML doc tags      `<para><b>WPF's fourth dial, <c>BubbleCountStrictLock</c>, is ABSENT`
 *                      quoted in a doc as plain prose. Replaced by a SPACE rather than deleted, so
 *                      `</para><para>` cannot weld two words together.
 *    space before      that same space then leaves `bubblecountstrictlock , is` against a needle
 *    punctuation       reading `bubblecountstrictlock, is`. Removed after the collapse.
 *    HTML entities     an XML doc comment must write `-&gt;` for `->`.
 *    curly forms       a doc reflowed through a word processor carries ’ “ ” and en/em dashes where
 *                      the source carries ' " and -.
 *    emphasis marks    `` ` ``, `*` and `_` are markdown, not text: **marked never-runnable** is
 *                      quoted as `marked never-runnable`.
 *    comment leaders   `///`, `//`, `*`, `#`, `--`, `<!--` at the head of a wrapped line.
 *    concatenation     a C# multi-line message is `"… dead controls: attention "` + `"checks and`
 *    seams             `the strict/retry apparatus"`, and the reader quoting it never saw the
 *                      seam. Removed AFTER the whitespace collapse, when it is exactly `" + "`.
 *  Case is folded last: two live citations on this checkout quote the same comment with different
 *  capitalisation, and neither is wrong about what the file says. */
export function normaliseProse(text) {
  return text
    .replace(
      /<\/?(?:c|b|i|em|strong|para|summary|remarks|see|seealso|item|list|term|description|code|paramref|typeparamref|value|returns|exception|example|inheritdoc)\b[^>]*>/gi,
      " ",
    )
    .replace(/&gt;/g, ">")
    .replace(/&lt;/g, "<")
    .replace(/&quot;/g, '"')
    .replace(/&amp;/g, "&")
    .replace(/[‘’]/g, "'")
    .replace(/[“”]/g, '"')
    .replace(/[‐-―]/g, "-")
    .replace(/[`*_]/g, "")
    .replace(/^[ \t]*(?:\/\/\/|\/\/|\*|#|--|<!--)+[ \t]?/gm, " ")
    .replace(/\s+/g, " ")
    .replace(/" \+ "/g, "")
    .replace(/\s+([,.;:!?)\]])/g, "$1")
    .replace(/([([])\s+/g, "$1")
    .trim()
    .toLowerCase();
}

/** True when `span` still carries `needle`.
 *
 *  ELLIPSIS IS AN ELISION, NOT A CHARACTER. The port quotes long comments the way a reader quotes
 *  anything long — `"NOT ported … override, never the local rotation"` — so a needle is SPLIT at
 *  every `...` or `…` and each fragment must appear, IN ORDER. Order is the half that keeps the
 *  elision from becoming a licence: requiring only presence would let a needle match a span that
 *  carries its pieces backwards, which is not what the author quoted. */
export function quotePresent(span, needle) {
  const haystack = normaliseProse(span);
  const fragments = normaliseProse(needle)
    .split(/\s*(?:\.\.\.|…)\s*/)
    .filter((f) => f.length > 0);
  if (fragments.length === 0) return true;
  let cursor = 0;
  for (const fragment of fragments) {
    const at = haystack.indexOf(fragment, cursor);
    if (at < 0) return false;
    cursor = at + fragment.length;
  }
  return true;
}

/** The quoted run this citation claims, or null. `start`/`end` bound the citation token itself. */
export function quotedNeighbour(text, start, end) {
  const after = QUOTED_AFTER.exec(text.slice(end, end + 400));
  if (after) return after[1];
  const before = QUOTED_PAREN_BEFORE.exec(text.slice(Math.max(0, start - 400), start));
  return before ? before[1] : null;
}

/** DOCUMENTS THAT RECORD DECISIONS RATHER THAN DESCRIBE THE TREE, named rather than pattern-matched
 *  and counted rather than implied, on the same discipline as FIXTURE_SOURCES above.
 *
 *  The task board is a DATED LEDGER. Its rows carry the evidence that justified admitting a piece
 *  of work — a COORDINATOR DECISION dated 2026-08-23 admitted the XP spine because four landed
 *  features said in their own source that they computed XP and granted none, and it quoted
 *  `Features/Intake/IntakeDraft.cs` saying so (line number dropped here, so this illustration is
 *  not itself a citation). A row reaching DONE is precisely what makes that quotation false: the
 *  XP store now exists, so the source no longer says it does not. Checking a closed row's evidence
 *  against today's tree asks the wrong question of it, and answering it would mean deleting the
 *  reasoning that authorised the work.
 *
 *  THIS SUPPRESSES THE QUOTED CHECK ONLY. Every other class still watches this document in full:
 *  its citations are still resolved, still range-checked, still direction-split, and its anchors
 *  are still checked. A file that vanished from this list would take three real rows with it, so
 *  the list is verified before it is trusted and the number it hides is printed on every run. */
export const LEDGER_DOCUMENTS = Object.freeze(["client/docs/task-board.md"]);

/** THE SECOND HALF OF THE SYMBOL RULE, AND THE TWO FALSE ROWS THAT PRODUCED IT.
 *  Adjacency alone read 9 citations as symbol-bearing on this tree and TWO of them were wrong —
 *  a 22% false rate in the one class whose rows must all be true:
 *
 *    (<c>Features/Intake/IntakeParticipant.cs:61</c>, <c>DtrhUserMedia.ImagesFolder</c>)
 *        a LIST of three subjects, not a citation labelled with a member;
 *    (<c>Session/OwnedSessionEffect.cs:220</c>), <c>SessionEngine.Stop()</c> disarms every module
 *        the neighbour is the SUBJECT OF THE NEXT CLAUSE.
 *
 *  Both are DOTTED and both name a type that is not the cited file's. The true form is either a
 *  BARE member (`Enabled`, `RefreshSchedule()`) or a dotted name whose type IS the cited file's own
 *  stem (`HapticSinkFactory.AdmittedRoutes` beside `HapticSinkFactory.cs:27`). So: a dotted
 *  neighbour must name the cited file's own type, or it is a different subject and is not checked.
 *  Deleting this rule puts both false rows back, which is what fact F9's mutation shows. */
export function symbolBelongsToFile(symbol, basename) {
  const dot = symbol.lastIndexOf(".");
  if (dot < 0) return true;
  const stem = basename.slice(0, basename.indexOf("."));
  return symbol.slice(0, dot) === stem;
}

/** THE HISTORICAL MARKER — the board row's "distinguishes current source evidence from historical
 *  references", made into a property of the text rather than a judgment call at review time.
 *
 *  A reference or anchor written `X @ 7527243e7` is a claim about a COMMIT, not about the working
 *  tree, and the working tree is the only thing this tool can check. So a marked reference is
 *  COUNTED and never checked, and the count is printed on every run.
 *
 *  IT IS AN ESCAPE HATCH AND IS SHAPED SO IT CANNOT BE USED CASUALLY. The marker must be adjacent —
 *  only markup and whitespace may precede the `@` — and must be followed by a 7-to-40-character
 *  lowercase hex token, which is a git object name and nothing else in this tree's prose. A `@` in
 *  a C# verbatim string, an email address and an `@param` tag all fail that shape.
 *
 *  WHAT IT DOES NOT DO, STATED BECAUSE THE HATCH IS REAL: this file runs no git, so the SHA is not
 *  resolved and the claim at it is not verified. The marker moves a reference from "checked against
 *  the working tree" to "checked by a human against a named commit", and the coverage block prints
 *  how many references are in that state so the number cannot grow quietly. */
const HISTORICAL_MARKER = /^(?:<\/c>|<\/?i>|<\/?b>|`|\*|\)|\]|\s)*@\s*(?:<c>|`)?([0-9a-f]{7,40})\b/;

// -------------------------------------------------------------------- utilities

const toPosix = (p) => p.replace(/\\/g, "/");

/** 1-based line number of a character offset, from a precomputed newline table. */
function lineOf(lineStarts, index) {
  let lo = 0;
  let hi = lineStarts.length - 1;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (lineStarts[mid] <= index) lo = mid;
    else hi = mid - 1;
  }
  return lo + 1;
}

function lineStartsOf(text) {
  const starts = [0];
  for (let i = 0; i < text.length; i++) if (text[i] === "\n") starts.push(i + 1);
  return starts;
}

/** True when `docText` still carries `anchor`.
 *    Dnnn  — the register row is a bare token, so a word-boundary search of the whole document is
 *            the same search a reader does with Ctrl-F.
 *    §N.M  — either the literal `§N.M`, or a HEADING whose text opens with that number. The port's
 *            docs number their headings (`## 9. Secret exclusion`, `### 6.1 The surface ...`), so
 *            the heading form is what a live anchor actually looks like.
 *
 *  THE TWO-PART TAIL IS THE WHOLE CORRECTNESS OF THE NUMBER MATCH, and the first version of it was
 *  wrong. `(?![0-9.])` alone rejects `## 9. Secret exclusion` for anchor §9, because a numbered
 *  heading is FOLLOWED by a period — which made every live §N read as dead. `(?!\d)(?!\.\d)`
 *  states the two things actually meant: §9 must not match `## 90`, and §6.1 must not match
 *  `## 6.10`, while `## 9.` and `## 6.1 ` both match. */
export function anchorPresent(docText, anchor) {
  if (anchor.startsWith("D")) return new RegExp(`\\b${anchor}\\b`).test(docText);
  const number = anchor.replace(/^§\s?/, "");
  if (docText.includes(`§${number}`) || docText.includes(`§ ${number}`)) return true;
  const escaped = number.replace(/\./g, "\\.");
  if (new RegExp(`^#{1,6}\\s*(?:§\\s?)?${escaped}(?!\\d)(?!\\.\\d)`, "m").test(docText)) return true;
  // §N.M WHERE M IS A NUMBERED RULE INSIDE SECTION N, and this branch is here because leaving it
  // out produced FALSE DEAD-ANCHOR rows on the first real run. The port's contract documents
  // number their RULES rather than their sub-headings: startup-shutdown-contract.md §4.4 is rule 4
  // of "## 4. Composition-root validation rules", and async-lifecycle-fault-contract.md §5.5 is
  // rule 5 of "## 5. UI dispatch boundary". Both are live anchors and both read as dead against
  // headings alone. The search is SCOPED to section N's own body — from its heading to the next
  // heading of the same or higher level — so a `5.` list item in a different section cannot
  // satisfy §4.5.
  const parts = number.split(".");
  if (parts.length !== 2) return false;
  const heading = new RegExp(`^(#{1,6})\\s*(?:§\\s?)?${parts[0]}(?!\\d)(?!\\.\\d)`, "m").exec(docText);
  if (!heading) return false;
  const from = heading.index + heading[0].length;
  const rest = docText.slice(from);
  const next = new RegExp(`^#{1,${heading[1].length}}\\s`, "m").exec(rest);
  const body = next ? rest.slice(0, next.index) : rest;
  return new RegExp(`^\\s*${parts[1]}\\.\\s`, "m").test(body);
}

/** THE AUTHOR'S OWN WORD ON DIRECTION, TAKEN BEFORE ANY RESOLUTION AND WITHOUT LOOKING AT DISK.
 *  A citation written `ConditioningControlPanel/Services/X.cs:12` or `CCP.Avalonia/App.axaml.cs:45`
 *  says which tree it means, and it stays that citation whether or not the path is present in this
 *  checkout. That matters here and not in detect.mjs, because the FIRST-ATTEMPT TREE IS ABSENT from
 *  this checkout: `ConditioningControlPanel/CCP.*` does not exist, so first-attempt-systemic-
 *  lessons.md's `CCP.Avalonia/App.axaml.cs:45-220` would otherwise fall through to the port's own
 *  App.axaml.cs by basename and be range-checked against the wrong file. The first-attempt half of
 *  the rule is detect.mjs's own namesFirstAttemptTree() (:336), derived there from the shipping
 *  csproj's DefaultItemExcludes, so the two tools draw that boundary in exactly one place. */
export function namesOtherTree(prefix) {
  if (!prefix) return false;
  return prefix.startsWith("ConditioningControlPanel/") || namesFirstAttemptTree(prefix);
}

/** THE PORT'S OWN WORD FOR "THIS ONE IS UPSTREAM", and it is a convention this tree already writes
 *  rather than a heuristic invented here: `// H1 (WPF Services/AIService/AiTextHygiene.cs:25-28)`,
 *  `upstream GoonHostService.cs:311-354`. Where a basename exists in BOTH trees, that word is the
 *  only thing distinguishing the port's own AiTextHygiene.cs from the one it was ported from.
 *
 *  TIGHT ON PURPOSE, AND THE TIGHTNESS IS THE REFUSAL. The marker must be the LAST word before the
 *  citation, with nothing between them but whitespace and code markup. Anything looser matches
 *  `the port's OWN trigger: AiAwarenessService.cs:229` — a sentence that mentions upstream while
 *  citing the port — and a mis-marked reference leaves this tool silently.
 *
 *  THE ERROR DIRECTION IS CHOSEN, NOT ACCIDENTAL. A false marker moves a reference OUT of this
 *  tool's territory: the cost is a missed check, never a false row. A missing marker leaves it IN,
 *  and the citation is reported as AMBIGUOUS-BASENAME, which is a row a human can answer. */
const UPSTREAM_MARKER = /(?:\bWPF|\b[Uu]pstream)(?:'s)?(?:\s|`|<c>|\(|\[)*$/;

export function namesUpstreamInProse(textBefore) {
  return UPSTREAM_MARKER.test(textBefore);
}

/** Candidate real paths for a citation, using the author's own prefix before the basename.
 *
 *  1. SUFFIX match on the written path (prefix + basename) against every candidate. One survivor
 *     wins, and that is how `Features/Dtrh/DtrhGate.cs` and `Views/Pages/StudioPage.axaml` resolve
 *     without ambiguity even where a basename repeats. `chosen` says the AUTHOR chose, not the tool.
 *  2. If the suffix match survives NOTHING — including when no prefix was written at all — the
 *     author chose nothing. Fall back to every basename candidate with `chosen: false`. The caller
 *     decides what an unchosen set means; nothing here picks one.
 *  No candidate is ever PICKED from a set larger than one: that decision belongs to the author. */
export function resolveCandidates(candidates, prefix, basename) {
  if (!prefix) return { candidates, chosen: false };
  const written = `${prefix}${basename}`;
  const matched = candidates.filter((c) => c === written || c.endsWith(`/${written}`));
  if (matched.length > 0) return { candidates: matched, chosen: true };
  return { candidates, chosen: false };
}

// ------------------------------------------------------------------- the core

/** Pure core: takes a repo root, returns {rows, summary}, PRINTS NOTHING.
 *  Same transposition as detect.mjs's runDetector (:525), so fixtures drive every branch. */
export function runIntraDetector({ repoRoot } = {}) {
  if (!repoRoot) throw new DetectorError("runIntraDetector requires a repoRoot");
  const root = toPosix(path.resolve(repoRoot));

  const clientRoot = path.join(root, "client");
  if (!fs.existsSync(clientRoot)) {
    throw new DetectorError(`client/ is absent under ${root} — there are no intra-client citations to check`);
  }

  // --- 1. the two universes. client/ is the subject; everything else is the other tool's.
  const clientFiles = [];
  for (const name of RESOLUTION_ROOTS) {
    clientFiles.push(...walkFiles(path.join(clientRoot, name), () => true).map((p) => p.slice(root.length + 1)));
  }
  for (const entry of fs.readdirSync(clientRoot, { withFileTypes: true })) {
    if (entry.isFile()) clientFiles.push(`client/${entry.name}`);
  }
  const outsideFiles = [];
  for (const name of OUTSIDE_ROOTS) {
    outsideFiles.push(...walkFiles(path.join(root, name), () => true).map((p) => p.slice(root.length + 1)));
  }
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    if (entry.isFile()) outsideFiles.push(entry.name);
  }
  const clientIndex = indexByBasename(clientFiles);
  const outsideIndex = indexByBasename(outsideFiles);

  // --- 2. the two exclusions, each verified before it is trusted. An exclusion pointing at nothing
  //        has silently stopped excluding, and a detector that cannot say what it skipped cannot
  //        say what it checked.
  const verifiedExclusion = (paths, what, constant) => {
    const set = new Set();
    for (const rel of paths) {
      if (!fs.existsSync(path.join(root, rel))) {
        throw new DetectorError(
          `the ${what} names ${rel}, which does not exist under ${root}. An exclusion pointing at ` +
            `nothing has silently stopped excluding. Update ${constant}. No report was printed.`,
        );
      }
      set.add(rel);
    }
    return set;
  };
  const fixtureSources = verifiedExclusion(FIXTURE_SOURCES, "citation-fixture exclusion", "FIXTURE_SOURCES");
  const ledgerDocuments = verifiedExclusion(LEDGER_DOCUMENTS, "decision-ledger exclusion", "LEDGER_DOCUMENTS");

  // --- 3. the corpus
  const corpus = [];
  for (const name of CORPUS_ROOTS) {
    corpus.push(...walkFiles(path.join(clientRoot, name), (n) => CORPUS_EXT.test(n)).map((p) => p.slice(root.length + 1)));
  }
  corpus.sort();

  const rows = [];
  const targetCache = new Map(); // client-relative path -> {lines, text}
  const readTarget = (rel) => {
    if (!targetCache.has(rel)) {
      let text = "";
      try {
        text = fs.readFileSync(path.join(root, rel), "utf8");
      } catch {
        text = "";
      }
      targetCache.set(rel, { text, lines: text.split("\n") });
    }
    return targetCache.get(rel);
  };
  /** A file's last REAL line. A trailing newline makes split("\n") produce one empty tail element,
   *  so the raw length would let a citation one line past the end read as in-range. */
  const lastLineOf = (rel) => {
    const { lines } = readTarget(rel);
    return lines.length > 0 && lines[lines.length - 1] === "" ? lines.length - 1 : lines.length;
  };
  const withLineCounts = (paths) => paths.map((p) => ({ path: p, lines: lastLineOf(p) }));

  const counts = {
    corpusFiles: 0,
    fixtureFilesSkipped: 0,
    fixtureReferencesHidden: 0,
    references: 0,
    outsideClient: 0,
    intra: 0,
    resolved: 0,
    symbolChecked: 0,
    quotesChecked: 0,
    quotesTooShort: 0,
    quotesInLedger: 0,
    bareCitations: 0,
    shorthandPrefixes: 0,
    bareContinuations: 0,
    commaContinuations: 0,
    anchorsFound: 0,
    anchorsChecked: 0,
    anchorsOutsideClient: 0,
    namedAnchorsNotChecked: 0,
    unattributed: 0,
    upstreamByMarker: 0,
    historical: 0,
  };
  const unattributedNames = new Set();

  for (const rel of corpus) {
    const isFixture = fixtureSources.has(rel);
    let text;
    try {
      text = fs.readFileSync(path.join(root, rel), "utf8");
    } catch {
      continue; // a file that vanished mid-scan is not a reason to lie about the rest
    }
    if (isFixture) {
      counts.fixtureFilesSkipped += 1;
      for (const _m of text.matchAll(REFERENCE)) counts.fixtureReferencesHidden += 1;
      continue;
    }
    counts.corpusFiles += 1;
    const lineStarts = lineStartsOf(text);

    for (const _m of text.matchAll(BARE_LINE_REFERENCE)) counts.bareContinuations += 1;
    for (const _m of text.matchAll(COMMA_CONTINUATION)) counts.commaContinuations += 1;

    // --- 3a. file:line references
    for (const m of text.matchAll(REFERENCE)) {
      counts.references += 1;
      const basename = m[1];
      const from = Number(m[2]);
      const to = m[3] ? Number(m[3]) : from;
      const at = `${rel}:${lineOf(lineStarts, m.index)}`;
      const prefix = precedingPathPrefix(text, m.index);
      // The historical marker is read BEFORE the direction split, deliberately: a reference into a
      // named commit is not a claim about either tree's working copy, so there is nothing for the
      // split to decide.
      const marked = HISTORICAL_MARKER.exec(text.slice(m.index + m[0].length, m.index + m[0].length + 60));
      if (marked) {
        counts.historical += 1;
        continue;
      }
      if (namesOtherTree(prefix)) {
        counts.outsideClient += 1;
        continue;
      }
      const inClient = clientIndex.get(basename) ?? [];
      const outside = outsideIndex.get(basename) ?? [];

      if (inClient.length === 0 && outside.length === 0) {
        // A NAME THAT RESOLVES NOWHERE HAS NO DIRECTION, AND GUESSING ONE IS THE CRY-WOLF SHAPE.
        // Measured on this checkout, 174 references resolve nowhere and only 12 of them name the
        // client tree in their own text; the rest are upstream files that were deleted or renamed
        // (Speech.cs, Presets.cs, Patreon.cs), the abandoned first-attempt tree
        // (CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs), third-party source read during a
        // spike (src/Avalonia.Controls/TopLevel.cs), and a tool's own documentation of its regex
        // (`Name.cs:426`). Not one of those is an intra-client claim, and detect.mjs already counts
        // the upstream half in its `dropped ... resolve nowhere` line. So a row is emitted ONLY when
        // the citation's own prefix says `client/`: then it is certainly a claim about a port file,
        // and that file is certainly not there. Everything else is counted by name and printed.
        if (prefix.startsWith("client/")) {
          rows.push({
            cls: CLASS.UNRESOLVABLE,
            at,
            cited: `${prefix}${basename}:${m[2]}${m[3] ? `-${m[3]}` : ""}`,
            reason: REASON.NO_SUCH_FILE,
            reads: null,
            action: "the file this claim rests on is gone or was never here — re-cite it or retire the claim",
          });
        } else {
          counts.unattributed += 1;
          unattributedNames.add(basename);
        }
        continue;
      }

      if (inClient.length === 0) {
        // OUTSIDE-CLIENT: detect.mjs's territory. NEVER a row here.
        counts.outsideClient += 1;
        continue;
      }
      const clientPick = resolveCandidates(inClient, prefix, basename);
      if (outside.length > 0) {
        // The basename lives in BOTH trees, so only the author's own text can say which was meant.
        // Whichever tree it chose — by path prefix or by the port's own `WPF`/`upstream` marker —
        // the tool honours; when it chose neither, the tool says so and picks nothing. That was the
        // shape of the port's unqualified `bridge.js` and `AiTextHygiene` citations before this
        // detector: each named a real client file AND a real WPF file, and each meant the WPF one.
        // `bridge.js` was the worst of them — FOUR real files answer to that basename.
        const outsidePick = resolveCandidates(outside, prefix, basename);
        if (outsidePick.chosen && !clientPick.chosen) {
          counts.outsideClient += 1;
          continue;
        }
        if (!clientPick.chosen && namesUpstreamInProse(text.slice(Math.max(0, m.index - 40), m.index))) {
          counts.outsideClient += 1;
          counts.upstreamByMarker += 1;
          continue;
        }
        if (!clientPick.chosen) {
          counts.intra += 1;
          rows.push({
            cls: CLASS.AMBIGUOUS,
            at,
            cited: `${prefix}${basename}:${m[2]}${m[3] ? `-${m[3]}` : ""}`,
            reason: REASON.BOTH_TREES,
            reads: null,
            candidates: withLineCounts([...inClient, ...outside].sort()),
            action: "write the real path this cites; the basename alone cannot say which tree it means",
          });
          continue;
        }
      }
      counts.intra += 1;
      if (prefix && !clientPick.chosen) counts.shorthandPrefixes += 1;
      if (clientPick.candidates.length > 1) {
        rows.push({
          cls: CLASS.AMBIGUOUS,
          at,
          cited: `${prefix}${basename}:${m[2]}${m[3] ? `-${m[3]}` : ""}`,
          reason: REASON.MANY_IN_CLIENT,
          reads: null,
          candidates: withLineCounts(clientPick.candidates),
          action: "write enough of the real path to choose one; no candidate was picked",
        });
        continue;
      }

      const target = clientPick.candidates[0];
      const { lines } = readTarget(target);
      const lastLine = lastLineOf(target);
      if (to > lastLine) {
        rows.push({
          cls: CLASS.WRONG_LINE,
          at,
          cited: `${target}:${m[2]}${m[3] ? `-${m[3]}` : ""}`,
          reason: `${REASON.PAST_END} (${lastLine} lines)`,
          reads: null,
          action: "the file shrank under this citation; re-read it and re-cite, or retire the claim",
        });
        continue;
      }

      // --- the QUOTATION beside the citation. Run before the symbol check because it is the more
      // specific claim: a quotation says which WORDS the cited lines carry, where a symbol says
      // only which NAME they carry. The two adjacency forms are mutually exclusive on the trailing
      // side (a symbol is backtick- or <c>-quoted, a quotation is double-quoted), so a citation
      // reaching both needles carried its quotation on the LEADING side, and both are checked.
      let checkedANeedle = false;
      const quoted = quotedNeighbour(text, m.index, m.index + m[0].length);
      if (quoted !== null) {
        if (ledgerDocuments.has(rel)) {
          counts.quotesInLedger += 1;
        } else if (quoted.trim().length < QUOTE_MIN_LENGTH) {
          counts.quotesTooShort += 1;
        } else {
          counts.quotesChecked += 1;
          checkedANeedle = true;
          const lo = Math.max(0, from - 1 - QUOTE_BLEED);
          const hi = Math.min(lines.length, to + QUOTE_BLEED);
          if (!quotePresent(lines.slice(lo, hi).join("\n"), quoted)) {
            const shown = quoted.trim();
            rows.push({
              cls: CLASS.WRONG_LINE,
              at,
              cited:
                `${target}:${m[2]}${m[3] ? `-${m[3]}` : ""} quoting ` +
                JSON.stringify(shown.length > 110 ? `${shown.slice(0, 110)}…` : shown),
              reason: `${REASON.QUOTE_ABSENT} (searched ${lo + 1}-${hi})`,
              reads: lines[from - 1] ?? "",
              action:
                "the sentence this claim quotes is not there any more — re-read the file and quote what it " +
                "says now, or keep the old words and mark the citation historical (`@ <sha>`)",
            });
            continue;
          }
        }
      }

      const neighbour = QUOTED_NEIGHBOUR.exec(text.slice(m.index + m[0].length, m.index + m[0].length + 80));
      const named = neighbour ? (neighbour[1] ?? neighbour[2]) : null;
      const symbol =
        named !== null && (NEIGHBOUR_IS_A_FILENAME.test(named) || !symbolBelongsToFile(named, basename))
          ? null
          : named;
      if (symbol === null) {
        if (!checkedANeedle) counts.bareCitations += 1;
        counts.resolved += 1;
        continue;
      }
      counts.symbolChecked += 1;
      const member = symbol.slice(symbol.lastIndexOf(".") + 1);
      const span = lines.slice(from - 1, to).join("\n");
      if (new RegExp(`\\b${member}\\b`).test(span)) {
        counts.resolved += 1;
        continue;
      }
      rows.push({
        cls: CLASS.WRONG_LINE,
        at,
        cited: `${target}:${m[2]}${m[3] ? `-${m[3]}` : ""} as \`${symbol}\``,
        reason: REASON.SYMBOL_ABSENT,
        reads: lines[from - 1] ?? "",
        action: "the subject moved; re-read the file and re-cite the line that carries it",
      });
    }

    // --- 3b. document anchors
    for (const m of text.matchAll(DOC_TOKEN)) {
      const basename = m[1];
      const prefix = precedingPathPrefix(text, m.index);
      const at = `${rel}:${lineOf(lineStarts, m.index)}`;
      // Anchors are COLLECTED first, then judged. `§10 D24` is ONE citation carrying two anchors,
      // and a historical marker written once at its end covers both — so the marker has to be read
      // after the whole chain rather than between its links.
      const anchors = [];
      let cursor = m.index + m[0].length;
      const limit = cursor + ANCHOR_WINDOW;
      for (;;) {
        const window = text.slice(cursor, limit);
        const lead = ANCHOR_LEAD.exec(window)[0];
        const found = ANCHOR.exec(window.slice(lead.length));
        if (!found) break;
        cursor += lead.length + found[0].length;
        anchors.push(found[0].replace(/§\s/, "§"));
      }
      const markedAnchors = anchors.length > 0 && HISTORICAL_MARKER.test(text.slice(cursor, cursor + 60));
      for (const anchor of anchors) {
        counts.anchorsFound += 1;
        if (markedAnchors) {
          counts.historical += 1;
          continue;
        }
        const inClient = clientIndex.get(basename) ?? [];
        if (inClient.length === 0) {
          counts.anchorsOutsideClient += 1;
          continue;
        }
        const resolved = resolveCandidates(inClient, prefix, basename);
        if (resolved.candidates.length !== 1) continue; // AMBIGUOUS-BASENAME already owns this shape
        counts.anchorsChecked += 1;
        const doc = readTarget(resolved.candidates[0]);
        if (anchorPresent(doc.text, anchor)) continue;
        rows.push({
          cls: CLASS.DEAD_ANCHOR,
          at,
          cited: `${resolved.candidates[0]} ${anchor}`,
          reason: REASON.ANCHOR_GONE,
          reads: null,
          action:
            "the section or divergence row this claim rests on was deleted from the document — " +
            "recover it from history and cite it as historical (`@ <sha>`), or retire the claim",
        });
      }
      // A NAMED section anchor (`§Privacy`) is countable but not resolvable without judgment.
      const tail = text.slice(m.index + m[0].length, m.index + m[0].length + ANCHOR_WINDOW);
      const namedLead = ANCHOR_LEAD.exec(tail)[0];
      if (/^§\s?[A-Za-z]/.test(tail.slice(namedLead.length))) counts.namedAnchorsNotChecked += 1;
    }
  }

  // Deterministic output: two runs are byte-identical and pasteable into a ledger.
  rows.sort((a, b) => {
    const ci = CLASS_ORDER.indexOf(a.cls) - CLASS_ORDER.indexOf(b.cls);
    if (ci !== 0) return ci;
    if (a.at !== b.at) return a.at < b.at ? -1 : 1;
    return a.cited < b.cited ? -1 : a.cited > b.cited ? 1 : 0;
  });

  return {
    rows,
    summary: {
      byClass: Object.fromEntries(CLASS_ORDER.map((c) => [c, rows.filter((r) => r.cls === c).length])),
      counts,
      unattributedNames: [...unattributedNames].sort(),
      totalRows: rows.length,
    },
  };
}

// -------------------------------------------------------------------- reporting

/** The coverage block. Every figure is DERIVED by the run that prints it. An unstated coverage gap
 *  is how a red test becomes a false reassurance, so the gap is printed as numbers, not implied
 *  (the discipline detect.mjs's formatNeedleCoverage sets). */
export function formatIntraCoverage(summary) {
  const c = summary.counts;
  return [
    "## INTRA-CLIENT COVERAGE",
    `  corpus: ${c.corpusFiles} files under client/{${CORPUS_ROOTS.join(",")}} carrying ` +
      `${c.references} file-qualified references with a line`,
    `  direction: ${c.intra} intra-client, ${c.outsideClient} outside client/ — the latter are ` +
      `NEVER reported here; upstream citations are detect.mjs's territory and reporting them twice ` +
      `is how a review list gets skimmed instead of read`,
    `  intra-client checked: ${c.resolved} resolved clean — ${c.symbolChecked} had a symbol beside ` +
      `them and were checked against the cited line, ${c.quotesChecked} had a QUOTATION beside them ` +
      `and were checked against the cited lines +/-${QUOTE_BLEED}, ${c.bareCitations} were bare ` +
      `path:line and were checked for EXISTENCE AND RANGE ONLY. A bare citation says nothing this ` +
      `tool can verify about what the line means.`,
    `  quoted runs NOT checked: ${c.quotesTooShort} shorter than ${QUOTE_MIN_LENGTH} characters — a ` +
      `scare-quoted word ("open", "quests") names a term rather than claiming the cited lines carry ` +
      `that text — and ${c.quotesInLedger} in a decision ledger (${LEDGER_DOCUMENTS.join(", ")}), ` +
      `whose rows quote the evidence that justified admitting work and are made false BY that work ` +
      `landing. Every other class still watches those documents in full.`,
    `  document anchors: ${c.anchorsFound} found, ${c.anchorsChecked} checked against a client ` +
      `document, ${c.anchorsOutsideClient} into a document outside client/ (not checked), ` +
      `${c.namedAnchorsNotChecked} named rather than numbered (§Privacy — not resolvable without judgment)`,
    `  references resolving NOWHERE and naming no tree: ${c.unattributed} across ` +
      `${summary.unattributedNames.length} distinct name(s) — counted, never rowed, because a name ` +
      `that resolves nowhere has no direction and guessing one is how a guard gets disabled. ` +
      `detect.mjs's "dropped ... resolve nowhere" line counts the upstream half of the same set: ` +
      `${summary.unattributedNames.join(", ")}`,
    `  references and anchors marked HISTORICAL (\`X @ <sha>\`): ${c.historical} — a claim about a ` +
      `named commit, not about the working tree, so it is counted and NEVER checked. This tool runs ` +
      `no git: the SHA's shape is checked, its content is not. That is the escape hatch, and this ` +
      `number is how it stays visible.`,
    `  colliding basenames sent upstream by the port's own \`WPF\`/\`upstream\` marker: ` +
      `${c.upstreamByMarker} — the marker is the last word before the citation, and honouring it is ` +
      `the same rule as honouring a path prefix: the author said which tree.`,
    `  shorthand path prefixes honoured as basenames: ${c.shorthandPrefixes} — a written prefix that ` +
      `matched no real path, so the basename decided. Treating those as missing files would be dozens ` +
      `of false rows.`,
    `  NOT CHECKED, and counted rather than implied away: ${c.bareContinuations} bare :NNN ` +
      `continuations and ${c.commaContinuations} comma continuations (\`Foo.cs:440,472\` — the ,472 ` +
      `half). Binding either needs the nearest-preceding-token heuristic detect.mjs measured and ` +
      `rejected at commit e3aee3e21.`,
    `  hidden by the citation-fixture exclusion: ${c.fixtureReferencesHidden} references in ` +
      `${c.fixtureFilesSkipped} file(s) — ${FIXTURE_SOURCES.join(", ")}. Those files write fake ` +
      `citations as test data; their REAL citations are unwatched, and that is the price.`,
    "  also not checked: whether a citation was correct the day it was written, and any reference " +
      "whose extension names no file in this repository at all (Win32 SDK headers, IP addresses).",
  ].join("\n");
}

export function formatIntraReport(outcome) {
  const { rows, summary } = outcome;
  const out = [];
  out.push("INTRA-CLIENT CITATION ROT");
  out.push("");
  for (const cls of CLASS_ORDER) {
    const group = rows.filter((r) => r.cls === cls);
    out.push(`## ${cls} (${group.length})`);
    if (group.length === 0) out.push("  (none)");
    for (const row of group) {
      // THE ACTIONABLE LINE. `citer:line: cited X but ...` — everything needed to fix it without
      // opening a file, which is what the board row asks a failure to print.
      out.push(
        `  ${row.at}: cited ${row.cited} but ${row.reads === null ? row.reason : `the line reads ${JSON.stringify(row.reads.trim())}`}`,
      );
      if (row.reads !== null) out.push(`      reason: ${row.reason}`);
      // Each candidate carries its LINE COUNT, because that is usually what settles which tree a
      // citation meant: a span past one candidate's end can only have meant the other.
      if (row.candidates) for (const c of row.candidates) out.push(`      candidate: ${c.path} (${c.lines} lines)`);
      out.push(`      action: ${row.action}`);
    }
    out.push("");
  }
  out.push("## SUMMARY");
  for (const cls of CLASS_ORDER) out.push(`  ${cls}: ${summary.byClass[cls]}`);
  out.push(`  TOTAL ROWS: ${summary.totalRows}`);
  out.push("");
  out.push(formatIntraCoverage(summary));
  out.push("");
  out.push(
    summary.totalRows === 0
      ? "No intra-client citation rot. Exit 0."
      : "Every row above is a claim this repository no longer supports. Exit 1.",
  );
  return out.join("\n");
}

// -------------------------------------------------------------------------- CLI

const USAGE = [
  "usage: node client/tools/citations/intra.mjs [--out <path>]",
  "",
  "  Checks every INTRA-CLIENT `File.ext:NNN` reference and every `§N`/`Dnnn` document anchor in",
  "  client/{src,tests,docs,tools,spikes} against the files they name in THIS checkout.",
  "  Upstream citations are not reported: that is client/tools/citations/detect.mjs's territory.",
  "",
  "  A reference or anchor that is deliberately about a PAST state carries the commit it was true",
  "  at — `wpf-surface-reachability.md D24 @ 7527243e7`, `port-lessons.md:52 @ a8d32c219`. Those are",
  "  COUNTED and never checked: the working tree cannot answer a question asked of a named commit.",
  "  The marker must be a 7-to-40-character hex object name, so it cannot be written casually.",
  "",
  "  --out writes the same report to a file as well (opt-in; nothing is written otherwise).",
  "",
  "  Exit 0: no rot.",
  "  Exit 1: rot found, one actionable line per row.",
  "  Exit 2: the detector could not run honestly; the reason is named on stderr and NO report is",
  "          printed. Separate from 1 so a broken detector never reads as a clean tree, or as a",
  "          dirty one.",
].join("\n");

export function main(argv = [], cwd = process.cwd()) {
  let out;
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const eq = arg.indexOf("=");
    const flag = arg.startsWith("--") && eq > 0 ? arg.slice(0, eq) : arg;
    if (flag === "-h" || flag === "--help") {
      process.stdout.write(`${USAGE}\n`);
      return 0;
    }
    if (flag !== "--out") {
      process.stderr.write(`intra-citations: unknown option ${JSON.stringify(arg)}\n${USAGE}\n`);
      return 2;
    }
    if (eq > 0) out = arg.slice(eq + 1);
    else if (i + 1 < argv.length) out = argv[++i];
    else {
      process.stderr.write(`intra-citations: --out needs a value\n${USAGE}\n`);
      return 2;
    }
  }

  let outcome;
  try {
    outcome = runIntraDetector({ repoRoot: findRepoRoot(cwd) });
  } catch (err) {
    if (err instanceof DetectorError) {
      // NO report on a broken input, ever, and exit 2 rather than 1 so it cannot be mistaken for rot.
      process.stderr.write(`intra-citations: COULD NOT RUN HONESTLY\n  ${err.message}\n`);
      return 2;
    }
    throw err;
  }
  const report = formatIntraReport(outcome);
  process.stdout.write(`${report}\n`);
  if (out) {
    fs.mkdirSync(path.dirname(path.resolve(out)), { recursive: true });
    fs.writeFileSync(path.resolve(out), `${report}\n`, "utf8");
  }
  return outcome.rows.length === 0 ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  // process.exitCode, NEVER process.exit(). On POSIX, node's stdout is ASYNCHRONOUS when it is a
  // PIPE (and synchronous when it is a file or a TTY), so process.exit() discards whatever is still
  // buffered. This report is ~975 lines; piped on Linux it lost its tail - including the TOTAL ROWS
  // line - while the identical run redirected to a FILE kept everything, which is why it looked like
  // a reader bug rather than a writer one. Windows never showed it: pipe writes are synchronous there.
  // Setting exitCode lets node drain and exit on its own, with the same status.
  process.exitCode = main(process.argv.slice(2), process.cwd());
}
