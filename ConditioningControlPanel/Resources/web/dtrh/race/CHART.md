# Racing Thoughts - track charts (the file is the track)

Owner call 2026-09-06: the race gets its identity from a hypno file. The player loads an audio
file, the host charts it (energy curve + spoken trigger words with timestamps), and the run is
driven by that chart: every trigger the voice says is a bubble the kart drives into at that second,
a drop is a jump and a spiral, a countdown is a run of air rings, a chant is a bubble lane in the
chant's rhythm, silence is a fogged straight with nothing in it. The run ends when the file ends.

Hard rules:
- **The clock is the file.** Kart speed, boost and brake change spectacle and score, never when an
  event is met. Events are scheduled by track time and spawned ahead at the kart's current speed
  so the pop lands on the spoken word whatever the player did with the throttle.
- **Pause pauses the voice.** The Brake, a host pause and a video pop all stop the track clock.
- **Audio never leaves the machine.** Charts hold timestamps and labels only. The cache key is a
  hash of the file, never its path or name in anything that is uploaded.
- Without a track the race runs exactly as today (seeded rooms, random spawns). Nothing in this
  document changes the no-track path.
- Everything else in `CONTRACT.md` still holds (600 lines per PR, track space via `layout.toWorld`,
  EMI canon, no em-dashes anywhere).

## Chart JSON (version 1)

```json
{
  "version": 1,
  "source": { "name": "bambi sleep 01.mp3", "hash": "3f2a...", "durationSec": 1834.2, "sampleRate": 16000 },
  "analysis": { "energy": "rms-flux-v1", "words": "vosk-v1", "lexicon": ["drop", "sleep", "good girl"], "generatedAt": "2026-09-06T10:00:00Z", "partial": false },
  "binSec": 0.5,
  "energy": [0.12, 0.13, 0.11],
  "acts": [ { "id": 0, "t0": 0, "t1": 212.5, "kind": "induction", "room": "teagarden", "name": "the settle" } ],
  "events": [ { "id": "e12", "t": 312.4, "kind": "trigger", "label": "good girl", "conf": 0.82, "dur": 0, "weight": 1 } ]
}
```

- `source.hash`: SHA1 hex of the file length (8 bytes little endian) + the first 1 MiB of the file.
- `analysis.words`: `"vosk-v1"` or `"none"`. `analysis.partial` is `true` on the chart posted after
  the energy pass and before the word pass has landed.
- `energy`: one value per `binSec`, `0..1`, normalised so the file's 98th percentile RMS is 1.
  Length is `ceil(durationSec / binSec)`.
- `acts`: contiguous, sorted, `t0` of the first is 0, `t1` of the last is `durationSec`. `kind` in
  `induction | deepening | triggers | mantra | build | silence | wake | free`. `room` is a room id
  from `consts.js ROOM_IDS`; the default mapping is `ACT_ROOM` below and the analyzer may override it
  to avoid repeating a room back to back.
- `events`: sorted by `t`. `id` unique in the chart. `conf` 0..1 (1 for energy events). `dur` seconds,
  0 for point events. `weight` 0..1 scales how loud the cue is (default 1).

Event kinds:

| kind | source | fields | meaning |
|------|--------|--------|---------|
| `trigger` | words | `label` (the phrase) | a mod trigger phrase or a user keyword trigger was spoken |
| `word` | words | `label` | a structure word from `STRUCTURE_WORDS` (drop, sleep, deeper, ...) |
| `count` | words | `label` ("3"), `n` (3), `of` (run length), `last` (bool) | a number inside a countdown run (numbers within 2.5 s of each other, descending or ascending) |
| `drop` | words | `strength` 0..1 | a drop moment: the end of a countdown, or a `word` in `DROP_WORDS` not inside a countdown |
| `chant` | words | `label`, `dur`, `reps`, `period` | the same phrase 3+ times within 25 s: `t` is the first, `period` the mean gap |
| `build` | energy | `dur` | RMS rising for 8 s or more (slope over the window > 0.25 of full scale) |
| `peak` | energy | | a local maximum above 0.8 |
| `release` | energy | | RMS falls by more than 0.4 within 3 s after a peak |
| `silence` | energy | `dur` | RMS under 0.06 for 3 s or more; `t` is the start |

