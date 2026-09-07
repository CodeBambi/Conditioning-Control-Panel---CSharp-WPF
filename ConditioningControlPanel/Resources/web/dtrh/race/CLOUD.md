# CLOUD.md - play from bambicloud

What the web build of Racing Thoughts does with audio hosted on bambicloud.com,
what it was allowed to find out, and where lane W2 plugs in.

Code: `race/cloud.js` (the player and the panel), `race/menu.js` (the verb),
`raceBoot.js` (the hooks and the outbound tap), `race/smoke/cloud-check.mjs`.

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
front of the player. The paste box is the whole door in this lane.

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

Two probes is the budget for this, so probing stopped there and the paste box
shipped alone. CORS on the API reflects the calling origin with credentials
allowed; that was observed on both probes and is not relied on by anything that
shipped.

**Before any future lane builds a playlist browser** it needs one successful
read of `/playlists` (or `/playlists/recent`, which takes no parameters and may
well be the easier door) and the field names off a real response. Until then the
entries seam is `addTracks([{ id, url, title, locked }])` in `race/cloud.js`:
hand it a listing's rows and the panel, the locked wording and the playlist walk
already work.

## The chart seam (lane W2)

`race/cloud.js` never charts anything itself. It calls one hook per track:

```js
hooks.chart({ id, url, title, durationSec, el }) -> Promise<chart>
```

`durationSec` is the element's real duration and `el` is the live `<audio>`
element, so a decoder can read the file the player is already streaming instead
of fetching it twice. Whatever comes back goes straight to `race.setTrack`, so
it must satisfy `CHART.md` (`normalizeChart` will throw otherwise, and the throw
is shown on the plate).

The default lives in `raceBoot.js` `makeCloud()`: a `demoChart` cut to the real
duration, renamed to the real track. That keeps the road and the clock honest
end to end today. Lane W2 replaces that one function body and touches nothing
else.

`CHART.md`'s authored-chart rule still wins: a hand-made chart for a track is
used instead of anything generated, and is never overwritten by one.

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
  host (cclabs-web `scripts/race-web-ext/host/index.js`) sets it; a desktop host
  does not, and `trackPick: true` (a host that can open a file dialog) turns it
  off outright - `cloudEnabled()` in `race/cloud.js` is the whole rule, exported
  so the menu and the smoke read one predicate.
- `?cloud=1` is the same switch for a page with no host under it. Dev and the
  smoke use it; it can never turn on where a desktop host owns track loading.
- `?panel=cloud` opens the menu on the panel, the way `?panel=howto` does.
- `window.__race` is a standalone-only handle on `{ race, cloud, menu, settings }`
  so a headless check can read the run's state. It is never defined under a host.

## The check

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
