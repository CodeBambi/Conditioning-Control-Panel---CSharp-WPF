#!/usr/bin/env python3
"""Generate Resources/Models/blazeface_anchors.json — 896 SSD anchor centers
for the BlazeFace short-range model.

Port of IntelliProve's `_ssd_generate_anchors` with `SSD_OPTIONS_SHORT`
(reference: fdlite/face_detection.py, MIT-licensed). Bundling these is faster
than computing at startup and keeps the math auditable in the repo.

Run:  python tools/generate-blazeface-anchors.py

Writes Resources/Models/blazeface_anchors.json (and prints SHA256 for the
download script's pinned-hash table).
"""
import hashlib
import json
import os

# SSD_OPTIONS_SHORT (face_detection_short_range_common.pbtxt)
NUM_LAYERS = 4
INPUT_HEIGHT = 128
INPUT_WIDTH = 128
ANCHOR_OFFSET_X = 0.5
ANCHOR_OFFSET_Y = 0.5
STRIDES = [8, 16, 16, 16]
INTERPOLATED_SCALE_ASPECT_RATIO = 1.0


def generate_anchors():
    anchors = []
    layer_id = 0
    while layer_id < NUM_LAYERS:
        last_same_stride_layer = layer_id
        repeats = 0
        while last_same_stride_layer < NUM_LAYERS and STRIDES[last_same_stride_layer] == STRIDES[layer_id]:
            last_same_stride_layer += 1
            repeats += 2 if INTERPOLATED_SCALE_ASPECT_RATIO == 1.0 else 1
        stride = STRIDES[layer_id]
        feature_map_height = INPUT_HEIGHT // stride
        feature_map_width = INPUT_WIDTH // stride
        for y in range(feature_map_height):
            y_center = (y + ANCHOR_OFFSET_Y) / feature_map_height
            for x in range(feature_map_width):
                x_center = (x + ANCHOR_OFFSET_X) / feature_map_width
                for _ in range(repeats):
                    anchors.append([x_center, y_center])
        layer_id = last_same_stride_layer
    return anchors


def main():
    anchors = generate_anchors()
    assert len(anchors) == 896, f"expected 896 anchors, got {len(anchors)}"

    project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_path = os.path.join(project_root, 'Resources', 'Models', 'blazeface_anchors.json')

    with open(out_path, 'w') as f:
        json.dump(anchors, f, separators=(',', ':'))

    with open(out_path, 'rb') as f:
        sha = hashlib.sha256(f.read()).hexdigest()

    size = os.path.getsize(out_path)
    print(f"Wrote {out_path}")
    print(f"  size:   {size} bytes")
    print(f"  sha256: {sha}")


if __name__ == '__main__':
    main()
