export const meta = {
  name: 'port-wave',
  description: 'Run one greenfield port wave: validate, then plan/review/implement/review/review each packet concurrently',
  whenToUse: 'After packets are authored and committed, to run a wave of N lanes through the full Review-Level ladder without hand-driving each seat.',
  phases: [
    { title: 'Validate', detail: 'validate-wave.mjs must exit 0 or no lane launches' },
    { title: 'Plan', detail: 'one planner per packet, read-only' },
    { title: 'Review plan', detail: 'fresh-context plan review, bounded REVISE loop' },
    { title: 'Implement', detail: 'one lane per packet, worktree-isolated' },
    { title: 'Review code', detail: 'fresh-context code review, bounded REVISE loop' },
    { title: 'Review final', detail: 'fresh-context completion review' },
  ],
}

// ---------------------------------------------------------------------------
// Wave driver, encoded from the wave-30 reference trace (2026-08-14/15).
//
// WHY THE PLANNER AND THE IMPLEMENTER ARE SEPARATE AGENTS, and not one agent
// paused for review: wave 30 paused a single lane at the plan checkpoint and
// told it to change nothing. Its unchanged worktree was garbage-collected while
// it sat idle, so when it resumed it had no worktree and committed straight onto
// the shared port branch. Nothing warned. Splitting the roles means the planner's
// throwaway worktree dying is harmless and the implementer always starts from a
// fresh one, so that failure is structurally unreachable here rather than
// guarded against. The plan travels between them as TEXT, never as a worktree.
//
// The driver never lands. It ends by reporting branches and head SHAs; a fresh
// session verifies and merges, because the context that produced work must not
// certify it.
// ---------------------------------------------------------------------------

const MAX_REVISE = 2

const VERDICT = {
  type: 'object',
  required: ['verdict', 'feedback'],
  additionalProperties: false,
  properties: {
    verdict: { type: 'string', enum: ['APPROVE', 'REVISE', 'PASS', 'REPLAN'] },
    feedback: { type: 'string' },
  },
}

const LANE_RESULT = {
  type: 'object',
  required: ['branch', 'headSha', 'observedUnit', 'observedHeadless', 'declaredUnit', 'declaredHeadless', 'notes'],
  additionalProperties: false,
  properties: {
    branch: { type: 'string' },
    headSha: { type: 'string' },
    observedUnit: { type: 'integer' },
    observedHeadless: { type: 'integer' },
    declaredUnit: { type: 'integer' },
    declaredHeadless: { type: 'integer' },
    notes: { type: 'string' },
  },
}

const packets = Array.isArray(args) ? args : (args ? [args] : [])
if (packets.length === 0) {
  throw new Error('port-wave needs packet directory names as args, e.g. ["SP-074-slug","SP-075-slug"]')
}

const RULES = `
Operational rules for this repository, learned the hard way — follow them exactly:
- Run plain, SEPARATE shell commands. The worktree isolation guard refuses compound commands ("cd X && ..."). Chain nothing.
- BUILD IMMEDIATELY BEFORE THE GATE, in your own tree, and run BOTH through the slot semaphore, as two separate commands:
    node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
    node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
  The semaphore is not optional at wave scale: you are one of up to eight concurrent lanes on an 8-core / 31GB machine, and ungated parallel builds thrash CPU, RAM and the msbuild/NuGet file locks. It passes the child's exit code through unchanged, so a red gate stays red.
  Build first because the wrapper runs "dotnet test --no-build": a stale bin/ makes it report a count that has nothing to do with your source. At the wave-30 close this reported 1022 against a tree containing 1018.
- NEVER edit client/tests/floor/floor.json. Declare spine-tasks/<packet>/floor-delta.json = {packet, unit, headless, reason}. Your gate WILL report a pin mismatch; that is the designed state. Confirm observed == pin + your declared delta and report both numbers.
- Never edit client/docs/task-board.md, docs/constitution.md, ConditioningControlPanel/**, .spine/**, .pi/**, or .claude/**.
- No new wall-clock waits. Only client/tests/CcpClient.Tests/TestWait.cs is approved; TestTimingGuardTests enforces it mechanically.
- Never export CCP_DATA_ROOT process-wide.
`

phase('Validate')
log(`validating wave: ${packets.join(', ')}`)

