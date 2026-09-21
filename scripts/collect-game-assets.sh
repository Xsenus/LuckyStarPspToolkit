#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'TXT'
usage: collect-game-assets.sh /path/to/game.iso [output.zip]
       [--include-optional] [--no-listing] [--skip-source-hash]
TXT
  exit 64
}

[[ $# -ge 1 ]] || usage
ROOT=$(cd "$(dirname "$0")/.." && pwd)
DOTNET=$("$ROOT/scripts/resolve-dotnet.sh")
ISO=$(realpath "$1")
shift
OUT=""
FLAGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --include-optional|--no-listing|--skip-source-hash)
      FLAGS+=("$1")
      ;;
    --*)
      usage
      ;;
    *)
      [[ -z "$OUT" ]] || usage
      OUT=$1
      ;;
  esac
  shift
done
OUT=${OUT:-$PWD/lucky-star-psp-assets.zip}
OUT=$(realpath -m "$OUT")
REPORT="$OUT.json"

if "$DOTNET" run \
  --project "$ROOT/src/LuckyStarPspToolkit.Cli/LuckyStarPspToolkit.Cli.csproj" \
  -c Release -- \
  collect-assets "$ISO" "$OUT" "${FLAGS[@]}" --json "$REPORT"; then
  STATUS=0
else
  STATUS=$?
fi
if [[ $STATUS -ne 0 && $STATUS -ne 2 ]]; then
  exit "$STATUS"
fi
printf 'Created: %s\nReport:  %s\n' "$OUT" "$REPORT"
if [[ $STATUS -eq 2 ]]; then
  echo 'warning: bundle is incomplete; inspect the JSON report for missing required files.' >&2
fi
exit "$STATUS"
