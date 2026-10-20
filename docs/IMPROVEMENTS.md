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

### §PB12 FP32 models on Hugging Face

Built: scripts/build-manifest.py reads every .cache/models/<name>/meta.json, checks each
listed file against its size and SHA-256, writes manifest.json (schema 1: version,
base_url, and per model the meta.json and each variant's URL, size and SHA-256, plus the
license) and stages flat assets named <name>-<variant>.onnx and <name>-meta.json in
.cache/publish.

Decided by the owner on 2026-10-20: the FP16 set ships inside the installer (PB51), and
only FP32 is hosted, as an optional download, on Hugging Face rather than GitHub. All
models may be redistributed with their license stated. A public model repository under
the owner's account carries Real-ESRGAN x2plus and x4plus, GFPGAN v1.4 and SCUNet FP32
with a model card naming each license (GFPGAN's non-commercial parts). RMBG-1.4 is not
re-hosted: our fp32.onnx is BRIA's own export, so the manifest points at briaai/RMBG-1.4
at the pinned commit. Every URL is a resolve/<commit>/ URL, so a later upload cannot
change a file the app already verifies by SHA-256.

Left for the owner: create the Hugging Face account or organization and run `hf auth
login` with a write token on this machine; the upload, the model card and the manifest
then follow.

### §PB13 Optional FP32 download

Rescoped by the owner's decision of 2026-10-20: the installer carries the FP16 set
(PB51), so nothing has to be downloaded for the app to work, and this is the button that
fetches the optional FP32 set from PB12's manifest (Hugging Face, with RMBG-1.4 from
BRIA's own repository), about 690 MB.

A "Download high-quality models" action, in the About panel or beside the quality choice
(PB29), fetches into %LOCALAPPDATA%\PhotoBoost\models\<name>\ (MainWindow.Options
.ModelsFolder, PB39). HttpClient streams each file to a .part, reports progress,
verifies SHA-256 against the manifest, then renames; cancel and retry are per file, and
a file that fails its hash is deleted, never loaded. When the app runs on the CPU it
suggests the download, because FP16 there takes about twice FP32's time (PB51's
measurements). Later launches only re-check presence and hashes.

### §PB51 Models inside the installer

Decided by the owner on 2026-10-20: the installer carries the models, so a fresh install
works offline, and FP16 is the set it carries. FP32 becomes an optional download from
Hugging Face (PB12, PB13); nothing loose is stored on GitHub, only the installer asset.

Why FP16: half of FP32's size (about 347 MB with YuNet, against 690 MB), 82 to 85 dB
PSNR against FP32 (PB10), and as fast on DirectML (SCUNet 132 against 126 ms per 256 px
tile). INT8 was slower than FP32 on the GPU (221 ms) and lost 29 to 33 dB, so it is not
shipped. The cost is the CPU: measured 2026-10-20, FP16 runs about twice FP32's time
there (Real-ESRGAN x4 2404 against 1141 ms at 128 px, GFPGAN 1864 against 981 ms; RMBG
the exception, 539 against 590 ms), which is what the FP32 download answers.

Work: build-installer.cmd copies each model's fp16.onnx and meta.json (and YuNet's
fp32.onnx) from .cache/models into {app}\models, failing when one is missing; the app
looks there before %LOCALAPPDATA%\PhotoBoost\models, which stays where downloads go; the
default variant becomes FP16 (Variant in MainWindow.Operations.cs); the About panel and
the cases keep reading what is on disk. The installer's size and a run of each operation
from the installed folder are the check.

## Block C — Inference and Real-ESRGAN end-to-end flow

### §PB52 TensorRT-RTX engine reuse

