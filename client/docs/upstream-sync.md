# Upstream Sync Protocol

Use this protocol whenever the shipping WPF product changes and the greenfield client may have new
parity obligations. The WPF tree remains behavioral evidence; do not import its architecture,
platform implementation, or service topology into `client/`.

## Reconcile

1. Identify the upstream range and inspect its changed source, assets, localization, dependencies,
   and user-visible release notes.
2. Classify each change as already represented, a parity obligation, an intentional divergence, an
   owner decision, or irrelevant to the greenfield client.
3. Compare linked payload bytes and the committed payload inventory. A source change is not
   synchronized until the client inventory and copied output agree.
4. Add or update only the necessary task-board rows. Each row identifies behavior evidence,
   platform implications, privacy or security impact, and the verification needed to close it.

## Implementation Rules

- Work from narrow source evidence, including follow-up fixes and reversions when they explain the
  shipped behavior.
- Research current Avalonia and platform guidance before choosing a client mechanism.
- Preserve Windows and Linux as separate acceptance targets. Mark unavailable headed or manual
  evidence explicitly rather than inventing support.
- Keep product changes isolated from synchronization bookkeeping. Shared documents and test-floor
  metadata have a single owner.

## Verification

Run the relevant focused checks for the changed surface. For changes that require the standard
client gates, run:

```powershell
node client/tests/floor/check-warnings.mjs --cold
node client/tests/floor/check-floor.mjs
```

Run headed capture and interaction checks where the upstream change affects pixels, window behavior,
input, media, focus, scaling, compositing, or native integration. Update the task board with the
observed result or exact blocker; no historical log or workflow record substitutes for current
verification.
