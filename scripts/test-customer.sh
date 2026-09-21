#!/usr/bin/env bash
set -euo pipefail
if [[ $# -ne 1 ]]; then
  echo "usage: $0 /path/to/archive.zip" >&2
  exit 64
fi
ROOT=$(cd "$(dirname "$0")/.." && pwd)
DOTNET=$("$ROOT/scripts/resolve-dotnet.sh")
ARCHIVE=$(realpath "$1")
"$ROOT/scripts/build.sh" --compile-only
cd "$ROOT"
"$DOTNET" run --project tests/LuckyStarPspToolkit.SelfTests/LuckyStarPspToolkit.SelfTests.csproj -c Release --no-build -- --customer-archive "$ARCHIVE"
"$DOTNET" run --project tests/LuckyStarPspToolkit.Formats.SelfTests/LuckyStarPspToolkit.Formats.SelfTests.csproj -c Release --no-build -- --customer-archive "$ARCHIVE"
set +e
"$DOTNET" run --project src/LuckyStarPspToolkit.Cli/LuckyStarPspToolkit.Cli.csproj -c Release --no-build -- customer-audit "$ARCHIVE" --json validation/csharp-customer-audit.json
code=$?
set -e
if [[ $code -ne 0 && $code -ne 2 ]]; then exit "$code"; fi
set +e
"$DOTNET" run --project src/LuckyStarPspToolkit.Cli/LuckyStarPspToolkit.Cli.csproj -c Release --no-build -- audit-rgo "$ARCHIVE" --json validation/csharp-rgo-audit.json
code=$?
set -e
if [[ $code -ne 0 && $code -ne 2 ]]; then exit "$code"; fi
