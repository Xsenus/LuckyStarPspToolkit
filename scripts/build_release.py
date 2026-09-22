#!/usr/bin/env python3
"""Build, test, smoke-test and package runtime-specific releases; any failed step stops publication.

Requires Python 3.11+, the .NET 9 SDK, and validation/requirements.txt.
No external command is executed through a shell. A failed run never replaces a
previous release directory. Cross-published binaries are labelled as not run on
that operating system; the CI matrix provides the separate native checks.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SUPPORTED_RIDS = ("win-x64", "linux-x64")
CLI_PROJECT = "src/LuckyStarPspToolkit.Cli/LuckyStarPspToolkit.Cli.csproj"


class BuildError(RuntimeError):
    """A failed prerequisite or build command; publishing must stop immediately."""


def native_rid() -> str | None:
    """Return the supported host RID, without claiming that cross-target binaries can run here."""
    if platform.machine().lower() not in {"amd64", "x86_64"}:
        return None
    return {"Windows": "win-x64", "Linux": "linux-x64"}.get(platform.system())


def parse_rids(value: str) -> list[str]:
    """Validate and deduplicate requested release targets, preserving the caller's order."""
    result = list(dict.fromkeys(value.split(",")))
    if not result or any(rid not in SUPPORTED_RIDS for rid in result):
        raise argparse.ArgumentTypeError("Use win-x64, linux-x64, or win-x64,linux-x64.")
    return result


def digest(path: Path) -> str:
    """Stream a file into SHA-256; never load large release archives into memory."""
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def ensure_regular_tree(path: Path) -> None:
    """Reject symlinks and Windows reparse points in an output path and existing ancestors."""
    for entry in (path, *path.parents):
        if entry.is_symlink() or (hasattr(entry, "is_junction") and entry.is_junction()):
            raise BuildError(f"Unsafe output link: {entry}")
        if entry.exists():
            attributes = getattr(entry.lstat(), "st_file_attributes", 0)
            if attributes & 0x400:
                raise BuildError(f"Unsafe output reparse point: {entry}")


def write_json(path: Path, value: object) -> None:
    """Write UTF-8 JSON through a neighbouring temporary file and replace on success."""
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def package_directory(directory: Path, output: Path) -> None:
    """Create a sorted ZIP with stable timestamps and retain Unix executable permissions."""
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(directory.rglob("*")):
            ensure_regular_tree(path)
            if not path.is_file():
                continue
            relative = path.relative_to(directory).as_posix()
            info = zipfile.ZipInfo(relative, (2020, 1, 1, 0, 0, 0))
            info.create_system = 3
            executable = path.name == "lsptool" or path.suffix == ".sh" or (os.name != "nt" and bool(path.stat().st_mode & 0o111))
            info.external_attr = (0o100755 if executable else 0o100644) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            with path.open("rb") as source, archive.open(info, "w", force_zip64=True) as target:
                shutil.copyfileobj(source, target, 1024 * 1024)
    with zipfile.ZipFile(output) as archive:
        bad = archive.testzip()
        if bad is not None:
            raise BuildError(f"ZIP integrity check failed: {bad}")


def promote_directory(staging: Path, destination: Path) -> None:
    """Promote a complete release, restoring the old directory if the final rename fails.

    This is rollback-capable rather than crash-atomic across two renames. The
    staging and destination must reside on the same filesystem.
    """
    ensure_regular_tree(destination)
    backup = destination.with_name(destination.name + ".previous-" + uuid.uuid4().hex)
    moved = False
    try:
        if destination.exists():
            destination.rename(backup)
            moved = True
        staging.rename(destination)
    except OSError:
        if moved and not destination.exists():
            backup.rename(destination)
        raise
    if moved:
        # Cleanup failure does not undo a successfully validated new release.
        try:
            shutil.rmtree(backup)
        except OSError as exc:
            print(f"WARNING: retained previous release at {backup}: {exc}", file=sys.stderr)


