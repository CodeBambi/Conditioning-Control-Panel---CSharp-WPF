#!/usr/bin/env python3
"""
THE CHARGE-HOLD's voice - procedural WAVs for the Trainer Card faucet.

House Book, Chapter 03 "THE CHARGE-HOLD": *conic ring fills over the hold budget,
tone rises with it; release early = THE SHIVER; complete = THE THUD*. Law X says
sight and sound land on the same frame, and the chime ladder "climbs one semitone
per link" rather than speeding up.

WPF cannot pitch-shift a clip, so the rising tone is a LADDER of six short pips
that the host plays in step with the ring (MainWindow.ProfileFaucet.cs). Six rungs
rather than one long sweep because the hold budget is variable (700ms + held/4,
cap 1400ms, owner call 2026-08-30) - a fixed-length sweep would finish in the
wrong place on a short hold, while a ladder always lands its top rung exactly as
the ring closes.

Also mints the early-release drop tone and the overflow chime layer.

Pure stdlib (wave + struct + math) on purpose: this runs on a clean checkout with
no numpy, exactly like the faucet_tick.wav that preceded it.

    python Tools/asset_gen/gen_faucet_charge_sfx.py

Writes into ConditioningControlPanel/Resources/sounds/. Deterministic - re-running
it produces byte-identical files, so it is safe to re-run before a release.
"""

import math
import os
import struct
import wave

RATE = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "ConditioningControlPanel", "Resources", "sounds"))

# The ladder. Roughly three semitones a rung over a bit more than an octave and a
# half - wide enough that the climb reads on laptop speakers under a wobble tick.
LADDER_HZ = [196.0, 233.0, 277.0, 330.0, 392.0, 466.0]

PIP_MS = 110
DROP_MS = 230
BRIM_MS = 540


def write_wav(name, samples):
    """16-bit mono PCM. Samples are floats in -1..1 and are hard-clipped."""
    path = os.path.join(OUT, name)
    frames = bytearray()
    for s in samples:
        v = int(max(-1.0, min(1.0, s)) * 32767.0)
        frames += struct.pack("<h", v)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(bytes(frames))
    print(f"  {name}  {len(frames)} bytes  ({len(samples) / RATE * 1000:.0f}ms)")


def env(i, n, attack=0.012, release=0.55):
    """Soft attack, exponential-ish release. Keeps every pip click-free."""
    t = i / RATE
    total = n / RATE
    a = min(1.0, t / attack) if attack > 0 else 1.0
    r_start = total * (1.0 - release)
    if t <= r_start:
        r = 1.0
    else:
        k = (t - r_start) / max(1e-6, total - r_start)
        r = math.exp(-4.2 * k)
    return a * r


def pip(freq):
    """One rung: a sine with a quiet second harmonic so it reads as metal, not a beep."""
    n = int(RATE * PIP_MS / 1000)
    out = []
    for i in range(n):
        t = i / RATE
        s = math.sin(2 * math.pi * freq * t) * 0.72
        s += math.sin(2 * math.pi * freq * 2 * t) * 0.16
        s += math.sin(2 * math.pi * freq * 3 * t) * 0.05
        out.append(s * env(i, n) * 0.5)
    return out


def drop():
    """
    THE SHIVER's audio half: the tone drops instead of climbing. A downward glide
    with one drip of body under it. The Brake says this is sympathy, never a
    buzzer - so it is quiet, short, and lands lower than rung 1.
    """
    n = int(RATE * DROP_MS / 1000)
    out = []
    phase = 0.0
    for i in range(n):
        k = i / n
        f = 176.0 * (1.0 - 0.36 * k)          # 176 Hz -> ~113 Hz
        phase += 2 * math.pi * f / RATE
        s = math.sin(phase) * 0.8 + math.sin(phase * 2) * 0.12
        out.append(s * env(i, n, attack=0.006, release=0.72) * 0.45)
    return out


def brim():
    """
    THE JACKPOT LADDER, minor rung: the extra chime layer that rides an overflow
    spill past the lip. A struck bell - three partials, fast decay - deliberately
    thin so it LAYERS over faucet_pour.wav instead of replacing it.
    """
    n = int(RATE * BRIM_MS / 1000)
    partials = [(1318.5, 1.00, 5.0), (1975.5, 0.42, 6.4), (2637.0, 0.20, 8.1)]
    out = []
    for i in range(n):
        t = i / RATE
        s = 0.0
        for f, amp, decay in partials:
            s += math.sin(2 * math.pi * f * t) * amp * math.exp(-decay * t)
        a = min(1.0, t / 0.004)
        out.append(s * a * 0.30)
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    print(f"faucet CHARGE-HOLD sfx -> {OUT}")
    for idx, hz in enumerate(LADDER_HZ, start=1):
        write_wav(f"faucet_charge_{idx}.wav", pip(hz))
    write_wav("faucet_charge_drop.wav", drop())
    write_wav("faucet_brim.wav", brim())
    print("done")


if __name__ == "__main__":
    main()
