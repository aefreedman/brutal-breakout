"""Offline asset source; Python standard library only. Never called by the game/build.
Run from this directory to reproduce the committed canonical mono PCM16 WAVs.
All constants describe authored media samples, not runtime user-tunable settings.
"""
import math
import struct
import wave
from pathlib import Path


def write_cue(name, frequencies, seconds, amplitude):
    samples = []
    count = round(22050 * seconds)
    for frequency in frequencies:
        for i in range(count):
            envelope = math.sin(math.pi * i / (count - 1)) ** 2
            samples.append(int(amplitude * envelope * math.sin(2 * math.pi * frequency * i / 22050)))
    with wave.open(str(Path(__file__).parent / (name + ".wav")), "wb") as output:
        output.setparams((1, 2, 22050, 0, "NONE", "not compressed"))
        output.writeframes(struct.pack("<" + "h" * len(samples), *samples))


for name, frequencies in {
    "launch": [440, 660], "catch": [660, 440], "brick": [880],
    "miss": [220, 165], "win": [523, 659, 784], "loss": [330, 247, 165],
}.items():
    write_cue(name, frequencies, .1, 6500)

# Short, quieter firing leaves room for repeated hits and higher-priority feedback.
for name, frequencies, seconds, amplitude in [
    ("shmup-fire", [1250, 900], .02, 3000),
    ("shmup-hit", [1100], .045, 4200),
    ("shmup-destroy", [660, 880, 1100], .045, 5000),
    ("shmup-damage", [220, 140], .08, 6500),
]:
    write_cue(name, frequencies, seconds, amplitude)
