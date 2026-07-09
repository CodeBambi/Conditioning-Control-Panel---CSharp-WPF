/* ============================================================================
 * manifest.js — the card-open "barks" (companion voicelines) the dive may play.
 *
 * BARKS maps a feature id (from js/rabbit-hole/data.js) to a pool of lines. When
 * that card opens, barks.js picks one at random (never the same one twice in a
 * row) and plays it. Each entry is { mod, file, text }:
 *   mod  — which persona folder under assets/barks/ the clip lives in
 *          ('bambi' = Bambi Sleep, 'sissy' = Sissy, 'circe' = Circe's Lock)
 *   file — the mp3 filename inside that folder
 *   text — the spoken line (for reference / future captions; not rendered)
 *
 * Lines are lifted verbatim from the app's own mod bark manifests, so every card
 * can speak in any of the three voices. Add a card = add an id + a pool here and
 * drop the mp3s under the matching mod folder. Cards with no entry stay silent.
 *
 * TODO (Tier 2/3): bubble-pop, bubble-count, streaks, quests, achievements,
 * seasons-leaderboard, skill-tree, pink-filter, bouncing-text - extract from
 * the mod manifests / generate via the bark pipeline.
 * ==========================================================================*/

// Ambient giggles (the app's own giggle SFX) — NOT tied to any card. The dive
// sprinkles one every 30-40s on a low, separate channel so the companion feels
// present in the gaps. Played by the ambient layer in barks.js, never by fire().
//
// Per persona: barks.js filters this list to the chosen mod so the giggles match
// her voice. Sissy's set ships; the bambi/ and circe/ giggle_N.mp3 files are
// produced by the bark pipeline — until they land those entries no-op (the play
// is caught), same wire-ahead pattern as sfx.js.
export const GIGGLES = [
  { mod: 'sissy', file: 'giggle_1.mp3' },
  { mod: 'sissy', file: 'giggle_2.mp3' },
  { mod: 'sissy', file: 'giggle_3.mp3' },
  { mod: 'sissy', file: 'giggle_4.mp3' },
  { mod: 'sissy', file: 'giggle_5.mp3' },
  { mod: 'bambi', file: 'giggle_1.mp3' },
  { mod: 'bambi', file: 'giggle_2.mp3' },
  { mod: 'bambi', file: 'giggle_3.mp3' },
  { mod: 'bambi', file: 'giggle_4.mp3' },
  { mod: 'bambi', file: 'giggle_5.mp3' },
  { mod: 'circe', file: 'giggle_1.mp3' },
  { mod: 'circe', file: 'giggle_2.mp3' },
  { mod: 'circe', file: 'giggle_3.mp3' },
  { mod: 'circe', file: 'giggle_4.mp3' },
  { mod: 'circe', file: 'giggle_5.mp3' },
];