const validation = await agent(
  `Run exactly this command from the repository root C:\\Code\\Conditioning-Control-Panel---CSharp-WPF and report its literal output and exit code:

node client/tools/wave/validate-wave.mjs ${packets.join(' ')}

Do not edit anything. Do not fix any violation it reports. Report ok=true ONLY if the exit code is 0.`,
  {
    label: 'validate-wave',
    phase: 'Validate',
    schema: {
      type: 'object',
      required: ['ok', 'output'],
      additionalProperties: false,
      properties: { ok: { type: 'boolean' }, output: { type: 'string' } },
    },
  }
)

if (!validation || !validation.ok) {
  log('WAVE REJECTED — no lane launched. Fix the packets, never the validator.')
  return { launched: false, validation: validation?.output ?? 'validator produced no parseable result (treated as rejection)' }
}

log(`wave validated; launching ${packets.length} lane(s)`)

// One pipeline per packet: each packet moves through the whole ladder on its own,
// so a slow planner never holds up another packet's implementation.
const results = await pipeline(
  packets,

  // --- Plan: read-only. Writes nothing; its worktree is expendable by design.
  (packet) => agent(
    `You are PLANNING one work item. Do not implement it and do not edit any product file.

Packet: spine-tasks/${packet}/PROMPT.md — read it in full; it is the contract.
Repo: C:\\Code\\Conditioning-Control-Panel---CSharp-WPF, branch feat/crossplatform.

Produce the plan a fresh-context reviewer will judge before any code is written:
1. The census or investigation the packet's first step demands, with File.cs:line evidence you opened yourself.
2. Which branch of any pre-authorized decision rule your evidence selects, and why. Resolve it on evidence; do not ask.
3. The proposed mechanism, and for each behaviour the packet FORBIDS, why your design cannot reach it.
4. Every lock, ordering, or concurrency invariant your change touches — name the actual lock, and check the written invariants near it rather than the one you assumed.
5. The facts you will add, and the INDEPENDENT revert that reds each one. An assertion that passes with the mechanism reverted is not a fact.
6. Anything out of File Scope you discovered, to be filed rather than fixed.
7. REUSE BEFORE YOU WRITE. Name the EXISTING helper, idiom, vocabulary or mechanism in this repository that your change reuses, cited \`File.cs:line\`. If you are introducing a new one, show the search you actually ran (the grep, the symbol you looked for) and say why nothing that exists fits. This repo already has typed-outcome vocabularies, a shared \`TestWait\`, harness/fake patterns, seam factories and skip-conversion patterns; a second one of anything is a cost, not a deliverable. The reviewer's contract already blocks "an abstraction with no concrete consumer" — do not hand it one. When the honest answer is "the mechanism already exists and the right change is to USE it", that is the strongest plan you can return, not a weaker one.

PERSIST IT BEFORE YOU RETURN. Write your plan to
  C:\\Code\\Conditioning-Control-Panel---CSharp-WPF\\.port\\plans\\${packet}\\plan-round-1.md
creating directories as needed. That path is gitignored, so it dirties no tree and collides with no lane's File Scope. Write it even though you are also returning it as text: if this packet later escalates, the returned text is discarded with the pipeline and the file is the only surviving artifact. Wave 31 lost six plans that way.

${RULES}`,
    { label: `plan:${packet}`, phase: 'Plan' }
  ),

  // --- Plan review, with a bounded REVISE loop. Fail closed on no verdict.
  async (plan, packet) => {
    let current = plan
    for (let round = 0; round <= MAX_REVISE; round++) {
      const verdict = await agent(
        `Review this PLAN against its packet BEFORE any implementation. Packet: spine-tasks/${packet}/PROMPT.md.
Repo: C:\\Code\\Conditioning-Control-Panel---CSharp-WPF.

Verify the plan's load-bearing claims against source yourself — do not accept them. Press hardest on: concurrency and lock-ordering arguments (check the lock the change ACTUALLY nests, not the one the plan names); any race the plan claims to have closed (walk the interleaving); whether each proposed fact would pin the semantics or merely execute the code; and whether the decision-rule branch is sound.

Return APPROVE or REVISE in your documented contract form. REVISE requires at least one finding in one of the blocking classes your contract enumerates — that list is exhaustive. Prose, wrong counts that do not select the edit target, a test name that misdescribes a correct fact, and anything about record.md content are SUGGESTIONS, and suggestions do not change the verdict.${round === 0 ? '' : `

THIS IS ROUND ${round + 1}: you are re-reviewing a REVISION, not re-opening the plan. Judge exactly two things — are the previous round's blocking issues resolved, and did the revision introduce a NEW defect in a blocking class. Do not raise fresh suggestion-class findings now.`}

--- PLAN (round ${round + 1}) ---
${current}`,
        { label: `plan-review:${packet}`, phase: 'Review plan', schema: VERDICT }
      )

      if (!verdict) {
        throw new Error(`${packet}: plan review returned no parseable verdict — failing closed as REVISE`)
      }
      if (verdict.verdict === 'APPROVE') {
        return { packet, plan: current, planFeedback: verdict.feedback }
      }
      if (round === MAX_REVISE) {
        throw new Error(`${packet}: plan still REVISE after ${MAX_REVISE + 1} rounds — escalating to the orchestrator rather than looping. Last feedback: ${verdict.feedback}`)
      }

      log(`${packet}: plan REVISE (round ${round + 1}) — revising`)
      current = await agent(
        `Revise your PLAN to address this review. Revise the PLAN, not code; implement nothing.

Packet: spine-tasks/${packet}/PROMPT.md

--- YOUR PLAN ---
${current}

--- REVIEW ---
${verdict.feedback}

ONLY the reviewer's \`### Blocking issues\` bind you. Address those.

Its \`### Suggestions (non-blocking)\` section MUST NOT change your plan's mechanism, fact set, revert matrix or floor delta. Carry those items forward VERBATIM under a \`## Carried conditions\` heading at the END of your plan so they reach the implementer, and do not expand the plan to pre-empt them. A plan that grows every round is exactly how this gate consumed six lanes in wave 31 without producing a line of code: each round handed the next fresh reviewer more surface to audit.

Verify each defect against source before accepting it, and say plainly if you believe one is wrong. If two defects share a root, say so and fix the root.

${RULES}`,
        { label: `plan-revise:${packet}`, phase: 'Review plan' }
      )
    }
  },

  // --- Implement: fresh worktree, commits on its own branch, never lands.
  (approved, packet) => agent(
    `Implement this pre-approved plan for packet spine-tasks/${packet}/PROMPT.md. The plan passed a fresh-context review; execute it faithfully rather than redesigning it.

Repo: C:\\Code\\Conditioning-Control-Panel---CSharp-WPF, base branch feat/crossplatform.

--- APPROVED PLAN ---
${approved.plan}

--- REVIEWER'S NOTES ON THAT APPROVAL ---
${approved.planFeedback}

Any \`## Carried conditions\` in the approved plan, and any \`### Suggestions (non-blocking)\` in those notes, are OBLIGATIONS you inherit — they were approved WITH, not waived. Discharge each one, or state in record.md why it does not apply. The final reviewer is given this same list and checks it, and a condition counts as discharged only when it is WRITTEN DOWN.

Execute the revert matrix for real: revert each mechanism source ONE AT A TIME, record how many facts red, and restore the tree byte-identically between reverts. Write spine-tasks/${packet}/record.md (census, decision, revert matrix with red counts, and an honesty section naming what is NOT proven) and spine-tasks/${packet}/floor-delta.json.

Do not merge, do not land, do not touch the board. Commit on YOUR branch.

Before reporting, run "git worktree list" and "git rev-parse --abbrev-ref HEAD" and confirm you are on your own worktree branch and NOT on feat/crossplatform. If you are on feat/crossplatform your isolation was lost — stop and say so instead of reporting success.

${RULES}`,
    { label: `implement:${packet}`, phase: 'Implement', schema: LANE_RESULT }
  ).then((lane) => (lane ? { ...lane, conditions: approved.planFeedback } : lane)),

  // --- Code review, bounded fix loop.
  async (lane, packet) => {
    if (!lane) throw new Error(`${packet}: implementation produced no parseable result`)
    let head = lane.headSha
    for (let round = 0; round <= MAX_REVISE; round++) {
      const verdict = await agent(
        `Review the committed lane DIFF for packet spine-tasks/${packet}/PROMPT.md.
Repo: C:\\Code\\Conditioning-Control-Panel---CSharp-WPF. Lane branch: ${lane.branch}, head ${head}.

Create your own scratch worktree under .worktrees/ on that branch so you can run the gate, and remove it when done.

EXPECTED, NOT A DEFECT: the lane is forbidden to edit client/tests/floor/floor.json, so check-floor.mjs WILL exit non-zero on a pin mismatch. The lane declares ${lane.declaredUnit} unit / ${lane.declaredHeadless} headless.

CRITICAL for a multi-lane wave: the pin you compare against is the pin ON DISK, which does NOT yet include any other lane's delta — the orchestrator sums every lane's declaration in ONE commit at land. So verify observed == pin + THIS lane's declared delta, and nothing else. Do not add another lane's numbers, and do not "reconcile" the pin. Treat any other number, any failure, or an unexpected skip as blocking.

Build and gate through the semaphore, as separate commands, because up to eight lanes are running:
    node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
    node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
BUILD FIRST — the wrapper is --no-build and a stale bin/ will lie about the source.

Judge whether the new tests pin the claimed semantics or merely execute the code; treat them as vacuous until shown otherwise. Return APPROVE or REVISE in your documented contract form.

--- CONDITIONS THIS PLAN WAS APPROVED WITH (not waived) ---
${lane.conditions ?? '(none recorded)'}`,
        { label: `code-review:${packet}`, phase: 'Review code', schema: VERDICT }
      )

      if (!verdict) throw new Error(`${packet}: code review returned no parseable verdict — failing closed as REVISE`)
      if (verdict.verdict === 'APPROVE') return { ...lane, headSha: head, codeFeedback: verdict.feedback }
      if (round === MAX_REVISE) {
        throw new Error(`${packet}: code still REVISE after ${MAX_REVISE + 1} rounds — escalating. Last: ${verdict.feedback}`)
      }

      log(`${packet}: code REVISE (round ${round + 1}) — fixing`)
      const fixed = await agent(
        `Address this blocking code review on your branch ${lane.branch} in packet ${packet}.

--- BLOCKING REVIEW ---
${verdict.feedback}

Verify each defect against source before accepting it. If a fix would change executable behaviour AFTER verification, prefer a record/comment fix and FILE the behavioural change instead — a post-verification behaviour change invalidates the revert matrix and is how this project once shipped a red base. If your change is comment-only, prove it: filter your diff for added and removed lines that are not comments and confirm both are empty.

Re-run build and gate, and report the same two numbers. Commit on your branch and report the new head SHA.

${RULES}`,
        { label: `code-fix:${packet}`, phase: 'Review code', schema: LANE_RESULT }
      )
      if (!fixed) throw new Error(`${packet}: code fix produced no parseable result`)
      head = fixed.headSha
    }
  },

  // --- Final review: judges the whole packet against its own contract.
  async (reviewed, packet) => {
    const verdict = await agent(
      `Final completion review for packet spine-tasks/${packet}/PROMPT.md, after plan and code review both approved. You are the seat that exists for what they missed.
Repo: C:\\Code\\Conditioning-Control-Panel---CSharp-WPF. Lane branch: ${reviewed.branch}, head ${reviewed.headSha}.

Judge the whole packet against its own contract: did the stated OUTCOME land, are the packet's forbidden outcomes genuinely unreachable, is the honesty section honest (and is anything material MISSING from it rather than merely present), and does the revert matrix show each fact guards its own mechanism? A residual that was decided "file, don't fix" is only filed if it is actually WRITTEN DOWN.

Return PASS, REVISE, or REPLAN in your documented contract form.

--- CODE REVIEW NOTES ---
${reviewed.codeFeedback}

--- CONDITIONS THE PLAN WAS APPROVED WITH ---
${reviewed.conditions ?? '(none recorded)'}

Treat that list as part of the packet's contract: each item was approved WITH, not waived. A condition is discharged only if the shipped tree or record.md actually says so — deciding to file something is not filing it. An undischarged, unmentioned condition is a REVISE.`,
      { label: `final-review:${packet}`, phase: 'Review final', schema: VERDICT }
    )
    if (!verdict) throw new Error(`${packet}: final review returned no parseable verdict — failing closed`)
    return { packet, branch: reviewed.branch, headSha: reviewed.headSha, verdict: verdict.verdict, feedback: verdict.feedback, lane: reviewed }
  }
)

const done = results.filter(Boolean)
const passed = done.filter((r) => r.verdict === 'PASS')
const held = done.filter((r) => r.verdict !== 'PASS')

log(`wave complete: ${passed.length} PASS, ${held.length} held, ${packets.length - done.length} failed outright`)

return {
  landed: false,
  note: 'This driver never lands. A FRESH session verifies the merged state, sums the floor deltas with client/tests/floor/sum-deltas.mjs --apply --packets <list>, merges, reconciles the board, and pushes.',
  passed: passed.map((r) => ({ packet: r.packet, branch: r.branch, headSha: r.headSha, declaredUnit: r.lane.declaredUnit, declaredHeadless: r.lane.declaredHeadless })),
  held: held.map((r) => ({ packet: r.packet, verdict: r.verdict, feedback: r.feedback })),
  summedDelta: {
    unit: passed.reduce((a, r) => a + r.lane.declaredUnit, 0),
    headless: passed.reduce((a, r) => a + r.lane.declaredHeadless, 0),
  },
}
