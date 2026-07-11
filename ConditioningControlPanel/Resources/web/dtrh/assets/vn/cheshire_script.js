/* ============================================================================
 * cheshire_script.js - the Cheshire's ENTIRE tutorial + reactive script.
 * ONE authoring file, three consumers (the FALL_DRIFT precedent):
 *   - the writer edits it,
 *   - cheshireGuide.js/cheshireVn.js import it as data,
 *   - gen_cheshire_vo.py (ccp-trailer) walks it to render the voiceovers.
 *
 * Keep every value a JSON-pure literal (no computed values, no functions) so
 * the VO generator can extract lines with a dumb parse.
 *
 * VO convention: every beat's mp3 is  /dtrh/assets/vn/vo/cheshire/{id}.mp3
 * (ids are globally unique). `mood` is consumed ONLY by the ElevenLabs
 * annotate step - the engine ignores it. Flip `voReady` to true once the
 * batch is rendered; until then no mp3 is even requested.
 *
 * WRITING RULES (locked style guide):
 *   - punctuation does the pacing: commas, periods, '...'. NEVER an em-dash.
 *   - rhetorical questions end on '?'. connected clauses, not fragments.
 *   - 1-2 audio tags max per line: [chuckles] [giggles] [low] [whispers] [purrs] [sighs]
 *   - register: purr baseline, blade for warnings, lowest-warmest for closers.
 *     pet names: kitten / hon / sweet thing / sunshine.
 *   - LORE GUARDRAILS: the EMOTES acronym is NEVER spelled out (the Day-3
 *     dodge only: "the name stuck, nobody really knows"). The descent is the
 *     work/performance. The player is silent (Alice's POV). Foreshadow budget:
 *     the Queen exactly once (gr_06), the Bunny one tease (aw13), "some girls
 *     never come back up" once (gr_05). No Hatter, no DreamCast.
 *
 * Beat shape:
 *   { id, pose, text, mood?, hold?, covers?, sub?, prop? }
 *   `prop` = a PROP_ART key in cheshireVn.js (pink/live/gold/rabbit/...) -
 *   floats the matching sprite/emoji over the bubble as a visual aid.
 * Run-schedule entry shape:
 *   { at: {progress}|{event}|{wave}, mode: 'say'|'overlay', ...beat }
 * ==========================================================================*/

