#!/usr/bin/env python3
"""
Keyframe sampler for the labeling game ("Beta Inspection Bureau").

Reads packs-index.json (from index_packs.py) plus the master pack zips,
decodes every animated file, perceptual-hashes each frame (dHash 64-bit),
and selects the visually DISTINCT keyframes per file. Writes the keyframe
list back into the index:

    images[i]["keyframes"]  = [0, 7, 19, ...]   # frame indices to label
    images[i]["kf_hashes"]  = ["a3f0...", ...]  # dHash hex per keyframe

Selection: greedy — keep a frame only if its Hamming distance to EVERY
already-kept keyframe >= threshold (comparing against all kept frames
dedupes looping/ping-pong animations, not just consecutive motion).
The threshold adapts upward until the count fits under --max-kf; if it
still overflows, the set is thinned uniformly. Stills get keyframes=[0].

Frame identity is (sha256, frameIndex) — indices are decode-order, which
any spec-conformant decoder reproduces. Frames that resemble an existing
keyframe don't need their own labels: boxes propagate.

Usage:
  python sample_keyframes.py <packs-folder> [-i packs-index.json] [--max-kf 12] [-j N]

Requires: Pillow  (pip install Pillow)
"""

import argparse
import io
import json
import sys
import time
import zipfile
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

try:
    from PIL import Image, ImageSequence
except ImportError:
    sys.exit("Pillow is required: pip install Pillow")

HASH_SIZE = 8  # dHash grid -> 64-bit hash
THRESHOLDS = [8, 10, 12, 14, 16, 20, 24]  # adaptive escalation (of 64 bits)


def dhash(im) -> int:
    g = im.convert("L").resize((HASH_SIZE + 1, HASH_SIZE), Image.BILINEAR)
    px = g.load()
    bits = 0
    for row in range(HASH_SIZE):
        for col in range(HASH_SIZE):
            bits = (bits << 1) | (1 if px[col, row] > px[col + 1, row] else 0)
    return bits


def hamming(a: int, b: int) -> int:
    return (a ^ b).bit_count()


def hash_frames(zip_path: str, member: str):
    """Worker: decode one animated file, return list of per-frame dHash ints."""
    hashes = []
    try:
        with zipfile.ZipFile(zip_path) as zf:
            data = zf.read(member)
        with Image.open(io.BytesIO(data)) as im:
            for frame in ImageSequence.Iterator(im):
                hashes.append(dhash(frame.convert("RGB")))
    except Exception as e:
        return member, hashes, f"{type(e).__name__}: {e}"
    return member, hashes, None


def select_keyframes(hashes, max_kf):
    """Pick distinct frame indices; adaptive threshold, uniform thin overflow."""
    n = len(hashes)
    if n <= 1:
        return [0] if n else []
    for t in THRESHOLDS:
        kept = [0]
        for i in range(1, n):
            if all(hamming(hashes[i], hashes[k]) >= t for k in kept):
                kept.append(i)
        if len(kept) <= max_kf:
            return kept
    # still too many at the loosest threshold: thin uniformly, keep first
    step = (len(kept) - 1) / (max_kf - 1)
    return sorted({kept[round(i * step)] for i in range(max_kf)})


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("packs", help="folder containing the master pack zips")
    ap.add_argument("-i", "--index", default=None, help="packs-index.json path (default: <packs>/packs-index.json)")
    ap.add_argument("--max-kf", type=int, default=12, help="max keyframes per file (default 12)")
    ap.add_argument("-j", "--jobs", type=int, default=None, help="worker processes (default: cpu-2)")
    args = ap.parse_args()

    packs_dir = Path(args.packs)
    index_path = Path(args.index) if args.index else packs_dir / "packs-index.json"
    index = json.loads(index_path.read_text(encoding="utf-8"))
    images = index["images"]

    zip_by_pack = {pid: packs_dir / meta["zip"] for pid, meta in index["packs"].items()}
    for pid, zp in zip_by_pack.items():
        if not zp.exists():
            sys.exit(f"Missing zip for pack '{pid}': {zp}")

    animated = [r for r in images if r["frames"] > 1]
    stills = [r for r in images if r["frames"] <= 1]
    for r in stills:
        r["keyframes"] = [0]

    # one task per (pack, path); duplicates by hash still decode per-location,
    # but selection results are keyed by record so dupes stay consistent.
    print(f"Hashing frames of {len(animated)} animated files "
          f"({sum(r['frames'] for r in animated)} frames) ...", flush=True)
    t0 = time.time()
    results = {}
    errors = []
    jobs = args.jobs or max(1, (__import__('os').cpu_count() or 4) - 2)
    with ProcessPoolExecutor(max_workers=jobs) as ex:
        futs = {
            ex.submit(hash_frames, str(zip_by_pack[r["pack"]]), r["path"]): r
            for r in animated
        }
        done = 0
        from concurrent.futures import as_completed
        for fut in as_completed(futs):
            r = futs[fut]
            member, hashes, err = fut.result()
            results[(r["pack"], r["path"])] = hashes
            if err:
                errors.append((r["pack"], member, err, len(hashes)))
            done += 1
            if done % 50 == 0 or done == len(animated):
                print(f"  {done}/{len(animated)} files hashed "
                      f"({time.time() - t0:.0f}s)", flush=True)

    total_kf = 0
    kf_counts = []
    for r in animated:
        hashes = results.get((r["pack"], r["path"]), [])
        kept = select_keyframes(hashes, args.max_kf)
        r["keyframes"] = kept
        r["kf_hashes"] = [f"{hashes[i]:016x}" for i in kept]
        kf_counts.append(len(kept))
        total_kf += len(kept)
    total_kf += len(stills)

    index["version"] = 2
    index["max_kf"] = args.max_kf
    index["total_keyframes"] = total_kf
    index_path.write_text(json.dumps(index, indent=1), encoding="utf-8")

    kf_counts.sort()
    print(f"\nWrote {index_path}")
    print(f"Labelable targets: {total_kf} keyframes "
          f"({len(stills)} stills + {sum(kf_counts)} from {len(animated)} animated files)")
    if kf_counts:
        med = kf_counts[len(kf_counts) // 2]
        print(f"Keyframes per animated file: median {med}, "
              f"min {kf_counts[0]}, max {kf_counts[-1]}, "
              f"single-keyframe (static loops): {sum(1 for c in kf_counts if c == 1)}")
    if errors:
        print(f"\n{len(errors)} files had decode errors (partial hashes used):")
        for pack, member, err, got in errors[:10]:
            print(f"  {pack}:{member} -> {err} ({got} frames hashed)")


if __name__ == "__main__":
    main()
