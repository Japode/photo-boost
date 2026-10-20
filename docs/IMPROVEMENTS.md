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

### §PB53 Installer minimum Windows version

Found while writing the README (PB33). build\installer.iss sets MinVersion=10.0.17763
(Windows 10 1809), written when the app ran on Microsoft.ML.OnnxRuntime.DirectML. Since
PB43 the app runs on Microsoft.Windows.AI.MachineLearning, whose own README names
Windows 10 19H1 (build 18362) as the minimum: its WinRT types activate reg-free, which
1809 lacks. So 1809 installs and then fails at the first ONNX Runtime call, for DirectML
and CPU too.

What to build: MinVersion=10.0.18362 in installer.iss, with the comment naming Windows
ML as the reason. Check whether app.manifest's supportedOS entries or a maxversiontested
line need the same floor, and add an architecture test that holds installer.iss's
MinVersion at or above the Windows ML package's minimum, so a package upgrade that
raises it is caught.

### §PB54 GFPGAN FP16 on DirectML

Found by PB34's benchmark on 2026-10-20 (tools\PhotoBoost.Bench, RTX 4060). GFPGAN
v1.4's fp16.onnx on DirectML pastes a flat brown square over each face of
Blake_Lively.jpg: 12.7 dB PSNR and SSIM 0.62 against FP32 on the same provider. On the
CPU the same file is right (76.8 dB), and so is FP32 on DirectML. Since PB51 FP16 is the
default set the installer carries, so face restore is broken on every DirectML PC.

Likely cause: an FP16 overflow in the StyleGAN2 decoder, whose modulated convolutions
square, sum and take the inverse square root of the style-scaled weights (demodulation);
the CPU provider computes those ops in FP32 internally and DirectML does not. The ORT
log also says the decoder's constant_input Tile has no CPU kernel to fold.

What to build: in export-and-quantize.py, convert GFPGAN to FP16 with the demodulation
kept in FP32 (onnxconverter_common's node_block_list or op_block_list on the ReduceSum,
Pow, Sqrt and Reciprocal nodes of each modulated conv), and add a DirectML check: PB10's
validation runs on the CPU only, which is how this shipped. If no block list holds the
quality at 40 dB on DirectML, bundle GFPGAN's fp32.onnx instead (+162 MB) as
stage-models.ps1 does for ViTMatte. Re-run the benchmark and update the README's table.

## Block C — Inference and Real-ESRGAN end-to-end flow

### §PB52 TensorRT-RTX engine reuse

PB43 measured TensorRT-RTX on the RTX 4060 with Real-ESRGAN x4: FP16 tiles three times
faster than DirectML, but 10 to 17 s of engine build at every session creation.

Landed:
- EngineCache keeps each built engine as an EP context model under
  %LOCALAPPDATA%\PhotoBoost\engines, keyed by model file, provider package, GPU, driver and ORT.
  First x4 upscale in a new process: 1.8 to 1.9 s from the cache against 8.1 s, 92 ms warm.
- EngineWarmup builds the operations' engines on the queue at startup on Windows ML.
- TensorRT-RTX 0.4 builds no engine for Real-ESRGAN x2plus, SCUNet or ViTMatte. The cache
  records that once, and those models run on DirectML on the same GPU. Before this, 2x upscale,
  denoise and refined background removal failed on --provider winml.

Left, and waiting on the owner:
- Whether auto should try TensorRT-RTX first. The owner set DirectML as the default (PB43).
  With engines cached, the first x4 upscale only matches DirectML's 1.9 s; warm tiles are
  about twice as fast, and three of the seven models fall back anyway. If yes: auto picks
  Windows ML once its engines are prepared, DirectML otherwise.
- The log and the status bar name the library's provider, so a model that fell back to
  DirectML is logged as TensorRT-RTX. Name the session's provider instead.
- An explicit optimization profile (nv_profile_min/opt/max_shapes, at the tile sizes PB41
  picks) may let the three build: SCUNet's error says the default kOPT shape breaks a reshape.

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
