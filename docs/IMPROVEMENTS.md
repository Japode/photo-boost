# Improvements

## Block A — Project foundation

### §PB38 winwright adoption

Follow winwright's adoption guide and quickshell: winwright.json at the root naming the
Debug exe and timeouts; tests/PhotoBoost.Cases with one PackageReference to Winwright
(NU1603 as error), no reference to WPF or the app; CasesRun.cs loading
cases/*.cases.json, asserting every case ran, exit 0 and nothing left running, skipping
when the desk cannot be observed. Add DefaultItemExcludes if the app project sits above
tests. Every element a case touches gets a unique automation id. Install the winwright
Claude plugin at project scope. Friction goes to winwright's roadmap (WW).

## Block B — Model management

### §PB12 Model hosting and manifest

Built: scripts/build-manifest.py reads every .cache/models/<name>/meta.json, checks each
listed file against its size and SHA-256, writes manifest.json (schema 1: version,
base_url, and per model the meta.json and each variant's URL, size and SHA-256, plus the
license) and stages flat assets named <name>-<variant>.onnx and <name>-meta.json in
.cache/publish.

Left for the owner: choose the host (GitHub release assets on the project's repository,
or a Hugging Face model repo under the project) and run the script with that --base-url.
Before uploading, settle redistribution: Real-ESRGAN is BSD-3-Clause, GFPGAN is
Apache-2.0 with non-commercial StyleGAN2 and DFDNet parts, and RMBG-1.4's BRIA license
is non-commercial and may not allow redistributing converted weights at all; U2-Net
(Apache-2.0) is the fallback listed in scripts/model-sources.json. GitHub release assets
are limited to 2 GB each, which every variant fits. The app then pins the manifest
version and URL.

### §PB13 First-run model download

Models live under %LOCALAPPDATA%\PhotoBoost\models\<name>\ unless --models overrides it
(MainWindow.Options.ModelsFolder, PB39, already resolves both). HttpClient streams each
file to a .part, reports progress, verifies SHA256, then renames. The screen shows
per-model bars, total size, retry and cancel, in the Fluent card style. Later launches
only re-check presence and hashes. INT8 is the default download; FP16 for GPU and FP32
are optional extras.

## Block C — Inference and Real-ESRGAN end-to-end flow

### §PB26 End-to-end test

cases/upscale.cases.json: a fixture stages a test image and the tiny model from the
cases fixture task into {files}, launches with --open, --models and --provider cpu,
invokes the 4x upscale, waits on the progress reading, exports with --export and checks
the file with sameBytesAs against a committed reference. Run it in the VM, never on the
operator's desktop. Also run the real models by hand on DirectML.

### §PB42 Tiny model fixture for cases

export-and-quantize.py also emits a tiny ONNX model (e.g. a nearest-neighbour 4x or a
3x3 conv) with a matching meta.json, committed under cases/fixtures/models. Fixtures
stage it into {files} and pass --models and --provider cpu, so the exported bytes are
stable and checkable with sameBytesAs. Cases waiting on it: an upscale run with tile
progress (PB24), two runs then undo and redo (PB28), Compare with the divider set
through the surface's RangeValue (PB27), and --export writing the result (PB25).

### §PB43 Windows ML providers

The PB5 spike (spike/onnx-runtime/README.md) ran Microsoft.Windows.AI.MachineLearning
2.1.74 unpackaged and self-contained, with no Windows App SDK: PREFER_GPU picked NVIDIA
TensorRT-RTX at 2.4 ms against DirectML's 5.5 to 12 ms on the same model. The brief
requires the switch to be proposed and approved first, so this waits on the owner.

Once approved: swap the DirectML package for Windows ML behind PB14's provider
abstraction, target net10.0-windows10.0.19041.0, call EnsureAndRegisterCertifiedAsync
and let SetEpSelectionPolicy(PREFER_GPU) choose, reporting the EP it picked. Three
questions decide the details. The first run took 57 s while Windows Update fetched the
providers, so it needs progress and must not block an offline start (DirectML and CPU
stay available meanwhile). TensorRT-RTX differed from CPU by 2e-3, which PB10's quality
threshold must accept. And TensorRT-RTX builds its engine at session creation (about 0.5
s on the tiny model), so PB16's idle release costs that again.

### §PB45 Keep metadata on export

ImageEncoder (PB25) writes no metadata: the pixels are already upright and in sRGB, so a
viewer needs none, but a photographer loses camera, lens, date taken, copyright and GPS.

Add EncodeOptions.MetadataFrom (the source path) and a "Keep metadata" check box on the
export card, off by default (GPS leaving with a shared photo is the reason). Through
WIC: read the source frame's IWICMetadataQueryReader and copy a curated set by query
name into the encoder frame's MetadataQueryWriter, for JPEG under /app1/ifd and its exif
and gps sub-IFDs, for PNG as an eXIf chunk where WIC supports it. Always write
Orientation = 1, since the pixels were rotated on decode, and never copy the embedded
thumbnail, which shows the original. WebP goes through SkiaSharp, which cannot write
EXIF; say so beside the check box rather than dropping it silently. Vortice passes
values as SharpGen Variants (GetMetadataByName(...).Value on read); the write side's
Variant construction is the part to prove first, in a test that round-trips a Make and a
DateTimeOriginal through a JPEG.

## Block D — Editor: comparison, history and quality modes

### §PB29 Quality mode selector

A Compact (INT8) / High quality toggle per operation or globally. Do not call INT8
"Fast": PB9 measured Real-ESRGAN x4plus at 256x256 and dynamic INT8 was slower
everywhere, 5.2 s FP32 against 10.0 s INT8 on CPU, and 352 ms against 1,080 ms on
DirectML (RTX 4060), where its output also drifted 0.14 from CPU INT8. FP16 on DirectML
ran 458 ms, also behind FP32. What INT8 buys is size: Real-ESRGAN 67 to 18 MB, RMBG 176
to 44 MB, GFPGAN 340 to 158 MB. So on DirectML prefer FP32, and present INT8 as the
small download. PB10 measured its cost on each model's test image: 29 to 33 dB PSNR
against FP32, and GFPGAN's SSIM fell to 0.87, visible on a face; FP16 stayed above 70
dB. Whether INT8 stays the default the brief names is the owner's call once PB34's table
exists. If the High quality files are not downloaded, offer to fetch them through the
model download flow.

## Block E — Face restoration and background removal

### §PB30 Face detection and alignment

Add a face detector in ONNX (RetinaFace or YuNet, as facexlib uses) to the model
pipeline of Block B, with verified URLs. Compute landmarks, warp each face to the FFHQ
512 template, and keep the inverse affine and a soft mask for pasting back.

### §PB31 Face restore pipeline

Pipelines/FaceRestore.cs: detect, align, run GFPGANv1.4 per face, paste back with the
mask, optionally upscale the background with Real-ESRGAN first. Expose a
fidelity/strength blend. CodeFormer can be added later as an alternative model behind
the same interface. GFPGAN's LICENSE puts its StyleGAN2 and DFDNet parts under
non-commercial terms (scripts/model-sources.json), so the About page states it as it
does for RMBG-1.4.

### §PB32 Background removal pipeline

Pipelines/RemoveBackground.cs: resize to the RMBG-1.4 input (1024px), run, upscale the
mask to the original size, refine edges and attach it as alpha. Export defaults to PNG
or WebP. Keep RMBG-1.4's non-commercial license (scripts/model-sources.json) visible in
the app's About page; U2-Net, listed there too, is the permissive fallback.

## Block F — Delivery: README, benchmarks and installers

### §PB33 README

Sections: what PhotoBoost is, dotnet build and run (SDK version, Windows 10/11 x64), how
to run download_models.py and export-and-quantize.py (Python version, requirements.txt,
CUDA optional), where models live, hardware requirements (DX12 GPU for DirectML, RAM,
VRAM per tile size), how to run tests and winwright cases, and each model's license.

### §PB34 FP32 vs INT8 benchmark

A script that runs each model and variant on fixed test images per available provider
and prints a Markdown table of file size, peak memory, time and PSNR/SSIM against FP32.
Paste the result into the README with the hardware it ran on. The PB5 spike's OrtSpike
--model already times a model file on CPU and DirectML from C#; PB9's first numbers are
in the PB29 design.

### §PB35 Installer

Follow claude-tray: a self-contained single-file publish (ReadyToRun), an Inno Setup
per-user installer built by a build-installer.cmd, and optionally winget manifests. The
version lives in one place and is checked against app.manifest, as pportal does. Models
are not bundled. The PB5 spike ran DirectML from a single-file exe both with natives
beside it and self-extracted; exclude DirectML.Debug.dll, the .pdb files and
onnxruntime.lib the package copies (about 14.5 MB). libSkiaSharp.dll ships too: WebP
export (PB25) needs it, MIT, to be credited beside ONNX Runtime and DirectML in the
third-party notices. Smoke-test the installed app offline after models are downloaded.
The non-goal Builds for macOS, Linux, web or mobile does not apply: this is the one
Windows build.

## Block G — Phase 2: denoising

### §PB36 Denoise pipeline

Pick a denoising model with a verified source and license that exports to ONNX (e.g.
SCUNet or NAFNet), run it through the same export, quantize and validate path, and add
src/main/pipelines/denoise.ts using the shared tiling.
