#!/usr/bin/env python3
"""link.py - put an authored chart in the race's index, and check the index.

    python link.py <chart.json> --url <cdn mp3 url> [--local <file.mp3>]
                                [--title T] [--name slug]
    python link.py --check

AUTHORED CHARTS ALWAYS WIN. A chart in Resources/web/dtrh/race/charts/ with top
level `hand: true` is used exactly as it was written, and the row in index.json
is how the game finds it before it downloads a byte. This writes that row, so
nobody computes a SHA1 by hand or guesses the id rule. Both keys are
race/charts/README.md's:

  cloudId  off the url alone. Ported from race/chartSource.js cloudIdFrom(): the
           LAST path segment that is a uuid or 20+ id characters, else the last
           segment with its extension off. Lower cased.
  hash     CHART.md's: SHA1 of the byte length as 8 bytes little endian plus the
           first 1 MiB. The same number as chartSource.js hashBytes(),
           chart/editor/audio.js hashFile() and common.py file_hash().

The hash comes off `--local` by reading a megabyte from disk, and off `--url` with
one HEAD for content-length then ONE ranged GET of bytes=0-1048575 (public cdn, no
credentials, no retries; a server that ignores the range sends the whole file and
the hash is still right). Give both and they are compared: when they disagree the
local copy is a DIFFERENT ENCODE, and the row gets the CLOUD hash, because the
cloud copy is what players hear. It says which it used either way.

Python 3, standard library only.
"""

import argparse
import hashlib
import json
import re
import struct
import sys
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import unquote, urlsplit

MIB = 1024 * 1024
HERE = Path(__file__).resolve().parent
RACE = HERE.parent.parent / "Resources" / "web" / "dtrh" / "race"
INDEX = RACE / "charts" / "index.json"
ROW_KEYS = ["cloudId", "hash", "title", "durationSec", "chart"]

UUID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", re.I)
IDISH = re.compile(r"^[A-Za-z0-9_-]+$")
EXT = re.compile(r"\.[a-z0-9]{2,4}$", re.I)
SHA1 = re.compile(r"^[0-9a-f]{40}$")
ID_MIN = 20


def cloud_id_from(url):
    """race/chartSource.js cloudIdFrom(), ported. A url this cannot parse is ''."""
    try:
        parts = urlsplit(str(url))
    except ValueError:
        return ""
    if not parts.scheme or not parts.netloc:
        return ""
    segs = []
    for raw in parts.path.split("/"):
        if not raw:
            continue
        try:
            segs.append(unquote(raw))
        except Exception:
            segs.append(raw)
    if not segs:
        return ""
    for seg in reversed(segs):
        if UUID.match(seg) or (len(seg) >= ID_MIN and IDISH.match(seg)):
            return seg.lower()
    return EXT.sub("", segs[-1]).lower()


def hash_bytes(head, total):
    """CHART.md: SHA1 of the length as 8 bytes LE plus the first 1 MiB."""
    if not total or total <= 0:
        return ""
    h = hashlib.sha1()
    h.update(struct.pack("<Q", int(total)))
    h.update(head[:MIB])
    return h.hexdigest()


def hash_local(path):
    p = Path(path)
    with p.open("rb") as fh:
        return hash_bytes(fh.read(MIB), p.stat().st_size)


