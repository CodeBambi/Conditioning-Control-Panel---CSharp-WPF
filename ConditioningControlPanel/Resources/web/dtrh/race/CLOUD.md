# CLOUD.md - play from bambicloud

What the web build of Racing Thoughts does with audio hosted on bambicloud.com,
what it was allowed to find out, and where lane W2 plugs in.

Code: `race/levels.js` + `race/levels.json` (the levels panel and its list),
`race/cloud.js` (the player and the paste box), `race/menu.js` (the verb),
`raceBoot.js` (the hooks and the outbound tap), `race/smoke/levels-check.mjs`
and `race/smoke/cloud-check.mjs`.

## The shape that shipped

A menu verb, `play from bambicloud`, opens a panel with a paste box. Every link
pasted becomes an entry; a playable entry becomes an `<audio>` element in this
page and that element's `currentTime` is the track clock, exactly the way the
standalone `?audio=` road in `raceBoot.js` already works. Several links are a
playlist: when a file ends, the run's lap ends and the next playable entry is
loaded and charted, so the next track is the next lap.

**There is no playlist browser.** See "what discovery found" below: the response
shape of the site's public playlist collection was never confirmed, and a
browser built against an unconfirmed shape is a screen of guesses that breaks in
front of the player. What shipped instead is a STATIC list, written down once and
carried in the repo: see "the levels" below. The paste box is still there, folded
under it, for a track that is not on the list.

## The levels

The verb is `levels` and it sits directly under `race`, because on a phone this is
the whole happy path: open the race, see the tracks, tap one, drive. No pasting, no
login, no search box.

`race/levels.json` is where the list lives. It ships with the game, it is read off
our own origin, and it is NEVER refreshed off the site: a set that is not written
down is not on the screen.

```json
{ "version": 1, "sets": [ { "id": "...", "title": "...", "source": "bambicloud",
  "playlistId": "...", "playlistUrl": "https://bambicloud.com/playlist/<id>",
  "levels": [ { "n": 1, "id": "<file uuid>", "title": "...",
                "url": "https://cdn.bambicloud.com/<uuid>.mp3",
                "durationSec": 162, "bytes": 3581627, "trackNum": 0 } ] } ] }
```

| field | what it is |
| --- | --- |
| `n` | the number on the row, 1 up. The order the set is played in. |
| `id` | the file's own id. It is the entry id the player keys on, and it is what a desktop host is asked to open (`https://bambicloud.com/file/<id>`, a PAGE, never a file). |
| `title` | what the row and the plate say. |
| `url` | the audio file on the cdn. The only url this page ever hands to an `<audio>` element or a `fetch`. |
| `durationSec` | the length, for the `m:ss` on the row. The element is still the clock. |
| `bytes` | the file's `Content-Length`. **Carried on purpose:** see below. |

**Adding a set** is editing that file and nothing else. Append an object to `sets`
with its own `levels`, keep `n` contiguous from 1, and give every level a real
`bytes`. The panel shows the FIRST set; a second one is a list the panel can be
taught to switch between later, and it costs no code to write it down now.

**Why `bytes` is written down.** The `CHART.md` hash is the byte length plus the
first 1 MiB, and the two lookups that can answer without downloading the file (the
authored index by hash, and the generated-chart cache) both need that hash. A
length already in hand is a `HEAD` request never sent, and on this cdn that matters
(the facts are below). `hashUrl(url, { byteLength })` skips the HEAD outright when
it is given one.

**What a row says.** Its number, its title, its length as `m:ss`, and one small
mark: `hand-tuned` when `race/charts/index.json` has a row for that track,
`road` when it does not, `again` on the last level played (kept in `localStorage`
under `race.level`, an id and nothing else), and the live word while it is being
worked on (`naming`, `reading`, `decoding`, `charting`, then `playing` / `paused`).
Deciding `hand-tuned` costs NO NETWORK: the index is same origin and already
fetched, and the key is the `cloudId` off the url.