`STRUCTURE_WORDS` (English v1, lowercase, the words pass grammar is this list + the trigger
lexicon + `[unk]`):
`drop, dropping, sleep, sleepy, asleep, deeper, deep, down, sink, sinking, relax, relaxing, breathe,
breath, blank, empty, obey, listen, focus, surrender, melt, float, floating, heavy, wake, awake,
waking, up, open, count, zero, one, two, three, four, five, six, seven, eight, nine, ten, now, good,
girl, bimbo, doll, mind, mindless, pink, spiral, trance, trigger`

`DROP_WORDS`: `drop, dropping, sleep, asleep, deeper, sink, sinking, now` (only `now` when it follows
a count within 1.5 s).

`ACT_ROOM` default: `induction: teagarden, deepening: undertow, triggers: toybox, mantra: chapel,
build: mirrors, silence: greyward, wake: coronation, free: casino`.

Act detection (energy pass, no words): split at every `silence` of 6 s or more and at every
`release` after a `peak`, merge segments under 45 s into their neighbour, then label by position
and energy: first segment `induction`; a segment whose mean energy is under 0.25 and that follows a
release is `deepening`; a segment with a `build` inside is `build`; the last 12 percent of the file
is `wake` if its mean energy is above the file mean; a segment that is mostly silence is `silence`;
everything else `free`. The words pass upgrades: a segment holding 3+ `trigger` events becomes
`triggers`; one holding a `chant` becomes `mantra`.

## Authored charts (owner rule: they always win)

A chart with top level `"hand": true` was written by a person. It is used exactly as written:
never merged with a generated road, never regenerated over, never written into any cache of
generated charts. `normalizeChart` keeps `hand`, keeps a top level `rules` object, and keeps
`hand`, `cue` and `note` on individual events, so an author can mark the events they placed and
leave notes in the file without the validation pass quietly eating them.

The web build ships them in `race/charts/`, with `race/charts/index.json` as the lookup table:

```json
{ "version": 1, "tracks": [
  { "cloudId": "<file id or cdn basename>", "hash": "<sha1>", "title": "the settle",
    "durationSec": 1834.2, "chart": "charts/the-settle.chart.json" } ] }
```

Lookup order for every track, first answer wins:

1. an index row matching `cloudId`, derived from the url (`race/chartSource.js` `cloudIdFrom`)
2. an index row matching the `CHART.md` hash of the file
3. the generated-chart cache (`race/chartCache.js`, IndexedDB, keyed by hash)
4. generate a road from the audio

Steps 1 and 2 both answer before the audio is downloaded, so an authored track costs no
download: 1 needs only the url, and 2 needs a length and the first 1 MiB. `race/charts/README.md`
is the format and the link step in full. The desktop host holds the same rule with its own
`TrackChartCache`: an authored chart is never handed to `Save`.

## Page side

### `race/chart.js` (PR c1, pure, node self-check, no THREE)
```js
export const STRUCTURE_WORDS, DROP_WORDS, EVENT_KINDS, ACT_KINDS, ACT_ROOM, CHART_VERSION;
export function normalizeChart(obj) -> chart        // validates + sorts + fills ids/defaults; throws Error('chart: ...') on a bad shape
export function demoChart({ seed = 1, durationSec = 240 } = {}) -> chart   // deterministic synthetic chart with every event kind, uses makeRng from consts.js
export function createScheduler(chart, { leadSec = 2.2 } = {}) -> sched
  sched.update(trackT, fire)      // fire(event, dueIn) once per event when event.t - leadSec <= trackT; dueIn = event.t - trackT (can be < 0 after a seek or a stall)
  sched.actAt(t) -> act | null
  sched.energyAt(t) -> 0..1       // linear interpolation between bins, 0 outside the file
  sched.replace(chart)            // swap in an upgraded chart: keeps fired ids and taken ids, adopts events with t > lastT only
  sched.taken(id)                 // the player met this event (popped its bubble, hit the drop)
  sched.skip(id)                  // cues.js put nothing on the road for it (a guess): it leaves the count, never a miss
  sched.stats() -> { total, fired, countable, taken }   // countable = events of kind trigger|word|count|drop, minus skipped ids
  sched.lastT                     // the last trackT seen
  sched.reset()
```
Scheduler rules: `update` never fires the same id twice; a trackT that jumps backwards by more than
1 s resets fired ids with `t > trackT` (a seek); events with `t < trackT - 0.5` at first sight fire
with a negative `dueIn` and the run may drop them.

