"""Music and effects for the Paddock brag video, written as one piece in A minor at 120 bpm."""
import numpy as np
from scipy.signal import butter, sosfilt, fftconvolve
from scipy.io import wavfile

SR, DUR = 44100, 20.0
N = int(SR * DUR)
rng = np.random.default_rng(3)
dry = np.zeros((N, 2))
send = np.zeros((N, 2))  # reverb send

NOTE = {n: i for i, n in enumerate(['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'])}
def hz(name):
    return 440.0 * 2 ** ((NOTE[name[:-1]] + 12 * (int(name[-1]) + 1) - 69) / 12)

def put(sig, t, gain=1.0, pan=0.0, wet=0.3):
    i = int(t * SR)
    if i >= N: return
    sig = sig[:N - i] * gain
    l, r = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
    st = np.stack([sig * l, sig * r], axis=1)
    dry[i:i + len(sig)] += st
    send[i:i + len(sig)] += st * wet

def env(n, a, d):
    t = np.arange(n) / SR
    return np.minimum(t / a, 1.0) * np.exp(-np.maximum(t - a, 0) / d)

def pluck(f, dur=0.6, bright=3):
    n = int(dur * SR); t = np.arange(n) / SR
    s = sum(np.sin(2 * np.pi * f * k * t) / k ** 1.6 * np.exp(-t * (3 + 2.5 * k)) for k in range(1, bright + 1))
    return s * np.minimum(t / 0.004, 1.0)

def pad(freqs, dur):
    n = int(dur * SR); t = np.arange(n) / SR
    s = np.zeros(n)
    for f in freqs:
        for det in (-0.004, 0.0, 0.004):
            s += np.sin(2 * np.pi * f * (1 + det) * t + rng.uniform(0, 6.28)) + 0.3 * np.sin(4 * np.pi * f * (1 + det) * t)
    a = np.minimum(t / 0.5, 1.0) * np.minimum((dur - t) / 0.5, 1.0)
    return s * a / (3 * len(freqs))

def noise(dur, lo, hi):
    n = int(dur * SR)
    return sosfilt(butter(2, [lo, hi], btype='band', fs=SR, output='sos'), rng.standard_normal(n))

def swell(dur, lo, hi, peak=0.6):
    n = int(dur * SR); x = np.arange(n) / n
    e = np.where(x < peak, (x / peak) ** 2, ((1 - x) / (1 - peak)) ** 1.5)
    return noise(dur, lo, hi) * e

def kick():
    n = int(0.22 * SR); t = np.arange(n) / SR
    return np.sin(2 * np.pi * (48 * t + 30 * (1 - np.exp(-t * 28)) / 28 * 3)) * np.exp(-t * 16)

def pip(f, dur=0.09):
    n = int(dur * SR); t = np.arange(n) / SR
    return np.sin(2 * np.pi * f * t) * env(n, 0.003, 0.025)

# harmony: one chord per 2s bar
BARS = [('Am', ['A2', 'A3', 'C4', 'E4']), ('Am', ['A2', 'A3', 'C4', 'E4']), ('F', ['F2', 'A3', 'C4', 'F4']),
        ('F', ['F2', 'A3', 'C4', 'E4']), ('C', ['C3', 'G3', 'C4', 'E4']), ('G', ['G2', 'G3', 'B3', 'D4']),
        ('Am', ['A2', 'A3', 'C4', 'E4']), ('F', ['F2', 'A3', 'C4', 'F4']), ('G', ['G2', 'G3', 'B3', 'D4']),
        ('Am', ['A2', 'A3', 'B3', 'E4'])]
for b, (_, notes) in enumerate(BARS):
    g = 0.20 if b < 2 else 0.17
    put(pad([hz(x) for x in notes[1:]], 2.5 if b < 9 else 2.0), b * 2.0, g, 0.0, 0.5)

