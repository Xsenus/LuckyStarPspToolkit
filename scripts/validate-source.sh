#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
python3 scripts/reference_oracle.py >/dev/null
python3 validation/static_validate.py
python3 validation/validate_font_fixtures.py
python3 validation/validate_iso_fixture.py
