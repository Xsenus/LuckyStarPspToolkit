#!/usr/bin/env bash
# Usage: publish.sh [linux-x64|win-x64] [PUBLIC-client-trust.json]; no trust means LOCKED preview.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ "$#" -gt 2 ]; then echo "usage: $0 [linux-x64|win-x64] [PUBLIC-client-trust.json]" >&2; exit 64; fi
ARGS=(--rids "${1:-linux-x64}")
if [ "$#" -eq 2 ]; then ARGS+=(--license-trust "$2"); fi
exec python3 "$ROOT/scripts/build_release.py" "${ARGS[@]}"
