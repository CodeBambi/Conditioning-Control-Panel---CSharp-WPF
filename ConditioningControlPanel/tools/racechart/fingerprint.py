#!/usr/bin/env python3
"""fingerprint.py - refine the words-file trigger seconds against the audio itself.

    python fingerprint.py --audio-dir <dir of <cloudId>.mp3>   [--write] [--only <cloudId>]
    python fingerprint.py --fetch                              [--write] [--only <cloudId>]
    python fingerprint.py --audio-dir <dir> --fetch   (local first, the rest fetched)

WHY. A words file puts a trigger phrase at the second the aligner heard it, which
is a good guess (Rapid Induction: onset minus t is a quarter second early in the
median). The plate, the row and the pop all land on that second, so every tenth
of a second the guess is off is a tenth the player feels. The phrase is also said
more than once in most files, and the aligner does not always hear every repeat.

WHAT. For each track in race/words/index.json and each TRIGGER_SET the maker's own
detector hears in it (scan-hits.mjs, so the windows are the game's windows):

  1. take the first N confident hits and cut their log-mel spectrogram slices
     (16 kHz mono, 25 ms hop, 48 mel bands) into one averaged TEMPLATE
  2. normalised cross-correlate the template along the whole track
  3. keep the peaks above --thresh, non-maximum suppressed over a phrase length
  4. a words-file hit with a peak within --match seconds MOVES to the peak
     (src 'fp'); one with no peak is kept where it was (src 'words', its own
     score); a peak above --new with no words-file hit near it is a NEW repeat
     (src 'fp'). --new is strict on purpose: a one-syllable phrase's template
     scores 0.72 to 0.77 on the wrong syllable, a true repeat by the same voice
     over 0.9 (measured on Rapid Induction, where 0.72 found nine that were not).

and, with --write, the words file gains a top-level `hits` array of
{ setId, t, dur, score, src, conf }, one minified line as before. race/words.js
passes it through and race/cloudChart.js prefers it over the live detector scan.

AUDIO NEVER LEAVES THE MACHINE. Charts hold timestamps and labels only. --fetch
downloads each missing track's PUBLIC cdn file (the url in race/levels.json) ONCE
into a temp dir with no credentials, no login, no retries and nothing else asked
of that server, and the temp dir is deleted when the run ends, whatever happened.
Decoded audio is freed between tracks (the longest is 29 minutes).

Nothing here prints a word of a transcript: set ids, seconds and scores only.

Needs python 3, numpy, scipy, an `ffmpeg` on PATH (the decoder) and `node` (for
scan-hits.mjs). The report goes to stdout.
"""

import argparse
import gc
import json
import shutil
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

import numpy as np
from scipy.signal import find_peaks
from scipy.signal import fftconvolve

HERE = Path(__file__).resolve().parent
RACE = HERE.parent.parent / 'Resources' / 'web' / 'dtrh' / 'race'
RATE = 16000                  # decode rate, mono
HOP = 400                     # 25 ms
WIN = 800                     # 50 ms Hann
NFFT = 1024
MELS = 48
FMIN, FMAX = 100.0, 6000.0    # the voice
BLOCK_FRAMES = 4800           # 2 minutes of frames per STFT block: bounds memory on a 29 minute file
TEMPLATE_MIN, TEMPLATE_MAX = 0.4, 2.0   # seconds; a hit's `dur` is the phrase's own length, clamped
TEMPLATE_N = 4                # confident hits averaged into the template
CONFIDENT = 0.8               # a hit is confident when every word in it is at least this sure
USER_AGENT = 'ccp-racechart-fingerprint/1 (offline chart tool)'


# ---- the audio ---------------------------------------------------------------

