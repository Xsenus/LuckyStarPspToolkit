#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'TXT'
usage: rebuild-game-iso.sh /path/to/game.iso /path/to/manifest.json [output.iso] [report.json]
TXT
  exit 64
}

[[ $# -ge 2 && $# -le 4 ]] || usage
ROOT=$(cd "$(dirname "$0")/.." && pwd)
VERSION=$(tr -d '\r\n' < "$ROOT/VERSION")
CLI="$ROOT/artifacts/releases/$VERSION/linux-x64/lsptool"
[[ -x "$CLI" ]] || { echo "Build and activate the configured Linux release first." >&2; exit 77; }
ISO=$(realpath "$1")
MANIFEST=$(realpath "$2")
OUTPUT=$(realpath -m "${3:-$PWD/LuckyStar-patched.iso}")
REPORT=$(realpath -m "${4:-$OUTPUT.json}")

"$CLI" iso-apply-manifest "$ISO" "$MANIFEST" "$OUTPUT" --json "$REPORT"

printf 'Created: %s\nReport:  %s\n' "$OUTPUT" "$REPORT"
