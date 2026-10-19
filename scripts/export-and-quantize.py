"""Export the upstream weights to ONNX (PB8; INT8 quantization follows in PB9).

Reads the .pth files download_models.py left in .cache/weights and writes, per model,
<out>/<name>/fp32.onnx and, where the graph converts cleanly, fp16.onnx (default out:
.cache/models, git-ignored). Every graph has dynamic batch, height and width axes, so the app
can feed it tiles of any size, and a fixed opset. Each output passes onnx.checker and is then
run in ONNX Runtime at two input sizes against the PyTorch model; a difference past the
tolerance fails the script rather than writing a model that is quietly wrong.

    .venv\\Scripts\\python scripts\\export-and-quantize.py
    .venv\\Scripts\\python scripts\\export-and-quantize.py --only realesrgan-x2plus

Requirements: scripts/requirements.txt. Exported so far: Real-ESRGAN x4plus and x2plus.
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

import numpy as np
import onnx
import onnxruntime as ort
import torch
from onnxconverter_common import float16

sys.path.insert(0, str(Path(__file__).resolve().parent))

from archs.rrdbnet import RRDBNet  # noqa: E402

REPO = Path(__file__).resolve().parent.parent
WEIGHTS = REPO / ".cache" / "weights"
OUT = REPO / ".cache" / "models"
OPSET = 17

# Largest absolute difference from PyTorch, on outputs in roughly [0, 1].
FP32_TOLERANCE = 1e-3
# FP16 is judged by PSNR against the FP32 output instead: per-pixel error is expected.
FP16_MIN_PSNR = 40.0


@dataclass(frozen=True)
class Export:
    name: str
    weights: str
    build: Callable[[], torch.nn.Module]
    # Height and width must be a multiple of this (x2plus pixel-unshuffles by 2).
    multiple: int


EXPORTS = [
    Export("realesrgan-x4plus", "RealESRGAN_x4plus.pth", lambda: RRDBNet(scale=4), 1),
    Export("realesrgan-x2plus", "RealESRGAN_x2plus.pth", lambda: RRDBNet(scale=2), 2),
]


class ExportError(Exception):
    """An export did not produce a model that matches its PyTorch source."""


def load(spec: Export, weights: Path) -> torch.nn.Module:
    path = weights / spec.weights
    if not path.is_file():
        raise ExportError(f"{spec.name}: {path} is missing; run scripts/download_models.py first")
    state = torch.load(path, map_location="cpu", weights_only=True)
    state = state.get("params_ema", state.get("params", state))
    model = spec.build()
    model.load_state_dict(state, strict=True)
    return model.eval()


def export_fp32(model: torch.nn.Module, spec: Export, target: Path) -> None:
    sample = torch.rand(1, 3, 32 * spec.multiple, 32 * spec.multiple)
    torch.onnx.export(
        model,
        (sample,),
        str(target),
        input_names=["input"],
        output_names=["output"],
        opset_version=OPSET,
        dynamo=False,
        dynamic_axes={"input": {0: "batch", 2: "height", 3: "width"}, "output": {0: "batch", 2: "out_height", 3: "out_width"}},
    )
    onnx.checker.check_model(str(target), full_check=True)


def export_fp16(fp32: Path, target: Path) -> None:
    converted = float16.convert_float_to_float16(onnx.load(str(fp32)), keep_io_types=True)
    onnx.save(converted, str(target))
    onnx.checker.check_model(str(target), full_check=True)


def run(path: Path, x: np.ndarray) -> np.ndarray:
    session = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])
    return session.run(None, {"input": x})[0]


def psnr(a: np.ndarray, b: np.ndarray) -> float:
    mse = float(np.mean((np.clip(a, 0, 1) - np.clip(b, 0, 1)) ** 2))
    return float("inf") if mse == 0 else 10 * np.log10(1 / mse)


def verify(model: torch.nn.Module, spec: Export, fp32: Path, fp16: Path | None) -> list[str]:
    """Two non-square sizes, neither the traced one, prove the axes are really dynamic."""
    lines = []
    generator = torch.Generator().manual_seed(7)
    for h, w in ((24, 40), (48, 36)):
        h, w = h * spec.multiple, w * spec.multiple
        x = torch.rand(1, 3, h, w, generator=generator)
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
            quality = psnr(run(fp16, x.numpy()), got)
            if quality < FP16_MIN_PSNR:
                raise ExportError(f"{spec.name}: fp16 PSNR {quality:.1f} dB < {FP16_MIN_PSNR} at {h}x{w}")
            line += f", fp16 PSNR {quality:.1f} dB"
        lines.append(line)
    return lines


def export(spec: Export, weights: Path, out: Path, fp16: bool = True) -> list[str]:
    model = load(spec, weights)
    folder = out / spec.name
    folder.mkdir(parents=True, exist_ok=True)
    fp32_path = folder / "fp32.onnx"
    fp16_path = folder / "fp16.onnx"
    export_fp32(model, spec, fp32_path)
    if fp16:
        export_fp16(fp32_path, fp16_path)
    return verify(model, spec, fp32_path, fp16_path if fp16 else None)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Export the upstream weights to ONNX with dynamic axes.")
    parser.add_argument("--weights", type=Path, default=WEIGHTS, help="where download_models.py put the .pth files")
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