class CommandRunner:
    """Execute checked subprocesses and record command, exit status, and captured log for each step."""

    def __init__(self, root: Path, logs: Path) -> None:
        """Initialize the fixed repository working directory and per-run log directory."""
        self.root = root
        self.logs = logs
        self.steps: list[dict[str, object]] = []

    def run(self, name: str, command: list[str]) -> str:
        """Run one command synchronously; raise BuildError on failure or a 30-minute hard timeout."""
        self.logs.mkdir(parents=True, exist_ok=True)
        log = self.logs / f"{len(self.steps) + 1:02d}-{name}.log"
        environment = {**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"}
        print(f"[{name}]", flush=True)
        try:
            process = subprocess.run(command, cwd=self.root, env=environment, text=True,
                                     encoding="utf-8", errors="replace", stdout=subprocess.PIPE,
                                     stderr=subprocess.STDOUT, timeout=1800, check=False)
            text, code = process.stdout, process.returncode
        except (OSError, subprocess.TimeoutExpired) as exc:
            text, code = str(exc), -1
        log.write_text(text, encoding="utf-8")
        self.steps.append({"name": name, "command": command, "exitCode": code,
                           "log": log.name, "passed": code == 0})
        if code != 0:
            raise BuildError(f"Step {name} failed ({code}). See {log}.\n{text[-4000:]}")
        return text


def publish_command(dotnet: str, rid: str, output: Path, trust_file: Path | None = None) -> list[str]:
    """Build a RID-specific publish command with implicit restore enabled for runtime packs."""
    command = [dotnet, "publish", CLI_PROJECT, "-c", "Release", "-r", rid,
            "--self-contained", "true", "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:PublishTrimmed=false",
            "--nologo", "-o", str(output)]
    if trust_file is not None:
        command.append("-p:LicenseTrustFile=" + str(trust_file))
    return command


def build_release(root: Path, rids: list[str], dotnet: str, *, compile_only: bool = False, license_trust: Path | None = None) -> Path:
    """Validate sources, compile, test and publish a complete release or leave the prior release untouched."""
    version = (root / "VERSION").read_text(encoding="utf-8").strip()
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise BuildError("VERSION must be a numeric major.minor.patch value.")
    artifacts = root / "artifacts"
    ensure_regular_tree(artifacts)
    run_id = uuid.uuid4().hex
    logs = artifacts / "build-logs" / run_id
    runner = CommandRunner(root, logs)
    staging = artifacts / (".release-" + run_id)
    destination = artifacts / "releases" / version
    report: dict[str, object] = {"schema": "lsptool.release-build.v1", "version": version,
        "status": "running", "compiled": False, "testsPassed": False,
        "gameRuntimeVerified": False, "targets": [], "steps": runner.steps}
    try:
        embedded_trust = None
        profile = None
        if license_trust is not None:
            from license_build_profile import validate_public_profile
            ensure_regular_tree(license_trust)
            if license_trust.stat().st_size > 8192:
                raise BuildError("Public license trust is too large")
            data = license_trust.read_bytes()
            profile = validate_public_profile(data)
            logs.mkdir(parents=True, exist_ok=True)
            embedded_trust = logs / "customer-public-trust.json"
            embedded_trust.write_bytes(data)  # Public snapshot only; no runtime override exists.
            report["licenseTrustSha256"] = digest(embedded_trust)
            report["licenseIssuer"] = profile["issuer"]
        report["licenseConfigured"] = profile is not None
        sdk = runner.run("sdk", [dotnet, "--version"]).strip()
        if not re.fullmatch(r"9\.\d+\.\d+", sdk):
            raise BuildError(f"This branch requires a stable .NET 9 SDK; selected: {sdk}")
        report["sdk"] = sdk
        checks = [
            ("documentation", ["scripts/document_csharp.py", "--check", "--include-tests"]),
            ("member-docs", ["scripts/document_members.py", "--check"]),
            ("xml-docs", ["validation/validate_csharp_docs.py"]),
            ("api-reference", ["scripts/generate_api_reference.py", "--check"]),
            ("fixtures", ["scripts/reference_oracle.py"]),
            ("static", ["validation/static_validate.py"]),
            ("repository-audit", ["validation/audit_repository.py"]),
            ("baseline", ["validation/validate_customer_baseline.py"]),
            ("font-oracle", ["validation/validate_font_fixtures.py"]),
            ("iso-oracle", ["validation/validate_iso_fixture.py"]),
            ("iso-rebuild-oracle", ["validation/validate_iso_rebuild.py"]),
            ("release-regressions", ["-m", "unittest", "discover", "-s", "validation", "-p", "test_release_pipeline.py", "-v"]),
        ]
        for name, command in checks:
            runner.run(name, [sys.executable, *command])
        runner.run("restore", [dotnet, "restore", "LuckyStarPspToolkit.sln", "--nologo"])
        runner.run("compile", [dotnet, "build", "LuckyStarPspToolkit.sln", "-c", "Release", "--no-restore", "--nologo"])
        report["compiled"] = True
        for name, project in [
            ("core-tests", "tests/LuckyStarPspToolkit.SelfTests"),
            ("formats-tests", "tests/LuckyStarPspToolkit.Formats.SelfTests"),
            ("licensing-tests", "tests/LuckyStarPspToolkit.Licensing.SelfTests"),
            ("roslyn-docs", "tools/LuckyStarPspToolkit.Documentation"),
        ]:
            arguments = [str(root)] if name == "roslyn-docs" else []
            runner.run(name, [dotnet, "run", "--project", project, "-c", "Release", "--no-build", "--", *arguments])
        runner.run("cli-license-gate", [sys.executable, "scripts/check_locked_cli.py", "--dotnet", dotnet,
            "--cli", str(root / "src/LuckyStarPspToolkit.Cli/bin/Release/net9.0/lsptool.dll")])
        runner.run("licensing-process-integration", [sys.executable, "scripts/license_integration.py", "--dotnet", dotnet])
        report["testsPassed"] = True
        if compile_only:
            report["status"] = "compiled-and-tested"
            return logs
        staging.mkdir(parents=True)
        targets: list[dict[str, object]] = []
        for rid in rids:
            folder = staging / rid
            runner.run("publish-" + rid, publish_command(dotnet, rid, folder, embedded_trust))
            executable = folder / ("lsptool.exe" if rid == "win-x64" else "lsptool")
            if not executable.is_file() or executable.stat().st_size < 4:
                raise BuildError(f"Expected published executable missing: {executable}")
            expected = b"MZ" if rid == "win-x64" else b"\x7fELF"
            with executable.open("rb") as source:
                if source.read(len(expected)) != expected:
                    raise BuildError(f"Invalid executable signature: {executable}")
            tested = rid == native_rid()
            if tested:
                actual = runner.run("version-" + rid, [str(executable), "version"]).strip()
                if actual != version:
                    raise BuildError(f"Published version mismatch: {actual} != {version}")
                if profile is None:
                    runner.run("license-gate-" + rid, [sys.executable, "scripts/check_locked_cli.py", "--cli", str(executable)])
                if profile is not None:
                    actual_profile = json.loads(runner.run("embedded-issuer-" + rid, [str(executable), "license", "build-info"]))
                    if actual_profile["issuer"] != profile["issuer"] or actual_profile["developmentLoopback"] is not False:
                        raise BuildError("Published customer executable has the wrong licensing trust")
            else:
                print(f"NOTICE: {rid} is cross-published, not executed on this host.")
            for name in ("README.md", "VERSION", "LICENSE", "THIRD_PARTY_NOTICES.md"):
                shutil.copy2(root / name, folder / name)
            (folder / "docs").mkdir(exist_ok=True)
            # Customer packages deliberately omit server/admin sources and owner operational documentation.
            for doc in ("LICENSE_CUSTOMER_RU.md", "USAGE_RU.md", "CLI_REFERENCE.md", "TROUBLESHOOTING.md"):
                if (root / "docs" / doc).is_file():
                    shutil.copy2(root / "docs" / doc, folder / "docs" / doc)
            shutil.copytree(root / "profiles", folder / "profiles", dirs_exist_ok=True)
            if any("Authority" in item.name or "LicenseAdmin" in item.name or "license-admin" in item.name or "license-server" in item.name
                   for item in folder.rglob("*")):
                raise BuildError("Owner-only component leaked into customer package")
            metadata = {"version": version, "runtime": rid, "selfContained": True,
                        "licenseConfigured": profile is not None,
                        "licenseIssuer": profile["issuer"] if profile else None,
                        "requiresOnlineLicense": True,
                        "nativeSmokePassed": tested, "gameRuntimeVerified": False,
                        "acceptance": "engineering-preview; not a completed game translation"}
            write_json(folder / "BUILD-STATUS.json", metadata)
            archive = staging / f"LuckyStarPspToolkit-{version}-{rid}.zip"
            package_directory(folder, archive)
            checksum = digest(archive)
            archive.with_suffix(".zip.sha256").write_text(f"{checksum}  {archive.name}\n", encoding="ascii")
            targets.append({**metadata, "archive": archive.name, "sha256": checksum})
        report["targets"] = targets
        report["status"] = "engineering-preview-built"
        write_json(staging / "build-report.json", report)
        destination.parent.mkdir(parents=True, exist_ok=True)
        promote_directory(staging, destination)
        print(f"Release packages: {destination}")
        return destination
    except (BuildError, OSError, ValueError) as exc:
        report["status"] = "failed"
        report["error"] = str(exc)
        raise
    finally:
        write_json(logs / "build-report.json", report)
        if staging.exists():
            shutil.rmtree(staging)


def main() -> int:
    """Parse release options and return a nonzero exit code for any missing prerequisite or failed check."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rids", type=parse_rids, default=list(SUPPORTED_RIDS))
    parser.add_argument("--compile-only", action="store_true", help="Compile and test, but do not publish binaries.")
    parser.add_argument("--license-trust", type=Path, help="Public production client-trust.json from your issuer; absent means a LOCKED unconfigured preview.")
    args = parser.parse_args()
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        print("ERROR: .NET 9 SDK not found. No binary release was created. Install the SDK, reopen the terminal, then rerun.", file=sys.stderr)
        return 2
    try:
        build_release(ROOT, args.rids, dotnet, compile_only=args.compile_only, license_trust=args.license_trust)
        return 0
    except (BuildError, OSError, ValueError) as exc:
        print(f"BUILD FAILED: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
