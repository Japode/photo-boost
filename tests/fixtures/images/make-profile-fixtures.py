"""PB44: writes the two colour-profile fixtures beside this script and prints the expected sRGB.

p3-swatch.png       8 x 8 of one colour, tagged with a Display P3 profile built here (ICC v2,
                    matrix/TRC: Display P3's D50-adapted colorants and a gamma 2.2 curve), so
                    no third-party profile and no license is involved.
broken-profile.png  the same pixels with an iCCP chunk that is not an ICC profile.

    .venv\\Scripts\\python tests\\fixtures\\images\\make-profile-fixtures.py

Needs Pillow (scripts/requirements.txt). The printed sRGB values are what ImageDecoderTests expects.
"""

from __future__ import annotations

import struct
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
P3 = (100, 180, 120)  # inside sRGB's gamut, so the conversion moves it without clipping
GAMMA = 2.2

# Colorants adapted to D50 (Bradford), as ICC matrix/TRC profiles carry them.
P3_D50 = ((0.5151, 0.2412, -0.0011), (0.2920, 0.6922, 0.0419), (0.1571, 0.0666, 0.7841))
SRGB_D50 = ((0.4361, 0.2225, 0.0139), (0.3851, 0.7169, 0.0971), (0.1431, 0.0606, 0.7141))
D50 = (0.9642, 1.0, 0.8249)


def s15f16(v: float) -> bytes:
    return struct.pack(">i", round(v * 65536))


def xyz_tag(xyz) -> bytes:
    return b"XYZ " + b"\0" * 4 + b"".join(s15f16(c) for c in xyz)


def curve_tag(gamma: float) -> bytes:
    return b"curv" + b"\0" * 4 + struct.pack(">I", 1) + struct.pack(">H", round(gamma * 256)) + b"\0\0"


def text_desc(text: str) -> bytes:
    ascii_ = text.encode("ascii") + b"\0"
    body = b"desc" + b"\0" * 4 + struct.pack(">I", len(ascii_)) + ascii_ + b"\0" * 8 + b"\0" * 3 + b"\0" * 67
    return body


def text_tag(text: str) -> bytes:
    return b"text" + b"\0" * 4 + text.encode("ascii") + b"\0"


def pad4(b: bytes) -> bytes:
    return b + b"\0" * (-len(b) % 4)


def profile() -> bytes:
    tags = [
        (b"desc", text_desc("PhotoBoost test Display P3 (gamma 2.2)")),
        (b"cprt", text_tag("Public domain test profile, PhotoBoost PB44")),
        (b"wtpt", xyz_tag(D50)),
        (b"rXYZ", xyz_tag(P3_D50[0])),
        (b"gXYZ", xyz_tag(P3_D50[1])),
        (b"bXYZ", xyz_tag(P3_D50[2])),
        (b"rTRC", curve_tag(GAMMA)),
        (b"gTRC", curve_tag(GAMMA)),
        (b"bTRC", curve_tag(GAMMA)),
    ]
    offset = 128 + 4 + 12 * len(tags)
    table, data = b"", b""
    for sig, body in tags:
        body = pad4(body)
        table += sig + struct.pack(">II", offset + len(data), len(body))
        data += body
    size = offset + len(data)
    header = struct.pack(">I", size) + b"none" + struct.pack(">I", 0x02100000) + b"mntrRGB XYZ "
    header += b"\0" * 12 + b"acsp" + b"MSFT" + b"\0" * 4 + b"\0" * 4 + b"\0" * 4 + b"\0" * 8 + b"\0" * 4
    header += b"".join(s15f16(c) for c in D50) + b"\0" * 4
    header = header + b"\0" * (128 - len(header))
    return header + struct.pack(">I", len(tags)) + table + data


def expected_srgb(rgb) -> tuple[int, int, int]:
    linear = [(c / 255) ** GAMMA for c in rgb]
    xyz = [sum(linear[i] * P3_D50[i][k] for i in range(3)) for k in range(3)]
    # Invert sRGB's colorant matrix (columns are the colorants).
    m = [[SRGB_D50[c][r] for c in range(3)] for r in range(3)]
    det = (m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0])
           + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]))
    inv = [
        [(m[1][1] * m[2][2] - m[1][2] * m[2][1]) / det, (m[0][2] * m[2][1] - m[0][1] * m[2][2]) / det, (m[0][1] * m[1][2] - m[0][2] * m[1][1]) / det],
        [(m[1][2] * m[2][0] - m[1][0] * m[2][2]) / det, (m[0][0] * m[2][2] - m[0][2] * m[2][0]) / det, (m[0][2] * m[1][0] - m[0][0] * m[1][2]) / det],
        [(m[1][0] * m[2][1] - m[1][1] * m[2][0]) / det, (m[0][1] * m[2][0] - m[0][0] * m[2][1]) / det, (m[0][0] * m[1][1] - m[0][1] * m[1][0]) / det],
    ]
    out = []
    for row in inv:
        v = max(0.0, min(1.0, sum(row[k] * xyz[k] for k in range(3))))
        v = 12.92 * v if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055
        out.append(round(v * 255))
    return tuple(out)


def main() -> None:
    swatch = Image.new("RGB", (8, 8), P3)
    swatch.save(HERE / "p3-swatch.png", icc_profile=profile())
    swatch.save(HERE / "broken-profile.png", icc_profile=b"this is not an ICC profile" * 4)
    print("p3", P3, "-> expected sRGB", expected_srgb(P3))


if __name__ == "__main__":
    main()
