# The Caucus Race - audio

One module, `race/audio.js`, owns every sound. Two doors: the hot beats are WebAudio in the page
on the dive's shared context (`engine/audioBus.js`, so the DtRH master colour applies); the
fanfares the host already ships (`Resources/sounds/chaos/*.mp3`) go out as `sfx` bridge messages.
`run.js` only calls `audio.sfx(name, scale)`, `audio.update(dt, { world, run, kart })` once per
step, `audio.duck(on, why)` on brake / host pause / end, and `audio.dispose()`. Everything else
audio.js reads off the live world it is handed: it subscribes to `field.onPop`, `score.onEvent`
and `items.onEvent` itself (re-subscribing when "again" builds a new world) and watches
per-frame edges (airborne, boost, drift, the Big Wheel span, the room).

`bubbles.js` plays its own kind-blind pop through `makeSfxPlayer`; that player honours
`shared/audioMute.isDucked()`, so audio.js sets ducked for the page's life and owns the pop.

## Levels and laws

- Master = host `init.settings.masterVolume / 100` (60 standalone). `M` toggles the dive's shared
  mute (`shared/audioMute.js`, persisted in localStorage as `rh-audio-muted`) with a HUD toast;
  a persisted mute is announced on boot. Muted = no in-page voices AND no host legs.
- One-shots are capped at **6 voices**; the 7th drops the quietest (logged, rate-limited 5 s).
  Loops (music, the speed bed, drift sparks) do not count.
- Pops climb with the combo: **+1 semitone per 4 combo, capped at +7**, plus +-20 cents of jitter
  so a chain never machine-guns. Rungs climb the chime ladder: x2 chime1, x3 chime2, x4 chime3,
  x6/x8 chime3 at +3/+6 semitones.
- Pops pan by lateral offset from the kart (`(x - kart.x) / ROAD_HALF_W * 0.6`).
- Music sits at `MUSIC_LEVEL` 0.3 (the OSTs are mastered at about -15.5 LUFS); the bed and sparks
  under it. No pitch shifting on the music, ever: room colour is filters only.
- Duck: brake and host pause (native video) pull the music to 10%; the end card to 45%; the first
  `update()` after resume lifts it (and the explicit `duck(false)` hooks do too).
- Autoplay: the run starts on a key, so the context is unlocked; if `play()` still rejects
  (`?autostart=1`), the track retries on the next pointer/key. A missing file marks the track dead,
  re-rolls that room from what is left, and never throws.

## Event map

| beat | source | in-page (WebAudio) | host leg (`sfx` name) |
|------|--------|--------------------|-----------------------|
| treat pop | `field.onPop` treat | Pop / Pop2 / Pop3 round-robin, combo pitch, panned, 0.34 | - |
| lucky pop | `field.onPop` lucky | Pop2 + chime1 at +5 (item incoming) | - |
| prism pop | `field.onPop` prism | Burst at +3 (its chained pops sound themselves) | - |
| golden pop | `field.onPop` golden | Burst 0.5 | `golden_pop` 0.9 |
| jackpot | `score` jackpot | GG 0.5 (minor 0.35), 120 ms after the Burst | - |
| effect pop | `field.onPop` effect (not "held") | Pop at -5 semis through a 1.1 kHz lowpass + 140>50 Hz sine thud | - |
| near miss | `score` almost | Pop2 at -4, 0.10 (the whisper) | - |
| rung up | `score` mult (to > from) | chime ladder (above) | swallowed (`streak_milestone` 0.6) |
| bank | `score` bank | thud (120>38 Hz sine + click) + chime1-2-3 arpeggio 90 ms | `streak_milestone` 0.9 or `pb_fanfare` 0.9 |
| boost (pad or sugar_rush) | `kart.boostSec` rising edge | noise whoosh, bandpass 300>3200 Hz, 0.7 s | `tunnel_powerup_collect` 0.8 |
| ramp launch | `kart.airborne` rising | sine rise 220>700 Hz, 0.36 s, soft | - |
| ramp land | `kart.airborne` falling | thump 110>42 Hz + click | - |
| Big Wheel | `layout.featuresBetween` loop | two noise sweeps up then down, 1.3 s + 1.0 s | - |
| item roll | `items` itemRoll | 6 Pop3 ticks over 0.85 s, pitch rising | swallowed (`ui_click` 0.5) |
| item arm | `items` itemArm | chime1 at +4 | swallowed (`ui_click` 0.4) |
| item use | `items` itemUse | Pop2 at -3 + noise puff | `tunnel_powerup_collect` 0.7 |
| tea time in / out | run.js | lowpass 1.4 kHz on the music while timeScale < 1 | `time_slow_in` / `time_slow_out` |
| gate (room change) | `run.room.id` edge | chime2 at -3 + the crossfade | `depth_change` 0.7 |
| brake open / close | `duck('brake')` | Pop at -7 / Pop2 at +3 | `ui_click` 0.5 (open) |
| end card | `sfx('surface')` | chime arpeggio 130 ms + chime3 at +5 | `surface` 0.8 |