### `race/cues.js` (PR c2 builds the plain mapping, PR c3 makes it sing)
```js
export function cueFor(event, ctx) -> cue | null
// ctx = { energy: 0..1, act, room, intensity, rng, triggerKinds: Map(label -> bubbleKindId), lyrics: bool }
// cue = {
//   spawn: [ { kindId, placement: 'spawn' | 'air' | 'rain', x, h, at, row? } ],  // at = seconds relative to event.t (0 = on the word)
//   jump: vh | 0, mix: bubbleKindId | null, mood: 'calm'|'streamed'|'fraught'|'smug'|'shock'|'jackpot' | null,
//   pose: name | null, toast: { text, kind } | null, word: label | null, fog: 0..1 | null, boost: sec | 0, density: mult | null,
//   holdSec: 0
// }
```
THE ROW (PR L4). A `trigger` the spotter is sure of is not one bubble, it is a LINE of them across
the whole road: every spawn carries `row: true`, they share one kind, one depth and `at: 0`, and
their x's run `-LANE_X_MAX` to `+LANE_X_MAX` with no gap wider than `2 * POP_HIT_X * 0.9`, so the
pop box cannot be threaded between two of them wherever the kart sits. A trigger word is a thing
that happens to you, not a thing you steer around. `ROW_X` and `ROW_MAX_GAP` are exported for the
smoke; `race/smoke/rows-check.mjs` holds the geometry against `consts.js`. `ctx.lyrics` says the
road came out of a transcript: it halves the `peak` rain and nothing else, because on a worded
track the rows are the loud thing and a rain over them takes the reading away.

THE CATALOGUE (2026-09-08). Which phrases lay a trigger row at all is one file,
`chart/editor/triggerSets.js`, shared by the Track Maker and the road, and every set carries a
`preset`. `race/triggerTheme.js` is the only place a preset turns into a bubble kind and a plate
theme, so what the kart drives into and what flies at the face come off one row and cannot drift.

