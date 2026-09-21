#!/usr/bin/env python3
"""Validate and report C# XML documentation coverage without compiling the project."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from csharp_symbols import scan_tree  # noqa: E402
from document_csharp import documentation_problems  # noqa: E402


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "validation/csharp-doc-validation.json")
    parser.add_argument("--src-only", action="store_true", help="Exclude test sources from the report.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    paths = sorted((ROOT / "src").rglob("*.cs"))
    if not args.src_only:
        paths.extend(sorted((ROOT / "tests").rglob("*.cs")))

    if not args.src_only:
        paths.extend(sorted((ROOT / "tools").rglob("*.cs")))
    paths = [p for p in paths if not {"bin", "obj"} & set(p.parts)]
    types, methods = scan_tree(paths)
    problems = documentation_problems(paths)
    public_methods = [item for item in methods if item.visibility in {"public", "protected"}]
    local_methods = [item for item in methods if item.is_local]
    generic_methods = [item for item in methods if item.type_parameters]

    generic_summaries: list[str] = []
    summary_pattern = re.compile(r"///\s+Executes the .+ operation\.")
    for path in paths:
        for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if summary_pattern.search(line):
                generic_summaries.append(f"{path.relative_to(ROOT)}:{line_number}")

    report = {
        "schema": "lucky-star-psp.csharp-doc-validation.v1",
        "passed": not problems,
        "version": (ROOT / "VERSION").read_text().strip(),
        "mode": "offline-lexical-xml; not compilation",
        "files": len(paths),
        "types": len(types),
        "methods": len(methods),
        "publicOrProtectedMethods": len(public_methods),
        "localFunctions": len(local_methods),
        "genericMethods": len(generic_methods),
        "genericGeneratedSummaries": len(generic_summaries),
        "problems": problems,
        "genericSummaryLocations": generic_summaries,
    }
    output = args.output if args.output.is_absolute() else ROOT / args.output
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(
        f"C# XML documentation: {len(paths)} files, {len(types)} types, "
        f"{len(methods)} methods/functions, {len(problems)} problem(s)."
    )
    if generic_summaries:
        print(f"Informational: {len(generic_summaries)} generated summaries use the generic fallback.")
    for problem in problems:
        print(f"FAIL {problem}", file=sys.stderr)
    return 0 if not problems else 1


if __name__ == "__main__":
    raise SystemExit(main())
