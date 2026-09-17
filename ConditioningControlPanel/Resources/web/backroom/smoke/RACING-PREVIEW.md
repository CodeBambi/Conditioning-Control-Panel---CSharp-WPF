# Local casino racing bridge

These files reproduce the working localhost8798 preview without duplicating its26MB generated game tree in Git. They contain no server implementation or credentials. The existing preview host serves `/__phone-server.js` and owns a per-browser test ledger. This adapter reads that ledger and never writes account XP or casino SP.

The same-window desktop implementation uses normal dtrh paths. This staging script is only for the assembled casino preview whose server overlays `backroom/` paths. It rewrites copied public asset paths as UTF-8 into `backroom/racing/`; it never changes fallback economy/media/voice scripts or restarts the server.

1. Preserve the assembled casino preview and its current host. Apply `racing-preview.patch` to its backroom directory only if it has no racing portal yet. Check the patch first; a changed baseline requires a deliberate port, never forced replacement. The patch is against the Sep17 combined casino runtime, not current-main room code. It preserves documents, EMI, paid-result guards, and all other casino changes.
2. Run `node stage-racing-preview.mjs <combined-preview-root>` from this folder. The script finds the adjacent shipped dtrh source and copies only public runtime dependencies.
3. Reload `/backroom/index.html`. Purchase a demo or pack with the preview ledger, then click the cabinet or press E nearby. Back to casino returns to the saved position. Ending a drive uses Brake, end the run, surface, then Back to casino.

Do not commit the generated `backroom/racing/` directory. Save edits in the canonical dtrh source or this adapter, then stage again. The native Play entry remains independent. A preview test grants nothing on a real account.

Verified with a fresh isolated headless browser: bundle1 without demo; exactly3owned levels; actual pointer and phone taps; repeated launch; actual driving and visible pause/end/results/menu/return; exact camera restoration; no change to casino balance or grants; one run completion; zero page exceptions. Desktop27checks and phone30checks passed. The WPF build and native tests passed, but its actual window handoff still needs an owner desk check.

Run the saved regression from the repository root while the combined preview is already serving port 8798:

```powershell
node ConditioningControlPanel/Resources/web/backroom/smoke/casino-bridge-check.mjs
node ConditioningControlPanel/Resources/web/backroom/smoke/casino-bridge-check.mjs --phone
```

Run these sequentially: both use debugging port 9374. They start their own headless Chrome profile, block external requests, and write screenshots plus results under the ignored tmp-race-juice directory. They do not use the owner's browser. The harness requires a recent Node version with global WebSocket support.
