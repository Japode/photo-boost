# Improvements

## Block A — Project foundation

### §PB1 Project scaffold

Mirror quickshell and pportal: PhotoBoost.slnx with src/PhotoBoost.App (WPF,
net10.0-windows, win-x64), src/PhotoBoost.Imaging (tensors, tiling, codecs),
src/PhotoBoost.Inference (ONNX Runtime, queue, sessions) and src/PhotoBoost.Render
(Vortice D3D11 display). global.json pins the SDK. Directory.Build.props: x64 only,
nullable, warnings as errors, performance analyzers on. ThemeMode="System" with WPF0001
in NoWarn. Done when dotnet build and dotnet run open an empty Fluent window with Mica.

### §PB2 Test and build gates

tests/Directory.Build.props wires xunit.v3 on Microsoft.Testing.Platform for every test
project, as quickshell does. Projects: PhotoBoost.Imaging.Tests,
PhotoBoost.Inference.Tests and PhotoBoost.Architecture.Tests (layer rules, plus the
colour rule from the design system task). run-tests.cmd runs them; UI cases run
separately through winwright.

### §PB5 ONNX Runtime package spike

In a self-contained Release publish, load Microsoft.ML.OnnxRuntime.DirectML and run a
tiny model on the DirectML and CPU providers. Then try Windows ML (Windows App SDK,
unpackaged) which picks TensorRT, OpenVINO or an NPU on its own, and the CUDA package on
an NVIDIA machine. Record size added to the installer, startup cost and which ops fall
back to CPU. DirectML is in sustained engineering, so weigh Windows ML as the long-term
path. Propose and explain any switch before making it, as the brief requires.

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
--models <dir>, --provider dml|cpu|cuda, --export <path> (write the current result and
stay open), --quality fast|high. Parsed in one place, unit tested, and ignored values
rejected with a clear message. These stand in for WW550 (drop from Explorer) and WW551
(Open/Save dialogs); once those ship, add cases that drive the real drop and dialogs.

### §PB40 GPU image display spike

Compare D3DImage fed by a shared D3D11 texture against a child HWND with a DXGI flip
swapchain, both through Vortice as in quickshell's Quickshell.Render and its host spike.
Measure pan and zoom frame time on a 50 MP image, memory, airspace issues with Fluent
overlays, and a WARP fallback. Note that winwright cannot screenshot a Mica window, so
the surface must expose what it shows (size, zoom, divider position) through automation;
WW552 asks winwright for a capture route for this surface.

## Block B — Model management

### §PB6 Verified model sources

Check each official release: xinntao/Real-ESRGAN GitHub releases (RealESRGAN_x4plus.pth,
and x2plus if 2x uses a native model), TencentARC/GFPGAN releases (GFPGANv1.4.pth),
briaai/RMBG-1.4 on Hugging Face. Record URL, size, upstream hash if published, and
license. RMBG-1.4 is non-commercial; note it in the README and consider U2-Net as a
permissive fallback.

### §PB7 Weight download script

Python, since the export step already requires it: download the verified URLs into a
cache folder, resume partial downloads, print progress and check size or hash.
Idempotent: a valid file already present is skipped. Pin dependencies in
scripts/requirements.txt.

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
colour order, recommended tile size and bytes per tile pixel, file sizes, SHA256 of each
variant, PSNR results, upstream URL and license. A C# record in PhotoBoost.Inference and
a JSON schema in scripts/ keep app and script in step; a test loads every meta.json the
script emits.

### §PB12 Model hosting and manifest

Publish the exported files (GitHub release assets or a Hugging Face repo under the
project) and a manifest.json listing each model, variant, URL, size and SHA256. The app
pins a manifest version. Check the upstream licenses allow redistribution of converted
weights before publishing.

### §PB13 First-run model download

Models live under %LOCALAPPDATA%\PhotoBoost\models\<name>\ unless --models overrides it.
HttpClient streams each file to a .part, reports progress, verifies SHA256, then
renames. The screen shows per-model bars, total size, retry and cancel, in the Fluent
card style. Later launches only re-check presence and hashes. INT8 is the default
download; FP16 for GPU and FP32 are optional extras.

