#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if command -v dotnet >/dev/null 2>&1; then command -v dotnet; exit 0; fi
if [ -f "$ROOT/.dotnet-path" ]; then
  D=$(cat "$ROOT/.dotnet-path")
  if [ -x "$D" ]; then printf '%s\n' "$D"; exit 0; fi
fi
if [ -x "$HOME/.dotnet/dotnet" ]; then printf '%s\n' "$HOME/.dotnet/dotnet"; exit 0; fi
echo "dotnet SDK not found" >&2
exit 1
