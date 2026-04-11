#!/usr/bin/env bash
# Build Meteor Idle for WebGL as a development build.
# Unity auto-defines DEVELOPMENT_BUILD which unlocks DebugOverlay and any
# other debug-only surfaces wrapped in #if UNITY_EDITOR || DEVELOPMENT_BUILD.
# Output: <repo>/build/WebGL-dev/
#
# Never deploy this directory to gh-pages — tools/deploy-webgl.sh aborts if
# it finds a .dev-build-marker sentinel inside build/WebGL/, and this script
# writes such a sentinel into build/WebGL-dev/ on success.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_VERSION="6000.4.1f1"
UNITY_BIN="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
BUILD_DIR="${REPO_ROOT}/build/WebGL-dev"
LOG_FILE="${REPO_ROOT}/build/webgl-dev-build.log"

if [[ ! -x "${UNITY_BIN}" ]]; then
    echo "error: Unity ${UNITY_VERSION} not found at ${UNITY_BIN}" >&2
    exit 1
fi

if pgrep -f "Unity.app/Contents/MacOS/Unity .* -projectPath .*Meteor Idle" >/dev/null 2>&1; then
    echo "error: Unity Editor has this project open. Close it before running a CLI build." >&2
    echo "       (Unity holds an exclusive lock on the project directory.)" >&2
    exit 1
fi

echo "==> Cleaning ${BUILD_DIR}"
rm -rf "${BUILD_DIR}"
mkdir -p "${REPO_ROOT}/build"

echo "==> Running Unity headless DEV build (log: ${LOG_FILE})"
set +e
"${UNITY_BIN}" \
    -batchmode \
    -nographics \
    -quit \
    -projectPath "${REPO_ROOT}" \
    -buildTarget WebGL \
    -executeMethod BuildScripts.BuildWebGLDev \
    -logFile "${LOG_FILE}"
rc=$?
set -e

if [[ ${rc} -ne 0 ]]; then
    echo "error: Unity build failed (exit ${rc}). Tail of log:" >&2
    tail -40 "${LOG_FILE}" >&2 || true
    exit "${rc}"
fi

if [[ ! -f "${BUILD_DIR}/index.html" ]]; then
    echo "error: build completed but ${BUILD_DIR}/index.html is missing." >&2
    exit 1
fi

# Sentinel file — tools/deploy-webgl.sh checks for this inside build/WebGL/
# and refuses to deploy if present. Living in build/WebGL-dev/ it also tags
# this directory for any human reader wondering whether it's a dev or prod build.
touch "${BUILD_DIR}/.dev-build-marker"

SIZE=$(du -sh "${BUILD_DIR}" | awk '{print $1}')
echo "==> WebGL DEV build OK: ${BUILD_DIR} (${SIZE})"
echo "    Serve locally with: tools/serve-webgl-dev.sh"
echo "    (Or: python3 -m http.server 8000 --directory \"${BUILD_DIR}\")"
