# Improvements

## Block A — Project foundation

### §PB1 Project scaffold

Create the skeleton with electron-vite (or Vite plus vite-plugin-electron) so main,
preload and renderer each get their own TypeScript build. Folders: src/main,
src/main/pipelines, src/preload, src/renderer, src/shared, scripts, models
(git-ignored). Done when npm install && npm run dev opens an empty React window with hot
reload in the renderer and restart on main changes.

### §PB2 Lint and test tooling

Add ESLint (typescript-eslint, react-hooks), Prettier and Vitest with npm scripts lint,
format and test. Vitest runs in a node environment for src/main and src/shared, jsdom
for src/renderer. One smoke test per process proves the config works.

### §PB3 Renderer security baseline

BrowserWindow with contextIsolation: true, nodeIntegration: false, sandbox: true. A CSP
meta tag and a session header: default-src 'self'; img-src 'self' data: blob:; no remote
origins, no unsafe-eval in production. Block navigation and window.open. Dev mode may
relax script-src for Vite HMR only.

### §PB4 Typed IPC contract

Define channel names and payload types in src/shared/ipc.ts. The preload exposes a
narrow window.photoBoost API through contextBridge (invoke-style calls plus
subscribe/unsubscribe for progress events). The renderer imports only the types.
Validate payloads in main handlers.

### §PB5 Native module spike

Load onnxruntime-node and sharp in the main process of a dev and a packaged build on
Windows first, then macOS and Linux. Run a tiny ONNX model and a sharp resize. Record
which onnxruntime-node build exposes CUDA, DirectML and CoreML. If either fails, propose
an alternative and explain it before switching, as the brief requires.

## Block B — Model management

### §PB6 Verified model sources

Check each official release: xinntao/Real-ESRGAN GitHub releases (RealESRGAN_x4plus.pth,
and x2plus if 2x uses a native model), TencentARC/GFPGAN releases (GFPGANv1.4.pth),
briaai/RMBG-1.4 on Hugging Face. Record URL, size, upstream hash if published, and
license. RMBG-1.4 is non-commercial; note it in the README and consider U2-Net as a
permissive fallback.

### §PB7 Weight download script

A Node script (run with tsx) that downloads the verified URLs into a cache folder,
resumes partial downloads, prints progress and checks size or hash. Idempotent: an
existing valid file is skipped.

### §PB8 ONNX export

Python with torch, onnx and onnxruntime (plus basicsr/gfpgan architecture code where
needed). Export FP32 with dynamic_axes for batch, height and width and a fixed opset.
Also emit an FP16 variant where the graph converts cleanly, for the High quality option.
Run onnx.checker on every output.

### §PB9 INT8 dynamic quantization

Apply onnxruntime.quantization.quantize_dynamic with weight_type QInt8 to each FP32
model. Conv-heavy models may gain little from dynamic quantization; measure, and record
ops left in float.

### §PB10 PSNR validation

Run one fixed test image through FP32 and INT8 with onnxruntime CPU and compute PSNR
between outputs (and SSIM if cheap). Fail below a per-model threshold stored in the
script. Write the numbers to meta.json; they feed the README table.

### §PB11 Model output layout

meta.json holds: name, task, scale, input/output names, layout (NCHW), value range,
color order, recommended tile size, file sizes, SHA256 of each variant, PSNR results,
upstream URL and license. A JSON schema for it lives in src/shared so app and script
agree.

### §PB12 Model hosting and manifest

Publish the exported files (GitHub release assets or a Hugging Face repo under the
project) and a manifest.json listing each model, variant, URL, size and SHA256. The app
pins a manifest version. Check the upstream licenses allow redistribution of converted
weights before publishing.

### §PB13 First-run model download

Main process downloads into app.getPath('userData')/models/<name>/, streams progress
over IPC, verifies SHA256, and renames from .part only on success. The renderer shows
per-model bars, total size, retry and cancel. Later launches only re-check presence and
hashes. Offer INT8 by default and the High quality variants as optional extras.

## Block C — Inference and Real-ESRGAN end-to-end flow

### §PB14 Execution provider detection

Try providers in order CUDA, DirectML (Windows), CoreML (macOS), CPU by creating a
session on a tiny probe model; keep the first that succeeds. Expose the result over IPC
and show it in a status bar. Allow forcing CPU from settings for troubleshooting.

### §PB15 InferenceService queue

A single FIFO queue in src/main/InferenceService.ts. Jobs carry an id, operation and
options; they can be cancelled while queued or between tiles. Results and errors return
through the IPC contract.

### §PB16 Session lifecycle

Load a session on first use of a model/variant, cache it, and release it after N idle
minutes (configurable, default 5). Switching INT8 to High quality drops the other
variant's session.

### §PB17 Preprocessing with sharp

Decode with sharp (honouring EXIF rotation), convert to sRGB, split alpha, produce
Float32 NCHW in 0..1 RGB, and the inverse with clamping. Unit tests cover round trips,
alpha handling, grayscale and 16-bit inputs.

