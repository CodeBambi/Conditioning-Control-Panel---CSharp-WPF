"""SP-129 — cross-deliverable sweep for every correction made in either review round, in every
representation a claim can take: digit, word, and paraphrase.

WHY THIS IS COMMITTED RATHER THAN DESCRIBED. Round one's class sweep was a `grep` over DIGITS, so it
discharged "correct somewhere, stale somewhere else" in the digit domain and nowhere else. Two live
restatements survived it into round two: a WORDED retraction ("conservative in the over-reporting
direction") that landed in one deliverable of three, and three stale CITATIONS. Neither the number
sweep nor the label tally in GoonGameCensusTests can see either class — a citation is one of the
number sweep's excluded classes by construction, and no mechanism here reads prose.

So this is a HAND method, and an unlabelled hand method is what let those through twice. It is
committed so the next reviewer can re-run it instead of trusting that it was run.

    python spine-tasks/SP-129-goon-game-census/sweep-corrections.py

Exit code is 0 always: every hit is a CANDIDATE for human triage, never a verdict. The heuristic
only separates "this line RESTATES the retired form" from "this line DESCRIBES the correction".
"""
import io
import re
import sys

DELIVERABLES = [
    'client/docs/goon-game-census.md',
    'client/docs/wpf-surface-reachability.md',
    'spine-tasks/SP-129-goon-game-census/record.md',
]

# (label, retired-form regex, representation)
CORRECTIONS = [
    ('shipped-path files 4->5', r'\bfour files\b|\b4 of (the )?25\b|\b4/25\b', 'digit+word'),
    ('reference files 21->20', r'\b21 files\b|\btwenty-one\b|the other 21\b', 'digit+word'),
    ('shipped-path lines 3573->4018', r'\b3573\b', 'digit'),
    ('reference lines 8736->8291', r'\b8736\b', 'digit'),
    ('file share 16.0->20.0', r'\b16(\.0)?%|\bsixteen percent\b', 'digit+word'),
    ('line share 29.0->32.6', r'\b29\.0%', 'digit'),
    ('reference share 84->80.0', r'\b84%|\beighty-four\b', 'digit+word'),
    ('OWNER-GATED 6->7', r'\bsix OWNER-GATED\b|\bOWNER-GATED: 6\b|\b6 of 12\b', 'digit+word'),
    ('owner decisions ->five', r'\bSIX owner decisions\b|\bsix owner decisions\b', 'word'),
    ('B6 label', r'consequent of B5\*\*\s*\|', 'paraphrase'),
    ('assets call 1503->1504', r'Assets\.cs:1503\b', 'citation'),
    ('assets comment 1501->1502', r'Assets\.cs:1501\b|`:1501`', 'citation'),
    ('probes 21->22', r'DtrhCapabilityProbes\.cs:21\b', 'citation'),
    ('headline quote 24-26->25-27', r'GoonHostService\.cs:24-26\b', 'citation'),
    ('cap history 79->80', r'TransferInboxStore\.cs:79\b', 'citation'),
    ('closure direction retraction',
     r'over-report(ing)? direction|conservative in the safe direction|not silently under-report',
     'PARAPHRASE — the class that survived round one'),
    ('haptic count of 14', r'haptic count of 14\b', 'paraphrase'),
    ('three fixtures -> four', r'Three `TheNumberSweep_\*` fixtures', 'paraphrase'),
]

# A line that TALKS ABOUT a correction legitimately contains the retired form.
DESCRIBING = re.compile(
    r'was really|really `|is a comment; the call|MISSED the adjacent|overclaimed|retract|'
    r'earlier draft|first draft|pre-`?ConsentSheetMsg|stale|corrected|the correction|'
    r'said so|which is how|that is how|survived|should be |pinned at|is the doc comment|'
    r'the check that catches|\| \*\*(word|citation|digit)', re.I)

# Content in the shared reachability ledger that belongs to OTHER packets. This sweep is scoped to
# SP-129's own corrections; a hit outside the SP-129 section is another lane's and is not mine.
FOREIGN_SECTIONS = re.compile(r'^## SP-(?!129\b)')


def section_of(lines, index):
    for j in range(index, -1, -1):
        if lines[j].startswith('## SP-'):
            return lines[j]
    return ''


def main():
    live = 0
    total = 0
    for path in DELIVERABLES:
        lines = io.open(path, encoding='utf-8').read().split('\n')
        for label, pattern, kind in CORRECTIONS:
            rx = re.compile(pattern)
            for i, line in enumerate(lines):
                if not rx.search(line):
                    continue
                total += 1
                owner = section_of(lines, i)
                if FOREIGN_SECTIONS.match(owner):
                    verdict = f'another packet ({owner.strip()[:40]}) — not SP-129'
                elif DESCRIBING.search(line):
                    verdict = 'describes-the-correction'
                else:
                    verdict = '*** LIVE RESTATEMENT — TRIAGE ***'
                    live += 1
                print(f'{path}:{i + 1}  [{label} / {kind}]  {verdict}')
                print(f'    {line.strip()[:150]}')

    print(f'\ncandidates: {total}   flagged live: {live}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
