#!/usr/bin/env bash
# Build Meteor Idle for WebGL via headless Unity CLI.
# Output: <repo>/build/WebGL/

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_VERSION="6000.4.1f1"
UNITY_BIN="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity"
BUILD_DIR="${REPO_ROOT}/build/WebGL"
LOG_FILE="${REPO_ROOT}/build/webgl-build.log"

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

echo "==> Running Unity headless build (log: ${LOG_FILE})"
set +e
"${UNITY_BIN}" \
    -batchmode \
    -nographics \
    -quit \
    -projectPath "${REPO_ROOT}" \
    -buildTarget WebGL \
    -executeMethod BuildScripts.BuildWebGL \
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

SIZE=$(du -sh "${BUILD_DIR}" | awk '{print $1}')
echo "==> WebGL build OK: ${BUILD_DIR} (${SIZE})"
echo "    Serve locally with: python3 -m http.server 8000 --directory \"${BUILD_DIR}\""