PB43 measured TensorRT-RTX on the RTX 4060 desk with Real-ESRGAN x4 on 0014.jpg: a tile
in 112 ms against DirectML's 324 ms at FP16 (252 against 232 ms at FP32), but session
creation builds the engine for 10 to 17 s, so the first upscale took 16 to 20 s against
DirectML's 3 to 6 s. PB16 releases a session idle for 5 minutes, which pays the build
again. That is why auto stays on DirectML and Windows ML is behind --provider winml
(src/PhotoBoost.Inference/WindowsMLProviders.cs holds the numbers).

The runtime cache (nv_runtime_cache_path) was tried and is not the answer: it cut a new
tile shape from 3.7 s to 0.8 s and left session creation at 10 to 17 s.

What to build: ONNX Runtime's EP context model. Create the session once with
ep.context_enable=1 and ep.context_file_path under %LOCALAPPDATA%\PhotoBoost\engines,
keyed by model, variant, GPU (DXGI LUID or device id), driver version and ORT version,
and load that _ctx.onnx afterwards instead of the plain model. A key that no longer
matches is rebuilt, never trusted. Measure session creation from the context model; if
it falls under DirectML's, auto tries TensorRT-RTX first (ProviderDetector), with
DirectML still the fallback. The first build should happen in the background after the
first launch, not inside a user's first upscale.

Open question: whether the tiles' fixed side (PB41 picks 128 to 1024) lets an explicit
optimization profile (nv_profile_min/opt/max_shapes) shorten the build; the EP warns
that it assumed a fully dynamic profile.

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

### §PB48 Sharpening the background mask's edge

Found by PB47. The grey guided filter cut RMBG-1.4's faint specks but widened the
semi-transparent band (alpha 10% to 90%) on example_input.jpg from 13481 to 17724
pixels: white ears on a white sky give a luminance guide no edge to follow.

Landed: ViTMatte small (Apache-2.0, owner-approved) is downloaded and exported by the
scripts: fp32 and int8 (43.8 dB against fp32, floor 41), input 1 x 4 x H x W, H and W
multiples of 32, RGB in [-1, 1] then the trimap (0, 0.5, 1); out alpha in [0, 1]. FP16
is dropped: the converter leaves shape casts feeding FP16 ops. A Python prototype on the
same photo narrowed the band about 12% (9081 to 7599 pixels, trimap floor 0.02, unknown
grown 8 px).

Left, in C#: RemoveBackground with Refine edges on runs ViTMatte where its model is on
disk, else the PB47 guided filter. The trimap is RMBG's stretched mask: unknown where it
is between the floor and 1 minus it, grown by about 8 px at 1405 px (scale it with the
photo), the rest thresholded at a half; trimap_from_mask in export-and-quantize.py is
the reference. Run it in tiles (Tiler.Plan, multiple 32, 64 px context), skipping tiles
with no unknown pixel: the four global-attention blocks grow with the token count
squared, so a 12 MP photo cannot run whole. Keep only unknown pixels from the model.
Measure the band on example_input.jpg against RMBG's own in RemoveBackgroundTests, and
keep the unrefined path for the Compare view.

## Block F — Delivery: README, benchmarks and installers

### §PB33 README

Sections: what PhotoBoost is, dotnet build and run (SDK version, Windows 10/11 x64), how
to run download_models.py and export-and-quantize.py (Python version, requirements.txt,
CUDA optional), where models live, hardware requirements (DX12 GPU for DirectML, RAM,
VRAM per tile size), how to run tests and winwright cases, and each model's license.

### §PB34 FP16 vs FP32 benchmark

A script that runs each model and variant on fixed test images per available provider
and prints a Markdown table of file size, peak memory, time and PSNR/SSIM against FP32.
Paste the result into the README with the hardware it ran on. The PB5 spike's OrtSpike
--model already times a model file on CPU and DirectML from C#; PB9's first numbers are
in the PB29 design, and PB51 has the first FP16 against FP32 times on CPU and DirectML.
The table covers the two shipped sets (FP16 in the installer, FP32 downloaded); INT8 is
no longer shipped.

## Block G — Phase 2: denoising
