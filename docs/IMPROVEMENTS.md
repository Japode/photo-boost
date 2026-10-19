# Improvements

## Block A — Project foundation

### §PB37 Design system reuse

Copy the pattern, not a package (none exists): Brand.cs with the app's colours declared
once as bytes, Theme.cs and Appearance.cs from quickshell for ThemeMode and
light/dark/system, Templates.cs for controls Fluent lacks, and the GroupCard,
SectionHeader, StatValue and NavDest styles from claude-tray, moved into one
ResourceDictionary. Rules from quickshell docs/design/friendly-shell: Fluent brushes
only on themed surfaces, Segoe UI Variable, flat cards with 1px border and 8px radius,
3px accent underline, status chips that carry a word. An architecture test refuses a
colour literal outside Brand.cs.

### §PB38 winwright adoption

Follow winwright's adoption guide and quickshell: winwright.json at the root naming the
Debug exe and timeouts; tests/PhotoBoost.Cases with one PackageReference to Winwright
(NU1603 as error), no reference to WPF or the app; CasesRun.cs loading
cases/*.cases.json, asserting every case ran, exit 0 and nothing left running, skipping
when the desk cannot be observed. Add DefaultItemExcludes if the app project sits above
tests. Every element a case touches gets a unique automation id. Install the winwright
Claude plugin at project scope. Friction goes to winwright's roadmap (WW).

### §PB39 Command-line flags

Flags in the quickshell style, documented in docs/COMMAND-LINE.md: --open <path>,
--models <dir>, --provider auto|dml|cpu (parsed by ProviderDetector.TryParse from PB14
and handed to ProviderDetector.Detect in MainWindow; CUDA is not shipped), --export
<path> (write the current result and stay open), --quality compact|high (PB29 renamed
fast). Parsed in one place, unit tested, and ignored values rejected with a clear
message. These stand in for WW550 (drop from Explorer) and WW551 (Open/Save dialogs);
once those ship, add cases that drive the real drop and dialogs.

### §PB40 GPU image display spike

Compare D3DImage fed by a shared D3D11 texture against a child HWND with a DXGI flip
swapchain, both through Vortice as in quickshell's Quickshell.Render and its host spike.
Measure pan and zoom frame time on a 50 MP image, memory, airspace issues with Fluent
overlays, and a WARP fallback. Note that winwright cannot screenshot a Mica window, so
the surface must expose what it shows (size, zoom, divider position) through automation;
WW552 asks winwright for a capture route for this surface.

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

Models live under %LOCALAPPDATA%\PhotoBoost\models\<name>\ unless --models overrides it.
HttpClient streams each file to a .part, reports progress, verifies SHA256, then
renames. The screen shows per-model bars, total size, retry and cancel, in the Fluent
card style. Later launches only re-check presence and hashes. INT8 is the default
download; FP16 for GPU and FP32 are optional extras.

## Block C — Inference and Real-ESRGAN end-to-end flow

### §PB19 Upscale pipeline

PhotoBoost.Inference/Pipelines/Upscale.cs drives the tiled run of RealESRGAN_x4plus. 2x
is either the x2plus model or x4 followed by a Lanczos downscale; decide from quality
and download size. Alpha channels are upscaled separately with a plain resampler.

### §PB20 Inference progress

The pipeline reports {jobId, tile, totalTiles, stage} through IProgress, throttled. The
operations panel shows a progress bar with an automation id cases can read, an ETA from
tile timings and a cancel button.

### §PB21 OOM retry

Recognise allocation failures from ONNX Runtime (DirectML E_OUTOFMEMORY and
device-removed, CUDA and CPU messages), halve the tile size down to a floor (e.g. 64px)
and retry the failed tile. Below the floor, fall back to CPU once, then report a clear
error. Log each step.

### §PB22 Inference logging

Log operation, model, variant, provider, adapter, image size, tile size, tile count,
retries and wall time to a rotating file under %LOCALAPPDATA%\PhotoBoost\logs, and show
the last run time in the status bar.

### §PB23 Open image screen

A drop zone accepting files dragged from Explorer plus the common open dialog, for PNG,
JPG, WebP, TIFF and whatever WIC decodes (HEIC, AVIF when installed). --open <path>
reaches the same state without either, which is how winwright cases get there: it has no
verb for a drop from Explorer.

### §PB24 Operations panel

Side panel listing operations; upscale offers 2x/4x. Disabled while a job runs or models
are missing. Built so face restore and background removal slot in as entries later.

### §PB25 Export

Save dialog plus an --export <path> route for cases. WIC writes PNG and JPG (quality
slider); WebP via a library if WIC lacks an encoder. Warn when a transparent image goes
to JPG and offer a background colour. Keep or strip metadata by choice.

### §PB26 End-to-end test

cases/upscale.cases.json: a fixture stages a test image and the tiny model from the
cases fixture task into {files}, launches with --open, --models and --provider cpu,
invokes the 4x upscale, waits on the progress reading, exports with --export and checks
the file with sameBytesAs against a committed reference. Run it in the VM, never on the
operator's desktop. Also run the real models by hand on DirectML.

### §PB41 Tile size from VRAM

Read the local budget and usage with DXGI QueryVideoMemoryInfo through Vortice, multiply
by the bytes per tile pixel in meta.json, and pick the largest tile from 128 to 1024
that fits with a margin. On CPU, size from available RAM. The OOM retry stays as the
safety net. Log the choice.

### §PB42 Tiny model fixture for cases

export-and-quantize.py also emits a tiny ONNX model (e.g. a nearest-neighbour 4x or a
3x3 conv) with a matching meta.json, committed under cases/fixtures/models. Fixtures
stage it into {files} and pass --models and --provider cpu, so the exported bytes are
stable and checkable with sameBytesAs.

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

### §PB44 Colour profile decode test

ImageDecoder (PB17) converts a frame that embeds a colour profile to sRGB with WIC's
IWICColorTransform, and leaves the pixels as they are when WIC cannot transform from
that profile. No test exercises either path: the desk that built it had only the sRGB
profile, and a fixture needs an image tagged with a wide-gamut one.

Commit a tiny PNG or JPEG tagged with Display P3 or Adobe RGB (1998), made with Pillow
and an ICC file whose license allows redistribution (the ICC's own sample profiles, or
one built with ImageCms), and assert that a saturated green comes out with its red and
blue channels moved the way the sRGB conversion predicts, within JPEG tolerance. Add a
second fixture with a deliberately broken profile and assert the decode still succeeds
unconverted. Record both in tests/fixtures/images/README.md beside the existing ones.

## Block D — Editor: comparison, history and quality modes

### §PB27 Before/after comparator

Draw both versions in the GPU surface from the display spike, with a draggable divider,
synced zoom and pan and a side-by-side toggle. The before image is scaled to the output
size so details line up. The divider is a Slider-like element with an automation id, so
a case can set its range.

### §PB28 History and undo/redo

Each operation produces a new image version kept in memory, spilled to a temp folder for
large images. A list shows the history; undo and redo move a pointer, and a new
operation after undo drops the forward branch. Ctrl+Z and Ctrl+Y / Ctrl+Shift+Z.

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
onnxruntime.lib the package copies (about 14.5 MB). Smoke-test the installed app offline
after models are downloaded. The non-goal Builds for macOS, Linux, web or mobile does
not apply: this is the one Windows build.

## Block G — Phase 2: denoising

### §PB36 Denoise pipeline

Pick a denoising model with a verified source and license that exports to ONNX (e.g.
SCUNet or NAFNet), run it through the same export, quantize and validate path, and add
src/main/pipelines/denoise.ts using the shared tiling.
