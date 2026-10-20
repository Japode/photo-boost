"""PB7: download_models.py against a local HTTP server - resume, verification, idempotence."""

from __future__ import annotations

import hashlib
import http.server
import io
import json
import sys
import tempfile
import threading
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import download_models as dm  # noqa: E402

PAYLOAD = bytes(range(256)) * 4096  # 1 MiB, several chunks


class _Handler(http.server.BaseHTTPRequestHandler):
    honour_range = True
    requests: list[str | None] = []

    def do_GET(self):  # noqa: N802 - the stdlib's name
        type(self).requests.append(self.headers.get("Range"))
        body = PAYLOAD
        status = 200
        ranged = self.headers.get("Range")
        if ranged and type(self).honour_range:
            start = int(ranged.removeprefix("bytes=").rstrip("-"))
            body = PAYLOAD[start:]
            status = 206
        self.send_response(status)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


class DownloadModelsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), _Handler)
        threading.Thread(target=cls.server.serve_forever, daemon=True).start()
        cls.url = f"http://127.0.0.1:{cls.server.server_port}/model.pth"

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()

    def setUp(self):
        _Handler.honour_range = True
        _Handler.requests = []
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.dest = Path(temporary.name)
        self.source = dm.Source("tiny", "model.pth", self.url, len(PAYLOAD), hashlib.sha256(PAYLOAD).hexdigest(), False)

    def fetch(self):
        return dm.fetch(self.source, self.dest, out=io.StringIO())

    def test_downloads_and_verifies(self):
        self.assertEqual("downloaded", self.fetch())
        self.assertEqual(PAYLOAD, (self.dest / "model.pth").read_bytes())
        self.assertFalse((self.dest / "model.pth.part").exists())

    def test_second_run_skips_a_valid_file(self):
        self.fetch()
        _Handler.requests = []
        self.assertEqual("present", self.fetch())
        self.assertEqual([], _Handler.requests)

    def test_resumes_a_partial_download_with_range(self):
        (self.dest / "model.pth.part").write_bytes(PAYLOAD[:300_000])
        self.fetch()
        self.assertEqual(["bytes=300000-"], _Handler.requests)
        self.assertEqual(PAYLOAD, (self.dest / "model.pth").read_bytes())

    def test_restarts_when_the_server_ignores_range(self):
        _Handler.honour_range = False
        (self.dest / "model.pth.part").write_bytes(PAYLOAD[:300_000])
        self.fetch()
        self.assertEqual(PAYLOAD, (self.dest / "model.pth").read_bytes())

    def test_hash_mismatch_fails_and_deletes_the_partial(self):
        wrong = dm.Source("tiny", "model.pth", self.url, len(PAYLOAD), "0" * 64, False)
        out = io.StringIO()
        with self.assertRaises(dm.DownloadError):
            dm.fetch(wrong, self.dest, out=out)
        self.assertFalse((self.dest / "model.pth").exists())
        self.assertFalse((self.dest / "model.pth.part").exists())

    def test_corrupt_existing_file_is_replaced(self):
        (self.dest / "model.pth").write_bytes(b"x" * len(PAYLOAD))
        self.assertEqual("downloaded", self.fetch())
        self.assertEqual(PAYLOAD, (self.dest / "model.pth").read_bytes())


class SelectionTests(unittest.TestCase):
    def setUp(self):
        self.sources = dm.load_sources()

    def test_default_skips_optional_models(self):
        names = [s.name for s in dm.select(self.sources, [], [])]
        self.assertIn("realesrgan-x4plus", names)
        self.assertNotIn("u2net", names)

    def test_include_adds_an_optional_model(self):
        self.assertIn("u2net", [s.name for s in dm.select(self.sources, [], ["u2net"])])

    def test_test_images_are_downloaded_by_default_into_their_folder(self):
        # PB10: the validation images come with the weights, under test-images/.
        images = [s for s in dm.select(self.sources, [], []) if s.name.startswith("image-")]
        # One per model, and PB30's whole photo with faces for the detector.
        self.assertEqual(4, len(images))
        self.assertTrue(all(s.file.startswith("test-images/") for s in images))

    def test_unknown_name_is_refused(self):
        with self.assertRaises(dm.DownloadError):
            dm.select(self.sources, ["nope"], [])

    def test_every_recorded_source_is_complete(self):
        data = json.loads(dm.SOURCES.read_text(encoding="utf-8"))
        for model in data["models"] + data["test_images"]:
            with self.subTest(model["name"]):
                self.assertRegex(model["sha256"], r"^[0-9a-f]{64}$")
                self.assertTrue(model["url"].startswith("https://"))
                self.assertGreater(model["size"], 0)
                self.assertTrue(model["license"])


if __name__ == "__main__":
    unittest.main()
