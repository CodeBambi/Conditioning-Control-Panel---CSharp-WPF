#!/usr/bin/env python3
"""
Catalogue exporter for the labeling game server ("Beta Inspection Bureau").

Turns packs-index.json (v2, with keyframes) into bureau-catalogue.json:
the flat list of labelable targets ("<sha256>:<frameIndex>") to upload via
POST /admin/bureau/catalogue. Contains hashes only — no pixels, no pack
names, no filenames — safe to hand to the server.

Duplicate records (same hash in several packs) collapse to one target set.

Usage:
  python export_catalogue.py packs-index.json [-o bureau-catalogue.json] [--season 1]

Upload (from the server repo, with the admin token):
  curl -X POST https://<server>/admin/bureau/catalogue \
       -H "x-admin-token: $ADMIN_TOKEN" -H "Content-Type: application/json" \
       --data @bureau-catalogue.json
"""

import argparse
import json
from pathlib import Path


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("index", help="packs-index.json (version 2, keyframe-enriched)")
    ap.add_argument("-o", "--output", default="bureau-catalogue.json")
    ap.add_argument("--season", type=int, default=1)
    args = ap.parse_args()

    index = json.loads(Path(args.index).read_text(encoding="utf-8"))
    if index.get("version", 1) < 2:
        raise SystemExit("Index has no keyframes — run sample_keyframes.py first.")

    targets = set()
    for r in index["images"]:
        for f in r.get("keyframes", [0]):
            targets.add(f"{r['id']}:{f}")

    out = {
        "season": args.season,
        "replace": False,
        "targets": sorted(targets),
    }
    Path(args.output).write_text(json.dumps(out, indent=0), encoding="utf-8")
    print(f"Wrote {args.output}: {len(targets)} targets (season {args.season})")
    print("Pass replace=true in the body manually if re-seeding from scratch.")


if __name__ == "__main__":
    main()
