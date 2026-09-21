#!/usr/bin/env python3
"""Audit the repository for secrets, proprietary customer files and unsafe artifacts."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / "validation/customer-validation-0.8.0.json"
FIXTURE_ROOT = Path("tests/LuckyStarPspToolkit.Formats.SelfTests/Fixtures")

SECRET_PATTERNS: tuple[tuple[str, re.Pattern[bytes]], ...] = (
    ("private-key", re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----")),
    ("github-token", re.compile(rb"gh[pousr]_[A-Za-z0-9]{30,}")),
    ("aws-access-key", re.compile(rb"AKIA[0-9A-Z]{16}")),
    ("generic-password", re.compile(rb"(?i)(?:password|passwd|pwd)\s*[:=]\s*['\"][^'\"\r\n]{8,}['\"]")),
    ("generic-secret", re.compile(rb"(?i)(?:client_secret|api_key|access_token)\s*[:=]\s*['\"][^'\"\r\n]{12,}['\"]")),
)
FORBIDDEN_NAMES = {
    "archive.zip", "eboot.bin", "boot.bin", "data.bin", "titlein.pmf",
    "param.sfo", "param-2.sfo", "opnssmp.bin",
}
RUNTIME_BINARY_SUFFIXES = {".exe", ".dll", ".pdb", ".so", ".dylib"}
GAME_SUFFIXES = {".iso", ".cso", ".pmf", ".sfo", ".cpk", ".bin"}


def tracked_files() -> list[Path]:
    """Return all files tracked by Git in deterministic order."""
    if (ROOT / '.git').exists():
        process = subprocess.run(
            ["git", "ls-files", "-z"], cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False
        )
        if process.returncode != 0:
            raise RuntimeError(process.stderr.decode("utf-8", "replace"))
        return [Path(item.decode("utf-8")) for item in process.stdout.split(b"\0") if item]
    excluded = {'.git', 'bin', 'obj', 'artifacts', '__pycache__', 'private', 'assets', 'TestResults'}
    return sorted(p.relative_to(ROOT) for p in ROOT.rglob('*')
                  if p.is_file() and not excluded.intersection(p.relative_to(ROOT).parts))


def known_customer_hashes() -> set[str]:
    """Load SHA-256 values of customer-supplied files from preserved evidence."""
    data = json.loads(BASELINE.read_text(encoding="utf-8"))
    hashes = {item["sha256"] for item in data["files"]}
    hashes.add(data["eboot"]["decrypted_sha256"])
    return hashes


def is_fixture(path: Path) -> bool:
    """Return whether a tracked binary belongs to the synthetic fixture directory."""
    return path.is_relative_to(FIXTURE_ROOT)


def sha256_file(path: Path) -> str:
    """Return the lowercase SHA-256 digest of one repository file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def find_secret_matches(data: bytes) -> Iterable[str]:
    """Yield names of secret-like patterns found in a bounded byte sequence."""
    for name, pattern in SECRET_PATTERNS:
        if pattern.search(data):
            yield name


def audit() -> dict[str, object]:
    """Audit all tracked files and return a machine-readable report."""
    files = tracked_files()
    customer_hashes = known_customer_hashes()
    problems: list[str] = []
    secret_matches: list[dict[str, str]] = []
    binary_artifacts: list[str] = []
    synthetic_fixtures: list[str] = []

    for relative in files:
        absolute = ROOT / relative
        if not absolute.is_file():
            problems.append(f"tracked path is not a regular file: {relative}")
            continue
        lowered = relative.name.lower()
        suffix = relative.suffix.lower()
        if lowered in FORBIDDEN_NAMES and not is_fixture(relative):
            problems.append(f"customer/game file name is tracked outside synthetic fixtures: {relative}")
        if suffix in RUNTIME_BINARY_SUFFIXES:
            binary_artifacts.append(relative.as_posix())
            problems.append(f"unverified compiled artifact is tracked: {relative}")
        if suffix in GAME_SUFFIXES:
            if is_fixture(relative):
                synthetic_fixtures.append(relative.as_posix())
            else:
                problems.append(f"game-like binary is tracked outside synthetic fixtures: {relative}")
        digest = sha256_file(absolute)
        if digest in customer_hashes:
            problems.append(f"tracked file matches a customer-supplied SHA-256: {relative}")

        if absolute.stat().st_size <= 4 * 1024 * 1024 and suffix not in GAME_SUFFIXES:
            data = absolute.read_bytes()
            for match in find_secret_matches(data):
                secret_matches.append({"path": relative.as_posix(), "kind": match})
                problems.append(f"possible {match} in {relative}")

    report: dict[str, object] = {
        "schema": "lucky-star-psp.repository-audit.v1",
        "passed": not problems,
        "trackedFileCount": len(files),
        "scope": "git-tracked" if (ROOT / ".git").exists() else "source-snapshot",
        "secretMatchCount": len(secret_matches),
        "unverifiedCompiledArtifactCount": len(binary_artifacts),
        "syntheticFixtureCount": len(synthetic_fixtures),
        "syntheticFixtures": synthetic_fixtures,
        "problems": problems,
    }
    return report


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments for the repository auditor."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path)
    return parser.parse_args()


def main() -> int:
    """Run the repository audit and return zero only when every check passes."""
    args = parse_args()
    try:
        report = audit()
    except (OSError, RuntimeError, ValueError, KeyError, json.JSONDecodeError) as exc:
        print(f"FAIL {exc}")
        return 1
    text = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text, encoding="utf-8")
    if report["passed"]:
        print("PASS no secrets, customer-file hashes, unverified binaries, or proprietary assets are tracked")
    else:
        print("FAIL repository audit found policy violations")
    print(text, end="")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