def decode(path):
    """16 kHz mono float32 through ffmpeg. Fails loud: a bad file is a message, not a chart."""
    cmd = ['ffmpeg', '-v', 'error', '-i', str(path), '-f', 'f32le', '-ac', '1', '-ar', str(RATE), '-']
    proc = subprocess.run(cmd, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError('ffmpeg could not decode %s: %s' % (path.name, proc.stderr.decode('utf-8', 'replace').strip()[:200]))
    return np.frombuffer(proc.stdout, dtype=np.float32)


def mel_bank():
    """MELS triangular filters over the rfft bins, FMIN..FMAX."""
    mel = lambda f: 2595.0 * np.log10(1.0 + f / 700.0)
    imel = lambda m: 700.0 * (10.0 ** (m / 2595.0) - 1.0)
    edges = imel(np.linspace(mel(FMIN), mel(FMAX), MELS + 2))
    freqs = np.fft.rfftfreq(NFFT, 1.0 / RATE)
    bank = np.zeros((MELS, len(freqs)), dtype=np.float32)
    for m in range(MELS):
        lo, mid, hi = edges[m], edges[m + 1], edges[m + 2]
        up = (freqs >= lo) & (freqs <= mid)
        dn = (freqs > mid) & (freqs <= hi)
        bank[m, up] = (freqs[up] - lo) / max(mid - lo, 1e-6)
        bank[m, dn] = (hi - freqs[dn]) / max(hi - mid, 1e-6)
    return bank


def logmel(x, bank):
    """(MELS, frames) log-mel spectrogram, per-band mean removed over the track (a whisper and a
    shout of the same phrase should correlate), then per-frame loudness removed."""
    n_frames = max(0, (len(x) - WIN) // HOP + 1)
    out = np.empty((MELS, n_frames), dtype=np.float32)
    window = np.hanning(WIN).astype(np.float32)
    for f0 in range(0, n_frames, BLOCK_FRAMES):
        f1 = min(n_frames, f0 + BLOCK_FRAMES)
        s0 = f0 * HOP
        seg = x[s0:s0 + (f1 - f0 - 1) * HOP + WIN]
        frames = np.lib.stride_tricks.as_strided(seg, shape=(f1 - f0, WIN), strides=(seg.strides[0] * HOP, seg.strides[0]))
        spec = np.abs(np.fft.rfft(frames * window, n=NFFT, axis=1)) ** 2       # (F, bins)
        out[:, f0:f1] = np.log(spec @ bank.T + 1e-8).T
        del frames, spec
    out -= out.mean(axis=1, keepdims=True)
    out -= out.mean(axis=0, keepdims=True)
    return out


# ---- the correlation ---------------------------------------------------------

def ncc(spec, template):
    """Normalised cross-correlation of `template` (MELS, L) along `spec` (MELS, T): one score per start
    frame, in [-1, 1]. The template is zero-meaned here, so the window's own mean drops out of the
    numerator and the denominator is the window's variance over the L*MELS cells."""
    T = template - template.mean()
    tn = float(np.sqrt((T * T).sum()))
    if tn < 1e-9:
        return np.zeros(max(0, spec.shape[1] - T.shape[1] + 1), dtype=np.float32)
    L = T.shape[1]
    ones = np.ones(L, dtype=np.float32)
    num = np.zeros(spec.shape[1] - L + 1, dtype=np.float64)
    for m in range(spec.shape[0]):
        num += fftconvolve(spec[m], T[m, ::-1], mode='valid')
    col = spec.sum(axis=0)                      # per-frame sums over bands
    col2 = (spec * spec).sum(axis=0)
    ssum = fftconvolve(col, ones, mode='valid')
    ssq = fftconvolve(col2, ones, mode='valid')
    var = ssq - ssum * ssum / (L * spec.shape[0])
    den = np.sqrt(np.maximum(var, 1e-6)) * tn
    return (num / den).astype(np.float32)


def template_for(spec, hits, n_frames):
    """The averaged slice of the first TEMPLATE_N confident hits, or None. Returns (template, L). The hits
    that went into it are marked `self`: their own peak is not a measurement, so the report leaves
    them out of the shift numbers."""
    sure = [h for h in hits if h['conf'] >= CONFIDENT and h['t'] >= 0]
    if not sure:
        sure = sorted(hits, key=lambda h: -h['conf'])[:1]
    sure = sure[:TEMPLATE_N]
    for h in sure:
        h['self'] = True
    durs = sorted(max(TEMPLATE_MIN, min(TEMPLATE_MAX, h['dur'] if h['dur'] > 0 else TEMPLATE_MIN)) for h in sure)
    L = int(round(durs[len(durs) // 2] * RATE / HOP))
    slices = []
    for h in sure:
        f0 = int(round(h['t'] * RATE / HOP))
        if f0 < 0 or f0 + L > n_frames:
            continue
        slices.append(spec[:, f0:f0 + L])
    if not slices:
        return None, L
    return np.mean(np.stack(slices), axis=0), L


def refine(spec, hits_by_set, dur_sec, thresh, new_thresh, match_sec):
    """One track. Returns (hits, stats): the refined hit list and the numbers for the report."""
    n_frames = spec.shape[1]
    out, moved, new_found = [], [], 0
    for set_id, hits in hits_by_set.items():
        template, L = template_for(spec, hits, n_frames)
        if template is None:
            for h in hits:
                out.append({'setId': set_id, 't': h['t'], 'dur': h['dur'], 'score': 0.0, 'src': 'words', 'conf': h['conf']})
            continue
        score = ncc(spec, template)
        if not len(score):
            continue
        peaks, props = find_peaks(score, height=thresh, distance=max(1, int(L * 0.7)))
        peak_t = peaks * HOP / RATE
        peak_s = props['peak_heights']
        taken = set()
        for h in hits:
            near = np.where(np.abs(peak_t - h['t']) <= match_sec)[0] if len(peak_t) else []
            if len(near):
                i = int(near[np.argmax(peak_s[near])])
                taken.add(i)
                t = float(round(peak_t[i], 3))
                out.append({'setId': set_id, 't': t, 'dur': h['dur'] if h['dur'] > 0 else round(L * HOP / RATE, 3),
                            'score': float(round(peak_s[i], 3)), 'src': 'fp', 'conf': h['conf']})
                if not h.get('self'):
                    moved.append((set_id, h['t'], t - h['t'], float(peak_s[i])))
            else:
                f0 = int(round(h['t'] * RATE / HOP))
                own = float(score[f0]) if 0 <= f0 < len(score) else 0.0
                out.append({'setId': set_id, 't': h['t'], 'dur': h['dur'], 'score': float(round(max(own, 0.0), 3)), 'src': 'words', 'conf': h['conf']})
        for i in range(len(peak_t)):
            if i in taken or peak_s[i] < new_thresh:
                continue
            if any(abs(peak_t[i] - h['t']) <= 1.0 for h in hits):
                continue
            if peak_t[i] + L * HOP / RATE > dur_sec:
                continue
            out.append({'setId': set_id, 't': float(round(peak_t[i], 3)), 'dur': round(L * HOP / RATE, 3),
                        'score': float(round(peak_s[i], 3)), 'src': 'fp', 'conf': float(round(min(1.0, peak_s[i]), 2))})
            new_found += 1
        del score
    out.sort(key=lambda h: (h['t'], h['setId']))
    return out, moved, new_found


# ---- the files ---------------------------------------------------------------

def scan_hits(words_dir):
    proc = subprocess.run(['node', str(HERE / 'scan-hits.mjs'), '--words-dir', str(words_dir)], capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError('scan-hits.mjs failed: ' + proc.stderr.decode('utf-8', 'replace').strip()[:300])
    return json.loads(proc.stdout.decode('utf-8'))


def level_urls():
    levels = json.loads((RACE / 'levels.json').read_text(encoding='utf-8'))
    return {lv['id']: lv['url'] for s in levels.get('sets', []) for lv in s.get('levels', [])}


def fetch_once(url, dest):
    """One GET of a public file, no credentials, no retry. Fails loud."""
    req = urllib.request.Request(url, headers={'User-Agent': USER_AGENT})
    with urllib.request.urlopen(req, timeout=120) as res, open(dest, 'wb') as out:
        shutil.copyfileobj(res, out, 1 << 20)


def write_words(path, hits):
    data = json.loads(path.read_text(encoding='utf-8'))
    ordered = {k: data[k] for k in ('version', 'hash', 'durationSec', 'words') if k in data}
    for k, v in data.items():
        if k not in ordered and k != 'hits':
            ordered[k] = v
    ordered['hits'] = hits
    # one minified line and no trailing newline, exactly as the files shipped: a regenerate is a one line diff
    path.write_text(json.dumps(ordered, separators=(',', ':'), ensure_ascii=False), encoding='utf-8')


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--audio-dir', type=Path, help='directory of <cloudId>.mp3 files')
    ap.add_argument('--fetch', action='store_true', help='download the missing public cdn files once into a temp dir, deleted after')
    ap.add_argument('--words-dir', type=Path, default=RACE / 'words')
    ap.add_argument('--only', help='one cloudId')
    ap.add_argument('--write', action='store_true', help='write the hits into the words files (default: report only)')
    ap.add_argument('--thresh', type=float, default=0.6, help='a peak must score at least this to move a hit')
    ap.add_argument('--new', type=float, default=0.85, help='and at least this to count as a new repeat (a one-syllable phrase scores 0.72 to 0.77 on the wrong syllable; a true repeat by the same voice scores over 0.9)')
    ap.add_argument('--match', type=float, default=0.4, help='a peak this close to a words-file hit refines it (the aligner is a quarter second early in the median, half a second at the tenth percentile)')
    args = ap.parse_args()
    if not args.audio_dir and not args.fetch:
        ap.error('give --audio-dir, --fetch or both')

    scan = scan_hits(args.words_dir)
    urls = level_urls()
    bank = mel_bank()
    tmp = Path(tempfile.mkdtemp(prefix='racechart-fp-')) if args.fetch else None
    fetched, shifts_all, summary = [], [], []
    try:
        for row in scan['rows']:
            cid = row['cloudId']
            if args.only and cid.lower() != args.only.lower():
                continue
            path = args.audio_dir / (cid + '.mp3') if args.audio_dir else None
            if not path or not path.exists():
                if not args.fetch:
                    print('%-24s SKIP: no %s.mp3 in --audio-dir and no --fetch' % (row['title'][:24], cid[:8]))
                    continue
                if cid not in urls:
                    print('%-24s SKIP: not in race/levels.json, so no public url to fetch' % row['title'][:24])
                    continue
                path = tmp / (cid + '.mp3')
                try:
                    fetch_once(urls[cid], path)
                    fetched.append(cid)
                except Exception as err:   # noqa: BLE001 - one bad download is a line in the report, not a crash
                    print('%-24s FAIL: fetch: %s' % (row['title'][:24], err))
                    continue
            try:
                x = decode(path)
                spec = logmel(x, bank)
                del x
            except Exception as err:   # noqa: BLE001
                print('%-24s FAIL: %s' % (row['title'][:24], err))
                continue
            by_set = {}
            for h in row['hits']:
                by_set.setdefault(h['setId'], []).append(h)
            hits, moved, new_found = refine(spec, by_set, row['durationSec'], args.thresh, args.new, args.match)
            del spec
            gc.collect()
            shifts = sorted(abs(m[2]) for m in moved)
            med = shifts[len(shifts) // 2] if shifts else 0.0
            mx = shifts[-1] if shifts else 0.0
            n_words = sum(1 for h in hits if h['src'] == 'words')
            summary.append((row['title'], len(row['hits']), len(hits), len(moved), med, mx, new_found, n_words))
            print('%-24s hits %3d -> %3d   moved %3d (median %.2fs, max %.2fs, template members left out)   new repeats %2d   kept as words %2d'
                  % (row['title'][:24], len(row['hits']), len(hits), len(moved), med, mx, new_found, n_words))
            for m in moved:
                shifts_all.append((row['title'], m[0], m[1], m[2], m[3]))
            if args.write:
                write_words(args.words_dir / row['file'], hits)
            if tmp and path.parent == tmp:
                path.unlink(missing_ok=True)   # the temp copy goes the moment it has been charted
    finally:
        if tmp:
            shutil.rmtree(tmp, ignore_errors=True)

    if shifts_all:
        print('\nthe 5 largest shifts (track, setId, words-file t, shift, score):')
        for s in sorted(shifts_all, key=lambda s: -abs(s[3]))[:5]:
            print('  %-24s %-14s %8.2fs  %+.2fs  %.2f' % (s[0][:24], s[1], s[2], s[3], s[4]))
    if fetched:
        print('\nfetched once and deleted: %d public cdn file(s): %s' % (len(fetched), ', '.join(c[:8] for c in fetched)))
    else:
        print('\nnothing fetched')
    print('written: %s' % ('yes, %d words file(s)' % len(summary) if args.write else 'no (--write to do it)'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