- **Precedence.** Sets overlap on purpose ("bimbo doll" lives inside the chant, "drop" inside "drop
  for cock"). When two land on the same tenth of a second the winner is, in order: the lower group
  rank (`named` 0, `sequence` 1, `words` 2, an author's own set 3), then the LONGER match, then the
  set id. `rankOf` / `compareHits` in `triggerSets.js`; the comparator sorts on the quantized tenth,
  not the raw second, because an order that is not transitive silently un-sorts the list.
- **`row: false`.** A set may be a colour in the Track Maker and lay no road at all. The words said
  a hundred times a track (accept, relax, sleep) are accents on the word bubbles, not rows: a row
  every four seconds is not a trigger, it is a wall.
- **`mode: 'countdown'`.** A count is its own mode, not a regex: the scripts put a whole sentence
  between one number and the next, so the rule is a number of five or under, then a smaller one
  within 40 s, at least three long. One span per count, on the first number.
- **The scan is the roll call, the fingerprint is the clock.** A words file may ship `hits` measured
  off the audio. Those no longer REPLACE the live scan (a set the file was never fingerprinted for
  could then never reach the road, however the catalogue grew); the scan says which phrases are
  said, and a scanned hit within `FP_SNAP_SEC` of a fingerprinted one of the same set adopts its
  second and its confidence. `GENERATOR_ID` is `web-road-v5` since.
- `TRIGGER_GAP` (2.2 s) thins what is left, first wins. `race/smoke/catalogue-check.mjs` holds all
  of the above, plus a per-track share cap so no one kind swallows a track.

The plain mapping (c2): `trigger` -> the row above, lane placement, `word: label`; `word` -> a treat bubble; `count` -> a golden air bubble, `last` adds
`jump: 6`; `drop` -> `jump: 7`, `mix: 'spiral'`, `mood: 'streamed'`, three golden air bubbles at
`at = 0.2, 0.5, 0.8`; `chant` -> `reps` treats in lane placement alternating `x = +-1.2` at
`at = k * period`; `build` -> `boost: min(dur, 4)`, `density: 1.6`; `peak` -> 6 rain treats; `release`
-> `mood: 'calm'`, `density: 0.6`; `silence` -> `fog: 1`, `density: 0`, `holdSec: dur`.

The feel (c3), on top of the mapping. Confidence: a `trigger` under conf 0.55 is a plain treat with no
`word` and no pose; a `word` under conf 0.5, a lone number word (the spotter hears "one" in "someone"),
or a wake word (`wake awake waking up open`) outside a `wake`/`free` act returns null, and run.js
calls `sched.skip(id)` so it never counts against the player. Room: an unmapped trigger wears the
room's effect (`teagarden flash, undertow spiral, toybox pink, chapel spiral, mirrors glitch, greyward
freeze, coronation prism, casino lucky`); a `peak` rains the room's bubble (`casino lucky, coronation
golden, chapel/mirrors prism`, else treats), 4..8 of them by `intensity`. Amount: a `chant` lane is
`reps * max(0.5, weight)` treats, every fourth gold; a `build` boost is `dur * max(0.5, weight)` capped
at 4; a `drop` under strength 0.6 is `jump: 5`, two rings and no spiral. A `count` ring sinks toward
the road as the spoken number falls (`n` is the number, `of` the run length) and toasts the number;
a lifting `word` (`float up open light rise lift`) hangs in the air. Poses and toasts: trigger `grab`,
last count `clamp` + `item` toast, drop `jackpot` toast of its label, chant `cheer`, build `boost`,
peak `cheer` (`smug` over intensity 0.7), release `drift`, silence a `. . .` toast.
`resultTag(taken, countable)` is the end card's second line: `every word`, `good girl` (>= 0.8),
`half of her` (>= 0.5), `she noticed` (>= 0.2), `you were not listening`; null with nothing to take.
A loaded track ducks the room OST to `TRACK_DUCK` (0.12) and the bed to silence via
`audio.duck(on, 'track')`, the standing level `update()` eases back to, until the track is cleared.

### `race/bubbles.js` additions (PR c2)
```js
field.spawnAt({ kindId, placement, d, x, h, eventId, script })   // an explicit placement; the slot id, or -1 when it was refused
field.spawnRow({ kindId, placement, d, h, xs, eventId, script }) // a whole row at one depth (PR L4); how many went down, 0 for none
field.setDensity(mult)                                  // already in CONTRACT.md; with a track this scales only the cue spawns
field.setSparse(on)                                     // the loaded road came out of a transcript (PR L4): seedChunk stands down
```
Pop events carry `eventId` when the bubble came from a cue; `onMiss` events do too.

A row goes down whole or not at all: a row that would not fit the pool is not laid, the density
gate is rolled ONCE for the line rather than per bubble, and `density > 1` never doubles it. It
also SPENDS as one thing: the first of its bubbles to pop or to slip past settles the row, so five
bubbles are one `taken` on the scheduler (`takenIds` is a set) and at worst one broken combo. With
`setSparse(true)` `seedChunk` lays only the chunk's own golden and no lanes or ramp lines,
so what the player drives through is the lyric. Too many bubbles is no bubbles.

**THE SCRIPT IS NOT DRESSING** (2026-09-08). `script: true` marks a spawn the FILE asked for -
`cues.js` puts it on the word bubbles (`case 'word'`) and on the trigger rows (`case 'trigger'`) -
and neither refusal above may touch one: no density roll, and a full pool recycles the bubble
farthest from the kart rather than dropping the line. `density` thins the road's DRESSING, and it
is turned down to 0.6 on every `release` and to 0 through a `silence`; rolling each word bubble
against it separately was taking 22 percent of the shelf's words and leaving 44 percent of the
lines short, which is the owner's "i see maybe 1 word out of a phrase".

**THE COVERAGE CHECK.** `wordedRoad` counts itself as it finishes (`wordBubbles.js coverageOf`),
stamps `analysis.coverage` (kept by `normalizeChart`, so the cache and `window.__race` both have
it) and says one `[race-coverage]` line to the host log. A word is on the road when it wears a
bubble or when it is inside a trigger row's own span; what is left is the guard margin, which is
the pop box. `node race/smoke/coverage-check.mjs` drives the whole shelf through the field's own
refusals and fails under 96 percent on the shelf, under 90 on a track, or on ONE word bubble the
chart asked for and the field did not lay.

**THE PILE, AND WHY MOST OF THEM ARE NOT ONE.** Twenty-one runs on the shelf have three or more
words inside `MERGE_SEC` of each other, and nineteen of them are real: "in a completely", "as a
perfect", "of a trap", said at 0.09 to 0.11 s a word. The other kind is the aligner losing the
words and dropping the whole run on the last instant it was sure of - seven words at 0.01 s a
word with 2.7 s of empty road in front of them. So the test is the RATE, `PILE_TIGHT_SEC`
(0.05 s a word, well under the fastest real run on the shelf), and a run that fails it is spread
back over the silence beside it at the local speech rate, anchored on the second the aligner DID
have, never further apart than `PHRASE_GAP_SEC` so it comes back as one line in one lane rather
than a word per lane. Those bubbles carry `est: true` to the chart event. A run with no silence to
go back into is left exactly where it is and counted (`coverage.pilesLeft`) - a guess with no room
is worse than the collision. `node race/smoke/word-sync-check.mjs` holds all of it.

### `race/run.js` + `raceBoot.js` (PR c2)
```js
race.setTrack(chart | null)        // before start(); null returns to the seeded run
race.trackClock(t, playing)        // the host's clock; the run integrates between ticks with performance.now()
race.track                          // { chart, sched, t, playing, name, durationSec } or null
```
- With a track: `S.intensity` follows `sched.energyAt(t)` smoothed over 2 s (floor 0.05); the random
  `spawnAhead` / `rain` timers are off; `seedChunk` still dresses chunks with plain treats at
  `density` so the road never looks empty (unless the road has words on it: see `setSparse`); every
  cue spawn goes through `field.spawnAt`, or `field.spawnRow` for the row, at
  `d = kart.d + kart.speed * max(event.t + at - t, 0.25)` (`sync.depthFor`).
- The sync (`race/sync.js`): the scheduler hands an event over `LEAD_SEC` early so the spawns can
  go down the road; only the spawns are spent at that handover. The visible half of the cue (plate,
  toast, mood, pose, jump, mix, fog, boost, density, hold) is held by `createCueSync` and fired from
  `trackFrame` the frame the track clock reaches `event.t` (`sync.update`), so it lands on the word
  and not 2.5 s before it. A held cue found more than `LATE_SEC` (1 s) past its second is dropped
  (a seek forward); a seek back drops what the scheduler is about to hand over again; a pause drains
  nothing because the second does not move. The row is re-placed every frame off the kart's current
  speed (`field.moveRow`) until `CUE_AHEAD_SEC` is left, so a boost or a ramp inside the lookahead
  still lands it under the kart on the word. A fog/density hold is a stretch of track seconds
  (`S.trackHold` is the second it ends), never a count of frames. `race.syncTrace()` is the per-event
  trace (handedAt, firedAt, rowPlacedAt, rowAt, dropped) the smokes read: `race/smoke/sync-check.mjs`
  (demo, headless) and `real-audio-check.mjs` section 3c (a real voice, `RACE_TRACK_FILE`).
- `TR.lyrics` is true while the loaded chart has a caption track or an `analysis.words` other than
  `none`. It drives `field.setSparse` and `ctx.lyrics`, and is re-read on `replaceTrack` so a words
  pass landing on a partial chart thins the road the moment it arrives.
- Acts: at a gate crossing use the current act's room instead of the feature's room; if the act
  changed and no gate is within 6 s of road, call `dresser.applyRoom` with a 2.5 s fade right away
  and show the MARQUEE with the act's `name`.
- The run ends when `t >= durationSec - 0.25` or on `track-ended`: the end summary gains
  `taken`, `countable`, `trackName`; `run-ended` gains `track: { name, hash, durationSec, taken, countable }`.
- Pause: the Brake, `pause {on:true}` from the host and a video pop send `track-pause {on:true}`;
  resume sends `{on:false}`. The clock only moves on `track-clock` ticks that say `playing`.
- Standalone (not hosted): `?chart=demo&dur=240` uses `demoChart`; `?chart=<url>` fetches a chart;
  `?audio=<url>` plays an `<audio>` element as the clock (its `currentTime` drives `trackClock`);
  without `?audio` the clock is wall time. The headless checks use `?autostart=1&chart=demo`.
- A `track-chart` message with `partial: true` calls `setTrack` (or `sched.replace` when the run is
  live); the later full chart calls `sched.replace`.

### The menu (PR c7, Fable)
"load a track" on the menu sends `track-pick`; a progress plate shows `track-progress`; when the
chart lands the plate shows the name, the duration and "N triggers found", and `race` starts the
run with the track. The results screen reads "you took N of M" for a tracked run.

## Host protocol additions (PR c6)

Page -> host: `track-pick` (open the file dialog), `track-play` (the run started; start the audio),
`track-pause {on}`, `track-stop` (end of run or exit: stop the audio), `track-cancel` (abandon an
analysis in flight).

Host -> page: `track-progress { stage: 'decode' | 'energy' | 'words', pct: 0..1, name }`,
`track-chart { chart, partial, authored }`, `track-clock { t, playing, durationSec }` every 250 ms
while a track is loaded, `track-ended`, `track-error { message }` (dialog cancelled is not an error:
the host posts `track-progress { stage: 'cancelled' }`).

`authored: true` on `track-chart` means a person wrote this chart. `raceBoot.js` puts it on the
ready plate and `race/menu.js` shows it as a small `hand-tuned` mark. An authored chart is never
partial and nothing fuller lands behind it.

## C# side

- `Models/Race/TrackChart.cs` (c4): POCOs mirroring the JSON with Newtonsoft attributes:
  `TrackChart`, `TrackSource`, `TrackAnalysis`, `TrackAct`, `TrackEvent`.
- `Services/Race/TrackPcm.cs` + `Services/Race/TrackAnalyzer.cs` (c4):
  `TrackDecoder.Decode(path, IProgress<double>, ct) -> TrackPcm { float[] Mono16k, double DurationSec, string Hash, string Name }`
  via `MediaFoundationReader` (falls back to `AudioFileReader`), `StereoToMonoSampleProvider`,
  `WdlResamplingSampleProvider(16000)`.
  `TrackAnalyzer.Energy(TrackPcm, IProgress<double>, ct) -> TrackChart` (energy, energy events, acts).
  `TrackChartCache.TryLoad(hash) / Save(chart)` under `Path.Combine(App.UserDataPath, "race", "charts")`.
- `Services/Race/TrackWordSpotter.cs` + `Services/Race/TrackLexicon.cs` (c5):
  `TrackLexicon.Build() -> IReadOnlyList<string>` = STRUCTURE_WORDS + the active mod's trigger
  phrases + `AppSettings.CustomTriggers` + `KeywordTriggers` phrases, lowercased, letters and spaces
  only, distinct. `TrackWordSpotter.Spot(TrackPcm, lexicon, IProgress<double>, ct) -> List<TrackEvent>`
  on a Vosk grammar recognizer (`SetWords(true)`, 8000-sample chunks, `result[]` word timings),
  then `TrackChartWords.Apply(chart, events, lexicon)` adds trigger/word/count/drop/chant events,
  upgrades acts, sets `analysis.words = "vosk-v1"`. No model on disk = `analysis.words = "none"`,
  never an exception to the caller.
- `Services/Race/TrackPlayer.cs` + `CaucusHostService` track messages (c6): `AudioFileReader` +
  `WaveOutEvent`, master volume, `PositionSec`, `Play/Pause/Resume/Stop`, `Ended` event; the file
  dialog on the UI thread; the analysis on a worker with progress posts; the 250 ms clock timer;
  `track-stop` on `run-ended` and `exit`.

### Authored charts on the desktop

The shared rule, the index format and the `cloudId` derivation live in the **Authored charts**
section above, written on `feat/race-cloud-w2-chart`; this is only what the C# host adds on top of
it. If the two ever disagree, the shared section is right.

- **Passthrough.** `TrackChart`, `TrackSource`, `TrackAnalysis`, `TrackAct` and `TrackEvent` each
  carry a Newtonsoft `[JsonExtensionData]` bag, so a field this build has no property for survives
  load -> post -> save untouched: the top level `rules`, the per-event `hand`, `cue` and `note`, and
  whatever a newer chart version adds. `hand` and `source.cloudId` are typed as well, because the
  lookup and the cache guard read them. A generated chart writes neither.
- **A folder of your own.** `%LOCALAPPDATA%/ConditioningControlPanel/race/authored/*.json`. Any
  chart file in there with `"hand": true` is indexed by `source.hash` and by `source.cloudId`, both
  optional but not both missing, and matched case insensitively. The folder is re-scanned when it
  changes and the chart is re-read on every hit, so dropping one in or editing one takes effect on
  the next track without a restart.
- **The order the host uses**, first answer wins, before anything is decoded:
  1. your authored folder, by hash or `cloudId`
  2. the shipped `race/charts/index.json`, by `cloudId` then hash (read off disk from
     `Resources/web/dtrh/race/` next to the exe, the same tree the page is served out of)
  3. `TrackChartCache`, by hash (the generated charts, under `race/charts/` in the user data folder)
  4. chart the audio
  The host logs one line per track: `RaceHost: chart for {Name} via {Door}`, where the door is
  `authored (user folder)`, `authored by cloudId`, `authored by hash`, `cached` or `generated` - the
  same four names the page logs, plus the one only the desktop has. An authored chart is posted with
  `partial: false` and the analysis is never run for it.
- **The cache cannot shadow an author.** `TrackChartCache.Save` refuses, with a log line and no
  exception, when the chart handed to it is authored, or when an authored chart already answers for
  that hash. The "re-chart a `none` chart once a Vosk model appears" rule skips authored charts too:
  there is no better pass than the person who wrote it.
- **Hash before download.** On the cloud path the host resolves doors 1 and 2 off the `cloudId` with
  no request at all, then asks the CDN for a `Content-Length` (HEAD, or the total out of a one byte
  range's `Content-Range`) and a `Range: bytes=0-1048575`, and hashes those with
  `TrackDecoder.HashBytes(length, head)` - the same recipe `HashFile` uses over a whole file. Doors
  1, 2 and 3 are all resolved off that hash before a byte of audio is fetched, so an authored or
  already charted track costs no download. If ranges are refused, or anything else goes sideways,
  the probe answers nothing and the full download happens as before. One retry, never more.

**Known gap: no prefetch of the next track.** The desktop never reads the playlist over there, so it
cannot know what is coming and cannot chart it early. Charting starts when the track does. In
practice that is seconds of the seeded road before the partial chart swaps in, and none at all for a
cache hit or an authored chart, which answer instantly. Closing it would mean reading their playlist
UI, which this lane does not do.