def hash_url(url):
    """One HEAD then one ranged GET. Answers (hash, total, how) or (None, None, why)."""
    total = None
    try:
        head = urllib.request.Request(url, method="HEAD")
        with urllib.request.urlopen(head, timeout=30) as res:
            n = res.headers.get("content-length")
            if n and int(n) > 0:
                total = int(n)
    except Exception as e:
        print("  HEAD refused (%s), the range will have to carry the length" % e)
    get = urllib.request.Request(url, headers={"Range": "bytes=0-%d" % (MIB - 1)})
    try:
        with urllib.request.urlopen(get, timeout=60) as res:
            body = res.read(MIB + 1)
            code = res.status
            crange = res.headers.get("content-range") or ""
    except urllib.error.HTTPError as e:
        return None, None, "the cdn answered %s to a ranged GET" % e.code
    except Exception as e:
        return None, None, "the cdn would not answer a ranged GET (%s)" % e
    ranged = code == 206
    if not total:
        m = re.search(r"/\s*(\d+)\s*$", crange)
        if m and int(m.group(1)) > 0:
            total = int(m.group(1))
    if not ranged:
        # A 200 to a ranged GET is the whole file, so its own length is the length.
        if len(body) > MIB:
            return None, None, "the cdn ignored the range and the file is bigger than a read"
        total = len(body)
    if not total or total <= 0:
        return None, None, "no length anywhere, so this file cannot be named"
    how = "HEAD + a 206 range" if ranged else "a full 200 body (the cdn ignored the range)"
    return hash_bytes(body, total), total, how


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def write_json(path, data):
    text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    Path(path).write_bytes(text.encode("utf-8"))


def load_index():
    if not INDEX.exists():
        return {"version": 1, "tracks": []}
    idx = read_json(INDEX)
    if not isinstance(idx.get("tracks"), list):
        idx["tracks"] = []
    return idx


def order_row(row):
    out = {k: row[k] for k in ROW_KEYS if k in row and row[k] not in (None, "")}
    for k in row:
        if k not in out and row[k] not in (None, ""):
            out[k] = row[k]
    return out


def check():
    """Every row of index.json, or a non-zero exit. Nothing here touches the network."""
    bad = 0

    def no(msg):
        nonlocal bad
        bad += 1
        print("  BAD  " + msg)

    if not INDEX.exists():
        print("no index at " + str(INDEX))
        return 1
    try:
        idx = read_json(INDEX)
    except Exception as e:
        print("index.json does not parse: %s" % e)
        return 1
    rows = idx.get("tracks")
    if not isinstance(rows, list):
        print("index.json has no tracks list")
        return 1
    print("checking %d row%s in %s" % (len(rows), "" if len(rows) == 1 else "s", INDEX))
    seen_id, seen_hash, seen_chart = {}, {}, {}
    for i, row in enumerate(rows):
        where = "row %d" % i
        if not isinstance(row, dict):
            no("%s is not an object" % where)
            continue
        cid = str(row.get("cloudId") or "").lower()
        h = str(row.get("hash") or "").lower()
        rel = str(row.get("chart") or "")
        where = "row %d (%s)" % (i, cid or h or rel or "nameless")
        if not cid and not h:
            no("%s has neither a cloudId nor a hash, so nothing can ever match it" % where)
        if h and not SHA1.match(h):
            no("%s hash is not 40 hex characters: %r" % (where, row.get("hash")))
        for what, key, seen in (("cloudId", cid, seen_id), ("hash", h, seen_hash), ("chart file", rel, seen_chart)):
            if not key:
                continue
            if key in seen:
                no("%s repeats the %s of row %d: only the first would ever win" % (where, what, seen[key]))
            seen[key] = i
        d = row.get("durationSec")
        if d is not None and not (isinstance(d, (int, float)) and not isinstance(d, bool) and d > 0):
            no("%s durationSec is not a positive number: %r" % (where, d))
        if not rel:
            no("%s names no chart file" % where)
            continue
        f = RACE / rel
        if not f.exists():
            no("%s points at %s, which is not there" % (where, rel))
            continue
        try:
            chart = read_json(f)
        except Exception as e:
            no("%s chart %s does not parse: %s" % (where, rel, e))
            continue
        if chart.get("hand") is not True:
            no("%s chart %s is missing `hand: true`, so the game would not treat it as authored" % (where, rel))
        if not isinstance(chart.get("events"), list):
            no("%s chart %s has no events list" % (where, rel))
    if bad:
        print("\n%d problem%s" % (bad, "" if bad == 1 else "s"))
        return 1
    print("index ok")
    return 0