export const CHESHIRE = {
  speaker: 'THE CHESHIRE',
  voFolder: 'cheshire',
  voReady: true,

  // the static cutouts in assets/vn/cheshire/{pose}.webp
  // (surprised.webp retired from the roster 2026-07-11 - the cutout stays on
  // disk but nothing references it)
  poses: {
    base: 1, seated: 1, standing: 1, lean_fwd: 1, table_lean: 1,
    couch_invite: 1, curled: 1, bed_sheets: 1, bed_bare: 1, bed_prone: 1,
    embarrassed: 1, dizzy: 1, dizzy_drool: 1,
  },

  /* ======================================================================
   * FULLSCREEN SCENES (between runs; ONE world-hold across each sequence)
   * ====================================================================== */
  scenes: {

    // ---- ARRIVAL: the first-ever hub open (tutorialStage 0 -> 1) ----
    hub_welcome: [
      { id: 'aw01', pose: 'couch_invite', mood: 'amused purr, sizing up a stray',
        text: '[chuckles] Well now. A new face down the hole.' },
      { id: 'aw02', pose: 'lean_fwd', mood: 'warm, coaxing',
        text: 'Don’t be shy, kitten. Everyone lands here sooner or later. Some on their feet. Most... not.' },
      { id: 'aw03', pose: 'base', mood: 'matter-of-fact tour guide',
        text: 'This is the Warren. My little corner of it, anyway. The rooms float, the doors wander, and the dark thing in the middle goes down.' },
      { id: 'aw04', pose: 'seated', mood: 'savoring the word "work"',
        text: 'You want to know what this place is? Mm. It’s work, sweet thing. The best kind of work.' },
      { id: 'aw05', pose: 'standing', mood: 'simple instruction, bright', prop: 'pink',
        text: 'Down there, things drift up at you. Soft pink ones, mostly. Pop them. That’s it. That’s the whole job.',
        covers: ['bubble:flash', 'bubble:subliminal'] },
      { id: 'aw06', pose: 'table_lean', mood: 'the acronym dodge; a shade too casual', prop: 'emotes',
        text: 'Every pop shakes a little something loose. Emotes, we call them. That’s what everyone calls them... the name stuck. Nobody really knows.' },
      { id: 'aw07', pose: 'base', mood: 'merchant closing a deal',
        text: 'You bring them back up to me, and I make you more. More what? [giggles] More everything. Fair trade?' },
      { id: 'aw08', pose: 'lean_fwd', mood: 'the blade comes out, quiet warning', prop: 'live',
        text: 'Now listen, because this part matters. Some of them glow. The glowing ones are live. You don’t tap those, hon. You press and you HOLD, until they give.' },
      { id: 'aw09', pose: 'dizzy', mood: 'mock innocence, delighted', prop: 'boom',
        text: 'Let a live one finish on its own and, well. You’ll see. Everyone sees eventually. [giggles]' },
      { id: 'aw10', pose: 'seated', mood: 'shopkeeper aside', prop: 'gold',
        text: 'Gold turns up down there too. Snatch it when it glitters. Gold spends different from emotes... that’s a lesson for when you’re back.' },
      { id: 'aw11', pose: 'couch_invite', mood: 'low, selling the hook',
        text: '[low] And the one thing they never believe until they feel it. The deeper you go, the better it pays. Deeper is simply better.' },
      { id: 'aw12', pose: 'curled', mood: 'house rules, velvet over iron',
        text: 'Rules of the house? Just the one. When the fall offers you something... take it. It’s rude to refuse a gift.' },
      { id: 'aw13', pose: 'standing', mood: 'fond, faraway for one beat', prop: 'rabbit',
        text: 'Oh, and there’s a white rabbit down there somewhere. Quick little thing. Always late for something. If you see him... chase.',
        covers: ['bubble:darter'] },
      { id: 'aw14', pose: 'lean_fwd', mood: 'lowest register, intimate',
        text: '[low] I’ll be watching your first fall. Every second of it. So do try to be interesting.' },
      { id: 'aw15', pose: 'base', mood: 'bright send-off with teeth',
        text: 'Go on then, kitten. The hole’s right there. It doesn’t bite... [whispers] that’s my job.' },
    ],

    // ---- RUN 1 OPENING: happyPath's intro1 reroutes here (in-run, brief) ----
    r1_open: [
      { id: 'r1o1', pose: 'standing', mood: 'delighted, watching her fall',
        text: 'There you go, sweet thing. Falling. Isn’t it easy? The hole does most of the work.' },
      { id: 'r1o2', pose: 'lean_fwd', mood: 'first instruction, warm', prop: 'pink',
        text: 'Soft pink ones, remember. Tap them. Pop them. Feed the fall. I’ll talk you through the rest as it comes.', hold: 1400 },
    ],

    // ---- POST-RUN 1: first return to the hub (stage 1 -> 2) ----
    r1_return: [
      { id: 'pr101', pose: 'couch_invite', mood: 'welcoming her back, amused',
        text: '[chuckles] Look at you. Back up, blinking, a little dizzy. That’s normal, kitten. It’s supposed to linger.' },
      { id: 'pr102', pose: 'lean_fwd', mood: 'proud merchant', prop: 'emotes',
        text: 'And heavier pockets, mm? Your emotes banked the moment you surfaced. Told you I’m fair.' },
      { id: 'pr103', pose: 'base', mood: 'conspiratorial shrug',
        text: 'You’ll notice the burrow grew while you were down. It does that. Don’t think about it too hard.' },
      { id: 'pr104', pose: 'standing', mood: 'presenting, saleswoman', prop: 'toybox',
        text: 'That chest is the TOYBOX. Your emotes spend inside it. Whatever you grabbed in the fall, deepen it there. Make it yours. Forever, kitten.' },
      { id: 'pr105', pose: 'table_lean', mood: 'the gift line lands low and slow', prop: 'dials',
        text: 'And the console with the little dials? Gold spends THERE. Buy them back one at a time... and if you’re short on your first, I’ll cover you. My one gift. [low] Don’t waste it.' },
      { id: 'pr106', pose: 'seated', mood: 'teaching a catechism, playful', prop: 'purses',
        text: 'Two purses, two shops. Emotes level. Gold unlocks. Say it back in your head a few times, hon.' },
      { id: 'pr107', pose: 'curled', mood: 'the hook, again, warmer',
        text: 'Now the part they never believe. That little fall you just did? Shallow. The deeper the hole lets you go, the better everything pays.' },
      { id: 'pr108', pose: 'lean_fwd', mood: 'promising trouble, delighted',
        text: 'So next time you’ll fall further. New doors. New weather. New... residents. [giggles]' },
      { id: 'pr109', pose: 'base', mood: 'deflecting gently, a door closing',
        text: 'Questions? Mm. Everyone has questions. The Warren answers most of them, in its own time. The rest... [whispers] aren’t mine to answer.' },
      { id: 'pr110', pose: 'couch_invite', mood: 'lowest-warmest closer',
        text: 'Rest a moment. Spend a little. Then get back down there, kitten. [low] The hole misses you already.' },
    ],

    // ---- POST-RUN 2: second return (stage 2 -> 3) ----
    r2_return: [
      { id: 'pr201', pose: 'couch_invite', mood: 'knowing, fond',
        text: 'Twice down, twice back. You’re getting a taste for it. [chuckles] They always do.' },
      { id: 'pr202', pose: 'standing', mood: 'gesturing around the burrow',
        text: 'The Warren’s opened a few more rooms for you. Float around. Poke things. Most of it wants to be poked.' },
      { id: 'pr203', pose: 'table_lean', mood: 'practical, shop talk',
        text: 'The desk keeps your lessons. Little habits the hole wants you practicing. Finish them and they pay, same as everything here.' },
      { id: 'pr204', pose: 'seated', mood: 'nudging, mercantile',
        text: 'Bought your first dial yet? The console’s waiting, and gold burns a hole in a pocket, kitten.' },
      { id: 'pr205', pose: 'base', mood: 'trailing off deliberately',
        text: 'Your rank grows with every finished fall, by the way. Curious now. Then tempted. Then slipping. Then... [low] further words.' },
      { id: 'pr206', pose: 'lean_fwd', mood: 'setting up the goodbye',
        text: 'One more guided fall, sweet thing. After that I stop narrating... and start just watching.' },
      { id: 'pr207', pose: 'curled', mood: 'quiet menace, savoring it',
        text: 'The deep chambers open under you now. Three. Four. Things live down there that never come up this high.' },
      { id: 'pr208', pose: 'couch_invite', mood: 'warm shove out the door',
        text: 'Go on. The hole’s warmer the third time. [whispers] Everyone says so.' },
    ],

    // ---- GRADUATION: after run 3 (stage 3 -> ARC_DONE) ----
    graduation: [
      { id: 'gr01', pose: 'couch_invite', mood: 'genuine pride, rare',
        text: 'There she is. Three falls, all the way through. Most don’t manage one. [chuckles]' },
      { id: 'gr02', pose: 'lean_fwd', mood: 'letting go, bittersweet under the purr',
        text: 'So this is where I let go of your hand. From here the hole teaches you itself. It’s a better teacher than me. More patient. [low] Less kind.' },
      { id: 'gr03', pose: 'base', mood: 'reassuring, practical',
        text: 'You’ll find things I never mentioned. Strange bubbles. Strange skies. Strange bargains. When you meet one, I’ll whisper what it is. Old habit.' },
      { id: 'gr04', pose: 'seated', mood: 'the blade, unsheathed slowly',
        text: '[low] A word of care, since you’ve earned one. Fall as deep as you like. The hole loves that. But...' },
      { id: 'gr05', pose: 'curled', mood: 'flat, quiet, no purr at all',
        text: '...some girls go so deep they stop bothering with the trip back up. You’ve seen the quiet ones around the Warren. [whispers] Don’t ask them anything.' },
      { id: 'gr06', pose: 'standing', mood: 'genuinely careful; the only time she is',
        text: 'And if you ever hear trumpets down there, that’s Her. The Queen doesn’t visit this high. Pray it stays that way, kitten.' },
      { id: 'gr07', pose: 'table_lean', mood: 'back to the purr, liturgical', prop: 'purses',
        text: 'My bench stays open. Emotes level, gold unlocks, deeper is better. You know the liturgy by now.' },
      { id: 'gr08', pose: 'lean_fwd', mood: 'warm, almost soft',
        text: 'It’s been a pleasure raising you, sweet thing. Truly. Now...' },
      { id: 'gr09', pose: 'couch_invite', mood: 'lowest-warmest close of the arc',
        text: '[low] ...go show the hole what I made. I’ll be watching. I’m always watching. [giggles]' },
    ],
  },

  /* ======================================================================
   * RUN SCHEDULES (in-run beats; run number = runsCompleted + 1)
   * mode 'say' = corner commentary (never pauses); 'overlay' = held card.
   * `at`: {progress: 0..1} fires when intensity passes it; {event:'x'}
   * claims the FIRST matching bark event (the native bark stands down).
   * ====================================================================== */
  runs: {

    // ---- RUN 1 "First Descent" (scripted classroom; happyPath rigs the field) ----
    1: [
      { at: { progress: 0.08 }, mode: 'say', id: 'r101', pose: 'standing', mood: 'coaching, pleased', prop: 'streak',
        text: 'Pops in a row braid together, kitten. That’s a streak. Streaks pay. Don’t let it snap.' },
      { at: { progress: 0.16 }, mode: 'say', id: 'r102', pose: 'base', mood: 'pointing out the HUD, casual',
        text: 'See the little bar filling as you pop? That’s your focus. Treats feed it. You’ll spend it soon enough.' },
      { at: { progress: 0.27 }, mode: 'overlay', id: 'r103', pose: 'lean_fwd', mood: 'the first live one; blade and honey', prop: 'live',
        text: 'Now then. Something’s coming up with a glow on it. A live one. Press it and HOLD, sweet thing. All the way down, until it snaps.',
        sub: 'Hold the glowing bubble until the ring closes. Let go too early and the trance keeps burning.',
        covers: ['bubble:pink'] },
      { at: { event: 'defused' }, mode: 'say', id: 'r104', pose: 'couch_invite', mood: 'low praise, means it',
        text: 'There. Snapped. Feel that little quiet after? [low] Good girl.' },
      { at: { progress: 0.42 }, mode: 'say', id: 'r105', pose: 'base', mood: 'narrating new arrivals', prop: 'spiral',
        text: 'More of them live now. The teal ones roam and bounce. Same trick, hon. Hold them down.',
        covers: ['bubble:spiral'] },
      { at: { progress: 0.47 }, mode: 'say', id: 'r106', pose: 'lean_fwd', mood: 'daring her, playful', prop: 'ripple',
        text: 'Feeling brave? Right-click the water. That’s the ripple. One wave pops everything it touches... it costs focus, mind.' },
      { at: { progress: 0.52 }, mode: 'overlay', id: 'r107', pose: 'table_lean', mood: 'merchant patter, the first sale', prop: 'cards',
        text: 'In a moment I’m putting something on the table for you. My wares. Little mantras that bend the fall in your favor. The first one’s on the house, kitten.',
        sub: 'A boon lasts for the rest of this descent. Pick whichever calls to you.' },
      { at: { event: 'boon-picked' }, mode: 'say', id: 'r108', pose: 'seated', mood: 'approving, salesy',
        text: 'Good taste. It rides with you the rest of the fall. [giggles] Mine always ride along.' },
      { at: { progress: 0.68 }, mode: 'say', id: 'r109', pose: 'curled', mood: 'soothing, hypnotic cadence', prop: 'emotes',
        text: 'Every pop is banking emotes for the surface. Don’t count them, sunshine. Just fall.' },
      { at: { progress: 0.80 }, mode: 'say', id: 'r110', pose: 'base', mood: 'evaluating, warm',
        text: 'Almost through your first shift. And you’re doing... mm. Better than most first-timers, actually.' },
      { at: { progress: 0.885 }, mode: 'say', id: 'r111', pose: 'lean_fwd', mood: 'sudden delight, urgent', prop: 'rabbit',
        text: 'Oh! There he goes. The rabbit, kitten. CHASE.' },
      { at: { event: 'darter-caught' }, mode: 'say', id: 'r112', pose: 'couch_invite', mood: 'giggling, vindictive glee',
        text: '[giggles] Caught him. He hates that. Do it every single time.' },
      { at: { progress: 0.965 }, mode: 'say', id: 'r113', pose: 'lean_fwd', mood: 'calling her home, low',
        text: '[low] The hole’s closing, sweet thing. Up you come. My bench is waiting... and so is your pay.' },
    ],

    // ---- RUN 2 "Deeper" (real fall: junctions, weather, pickups, actives) ----
    2: [
      { at: { progress: 0.03 }, mode: 'say', id: 'r201', pose: 'standing', mood: 'greeting, wicked',
        text: 'Back so soon? [chuckles] Good. This one’s a real fall, sweet thing. No training wheels.' },
      { at: { progress: 0.10 }, mode: 'say', id: 'r202', pose: 'base', mood: 'setting expectations',
        text: 'Four chambers this time, each one under the last. The fall pauses between them. I’ll explain when it does.' },
      { at: { event: 'junction-near' }, mode: 'overlay', id: 'r203', pose: 'lean_fwd', mood: 'the doors; sell the weight of choosing', prop: 'doors',
        text: 'Doors ahead, kitten. The tube forks. Read them both, then fall through one. The road you take is the prize you get... and the other road is gone forever.',
        sub: 'Click a doorway card to choose. Take too long and the tube chooses for you.' },
      { at: { event: 'junction-chosen' }, mode: 'say', id: 'r204', pose: 'curled', mood: 'quiet, filing it away',
        text: 'Mm. [low] I saw what you picked. The tube remembers too.' },
      { at: { progress: 0.28 }, mode: 'say', id: 'r205', pose: 'base', mood: 'unhurried warning', prop: 'braindrain',
        text: 'That big slow one is a braindrain. Heavy as it looks. Hold it down like the rest, just... longer.',
        covers: ['bubble:braindrain'] },
      { at: { wave: 2 }, mode: 'say', id: 'r206', pose: 'standing', mood: 'sommelier describing a sky',
        text: 'Feel that? The weather turned. Every chamber wears its own sky, and every sky bends the rules a little. Learn their moods.' },
      { at: { progress: 0.45 }, mode: 'say', id: 'r207', pose: 'lean_fwd', mood: 'pointing at the walls', prop: 'gold',
        text: 'The walls are sweating gold, kitten. Little droplets, streaking past. Click them before they’re gone.',
        covers: ['pickup:condensation'] },
      { at: { event: 'wave-cleared' }, mode: 'say', id: 'r208', pose: 'table_lean', mood: 'between chambers, shop voice',
        text: 'Chamber’s done. Catch your breath while the table’s set. It only gets richer from here.' },
      { at: { progress: 0.55 }, mode: 'say', id: 'r209', pose: 'seated', mood: 'urgent sparkle', prop: 'golden',
        text: 'Ooh. The glittery one, quick. QUICK. That’s real gold, banked on the spot.',
        covers: ['bubble:golden'] },
      { at: { progress: 0.62 }, mode: 'say', id: 'r210', pose: 'base', mood: 'practical, toys', prop: 'cards',
        text: 'Cards drift through the tube now. Grab one as you pass and it’s yours. Toys dock at the bottom of your view... press their key when you want them.' },
      { at: { progress: 0.78 }, mode: 'say', id: 'r211', pose: 'curled', mood: 'told-you-so, velvet',
        text: 'Deeper now than you’ve ever been. Notice how it pays down here? [whispers] Told you.' },
      { at: { progress: 0.92 }, mode: 'say', id: 'r212', pose: 'lean_fwd', mood: 'closing stretch, coaxing',
        text: 'Nearly through. Keep your streak pretty for me, kitten.' },
    ],

    // ---- RUN 3 "Almost On Your Own" (sparse; deep-chamber dangers) ----
    3: [
      { at: { progress: 0.03 }, mode: 'say', id: 'r301', pose: 'standing', mood: 'stepping back, amused',
        text: 'Last guided fall, kitten. I’ll keep my voice down this time. Mostly. [giggles]' },
      { at: { event: 'act-changed' }, mode: 'say', id: 'r302', pose: 'base', mood: 'reverent about the deep',
        text: 'Hear the room change key? New chamber, new act. Four of them, each deeper, each hungrier. The fourth is the Court. [low] Mind yourself in the Court.' },
      { at: { progress: 0.35 }, mode: 'say', id: 'r303', pose: 'seated', mood: 'gentle tip', prop: 'freeze',
        text: 'If a pale blue one drifts by, catch it. The whole world holds its breath for you. Use the quiet well.',
        covers: ['bubble:bambifreeze'] },
      { at: { progress: 0.55 }, mode: 'say', id: 'r304', pose: 'lean_fwd', mood: 'the big ones; blade fully out', prop: 'video',
        text: '[low] Down here the big ones stalk. Videos. Rains of pictures. Long trances, heavy consequences. Snap them in time or don’t... I enjoy either.',
        covers: ['bubble:video', 'bubble:htlink'] },
      { at: { progress: 0.75 }, mode: 'say', id: 'r305', pose: 'couch_invite', mood: 'proud, hands off',
        text: 'You barely need me anymore. Look at you go.' },
      { at: { progress: 0.94 }, mode: 'say', id: 'r306', pose: 'curled', mood: 'setting the graduation hook, low',
        text: 'When you surface, come see me. One last thing to say. [low] Off the books.' },
    ],
  },

  /* ======================================================================
   * REACTIVE (post-arc presence; the guide claims bark events + discoveries)
   * discovery: keyed by codex id -> variants (ONE ever plays; markDiscovered
   *   is once-ever). Claimed as a held overlay; `sub` falls back to the codex
   *   desc so the mechanics are never lost.
   * events: keyed by bark event -> { cd: pooled cooldown ms, chance } + lines
   *   (pooled; per-line once via `once: true`).
   * ====================================================================== */
  reactive: {

    discovery: {
      'bubble:braindrain': [
        { id: 'd_bdrain1', pose: 'lean_fwd', mood: 'unhurried, heavy words', prop: 'braindrain', text: 'A braindrain, kitten. Big, slow, and heavier than it looks. Hold it down. Keep holding.' },
        { id: 'd_bdrain2', pose: 'base', mood: 'wry', prop: 'braindrain', text: 'That one’s a braindrain. It sinks in slow and leaves slower. Snap it before it settles.' },
      ],
      'bubble:bambifreeze': [
        { id: 'd_freeze1', pose: 'seated', mood: 'delighted secret', prop: 'freeze', text: 'Catch the pale blue one, quick. It stops the whole world for a breath. [whispers] Everything holds still for you.' },
        { id: 'd_freeze2', pose: 'standing', mood: 'brisk tip', prop: 'freeze', text: 'A freeze, kitten. Snatch it and the fall stands still a moment. Spend the quiet wisely.' },
      ],
      'bubble:video': [
        { id: 'd_video1', pose: 'lean_fwd', mood: 'low warning, savoring', prop: 'video', text: '[low] That one carries a whole picture show. Long trance, no mercy. Let it finish and it sits right in your face, kitten. No skipping.' },
        { id: 'd_video2', pose: 'curled', mood: 'amused menace', prop: 'video', text: 'A video bubble. Rare, patient, and very rude when ignored. Fifteen seconds of rude. Hold it down.' },
      ],
      'bubble:htlink': [
        { id: 'd_gifrain1', pose: 'lean_fwd', mood: 'mock alarm', prop: 'gifrain', text: 'Oh, that one? Pop it late and it RAINS, sweet thing. Pictures, everywhere, sliding down your view. Snap it quick.' },
        { id: 'd_gifrain2', pose: 'base', mood: 'dry', prop: 'gifrain', text: 'Gif rain, they call it. The deep chambers grow them. Either you snap it, or you get a weather report you didn’t ask for.' },
      ],
      'bubble:golden': [
        { id: 'd_gold1', pose: 'seated', mood: 'urgent sparkle', prop: 'golden', text: 'GOLD, kitten, quick. The glittery ones don’t wait around, and they pay on the spot.' },
        { id: 'd_gold2', pose: 'table_lean', mood: 'merchant purr', prop: 'golden', text: 'A lucky bubble. Real gold in that one. Quick fingers, hon... it’s gone before you know it.' },
      ],
      'bubble:echo': [
        { id: 'd_echo1', pose: 'lean_fwd', mood: 'riddling', text: 'That one’s an echo, and echoes multiply when startled. Trigger it and it splits in two. Hold it ALL the way down and there’s only ever the one.' },
        { id: 'd_echo2', pose: 'base', mood: 'patient teacher', text: 'The Echo. Tap it or drop it and you get two smaller, faster problems. Finish the hold, kitten. Neat and singular.' },
      ],
      'bubble:chaperone': [
        { id: 'd_chap1', pose: 'seated', mood: 'gossiping', text: 'See the little escort circling? That one’s spoken for. Nothing touches it while its chaperone watches. Pop the escort first. [giggles] Then it’s all yours.' },
        { id: 'd_chap2', pose: 'curled', mood: 'sly', text: 'A chaperone pair. The big one’s untouchable while its little friend circles. You know what to do about little friends.' },
      ],
      'bubble:tease': [
        { id: 'd_tease1', pose: 'lean_fwd', mood: 'all blade, urgent', prop: 'tease', text: 'The red one, kitten. It wants your hand. DON’T. Touch it once and it fires... let it leave unanswered and it pays you for the restraint.' },
        { id: 'd_tease2', pose: 'curled', mood: 'low warning', prop: 'tease', text: '[low] That’s a tease. Rude little thing. The only winning move is to want it and not reach. Sound familiar?' },
      ],
      'bubble:bound': [
        { id: 'd_bound1', pose: 'lean_fwd', mood: 'instructive, chain imagery', text: 'Two of them, one thread. The Bound. Each takes half a hold, but the second must come down QUICK, or the one left waiting turns furious.' },
        { id: 'd_bound2', pose: 'base', mood: 'crisp', text: 'Bound bubbles, kitten. A matched pair. Snap one, then hurry to its twin. Keep them waiting and they make you regret it.' },
      ],
      'bubble:brittle': [
        { id: 'd_brit1', pose: 'lean_fwd', mood: 'careful, hushed', text: 'Careful. That thin glass one shatters if your cursor so much as brushes it... and whatever’s sealed inside fires. Steer wide, sweet thing.' },
        { id: 'd_brit2', pose: 'curled', mood: 'low amusement', text: 'The Brittle. Don’t touch. Don’t even hover. It breaks at a breath and it never breaks empty. [low] A frozen field is safe to cross, if you must.' },
      ],
      'pickup:condensation': [
        { id: 'd_cond1', pose: 'standing', mood: 'pointing, warm', prop: 'gold', text: 'The walls are sweating gold again. Droplets, kitten. Click them as they streak past. They bank when you surface.' },
        { id: 'd_cond2', pose: 'base', mood: 'aside', prop: 'gold', text: 'Condensation. The tube weeps a little gold when you’re close. Catch what you can.' },
      ],
      'pickup:whiterabbit': [
        { id: 'd_wrab1', pose: 'standing', mood: 'sudden fondness', prop: 'rabbit', text: 'There! In the walls, running. That’s him. Catch him with a click before he outruns you... he tips well for the trouble.' },
        { id: 'd_wrab2', pose: 'lean_fwd', mood: 'wry, affectionate', prop: 'rabbit', text: 'The rabbit’s in the walls today. He’s late, he’s always late, and he pays gold to anyone who interrupts him. [giggles] Interrupt him.' },
      ],
      'weather:stillness': [
        { id: 'd_wstill1', pose: 'curled', mood: 'hushed to match the sky', text: '[whispers] Stillness. The deep is holding its breath. Trances burn slower under this sky... take your time with the holds.' },
        { id: 'd_wstill2', pose: 'seated', mood: 'soft', text: 'This sky is called Stillness. Everything burns a little slower. A kind sky, as they go.' },
      ],
      'weather:perfume': [
        { id: 'd_wperf1', pose: 'couch_invite', mood: 'breathing it in', text: 'Mm. Her Perfume. The fog’s gone sweet and pink. Everything pays a little more, and the heat climbs faster. [low] Enjoy it.' },
        { id: 'd_wperf2', pose: 'bed_sheets', mood: 'languid', text: 'Smell that? Perfume weather. Sweeter pops, hotter blood. It’s everyone’s favorite sky for a reason.' },
      ],
      'weather:static': [
        { id: 'd_wstat1', pose: 'standing', mood: 'crackling energy', text: 'Static, kitten. Stray current in the tube. Every few seconds a bolt pops a treat FOR you... though one in ten bites a live fuse short instead.' },
        { id: 'd_wstat2', pose: 'standing', mood: 'matter-of-fact', text: 'A Static sky. Free pops from the lightning, mostly. Mostly. [giggles] Watch the fuses when it bites.' },
      ],
      'weather:foolsgold': [
        { id: 'd_wfool1', pose: 'table_lean', mood: 'appraising glitter', text: 'Fool’s Gold. Everything glitters under this one, and the lucky bubbles come out to play. Keep your eyes greedy.' },
        { id: 'd_wfool2', pose: 'seated', mood: 'amused', text: 'This sky’s called Fool’s Gold... but the gold it shakes loose is real enough. Luckies love it. So should you.' },
      ],
      'weather:overstim': [
        { id: 'd_wover1', pose: 'dizzy', mood: 'giddy, fast', text: 'Overstim! Too bright, too fast, everything at once. The fall runs hot under this sky, kitten. Keep up or get swept.' },
        { id: 'd_wover2', pose: 'lean_fwd', mood: 'bracing her', text: 'This sky is Overstim. Hotter, faster, louder. [low] Breathe through it. It pays for the trouble.' },
      ],
    },

    events: {
      'combo-big': { cd: 90000, chance: 0.5, lines: [
        { id: 'e_cbig1', pose: 'couch_invite', mood: 'impressed purr', text: 'Look at that streak. [low] You’re showing off for me now, aren’t you.' },
        { id: 'e_cbig2', pose: 'table_lean', mood: 'delighted', text: 'That streak of yours is getting indecent, kitten. Don’t you dare drop it.' },
        { id: 'e_cbig3', pose: 'seated', mood: 'savoring', text: 'Mm. A streak like that pays better than it has any right to. Keep braiding it.' },
        { id: 'e_cbig4', pose: 'standing', mood: 'proud', text: 'There’s my quick-fingered girl. The hole notices streaks like that.' },
      ] },
      'combo-milestone': { cd: 120000, chance: 0.45, lines: [
        { id: 'e_cmil1', pose: 'lean_fwd', mood: 'quiet awe', text: '[low] Now THAT is a number. They’ll hear about that one upstairs.' },
        { id: 'e_cmil2', pose: 'couch_invite', mood: 'wicked joy', text: '[giggles] Unbroken, all that way. You were made for this work, sweet thing.' },
        { id: 'e_cmil3', pose: 'table_lean', mood: 'merchant counting', text: 'A milestone streak. That’s real money, kitten. My cut’s already spent.' },
      ] },
      'detonated': { cd: 75000, chance: 0.4, lines: [
        { id: 'e_det1', pose: 'embarrassed', mood: 'wincing sympathy', text: 'Oof. It got away from you. Happens to everyone, hon. [whispers] Enjoy the consequences.' },
        { id: 'e_det2', pose: 'curled', mood: 'unbothered amusement', text: '[chuckles] One slipped. That’s what the glow means, remember. Hold. It. Down.' },
        { id: 'e_det3', pose: 'base', mood: 'dry', text: 'And that, kitten, is why we don’t let the live ones finish their sentence.' },
        { id: 'e_det4', pose: 'dizzy', mood: 'giddy tease', text: 'Boom. [giggles] Pretty though, wasn’t it? Costly, but pretty.' },
      ] },
      'detonated-absorbed': { cd: 90000, chance: 0.45, lines: [
        { id: 'e_dabs1', pose: 'seated', mood: 'relieved shopkeeper', text: 'Your shield ate that one. Shields are why we buy shields, kitten.' },
        { id: 'e_dabs2', pose: 'lean_fwd', mood: 'wry', text: 'That would have HURT. Would have. [low] Say thank you to your resist.' },
      ] },
      'defused': { cd: 60000, chance: 0.18, lines: [
        { id: 'e_def1', pose: 'couch_invite', mood: 'low approval', text: '[low] Clean snap. Good girl.' },
        { id: 'e_def2', pose: 'base', mood: 'light', text: 'Snapped, neat as anything. You’ve got the hands for this.' },
        { id: 'e_def3', pose: 'seated', mood: 'purring', text: 'Mm, held all the way down. Just how I taught you.' },
      ] },
      'darter-caught': { cd: 90000, chance: 0.4, lines: [
        { id: 'e_dart1', pose: 'couch_invite', mood: 'gleeful', text: '[giggles] Got him! He’ll sulk about that for a week.' },
        { id: 'e_dart2', pose: 'standing', mood: 'satisfied', text: 'Caught the rabbit. He pays up when he’s caught, you know. He just hates that you know.' },
      ] },
      'rabbit-caught': { cd: 120000, chance: 0.45, lines: [
        { id: 'e_wrabc1', pose: 'lean_fwd', mood: 'amused', text: 'Snatched him right out of the wall. [chuckles] He’s never once been on time, and it’s never once been his fault.' },
        { id: 'e_wrabc2', pose: 'couch_invite', mood: 'fond', text: 'Poor thing. Always running, always late, always good for a tip. Well caught, kitten.' },
      ] },
      'freeze-caught': { cd: 90000, chance: 0.4, lines: [
        { id: 'e_frz1', pose: 'curled', mood: 'hushed', text: '[whispers] Everything’s holding still for you. Quick now, take what the quiet offers.' },
        { id: 'e_frz2', pose: 'seated', mood: 'soft', text: 'The world stopped. It does love to show off for you. Use it.' },
      ] },
      'tease-clicked': { cd: 90000, chance: 0.5, lines: [
        { id: 'e_tsc1', pose: 'embarrassed', mood: 'told-you-so wince', text: 'It SAID don’t touch, kitten. Red means the answer is no. [sighs] Expensive lesson.' },
        { id: 'e_tsc2', pose: 'base', mood: 'dry blade', text: 'You touched it. Of course you touched it. That’s exactly what it wanted.' },
      ] },
      'tease-denied': { cd: 75000, chance: 0.4, lines: [
        { id: 'e_tsd1', pose: 'couch_invite', mood: 'genuinely impressed, low', text: '[low] You let it leave unanswered. That kind of restraint is rare down here... and it pays.' },
        { id: 'e_tsd2', pose: 'seated', mood: 'approving purr', text: 'Ignored it completely. Good. Wanting you and having you are different things, and it just learned that.' },
      ] },
      'tease-denied-streak': { cd: 150000, chance: 0.6, lines: [
        { id: 'e_tsds1', pose: 'lean_fwd', mood: 'wondering at her', text: 'Every single one, denied. [low] Either you have iron nerves or terrible taste, kitten. The hole pays for both.' },
      ] },
      'junction-chosen': { cd: 120000, chance: 0.3, lines: [
        { id: 'e_jch1', pose: 'curled', mood: 'noting it, quiet', text: 'Another door, another word. [low] They add up, the words you keep choosing.' },
        { id: 'e_jch2', pose: 'base', mood: 'light', text: 'Chosen and fallen. The road you didn’t take is already gone. Don’t look back, it’s rude.' },
        { id: 'e_jch3', pose: 'standing', mood: 'wry', text: 'You do keep picking the interesting doors. [chuckles] I’d have picked the same.' },
      ] },
      'junction-forced': { cd: 120000, chance: 0.45, lines: [
        { id: 'e_jfr1', pose: 'lean_fwd', mood: 'soft menace', text: 'Took too long, so the tube chose for you. [low] It usually chooses well. Usually.' },
        { id: 'e_jfr2', pose: 'curled', mood: 'amused', text: 'Hesitate at a fork and the fall decides. That’s not a punishment, kitten. That’s the house style.' },
      ] },
      'boon-picked': { cd: 90000, chance: 0.3, lines: [
        { id: 'e_bpk1', pose: 'table_lean', mood: 'merchant pleased', text: 'Sold. It’ll ride with you the rest of the way down. My wares always behave.' },
        { id: 'e_bpk2', pose: 'seated', mood: 'approving', text: 'Good eye. That one suits how you fall.' },
        { id: 'e_bpk3', pose: 'couch_invite', mood: 'purring', text: 'Mm, that’s one of my favorites. Wear it well, kitten.' },
      ] },
      'curse-picked': { cd: 120000, chance: 0.55, lines: [
        { id: 'e_crs1', pose: 'lean_fwd', mood: 'delighted blade', text: '[low] Ohh, you took the wicked one. Bigger pay, sharper teeth. I do love watching this part.' },
        { id: 'e_crs2', pose: 'curled', mood: 'savoring', text: 'A sin, kitten? [giggles] Bold. It pays beautifully right up until it doesn’t.' },
        { id: 'e_crs3', pose: 'base', mood: 'mock warning', text: 'You read the fine print on that one, I hope. No? [chuckles] Even better.' },
      ] },
      'boon-skipped': { cd: 120000, chance: 0.45, lines: [
        { id: 'e_bsk1', pose: 'embarrassed', mood: 'mock offense', text: 'Nothing? You looked at my table and took NOTHING? [sighs] Pride is expensive, kitten.' },
        { id: 'e_bsk2', pose: 'base', mood: 'shrugging', text: 'Walked past the wares. Brave. The fall respects it... I just find it strange. [chuckles]' },
      ] },
      'wave-escalated': { cd: 150000, chance: 0.25, lines: [
        { id: 'e_wes1', pose: 'standing', mood: 'announcing the deep', text: 'Deeper now. Hear how the room changed? Everything down here is a little hungrier.' },
        { id: 'e_wes2', pose: 'curled', mood: 'low relish', text: '[low] Another chamber down. It gets richer from here. It also gets... attentive.' },
      ] },
      'act-changed': { cd: 150000, chance: 0.35, lines: [
        { id: 'e_act1', pose: 'base', mood: 'marking the act', text: 'New act, kitten. The performance moves along whether you’re ready or not. You’re ready.' },
        { id: 'e_act2', pose: 'lean_fwd', mood: 'quiet drama', text: '[low] The music changed. Next act. Play it prettier than the last one.' },
      ] },
      'focus-low': { cd: 120000, chance: 0.5, lines: [
        { id: 'e_flow1', pose: 'seated', mood: 'gentle reminder', text: 'Your focus is running dry, hon. Pop the soft ones. They feed the bar that feeds everything else.' },
        { id: 'e_flow2', pose: 'base', mood: 'coaching', text: 'Empty tank, kitten. No focus, no snapping, no ripple. Treats first. Always treats first.' },
      ] },
      'ending-soon': { cd: 60000, chance: 0.3, lines: [
        { id: 'e_end1', pose: 'couch_invite', mood: 'calling her up, warm', text: 'Ten seconds, sweet thing. Finish pretty. I’m watching the dismount.' },
        { id: 'e_end2', pose: 'lean_fwd', mood: 'low', text: '[low] The hole’s closing. Take one last thing on the way up.' },
      ] },
      'draft-autopick': { cd: 120000, chance: 0.45, lines: [
        { id: 'e_apk1', pose: 'table_lean', mood: 'indulgent', text: 'Couldn’t decide, so I chose for you. [chuckles] I have excellent taste, don’t worry.' },
        { id: 'e_apk2', pose: 'seated', mood: 'wry', text: 'Time’s up, so the table picked. It knows what you like better than you do, kitten.' },
      ] },
      'gold-first': { cd: 1, chance: 1, lines: [
        { id: 'e_gld1', pose: 'table_lean', mood: 'the first gold; merchant delight', once: true, prop: 'gold', text: 'That shine in your hand? Gold, kitten. It spends at my bench, and ONLY at my bench. [low] Careful. It’s habit-forming.' },
      ] },
      'duo-demo': { cd: 1, chance: 1, lines: [
        { id: 'e_duo1', pose: 'table_lean', mood: 'genuine delight', once: true, prop: 'cards', text: 'A gold card? [giggles] Your toys learned to work together, kitten. That’s rarer than you know. Take it.' },
      ] },
      'tease-debut': { cd: 1, chance: 1, lines: [
        { id: 'e_tsb1', pose: 'lean_fwd', mood: 'the one warning; all blade', once: true, prop: 'tease', text: 'Red one, kitten. Stop. That one WANTS your hand. Touch it and it fires. Let it leave unanswered and it pays you for the restraint. [low] Don’t.' },
      ] },
    },

    // ---- hub ambience (the Warren, post-arc; local-session cooldowns) ----
    hub: {
      greeting: { cd: 21600000, chance: 0.55, lines: [
        { id: 'h_gr1', pose: 'couch_invite', mood: 'welcoming a regular', text: 'There she is. The burrow’s been quiet without you, kitten.' },
        { id: 'h_gr2', pose: 'seated', mood: 'shop open, lazy', text: 'Welcome back, sunshine. The bench is open, the hole is hungry, and I’m all ears.' },
        { id: 'h_gr3', pose: 'table_lean', mood: 'merchant patter', text: 'Back for more? [chuckles] Of course you are. Nobody visits the Warren just once.' },
        { id: 'h_gr4', pose: 'curled', mood: 'sleepy warmth', text: 'Mm. You again. Good. I was starting to talk to the furniture.' },
        { id: 'h_gr5', pose: 'base', mood: 'light tease', text: 'You’ve got that look, kitten. The one that means you’re about to fall down a hole on purpose.' },
        { id: 'h_gr6', pose: 'standing', mood: 'brisk', text: 'Perfect timing. The hole’s in a generous mood today... [low] I can always tell.' },
      ] },
      idle: { cd: 300000, chance: 0.25, lines: [
        { id: 'h_id1', pose: 'curled', mood: 'idle musing', text: 'The rooms rearranged themselves again last night. [sighs] Nobody asks the furniture what it wants.' },
        { id: 'h_id2', pose: 'seated', mood: 'nudging', text: 'Browsing is free, kitten. Falling pays better.' },
        { id: 'h_id3', pose: 'table_lean', mood: 'shop talk', text: 'Emotes level. Gold unlocks. The liturgy hasn’t changed since you last checked, hon. [chuckles]' },
        { id: 'h_id4', pose: 'base', mood: 'idle observation', text: 'You can hear it if you stand still, you know. The hole. It hums when it’s waiting for someone.' },
        { id: 'h_id5', pose: 'couch_invite', mood: 'soft sell', text: 'No rush. The hole keeps. [low] It always keeps.' },
      ] },
    },
  },
};
