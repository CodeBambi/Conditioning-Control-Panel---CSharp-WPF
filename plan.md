# Quoted-needle citation check — measured plan

## Measured before writing any product code

Baseline `intra.mjs`: 6877 references, 1189 intra-client, **12 symbol-checked**, 1177 bare.

Candidate quoted needles adjacent to a RESOLVED INTRA-CLIENT citation: **41**.

Window sweep (needle must appear in the cited range extended by +/-B lines):

| B | match | miss |
|---:|---:|---:|
| 0 | 16 | 25 |
| 1 | 26 | 15 |
| 2 | 26 | 15 |
| 3 | 26 | 15 |
| 5 | 27 | 14 |
| 10 | 27 | 14 |
| 25 | 29 | 12 |
| whole file | 33 | 8 |

+/-1 is the knee. Everything +/-2..+/-25 adds is text that moved 4-154 lines, i.e. rot.

All 15 misses at +/-1 were hand-checked against the target files. **Zero false positives.**

## Rule

- Needle: a double-quoted run either IMMEDIATELY AFTER the citation (markup/punctuation only,
  no prose word) or immediately before a PARENTHESISED citation (`"x" (Foo.cs:12)`). The bare
  `"x", Foo.cs:12` list form is refused: in a list of `cite "q", cite "q"` each citation's
  preceding quote belongs to the PREVIOUS citation (2 of 3 measured bare-before hits were that).
- Haystack: cited range +/-1 line.
- Both sides normalised: XML doc tags, HTML entities, curly quotes/dashes, markdown emphasis,
  comment leaders, C# `" + "` concatenation seams, whitespace, case.
- Ellipsis (`...` / `…`) splits the needle into fragments that must appear IN ORDER.
- Floor of 12 characters; shorter runs are scare-quoted words, not quotations.

## What I fix vs what I leave

12 rows in `client/docs/port-completeness-census.md` and `client/docs/window-behavior-manifest.md`
are mine to correct. 3 are in `client/docs/task-board.md`, a chokepoint the packet forbids me to
edit; they are named in the report for the orchestrator.
