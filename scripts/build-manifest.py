"""Build the manifest the app downloads models from, and stage the files to publish (PB12).

Reads every <models>/<name>/meta.json export-and-quantize.py wrote and writes
<stage>/manifest.json plus, beside it, each file under the flat name a release asset needs:
<name>-<variant>.onnx and <name>-meta.json. Upload the whole stage folder to the location
--base-url names (a GitHub release or a Hugging Face repo); the app pins the manifest version.

    .venv\\Scripts\\python scripts\\build-manifest.py --base-url https://example.org/models/v1 --version 1

Only the manifest is built here. Publishing is the owner's: the weights' licenses decide what
may be redistributed (RMBG-1.4 is non-commercial under BRIA's terms), see model-sources.json.
Standard library only.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MODELS = REPO / ".cache" / "models"
STAGE = REPO / ".cache" / "publish"
MANIFEST_SCHEMA = 1


class ManifestError(Exception):
    """A model folder is not ready to publish."""


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as f:
        while chunk := f.read(1 << 20):
            digest.update(chunk)
    return digest.hexdigest()


def build(models: Path, stage: Path, base_url: str, version: str, copy: bool = True) -> dict:
    folders = sorted(p.parent for p in models.glob("*/meta.json"))
    if not folders:
        raise ManifestError(f"no meta.json under {models}; run scripts/export-and-quantize.py first")
    base = base_url.rstrip("/")
    stage.mkdir(parents=True, exist_ok=True)
    entries = []
    for folder in folders:
        meta_path = folder / "meta.json"
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        name = meta["name"]
        variants = {}
        for variant, described in meta["variants"].items():
            source = folder / described["file"]
            if not source.is_file():
                raise ManifestError(f"{name}: {source} is listed in meta.json but missing")
            if source.stat().st_size != described["bytes"] or sha256_of(source) != described["sha256"]:
                raise ManifestError(f"{name}: {source} differs from meta.json; re-run export-and-quantize.py")
            asset = f"{name}-{variant}.onnx"
            variants[variant] = {"url": f"{base}/{asset}", "bytes": described["bytes"], "sha256": described["sha256"]}
            if copy:
                shutil.copyfile(source, stage / asset)
        meta_asset = f"{name}-meta.json"
        if copy:
            shutil.copyfile(meta_path, stage / meta_asset)
        entries.append(
            {
                "name": name,
                "task": meta["task"],
                "meta": {"url": f"{base}/{meta_asset}", "bytes": meta_path.stat().st_size, "sha256": sha256_of(meta_path)},
                "variants": variants,
                "license": meta["upstream"]["license"],
                "license_url": meta["upstream"]["license_url"],
            }
        )
    manifest = {"schema": MANIFEST_SCHEMA, "version": version, "base_url": base, "models": entries}
    (stage / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    return manifest


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Build manifest.json and stage the model files to publish.")
    parser.add_argument("--base-url", required=True, help="where the staged files will be served from")
    parser.add_argument("--version", required=True, help="the manifest version the app pins")
    parser.add_argument("--models", type=Path, default=MODELS, help="export output (default: .cache/models)")
    parser.add_argument("--stage", type=Path, default=STAGE, help="folder to stage into (default: .cache/publish)")
    parser.add_argument("--manifest-only", action="store_true", help="write manifest.json without copying files")
    args = parser.parse_args(argv)
    try:
        manifest = build(args.models, args.stage, args.base_url, args.version, copy=not args.manifest_only)
    except ManifestError as error:
        print(f"ERROR {error}", file=sys.stderr)
        return 1
    files = sum(len(m["variants"]) + 1 for m in manifest["models"])
    print(f"manifest {args.version}: {len(manifest['models'])} models, {files} files -> {args.stage}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
