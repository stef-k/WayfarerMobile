#!/usr/bin/env python3
"""Interactive release script for WayfarerMobile.

Automates the full release ceremony:
  - Preflight checks (git, gh, clean tree, on main)
  - Version bump in .csproj (regex write, ElementTree read)
  - Branch creation, commit, push, PR
  - Post-merge cleanup, tag, draft GitHub release

Usage:
    python scripts/release.py
"""

from __future__ import annotations

import os
import re
import signal
import subprocess
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from enum import Enum, auto
from pathlib import Path
from typing import Optional

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

CSPROJ_RELATIVE = os.path.join("src", "WayfarerMobile", "WayfarerMobile.csproj")
REPO_NAME = "stef-k/WayfarerMobile"
CHANGELOG_URL = f"https://github.com/{REPO_NAME}/blob/main/CHANGELOG.md"

SEMVER_RE = re.compile(r"^\d+\.\d+\.\d+$")


# ---------------------------------------------------------------------------
# Release state tracker (cleanup on failure)
# ---------------------------------------------------------------------------


class Step(Enum):
    """Tracks how far the release process has progressed."""

    NONE = auto()
    BRANCH_CREATED = auto()
    CSPROJ_MODIFIED = auto()
    COMMITTED = auto()
    PUSHED = auto()
    PR_CREATED = auto()
    MERGED = auto()
    ON_MAIN = auto()
    LOCAL_BRANCH_DELETED = auto()
    TAGGED = auto()
    TAG_PUSHED = auto()
    RELEASE_CREATED = auto()


@dataclass
class ReleaseState:
    """Tracks release progress for cleanup instructions on failure."""

    step: Step = Step.NONE
    branch_name: str = ""
    version: str = ""
    pr_number: str = ""
    tag_name: str = ""

    def cleanup_instructions(self) -> list[str]:
        """Return shell commands to undo partial release state."""
        cmds: list[str] = []

        if self.step.value >= Step.RELEASE_CREATED.value:
            cmds.append(f"gh release delete {self.tag_name} --yes")

        if self.step.value >= Step.TAG_PUSHED.value:
            cmds.append(f"git push origin :refs/tags/{self.tag_name}")

        if self.step.value >= Step.TAGGED.value:
            cmds.append(f"git tag -d {self.tag_name}")

        if self.step.value >= Step.PR_CREATED.value and self.step.value < Step.MERGED.value:
            cmds.append(f"gh pr close {self.pr_number} --delete-branch")

        if self.step.value >= Step.PUSHED.value and self.step.value < Step.MERGED.value:
            cmds.append(f"git push origin --delete {self.branch_name}")

        if self.step.value >= Step.BRANCH_CREATED.value and self.step.value < Step.LOCAL_BRANCH_DELETED.value:
            cmds.append("git checkout main")
            cmds.append(f"git branch -D {self.branch_name}")

        if self.step.value >= Step.CSPROJ_MODIFIED.value and self.step.value < Step.MERGED.value:
            cmds.append(f"git checkout main -- {CSPROJ_RELATIVE}")

        return cmds


# Global state for signal handler access.
_state = ReleaseState()


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _abort_handler(_signum: int, _frame: object) -> None:
    """Print cleanup instructions on Ctrl+C."""
    print("\n\nAborted by user.")
    _print_cleanup()
    sys.exit(1)


def _print_cleanup() -> None:
    """Print cleanup commands if any state needs reverting."""
    cmds = _state.cleanup_instructions()
    if cmds:
        print("\nTo clean up the partial release, run:")
        for cmd in cmds:
            print(f"  {cmd}")
    else:
        print("\nNo cleanup needed.")


def run(cmd: list[str], *, check: bool = True, capture: bool = True) -> subprocess.CompletedProcess[str]:
    """Run a subprocess command, returning CompletedProcess."""
    return subprocess.run(cmd, check=check, capture_output=capture, text=True)


def run_output(cmd: list[str]) -> str:
    """Run a command and return stripped stdout."""
    return run(cmd).stdout.strip()


def fatal(msg: str) -> None:
    """Print an error and exit."""
    print(f"\nError: {msg}")
    _print_cleanup()
    sys.exit(1)


def confirm(prompt: str) -> str:
    """Prompt the user and return their lowered input."""
    try:
        return input(prompt).strip().lower()
    except (EOFError, KeyboardInterrupt):
        print()
        _abort_handler(0, None)
        return ""  # unreachable


