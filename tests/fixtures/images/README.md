# Image fixtures

Tiny files the decoder tests read, committed because Windows' own encoders cannot write them:
WIC has no WebP encoder, and writing EXIF through it needs metadata plumbing a test should not
carry. Both were made with Pillow (`scripts/requirements.txt`):

| File | What it is |
| --- | --- |
| `rotated-exif6.jpg` | 16 x 8, left half red, right half blue, quality 100, EXIF Orientation 6 (display rotated 90° clockwise), so the upright image is 8 x 16 with red on top. |
| `alpha-lossless.webp` | 4 x 2 lossless with exact alpha: red opaque, green at alpha 128, blue opaque, white fully transparent. |

| `p3-swatch.png` | 8 x 8 of P3 (100, 180, 120), tagged with a Display P3 profile (gamma 2.2) built by `make-profile-fixtures.py`, so no third-party profile is redistributed. Decoded to sRGB it is (64, 184, 115), by the colorant matrices and by LittleCMS. |
| `broken-profile.png` | The same pixels with an iCCP chunk that is not a profile; it must decode unconverted. |

The two profile fixtures are remade with `.venv\Scripts\python tests\fixtures\images\make-profile-fixtures.py`,
which prints the expected sRGB. Other PNG cases are written by the tests themselves through WIC's PNG encoder.
