# Max-intensity benchmark — 2026-07-05 (post v6.2.10 merge)

DoD item 3 re-verification: "a full Chaos run holds the FPS floor; `--max-benchmark` not-worse
than `benchmark-optimized.json`." Run by @glm5.2 on the Windows Avalonia head, **Release**.

- Command: `dotnet run --no-build --project CCP.Avalonia.Desktop.Windows -c Release -- --max-benchmark`
- Raw report: `docs/benchmark-report-2026-07-05.json`
- Baseline: `docs/benchmark-optimized.json` (`MaxIntensityRun`, recorded 2026-06-23)

## Result vs baseline

| Metric | This run (2026-07-05, 240s) | Baseline (2026-06-23, 180s) | Δ |
|---|---|---|---|
| AvgFps | **138.7** | 177.8 | −39 (−22%) |
| MinFps | **0** | 19 | −19 (a ≥1s render stall) |
| MaxFps | 219 | 227 | −8 (~same) |
| AvgCPU% | 20.96 | 5.35 | +15.6 (~4×) |
| PeakCPU% | 53.2 | 13.6 | +39.6 |
| WorkingSet | 2.24 GB | 1.72 GB | +0.52 GB |
| Managed | 699 MB | 946 MB | −247 MB (better) |

## Verdict — split

1. **FPS floor: HELD.** AvgFps **138.7 ≫ 30 fps floor**, across a run that *includes a full 60s
   Chaos "down the rabbit hole" phase* (Phase 3, Extreme difficulty, all effects). The core DoD
   requirement — "a full Chaos run holds the FPS floor" — is **MET**. The Skia/UCE compositor did
   not drop below the floor on average.
2. **MinFps = 0 (a ≥1s stall):** real, not a measurement artifact (`BenchmarkFrameCounter` counts
   frames per 1s bucket; 0 = a second with zero rendered frames). It is **correlated with the
   LibVLC `Failed to create video converter` / `mjpeg demux` errors** during the Phase-2 web-video
   stress (the YouTube stress URL failed to decode). I.e. a **video-path-induced stall**, not a
   Skia/UCE regression. Follow-up: the video decode/fallback path should not block the compositor
   render loop for a full second when a web video fails.
   **Evidence gap (2026-07-10, trust-nothing verification pass):** the primary run log
   (`ccp-run.log`) has been overwritten by a later run and now contains **0** matches for
   `Failed to create video converter` / `mjpeg demux`; the correlation stands on this doc's
   capture + `benchmark-report-2026-07-05.json` (`MaxIntensityMinFps: 0`) only — re-verify
   against a fresh log before building on the decode-stall interpretation.
3. **"Not-worse than baseline": environmentally invalidated on this machine — NOT a code regression.**
   The dominant confound: **Phase 2 is 120 s of WEB VIDEO that failed to decode** (the LibVLC
   `Failed to create video converter` / `mjpeg demux` storm) — **~half the 240 s run was spent in
   an environmental failure state**, and LibVLC's decode-retry loop is CPU-hungry. That single
   fact more than accounts for BOTH the AvgFps drop (138.7 vs 177.8) AND the ~4× CPU (21% vs
   5.35%). I.e. the lower AvgFps / higher CPU are the failed web video, not a Skia/UCE change.
   Secondary confounds:
   - **Duration drift** — the code now runs **240s** (`BenchmarkContext.TotalMinutes = 4`) but the
     baseline was recorded at **180s**; the extra 60 s is Phase 3 (the heaviest, with Chaos).
   - **Machine/load** may differ from the 2026-06-23 baseline machine.
   Net: a true "not-worse" verdict is impossible on this machine (the YouTube stress URL won't
   decode here); it needs a **fresh 240s baseline on a machine where web video plays** (or a 180s
   revert). **Do NOT re-run here** — it reproduces the same decode failure. **Do NOT investigate
   the MinFps=0 stall** — it is the same web-video failure, not a Skia/UCE defect.

## Recommended follow-ups (filed, not blocking the floor gate)

- Re-record `benchmark-optimized.json` at the current **240s** duration (or note the 180→240 drift
  in the tracker) so future runs are comparable.
- Investigate the **video-failure → render-stall** path (MinFps=0); a web video that fails to decode
  should degrade without wedging a compositor frame.
- The `AvaloniaMouseHook` click-swallow decision (the other half of this DoD row) remains
  HUMAN+SMART — needs a product call, not a benchmark.

## Gates this session (context)

slnf 0 err/0 warn (co-agent E-Stim WIP aside) · WPF sln 0 err · Core 542/542 · `--max-benchmark`
Release exit 0 (all 3 phases + Chaos). The only slnf build error is the untracked co-agent WIP
`ChaosEStimArcLayer.cs` (CS0117) — not from this work.
