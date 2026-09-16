#!/usr/bin/env python3
"""align.py - script-align v2: every word of the script at the second it is said.

    python align.py --audio-dir <dir> [--only <stem>] [--dry | --write]
    python align.py --audio-dir <dir> --write --no-race     (chart files only)

WHY. The v1 words files (chart/words/NN Title.words.json) put each script word at
the second faster-whisper heard it, then script-align-v1 stitched the script onto
that transcript. fingerprint.py measured the result against the audio itself: a
quarter second early in the median, half a second at the tenth percentile. The
race lays every word bubble and every trigger row at word.t, so that slop is the
drop that feels early. This pass keeps whisper's script and throws away its
clock: the script is forced-aligned to the audio with a CTC model (torchaudio's
MMS_FA bundle, a 300M wav2vec2 trained for exactly this), which places each
character on a 20 ms frame.

HOW.
  1. the mp3 is decoded to 16 kHz mono through ffmpeg (fingerprint.py's decoder)
     and its CHART.md hash is checked against source.hash, so a different encode
     can never lend its clock to the cloud copy
  2. the model runs over 20 s chunks with a 1 s pad on both sides; the padded
     frames are dropped and the middles stitched into one emission matrix for the
     whole file (T frames x 29 tokens), so GPU memory is flat whatever the length
  3. the script is cut into segments at the v1 pauses (a gap of CUT_GAP_SEC once a
     segment spans SEG_MIN_SEC, or the widest gap since then once it spans
     SEG_MAX_SEC); each segment is aligned on its own window of emission, the v1
     span plus MARGIN_SEC on both sides, with the bundle's star token at both
     ends to absorb whatever the margin holds. The v1 times only choose the
     windows: inside a window the audio alone decides where a word lands
  4. a word is `placed` when the aligner scored it at least PLACE_MIN; below that
     (a stretch nobody says, a whisper under a whisper) it keeps its v1 second at
     conf FLOOR, and so does a word with no letters the model knows. Nothing is
     ever dropped: word count in == word count out, `i` and `w` untouched
  5. `d` keeps v1's convention: a word runs to the next word's start when that is
     within JOIN_SEC of its own end (whisper's contiguous words), else it is the
     model's span (floored at D_MIN); the last word is its span
  6. SANITY, before anything is written: over the placed words, v2.t must sit
     within AGREE_SEC of the v1 word's span [t, t + d] for more than AGREE_PCT of
     them, per track (whisper hangs the pause before a phrase on the phrase's
     first word, so v1.t alone is 1 to 2 s early on one word in eight; the strict
     |v2.t - v1.t| rate is printed beside it). A track that fails is reported and
     NOT written (a chunking drift, a wrong file, a script that does not match
     the audio). Rapid Induction's script starting at 80 s of a 162 s file is
     real, and passes

--dry reports and writes nothing. --write writes the chart/words file (one
minified line, same shape, engine "faster-whisper-large-v3 + script-align-v2")
and, unless --no-race, the race copy race/words/<cloudId>.json that
race/words.js reads (found through race/words/index.json by hash; `hits` on it
are left as they were, run fingerprint.py --write after this to rebuild them
on the new seconds).

AUDIO NEVER LEAVES THE MACHINE. Nothing here fetches, uploads or logs a word of
the script: file names, counts and seconds only. The audio has to be on disk
under --audio-dir (searched recursively for source.name).

Runs on C:/Tools/align-venv (python 3.12: torch has no wheel for 3.14 yet) with
torch 2.5.1+cu121, torchaudio 2.5.1, numpy and an `ffmpeg` on PATH. The MMS_FA
weights (1.2 GB) are fetched once by torchaudio into ~/.cache/torch/hub.
"""

import argparse
import gc
import json
import math
import re
import subprocess
import sys
from pathlib import Path

import numpy as np
import torch
import torchaudio
import torchaudio.functional as F

sys.path.insert(0, str(Path(__file__).resolve().parent))
from link import hash_local  # noqa: E402  (CHART.md's hash, the one the race keys on)

HERE = Path(__file__).resolve().parent
WEB = HERE.parent.parent / 'Resources' / 'web' / 'dtrh'
CHART_WORDS = WEB / 'chart' / 'words'
RACE_WORDS = WEB / 'race' / 'words'
ENGINE = 'faster-whisper-large-v3 + script-align-v2'