### §PB18 Tiling with overlap

Split into 256px tiles with 16px overlap (both configurable), run each, crop the overlap
from the scaled output and stitch, optionally feathering seams. Unit test: tiling then
recomposing with an identity model reproduces the original image exactly, including edge
tiles of odd sizes.

### §PB19 Upscale pipeline

src/main/pipelines/upscale.ts drives the tiled run of RealESRGAN_x4plus. 2x is either
the x2plus model or x4 followed by a Lanczos downscale; decide from quality and download
size. Alpha channels are upscaled separately with sharp.

### §PB20 Inference progress over IPC

The pipeline emits {jobId, tile, totalTiles, stage} events, throttled, through the IPC
contract. The renderer shows a progress bar, an ETA from tile timings and a cancel
button.

### §PB21 OOM retry

Recognize allocation failures from onnxruntime (CUDA, DirectML, CPU messages), halve the
tile size down to a floor (e.g. 64px), and retry the failed tile. Below the floor, fall
back to CPU once, then report a clear error. Log each step.

### §PB22 Inference logging

Log operation, model, variant, provider, image size, tile size, tile count, retries and
wall time to a rotating file in userData/logs (electron-log or similar), and show the
last run time in the UI.

### §PB23 Open image screen

A drop zone plus a native file dialog (through main) for PNG, JPG, WebP, TIFF. Main
reads the file and returns an id and a preview; full-resolution pixels stay in the main
process.

### §PB24 Operations panel

Side panel listing operations; upscale offers 2x/4x. Disabled while a job runs or models
are missing. Built so face restore and background removal slot in as entries later.

### §PB25 Export

Save dialog in main; sharp writes PNG, JPG (quality slider) or WebP (quality, lossless
option). Warn when exporting a transparent image to JPG and offer a background colour.
Preserve or strip metadata by choice.

### §PB26 End-to-end test

Automate with Playwright for Electron: launch, open a fixture image, run 4x upscale with
INT8 on CPU, export PNG, and assert dimensions and a PSNR floor against a reference.
Also run it by hand on the GPU providers available.

## Block D — Editor: comparison, history and quality modes

### §PB27 Before/after comparator

Overlay both images with a draggable divider, synced zoom and pan, and a toggle for side
by side. The before image is scaled to the output size so details line up.

### §PB28 History and undo/redo

Each operation produces a new image version kept in main (on disk in a temp folder for
large images). The renderer shows the list; undo and redo move a pointer, and a new
operation after undo drops the forward branch. Ctrl/Cmd+Z and Shift+Ctrl/Cmd+Z.

### §PB29 Quality mode selector

A Fast (INT8) / High quality toggle per operation or globally. If the High quality files
are not downloaded, offer to fetch them through the model download flow. On GPU
providers prefer FP16 when available.

## Block E — Face restoration and background removal

### §PB30 Face detection and alignment

Add a face detector in ONNX (RetinaFace or YuNet, as facexlib uses) to the model
pipeline of Block B, with verified URLs. Compute landmarks, warp each face to the FFHQ
512 template, and keep the inverse affine and a soft mask for pasting back.

### §PB31 Face restore pipeline

src/main/pipelines/faceRestore.ts: detect, align, run GFPGANv1.4 per face, paste back
with the mask, optionally upscale the background with Real-ESRGAN first. Expose a
fidelity/strength blend. CodeFormer can be added later as an alternative model behind
the same interface.

### §PB32 Background removal pipeline

src/main/pipelines/removeBackground.ts: resize to the RMBG-1.4 input (1024px), run,
upscale the mask to the original size, refine edges, and attach it as alpha. Export
defaults to PNG or WebP. Keep the license caveat from PB6 visible in the app's About
screen.

## Block F — Delivery: README, benchmarks and installers

### §PB33 README

Sections: what PhotoBoost is, npm install && npm run dev, how to run download-models and
export-and-quantize (Python version, pip requirements, CUDA optional), where models
live, hardware requirements per provider, licenses of each model.

### §PB34 FP32 vs INT8 benchmark

A script that runs each model and variant on fixed test images per available provider
and prints a Markdown table of file size, peak memory, time and PSNR/SSIM against FP32.
Paste the result into the README with the hardware it ran on.

### §PB35 electron-builder packaging

electron-builder targets: NSIS for Windows, dmg for macOS (arm64 and x64), AppImage and
deb for Linux. asarUnpack onnxruntime-node and sharp binaries, and ship the right
onnxruntime build per platform for DirectML/CUDA/CoreML. Models are not bundled.
Smoke-test each installer offline after models are downloaded. The non-goal Web or
mobile builds does not apply: these are desktop builds only.

## Block G — Phase 2: denoising

### §PB36 Denoise pipeline

Pick a denoising model with a verified source and license that exports to ONNX (e.g.
SCUNet or NAFNet), run it through the same export, quantize and validate path, and add
src/main/pipelines/denoise.ts using the shared tiling.
