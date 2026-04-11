#!/usr/bin/env bash
# Serve the local WebGL dev build on http://localhost:8000 using Python's
# built-in http.server. Foreground by default — Ctrl-C to stop.
#
# Prerequisites: run tools/build-webgl-dev.sh first to produce build/WebGL-dev/.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="${REPO_ROOT}/build/WebGL-dev"
PORT="${PORT:-8000}"

if [[ ! -f "${BUILD_DIR}/index.html" ]]; then
    echo "error: ${BUILD_DIR}/index.html missing. Run tools/build-webgl-dev.sh first." >&2
    exit 1
fi

if lsof -iTCP:"${PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "error: port ${PORT} is already in use. Holder:" >&2
    lsof -iTCP:"${PORT}" -sTCP:LISTEN >&2 || true
    echo "       Stop the holding process or override with PORT=<n> tools/serve-webgl-dev.sh" >&2
    exit 1
fi

echo "==> Serving ${BUILD_DIR} on http://localhost:${PORT}/"
echo "    Press Ctrl-C to stop."
exec python3 -m http.server "${PORT}" --directory "${BUILD_DIR}"