**Two hosts, one panel.** On the web a tap hands that one track to `race/cloud.js`
and the run follows the element. On a desktop host (`trackPick: true`) the desktop
owns playback, so a tap posts `cloud-open { url }` with the track's PAGE url and
toasts "press play over there"; this page loads no audio at all in that mode, and
`play the set` is not offered because the playlist is not this page's to hold.

> As of this lane the C# host's `cloud-open` handler takes no `url`: it opens its
> window and ignores the field. The message carries it anyway so the host can learn
> to read it without the page changing.

**The cdn facts this is built on** (checked 2026-09-07 with `Origin:
https://app.cclabs.app`):

- `HEAD` answers 200 with `Accept-Ranges: bytes` and a `Content-Length`, but with
  **no** `Access-Control-Allow-Origin`, so a cross origin HEAD from a browser
  fails. That failure is one log line and an unknown length, never a thrown error.
- `GET` with `Range: bytes=0-1048575` answers 206 with
  `Access-Control-Allow-Origin: *`, so it works from the browser (a simple `Range`
  value is a CORS-safelisted request header and needs no preflight). But there is
  no `Access-Control-Expose-Headers`, so `Content-Range` and the total length are
  **not** readable from JS, and the 206's `Content-Length` reads as the part size.
- `OPTIONS` answers 403, so anything that would trigger a preflight fails.

Which is exactly why the length is written down: for a level the hash is
`SHA1(8-byte LE bytes + first 1 MiB)` off ONE ranged GET and no HEAD at all. A
pasted link with no known length falls back to lane W2's other road: the length off
`Content-Range` if the server exposes it, else off the full body.

## The rules this holds

- **Direct or not at all.** The player's browser talks to the site. Nothing is
  proxied through CC Labs, nothing is uploaded, and no chart or audio byte is
  sent anywhere. The element is `crossOrigin = 'anonymous'`, which is a
  no-cookie request: no credential can reach them from here.
- **Two kinds of url, and no others.** A url is loaded only if its host is
  `cdn.bambicloud.com` (the audio CDN, which answers
  `Access-Control-Allow-Origin: *`) or it is same-origin. Everything else,
  including a link to a page on the site itself, becomes a **locked** entry:
  shown in the list, worded `locked on bambicloud`, skipped by the playlist walk
  and **never requested**. Nothing here ever works around a locked file.
- **Fail loud, once.** A load that does not answer inside `LOAD_TIMEOUT_MS` is
  retried exactly once, `RETRY_MS` later. The second failure marks that entry,
  says `bambicloud is not answering, load your own file instead` and puts the
  panel back on the paste box. Nothing polls and nothing loops.
- **No login, ever.** No session, token or account endpoint is called, named or
  linked from any of this code.

`cloud-check.mjs` records every request the page makes and fails if a single one
left localhost, so the first two rules are checked rather than asserted.

## What discovery found

Read out of the site's own public client bundle (one GET for the page, one for
the bundle it names), plus two GET probes. No credential was ever sent, no
login, session or account endpoint was called, and nothing private or locked was
listed or touched.

API base: `https://api.bambicloud.com/`. Audio: `https://cdn.bambicloud.com/`.

Read endpoints the bundle names (all GET):

| path | query the bundle sends | what it reads back |
| --- | --- | --- |
| `/playlists` | `ids` / `uuid`, and the filter set `user, creator, core, plan, official, public, favorited, liked, sort, sortDir, searchQuery, includeFiles, simple, expLevels, minDate, maxDate, pageSize, pageOffset` | `data.playlists[]` |
| `/playlists/recent` | none | the body |
| `/tags` | none | the body |
| `/series` | `id`, `includePlaylists` | the body |

Validation the API stated itself, in a 422: `sort` is one of `name, likes,
favorites, explevel, datecreated`; `sortDir` is one of `ascending,
descending`.

