#!/usr/bin/env bash
# Usage: publish.sh [linux-x64|win-x64]; release destination is fixed by VERSION.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ "$#" -gt 1 ]; then echo "usage: $0 [linux-x64|win-x64]" >&2; exit 64; fi
exec python3 "$ROOT/scripts/build_release.py" --rids "${1:-linux-x64}"
