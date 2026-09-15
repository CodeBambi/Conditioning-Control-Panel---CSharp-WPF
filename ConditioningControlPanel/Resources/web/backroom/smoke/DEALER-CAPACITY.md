# Dealer capacity

The six-card Charlie belongs to player hands. An S17 dealer can legally draw twelve cards: six aces, a six, then five more aces makes hard 17. A rank-state enumeration, allowing unlimited rank copies as a conservative bound, proves no S17 path exceeds twelve. Six decks contain enough copies for that witness.

The source additive script at `C:/Projects/blender-scripting/card-table/build_play_anchors.py` authors twelve dealer anchors in two compact overlapping rows, preserving the original model and seated camera. Players retain six slots each. The asset pipeline preserves dealer anchors 0 through 11. Seven-card and twelve-card mocked hands traverse the actual station and shared renderer in cards-3d-check.mjs. Malformed slot indices are ignored by the view before allocating GPU resources; the station can finish its result and Back remains live.
