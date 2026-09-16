# race/charts - authored charts

A chart in this folder was written by a person. **Authored charts always win**: if
a track has one, the race uses it and never generates a road for that track, never
merges a generated road into it, and never writes it into the generated-chart
cache. Nothing an automatic pass does can overwrite what is in here.

Everything in this folder ships with the web build and is served from the same
origin as the game, so reading one costs no third-party request.

## The two files

`index.json` is the lookup table. It ships empty and it is the only file the game
reads at boot.

```json
{
  "version": 1,
  "tracks": [
    {
      "cloudId": "0f4c2a18-9d3b-4c77-b0e1-6a2d5f8c1234",
      "hash": "3f2a9c1d84b6e0f5a7c2d9e3b1f408a6c5d7e920",
      "title": "the settle",
      "durationSec": 1834.2,
      "chart": "charts/the-settle.chart.json"
    }
  ]
}
```

| field | what it is |
| --- | --- |
| `cloudId` | the stable name of the file on the CDN, derived from its url (see below). Optional if `hash` is set. |
| `hash` | the `CHART.md` hash of the audio file: SHA1 hex of the byte length as 8 bytes little endian plus the first 1 MiB. Optional if `cloudId` is set. |
| `title` | what the plate and the results card say. The chart's own `source.name` wins if it has one. |
| `durationSec` | the file's length in seconds. For the reader's benefit; the chart carries the number that counts. |
| `chart` | the chart file, relative to `race/` (so `charts/<name>.chart.json`). |

The chart file itself is `CHART.md` version 1 JSON with one extra top level flag:

```json
{ "version": 1, "hand": true, "source": { ... }, "acts": [ ... ], "events": [ ... ] }
```

`hand: true` is what makes it authored. `race/chart.js` `normalizeChart` keeps
that flag, keeps a top level `rules` object, and keeps `hand`, `cue` and `note` on
individual events, so an author can leave notes to the next author in the file and
mark the events they placed by hand inside an otherwise generated road.

## Deriving `cloudId` from a url

One rule, implemented once in `race/chartSource.js` `cloudIdFrom()`:

1. Split the url's path on `/` and drop the empty parts.
2. If any part is a uuid (`8-4-4-4-12` hex) or is 20 characters or more of
   `[A-Za-z0-9_-]`, the **last** such part is the id.
3. Otherwise the id is the last part with its file extension removed.
4. Lower case, always.

So `https://cdn.bambicloud.com/files/0f4c2a18-9d3b-4c77-b0e1-6a2d5f8c1234/track.mp3`
gives `0f4c2a18-9d3b-4c77-b0e1-6a2d5f8c1234`, and a plain
`https://cdn.bambicloud.com/audio/the-settle.mp3` gives `the-settle`.

An id survives a rename, which is why it is preferred over the file name. Set both
`cloudId` and `hash` when you can: `cloudId` is checked first and costs nothing,
and `hash` still finds the track if it ever moves to another url.

## The link step

Steps 2 to 4 are what `tools/racechart/link.py` is for. It computes both keys,
copies the chart in and writes the row, so nobody types a SHA1 by hand:

```
python tools/racechart/link.py my.chart.json --url https://<cdn>/<path>.mp3 \
    [--local my.mp3] [--title "the settle"] [--name the-settle]
```

It sets `"hand": true` if the chart is missing it, takes `cloudId` off the url by
the rule above, and takes `hash` from the url with one HEAD and one ranged GET of
the first megabyte. Add `--local` and it hashes your copy too and holds the two
numbers up next to each other: when they disagree the local copy is a different
encode, and the row gets the **cloud** hash, because the cloud copy is the one
players hear. It prints which number it used either way.

1. Write the chart. `chart/editor/` builds one; so does hand editing a generated
   chart out of the browser's cache. Set `"hand": true` at the top level.
2. Save it as `race/charts/<name>.chart.json`.
3. Get the two keys. `cloudId` comes off the url by the rule above.
   `hash` is what `race/chartSource.js` `hashUrl()` logs for that track, and
   `tools/racechart/align.py` computes the same number from a local copy.
4. Add the row to `index.json`.
5. Check the index: `python tools/racechart/link.py --check`. It reads every row
   and exits non-zero if a chart file is missing or unparseable, if one is missing
   `hand: true`, if a hash is not 40 hex characters, if a row has neither key, or
   if two rows fight over the same `cloudId`, `hash` or chart file. It never
   touches the network, so it is safe to run anywhere.
6. Load the track. The log says which door it came through:
   `chart: authored by cloudId`, `authored by hash`, `cached` or `generated`.

## The order every track is looked up in

1. `index.json` row matching `cloudId`
2. `index.json` row matching `hash`
3. the IndexedDB cache of generated charts, by `hash`
4. generate one from the audio

Steps 1 and 2 both answer before the audio is downloaded: 1 needs only the url,
and 2 needs the hash, which is a length and the first megabyte. An authored track
therefore costs no download at all.
