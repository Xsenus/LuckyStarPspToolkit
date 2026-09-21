#!/usr/bin/env bash
# Delegate all checked build/release work to the shared driver.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
exec python3 "$ROOT/scripts/build_release.py" "$@"
