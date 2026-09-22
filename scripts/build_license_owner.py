#!/usr/bin/env python3
"""Publish server and issuer tools in an OWNER-ONLY package, separately from customer binaries.

The normal checked solution pipeline runs first. No default signing key, token,
passphrase, database or client entitlement is ever created by a build.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import shutil
import sys
import uuid
from build_release import (BuildError, CommandRunner, build_release, digest, ensure_regular_tree,
                           native_rid, package_directory, parse_rids, promote_directory, write_json)

ROOT = Path(__file__).resolve().parents[1]


def publish_owner(rids: list[str], dotnet: str) -> Path:
    """Run checked compilation/integration, then publish only the selected owner runtimes."""
    build_release(ROOT, rids, dotnet, compile_only=True)
    version = (ROOT / "VERSION").read_text().strip()
    base = ROOT / "artifacts"
    run = uuid.uuid4().hex
    temporary = base / (".owner-release-" + run)
    target = base / "owner-releases" / version
    ensure_regular_tree(target)
    runner = CommandRunner(ROOT, base / "build-logs" / ("owner-" + run))
    report = {"version": version, "ownerOnly": True, "containsCredentials": False, "steps": runner.steps, "status": "running"}
    try:
        temporary.mkdir(parents=True)
        for rid in rids:
            directory = temporary / rid
            for project, executable_name, subdir in (
                ("LuckyStarPspToolkit.LicenseServer", "lsp-license-server", "server"),
                ("LuckyStarPspToolkit.LicenseAdmin", "lsp-license-admin", "admin"),
            ):
                output = directory / subdir
                runner.run("publish-" + rid + "-" + subdir, [dotnet, "publish", "src/" + project,
                    "-c", "Release", "-r", rid, "--self-contained", "true", "--nologo",
                    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
                    "-p:PublishTrimmed=false", "-o", str(output)])
                executable = output / (executable_name + (".exe" if rid.startswith("win-") else ""))
                if not executable.is_file():
                    raise BuildError("Owner executable was not published")
                if rid == native_rid():
                    runner.run("owner-help-" + rid + "-" + subdir, [str(executable), "--help"])
            (directory / "OWNER_ONLY.txt").write_text("OWNER ONLY. Never send this package, issuer state or its passphrase to customers.\n", encoding="utf-8")
            (directory / "docs").mkdir(exist_ok=True)
            for doc in (ROOT / "docs").glob("LICENSE*.md"):
                shutil.copy2(doc, directory / "docs" / doc.name)
            shutil.copytree(ROOT / "deploy/licensing", directory / "deploy")
            archive = temporary / f"LuckyStarPspToolkit-OWNER-{version}-{rid}.zip"
            package_directory(directory, archive)
            archive.with_suffix(".zip.sha256").write_text(digest(archive) + "  " + archive.name + "\n", encoding="ascii")
        report["status"] = "owner-packages-built"
        write_json(temporary / "build-report.json", report)
        target.parent.mkdir(parents=True, exist_ok=True)
        promote_directory(temporary, target)
        return target
    finally:
        write_json(runner.logs / "owner-build-report.json", report)
        if temporary.exists():
            shutil.rmtree(temporary)


def main() -> int:
    """Select runtimes and fail loudly if the stable target SDK or any real check is unavailable."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rids", type=parse_rids, default=["win-x64", "linux-x64"])
    args = parser.parse_args()
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        print(".NET 9 SDK is required. No owner binaries were created.", file=sys.stderr)
        return 2
    try:
        print("OWNER package: " + str(publish_owner(args.rids, dotnet)))
        return 0
    except (BuildError, OSError, ValueError) as exc:
        print("OWNER BUILD FAILED: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