RATE = 16000
FRAME = 320                 # samples per emission frame: 20 ms
FRAME_SEC = FRAME / RATE
CHUNK_SEC = 20.0            # audio per model pass
PAD_SEC = 1.0               # context on both sides of a chunk, dropped after
MARGIN_SEC = 3.0            # window around a segment's v1 span
SEG_MIN_SEC = 40.0          # a segment is cut at the first CUT_GAP_SEC pause past this
SEG_MAX_SEC = 100.0         # or at the widest pause seen, once it spans this
CUT_GAP_SEC = 0.5
JOIN_SEC = 0.6              # a word runs to the next when that starts within this of its end
D_MIN = 0.1
PLACE_MIN = 0.10            # aligner score under this: the word keeps its v1 second
FLOOR = 0.05                # and says so with this conf
AGREE_SEC = 1.0
AGREE_PCT = 97.0
STEP = 0.01                 # two words never share a second

TRIGGER_WORDS = {'drop', 'sleep', 'relax', 'deeper', 'obey'}
TRIGGER_PHRASES = [('good', 'girl'), ('bambi', 'sleep')]
NUMBERS = {'0': 'zero', '1': 'one', '2': 'two', '3': 'three', '4': 'four', '5': 'five', '6': 'six', '7': 'seven',
           '8': 'eight', '9': 'nine', '10': 'ten', '11': 'eleven', '12': 'twelve', '13': 'thirteen', '14': 'fourteen',
           '15': 'fifteen', '16': 'sixteen', '17': 'seventeen', '18': 'eighteen', '19': 'nineteen', '20': 'twenty',
           '30': 'thirty', '40': 'forty', '50': 'fifty', '60': 'sixty', '70': 'seventy', '80': 'eighty', '90': 'ninety'}


# ---- the words ---------------------------------------------------------------

def normalise(tok):
    """The v1 rule for `w`: lowercase, apostrophes kept, everything else stripped."""
    t = tok.lower().replace('\u2019', "'").replace('\u2018', "'")
    return re.sub(r"[^a-z0-9']", '', t).strip("'")


def spell(w):
    """What the model reads for `w`: letters and apostrophes; a number becomes its name."""
    def num(m):
        s = m.group(0)
        if s in NUMBERS:
            return NUMBERS[s]
        if len(s) == 2 and s[0] in '23456789':
            return NUMBERS[s[0] + '0'] + NUMBERS[s[1]] if s[1] != '0' else NUMBERS[s]
        return ''.join(NUMBERS[c] for c in s)
    return re.sub(r"[^a-z']", '', re.sub(r'\d+', num, w))


def trigger_flags(words):
    """True for the trigger-set words: the five single words and the two phrases."""
    ws = [w['w'] for w in words]
    flag = [w in TRIGGER_WORDS for w in ws]
    for a, b in TRIGGER_PHRASES:
        for i in range(len(ws) - 1):
            if ws[i] == a and ws[i + 1] == b:
                flag[i] = flag[i + 1] = True
    return flag


def half_up(v, places=2):
    return math.floor(v * 10 ** places + 0.5) / 10 ** places


# ---- the audio and the model ------------------------------------------------

