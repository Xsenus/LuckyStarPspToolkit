#!/usr/bin/env bash
# Build, test, publish and package; a failed step prevents publication.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")" && pwd)
exec python3 "$ROOT/scripts/build_release.py" "$@"
