#!/usr/bin/env python3
"""
Pack indexer for the labeling game ("Beta Inspection Bureau").

Scans a folder of content-pack zip files (the MASTER, unencrypted zips) and
produces packs-index.json: the canonical registry of every image eligible to
enter the training set.

Identity model:
  - canonical id  = sha256 of the raw file bytes (survives renames, catches
                    re-encodes, dedupes across packs)
  - human identity = (pack, path-in-zip) — kept for display/debugging and as
                    the client-side lookup key when hashing isn't available

Only images present in this index may ever be labeled/trained on. The server
stores labels keyed by sha256; pixels never leave the user's machine.

Usage:
  python index_packs.py <packs-folder-or-zip> [more zips/folders...] [-o packs-index.json]

Requires: Pillow  (pip install Pillow)
"""

import argparse
import hashlib
import io
import json
import sys
import zipfile
from collections import defaultdict
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow is required: pip install Pillow")

IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"}
TOOL_VERSION = 1


def probe_image(data: bytes):
    """Return (width, height, format, frame_count) or None if not decodable."""
    try:
        with Image.open(io.BytesIO(data)) as im:
            frames = getattr(im, "n_frames", 1)
            return im.width, im.height, (im.format or "?").lower(), frames
    except Exception:
        return None


def index_zip(zip_path: Path, pack_id: str):
    """Yield one record per decodable image inside the zip."""
    skipped = []
    with zipfile.ZipFile(zip_path) as zf:
        for info in zf.infolist():
            if info.is_dir():
                continue
            ext = Path(info.filename).suffix.lower()
            if ext not in IMAGE_EXTS:
                continue
            data = zf.read(info)
            probed = probe_image(data)
            if probed is None:
                skipped.append(info.filename)
                continue
            w, h, fmt, frames = probed
            yield {
                "id": hashlib.sha256(data).hexdigest(),
                "pack": pack_id,
                "path": info.filename.replace("\\", "/"),
                "name": Path(info.filename).name,
                "format": fmt,
                "width": w,
                "height": h,
                "frames": frames,
                "bytes": len(data),
            }
    for name in skipped:
        print(f"  ! undecodable image skipped: {pack_id}:{name}", file=sys.stderr)


def collect_zips(inputs):
    zips = []
    for raw in inputs:
        p = Path(raw)
        if p.is_dir():
            zips.extend(sorted(p.glob("*.zip")))
        elif p.suffix.lower() == ".zip" and p.exists():
            zips.append(p)
        else:
            sys.exit(f"Not a zip or folder: {raw}")
    if not zips:
        sys.exit("No zip files found in the given inputs.")
    return zips


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("inputs", nargs="+", help="pack zip files and/or folders containing them")
    ap.add_argument("-o", "--output", default="packs-index.json", help="output index path")
    args = ap.parse_args()

    zips = collect_zips(args.inputs)
    images = []
    packs = {}

    for zp in zips:
        pack_id = zp.stem
        print(f"Indexing {pack_id} ({zp.name}) ...")
        records = list(index_zip(zp, pack_id))
        images.extend(records)
        packs[pack_id] = {
            "zip": zp.name,
            "images": len(records),
            "gif_files": sum(1 for r in records if r["frames"] > 1),
            "total_frames": sum(r["frames"] for r in records),
        }
        print(f"  {len(records)} images, {packs[pack_id]['total_frames']} frames")

    # --- integrity reports -------------------------------------------------
    by_hash = defaultdict(list)
    by_pack_name = defaultdict(list)
    for r in images:
        by_hash[r["id"]].append(r)
        by_pack_name[(r["pack"], r["name"])].append(r)

    cross_pack_dupes = {h: rs for h, rs in by_hash.items() if len(rs) > 1}
    name_collisions = {k: rs for k, rs in by_pack_name.items() if len(rs) > 1}

    unique_images = len(by_hash)
    index = {
        "version": TOOL_VERSION,
        "packs": packs,
        "unique_images": unique_images,
        "total_records": len(images),
        "images": images,
    }
    out = Path(args.output)
    out.write_text(json.dumps(index, indent=1), encoding="utf-8")

    print(f"\nWrote {out}  ({len(images)} records, {unique_images} unique by content hash)")
    if cross_pack_dupes:
        n_extra = sum(len(rs) - 1 for rs in cross_pack_dupes.values())
        print(f"Duplicates: {len(cross_pack_dupes)} images appear in >1 pack/path ({n_extra} redundant copies) — fine, they share one canonical id.")
    if name_collisions:
        print(f"WARNING: {len(name_collisions)} (pack, filename) pairs are ambiguous (same name, different files in one pack):")
        for (pack, name), rs in list(name_collisions.items())[:10]:
            print(f"  {pack}:{name} -> {[r['path'] for r in rs]}")
        print("  (pack+name lookup is NOT unique for these; hash lookup still is)")
    else:
        print("(pack, filename) is unique across every pack — name-based lookup is safe as a fallback.")


if __name__ == "__main__":
    main()