Track fields the bundle reads off a file object: `audioURL` (the CDN mp3 the
player element is pointed at), `patreonTiers`, `creator.username`. Its own
locked test is `patreonTiers.length > 0 && audioURL === null`, which is why
**no audio url means locked** is the honest signal and the one this code uses.

**Both probes failed, so the shape is unconfirmed.**

1. `GET /playlists?public=true&official=true&includeFiles=true&pageSize=3&pageOffset=0&sort=plays&sortDir=desc`
   -> `422`, with the validation detail quoted above.
2. `GET /playlists?public=true&official=true&includeFiles=true&pageSize=2&pageOffset=0&sort=likes&sortDir=descending`
   -> `500 Internal Server Error`, empty detail.

3. `GET /playlists/recent` (lane W2, one probe, no parameters, no credential)
   -> `400`, body `{"error": "Missing auth token."}`

That third answer settles it: the public playlist collection is not public. It
wants a session, and this code never logs in and never will, so **there is still no
playlist browser, and there is not going to be one built this way.** The paste box
is the door. CORS on the API reflects the calling origin with credentials allowed;
that was observed on all three probes and is not relied on by anything that
shipped.

**A playlist browser would need the player's own session**, which is theirs and not
ours to hold, so the honest shape for one is the player copying their own links out
of the site. The entries seam stays open for whatever lands:
`addTracks([{ id, url, title, locked }])` in `race/cloud.js`. Hand it a listing's
rows and the panel, the locked wording and the playlist walk already work, and
`race/cloudChart.js` charts whatever comes out of it.

## The charts (lane W2)

`race/cloud.js` still never charts anything itself: it calls one hook per track,
`hooks.chart({ id, url, title, durationSec, el }) -> Promise<chart>`, and
`raceBoot.js` points that at `race/cloudChart.js`. Whatever comes back goes
straight to `race.setTrack`, so it has to satisfy `CHART.md`.

### The four doors

Every track is looked up in this order and stops at the first answer. The log says
which one it came through.

| door | what it costs | what it is |
| --- | --- | --- |
| `authored by cloudId` | nothing | a row in `race/charts/index.json` matching the id in the url |
| `authored by hash` | a HEAD and 1 MiB | a row matching the file's `CHART.md` hash |
| `cached` | a HEAD and 1 MiB | a road this browser generated before, out of IndexedDB |
| `generated` | the whole file | decode it, walk the peaks, lay a road |

**Authored charts always win** and are used exactly as written: never merged with a
generated road, never regenerated, never written into the cache.
`race/charts/README.md` is the format, the `cloudId` derivation and the link step;
`race/charts/index.json` ships empty.

### Naming a file before downloading it

The hash is `CHART.md`'s, unchanged: SHA1 of the byte length as 8 bytes little
endian plus the first 1 MiB. `race/chartSource.js` `hashUrl()` gets it from a HEAD
(for `content-length`, a CORS safelisted response header) and a `Range` GET for the
first megabyte, so the two lookups that can answer without the audio get their
chance before any audio moves.

None of that is required. `Range` is not a CORS simple header, so the ranged GET
needs a preflight the CDN may not answer, and `Content-Range` needs to be exposed
before it can be read. If any of it is shut, `hashUrl` answers null quietly and the
hash is taken from the body that had to be downloaded anyway: the cache still hits
on the second run, only the saved download is lost.

### Generating a road with no words

The desktop host runs a word spotter. A browser has none, so the energy curve is
the entire road. `race/cloudChart.js` fetches the file (the player's browser
straight to the CDN, nothing of ours in the middle), decodes it once in a throwaway
16 kHz `OfflineAudioContext`, walks min/max peaks at 50 a second in chunks that hand
the frame back between them, drops the buffer, and runs the pure road generator
`chart/maker/generate.js` with no words.

Three knobs are different from the generator's defaults, and only three:

