#!/usr/bin/env python3
"""Pair WPF reference renders with Avalonia renders per parity row.

usage: pair-renders.py <parity.md> <wpf-dir> <avalonia-dir> <out-dir>

Reads docs/avalonia-parity.md rows (id | WPF file(s) | Avalonia file(s) | status | notes), maps
each backticked file to a type name (basename up to the first '.', so MainWindow.Foo.cs ->
MainWindow and MainShellWindow.Foo.cs -> MainShellWindow), takes the first one per side that has a
<TypeName>.png render, and writes <out>/<row-id>.png = WPF left | Avalonia right. Also writes
_unpaired.txt (renders no row used), index.html and summary.json. Needs Pillow.
"""
import html
import json
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

LABEL_H = 28
MISSING = (420, 260)


def stems(cell):
    out = []
    for tok in re.findall(r"`([^`]+)`", cell):
        base = tok.rstrip("/").split("/")[-1]
        if tok.endswith("/") or not base:
            continue
        stem = base.split(".")[0]
        if stem and stem not in out:
            out.append(stem)
    return out


def rows(parity):
    seen = {}
    for line in open(parity, encoding="utf-8"):
        if not line.startswith("| "):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 4 or cells[0] in ("id", "---") or set(cells[0]) <= set("-"):
            continue
        rid = re.sub(r"[^A-Za-z0-9._-]", "_", cells[0].strip("`"))
        seen[rid] = seen.get(rid, 0) + 1
        if seen[rid] > 1:
            rid = f"{rid}-{seen[rid]}"
        yield rid, stems(cells[1]), stems(cells[2]), cells[3]


def renders(d):
    if not os.path.isdir(d):
        return {}
    return {f[:-4]: os.path.join(d, f) for f in os.listdir(d) if f.endswith(".png") and not f.startswith("_")}


def font(size):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            pass
    return ImageFont.load_default()


def half(path, title, f):
    img = Image.open(path).convert("RGB") if path else Image.new("RGB", MISSING, (40, 40, 40))
    out = Image.new("RGB", (img.width, img.height + LABEL_H), (20, 20, 20))
    out.paste(img, (0, LABEL_H))
    d = ImageDraw.Draw(out)
    d.text((6, 6), title, fill=(240, 240, 240), font=f)
    if not path:
        d.text((12, LABEL_H + 12), "no render", fill=(255, 120, 120), font=f)
    return out


def main(parity, wpf_dir, ava_dir, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    wpf, ava = renders(wpf_dir), renders(ava_dir)
    used_w, used_a = set(), set()
    f = font(14)
    summary = {"wpf_renders": len(wpf), "avalonia_renders": len(ava), "rows": []}
    for rid, ws, as_, status in rows(parity):
        w = next((s for s in ws if s in wpf), None)
        a = next((s for s in as_ if s in ava), None)
        if not w and not a:
            continue
        used_w.add(w)
        used_a.add(a)
        left = half(wpf.get(w), f"WPF {w or '-'}", f)
        right = half(ava.get(a), f"Avalonia {a or '-'}", f)
        img = Image.new("RGB", (left.width + right.width + 8, max(left.height, right.height) + LABEL_H), (0, 0, 0))
        ImageDraw.Draw(img).text((6, 6), f"{rid}  [{status}]", fill=(255, 210, 90), font=f)
        img.paste(left, (0, LABEL_H))
        img.paste(right, (left.width + 8, LABEL_H))
        img.save(os.path.join(out_dir, rid + ".png"))
        summary["rows"].append({"id": rid, "status": status, "wpf": w, "avalonia": a, "paired": bool(w and a)})

    unpaired = [f"wpf {k}" for k in sorted(set(wpf) - used_w)] + [f"avalonia {k}" for k in sorted(set(ava) - used_a)]
    with open(os.path.join(out_dir, "_unpaired.txt"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(unpaired) + "\n")
    summary["rows_written"] = len(summary["rows"])
    summary["rows_paired"] = sum(r["paired"] for r in summary["rows"])
    summary["unpaired_renders"] = len(unpaired)
    with open(os.path.join(out_dir, "summary.json"), "w", encoding="utf-8") as fh:
        json.dump(summary, fh, indent=2)

    parts = ["<!doctype html><meta charset=utf-8><title>WPF | Avalonia</title>",
             "<style>body{background:#111;color:#ddd;font:13px sans-serif}div{display:inline-block;margin:6px;"
             "width:320px;vertical-align:top}img{width:320px;border:1px solid #444}</style>"]
    for status in sorted({r["status"] for r in summary["rows"]}):
        group = [r for r in summary["rows"] if r["status"] == status]
        parts.append(f"<h2>{html.escape(status)} ({len(group)})</h2>")
        for r in group:
            href = html.escape(r["id"]) + ".png"
            parts.append(f'<div><a href="{href}"><img loading=lazy src="{href}"></a><br>{html.escape(r["id"])}</div>')
    with open(os.path.join(out_dir, "index.html"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(parts))
    print(f"wpf={len(wpf)} avalonia={len(ava)} rows={summary['rows_written']} "
          f"paired={summary['rows_paired']} unpaired_renders={len(unpaired)}")


if __name__ == "__main__":
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    main(*sys.argv[1:])
