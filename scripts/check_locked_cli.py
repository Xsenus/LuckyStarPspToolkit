#!/usr/bin/env python3
"""Assert that an unconfigured distribution refuses processing without a license; this is not a bypass."""
from __future__ import annotations
import argparse
from pathlib import Path
import subprocess
import sys
import tempfile
import os

ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    """Execute the real entry point and require exit 77 before the input file is touched."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cli', type=Path, required=True)
    parser.add_argument('--pwsh', type=Path)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    prefix = ([str(args.pwsh), '-NoLogo', '-NoProfile', '-File', str(ROOT / 'scripts/run_managed.ps1'), str(args.cli)]
              if args.pwsh else [args.dotnet, str(args.cli)] if args.cli.suffix == '.dll' else [str(args.cli)])
    with tempfile.TemporaryDirectory(prefix="lsp-locked-") as home:
        env = {**os.environ, "HOME": home, "XDG_DATA_HOME": home, "LOCALAPPDATA": home, "APPDATA": home}
        result = subprocess.run([*prefix, 'cpk-list', 'THIS_INPUT_MUST_NOT_BE_OPENED.cpk'], capture_output=True, text=True, timeout=30, env=env)
    if result.returncode != 77 or 'LICENSE_' not in result.stderr:
        print('Unlicensed processing was not blocked as expected.', file=sys.stderr)
        return 1
    print('PASS: actual CLI entry point denies unlicensed processing (exit 77).')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