# ---------------------------------------------------------------------------
# Preflight checks
# ---------------------------------------------------------------------------


def preflight(repo_root: Path) -> Path:
    """Run all preflight checks. Returns the absolute .csproj path."""

    # 1. git on PATH
    try:
        run(["git", "--version"])
    except FileNotFoundError:
        fatal("git is not installed or not on PATH.")

    # 2. gh on PATH and authenticated
    try:
        result = run(["gh", "auth", "status"], check=False)
        if result.returncode != 0:
            fatal("gh CLI is not authenticated. Run 'gh auth login' first.")
    except FileNotFoundError:
        fatal("gh CLI is not installed or not on PATH.")

    # 3. On main branch
    branch = run_output(["git", "rev-parse", "--abbrev-ref", "HEAD"])
    if branch != "main":
        fatal(f"Must be on 'main' branch (currently on '{branch}').")

    # 4. Clean working tree
    status = run_output(["git", "status", "--porcelain"])
    if status:
        fatal("Working tree is not clean. Commit or stash changes first.")

    # 5. .csproj exists
    csproj = repo_root / CSPROJ_RELATIVE
    if not csproj.exists():
        fatal(f".csproj not found at {csproj}")

    # 6. Warn if behind remote
    run(["git", "fetch", "origin", "main"], check=False)
    behind = run_output(["git", "rev-list", "--count", "HEAD..origin/main"])
    if behind and int(behind) > 0:
        print(f"Warning: main is {behind} commit(s) behind origin/main.")
        ans = confirm("Continue anyway? [y/n] ")
        if ans != "y":
            print("Aborted.")
            sys.exit(0)

    return csproj


# ---------------------------------------------------------------------------
# Version reading
# ---------------------------------------------------------------------------


def parse_semver(v: str) -> tuple[int, ...]:
    """Parse a version string to a comparable tuple.

    Accepts both 'X.Y' and 'X.Y.Z' formats.
    """
    parts = v.split(".")
    if len(parts) == 2:
        parts.append("0")
    return tuple(int(p) for p in parts)


def read_csproj_versions(csproj: Path) -> tuple[str, int]:
    """Read ApplicationDisplayVersion and ApplicationVersion from .csproj.

    Returns (display_version, build_number).
    """
    tree = ET.parse(csproj)
    root = tree.getroot()

    display_el = root.find(".//ApplicationDisplayVersion")
    build_el = root.find(".//ApplicationVersion")

    if display_el is None or display_el.text is None:
        fatal("Cannot find <ApplicationDisplayVersion> in .csproj")
    if build_el is None or build_el.text is None:
        fatal("Cannot find <ApplicationVersion> in .csproj")

    return display_el.text.strip(), int(build_el.text.strip())


def get_latest_tag() -> Optional[str]:
    """Return the latest semver tag, or None if no tags exist."""
    result = run(["git", "tag", "--list", "--sort=-v:refname"], check=False)
    tags = [t for t in result.stdout.strip().splitlines() if SEMVER_RE.match(t.strip())]
    return tags[0].strip() if tags else None


# ---------------------------------------------------------------------------
# Version validation
# ---------------------------------------------------------------------------


def validate_version(new_ver: str, current_display: str, latest_tag: Optional[str]) -> Optional[str]:
    """Validate new version. Returns error message or None if valid."""
    if not SEMVER_RE.match(new_ver):
        return f"'{new_ver}' is not valid semver (expected X.Y.Z)."

    new_tuple = parse_semver(new_ver)
    current_tuple = parse_semver(current_display)

    if new_tuple <= current_tuple:
        return f"New version {new_ver} must be greater than current {current_display}."

    if latest_tag:
        tag_tuple = parse_semver(latest_tag)
        if new_tuple <= tag_tuple:
            return f"New version {new_ver} must be greater than latest tag {latest_tag}."

    # Check tag doesn't already exist
    existing = run(["git", "tag", "--list", new_ver], check=False)
    if existing.stdout.strip():
        return f"Tag '{new_ver}' already exists."

    return None


# ---------------------------------------------------------------------------
# .csproj modification (regex — preserves formatting and comments)
# ---------------------------------------------------------------------------