1. **`binSec` 0.25, not 0.5.** `buildEvents` calls a peak a local maximum over plus
   or minus 8 bins. At 0.5 s bins that is four seconds either side, which swallows
   the swell of a spoken sentence whole; at 0.25 it is two seconds, which is the
   length of the swell, so a build lands on the rise it belongs to.
2. **Silence is read off the curve, not off the gaps between words.**
   `silenceEvents` finds quiet in the gaps between words, so with no words it sees
   exactly one gap, the whole file, and lays a 20 second fog over the start line.
   Those are dropped, and quiet is found where the energy is under `QUIET_LEVEL`
   for `QUIET_MIN_SEC` or more, which is what `CHART.md` says a silence event is
   anyway. A quiet stretch that reaches the end of the file is not laid: a fade out
   is not fog on the finish line.
3. **Acts are read off the curve, not off act words.** `actsFrom` scores act kinds
   from spoken words; with none, every window scores zero and carries the last kind
   forward, so an hour of audio comes out as ONE act, one room and one mood for the
   whole run. `actsFromEnergy` labels each 30 second window by level instead (loud
   is `triggers`, quiet is `deepening`, silent is `silence`, the rest is `free`, the
   first is the settle and a loud tail is the way up), then applies `CHART.md`'s own
   tidy-up: nothing under 45 seconds, no more than 16 of them.

Everything else, the build/peak/release pass and the energy normalisation and the
act merge rules, is `chart/maker/generate.js` unchanged and imported, never copied.
`analysis.words` is `none`, so the end card never claims the player missed words
nobody ever heard.

`GENERATOR_ID` in `race/cloudChart.js` is the second half of the cache key. Tune any
of the three knobs and change that string, or players get yesterday's road.

### While it is being read

`chartFor` answers inside `PARTIAL_MS` whatever happens. If the road is not in hand
by then it hands back a plain road marked `analysis.partial` and the run starts on
that; the real one arrives through `onUpgrade`, which calls `race.replaceTrack` on a
live lap and `race.setTrack` otherwise, which is `CHART.md`'s partial rule. A file
that will not fetch, will not decode, or is longer than `MAX_DECODE_SEC` gets the
demo road cut to its real duration and renamed to the real track, plus a toast.
There is never a dead run.

### Reading the next one ahead

When a track starts, `race/cloud.js` names the next playable entry through
`hooks.prefetch`, and `cloudChart` resolves it in the background so the next lap
starts with its road in hand. One at a time; a new one replaces the old; closing the
panel or forgetting the list lets it go. `chartFor` on the next lap takes the
prefetched promise rather than starting again.

### The cache

`race/chartCache.js`: IndexedDB `race-charts`, keyed by hash, capped at 50 entries,
least recently read first. It holds GENERATED charts only (`put` refuses a chart
with `hand: true`) and every record carries `GENERATOR_ID`, so a tuned road drops
its own old entries on sight instead of playing them back. A browser that refuses
IndexedDB, a private window or blocked site data, charts every track instead:
slower, never broken.

One file can sit at two urls under two names, and the cache is keyed on the file. So
a chart out of the cache is re-stamped with the name of the track that asked for it:
the plate, the marquee and the results card say what the player pasted.

### What never happens

The mp3 is fetched by the player's browser straight from the CDN, which answers
`Access-Control-Allow-Origin: *`. Nothing of ours is in the middle of it. The bytes
are decoded, walked for peaks and dropped; what is kept is a chart, which is
timestamps and labels. No byte of audio is uploaded anywhere, and the cache lives in
the browser that wrote it.

An hour of 16 kHz mono is about 230 MB of decoded buffer, which is a lot to be
holding on a phone. It is held for as long as the peak walk takes and then dropped,
and a file longer than `MAX_DECODE_SEC` (90 minutes) is not decoded at all: it gets
the stand-in road and a line saying so, because a phone that runs out of memory mid
lap is worse than a road that is not the file's own.

## Frames, and why the Brake works

