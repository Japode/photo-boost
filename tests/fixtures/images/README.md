# Image fixtures

Tiny files the decoder tests read, committed because Windows' own encoders cannot write them:
WIC has no WebP encoder, and writing EXIF through it needs metadata plumbing a test should not
carry. Both were made with Pillow (`scripts/requirements.txt`):

| File | What it is |
| --- | --- |
| `rotated-exif6.jpg` | 16 x 8, left half red, right half blue, quality 100, EXIF Orientation 6 (display rotated 90° clockwise), so the upright image is 8 x 16 with red on top. |
| `alpha-lossless.webp` | 4 x 2 lossless with exact alpha: red opaque, green at alpha 128, blue opaque, white fully transparent. |

PNG cases are written by the tests themselves through WIC's PNG encoder.
