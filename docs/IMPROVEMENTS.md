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

### §PB48 Sharpening the background mask's edge

Found by PB47. The grey guided filter removed RMBG-1.4's faint specks (pixels with alpha
between 0 and 10% on example_input.jpg: 58248 to 4529) but did not sharpen the edge: the
semi-transparent band (alpha 10% to 90%) grew from 13481 to 17724 pixels. Filtering the
whole mask was worse still (a light halo, 31325 band pixels), so PB47 refines only where
BRIA's mask is unsure. Epsilon 1e-3 and 1e-4 measured the same. The cause is the guide:
the giraffes' white ears against a white sky have almost no luminance difference, so the
filter has no edge to follow and averages instead.

Measured 2026-10-20: the colour guided filter (fitted at up to 2048 px) gave 17677 band
and 4839 faint pixels, no better and 0.4 s slower, so it was dropped. At 1405 px the
mask is barely scaled, so the band is mostly RMBG's own softness. Tiling helps only far
larger photos; a matting model is a further download, which the owner decides on. Left:
running RMBG on overlapping 1024 px tiles of a large photo so the mask is made nearer
full size; or a matting model (ViTMatte, MIT) on a trimap from the mask's unsure band,
with its weights verified and exported like the others. Keep Refine edges and the
unrefined path, so the Compare view can show each against the photo.

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

## Block G — Phase 2: denoising