`run.js` already speaks the `CHART.md` host protocol and posts `track-play` when
a run starts, `track-pause {on}` on the Brake and on a host pause, and
`track-stop` when a run ends. On the web nothing answers those. So `raceBoot.js`
taps its own outbound `send` and hands every `track-*` frame to
`cloud.hostFrame()`: the mini-player is the track host, living in the page.

That is the whole pause wiring. The panel's own pause row goes the same way
round - it calls `race.setPaused`, the run posts `track-pause`, the tap brings it
back - so there is **one** pause path and the file and the road cannot disagree.

`track-play` seeks to 0, which is what makes "again" a replay and a rolled-over
track a new lap.

## Switches

- `cloud: true` in the host's `init.settings` turns the verb on. The browser
  host (cclabs-web `scripts/race-web-ext/host/index.js`) sets it, and a desktop
  host that carries it gets the levels panel in its desktop shape.
  `levelsEnabled()` in `race/levels.js` is the whole rule for the verb, exported
  so the menu and the smoke read one predicate; `cloudEnabled()` in
  `race/cloud.js` is the narrower one for the mini-player, and `trackPick: true`
  still turns THAT off outright, so a desktop host never streams audio here.
- `?cloud=1` is the same switch for a page with no host under it. Dev and the
  smokes use it.
- `?levels=<url>` swaps the level list for another one, **same origin only**, so a
  query string can never point the panel at somebody else's file. It exists for
  `race/smoke/levels-check.mjs`, which serves its own two-level list.
- `?trackpick=1` makes an unhosted page claim the desktop host's track door, so
  the check can walk the `cloud-open` branch. A check aid, nothing else.
- `?panel=cloud` opens the menu on the panel, the way `?panel=howto` does.
- `window.__race` is a standalone-only handle on
  `{ race, cloud, levels, menu, settings }` so a headless check can read the run's
  state. It is never defined under a host.

## The check

```
node race/smoke/levels-check.mjs
```

The levels half. Serves `Resources/web` itself plus its OWN two-level list, its own
authored index and its two WAV tracks (all in memory, so no binary and no test row
is committed), and drives headless Chrome at 390x844, a phone, because that is who
this panel is for. It holds: the verb is `levels` and sits under `race`; the panel
lists the set title and one row per level with number, name and `m:ss`, every row
at least 48 px tall and full width; `hand-tuned` and `road` land on the right rows
and cost no request; a tap is a ONE TRACK run whose clock is the file's clock;
`again` lands on the last level played and survives a reload; `play the set` is the
whole list in order, the first one coming through the authored door, and the end of
a file rolls the lap on; `or paste a link` opens lane W1's box unchanged; on a
desktop host the row posts `cloud-open` with the page url and no byte of audio is
asked for; and nothing left localhost.

```
node race/smoke/cloud-check.mjs
```

Serves `Resources/web` itself, writes its two WAV tracks in memory (no binary is
committed for it), drives headless Chrome over CDP with node's own WebSocket and
walks the panel: the gate both ways, the panel opening, a dead link failing loud
and exactly once, a locked entry that is never requested, the plate, the clock
following the element second for second, the Brake both ways, the rollover to
the next track, and that nothing left localhost. `CHROME_PATH` overrides the
Chrome it looks for.

```
node race/smoke/cloud-chart-check.mjs
```

The W2 half. Sections 1 to 3 are pure and run in node before Chrome is started: the
`cloudId` derivation, the hash, the wordless road (energy bins, build, peak,
release, quiet where the file is quiet, nothing fogging the start line, more than
one room) and what `normalizeChart` is allowed to throw away. Then the browser half,
over a synthesized 90 second WAV that swells twice and has two long quiet stretches:
a real decode gives a real road, an authored chart wins and costs no download, the
next track is read ahead while this one plays, the second load of the same file is a
cache hit with no decode, and nothing left localhost. The authored index it tests
against is served by the smoke, not shipped: `race/charts/index.json` in the repo
stays empty.