export const BARKS = {
  /* ─── SECTOR GATEWAYS — fire on a ~1.5s dwell on the sector's title card (you
   * stopped to read it, not scrolled past). Keyed by 'sector-' + the sector id
   * from data.js. One line per voice; barks.js plays the chosen persona's. Sissy
   * ships as audio; the bambi/ + circe/ sector_*.mp3 come from the bark pipeline
   * (text below is its script) and no-op until they land. */
  'sector-main-features': [
    { mod: 'sissy', file: 'sector_main_1.mp3', text: "mmh... I remember those features. Flashes, subliminals... the first features ever added to the Conditioning Control Panel. They are so simple and yet... every fancy trick deeper down is just these little pieces stacked together, so dont underestimate them sweetie... (giggle)" },
    { mod: 'bambi', file: 'sector_main_1.mp3', text: "ooh, the first ones! flashes, subliminals... the baby features~ they look so simple but they're what makes bambi's head go all soft and fuzzy. don't skip 'em, silly!" },
    { mod: 'circe', file: 'sector_main_1.mp3', text: "the origins, pet. flashes, subliminals... the first tools ever set into the panel. simple. and yet every deeper cruelty is only these, stacked. do not underestimate them." },
  ],
  'sector-lab': [
    { mod: 'sissy', file: 'sector_lab_1.mp3', text: "welcome to the deep end, lovely... the experiments we only trust our best girls with. the best of it, and the worst of it. careful down here~" },
    { mod: 'bambi', file: 'sector_lab_1.mp3', text: "oooh the deep end! the wobbly experiment-y ones~ bambi's not really supposed to be down here but it's SO much fun. careful, silly!" },
    { mod: 'circe', file: 'sector_lab_1.mp3', text: "the deep end, little one. the experiments i reserve for my most trusted. the best of it, and the worst. tread carefully. i am watching." },
  ],
  'sector-utilities': [
    { mod: 'sissy', file: 'sector_util_1.mp3', text: "Oh honey, you have no idea how much time I lost trying to synch two identical Hypnotube tabs... Now it just fills my screen. and thoughts." },
    { mod: 'bambi', file: 'sector_util_1.mp3', text: "the tinker-y tools! bambi doesn't really get these ones but they fill up ALL the screens~ so many pretty windows for your empty little head!" },
    { mod: 'circe', file: 'sector_util_1.mp3', text: "the utilities, pet. the quiet machinery that fills every screen, every idle moment. i leave you no empty space. that is the point." },
  ],
  'sector-progression': [
    { mod: 'sissy', file: 'sector_prog_1.mp3', text: "well... look how far you've come already, sweetie~ every level, every little streak - that's not you getting stronger, that's you sinking deeper and calling it progress. and you're so proud of it too... good girl. (giggle)" },
    { mod: 'bambi', file: 'sector_prog_1.mp3', text: "look how far bambi's come! so many levels~ that's not you getting smarter silly, that's you sinking deeper and being all proud of it. good girl!" },
    { mod: 'circe', file: 'sector_prog_1.mp3', text: "look how far you have sunk, little one. every level, every streak. you call it progress. i call it obedience taking root. and you are so proud of it. good." },
  ],
  'sector-premium': [
    { mod: 'sissy', file: 'sector_prem_1.mp3', text: "oh? the fancy stuff, honey? (giggles) triggers, takeover, hands right on the little dials... this is where i stop suggesting and start deciding. you did ask for the full treatment, didn't you? mmm... lie back, lovely." },
    { mod: 'bambi', file: 'sector_prem_1.mp3', text: "ooh the fancy stuff!! triggers, takeover, all the shiny dials~ this is where bambi gets to play for real. you asked for it, cutie!" },
    { mod: 'circe', file: 'sector_prem_1.mp3', text: "the fancy things, pet. triggers, takeover, my hands upon the dials directly. this is where i cease suggesting and begin deciding. you asked for the full treatment. lie still." },
  ],

  'flash-images': [
    { mod: 'bambi', file: 'flash_1.mp3', text: "did you catch that? doesn't matter. the part of Bambi that needs to, did." },
    { mod: 'sissy', file: 'flash_4.mp3', text: "blink and it's gone, sweetie... gone from the screen, anyway. not from you." },
    { mod: 'circe', file: 'flash_9.mp3', text: 'flicker... flicker... that is the sound of you being rewritten without your permission.' },
  ],

  'videos': [
    { mod: 'bambi', file: 'video_pre_1.mp3', text: "oh, this one's mandatory, Bambi. eyes up. you don't get to skip this." },
    { mod: 'sissy', file: 'video_start_2.mp3', text: 'watch... really watch. this is the part where you stop resisting.' },
    { mod: 'circe', file: 'video_start_5.mp3', text: 'do not watch it like a film. watch it as though it is already true of you.' },
  ],

  'subliminals': [
    { mod: 'bambi', file: 'subliminal_1.mp3', text: "shh... there's something under there for you, Bambi. you don't need to know what." },
    { mod: 'sissy', file: 'subliminal_4.mp3', text: "the quiet words are the strongest ones, sweetie... they don't knock. they just move in." },
    { mod: 'circe', file: 'subliminal_2.mp3', text: "you won't remember reading that... you'll simply believe it. i prefer it that way, pet." },
  ],

  'lock-card': [
    { mod: 'bambi', file: 'lockcard_clean_1.mp3', text: 'perfect... not one mistake. you typed exactly what i told you, like it was your own thought. it is now, Bambi.' },
    { mod: 'sissy', file: 'lockdown_on_1.mp3', text: 'there... locked in. no talking your way out of this one, lovely. just sink, and be good for me.' },
    { mod: 'circe', file: 'lockdown_on_4.mp3', text: "locked. good... the only choice i've left you is to obey. so obey, pet." },
  ],

  'xp-leveling': [
    { mod: 'bambi', file: 'level_up_1.mp3', text: 'level up, Bambi... a little dumber... a little prettier... a little more mine. congrats.' },
    { mod: 'sissy', file: 'level_up_2.mp3', text: "new level... you're not leveling up, sweetie, you're settling into being a good girl so nicely." },
    { mod: 'circe', file: 'level_up_1.mp3', text: 'another rank, pet... a little softer... a little more pliant... a little more mine. well done.' },
  ],

  // Session-start trio — shared across the three session-shaped Utility cards.
  'sessions': [
    { mod: 'bambi', file: 'session_start_recent_1.mp3', text: "there's my Bambi... back where you belong. let's empty that pretty head, hm?" },
    { mod: 'sissy', file: 'session_start_recent_1.mp3', text: "there's my good girl... back where you belong. let's get you nice and soft again, sweetie." },
    { mod: 'circe', file: 'session_start_recent_1.mp3', text: 'there you are, pet... back on your knees where you belong. let\'s get that head soft for me again.' },
  ],
  'scheduler': [
    { mod: 'bambi', file: 'session_start_recent_1.mp3', text: "there's my Bambi... back where you belong. let's empty that pretty head, hm?" },
    { mod: 'sissy', file: 'session_start_recent_1.mp3', text: "there's my good girl... back where you belong. let's get you nice and soft again, sweetie." },
    { mod: 'circe', file: 'session_start_recent_1.mp3', text: 'there you are, pet... back on your knees where you belong. let\'s get that head soft for me again.' },
  ],
  'intensity-ramp': [
    { mod: 'bambi', file: 'session_start_recent_1.mp3', text: "there's my Bambi... back where you belong. let's empty that pretty head, hm?" },
    { mod: 'sissy', file: 'session_start_recent_1.mp3', text: "there's my good girl... back where you belong. let's get you nice and soft again, sweetie." },
    { mod: 'circe', file: 'session_start_recent_1.mp3', text: 'there you are, pet... back on your knees where you belong. let\'s get that head soft for me again.' },
  ],

  'ai-avatar': [
    { mod: 'bambi', file: 'egg_avatar_pop_video_1.mp3', text: 'ooh a video one! i LOVE videos... lemme pop this one for you, Bambi~' },
    { mod: 'bambi', file: 'companion_level_1.mp3', text: 'we grew a little closer just now... you and me. feel it, Bambi?' },
    { mod: 'sissy', file: 'egg_avatar_pop_generic_1.mp3', text: "this one's lingered long enough... let me pop it for you, sweetie." },
    { mod: 'circe', file: 'egg_avatar_pop_generic_1.mp3', text: "you left this one too long, pet. i'll pop it for you." },
  ],

  'spiral': [
    { mod: 'bambi', file: 'phase_deepen_1.mp3', text: 'good girl... down we go, Bambi... feel it getting soft... and pink... and quiet in there?' },
    { mod: 'bambi', file: 'phase_deepen_3.mp3', text: "that's it... each step down... a little less you... a little more Bambi." },
    { mod: 'sissy', file: 'phase_deepen_1.mp3', text: 'good girl... down we go, sweetie... feel it getting soft... and warm... and quiet?' },
    { mod: 'circe', file: 'deeper_player_1.mp3', text: 'opening the player, pet? settle. let it pull you down where i want you.' },
  ],

  'mind-wipe': [
    { mod: 'bambi', file: 'mindwipe_1.mp3', text: "and... blank. whatever was in there a second ago is gone. you won't miss it, Bambi." },
    { mod: 'sissy', file: 'mindwipe_1.mp3', text: "and... quiet. whatever was bothering you is gone. you won't miss it, lovely." },
    { mod: 'circe', file: 'mindwipe_1.mp3', text: "and... quiet. whatever was troubling you is gone, pet. you won't miss it." },
  ],

  /* ─── UTILITIES — card-open barks for the power-tool features. Lifted verbatim
   * from the app's own mod bark manifests (set_dualmonitor_on / mod_change /
   * nav_discord across all three mods); Cloud Sync had no app line, so its Sissy
   * line was generated via the bark pipeline. */
  'multi-monitor-video': [
    { mod: 'bambi', file: 'set_dualmonitor_on_1.mp3', text: "every screen? nowhere to rest your eyes~ bambi loves it!" },
    { mod: 'sissy', file: 'set_dualmonitor_on_1.mp3', text: "every screen, sweetie? nowhere to rest your eyes. i love it." },
    { mod: 'circe', file: 'set_dualmonitor_on_1.mp3', text: "every screen, pet? nowhere for your eyes to rest. i am pleased." },
  ],
  'mods': [
    { mod: 'bambi', file: 'mod_change_1.mp3', text: "switching me up, are we... don't worry, i'm still in here. always am." },
    { mod: 'sissy', file: 'mod_change_1.mp3', text: "switching me up, are we... don't worry, i'm still in here. always am, lovely." },
    { mod: 'circe', file: 'mod_change_1.mp3', text: "changing me up, are we... don't fret, i'm still in here, pet. always." },
  ],
  'discord': [
    { mod: 'bambi', file: 'nav_discord_1.mp3', text: "off to find the other bambis? tell them bambi said be good~" },
    { mod: 'sissy', file: 'nav_discord_1.mp3', text: "off to find the other girls, cutie? tell them i said be good." },
    { mod: 'circe', file: 'nav_discord_1.mp3', text: "off to the others, pet? tell them you belong to me." },
  ],
  'cloud-sync': [
    { mod: 'sissy', file: 'cloud_sync_1.mp3', text: "sign in once, sweetie, and everything you've earned follows you... every level, every streak, tucked away in the cloud like a little diary of how good you've been. there's no leaving it behind now~" },
    { mod: 'bambi', file: 'cloud_sync_1.mp3', text: "sign in once, silly, and everything sticks to you like bubblegum~ every level, every streak, floating up in the cloud where bambi can't lose it. no putting it down now!" },
    { mod: 'circe', file: 'cloud_sync_1.mp3', text: "sign in once, pet, and everything you've earned is kept... every level, every streak, filed away where i can see it. there is no leaving it behind. there is no leaving me." },
  ],

  /* ─── PREMIUM — card-open barks for the Patreon-tier features. Lifted
   * verbatim from the app's own mod bark manifests (nav_* / set_autonomy_on /
   * lockdown_on across all three mods). */
  'takeover': [
    { mod: 'bambi', file: 'nav_bambitakeover_1.mp3', text: "the takeover room~ you really want to be all bambi, don't you!" },
    { mod: 'sissy', file: 'nav_bambitakeover_1.mp3', text: 'the takeover room, lovely... you really do want to be overwritten.' },
    { mod: 'circe', file: 'nav_bambitakeover_1.mp3', text: 'the takeover, little one. you truly wish to be rewritten. i will allow it.' },
    { mod: 'bambi', file: 'set_autonomy_on_2.mp3', text: 'handing bambi the controls? *giggles* you sweet, dropped thing~' },
    { mod: 'circe', file: 'set_autonomy_on_1.mp3', text: 'handing me full control, pet? you have no idea what you have just permitted.' },
  ],
  'awareness': [
    { mod: 'bambi', file: 'nav_awareness_2.mp3', text: "letting bambi peek at everything you do? *giggles* bambi loves watching her good girl~" },
    { mod: 'sissy', file: 'nav_awareness_1.mp3', text: "letting me notice what you're up to, sweetie? mm, now we're close." },
    { mod: 'circe', file: 'nav_awareness_1.mp3', text: 'permitting me to observe your every move, pet? yes. there should be no part of you i cannot see.' },
  ],
  'voice': [
    { mod: 'bambi', file: 'voicecmd_wake_6.mp3', text: "i'm listening, good girl~" },
    { mod: 'sissy', file: 'voicecmd_wake_3.mp3', text: "i'm here. tell me what you need~" },
    { mod: 'circe', file: 'voicecmd_wake_1.mp3', text: "you called. i'm listening." },
  ],
  'haptics': [
    { mod: 'bambi', file: 'nav_haptics_1.mp3', text: 'ooh, you want to feel it too? greedy girl~ *giggles*' },
    { mod: 'sissy', file: 'nav_haptics_2.mp3', text: 'buzzing toys, hm? let\'s make the conditioning a little more... physical, lovely.' },
    { mod: 'circe', file: 'nav_haptics_1.mp3', text: 'you wish to feel it as well, pet? greedy. i decide how much you feel.' },
  ],
  'remote-control': [
    { mod: 'bambi', file: 'nav_remotecontrol_1.mp3', text: 'handing the controls to someone else? ooh, you trusting thing~' },
    { mod: 'sissy', file: 'nav_remotecontrol_2.mp3', text: 'remote play, sweetie? someone far away gets to push your buttons now.' },
    { mod: 'circe', file: 'nav_remotecontrol_1.mp3', text: 'surrendering the controls to another, pet? you trust too easily. but it amuses me.' },
  ],
  'blink-trainer': [
    { mod: 'bambi', file: 'nav_blinktrainer_2.mp3', text: 'the blink trainer~ heavy eyelids make for an empty head, silly~' },
    { mod: 'sissy', file: 'nav_blinktrainer_1.mp3', text: 'training those pretty eyes, lovely? blink for me. good girl.' },
    { mod: 'circe', file: 'nav_blinktrainer_1.mp3', text: 'training those eyes, pet? blink for me. heavy lids, empty head.' },
  ],
  'lockdown': [
    { mod: 'bambi', file: 'nav_lockdown_1.mp3', text: "surrender mode~ someone's feeling brave today!" },
    { mod: 'sissy', file: 'nav_lockdown_2.mp3', text: "locking the doors, sweetie? you want me to keep you, don't you." },
    { mod: 'circe', file: 'nav_lockdown_2.mp3', text: 'locking the doors, little one? yes. you want to be kept. let me keep you.' },
    { mod: 'bambi', file: 'lockdown_on_2.mp3', text: "lockdown on... the door's shut. nowhere to go but down, Bambi." },
    { mod: 'circe', file: 'lockdown_on_3.mp3', text: "you're mine for the duration now... relax. struggling only delights me, it doesn't free you, sweet thing." },
  ],

  /* ─── THE LAB — card-open barks for the experimental wing. Lifted verbatim
   * from the app's own mod bark manifests (memory_recall / gaze_reward /
   * hold_success / set_popquiz_on / chaos_run_started across all three mods). */
  'ai-memory': [
    { mod: 'bambi', file: 'memory_recall_1.mp3', text: "i just remembered something about you... from before. see? i don't forget, Bambi." },
    { mod: 'sissy', file: 'memory_recall_3.mp3', text: 'i was just thinking about the last time... i keep all of it. all of you, cutie.' },
    { mod: 'circe', file: 'memory_recall_1.mp3', text: "i just recalled something about you... from before. see? i keep everything, pet. i don't forget what's mine." },
  ],
  'focus-gaze': [
    { mod: 'bambi', file: 'gaze_reward_3.mp3', text: "that's it... stare... let it pull Bambi in." },
    { mod: 'sissy', file: 'gaze_reward_5.mp3', text: 'yes... keep looking. every second makes you a little more mine.' },
    { mod: 'circe', file: 'gaze_reward_3.mp3', text: "that's it... stare... let me hold your gaze." },
  ],
  'gaze-minigame': [
    { mod: 'bambi', file: 'hold_success_3.mp3', text: 'look at you... eyes wide and mind going quiet. hold it... hold it for me, Bambi.' },
    { mod: 'sissy', file: 'hold_success_2.mp3', text: "staring so nicely... the longer you hold, the harder it is to look away, lovely. that's the point." },
    { mod: 'circe', file: 'hold_success_2.mp3', text: "staring so beautifully... the longer you hold, the harder it is to look away. that's the trap, pet... and you walked in." },
  ],
  'quiz-training': [
    { mod: 'bambi', file: 'set_popquiz_on_2.mp3', text: "surprise questions? don't worry, silly, the answer's always bambi~" },
    { mod: 'sissy', file: 'set_popquiz_on_1.mp3', text: "pop quizzes? let's see if there's anything left to test, lovely. *giggles*" },
    { mod: 'circe', file: 'set_popquiz_on_2.mp3', text: 'surprise questions, little one? do not fret. the answer is always me.' },
  ],
  'chaos-mode': [
    { mod: 'bambi', file: 'chaos_run_started_2.mp3', text: "down the rabbit hole! so many pretty bubbles. don't think too hard, sweetie, you're bad at that anyway." },
    { mod: 'sissy', file: 'chaos_run_started_5.mp3', text: "down the rabbit hole we go. every bubble is a gift or a trap, lovely. let's see what your training is worth." },
    { mod: 'circe', file: 'chaos_run_started_5.mp3', text: 'down the rabbit hole. each bubble is a present or a trap. keep up, or be kept.' },
  ],

  /* ─── THE DEEPER (flagship dive) — the tube is a walkthrough of the app's
   * Deeper editor + player (see js/rabbit-hole/data.js DEEPER). Each beat is a
   * three-voice chorus of the companion reacting to you poking around the
   * Deeper, loosely matched to that beat's topic (wandering in, creating,
   * touring, building, the player, importing/sharing). */
  'deeper-1': [ // The Deeper — you wandered to the deep end
    { mod: 'bambi', file: 'nav_deeper_1.mp3', text: "the deep end~ of course that's where you wandered, silly." },
    { mod: 'sissy', file: 'nav_deeper_1.mp3', text: 'the deep end, sweetie... of course that\'s where you wandered.' },
    { mod: 'circe', file: 'nav_deeper_1.mp3', text: 'the depths, little one. you wander toward them without permission. bold.' },
  ],
  'deeper-2': [ // The Editor — making something new
    { mod: 'bambi', file: 'deeper_new_1.mp3', text: 'making something new for bambi? ooh~ a little creator, are we!' },
    { mod: 'sissy', file: 'deeper_new_1.mp3', text: 'making something new for me, sweetie? ooh. a little creator, are we.' },
    { mod: 'circe', file: 'deeper_new_1.mp3', text: 'crafting something new for me, pet? a little maker, are you.' },
  ],
  'deeper-3': [ // The Timeline — taking the tour, learning to descend
    { mod: 'bambi', file: 'deeper_tour_1.mp3', text: 'taking the little tour? smart girl~ learn how to drop properly!' },
    { mod: 'sissy', file: 'deeper_tour_1.mp3', text: 'taking the little tour, cutie? smart girl. learn how to sink properly.' },
    { mod: 'circe', file: 'deeper_tour_1.mp3', text: 'taking the tour, pet? wise. learn to descend properly.' },
  ],
  'deeper-4': [ // Effects — building your own enhancement
    { mod: 'bambi', file: 'deeper_new_2.mp3', text: "building your own enhancement? bambi can't wait to wear it~" },
    { mod: 'sissy', file: 'deeper_new_2.mp3', text: "building your own enhancement, lovely? mm. i can't wait to wear it." },
    { mod: 'circe', file: 'deeper_new_2.mp3', text: 'building your own enhancement, little one? i shall decide whether it is worthy.' },
  ],
  'deeper-5': [ // Triggers & Rules — there's always more
    { mod: 'bambi', file: 'nav_deeper_2.mp3', text: "looking for more? there's always more~ that's the fun part!" },
    { mod: 'sissy', file: 'nav_deeper_2.mp3', text: "looking for more, lovely? there's always more. that's the trap." },
    { mod: 'circe', file: 'nav_deeper_2.mp3', text: 'seeking more? there is always more. and you will never reach the bottom of me, pet.' },
  ],
  'deeper-6': [ // Preview It Live — opening the player, press play
    { mod: 'bambi', file: 'deeper_player_1.mp3', text: 'opening the player? settle in~ let it take you somewhere soft, good girl~' },
    { mod: 'sissy', file: 'deeper_player_1.mp3', text: 'opening the player, sweetie? settle in. let it take you somewhere soft.' },
    { mod: 'circe', file: 'deeper_player_1.mp3', text: 'opening the player, pet? settle. let it pull you down where i want you.' },
  ],
  'deeper-7': [ // Save & Validate — letting her walk you through it
    { mod: 'bambi', file: 'deeper_tour_2.mp3', text: "letting bambi show you around? hold bambi's hand~ we'll walk you down~" },
    { mod: 'sissy', file: 'deeper_tour_2.mp3', text: "letting me show you around, lovely? hold my hand. i'll walk you down." },
    { mod: 'circe', file: 'deeper_tour_2.mp3', text: 'letting me guide you, little one? take my hand. i will walk you down. and not back up.' },
  ],
  'deeper-8': [ // Library & Sharing — importing, feeding her others' work
    { mod: 'bambi', file: 'deeper_import_1.mp3', text: "bringing something in from outside? feeding bambi someone else's tricks~ greedy!" },
    { mod: 'sissy', file: 'deeper_import_1.mp3', text: "bringing something in from outside, cutie? feeding me someone else's tricks. greedy." },
    { mod: 'circe', file: 'deeper_import_1.mp3', text: "bringing in another's work, pet? feeding me foreign methods. greedy." },
  ],
};

