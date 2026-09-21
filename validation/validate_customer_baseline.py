#!/usr/bin/env python3
"""Validate the preserved customer-data evidence against current release profiles.

This validator deliberately does not pretend to reprocess the private customer archive.
It proves that the checked-in, previously generated evidence still agrees with the
current revision profile, the current VWF JSON profile and all recorded invariants.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
CUSTOMER_BASELINE = ROOT / "validation/customer-validation-0.8.0.json"
VWF_BASELINE = ROOT / "validation/vwf-patch-validation-0.8.0.json"
REVISION_PROFILE = ROOT / "profiles/rgo-uljm05752.json"
VWF_PROFILE = ROOT / "profiles/rgo-uljm05752-vwf-profile.json"


def sha256_file(path: Path) -> str:
    """Return the lowercase SHA-256 digest of one file."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_json(path: Path) -> dict[str, Any]:
    """Load a UTF-8 JSON object and reject non-object roots."""
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return value


def require(condition: bool, message: str) -> None:
    """Raise a validation error when a required invariant is false."""
    if not condition:
        raise ValueError(message)


def validate() -> dict[str, Any]:
    """Validate all preserved customer and VWF baseline invariants."""
    customer = load_json(CUSTOMER_BASELINE)
    vwf = load_json(VWF_BASELINE)
    revision = load_json(REVISION_PROFILE)
    current_vwf = load_json(VWF_PROFILE)

    require(customer.get("schema") == "lucky-star-psp.customer-validation.v2", "unexpected customer baseline schema")
    require(vwf.get("schema") == "lucky-star-psp.rgo-eboot-vwf-validation.v1", "unexpected VWF baseline schema")
    require(vwf.get("passed") is True, "preserved VWF baseline is not marked passed")

    conclusions = customer["customer_conclusions"]
    eboot = customer["eboot"]
    require(conclusions["game_disc_id"] == revision["discId"] == current_vwf["discId"], "DISC_ID mismatch")
    require(eboot["encrypted_sha256"] == revision["encryptedEbootSha256"], "encrypted EBOOT hash mismatch")
    require(eboot["decrypted_sha256"] == revision["decryptedEbootSha256"], "decrypted EBOOT hash mismatch")
    require(current_vwf["input"]["sha256"] == revision["decryptedEbootSha256"], "VWF input hash mismatch")
    require(vwf["decryptedInput"]["sha256"] == revision["decryptedEbootSha256"], "baseline VWF input hash mismatch")
    require(vwf["customerEncryptedSha256"] == revision["encryptedEbootSha256"], "baseline encrypted hash mismatch")
    require(vwf["profileId"] == current_vwf["profileId"], "VWF profile ID mismatch")
    require(vwf["profile"]["sha256"] == sha256_file(VWF_PROFILE), "VWF profile file changed since customer validation")
    require(vwf["fullPatch"]["sha256"] == current_vwf["fullOutput"]["sha256"], "full VWF output hash mismatch")
    require(vwf["profile"]["patchCount"] == len(current_vwf["patches"]) == 133, "VWF patch count mismatch")
    require(vwf["profile"]["csharpProfileMatches"] is True, "preserved C# profile comparison failed")

    crypto = eboot["crypto"]
    require(all(crypto[name] == "passed" for name in ("type2_sha1", "kirk_header_cmac", "kirk_payload_cmac")), "customer cryptographic checks were not all passed")
    require(eboot["script_table_consistent"] is True, "customer scenario table baseline is inconsistent")
    require(eboot["all_patch_points_match"] is True, "customer patch-point baseline is inconsistent")
    require(eboot["tamper_rejected"] is True, "customer negative tamper test was not recorded as rejected")
    require(conclusions["zero_boot_detected"] is True, "zero-filled BOOT.BIN baseline changed")
    require(conclusions["sc_cpk_present"] is False and conclusions["lt_bin_present"] is False, "baseline resource-presence conclusion changed")

    slots = eboot["script_slots"]
    require([item["id"] for item in slots] == revision["scriptIds"], "scenario IDs differ from the current revision profile")
    cumulative = 0
    for item in slots:
        require(item["blocks_2k"] > 0, f"scenario {item['id']} has a zero block count")
        cumulative += item["blocks_2k"]
        require(item["cumulative_blocks_2k"] == cumulative, f"scenario {item['id']} cumulative value mismatch")
        require(item["size_bytes"] == item["blocks_2k"] * 2048, f"scenario {item['id']} byte size mismatch")
        require(item["cumulative_valid"] is True, f"scenario {item['id']} was not validated")

    require(vwf["fullPatch"]["idempotent"] is True, "VWF idempotence baseline failed")
    require(vwf["fullPatch"]["unexpectedChangedBytes"] == 0, "VWF baseline changed bytes outside the profile")
    require(vwf["partialPatch"]["completionMatchesFullPatch"] is True, "partial-to-full VWF baseline failed")
    require(vwf["adjustedScriptTable"]["tableConsistent"] is True, "size-table baseline failed")
    require(vwf["expandedScriptHeap"]["canonicalLineageAfter"] is True, "expanded-heap lineage baseline failed")
    require(vwf["negativeTests"]["patchWordTamperRejected"] is True, "patch-word tamper baseline failed")
    require(vwf["negativeTests"]["unrelatedTamperRejectedByLineage"] is True, "unrelated-byte tamper baseline failed")

    return {
        "schema": "lucky-star-psp.customer-baseline-consistency.v1",
        "passed": True,
        "rawCustomerArchiveReprocessed": False,
        "evidence": {
            "customerBaseline": str(CUSTOMER_BASELINE.relative_to(ROOT)),
            "customerBaselineSha256": sha256_file(CUSTOMER_BASELINE),
            "vwfBaseline": str(VWF_BASELINE.relative_to(ROOT)),
            "vwfBaselineSha256": sha256_file(VWF_BASELINE),
        },
        "revision": {
            "discId": revision["discId"],
            "encryptedEbootSha256": revision["encryptedEbootSha256"],
            "decryptedEbootSha256": revision["decryptedEbootSha256"],
            "scenarioCount": len(slots),
            "finalCumulativeBlocks2K": cumulative,
        },
        "vwf": {
            "profileId": current_vwf["profileId"],
            "profileSha256": sha256_file(VWF_PROFILE),
            "patchCount": len(current_vwf["patches"]),
            "fullOutputSha256": current_vwf["fullOutput"]["sha256"],
        },
        "note": "This check validates preserved evidence and current profiles; it does not replace a fresh run against the private archive.",
    }


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments for the baseline validator."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path)
    return parser.parse_args()


def main() -> int:
    """Run validation, optionally write JSON and return a process exit code."""
    args = parse_args()
    try:
        report = validate()
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f"FAIL {exc}")
        return 1
    text = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text, encoding="utf-8")
    print("PASS preserved customer-data evidence remains consistent with current profiles")
    print(text, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
