"""Export the upstream weights to ONNX (PB8; INT8 quantization follows in PB9).

Reads what download_models.py left in .cache/weights and writes, per model,
<out>/<name>/fp32.onnx and, where the graph converts cleanly, fp16.onnx (default out:
.cache/models, git-ignored). Each output passes onnx.checker and is then run in ONNX Runtime
against the PyTorch model; an FP32 difference past the tolerance fails the script rather than
writing a model that is quietly wrong. An FP16 variant that loses too much is dropped, and said.

    Real-ESRGAN x4plus, x2plus  dynamic batch, height and width; input RGB in [0, 1]
    GFPGAN v1.4                 1 x 3 x 512 x 512 aligned face; RGB in [-1, 1], out the same
    RMBG-1.4                    BRIA's own ONNX export, adopted once it matches PyTorch;
                                batch x 3 x 1024 x 1024, pixels / 255 - 0.5; out the mask

    .venv\\Scripts\\python scripts\\export-and-quantize.py
    .venv\\Scripts\\python scripts\\export-and-quantize.py --only gfpgan-v1.4

Requirements: scripts/requirements.txt.
"""

from __future__ import annotations

import argparse
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

import numpy as np
import onnx
import onnxruntime as ort
import torch
from onnxconverter_common import float16
from torch import nn

sys.path.insert(0, str(Path(__file__).resolve().parent))

from archs.gfpganv1_clean_arch import GFPGANv1Clean  # noqa: E402
from archs.rrdbnet import RRDBNet  # noqa: E402

REPO = Path(__file__).resolve().parent.parent
WEIGHTS = REPO / ".cache" / "weights"
OUT = REPO / ".cache" / "models"
OPSET = 17

# Largest absolute difference from PyTorch, on outputs spanning a range of about 1 or 2.
FP32_TOLERANCE = 1e-3
# FP16 is judged by PSNR against the FP32 output instead: per-pixel error is expected.
FP16_MIN_PSNR = 40.0


@dataclass(frozen=True)
class Export:
    name: str
    weights: str
    # Builds the module from the weights folder; the module returns the one output the app reads.
    build: Callable[[Path], nn.Module]
    # Height and width must be a multiple of this (x2plus pixel-unshuffles by 2).
    multiple: int = 1
    # A fixed square input, batch 1, instead of dynamic axes.
    size: int | None = None
    # The range of the input the model expects, and of what it returns.
    input_range: tuple[float, float] = (0.0, 1.0)
    output_range: tuple[float, float] = (0.0, 1.0)
    # An upstream ONNX file (in the weights folder) adopted as fp32.onnx instead of exporting.
    upstream_onnx: str | None = None


class ExportError(Exception):
    """An export did not produce a model that matches its PyTorch source."""


class FirstOutput(nn.Module):
    """Calls the wrapped network with fixed keyword arguments and keeps one output."""

    def __init__(self, net: nn.Module, pick: Callable[[object], torch.Tensor], **kwargs) -> None:
        super().__init__()
        self.net = net
        self.pick = pick
        self.kwargs = kwargs

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.pick(self.net(x, **self.kwargs))


def state_dict(path: Path) -> dict:
    if not path.is_file():
        raise ExportError(f"{path} is missing; run scripts/download_models.py first")
    state = torch.load(path, map_location="cpu", weights_only=True)
    return state.get("params_ema", state.get("params", state))


def build_rrdbnet(scale: int, weights: str) -> Callable[[Path], nn.Module]:
    def build(folder: Path) -> nn.Module:
        model = RRDBNet(scale=scale)
        model.load_state_dict(state_dict(folder / weights), strict=True)
        return model

    return build


def build_gfpgan(folder: Path) -> nn.Module:
    # GFPGANv1.4's published configuration (gfpgan/utils.py, arch "clean").
    net = GFPGANv1Clean(
        out_size=512,
        num_style_feat=512,
        channel_multiplier=2,
        decoder_load_path=None,
        fix_decoder=False,
        num_mlp=8,
        input_is_latent=True,
        different_w=True,
        narrow=1,
        sft_half=True,
    )
    net.load_state_dict(state_dict(folder / "GFPGANv1.4.pth"), strict=True)
    # The stored noise buffers instead of fresh noise per call, so the graph is deterministic.
    return FirstOutput(net, lambda out: out[0], return_rgb=False, randomize_noise=False)


