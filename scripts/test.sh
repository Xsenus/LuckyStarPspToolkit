#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
DOTNET=$("$ROOT/scripts/resolve-dotnet.sh")
"$ROOT/scripts/build.sh" --compile-only
cd "$ROOT"
python3 scripts/reference_oracle.py >/dev/null
python3 validation/validate_font_fixtures.py >/dev/null
python3 validation/validate_iso_fixture.py >/dev/null
CUSTOMER_ARGS=()
if [ $# -gt 1 ]; then
  echo "usage: $0 [/path/to/archive.zip]" >&2
  exit 64
fi
if [ $# -eq 1 ]; then CUSTOMER_ARGS+=(--customer-archive "$(realpath "$1")"); fi
"$DOTNET" run --project tests/LuckyStarPspToolkit.SelfTests/LuckyStarPspToolkit.SelfTests.csproj -c Release --no-build -- "${CUSTOMER_ARGS[@]}"
"$DOTNET" run --project tests/LuckyStarPspToolkit.Formats.SelfTests/LuckyStarPspToolkit.Formats.SelfTests.csproj -c Release --no-build -- "${CUSTOMER_ARGS[@]}"
python3 scripts/check_locked_cli.py --dotnet "$DOTNET" --cli "$ROOT/src/LuckyStarPspToolkit.Cli/bin/Release/net9.0/lsptool.dll"
