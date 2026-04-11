#!/usr/bin/env bash
# Stage a WebGL deploy onto the gh-pages branch worktree.
# Does NOT push — prints the exact push command for the user to run after review.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="${REPO_ROOT}/build/WebGL"
WORKTREE_DIR="${REPO_ROOT}/../Meteor-Idle-gh-pages"
BRANCH="gh-pages"
REMOTE="origin"

cd "${REPO_ROOT}"

if [[ ! -f "${BUILD_DIR}/index.html" ]]; then
    echo "error: ${BUILD_DIR}/index.html missing. Run tools/build-webgl.sh first." >&2
    exit 1
fi

# Refuse to deploy over uncommitted source changes — deploys must correspond to a committed state.
if ! git diff --quiet -- Assets ProjectSettings || ! git diff --cached --quiet -- Assets ProjectSettings; then
    echo "error: uncommitted changes in Assets/ or ProjectSettings/. Commit first, then deploy." >&2
    exit 1
fi

SOURCE_SHA=$(git rev-parse --short HEAD)
SOURCE_BRANCH=$(git rev-parse --abbrev-ref HEAD)

# Identity-leak check on what we're about to publish. Hard gate per CLAUDE.md.
echo "==> Running identity scrub on build output"
pushd "${BUILD_DIR}" >/dev/null
if ! git -C "${REPO_ROOT}" ls-files --error-unmatch "${BUILD_DIR}" >/dev/null 2>&1; then
    # build/ is gitignored, so identity-scrub.py's git-based modes don't apply.
    # Fall back to grepping the build output directly against the patterns file.
    PATTERNS_FILE="${REPO_ROOT}/.claude-identity-scrub"
    if [[ ! -f "${PATTERNS_FILE}" ]]; then
        echo "error: ${PATTERNS_FILE} missing. See memory/feedback_identity_leaks.md." >&2
        exit 2
    fi
    # Build a safe grep invocation that doesn't echo the tokens.
    MATCHES=$(grep -rIf "${PATTERNS_FILE}" "${BUILD_DIR}" 2>/dev/null | wc -l | tr -d ' ')
    if [[ "${MATCHES}" != "0" ]]; then
        echo "IDENTITY LEAK DETECTED in build output (${MATCHES} matches). Aborting deploy." >&2
        echo "Inspect manually with: grep -rIf .claude-identity-scrub build/WebGL" >&2
        exit 1
    fi
    echo "    clean (0 matches)"
fi
popd >/dev/null

# Ensure gh-pages branch exists locally or on the remote.
git fetch "${REMOTE}" "${BRANCH}" 2>/dev/null || true
if ! git rev-parse --verify --quiet "refs/heads/${BRANCH}" >/dev/null && \
   ! git rev-parse --verify --quiet "refs/remotes/${REMOTE}/${BRANCH}" >/dev/null; then
    echo "error: ${BRANCH} branch does not exist locally or on ${REMOTE}." >&2
    echo "       Bootstrap it once with:" >&2
    echo "         git checkout --orphan ${BRANCH}" >&2
    echo "         git rm -rf ." >&2
    echo "         echo 'Meteor Idle WebGL build' > README.md" >&2
    echo "         touch .nojekyll" >&2
    echo "         git add -A && git commit -m 'Initial gh-pages'" >&2
    echo "         git push -u ${REMOTE} ${BRANCH}" >&2
    echo "         git checkout ${SOURCE_BRANCH}" >&2
    exit 1
fi

# Prepare worktree.
if [[ -d "${WORKTREE_DIR}" ]]; then
    echo "==> Reusing worktree at ${WORKTREE_DIR}"
    git -C "${WORKTREE_DIR}" fetch "${REMOTE}" "${BRANCH}"
    git -C "${WORKTREE_DIR}" reset --hard "${REMOTE}/${BRANCH}"
else
    echo "==> Creating worktree at ${WORKTREE_DIR}"
    git worktree add "${WORKTREE_DIR}" "${BRANCH}"
fi

# rsync build output into the worktree. Preserve .git via exclude; everything else is delete-synced.
echo "==> Syncing ${BUILD_DIR}/ -> ${WORKTREE_DIR}/"
rsync -a --delete --exclude '.git' "${BUILD_DIR}/" "${WORKTREE_DIR}/"

# .nojekyll so GH Pages does not hide the Build/ folder (Jekyll ignores underscore-prefixed dirs,
# and serves .wasm/.data mimetypes more reliably with Jekyll disabled).
touch "${WORKTREE_DIR}/.nojekyll"

# Stage + commit.
git -C "${WORKTREE_DIR}" add -A
if git -C "${WORKTREE_DIR}" diff --cached --quiet; then
    echo "==> No changes on ${BRANCH}; nothing to deploy."
    exit 0
fi

COMMIT_MSG="Deploy WebGL build ${SOURCE_SHA} from ${SOURCE_BRANCH}"
git -C "${WORKTREE_DIR}" commit -m "${COMMIT_MSG}"

echo ""
echo "==> gh-pages commit staged. Review, then push manually:"
echo "      git -C \"${WORKTREE_DIR}\" log -1"
echo "      git -C \"${WORKTREE_DIR}\" push ${REMOTE} ${BRANCH}"
echo ""
echo "    Live URL after push: https://muwamath.github.io/Meteor-Idle/"