def load_briarmbg(path: Path) -> type[nn.Module]:
    """BRIA's briarmbg.py with its transformers base class and config replaced by plain ones."""
    if not path.is_file():
        raise ExportError(f"{path} is missing; run scripts/download_models.py first")
    source = path.read_text(encoding="utf-8")
    for line in ("from transformers import PreTrainedModel", "from .MyConfig import RMBGConfig"):
        if line not in source:
            raise ExportError(f"{path} no longer contains '{line}'; the shim needs updating")
        source = source.replace(line, "")

    class RMBGConfig:
        in_ch = 3
        out_ch = 1

    class PreTrainedModel(nn.Module):
        def __init__(self, config) -> None:
            super().__init__()
            self.config = config

    namespace = {"__name__": "briarmbg", "PreTrainedModel": PreTrainedModel, "RMBGConfig": RMBGConfig}
    exec(compile(source, str(path), "exec"), namespace)  # noqa: S102 - pinned by sha256 in model-sources.json
    return namespace["BriaRMBG"]


def build_rmbg(folder: Path) -> nn.Module:
    net = load_briarmbg(folder / "briarmbg.py")()
    net.load_state_dict(state_dict(folder / "RMBG-1.4.pth"), strict=True)
    return FirstOutput(net, lambda out: out[0][0])


EXPORTS = [
    Export("realesrgan-x4plus", "RealESRGAN_x4plus.pth", build_rrdbnet(4, "RealESRGAN_x4plus.pth")),
    Export("realesrgan-x2plus", "RealESRGAN_x2plus.pth", build_rrdbnet(2, "RealESRGAN_x2plus.pth"), multiple=2),
    Export(
        "gfpgan-v1.4",
        "GFPGANv1.4.pth",
        build_gfpgan,
        size=512,
        input_range=(-1.0, 1.0),
        output_range=(-1.0, 1.0),
    ),
    Export(
        "rmbg-1.4",
        "RMBG-1.4.pth",
        build_rmbg,
        size=1024,
        input_range=(-0.5, 0.5),
        upstream_onnx="RMBG-1.4.onnx",
    ),
]


def load(spec: Export, weights: Path) -> nn.Module:
    try:
        return spec.build(weights).eval()
    except ExportError as error:
        raise ExportError(f"{spec.name}: {error}") from error


def export_fp32(model: nn.Module, spec: Export, target: Path) -> None:
    side = spec.size or 32 * spec.multiple
    sample = torch.rand(1, 3, side, side)
    axes = None
    if spec.size is None:
        axes = {"input": {0: "batch", 2: "height", 3: "width"}, "output": {0: "batch", 2: "out_height", 3: "out_width"}}
    torch.onnx.export(
        model,
        (sample,),
        str(target),
        input_names=["input"],
        output_names=["output"],
        opset_version=OPSET,
        dynamo=False,
        dynamic_axes=axes,
    )
    onnx.checker.check_model(str(target), full_check=True)


def adopt_fp32(spec: Export, weights: Path, target: Path) -> None:
    source = weights / spec.upstream_onnx
    if not source.is_file():
        raise ExportError(f"{spec.name}: {source} is missing; run scripts/download_models.py first")
    shutil.copyfile(source, target)
    onnx.checker.check_model(str(target), full_check=True)


def export_fp16(fp32: Path, target: Path) -> None:
    converted = float16.convert_float_to_float16(onnx.load(str(fp32)), keep_io_types=True)
    onnx.save(converted, str(target))
    onnx.checker.check_model(str(target), full_check=True)


def run(path: Path, x: np.ndarray) -> np.ndarray:
    session = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])
    name = session.get_inputs()[0].name
    return session.run(None, {name: x})[0]