def link(args):
    chart_path = Path(args.chart)
    chart = read_json(chart_path)
    if chart.get("hand") is not True:
        # Keep the author's key order, but put the flag where a reader looks for it.
        rebuilt = {}
        for k, v in chart.items():
            rebuilt[k] = v
            if k == "version":
                rebuilt["hand"] = True
        if "hand" not in rebuilt:
            rebuilt = dict([("hand", True)] + list(chart.items()))
        chart = rebuilt
        print("set `hand: true` (it was not there)")

    cloud_id = cloud_id_from(args.url)
    if not cloud_id:
        print("that url has no id in it: " + args.url)
        return 1

    local = hash_local(args.local) if args.local else None
    # The cdn is always asked, even with --local, because "the local copy is a
    # different encode" is only knowable by holding both numbers up next to
    # each other. With no --local it is the only source there is.
    cloud, total, how = hash_url(args.url)

    if local and cloud and local != cloud:
        print("WARNING: the local file is a different encode of this track.")
        print("  local %s" % local)
        print("  cloud %s" % cloud)
        print("  the index gets the CLOUD hash, because the cloud copy is what players hear.")
        digest, source = cloud, "the cdn (%s)" % how
    elif cloud:
        digest, source = cloud, "the cdn (%s)" % how
        if local:
            print("the local copy and the cloud copy are the same bytes")
    elif local:
        print("the cdn could not be hashed (%s), falling back to the local copy" % how)
        digest, source = local, "the local file"
    else:
        print("no hash: %s, and no --local to fall back on" % how)
        return 1
    print("hash %s from %s" % (digest, source))

    src = chart.get("source") or {}
    duration = src.get("durationSec")
    slug = args.name or EXT.sub("", chart_path.name.replace(".chart.json", "")) or cloud_id
    slug = re.sub(r"[^a-z0-9._-]+", "-", str(slug).lower()).strip("-")
    title = args.title or src.get("name") or slug
    rel = "charts/%s.chart.json" % slug
    out = RACE / rel
    out.parent.mkdir(parents=True, exist_ok=True)
    write_json(out, chart)
    print("chart -> %s" % out)

    row = order_row({"cloudId": cloud_id, "hash": digest, "title": title, "durationSec": duration, "chart": rel})
    idx = load_index()
    # cloudId first and hash second, the same order the game looks a track up in.
    at = -1
    for i, r in enumerate(idx["tracks"]):
        if isinstance(r, dict) and (str(r.get("cloudId") or "").lower() == cloud_id
                                    or (digest and str(r.get("hash") or "").lower() == digest)):
            at = i
            break
    if at >= 0:
        print("replacing row %d (it already had this track)" % at)
        idx["tracks"][at] = row
    else:
        idx["tracks"].append(row)
    idx["version"] = idx.get("version") or 1
    write_json(INDEX, {"version": idx["version"], "tracks": idx["tracks"]})
    print("index -> %s" % INDEX)
    print(json.dumps(row, indent=2, ensure_ascii=False))
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description="link an authored chart into race/charts/index.json")
    ap.add_argument("chart", nargs="?", help="the chart JSON to link")
    ap.add_argument("--url", help="the cdn url the track plays from")
    ap.add_argument("--local", help="a local copy of the same audio, to hash without the network")
    ap.add_argument("--title", help="what the plate says (default: the chart's source.name)")
    ap.add_argument("--name", help="the slug the chart is saved under (default: the chart's file name)")
    ap.add_argument("--check", action="store_true", help="validate every row of index.json and exit")
    args = ap.parse_args(argv)
    if args.check:
        return check()
    if not args.chart or not args.url:
        ap.error("a chart and a --url, or --check")
    return link(args)


if __name__ == "__main__":
    sys.exit(main())