def update_csproj(csproj: Path, new_ver: str, new_build: int) -> None:
    """Update version fields in .csproj using regex substitution."""
    content = csproj.read_text(encoding="utf-8")

    updated = re.sub(
        r"(<ApplicationDisplayVersion>).*?(</ApplicationDisplayVersion>)",
        rf"\g<1>{new_ver}\g<2>",
        content,
    )
    updated = re.sub(
        r"(<ApplicationVersion>)\d+(</ApplicationVersion>)",
        rf"\g<1>{new_build}\g<2>",
        updated,
    )

    if updated == content:
        fatal("Regex substitution did not change the .csproj — check the file format.")

    csproj.write_text(updated, encoding="utf-8")


# ---------------------------------------------------------------------------
# Git / GitHub operations
# ---------------------------------------------------------------------------


def create_branch(name: str) -> None:
    """Create and switch to a new branch."""
    run(["git", "checkout", "-b", name])


def commit_version_bump(csproj: Path, version: str, build: int) -> None:
    """Stage the .csproj and commit the version bump."""
    run(["git", "add", str(csproj)])
    run(["git", "commit", "-m", f"chore: bump version to {version} (build {build})"])


def push_branch(branch: str) -> None:
    """Push the branch to origin with upstream tracking."""
    run(["git", "push", "-u", "origin", branch])


def create_pr(branch: str, version: str) -> str:
    """Create a PR and return the PR number."""
    title = f"chore: bump version to {version}"
    body = (
        f"## Summary\n"
        f"- Bump `ApplicationDisplayVersion` to `{version}`\n"
        f"- Auto-increment `ApplicationVersion` (build number)\n"
        f"\nAutomated by `scripts/release.py`."
    )
    result = run(["gh", "pr", "create", "--base", "main", "--head", branch, "--title", title, "--body", body])
    # gh pr create outputs the PR URL; extract the number.
    pr_url = result.stdout.strip()
    pr_number = pr_url.rstrip("/").split("/")[-1]
    return pr_number


def wait_for_merge(pr_number: str) -> bool:
    """Poll the user for merge action. Returns True if merged, False to abort."""
    while True:
        print(f"\nPR #{pr_number} created. Waiting for merge.")
        print("  [w] Check merge status")
        print("  [m] Merge via CLI (gh pr merge)")
        print("  [a] Abort (print cleanup commands)")
        ans = confirm("\nChoice: ")

        if ans == "w":
            result = run(["gh", "pr", "view", pr_number, "--json", "state"], check=False)
            if result.returncode == 0 and '"MERGED"' in result.stdout:
                print("PR is merged!")
                return True
            print("PR is not yet merged.")

        elif ans == "m":
            merge_result = run(["gh", "pr", "merge", pr_number, "--merge", "--delete-branch"], check=False)
            if merge_result.returncode == 0:
                print("PR merged successfully.")
                return True
            print(f"Merge failed: {merge_result.stderr.strip()}")

        elif ans == "a":
            return False
        else:
            print("Invalid choice.")


def post_merge_cleanup(branch: str) -> None:
    """Switch to main, pull, and delete the local feature branch."""
    run(["git", "checkout", "main"])
    _state.step = Step.ON_MAIN
    run(["git", "pull", "origin", "main"])
    # Delete local branch (may already be deleted by --delete-branch in merge).
    result = run(["git", "branch", "-D", branch], check=False)
    if result.returncode == 0:
        _state.step = Step.LOCAL_BRANCH_DELETED


def create_tag(version: str) -> None:
    """Create an annotated tag locally."""
    run(["git", "tag", "-a", version, "-m", f"Release {version}"])


def push_tag(version: str) -> None:
    """Push a single tag to origin."""
    run(["git", "push", "origin", version])


def create_github_release(version: str) -> str:
    """Create a draft GitHub release. Returns the release URL."""
    body = f"See [{version} changelog]({CHANGELOG_URL}#{version.replace('.', '')}) for details."
    result = run([
        "gh", "release", "create", version,
        "--title", f"v{version}",
        "--notes", body,
        "--draft",
    ])
    return result.stdout.strip()


# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------


def display_current_state(display_ver: str, build_num: int, latest_tag: Optional[str]) -> None:
    """Print the current version state."""
    print("\n--- Current State ---")
    print(f"  ApplicationDisplayVersion : {display_ver}")
    print(f"  ApplicationVersion (build): {build_num}")
    print(f"  Latest git tag            : {latest_tag or '(none)'}")
    print()