def psnr(a: np.ndarray, b: np.ndarray, value_range: tuple[float, float] = (0.0, 1.0)) -> float:
    low, high = value_range
    mse = float(np.mean((np.clip(a, low, high) - np.clip(b, low, high)) ** 2))
    return float("inf") if mse == 0 else 10 * np.log10((high - low) ** 2 / mse)


def sizes(spec: Export) -> list[tuple[int, int]]:
    """Two non-square sizes, neither the traced one, prove dynamic axes; a fixed model gets two inputs."""
    if spec.size is not None:
        return [(spec.size, spec.size)] * 2
    return [(24 * spec.multiple, 40 * spec.multiple), (48 * spec.multiple, 36 * spec.multiple)]


def verify(model: nn.Module, spec: Export, fp32: Path, fp16: Path | None) -> tuple[list[str], bool]:
    """Returns the report lines and whether the FP16 file, if any, is good enough to keep."""
    lines = []
    fp16_ok = fp16 is not None
    generator = torch.Generator().manual_seed(7)
    low, high = spec.input_range
    for h, w in sizes(spec):
        x = torch.rand(1, 3, h, w, generator=generator) * (high - low) + low
        with torch.no_grad():
            expected = model(x).numpy()
        got = run(fp32, x.numpy())
        if got.shape != expected.shape:
            raise ExportError(f"{spec.name}: fp32 shape {got.shape} != torch {expected.shape} at {h}x{w}")
        diff = float(np.max(np.abs(got - expected)))
        if diff > FP32_TOLERANCE:
            raise ExportError(f"{spec.name}: fp32 differs from torch by {diff:.2e} at {h}x{w}")
        line = f"{h}x{w} -> {got.shape[2]}x{got.shape[3]}: fp32 max diff {diff:.2e}"
        if fp16 is not None:
            quality = psnr(run(fp16, x.numpy()), got, spec.output_range)
            line += f", fp16 PSNR {quality:.1f} dB"
            fp16_ok = fp16_ok and quality >= FP16_MIN_PSNR
        lines.append(line)
    return lines, fp16_ok


def export(spec: Export, weights: Path, out: Path, fp16: bool = True) -> list[str]:
    model = load(spec, weights)
    folder = out / spec.name
    folder.mkdir(parents=True, exist_ok=True)
    fp32_path = folder / "fp32.onnx"
    fp16_path = folder / "fp16.onnx"
    if spec.upstream_onnx:
        adopt_fp32(spec, weights, fp32_path)
    else:
        export_fp32(model, spec, fp32_path)
    if fp16:
        export_fp16(fp32_path, fp16_path)
    lines, fp16_ok = verify(model, spec, fp32_path, fp16_path if fp16 else None)
    if fp16 and not fp16_ok:
        fp16_path.unlink()
        lines.append(f"fp16 dropped: below {FP16_MIN_PSNR} dB, so only fp32 is written")
    return lines


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Export the upstream weights to ONNX.")
    parser.add_argument("--weights", type=Path, default=WEIGHTS, help="where download_models.py put the files")
    parser.add_argument("--out", type=Path, default=OUT, help="output folder (default: .cache/models)")
    parser.add_argument("--only", action="append", default=[], metavar="NAME", help="export just this model; repeatable")
    parser.add_argument("--no-fp16", action="store_true", help="skip the FP16 variant")
    args = parser.parse_args(argv)

    known = {e.name for e in EXPORTS}
    unknown = [n for n in args.only if n not in known]
    if unknown:
        print(f"ERROR unknown model(s): {', '.join(unknown)}; known: {', '.join(sorted(known))}", file=sys.stderr)
        return 2

    failed = []
    for spec in EXPORTS:
        if args.only and spec.name not in args.only:
            continue
        try:
            for line in export(spec, args.weights, args.out, fp16=not args.no_fp16):
                print(f"{spec.name}: {line}")
            print(f"{spec.name}: exported to {args.out / spec.name}")
        except ExportError as error:
            print(f"ERROR {error}", file=sys.stderr)
            failed.append(spec.name)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