# pulse, bass and arpeggio enter at the reveal (4.5s) and drop out for the outro
t = 4.5
while t < 17.0:
    bar = BARS[int(t // 2)][1]
    put(kick(), t, 0.42, 0.0, 0.05)
    put(pluck(hz(bar[0]), 0.5, 2), t, 0.30, 0.0, 0.1)
    t += 0.5
ARP = [1, 2, 3, 2]
t, k = 4.5, 0
while t < 17.2:
    bar = BARS[int(t // 2)][1]
    name = bar[ARP[k % 4]]
    f = hz(name) * 2
    put(pluck(f, 0.5, 4), t, 0.11, 0.35 if k % 2 else -0.35, 0.45)
    t += 0.25; k += 1

# effects, all from the A minor scale
put(swell(2.6, 120, 500, 0.75), 0.0, 0.10, 0.0, 0.4)                       # hook: low unsettled air
put(swell(0.8, 600, 2400, 0.9), 3.25, 0.07, -0.4, 0.4)                     # drawing the rectangle
for ct, f in [(4.5, 'E6'), (11.05, 'E6'), (11.2, 'E6'), (13.0, 'A5'), (13.15, 'A5')]:
    put(pip(hz(f)), ct, 0.10, 0.0, 0.5)                                    # clicks
for f, p in [('A4', -0.3), ('C5', 0.0), ('E5', 0.3)]:
    put(pluck(hz(f), 1.0, 5), 4.5, 0.20, p, 0.6)                           # fence appears
for ct, f in [(4.85, 'E5'), (5.05, 'A5')]:
    put(pluck(hz(f), 0.6, 3), ct, 0.10, 0.4, 0.6)
put(swell(1.5, 500, 3000, 0.55), 5.0, 0.085, 0.0, 0.5)                     # icons fly
PENTA = ['A5', 'C6', 'D6', 'E6', 'G6']
for i in range(18):
    put(pip(hz(PENTA[i % 5]), 0.06), 5.72 + i * 0.042, 0.028, rng.uniform(-0.7, 0.7), 0.6)
put(pluck(hz('C3'), 0.6, 2), 8.4, 0.34, -0.3, 0.2)                         # drop into the fence
put(pluck(hz('G5'), 0.7, 3), 8.4, 0.10, -0.3, 0.6)
put(pluck(hz('E6'), 0.5, 2), 8.6, 0.06, -0.3, 0.7)                         # path chip
for ct, f, p in [(11.2, 'D5', -0.5), (11.6, 'B4', 0.0), (11.8, 'G4', 0.5)]:
    put(pluck(hz(f), 0.6, 4), ct, 0.15, p, 0.5)                            # roll-ups, falling
put(swell(0.6, 400, 1800, 0.15), 13.15, 0.08, 0.0, 0.6)                    # quick hide
put(swell(0.5, 500, 2600, 0.85), 14.3, 0.07, 0.0, 0.5)                     # fences return
for ct, f, p in [(15.0, 'C6', -0.5), (15.25, 'F6', 0.0), (15.5, 'A6', 0.5),
                 (16.1, 'D6', -0.5), (16.35, 'G6', 0.0), (16.6, 'B6', 0.5)]:
    put(pluck(hz(f), 0.8, 2), ct, 0.085, p, 0.7)                           # colour changes, rising
put(swell(0.5, 300, 1500, 0.9), 17.1, 0.07, 0.0, 0.5)                      # dip into the outro
for f, p in [('A2', 0.0), ('A3', -0.2), ('E4', 0.2), ('B4', -0.4), ('C5', 0.4), ('E5', 0.0)]:
    put(pluck(hz(f), 2.4, 4), 17.6, 0.16, p, 0.6)                          # final chord

# one shared room
ir_n = int(1.8 * SR)
ir = rng.standard_normal((ir_n, 2)) * np.exp(-np.arange(ir_n) / (0.42 * SR))[:, None]
ir = sosfilt(butter(2, [250, 6000], btype='band', fs=SR, output='sos'), ir, axis=0)
ir /= np.sqrt((ir ** 2).sum(axis=0))
wet = np.stack([fftconvolve(send[:, c], ir[:, c])[:N] for c in range(2)], axis=1)
mix = dry + 0.55 * wet
mix = sosfilt(butter(2, 35, btype='high', fs=SR, output='sos'), mix, axis=0)
mix = np.tanh(mix * 1.4) / 1.4                                             # gentle glue, no hard peaks
fade = np.ones(N); k = int(0.9 * SR); fade[-k:] = np.linspace(1, 0, k) ** 2
mix *= fade[:, None]
mix *= 10 ** (-1.5 / 20) / np.abs(mix).max()
wavfile.write('audio.wav', SR, (mix * 32767).astype(np.int16))
print('peak ok, rms dBFS', round(20 * np.log10(np.sqrt((mix ** 2).mean())), 1))
