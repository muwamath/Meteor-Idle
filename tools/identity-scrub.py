#!/usr/bin/env python3
"""
Identity-leak scrub for the Meteor Idle repo.

Reads patterns from a gitignored file (`.claude-identity-scrub` in the repo
root) and scans the current staged git diff (or a specified commit range)
for any case-insensitive substring match. Exits:

  0 - clean (no matches)
  1 - one or more matches found
  2 - patterns file missing or empty (cannot run the scrub safely)

The patterns file lives outside git so the identity tokens never land in the
repo or in git history. A fresh clone won't have it — the first run will
exit with code 2 and a message pointing at the setup instructions.

Usage:
    python3 tools/identity-scrub.py                  # scan staged diff
    python3 tools/identity-scrub.py main..HEAD       # scan a commit range
    python3 tools/identity-scrub.py --working-tree   # scan entire working tree
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
PATTERNS_FILE = REPO_ROOT / ".claude-identity-scrub"

SETUP_HINT = """
The patterns file is gitignored and stores the identity tokens to scrub for.
It is intentionally outside git so the tokens themselves never land in the
repo or in history.

To set it up:
  1. Open CLAUDE.md and search for "identity scrub" OR open
     ~/.claude/projects/<project>/memory/feedback_identity_leaks.md
  2. Copy the listed tokens into .claude-identity-scrub, one per line.
     Lines starting with `#` are comments.
  3. Re-run this scrub — it will read the file and verify your diff.

The patterns file is already in .gitignore so it cannot be accidentally
staged or committed.
""".strip()


def load_patterns() -> list[str]:
    if not PATTERNS_FILE.exists():
        print(f"ERROR: patterns file {PATTERNS_FILE.name} is missing.", file=sys.stderr)
        print("", file=sys.stderr)
        print(SETUP_HINT, file=sys.stderr)
        sys.exit(2)

    patterns = [
        line.strip()
        for line in PATTERNS_FILE.read_text().splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    ]
    if not patterns:
        print(f"ERROR: patterns file {PATTERNS_FILE.name} is empty.", file=sys.stderr)
        print("", file=sys.stderr)
        print(SETUP_HINT, file=sys.stderr)
        sys.exit(2)
    return patterns


def get_diff(arg: str | None) -> str:
    if arg == "--working-tree":
        cmd = ["git", "diff", "HEAD"]
    elif arg is None:
        cmd = ["git", "diff", "--cached"]
    else:
        cmd = ["git", "diff", arg]
    result = subprocess.run(cmd, cwd=REPO_ROOT, capture_output=True)
    if result.returncode != 0:
        print(f"ERROR: git diff failed: {result.stderr.decode(errors='replace')}", file=sys.stderr)
        sys.exit(2)
    return result.stdout.decode(errors="replace")


def main() -> None:
    arg = sys.argv[1] if len(sys.argv) > 1 else None

    patterns = load_patterns()
    diff = get_diff(arg)
    diff_lower = diff.lower()

    matches = [p for p in patterns if p.lower() in diff_lower]
    if matches:
        print("IDENTITY LEAK DETECTED in git diff:", file=sys.stderr)
        for p in matches:
            print(f"  - matched pattern", file=sys.stderr)
        print(
            f"\n{len(matches)} pattern(s) from {PATTERNS_FILE.name} matched the diff.",
            file=sys.stderr,
        )
        print(
            "Do NOT commit. Rewrite the offending content and re-run the scrub.",
            file=sys.stderr,
        )
        sys.exit(1)

    target = arg or "staged diff"
    print(f"identity scrub: clean ({len(patterns)} pattern(s) checked against {target})")
    sys.exit(0)


if __name__ == "__main__":
    main()
