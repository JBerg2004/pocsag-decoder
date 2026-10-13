#!/usr/bin/env python3
"""Erzeugt ein synthetisches POCSAG-Basisbandsignal (22050 Hz, s16le, mono),
wie es rtl_fm nach der FM-Demodulation liefert. Zum Testen ohne SDR-Stick:

    python3 pocsag_gen.py 1200 1234567:3:"B3 Brand" 0012345:0: > test.raw
    multimon-ng -t raw -a POCSAG1200 -f alpha test.raw
"""
import struct
import sys

SYNC = 0x7CD215D8
IDLE = 0x7A89C197
RATE = 22050


def bch(cw21: int) -> int:
    """Ergänzt 21 Datenbits um BCH(31,21)-Prüfbits und gerade Parität."""
    gen = 0x769
    reg = cw21 << 10
    for i in range(30, 9, -1):
        if reg & (1 << i):
            reg ^= gen << (i - 10)
    cw = ((cw21 << 10) | (reg & 0x3FF)) << 1
    return cw | (bin(cw).count("1") & 1)


def alpha_words(text: str):
    bits = []
    for ch in text:
        c = ord(ch) & 0x7F
        bits += [(c >> i) & 1 for i in range(7)]  # LSB zuerst
    while len(bits) % 20:
        bits.append(0)
    for i in range(0, len(bits), 20):
        v = 0
        for b in bits[i:i + 20]:
            v = (v << 1) | b
        yield bch((1 << 20) | v)


def encode(pages):
    words = []
    for ric, func, text in pages:
        frame = ric & 7
        # Platz bis zum richtigen Frame mit Idle füllen
        while len(words) % 16 != frame * 2:
            words.append(IDLE)
        words.append(bch(((ric >> 3) << 2) | func))
        words.extend(alpha_words(text) if text else [])
        words.append(IDLE)
    while len(words) % 16:
        words.append(IDLE)
    bits = [1, 0] * 288
    for b in range(0, len(words), 16):
        for w in [SYNC] + words[b:b + 16]:
            bits += [(w >> (31 - i)) & 1 for i in range(32)]
    return bits + [1, 0] * 32


def main():
    baud = int(sys.argv[1])
    pages = []
    for arg in sys.argv[2:]:
        ric, func, text = arg.split(":", 2)
        pages.append((int(ric), int(func), text))
    bits = encode(pages)
    out = bytearray()
    out += b"\0\0" * RATE  # 1 s Stille
    acc = 0.0
    for bit in bits:
        acc += RATE / baud
        n = int(acc)
        acc -= n
        # Bit 1 = untere Frequenz = negativer Wert nach FM-Demodulation
        out += struct.pack("<h", -12000 if bit else 12000) * n
    out += b"\0\0" * RATE
    sys.stdout.buffer.write(out)


if __name__ == "__main__":
    main()
