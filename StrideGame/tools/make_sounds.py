#!/usr/bin/env python3
"""Synthesizes the game's sound effects (gunshots, reload, hit, pickup...) as small mono WAV files."""
import math, os, random, struct, wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "ArenaRush.Game", "Resources", "Audio")


def save(name, samples):
    os.makedirs(OUT, exist_ok=True)
    peak = max(1e-6, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s / peak * 0.95)) * 32767)) for s in samples))


def lowpass(samples, k):
    out, y = [], 0.0
    for s in samples:
        y += k * (s - y)
        out.append(y)
    return out


def gunshot(seed, length, crack_decay, body_freq, body_decay, tone, tail):
    rnd = random.Random(seed)
    n = int(RATE * length)
    noise = [rnd.uniform(-1, 1) for _ in range(n)]
    dark = lowpass(noise, tone)
    out = []
    for i in range(n):
        t = i / RATE
        crack = noise[i] * math.exp(-t / crack_decay)
        body = math.sin(2 * math.pi * body_freq * t * (1 - t * 2)) * math.exp(-t / body_decay)
        rumble = dark[i] * math.exp(-t / tail) * 2.5
        out.append(crack * 0.9 + body * 0.8 + rumble)
    return out


def click(seed, freq, decay, length=0.06, noise_amt=0.5):
    rnd = random.Random(seed)
    n = int(RATE * length)
    return [(math.sin(2 * math.pi * freq * i / RATE) + rnd.uniform(-1, 1) * noise_amt) * math.exp(-i / RATE / decay) for i in range(n)]


def concat(*parts):
    out = []
    for gap, part in parts:
        out += [0.0] * int(RATE * gap) + part
    return out


save("shot_ak", gunshot(1, 0.55, 0.012, 95, 0.05, 0.08, 0.16))
save("shot_m4", gunshot(2, 0.45, 0.009, 130, 0.035, 0.12, 0.11))
save("shot_pistol", gunshot(3, 0.35, 0.007, 170, 0.025, 0.18, 0.07))
save("shot_enemy", gunshot(4, 0.5, 0.014, 85, 0.05, 0.05, 0.14))
save("reload", concat((0.0, click(5, 900, 0.02)), (0.35, click(6, 600, 0.03, 0.08)), (0.55, click(7, 1400, 0.015)), (0.2, click(8, 1100, 0.02))))
save("empty", click(9, 2200, 0.008, 0.04, 0.3))
save("hit", click(10, 1800, 0.02, 0.08, 0.2))
save("click", click(11, 1500, 0.01, 0.04, 0.1))
save("pickup", [math.sin(2 * math.pi * (600 + 900 * i / (RATE * 0.25)) * i / RATE) * math.exp(-i / RATE / 0.15) for i in range(int(RATE * 0.25))])
save("hurt", lowpass([random.Random(12).uniform(-1, 1) * math.exp(-i / RATE / 0.08) for i in range(int(RATE * 0.2))], 0.05))
print("sounds written to", OUT)
