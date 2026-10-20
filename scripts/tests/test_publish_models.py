"""PB12: publish-models.py stages the FP32 set, points RMBG at BRIA, and writes the model card."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent.parent


def _load():
    spec = importlib.util.spec_from_file_location("publish_models", SCRIPTS / "publish-models.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


pm = _load()


class PublishModelsTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        self.models, self.stage, self.sources = root / "models", root / "stage", root / "model-sources.json"
        rmbg_bytes = b"bria's own export"
        for name in [*pm.HOSTED, *pm.EXTERNAL]:
            folder = self.models / name
            folder.mkdir(parents=True)
            variants = {}
            for variant in ("fp16", "fp32"):
                payload = rmbg_bytes if name == "rmbg-1.4" and variant == "fp32" else f"{name} {variant}".encode()
                (folder / f"{variant}.onnx").write_bytes(payload)
                variants[variant] = {"file": f"{variant}.onnx", "bytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest()}
            meta = {"name": name, "task": "t", "variants": variants,
                    "upstream": {"license": f"{name} license", "license_url": "https://example.org/LICENSE"}}
            (folder / "meta.json").write_text(json.dumps(meta), encoding="utf-8")
        self.rmbg_url = "https://huggingface.co/briaai/RMBG-1.4/resolve/abc/onnx/model.onnx"
        sources = {"models": [{"name": "rmbg-1.4-onnx", "url": self.rmbg_url, "sha256": hashlib.sha256(rmbg_bytes).hexdigest()}]}
        self.sources.write_text(json.dumps(sources), encoding="utf-8")
        self.original = pm.SOURCES
        pm.SOURCES = self.sources
        self.addCleanup(setattr, pm, "SOURCES", self.original)

    def staged(self):
        return pm.bm.build(self.models, self.stage, pm.resolve_base("c0ffee"), pm.VERSION, only=[*pm.HOSTED, *pm.EXTERNAL],
                           variants_wanted={pm.VARIANT}, external=pm.external_urls(self.models, self.sources))

    def test_only_fp32_is_staged_and_rmbg_is_not_rehosted(self):
        manifest = self.staged()
        staged = sorted(p.name for p in self.stage.glob("*.onnx"))
        self.assertEqual(sorted(f"{name}-fp32.onnx" for name in pm.HOSTED), staged)
        self.assertTrue((self.stage / "rmbg-1.4-meta.json").is_file())
        urls = {m["name"]: m["variants"]["fp32"]["url"] for m in manifest["models"]}
        self.assertEqual(self.rmbg_url, urls["rmbg-1.4"])
        self.assertEqual(f"https://huggingface.co/{pm.REPO_ID}/resolve/c0ffee/gfpgan-v1.4-fp32.onnx", urls["gfpgan-v1.4"])
        self.assertTrue(all(list(m["variants"]) == ["fp32"] for m in manifest["models"]))

    def test_an_external_model_whose_file_is_not_the_upstreams_is_refused(self):
        (self.models / "rmbg-1.4" / "fp32.onnx").write_bytes(b"changed")
        meta_path = self.models / "rmbg-1.4" / "meta.json"
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        meta["variants"]["fp32"]["sha256"] = hashlib.sha256(b"changed").hexdigest()
        meta_path.write_text(json.dumps(meta), encoding="utf-8")
        with self.assertRaises(pm.bm.ManifestError):
            pm.external_urls(self.models, self.sources)

    def test_the_card_names_every_license_and_where_each_file_lives(self):
        card = pm.model_card(self.staged())
        for name in [*pm.HOSTED, *pm.EXTERNAL]:
            self.assertIn(f"{name} license", card)
        self.assertIn(f"| rmbg-1.4 | [upstream]({self.rmbg_url})", card)
        self.assertIn("| gfpgan-v1.4 | this repository |", card)
        self.assertIn("non-commercial", card)


if __name__ == "__main__":
    unittest.main()