Every host name is verified against `Resources/sounds/chaos/` (`HOST_SFX` in audio.js is the
closed set; anything else is dropped and logged). A leg an in-page event already covers is
swallowed in `sfx()` so nothing sounds twice.

## Room -> track (the Arcademy OST, for now)

Nobody here has listened. Moods come from `arcademy/shell/ost.js` (the owner's own "softer:
the two Midnight Statics, active: the rest") and the file lengths; re-order freely.

| track | title | mood | energy | length |
|-------|-------|------|--------|--------|
| ost_campus | Star Byte Loop | soft | 0.45 | 75 s |
| ost_deep_end | Pixel Rush | loud | 0.80 | 77 s |
| ost_sort | Pixel Rush 2 | loud | 0.85 | 52 s |
| ost_records | Midnight Static | soft | 0.30 | 109 s |
| ost_lost_found | Neon Skyline | soft | 0.50 | 141 s |
| ost_instant_recall | Midnight Static 2 | soft | 0.25 | 121 s |
| ost_anomaly | Midnight Static 3 | soft | 0.30 | 124 s |
| ost_daily_trigger | Neon Pixel Rain | loud | 0.75 | 164 s |
| ost_impulse_control | Neon Pixel Rain 2 | loud | 0.80 | 159 s |
| ost_prizes | Neon Jackpot 3 | loud | 0.70 | 98 s |
| ost_misdirection | Neon Jackpot | loud | 0.75 | 76 s |
| ost_deja_vu | Neon Jackpot 2 | loud | 0.70 | 41 s |
| ost_annex | Corroded Pulse | soft | 0.35 | 139 s |

| room | loud | pool (best first; the run rotates by seed and skips a track another room took) | colour |
|------|------|------|--------|
| The Tea Garden | no | ost_campus (always: the hub tune opens every lap) | - |
| The Toybox | yes | ost_daily_trigger, ost_deep_end, ost_impulse_control | - |
| The Fool's Casino | yes | ost_misdirection, ost_prizes, ost_deja_vu | - |
| The Pink Chapel | yes | ost_impulse_control, ost_sort, ost_daily_trigger | - |
| The Coronation | yes | ost_prizes, ost_deep_end, ost_misdirection | - |
| The Undertow | no | ost_instant_recall, ost_records, ost_anomaly | lowpass 650 Hz |
| The Hall of Mirrors | no | ost_anomaly, ost_lost_found, ost_deja_vu | - |
| The Grey Ward | no | ost_annex, ost_records, ost_instant_recall | highpass 240 Hz |

Plus, in any room: the fraught state (effects stacked, `run.effects.length / 3`) eases a lowpass
from open down to 800 Hz; tea time caps it at 1.4 kHz. Crossfade at every gate is 1.5 s (linear
ramps on per-track gain nodes; the outgoing element pauses 200 ms after the ramp and resumes
from where it was next lap). Each track is one `<audio>` element through a
`MediaElementSource` into the chain `duck -> lowpass -> highpass -> level -> master`. If that
route ever throws (it should not: the files are same-origin on `ccp.game`, the same route
`engine/scene.js` uses for the drone) the player falls back to element volume with a 50 ms
stepped fade and logs `media element route failed`.

## Swapping the soundtrack

1. Drop the mp3s in `Resources/web/arcademy/assets/sfx/` (or change `OST_BASE`).
2. Edit `TRACKS` in `race/audio.js`: one row per file (`name`, `title`, `mood`, `energy`, `sec`,
   `start` = seconds to skip on first play).
3. Edit `ROOM_POOLS`: each room lists the tracks it may draw, best first. That is the whole change.
4. Optional per-track level: add `level: 0.8` to a row and multiply it in `fade()` (one line;
   not wired until a mix needs it).

## When the owner's mix lands

Send: the files, one line per track saying which room(s) it is for and whether it is loud or
soft, an integrated LUFS if you have it (the Arcademy files are -15.2 to -16.0 and `MUSIC_LEVEL`
was set for that), any loop point (start/end seconds) if a file should not loop whole, and a
call on the two colours (Undertow lowpass, Grey Ward highpass) once heard on real speakers.
Debug: every track start, crossfade, voice-cap drop and missing file logs as `[audio] ...`
through `bridge.log` (the host log, or the console as `[race->host] log` standalone).
