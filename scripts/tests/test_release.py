import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from subprocess import CompletedProcess
from unittest.mock import patch


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "release.py"
SPEC = importlib.util.spec_from_file_location("wayfarer_release", SCRIPT_PATH)
release = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = release
SPEC.loader.exec_module(release)


class ValidateChangelogVersionTests(unittest.TestCase):
    def test_accepts_exact_release_heading(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "CHANGELOG.md").write_text(
                "# Changelog\n\n## 1.2.0\n\n- Entry\n",
                encoding="utf-8",
            )

            self.assertIsNone(release.validate_changelog_version(root, "1.2.0"))

    def test_rejects_missing_release_heading(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "CHANGELOG.md").write_text(
                "# Changelog\n\n## 1.1.0\n",
                encoding="utf-8",
            )

            error = release.validate_changelog_version(root, "1.2.0")

            self.assertIn("## 1.2.0", error)


class CleanupInstructionsTests(unittest.TestCase):
    def test_local_branch_cleanup_is_non_forcing(self):
        state = release.ReleaseState(
            step=release.Step.BRANCH_CREATED,
            branch_name="feature/version-bump-1.2.0",
        )

        commands = state.cleanup_instructions()

        self.assertIn("git branch -d feature/version-bump-1.2.0", commands)
        self.assertNotIn("git branch -D feature/version-bump-1.2.0", commands)

    @patch.object(release, "run")
    def test_post_merge_cleanup_accepts_already_deleted_local_branch(self, run_mock):
        run_mock.side_effect = [
            CompletedProcess(["git", "checkout", "main"], 0),
            CompletedProcess(["git", "pull", "origin", "main"], 0),
            CompletedProcess(["git", "show-ref"], 1),
        ]

        release.post_merge_cleanup("feature/version-bump-1.2.0")

        self.assertEqual(release.Step.LOCAL_BRANCH_DELETED, release._state.step)
        self.assertEqual(3, run_mock.call_count)

    @patch.object(release, "run")
    def test_post_merge_cleanup_safely_deletes_existing_local_branch(self, run_mock):
        run_mock.side_effect = [
            CompletedProcess(["git", "checkout", "main"], 0),
            CompletedProcess(["git", "pull", "origin", "main"], 0),
            CompletedProcess(["git", "show-ref"], 0),
            CompletedProcess(["git", "branch", "-d"], 0),
        ]

        release.post_merge_cleanup("feature/version-bump-1.2.0")

        run_mock.assert_called_with(
            ["git", "branch", "-d", "feature/version-bump-1.2.0"],
            check=False,
        )


if __name__ == "__main__":
    unittest.main()