def decode(path):
    cmd = ['ffmpeg', '-v', 'error', '-i', str(path), '-f', 'f32le', '-ac', '1', '-ar', str(RATE), '-']
    proc = subprocess.run(cmd, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError('ffmpeg could not decode %s: %s' % (path.name, proc.stderr.decode('utf-8', 'replace').strip()[:200]))
    return np.frombuffer(proc.stdout, dtype=np.float32).copy()


def load_model(device):
    bundle = torchaudio.pipelines.MMS_FA
    model = bundle.get_model(with_star=True).to(device)
    return model, bundle.get_dict()


def emissions(model, x, device):
    """(T, C) log probabilities for the whole file, 20 s chunks with a 1 s pad each side."""
    chunk, pad = int(CHUNK_SEC * RATE), int(PAD_SEC * RATE)
    n_frames = max(1, (len(x) - 400) // FRAME + 1)
    out = np.full((n_frames, 29), -30.0, dtype=np.float32)
    out[:, 0] = 0.0
    for s0 in range(0, len(x), chunk):
        a, b = max(0, s0 - pad), min(len(x), s0 + chunk + pad)
        seg = torch.from_numpy(x[a:b]).unsqueeze(0).to(device)
        with torch.inference_mode():
            e, _ = model(seg)
        e = e[0].float().cpu().numpy()
        f_skip = (s0 - a) // FRAME
        f0 = s0 // FRAME
        take = e[f_skip:f_skip + chunk // FRAME]
        take = take[:max(0, n_frames - f0)]
        out[f0:f0 + len(take)] = take
        del seg, e
    if device == 'cuda':
        torch.cuda.empty_cache()
    return out


# ---- the alignment -----------------------------------------------------------

def segments(v1):
    """Runs of word indexes [i0, i1) cut at the v1 pauses."""
    cuts, start, best, best_gap, i = [], 0, -1, 0.0, 0
    while i < len(v1) - 1:
        span = v1[i]['t'] + v1[i]['d'] - v1[start]['t']
        gap = v1[i + 1]['t'] - (v1[i]['t'] + v1[i]['d'])
        if span >= SEG_MIN_SEC and gap > best_gap:
            best, best_gap = i, gap
        cut = -1
        if span >= SEG_MIN_SEC and gap >= CUT_GAP_SEC:
            cut = i
        elif span >= SEG_MAX_SEC:
            cut = best if best >= 0 else i
        if cut >= 0:
            cuts.append((start, cut + 1))
            start, best, best_gap, i = cut + 1, -1, 0.0, cut + 1
            continue
        i += 1
    cuts.append((start, len(v1)))
    return [c for c in cuts if c[1] > c[0]]


def align_segment(E, v1, i0, i1, tokens, star):
    """One window. Returns per word (start_sec, end_sec, score) or None when the word had no letters,
    or None for the whole segment when the aligner could not run on it."""
    f0 = max(0, int((v1[i0]['t'] - MARGIN_SEC) / FRAME_SEC))
    f1 = min(len(E), int(math.ceil((v1[i1 - 1]['t'] + v1[i1 - 1]['d'] + MARGIN_SEC) / FRAME_SEC)))
    idx = [i for i in range(i0, i1) if tokens[i]]
    flat = [star] + [c for i in idx for c in tokens[i]] + [star]
    repeats = sum(1 for a, b in zip(flat, flat[1:]) if a == b)
    if f1 - f0 < len(flat) + repeats + 2:
        return None
    em = torch.from_numpy(E[f0:f1]).unsqueeze(0)
    try:
        labels, scores = F.forced_align(em, torch.tensor([flat], dtype=torch.int32), blank=0)
    except Exception as err:   # noqa: BLE001 - a window the aligner refuses is a kept segment, not a crash
        print('    segment %d..%d: aligner refused (%s)' % (i0, i1, str(err)[:80]))
        return None
    spans = F.merge_tokens(labels[0], scores[0].exp(), blank=0)
    lengths = [1] + [len(tokens[i]) for i in idx] + [1]
    if len(spans) != sum(lengths):
        return None
    out = {i: None for i in range(i0, i1)}
    k = 1
    for j, i in enumerate(idx):
        n = lengths[j + 1]
        ws = spans[k:k + n]
        k += n
        frames = sum(s.end - s.start for s in ws)
        score = sum(s.score * (s.end - s.start) for s in ws) / max(1, frames)
        out[i] = ((f0 + ws[0].start) * FRAME_SEC, (f0 + ws[-1].end) * FRAME_SEC, float(score))
    return out


def align_track(E, v1, dictionary):
    """Every word placed or kept. Returns (rows, raw, kept) where rows are the v2 words, raw the
    placed words' (i, t) before any ordering fix, and kept a count per reason."""
    star = dictionary['*']
    tokens = []
    for w in v1:
        letters = spell(w['w'])
        tokens.append([dictionary[c] for c in letters] if letters else [])
    placed = {}
    kept = {'empty': 0, 'weak': 0, 'order': 0, 'segment': 0}
    for i0, i1 in segments(v1):
        got = align_segment(E, v1, i0, i1, tokens, star)
        for i in range(i0, i1):
            r = got[i] if got else None
            if not tokens[i]:
                kept['empty'] += 1
            elif got is None:
                kept['segment'] += 1
            elif r[2] < PLACE_MIN:
                kept['weak'] += 1
            else:
                placed[i] = r
    raw = [(i, placed[i][0]) for i in sorted(placed)]
    # order: of two placed words out of order, the one further from its v1 second is the outlier and is kept
    order = []
    for i in sorted(placed):
        while order and placed[i][0] <= placed[order[-1]][0]:
            if abs(placed[i][0] - v1[i]['t']) < abs(placed[order[-1]][0] - v1[order[-1]]['t']):
                del placed[order.pop()]
                kept['order'] += 1
            else:
                break
        else:
            order.append(i)
            continue
        del placed[i]
        kept['order'] += 1
    rows = []
    for i, w in enumerate(v1):
        if i in placed:
            t, end, score = placed[i]
            rows.append({'i': w['i'], 't': t, 'end': end, 'w': w['w'], 'conf': score, 'kept': False})
        else:
            rows.append({'i': w['i'], 't': w['t'], 'end': w['t'] + max(D_MIN, w['d']), 'w': w['w'], 'conf': FLOOR, 'kept': True})
    # every second its own: a kept word is nudged behind its neighbour, never the other way round
    for i in range(1, len(rows)):
        if rows[i]['t'] <= rows[i - 1]['t']:
            rows[i]['t'] = rows[i - 1]['t'] + STEP
            rows[i]['end'] = max(rows[i]['end'], rows[i]['t'] + D_MIN)
    out = []
    for i, r in enumerate(rows):
        t = round(r['t'], 3)
        end = max(r['end'], t + D_MIN)
        if i + 1 < len(rows):
            nxt = round(rows[i + 1]['t'], 3)          # always past t: the pass above saw to it
            if end > nxt or nxt - end <= JOIN_SEC:
                end = nxt
        out.append({'i': r['i'], 't': t, 'd': round(end - t, 3), 'w': r['w'], 'conf': round(max(FLOOR, min(1.0, r['conf'])), 3)})
    return out, raw, kept


# ---- the report --------------------------------------------------------------

def quantiles(diffs):
    if not diffs:
        return 0.0, 0.0
    s = sorted(diffs)
    med = s[len(s) // 2]
    p90 = sorted(abs(d) for d in diffs)[min(len(s) - 1, int(0.9 * len(s)))]
    return med, p90


def span_dev(v1w, t):
    """How far a v2 second sits outside the v1 word's span [t, t + d]. Whisper hangs the pause before a
    phrase on the phrase's first word (d of 1 to 2 s on a word of 0.3 s), so a v2 second anywhere in
    that span is the same word heard at the right end of it, and counts as agreement."""
    lo, hi = v1w['t'], v1w['t'] + max(0.0, v1w['d'])
    return 0.0 if lo <= t <= hi else min(abs(t - lo), abs(t - hi))


def measure(v1, raw, flags):
    """Signed v2.t - v1.t (median, p90 of the magnitude) over every placed word and over the trigger
    words, then the two agreement rates: strict (|v2.t - v1.t| <= AGREE_SEC, the number the brief
    asked for) and by span (span_dev <= AGREE_SEC, the one the verdict uses)."""
    diffs = [(t - v1[i]['t'], flags[i]) for i, t in raw]
    every = [d for d, _ in diffs]
    trig = [d for d, f in diffs if f]
    strict = 100.0 * sum(1 for d in every if abs(d) <= AGREE_SEC) / max(1, len(every))
    agree = 100.0 * sum(1 for i, t in raw if span_dev(v1[i], t) <= AGREE_SEC) / max(1, len(raw))
    return quantiles(every), quantiles(trig), len(trig), strict, agree


# ---- the files ---------------------------------------------------------------

def find_audio(audio_dir, name, digest):
    for p in Path(audio_dir).rglob(name):
        if hash_local(p) == digest:
            return p
    return None


def write_chart(path, data, words):
    out = {}
    for k, v in data.items():
        out[k] = words if k == 'words' else v
    out['engine'] = ENGINE
    path.write_text(json.dumps(out, separators=(',', ':'), ensure_ascii=False), encoding='utf-8')


def race_copy(digest, duration, words):
    """The race copy that race/words.js reads, found by hash. `hits` stay until fingerprint.py rebuilds them."""
    index = json.loads((RACE_WORDS / 'index.json').read_text(encoding='utf-8'))
    row = next((r for r in index.get('rows', []) if str(r.get('hash', '')).lower() == digest), None)
    if not row:
        return None
    path = RACE_WORDS / row['file']
    old = json.loads(path.read_text(encoding='utf-8')) if path.exists() else {}
    lean = [{'t': w['t'], 'd': w['d'], 'w': w['w'], 'conf': half_up(w['conf'])} for w in words]
    data = {'version': 1, 'hash': digest, 'durationSec': int(round(duration)), 'words': lean}
    for k, v in old.items():
        if k not in data:
            data[k] = v
    path.write_text(json.dumps(data, separators=(',', ':'), ensure_ascii=False), encoding='utf-8')
    return path


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--audio-dir', type=Path, required=True, help='searched recursively for each file\'s source.name')
    ap.add_argument('--words-dir', type=Path, default=CHART_WORDS)
    ap.add_argument('--only', help='a substring of the words file name')
    ap.add_argument('--dry', action='store_true', help='report only (the default)')
    ap.add_argument('--write', action='store_true', help='write the passing tracks')
    ap.add_argument('--no-race', action='store_true', help='with --write: leave the race copies alone')
    ap.add_argument('--device', default='cuda' if torch.cuda.is_available() else 'cpu')
    args = ap.parse_args()

    files = sorted(p for p in args.words_dir.glob('*.words.json') if not args.only or args.only.lower() in p.name.lower())
    if not files:
        print('no words files under %s' % args.words_dir)
        return 1
    print('model: MMS_FA on %s (torch %s, torchaudio %s)' % (args.device, torch.__version__, torchaudio.__version__))
    model, dictionary = load_model(args.device)
    print('%-28s %5s %6s %-14s %-15s %-20s %6s %6s  %s' % ('track', 'words', 'placed', 'kept e/w/o/s', 'all med/p90', 'trig n med/p90', 'strict', 'span', 'verdict'))
    written, failed = [], []
    for path in files:
        data = json.loads(path.read_text(encoding='utf-8'))
        v1 = data['words']
        name = data['source']['name']
        digest = str(data['source']['hash']).lower()
        audio = find_audio(args.audio_dir, name, digest)
        if not audio:
            print('%-28s SKIP: no %s under --audio-dir with hash %s' % (path.stem[:28], name, digest[:8]))
            failed.append(path.stem)
            continue
        if len(data['text'].split()) != len(v1) or any(normalise(a) != b['w'] for a, b in zip(data['text'].split(), v1)):
            print('%-28s SKIP: the script and the v1 words disagree, so `w` could not be kept' % path.stem[:28])
            failed.append(path.stem)
            continue
        x = decode(audio)
        E = emissions(model, x, args.device)
        del x
        words, raw, kept = align_track(E, v1, dictionary)
        del E
        gc.collect()
        flags = trigger_flags(v1)
        (med, p90), (tmed, tp90), n_trig, strict, agree = measure(v1, raw, flags)
        passed = agree > AGREE_PCT and len(raw) >= 0.9 * len(v1)
        print('%-28s %5d %6d %-14s %+6.3f / %5.3f  %4d %+6.3f / %5.3f  %5.1f%% %5.1f%%  %s' % (
            path.stem[:28], len(v1), len(raw), '%d/%d/%d/%d' % (kept['empty'], kept['weak'], kept['order'], kept['segment']),
            med, p90, n_trig, tmed, tp90, strict, agree, 'ok' if passed else 'FAIL, not written'))
        if not passed:
            failed.append(path.stem)
            continue
        if args.write:
            write_chart(path, data, words)
            if not args.no_race:
                rc = race_copy(digest, data['source']['durationSec'], words)
                print('    wrote %s and %s' % (path.name, rc.name if rc else 'no race copy (hash not in race/words/index.json)'))
            else:
                print('    wrote %s' % path.name)
            written.append(path.stem)
    print('\nwritten: %s' % (', '.join(written) if written else 'nothing' + ('' if args.write else ' (--write to do it)')))
    if failed:
        print('not written: %s' % ', '.join(failed))
    if written and not args.no_race:
        print('now: python tools/racechart/fingerprint.py --audio-dir <dir of cloudId.mp3> --write  (the hits still sit on the old seconds)')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