def prompt_version(current_display: str, latest_tag: Optional[str]) -> str:
    """Prompt the user for a new version, with validation."""
    while True:
        new_ver = input("Enter new version (X.Y.Z): ").strip()
        if not new_ver:
            continue
        error = validate_version(new_ver, current_display, latest_tag)
        if error:
            print(f"  {error}")
            continue
        return new_ver


def show_confirmation(display_ver: str, build_num: int, new_ver: str, new_build: int, branch: str) -> str:
    """Show before/after and ask for confirmation. Returns 'y', 'n', or 'r'."""
    print("\n--- Confirmation ---")
    print(f"  ApplicationDisplayVersion : {display_ver} -> {new_ver}")
    print(f"  ApplicationVersion (build): {build_num} -> {new_build}")
    print(f"  Branch                    : {branch}")
    print(f"  Tag                       : {new_ver}")
    print()
    print("  [y] Proceed")
    print("  [n] Cancel")
    print("  [r] Restart with different version")
    return confirm("\nChoice: ")


def main() -> None:
    """Entry point for the release script."""
    signal.signal(signal.SIGINT, _abort_handler)

    # Determine repo root (script is in scripts/).
    repo_root = Path(__file__).resolve().parent.parent
    os.chdir(repo_root)

    print("=== WayfarerMobile Release Script ===\n")

    # --- Preflight ---
    csproj = preflight(repo_root)

    # --- Current state ---
    display_ver, build_num = read_csproj_versions(csproj)
    latest_tag = get_latest_tag()
    display_current_state(display_ver, build_num, latest_tag)

    # --- Version prompt loop ---
    while True:
        new_ver = prompt_version(display_ver, latest_tag)
        new_build = build_num + 1
        branch = f"chore/version-bump-{new_ver}"

        choice = show_confirmation(display_ver, build_num, new_ver, new_build, branch)
        if choice == "y":
            break
        elif choice == "r":
            continue
        else:
            print("Cancelled.")
            sys.exit(0)

    # --- Point of no return ---
    _state.version = new_ver
    _state.branch_name = branch
    _state.tag_name = new_ver

    # 1. Create branch
    print(f"\nCreating branch '{branch}'...")
    create_branch(branch)
    _state.step = Step.BRANCH_CREATED

    # 2. Update .csproj
    print("Updating .csproj...")
    update_csproj(csproj, new_ver, new_build)
    _state.step = Step.CSPROJ_MODIFIED

    # 3. Commit
    print("Committing version bump...")
    commit_version_bump(csproj, new_ver, new_build)
    _state.step = Step.COMMITTED

    # 4. Push
    print(f"Pushing '{branch}' to origin...")
    push_branch(branch)
    _state.step = Step.PUSHED

    # 5. Create PR
    print("Creating pull request...")
    pr_number = create_pr(branch, new_ver)
    _state.pr_number = pr_number
    _state.step = Step.PR_CREATED
    print(f"PR #{pr_number} created.")

    # 6. Wait for merge
    if not wait_for_merge(pr_number):
        print("\nAborted. PR is still open.")
        _print_cleanup()
        sys.exit(1)
    _state.step = Step.MERGED

    # 7. Post-merge cleanup
    print("\nPost-merge cleanup...")
    post_merge_cleanup(branch)

    # 8. Create + push tag
    print(f"Creating tag '{new_ver}'...")
    create_tag(new_ver)
    _state.step = Step.TAGGED

    print(f"Pushing tag '{new_ver}'...")
    push_tag(new_ver)
    _state.step = Step.TAG_PUSHED

    # 9. Draft GitHub release
    print("Creating draft GitHub release...")
    release_url = create_github_release(new_ver)
    _state.step = Step.RELEASE_CREATED

    # --- Success ---
    print("\n=== Release Complete ===")
    print(f"  Version : {new_ver}")
    print(f"  Build   : {new_build}")
    print(f"  Tag     : {new_ver}")
    print(f"  Release : {release_url}")
    print()
    print("Next steps:")
    print(f"  1. Update CHANGELOG.md with {new_ver} entries")
    print(f"  2. Review and publish the draft release on GitHub")
    print(f"  3. Build and attach the APK to the release")


if __name__ == "__main__":
    main()
