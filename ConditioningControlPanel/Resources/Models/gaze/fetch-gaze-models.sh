#!/bin/sh
# Fetch the MobileGaze deep-gaze ONNX weights (Tier 2 gaze pipeline) into this folder.
# These are gitignored; run this once locally, and the installer bundles them for release.
set -e
BASE="https://github.com/yakhyo/gaze-estimation/releases/download/weights"
DIR="$(cd "$(dirname "$0")" && pwd)"
for m in mobileone_s0_gaze.onnx mobilenetv2_gaze.onnx resnet18_gaze.onnx resnet34_gaze.onnx resnet50_gaze.onnx; do
  echo "Fetching $m ..."
  curl -fSL -o "$DIR/$m" "$BASE/$m"
done
echo "Done. Models in $DIR"