## Block C — Inference and Real-ESRGAN end-to-end flow

### §PB14 Execution provider detection

Try DirectML first (any DX12 GPU: NVIDIA, AMD, Intel), then CUDA if that package ships,
then CPU, by creating a session on a tiny probe model; keep the first that works. If the
PB5 spike picks Windows ML, let it choose and report its choice. Name the adapter
through DXGI, show provider and GPU in the status bar, and accept --provider to force
one.

### §PB15 InferenceService queue

A single queue in PhotoBoost.Inference running on a background thread. Jobs carry an id,
operation and options, can be cancelled while queued or between tiles, and report
results and progress through IProgress back to the UI thread.

### §PB16 Session lifecycle

Load a session on first use of a model/variant, cache it, and release it after N idle
minutes (configurable, default 5). Switching INT8 to High quality drops the other
variant's session.

### §PB17 Preprocessing

Decode with WIC (honouring EXIF orientation, colour profiles and 16-bit), convert to
sRGB, split alpha, and produce Float32 NCHW in 0..1 RGB, plus the inverse with clamping.
Write it with spans and SIMD where it pays. Unit tests cover round trips, alpha,
grayscale and 16-bit inputs. A GPU compute-shader path can follow once tensors stay on
the GPU.

### §PB18 Tiling with overlap

Split into 256px tiles with 16px overlap (both configurable), run each, crop the overlap
from the scaled output and stitch, optionally feathering seams. Unit test: tiling then
recomposing with an identity model reproduces the original image exactly, including edge
tiles of odd sizes.

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

A Fast (INT8) / High quality toggle per operation or globally. INT8 dynamic quantization
mostly pays on the CPU; on DirectML prefer FP16. If the High quality files are not
downloaded, offer to fetch them through the model download flow.

## Block E — Face restoration and background removal

### §PB30 Face detection and alignment

Add a face detector in ONNX (RetinaFace or YuNet, as facexlib uses) to the model
pipeline of Block B, with verified URLs. Compute landmarks, warp each face to the FFHQ
512 template, and keep the inverse affine and a soft mask for pasting back.

### §PB31 Face restore pipeline

Pipelines/FaceRestore.cs: detect, align, run GFPGANv1.4 per face, paste back with the
mask, optionally upscale the background with Real-ESRGAN first. Expose a
fidelity/strength blend. CodeFormer can be added later as an alternative model behind
the same interface.

### §PB32 Background removal pipeline

Pipelines/RemoveBackground.cs: resize to the RMBG-1.4 input (1024px), run, upscale the
mask to the original size, refine edges and attach it as alpha. Export defaults to PNG
or WebP. Keep the license caveat from PB6 visible in the app's About page.

## Block F — Delivery: README, benchmarks and installers

### §PB33 README

Sections: what PhotoBoost is, dotnet build and run (SDK version, Windows 10/11 x64), how
to run download_models.py and export-and-quantize.py (Python version, requirements.txt,
CUDA optional), where models live, hardware requirements (DX12 GPU for DirectML, RAM,
VRAM per tile size), how to run tests and winwright cases, and each model's license.

### §PB34 FP32 vs INT8 benchmark

A script that runs each model and variant on fixed test images per available provider
and prints a Markdown table of file size, peak memory, time and PSNR/SSIM against FP32.
Paste the result into the README with the hardware it ran on.

### §PB35 Installer

Follow claude-tray: a self-contained single-file publish (ReadyToRun), an Inno Setup
per-user installer built by a build-installer.cmd, and optionally winget manifests. The
version lives in one place and is checked against app.manifest, as pportal does. Models
are not bundled. Smoke-test the installed app offline after models are downloaded. The
non-goal Builds for macOS, Linux, web or mobile does not apply: this is the one Windows
build.

## Block G — Phase 2: denoising

### §PB36 Denoise pipeline

Pick a denoising model with a verified source and license that exports to ONNX (e.g.
SCUNet or NAFNet), run it through the same export, quantize and validate path, and add
src/main/pipelines/denoise.ts using the shared tiling.
