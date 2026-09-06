# The Caucus Race - track charts (the file is the track)

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
  sched.stats() -> { total, fired, countable, taken }   // countable = events of kind trigger|word|count|drop
  sched.lastT                     // the last trackT seen
  sched.reset()
```
Scheduler rules: `update` never fires the same id twice; a trackT that jumps backwards by more than
1 s resets fired ids with `t > trackT` (a seek); events with `t < trackT - 0.5` at first sight fire
with a negative `dueIn` and the run may drop them.

### `race/cues.js` (PR c2 builds the plain mapping, PR c3 makes it sing)
```js
export function cueFor(event, ctx) -> cue | null
// ctx = { energy: 0..1, act, room, intensity, rng, triggerKinds: Map(label -> bubbleKindId) }
// cue = {
//   spawn: [ { kindId, placement: 'spawn' | 'air' | 'rain', x, h, at } ],   // at = seconds relative to event.t (0 = on the word)
//   jump: vh | 0, mix: bubbleKindId | null, mood: 'calm'|'streamed'|'fraught'|'smug'|'shock'|'jackpot' | null,
//   pose: name | null, toast: { text, kind } | null, word: label | null, fog: 0..1 | null, boost: sec | 0, density: mult | null,
//   holdSec: 0
// }
```
The plain mapping (c2): `trigger` -> one effect bubble of `triggerKinds.get(label)` or `flash`, lane
placement, `word: label`; `word` -> a treat bubble; `count` -> a golden air bubble, `last` adds
`jump: 6`; `drop` -> `jump: 7`, `mix: 'spiral'`, `mood: 'streamed'`, three golden air bubbles at
`at = 0.2, 0.5, 0.8`; `chant` -> `reps` treats in lane placement alternating `x = +-1.2` at
`at = k * period`; `build` -> `boost: min(dur, 4)`, `density: 1.6`; `peak` -> 6 rain treats; `release`
-> `mood: 'calm'`, `density: 0.6`; `silence` -> `fog: 1`, `density: 0`, `holdSec: dur`.

### `race/bubbles.js` additions (PR c2)
```js
field.spawnAt({ kindId, placement, d, x, h, eventId })   // an explicit placement; returns the slot id or -1 when the pool is full
field.setDensity(mult)                                  // already in CONTRACT.md; with a track this scales only the cue spawns
```
Pop events carry `eventId` when the bubble came from a cue; `onMiss` events do too.

### `race/run.js` + `raceBoot.js` (PR c2)
```js
race.setTrack(chart | null)        // before start(); null returns to the seeded run
race.trackClock(t, playing)        // the host's clock; the run integrates between ticks with performance.now()
race.track                          // { chart, sched, t, playing, name, durationSec } or null
```
- With a track: `S.intensity` follows `sched.energyAt(t)` smoothed over 2 s (floor 0.05); the random
  `spawnAhead` / `rain` timers are off; `seedChunk` still dresses chunks with plain treats at
  `density` so the road never looks empty; every cue spawn goes through `field.spawnAt` at
  `d = kart.d + kart.speed * max(dueIn + at, 0.25)`.
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
`track-chart { chart, partial }`, `track-clock { t, playing, durationSec }` every 250 ms while a
track is loaded, `track-ended`, `track-error { message }` (dialog cancelled is not an error: the
host posts `track-progress { stage: 'cancelled' }`).

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
