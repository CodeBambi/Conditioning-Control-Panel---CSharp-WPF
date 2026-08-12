## STATUS: SP-057 — Profile isolation seam (APPDATA trap + m2test fixture discipline)
**Current Step:** Step 1 — consumer census + seam design + pre-approach consult
**Last Updated:** 2026-08-12 (worker, step 1 in progress)
**Blockers:** none

### Step 1: consumer census + seam design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Census the real `DefaultSettingsPath()`/`SpecialFolder` consumer set (file:line table, bypasses named)
- [x] Prove the APPDATA trap as a fact in this repo (Windows + Linux behavior recorded)
- [x] Design the override (name, absolute-path rule, typed loud failure, choke-point entry)
- [x] Design the m2test declared-fixture discipline
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: implement the seam + fixture discipline
- [ ] Data-root override at the choke point; defaults unchanged both platforms
- [ ] Typed loud failure for unusable/relative override
- [ ] m2test clones the declared fixture, never the live doc
- [ ] Tests: per-consumer honoring, typed failure, fixture sourcing, bypass guard

### Step 3: real-profile byte-identity evidence (headed)
- [ ] Pre-run manifest of the real user data directory
- [ ] Headed run under the override on DISPLAY3 (DTRH host + m2test)
- [ ] Post-run manifest + diff: byte-identical real profile, override root populated
- [ ] Linux/WSLg disposition recorded honestly
- [ ] Transcripts (no negative case against the live profile)

### Step 4: record + pre-completion consult
- [ ] record.md (census, trap proof, design + rejected alternatives, manifests, consults, review presence)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs exit 0; build 0W/0E; ≥833 unit + ≥33 headless, TRX)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
