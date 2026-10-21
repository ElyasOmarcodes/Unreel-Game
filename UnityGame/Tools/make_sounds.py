#!/usr/bin/env python3
"""
Synthesises the game's sound effects (gunshots, explosion, grenade, UI) into Assets/Resources/Audio.

Every gun has its own recipe: a short supersonic crack, the muzzle blast (band-passed noise),
a low thump that drops in pitch, the action's metallic click and an outdoor tail with echoes.
Run:  python3 UnityGame/Tools/make_sounds.py   (needs numpy and scipy)
"""
import os
import wave
import numpy as np
from scipy import signal

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Audio")
rng = np.random.default_rng(7)


def t_axis(seconds):
    return np.arange(int(SR * seconds)) / SR


def noise(seconds):
    return rng.standard_normal(int(SR * seconds))


def band(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="bandpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lowpass(x, hz, order=2):
    return signal.sosfilt(signal.butter(order, hz, btype="lowpass", fs=SR, output="sos"), x)


def highpass(x, hz, order=2):
    return signal.sosfilt(signal.butter(order, hz, btype="highpass", fs=SR, output="sos"), x)


def env(seconds, attack, tau, start=0.0):
    t = t_axis(seconds)
    e = np.where(t < start, 0.0, 1.0)
    tt = np.maximum(t - start, 0)
    a = np.minimum(tt / max(attack, 1e-5), 1.0)
    return e * a * np.exp(-tt / tau)


def pad(x, n):
    return np.pad(x, (0, max(0, n - len(x))))[:n]


def normalise(x, peak=0.89):
    x = x - np.mean(x)
    m = np.max(np.abs(x)) or 1.0
    return x / m * peak


def fade_out(x, seconds=0.05):
    n = min(len(x), int(SR * seconds))
    x[-n:] *= np.linspace(1, 0, n)
    return x


def save(name, x):
    x = fade_out(normalise(x))
    data = (x * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print(f"{name}: {len(x) / SR:.2f}s")


def thump(seconds, f0, f1, tau):
    t = t_axis(seconds)
    f = f1 + (f0 - f1) * np.exp(-t / (tau * 0.6))
    phase = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(phase) * env(seconds, 0.001, tau)


def click(seconds, at, freq, tau=0.004, level=1.0):
    """Metallic click of the action: a few damped resonances."""
    t = t_axis(seconds)
    out = np.zeros_like(t)
    tt = np.maximum(t - at, 0)
    on = t >= at
    for k, mult in enumerate((1.0, 2.31, 3.97, 5.6)):
        out += on * np.sin(2 * np.pi * freq * mult * tt) * np.exp(-tt / (tau / (1 + k * 0.5))) / (1 + k)
    return out * level


def tail(seconds, start, tau, lo, hi, level, echoes=()):
    """Outdoor reflections: smooth noise tail plus discrete slap-back echoes."""
    x = band(noise(seconds), lo, hi) * env(seconds, 0.01, tau, start) * level
    for delay, gain in echoes:
        x += band(noise(seconds), lo * 0.8, hi * 0.6) * env(seconds, 0.003, 0.035, start + delay) * level * gain * 2.5
    return x


def gunshot(name, length, crack, blast_lo, blast_hi, blast_tau, thump_f, thump_tau, thump_level,
            tail_tau, tail_level, drive, action=None, echoes=((0.11, 0.5), (0.27, 0.3))):
    n = int(SR * length)
    x = np.zeros(n)
    if crack > 0:  # supersonic N-wave: very short broadband snap
        x += highpass(noise(length), 2500) * env(length, 0.0002, 0.0015) * crack
    x += band(noise(length), blast_lo, blast_hi) * env(length, 0.0006, blast_tau) * 1.0
    x += lowpass(noise(length), 500) * env(length, 0.002, blast_tau * 2.2) * 0.6
    x += thump(length, thump_f * 2.2, thump_f, thump_tau) * thump_level
    if action:
        at, freq, level = action
        x += click(length, at, freq, level=level)
    # saturate only the blast, then add the reflections well below it
    x = np.tanh(normalise(x, 1.0) * drive) / np.tanh(drive)
    reflections = tail(length, 0.012, tail_tau, 150, 2200, 1.0, echoes)
    x += normalise(reflections, 1.0) * tail_level * 0.38
    save(name, x)


def explosion():
    length = 3.2
    x = lowpass(noise(length), 900) * env(length, 0.004, 0.22) * 1.0
    x += thump(length, 90, 32, 0.35) * 1.4
    x += band(noise(length), 1200, 6000) * env(length, 0.0005, 0.02) * 0.8
    rumble = tail(length, 0.05, 0.9, 80, 1200, 1.0, ((0.18, 0.6), (0.42, 0.4), (0.8, 0.25)))
    # debris: random small crackles in the first second
    for _ in range(60):
        at = rng.uniform(0.08, 1.1)
        x += highpass(noise(length), 1500) * env(length, 0.0003, 0.003, at) * rng.uniform(0.05, 0.25) * np.exp(-at * 2)
    x = np.tanh(normalise(x, 1.0) * 2.2)
    save("explosion", x + normalise(rumble, 1.0) * 0.3)


def ui():
    # soft "tock" for every button: two partials, very short, low-passed
    length = 0.09
    t = t_axis(length)
    x = (np.sin(2 * np.pi * 1320 * t) + 0.5 * np.sin(2 * np.pi * 2640 * t)) * env(length, 0.0015, 0.012)
    x += lowpass(noise(length), 6000) * env(length, 0.0003, 0.0015) * 0.35
    save("click", lowpass(x, 7000))
    # back: a lower, falling tick
    length = 0.12
    t = t_axis(length)
    f = 760 - 260 * t / length
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * env(length, 0.002, 0.03)
    save("ui_back", lowpass(x, 5000))
    # confirm: two soft bell notes
    length = 0.45
    t = t_axis(length)
    x = np.zeros_like(t)
    for at, f in ((0.0, 987.8), (0.075, 1479.98)):
        tt = np.maximum(t - at, 0)
        x += (t >= at) * (np.sin(2 * np.pi * f * tt) + 0.3 * np.sin(2 * np.pi * f * 2 * tt)) * np.exp(-tt / 0.11) * np.minimum(tt / 0.003, 1)
    save("ui_confirm", x)
    # dry trigger click when the magazine is empty
    length = 0.12
    save("empty", click(length, 0.002, 2100, 0.006) + highpass(noise(length), 3000) * env(length, 0.0002, 0.002) * 0.4)


def grenade():
    length = 0.5
    # pin pulled: ring scrape and spoon flick
    x = highpass(noise(length), 3000) * env(length, 0.01, 0.04) * 0.4 + click(length, 0.18, 2600, 0.01, 0.8)
    save("grenade_pin", x)
    length = 0.25
    save("grenade_bounce", click(length, 0.001, 900, 0.02, 1.0) + lowpass(noise(length), 1500) * env(length, 0.0005, 0.01) * 0.5)


def main():
    os.makedirs(OUT, exist_ok=True)
    #        name           len  crack lo    hi    tau    thumpHz tau  lvl  tail  lvl  drive action(at, Hz, level)
    gunshot("shot_ak",      1.0, 0.7, 350, 3800, 0.022, 78, 0.06, 0.9, 0.42, 0.40, 3.2, (0.065, 1500, 0.25))
    gunshot("shot_m4",      0.9, 0.9, 600, 6500, 0.015, 95, 0.045, 0.6, 0.34, 0.33, 2.8, (0.05, 2300, 0.22))
    gunshot("shot_shotgun", 1.3, 0.2, 220, 2600, 0.04, 55, 0.11, 1.2, 0.65, 0.48, 3.6, None,
            ((0.13, 0.6), (0.33, 0.35)))
    gunshot("shot_sniper",  2.0, 1.2, 280, 5000, 0.035, 48, 0.12, 1.3, 1.25, 0.55, 3.8, (0.75, 1100, 0.07),
            ((0.16, 0.7), (0.42, 0.5), (0.9, 0.3)))
    gunshot("shot_pistol",  0.7, 0.4, 900, 7000, 0.010, 120, 0.03, 0.45, 0.26, 0.28, 2.4, (0.03, 2800, 0.25))
    gunshot("shot_deagle",  1.0, 0.6, 450, 4200, 0.025, 70, 0.07, 1.0, 0.5, 0.42, 3.4, (0.04, 1700, 0.3))
    gunshot("shot_smg",     0.6, 0.3, 1100, 7500, 0.008, 125, 0.025, 0.35, 0.22, 0.22, 2.2, (0.025, 3200, 0.3))
    gunshot("shot_marksman", 1.6, 1.0, 400, 5600, 0.028, 60, 0.09, 1.1, 0.95, 0.48, 3.5, (0.08, 1800, 0.12),
            ((0.15, 0.6), (0.38, 0.4), (0.8, 0.25)))
    explosion()
    grenade()
    ui()


if __name__ == "__main__":
    main()
