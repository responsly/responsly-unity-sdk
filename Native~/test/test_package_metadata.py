"""Regression tests for native artifact integrity checks; no Unity or SDK required."""

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest


PACKAGE = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("verify_metadata", PACKAGE / "scripts/verify-metadata.py")
metadata = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(metadata)


class NativeIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.android = {
            "artifact": "Plugins/Android/responsly-sdk.aar",
            "sha256": self.write_artifact("Plugins/Android/responsly-sdk.aar", b"android"),
        }
        binary = "Plugins/iOS/ResponslySDK.xcframework/ios-arm64/ResponslySDK.framework/ResponslySDK"
        self.ios = {
            "artifact": "Plugins/iOS/ResponslySDK.xcframework",
            "slices": [{"id": "ios-arm64", "sha256": self.write_artifact(binary, b"ios")}],
        }
        self.write_metadata()

    def write_artifact(self, relative, value):
        destination = self.root / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(value)
        return hashlib.sha256(value).hexdigest()

    def write_metadata(self):
        for platform, value in (("android", self.android), ("ios", self.ios)):
            destination = self.root / "Native~" / platform / "build-info.json"
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(json.dumps(value))

    def errors(self):
        return metadata.native_integrity_errors(self.root)

    def test_matching_artifacts_pass(self):
        self.assertEqual([], self.errors())

    def test_changed_android_binary_is_rejected(self):
        self.write_artifact(self.android["artifact"], b"changed")
        self.assertTrue(any("SHA-256 differs" in error for error in self.errors()))

    def test_changed_ios_binary_is_rejected(self):
        self.write_artifact(self.ios["artifact"] + "/ios-arm64/ResponslySDK.framework/ResponslySDK", b"changed")
        self.assertTrue(any("SHA-256 differs" in error for error in self.errors()))

    def test_missing_binary_is_rejected(self):
        (self.root / self.android["artifact"]).unlink()
        self.assertTrue(self.errors())

    def test_missing_hash_is_rejected(self):
        del self.android["sha256"]
        self.write_metadata()
        self.assertTrue(any("malformed SHA-256" in error for error in self.errors()))

    def test_extra_framework_slice_is_rejected(self):
        self.write_artifact(self.ios["artifact"] + "/ios-extra/ResponslySDK.framework/ResponslySDK", b"extra")
        self.assertTrue(any("slices differ" in error for error in self.errors()))

    def test_duplicate_slice_is_rejected(self):
        self.ios["slices"].append(copy.deepcopy(self.ios["slices"][0]))
        self.write_metadata()
        self.assertTrue(any("duplicate slice" in error for error in self.errors()))

    def test_metadata_cannot_redirect_artifact_checks(self):
        self.android["artifact"] = "elsewhere.aar"
        self.ios["slices"][0]["id"] = "../../outside"
        self.write_metadata()
        errors = self.errors()
        self.assertTrue(any("unexpected artifact path" in error for error in errors))
        self.assertTrue(any("invalid slice identifier" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
