"""Ensure native working snapshots capture fixes without changing their source repository."""

import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


PACKAGE = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("snapshot_native", PACKAGE / "scripts/snapshot-native.py")
snapshot_native = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(snapshot_native)


class NativeSnapshotTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.repository = self.root / "repository"
        self.repository.mkdir()
        self.git("init", "-q")
        (self.repository / ".gitignore").write_text("ignored/\n")
        (self.repository / "source.txt").write_text("committed")
        self.git("add", ".")
        self.git("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "fixture")
        self.commit = self.git("rev-parse", "HEAD").strip()

    def git(self, *arguments):
        return subprocess.check_output(["git", "-C", str(self.repository), *arguments], text=True)

    def export(self, name, working=False):
        destination = self.root / name
        digest = snapshot_native.snapshot(self.repository, self.commit, destination, working)
        return destination, digest

    def test_commit_export_uses_recorded_commit_when_head_moves(self):
        (self.repository / "source.txt").write_text("new commit")
        self.git("add", ".")
        self.git("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "later")
        destination, _ = self.export("snapshot")
        self.assertEqual("committed", (destination / "source.txt").read_text())

    def test_working_export_includes_edits_and_new_files_excludes_build_output(self):
        (self.repository / "source.txt").write_text("fix")
        (self.repository / "new.txt").write_text("helper")
        (self.repository / "ignored").mkdir()
        (self.repository / "ignored/output").write_text("build")
        before = self.git("status", "--porcelain")
        destination, digest = self.export("snapshot", working=True)
        self.assertEqual("fix", (destination / "source.txt").read_text())
        self.assertEqual("helper", (destination / "new.txt").read_text())
        self.assertFalse((destination / "ignored").exists())
        self.assertEqual(before, self.git("status", "--porcelain"))
        self.assertEqual(digest, snapshot_native.source_digest(destination))

    def test_working_export_respects_deleted_tracked_files(self):
        (self.repository / "source.txt").unlink()
        destination, _ = self.export("snapshot", working=True)
        self.assertFalse((destination / "source.txt").exists())

    def test_digest_is_repeatable_and_detects_edits(self):
        _, first = self.export("first", working=True)
        _, second = self.export("second", working=True)
        (self.repository / "source.txt").write_text("fix")
        _, changed = self.export("changed", working=True)
        self.assertEqual(first, second)
        self.assertNotEqual(first, changed)

    def test_existing_destination_is_preserved(self):
        self.export("snapshot")
        with self.assertRaises(FileExistsError):
            self.export("snapshot", working=True)
        self.assertEqual("committed", (self.root / "snapshot/source.txt").read_text())

    def test_build_preserves_existing_scratch_parent_on_failure(self):
        parent = self.root / "existing"
        parent.mkdir()
        (parent / "keep.txt").write_text("user data")
        android_home = self.root / "android-sdk"
        android_home.mkdir()
        environment = dict(os.environ, RESPONSLY_ANDROID_SDK=str(self.repository),
                           RESPONSLY_BUILD_DIR="existing", RESPONSLY_MIN_FREE_GB="0",
                           ANDROID_HOME=str(android_home))
        result = subprocess.run(["bash", str(PACKAGE / "scripts/build-native.sh"), "android"],
                                cwd=self.root, env=environment, capture_output=True, text=True)
        # The fixture has no gradlew. Failure and cleanup must leave the parent intact.
        self.assertNotEqual(0, result.returncode)
        self.assertEqual("user data", (parent / "keep.txt").read_text())
        self.assertEqual(["keep.txt"], sorted(entry.name for entry in parent.iterdir()))


if __name__ == "__main__":
    unittest.main()
