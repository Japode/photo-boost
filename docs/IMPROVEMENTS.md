# Improvements

## Block A — Project foundation

## Block B — Model management

### §PB13 Optional FP32 download

Rescoped by the owner's decision of 2026-10-20: the installer carries the FP16 set
(PB51), so nothing has to be downloaded for the app to work, and this is the button that
fetches the optional FP32 set from PB12's manifest, about 690 MB: pin the manifest_url
in scripts/published-models.json (alegauss/photo-boost-models at a resolve/<commit> URL,
with RMBG-1.4 from BRIA's own repository).

A "Download high-quality models" action, in the About panel or beside the quality choice
(PB29), fetches into %LOCALAPPDATA%\PhotoBoost\models\<name>\ (MainWindow.Options
.ModelsFolder, PB39). HttpClient streams each file to a .part, reports progress,
verifies SHA-256 against the manifest, then renames; cancel and retry are per file, and
a file that fails its hash is deleted, never loaded. When the app runs on the CPU it
suggests the download, because FP16 there takes about twice FP32's time (PB51's
measurements). Later launches only re-check presence and hashes.

## Block C — Inference and Real-ESRGAN end-to-end flow

## Block D — Editor: comparison, history and quality modes

### §PB29 Quality mode selector

Rescoped by the owner's decision of 2026-10-20: the choice is between the FP16 set the
installer carries (Standard, the default) and the FP32 set PB13 downloads (High
quality). INT8 is not shipped. PB9 measured it slower than FP32 everywhere (Real-ESRGAN
x4plus 10.0 against 5.2 s on CPU, 1,080 against 352 ms on DirectML), and PB10 measured
its cost at 29 to 33 dB against FP32, with GFPGAN's SSIM at 0.87, visible on a face;
FP16 stays above 80 dB. This departs from the brief's INT8/High pair, by the owner's
choice of FP16.

The toggle is global, in the operations panel. High quality is enabled only once the
FP32 files are on disk and offers PB13's download otherwise. On DirectML the two run at
the same speed and FP32 is a fidelity choice; on the CPU FP32 is also about twice as
fast (PB51's measurements), so the panel says so. --quality compact|high maps to
Standard and High. PB34's table compares the two sets.

## Block E — Face restoration and background removal

## Block F — Delivery: README, benchmarks and installers

## Block G — Phase 2: denoising
