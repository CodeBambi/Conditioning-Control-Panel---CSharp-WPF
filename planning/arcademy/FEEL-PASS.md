# Arcademy: four games worth touching again

Owner authorized implementation on 2026-09-22. This brief supersedes the old mechanics, mandatory full-bell duration, and distraction-as-difficulty rules for these four replacements. The first deliverable is four playable slices in a local preview. Production integration follows the owner's feel checkpoint. The Annex change remains separate in PR #1542.

## What feel means here

Feel is the connection between intent, input and believable response. Juice amplifies that response so actions have weight and success is pleasurable. It must communicate what happened. It cannot cover up shallow decisions or conceal information needed to play.

Sources: [Designing Game Feel](https://arxiv.org/abs/2011.09201) describes physicality, amplification and support. [Juice It or Lose It](https://www.gdcvault.com/play/1016789/Juice-It-or-Lose) demonstrates layered audiovisual response. The timings below are initial tuning values, not research findings or measured guarantees.

## Shared feel contract

- A press changes its target on the next rendered frame. No delayed click animation. Desktop keyboard and touch get equivalent feedback. All important controls are at least 48 CSS px.
- Use three strengths: touch acknowledgement, successful action, finale. Save the strongest response for earned moments. No continuous screen shake or particle blanket.
- Initial palette: press compression 70 ms, release spring 160 ms, successful reveal 280-420 ms, finale 900-1400 ms. Input stays available wherever the rules allow it. Animate child faces, never move hitboxes under a finger.
- Generous particles originate at the action, have a purpose and die quickly. Bound simultaneous particles and audio voices. Transform/opacity animation preferred. No unbounded timers or allocations in frame loops.
- Tactile sound: short attack, layered body, restrained tail. Small variation prevents identical repeated clicks. Musical layers share a key. Player input sounds immediately; accompaniment uses the audio clock.
- Reduced motion removes shake, travel and bursts while keeping outlines, labels, progress and success contrast. Silent play retains every rule and timing cue. Haptics are optional and never the only signal.
- Pause freezes play and sound. Returning from a hidden page never advances the clock into a miss. Destroy removes listeners, timers, animation frames and audio. No catch-up physics explosion.
- Each slice teaches through one short instruction and a forgiving first action. First satisfying action within about 10 seconds. Natural ending near 60-90 seconds, with no forced filler to reach a bell.
- Mistakes explain themselves and preserve momentum. No red punishment wall, loss of a long run, or punishment for taking time to understand.
- First milestone uses offline original vector art. Real media belongs behind the existing provider, with loaded-only targets, whole-path identity, and a readable fallback. No credentials, private records, external telemetry, or new paid assets.

## Instant Recall: catch the room lying

Watch three large distinct pictures. A replay swaps one. Tap the changed picture. Six rounds, gentle variation, no peripheral-event questions and no speed requirement for a perfect grade.

Feel sequence: picture arrives with a soft shutter; selected answer compresses immediately; a correct false image splits into fragments and its original snaps back; a short resolving pair of notes confirms the repair. Wrong answers show original versus changed side by side, then allow the next round. A strip of restored pictures builds toward a complete final mosaic. The final successful repair restores the whole room in a restrained light sweep.

First slice: replacement only, three choices, six rounds, truthful evidence, recoverable errors, start/finish lifecycle. Later: explicitly introduced order changes and curated visual families. No hidden novelty rules.

## Composure: make it stand

Build a six-piece sculpture on a small platform. Show upcoming pieces. Drag above the platform, rotate with a separate button, then release. Choosing wide support or a daring overhang has a visible consequence.

Feel sequence: grabbed piece lifts, its shadow separates, placement follows the finger without easing lag; release has weight; collision gets a tiny squash on the visual only and a low wooden tap; wobble decays into a clear settled state. The final settling moment gives a warm chord and a silhouette reveal. Fallen pieces return to the tray, never erase the sculpture.

First slice: credible stable physics, six pieces, placement/rotation, recovery and an ending. Prove physics first. Prefer a small licensed offline physics library over pretending a snap-grid is balancing. Separate cosmetic motion from collision state. Later: curated piece sets and silhouette collection. No collection work before placing pieces feels good.

## Lost & Found: peel the room open

Three reference pictures stay visible above a compact layered wall. Find any exposed target and lift it away. Each choice uncovers different possibilities. Clear the wall to reveal its hidden final picture.

Feel sequence: touch lifts the tile a few pixels; correct tile curls and peels into its reference slot, leaving a clean opening; paper flecks and a soft rip emphasize the removal; the new layer catches the light. Consecutive discoveries add gentle musical steps without a timer penalty. Final tiles release a wider reveal, giving the satisfying clean-board moment.

First slice: all targets reachable, three visible references, no tiny search objects, a solvable small layered deal, clear ending. Target identity stays unique. Later: real provider media, more compositions, and choices that reward remembered lower layers. No wall drift during aiming.

## Echo: finish the tune

Four fixed pads form a short duet. Hear a phrase, then answer along visible timing cues. The part you finish joins the backing. Choose a compatible next phrase and build to a composed ending.

Feel sequence: pad depresses and sounds immediately; an expanding ring meets a stable target on the beat; clean notes send a small light trail into the arrangement; completed phrases visibly become persistent layers. A miss briefly thins your part but never stops the music. Rejoin on the next cue. Finish with a prepared cadence and a short moment to hear what you made.

First slice: one authored piece, four pads, forgiving windows, visible cues, growing layers, one compatible phrase choice, final cadence. Audio starts from a deliberate tap. Audio clock drives timing. Do not grade against delayed animation frames. Later: small authored phrase bank, more arrangements, saved choices. Never random pitches disguised as a composition.

## Execution and ownership

Three agents work independently: Recall, Composure, Echo. Root builds Lost & Found and the common preview. Each lane owns only new feel* files inside its game folder, plus its focused tests. No nested agents. Fifteen-minute checkpoint per lane. Root owns shared integration and inspects stalled work directly.

All modules export `create(ctx)` and return `{start(spec), pause(), resume(), suspend(on), destroy()}`. `ctx.root` is the mount node; `ctx.endClass(report)` is called once. Optional `ctx.lexicon(key, fallback)`, `ctx.motion.motionLevel`, `ctx.audioAudible`. Reports use `{metrics:{composite}, hardGates:{}, flavorXp:0, feelVersion:2}`. The preview supplies these adapters, never writes progression, and hosts these same modules, not throwaway duplicate games. Modules must not read the old best score for difficulty.

The preview provides selection, explicit Start, Pause, sound and motion controls. It imports each game's feel.js directly. No production registry change in milestone one. Agent code must not commit private data. Changes remain reviewable in stacked PRs of roughly 600 lines each.

## Checkpoints

1. Four playable minutes: inspect the concrete press/reveal, place/settle, peel/uncover and play/build sequences. Judge each with sound on, muted, and reduced motion. Phone layout includes portrait 390x844 and compact 360x640. Real phone feel still requires a real phone.
2. Owner feel review: can the rule be understood immediately, is the basic action pleasant, are mistakes fair, and does another round sound appealing? Keep, revise or replace weak loops before expanding content.
3. Accepted loops gain content and production adapters: current cards, stars, purchases, daily limits and Annex access remain intact; grade and saved game records are versioned. Retakes grant no extra daily XP or stars. Existing players see a short new intro.
4. Validate deal truth/reachability, physics recovery, audio scheduling, pause/destroy, and one completion report. Run the full unit suite once at production integration, plus focused behavior checks for these modules. Do not repair unrelated harnesses.
5. Before rollout add minimal versioned starts, finishes, early exits and voluntary replay counts through existing approved tracking. Server changes stay in the private repo. Desktop release and phone publication are separate. No merge or deployment is part of this first feel checkpoint.

## Milestone one checkpoint, 2026-09-22

All four slices are implemented in `feel.js` modules and available through `feel-preview.html`. These are original vector/synth prototypes, not production game replacements. Composure vendors Matter.js 0.20.0 with its MIT license.

- Recall: three visible pictures, one substitution, six untimed rounds, fracture/restore response, answer evidence and a restored-picture finale. The seed repeats the same deal.
- Composure: six physical pieces, rotation, direct drag, recovery, collision response and a stable-sculpture ending.
- Lost & Found: 27 unique pictures across nine three-layer stacks, three reachable references, peel flight, paper flecks and a hidden garden reveal.
- Echo: six authored phrases, four pads, a compatible phrase branch, earned accompaniment and a resolving cadence.

Validation: 17 focused tests pass. All four modules open without browser errors at 390x844, with no horizontal overflow and game buttons at least 48 CSS px. Complete browser runs at 360x640 reached all four endings: 27 real search presses, a stable six-piece sculpture, six Recall answers, and an Echo run with every note missed. Pause/resume was exercised for each module. Full desktop integration and the C# suite are not relevant yet because no production host or registry changes were made.

Still unproven: subjective satisfaction, real phone touch/audio latency, composition quality by ear, real media integration, difficulty curves and long-term replay value. The next checkpoint is the owner's playthrough of these slices, followed by targeted revisions before content expansion.

Local preview: serve `ConditioningControlPanel/Resources/web/arcademy` on localhost and open `/feel-preview.html`. This preview never writes cards, stars, XP or player records.