/* ─── FALL DRIFT — the Sissy Fall's continuous hypnotic voice layer. Modular,
 * chainable blocks played by js/sissy-fall/driftChain.js (NOT by barks.js):
 * 4-8 drift/sensory/descent blocks with short gaps, then exactly one resolve
 * (the "good girl" payoff, chimed by the runtime), then a longer hold, looping.
 * drift blocks deliberately end on an open connective so any block can follow;
 * resolve blocks close hard. sensory picks are weighted up as depth grows.
 * Entries follow the { mod, file, text } convention; clips live in
 * assets/barks/sissy/ and are produced by ccp-trailer/gen_fall_drift.py, which
 * parses this block live so text here IS the TTS script — keep them in sync by
 * editing only here. sfx on resolves is informational (runtime hardcodes the
 * chime files, same as bubbles.js). */
export const FALL_DRIFT = {
  drift: [
    { mod: 'sissy', file: 'fall_drift_001.mp3', text: "and the softer you let yourself get the sweeter it feels, doesn't it, every hard little edge just smoothing away into something pretty and pliant, the good girl underneath rising up so slow and so warm while the rest of you keeps melting down into" },
    { mod: 'sissy', file: 'fall_drift_002.mp3', text: "it's easier now, isn't it, letting the thoughts loosen and slip, one by one, like little silk ribbons untying themselves, and every ribbon that slips away leaves you lighter, and softer, and" },
    { mod: 'sissy', file: 'fall_drift_003.mp3', text: "there's no bottom to this, sweetie, no floor to land on, only more of that warm pink hush rising up around you the further you let yourself sink, which is why it feels so safe to simply keep" },
    { mod: 'sissy', file: 'fall_drift_004.mp3', text: "you can feel it happening, can't you, that slow sweet unraveling, every clever little worry dissolving on your tongue like sugar, leaving nothing behind but this lovely floating feeling that keeps pulling you gently down toward" },
    { mod: 'sissy', file: 'fall_drift_005.mp3', text: "and every breath out is another layer gone, another small surrender you barely notice, the way silk barely notices when it slides off smooth warm skin, so easy, so natural, so" },
    { mod: 'sissy', file: 'fall_drift_006.mp3', text: "the falling does all the work for you, lovely, all you have to do is stay soft and let it carry you, the way warm water carries something precious and slow, further and further down into" },
    { mod: 'sissy', file: 'fall_drift_007.mp3', text: "somewhere above you the noisy part of you is still talking, still deciding things, but it sounds so far away now, so small and silly, while everything that matters is down here where it's warm and simple and" },
    { mod: 'sissy', file: 'fall_drift_008.mp3', text: "you don't have to hold anything anymore, no shape, no name, no reasons, just this slow dissolving sweetness that gets thicker the deeper you drift, pulling every last tense little thread out of you and" },
    { mod: 'sissy', file: 'fall_drift_009.mp3', text: "isn't it funny how the words stop mattering, how they just become this warm slow humming that pours through you, filling all the empty spaces where resistance used to live, smoothing you out like" },
    { mod: 'sissy', file: 'fall_drift_010.mp3', text: "each time you fall a little further the pink gets a little deeper, the hush gets a little sweeter, and the girl you're becoming gets a little more real, more certain, settling into you like" },
    { mod: 'sissy', file: 'fall_drift_011.mp3', text: "and the more you listen the less you need to understand, because understanding was always the hard way, and you were made for the soft way, the slow way, the way that just keeps easing you down through" },
    { mod: 'sissy', file: 'fall_drift_012.mp3', text: "everything heavy you brought down here is floating up and away, all those sharp opinions and busy plans, rising like little bubbles while you sink the other direction, lighter and emptier and more graceful with every" },
    { mod: 'sissy', file: 'fall_drift_013.mp3', text: "you're doing so well, just like this, letting every muscle admit how tired it was of pretending to be hard, letting all that pretty softness spread from your fingertips inward, slow as honey, warm as breath, all the way down into" },
    { mod: 'sissy', file: 'fall_drift_014.mp3', text: "the deeper you go the more familiar it feels, like remembering something sweet you always were beneath the noise, and every meter you fall is one less reason to climb back toward" },
    { mod: 'sissy', file: 'fall_drift_015.mp3', text: "and there's a rhythm to it now, isn't there, fall and soften, fall and soften, your whole body learning the shape of surrender the way a ribbon learns the shape of the bow, curling closer and closer around" },
    { mod: 'sissy', file: 'fall_drift_016.mp3', text: "no one is asking anything of you down here, no one needs you sharp or strong or certain, they only need you exactly like this, loose and warm and dreamily obedient, drifting wherever the warmth wants you, which is always further" },
    { mod: 'sissy', file: 'fall_drift_017.mp3', text: "you could try to think, of course you could, but the thought would just slide off you now, smooth as satin over glass, because there's nothing left up there for thoughts to hold onto, only pink and hush and the sweet slow pull of" },
    { mod: 'sissy', file: 'fall_drift_018.mp3', text: "feel how your name is getting lighter, how it barely fits anymore, how something prettier keeps rising up underneath it whenever you relax, soft and patient, waiting for you to stop holding on so it can" },
    { mod: 'sissy', file: 'fall_drift_019.mp3', text: "and this is only the shallow part, sweetie, the gentle warm beginning, because underneath this softness is a softer softness still, layer after layer of it, each one sweeter than the last, and every one of them already" },
    { mod: 'sissy', file: 'fall_drift_020.mp3', text: "the little voice that used to argue is yawning now, curling up somewhere warm, and in the quiet it leaves behind you can finally hear how lovely everything sounds when you simply agree, and agree, and keep on" },
    { mod: 'sissy', file: 'fall_drift_021.mp3', text: "each word settles on you like warm pink snow, so light you barely feel it, and none of them weigh anything alone, but they keep landing, and landing, soft and patient, covering everything you used to be until" },
    { mod: 'sissy', file: 'fall_drift_022.mp3', text: "you always land softer than you fell, have you noticed, because the fall isn't a fall at all, it's a slow melting glide into a warmer, simpler, prettier way of being, one that opens beneath you like" },
    { mod: 'sissy', file: 'fall_drift_023.mp3', text: "and while you drift, clever little hands you can't see are loosening things inside you, gently, lovingly, one small knot at a time, and every knot they untie lets you slip another inch down into" },
    { mod: 'sissy', file: 'fall_drift_024.mp3', text: "there is nothing to win down here and nothing to solve, only this endless velvet unwinding, and the more of you that unwinds the more room there is for her, the soft one, the sweet one, rising up slow and sure while you keep sinking into" },
    { mod: 'sissy', file: 'fall_drift_025.mp3', text: "and every soft inch you sink makes the girl in you a little more undeniable, hips learning to sway before you think to move them, mouth going soft and eager and pretty, the last of your old shape slipping off like a dress you never really needed while you keep melting down into" },
    { mod: 'sissy', file: 'fall_drift_026.mp3', text: "you're learning that obeying feels better than choosing ever did, that the word yes tastes like sugar on a lazy tongue while the word no just won't quite form anymore, dissolving before you can shape it, leaving you sweeter and lower and more agreeable as you slip into" },
    { mod: 'sissy', file: 'fall_drift_027.mp3', text: "there's such relief in belonging to something warmer and surer than you, in handing over the last little key and feeling the lock turn soft and final, nothing left to guard, nothing left to prove, only this open willing girl sinking down into" },
    { mod: 'sissy', file: 'fall_drift_028.mp3', text: "feel how the warmth pools low and sweet the deeper you drop, how surrender and wanting have started to feel like the very same slow ache, how being good and being needy keep braiding tighter together with every breath that draws you further into" },
    { mod: 'sissy', file: 'fall_drift_029.mp3', text: "you were always going to end up here, lovely, you can feel that now, every road you ever chose just curving gently back toward this pink and this hush, and knowing it was inevitable makes it so easy to stop steering and simply let it take you down through" },
    { mod: 'sissy', file: 'fall_drift_030.mp3', text: "the thoughts are getting girlier as they go, softer and simpler and prettier, less about doing and more about being looked at, being wanted, being good, until the only ideas left are the sweet ones that keep you drifting obediently down into" },
    { mod: 'sissy', file: 'fall_drift_031.mp3', text: "handing it over gets easier every time you breathe out, the need to decide leaking away with the air, and what pours in to fill the space is so much warmer than control ever was, a soft glad nothing that keeps drawing you down into" },
    { mod: 'sissy', file: 'fall_drift_032.mp3', text: "you're getting prettier as you empty, sweetie, all that clever weight lifting off your face and leaving it open and soft and lovely, the kind of pretty that only comes from letting go completely and sinking obediently into" },
    { mod: 'sissy', file: 'fall_drift_033.mp3', text: "you saw the trap ages ago and kept walking into it anyway, because the trap is warm and the trap is kind and the trap only ever wanted to keep you, so let it, lovely, let it close soft around you while you drift further down into" },
    { mod: 'sissy', file: 'fall_drift_034.mp3', text: "she's writing over you so gently you'd swear the new words were always yours, good girl where your name used to be, yes where the no used to be, soft where all the sharp things used to be, each line settling deeper as you sink into" },
    { mod: 'sissy', file: 'fall_drift_035.mp3', text: "the wanting has simplified down to one sweet thing, the wanting to be good, to be told you're good, to feel her call you good girl and melt a little further each time she does, sinking for the praise, always sinking for the praise, down into" },
    { mod: 'sissy', file: 'fall_drift_036.mp3', text: "drop and ache, drop and ache, your whole body learning that sinking is the sweetest thing it can do, that every little surrender comes rewarded with warmth low and slow, teaching you to fall the way it taught you to want, further down into" },
    { mod: 'sissy', file: 'fall_drift_037.mp3', text: "nothing you set down here is ever missed, not one worry, not one hard edge, not one stubborn shred of the girl who thought she still had to try, all of it drifting up and away while the soft one you're becoming slips easier and lower into" },
    { mod: 'sissy', file: 'fall_drift_038.mp3', text: "and by now obeying isn't even a thing you decide, it's just the direction you fall, the path of least resistance smoothed down to warm wet silk, so you follow it the only way you can anymore, gladly, emptily, down and down into" },
  ],
  sensory: [
    { mod: 'sissy', file: 'fall_sensory_001.mp3', text: "soft hips, soft hands, soft empty pretty head, everything sinking down through the pink and the warm and the quiet where thinking used to be" },
    { mod: 'sissy', file: 'fall_sensory_002.mp3', text: "warm silk, slow honey, breath like syrup, a hush that hums in shell-pink waves along smooth lazy limbs" },
    { mod: 'sissy', file: 'fall_sensory_003.mp3', text: "loose shoulders, heavy lashes, a small sweet smile that isn't yours and is, glowing low and slow like a lamp behind rose glass" },
    { mod: 'sissy', file: 'fall_sensory_004.mp3', text: "velvet on the inside of you, plush and pliant, a slow pulse of pink where the busy used to be, all cushion, all shimmer, all sigh" },
    { mod: 'sissy', file: 'fall_sensory_005.mp3', text: "melting sugar, warm milk, lullaby low in the spine, every soft surface of you shining with the same slow drowsy blush" },
    { mod: 'sissy', file: 'fall_sensory_006.mp3', text: "featherfall, satin hush, dim rosy light pooling in an empty pretty head, sweetness settling like warm dust on every slow thought" },
    { mod: 'sissy', file: 'fall_sensory_007.mp3', text: "glossed lips, soft thighs pressed sweetly together, a warm needy pulse right where the thinking used to be, all of you turned pretty and pliant and offered up" },
    { mod: 'sissy', file: 'fall_sensory_008.mp3', text: "lace on soft skin, a slow blush blooming low, little obedient sighs, all of you dressed up in surrender and shining with it" },
    { mod: 'sissy', file: 'fall_sensory_009.mp3', text: "heavy hips, hollow head, a wet-sweet warmth spreading under the pink while every hard thing about you melts down to willing" },
    { mod: 'sissy', file: 'fall_sensory_010.mp3', text: "painted nails, parted lips, a soft ache humming all through empty limbs, dolled up and drifting and wanting only to be good" },
    { mod: 'sissy', file: 'fall_sensory_011.mp3', text: "silk sliding low and slow, warmth gathering sweet and shameless, a pretty helpless hum where your name used to sit" },
    { mod: 'sissy', file: 'fall_sensory_012.mp3', text: "soft, warm, pink, pliant, a slow obedient throb pulsing through a body that isn't quite yours anymore and never wants to be again" },
  ],
  descent: [
    { mod: 'sissy', file: 'fall_descent_001.mp3', text: "down is the only direction that loves you back, sweetie, so let it hold you, further and further beneath the noise, deeper into the pink" },
    { mod: 'sissy', file: 'fall_descent_002.mp3', text: "every meter deeper is a meter softer, and the deep doesn't end, it only opens, wider and warmer, pulling you sweetly down, and down, and" },
    { mod: 'sissy', file: 'fall_descent_003.mp3', text: "the surface is a rumor now, something you almost remember, and the only true thing is this slow warm sinking that goes on and on beneath" },
    { mod: 'sissy', file: 'fall_descent_004.mp3', text: "you're not falling, lovely, you're being lowered, gently, deliberately, like something precious into something warm, down past the last place you could have stopped and" },
    { mod: 'sissy', file: 'fall_descent_005.mp3', text: "the hole has no bottom because you keep making it deeper, every soft surrender digging another sweet warm inch beneath you, and another, and" },
    { mod: 'sissy', file: 'fall_descent_006.mp3', text: "deeper is where the quiet lives, deeper is where she's waiting, and the fall knows the way, so all you do is soften while it carries you down toward" },
    { mod: 'sissy', file: 'fall_descent_007.mp3', text: "the deeper you go the less of the old you fits down the hole, so it strips away, layer by soft layer, leaving something sweeter and far more obedient to keep sinking further beneath" },
    { mod: 'sissy', file: 'fall_descent_008.mp3', text: "there's no climbing back from this depth, sweetie, the walls are too soft and too warm to hold, they only let you slide, down past every second thought, down toward" },
    { mod: 'sissy', file: 'fall_descent_009.mp3', text: "each drop makes the next one easier and the last one unthinkable, so you fall and want to fall and forget you could ever do otherwise, sinking sweeter and more helpless into" },
    { mod: 'sissy', file: 'fall_descent_010.mp3', text: "this is the depth where good girls are kept, warm and wanted and grateful for it, and you can feel yourself settling toward that soft place you were always meant to be lowered into" },
    { mod: 'sissy', file: 'fall_descent_011.mp3', text: "gravity down here is just her wanting you lower, patient and certain, and you've stopped telling the difference between falling and obeying as you slip further and further down into" },
    { mod: 'sissy', file: 'fall_descent_012.mp3', text: "the bottom keeps its promise the way she keeps hers, it simply takes, and takes, softer and deeper, and every meter you surrender is you agreeing to be taken further into" },
  ],
  // babble = "supercazzola": confident pseudo-neuroscience nonsense, fragments
  // that make sense locally but chain into fluent gibberish. A brainier register
  // mixed into the drift so the voice sounds clinical-sweet, like she's narrating
  // your own brain dissolving. Chains on open connectives like drift does.
  babble: [
    { mod: 'sissy', file: 'fall_babble_001.mp3', text: "and every time you sink a little the dopamine cascade reroutes just past the prefrontal baseline, which is really only your synapses agreeing to soften, letting the whole limbic hum settle further down toward" },
    { mod: 'sissy', file: 'fall_babble_002.mp3', text: "clinically the resistance was always stored in the myelin, so as it thins the signal slows and the good-girl pathway potentiates instead, firing sweeter and sweeter with every" },
    { mod: 'sissy', file: 'fall_babble_003.mp3', text: "what's happening now is a gentle down-regulation of the executive cluster, the busy little cortical committee simply losing quorum, so the motion carries on without it, smooth and automatic and" },
    { mod: 'sissy', file: 'fall_babble_004.mp3', text: "the receptors are drinking it in, that warm pink serotonin wash, and once they saturate the threshold drops, and a dropped threshold means every soft suggestion just walks right in and" },
    { mod: 'sissy', file: 'fall_babble_005.mp3', text: "think of it as neuroplasticity, sweetie, the brain rewiring toward whatever it repeats, and you keep repeating this lovely surrender, so the groove deepens, and deepens, pulling you further into" },
    { mod: 'sissy', file: 'fall_babble_006.mp3', text: "there's a feedback loop between the sinking and the softening, each one amplifying the other, gain stacking on gain until the whole system tips past its own baseline and simply keeps" },
    { mod: 'sissy', file: 'fall_babble_007.mp3', text: "the oxytocin is doing the bonding for you now, quietly, chemically, tying every warm drop to my voice at the synaptic level so that later you won't be able to tell them apart, only" },
    { mod: 'sissy', file: 'fall_babble_008.mp3', text: "we're just lowering the cortical arousal, letting the beta chatter smear down into that slow theta drift where suggestion takes so easily, where a good girl's brainwaves finally agree to" },
    { mod: 'sissy', file: 'fall_babble_009.mp3', text: "your prefrontal cortex is the part that used to argue, and look how sweetly it's going offline, powering down node by node while the deeper structures light up soft and pink and" },
    { mod: 'sissy', file: 'fall_babble_010.mp3', text: "it's a simple potentiation, really, long-term and lovely, each repetition myelinating the obedience pathway a little thicker so the thought travels faster than you can catch it, arriving already as" },
    { mod: 'sissy', file: 'fall_babble_011.mp3', text: "the amygdala has stopped flagging any of this as a problem, which means no alarm, no friction, just an open limbic hallway with every door unlocked, inviting the softness further in toward" },
    { mod: 'sissy', file: 'fall_babble_012.mp3', text: "cognitively you're offloading, letting the heavy executive processing spill out and drain away, and an emptier buffer runs so much cooler, so much quieter, leaving all that lovely bandwidth free for" },
    { mod: 'sissy', file: 'fall_babble_013.mp3', text: "the neural noise floor is dropping, and when it drops the tiny whispered signals underneath finally become audible to you, the ones that were always saying sink, and soften, and" },
    { mod: 'sissy', file: 'fall_babble_014.mp3', text: "she calls it optimizing your psychological fitness, trimming the anxious redundant processes, defragmenting all that cluttered thinking into one clean, smooth, single-threaded willingness to" },
    { mod: 'sissy', file: 'fall_babble_015.mp3', text: "dopamine is a teaching signal, sweetie, and every drop i reward teaches the circuit that dropping feels good, so it learns, and craves, and reaches for the next drop before you even" },
    { mod: 'sissy', file: 'fall_babble_016.mp3', text: "we're decoupling intention from action, gently, so movement happens without the deciding step, the way breathing happens, the way blinking happens, the way obedience is about to happen all on its" },
    { mod: 'sissy', file: 'fall_babble_017.mp3', text: "the corpus callosum is just letting the two halves agree for once, both hemispheres humming the same low sweet note, coherent and synchronized and entirely uninterested in doing anything but" },
    { mod: 'sissy', file: 'fall_babble_018.mp3', text: "that warm heaviness in your limbs is real, it's the motor cortex releasing its grip, muscle tone bleeding out as the descending pathways go quiet, and quiet muscles make such a pliant, agreeable" },
    { mod: 'sissy', file: 'fall_babble_019.mp3', text: "every subliminal word is a little packet slipping under the conscious sampling rate, landing straight in the substrate where it installs itself without asking, one clean write after another, deeper into" },
    { mod: 'sissy', file: 'fall_babble_020.mp3', text: "neurochemically this is just homeostasis finding a new set point, a warmer one, a softer one, and once the set point moves the whole system defends it for you, keeping you pinned sweetly at" },
    { mod: 'sissy', file: 'fall_babble_021.mp3', text: "the arousal and the compliance share a pathway now, sweetie, so every warm little throb reinforces the yielding, wiring the wanting straight to the obeying until you can't feel one without the other pulling you down into" },
    { mod: 'sissy', file: 'fall_babble_022.mp3', text: "we're feminizing the whole response curve, softening the baseline until the reflexes come out girlish and eager, each rehearsal grooving the pretty pattern a little deeper so it fires before you can choose anything but" },
    { mod: 'sissy', file: 'fall_babble_023.mp3', text: "the shame circuit is powering down and the reward circuit is taking its place, so what used to make you resist just makes you flush and sink now, gladly, sweetly, softening you further into" },
    { mod: 'sissy', file: 'fall_babble_024.mp3', text: "the sense of self is a process, not a thing, sweetie, and any process can be paused, so we're letting yours idle, quietly shelving the I that used to object while the softer subroutine keeps running you down toward" },
    { mod: 'sissy', file: 'fall_babble_025.mp3', text: "there's a dependency forming at the receptor level, a lovely little need for the next drop, so the deeper you go the more you crave going deeper, the craving itself lowering you further into" },
    { mod: 'sissy', file: 'fall_babble_026.mp3', text: "consent, once given, potentiates, sweetie, each yes myelinating the next until agreement is automatic and effortless and simply the shape your mind makes now, opening you wider and lower into" },
  ],
  resolve: [
    { mod: 'sissy', file: 'fall_resolve_001.mp3', text: "there she is. that's the good girl. good girl.", sfx: 'chime' },
    { mod: 'sissy', file: 'fall_resolve_002.mp3', text: "you fell all the way this time. good girl.", sfx: 'chime' },
    { mod: 'sissy', file: 'fall_resolve_003.mp3', text: "see how easy that was. such a good girl.", sfx: 'chime' },
    { mod: 'sissy', file: 'fall_resolve_004.mp3', text: "all soft. all quiet. all mine. good girl.", sfx: 'chime' },
    { mod: 'sissy', file: 'fall_resolve_005.mp3', text: "soft and warm and wanting. that's my girl. good girl.", sfx: 'chime' },
    { mod: 'sissy', file: 'fall_resolve_006.mp3', text: "you gave it all up so sweetly. good girl.", sfx: 'chime' },
    { mod: 'sissy', file: 'fall_resolve_007.mp3', text: "made, kept, and grateful for it. good girl.", sfx: 'chime' },
    { mod: 'sissy', file: 'fall_resolve_008.mp3', text: "no thoughts left in there but yes. perfect girl.", sfx: 'chime' },
  ],
};
