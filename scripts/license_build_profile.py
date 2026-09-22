#!/usr/bin/env python3
"""Validate public licensing trust before embedding it in a customer release; never accept private issuer material."""
from __future__ import annotations
import argparse
import base64
import json
from pathlib import Path
import re
from urllib.parse import urlsplit
import uuid

PRODUCT = "lucky-star-psp-toolkit"


def unique(pairs: list[tuple[str, object]]) -> dict[str, object]:
    """Reject duplicate and escaped-equivalent JSON property names."""
    result: dict[str, object] = {}
    for name, value in pairs:
        if name in result:
            raise ValueError("Duplicate trust property")
        result[name] = value
    return result


def validate_public_profile(data: bytes) -> dict[str, object]:
    """Validate a production-only, bounded HTTPS profile and canonical P-256 public keys.

    The customer package must contain public SPKI only. Test loopback profiles are
    accepted exclusively by isolated integration compilation, not this publisher.
    """
    if not 1 <= len(data) <= 8192:
        raise ValueError("Trust file must be between 1 and 8192 bytes")
    value = json.loads(data.decode("utf-8"), object_pairs_hook=unique)
    fields = {"schema", "productId", "issuer", "serverUrl", "publicKeys", "developmentLoopback"}
    if not isinstance(value, dict) or set(value) - fields or not (fields - {"developmentLoopback"}) <= set(value):
        raise ValueError("Unexpected trust properties; owner secrets must never enter a customer build")
    if type(value["schema"]) is not int or value["schema"] != 1 or value["productId"] != PRODUCT:
        raise ValueError("Wrong licensing schema or product")
    if value.get("developmentLoopback", False) is not False:
        raise ValueError("A loopback/development issuer is prohibited in production packages")
    if not isinstance(value["issuer"], str) or str(uuid.UUID(value["issuer"])) != value["issuer"]:
        raise ValueError("Issuer must be a canonical UUID")
    if not isinstance(value["serverUrl"], str) or len(value["serverUrl"]) > 2048:
        raise ValueError("Invalid HTTPS issuer URL")
    url = urlsplit(value["serverUrl"])
    if url.scheme != "https" or not url.hostname or url.username or url.password or url.query or url.fragment or url.path != "/":
        raise ValueError("Public authority URL must be an HTTPS origin ending in slash")
    _ = url.port  # Forces validation of malformed port numbers.
    keys = value["publicKeys"]
    if not isinstance(keys, dict) or not 1 <= len(keys) <= 3:
        raise ValueError("Expected one to three public signing keys")
    from cryptography.hazmat.primitives import serialization
    from cryptography.hazmat.primitives.asymmetric import ec
    for key_id, encoded in keys.items():
        if not isinstance(key_id, str) or not re.fullmatch(r"[0-9a-f]{16}", key_id) or not isinstance(encoded, str):
            raise ValueError("Invalid public-key ID or encoding")
        raw = base64.b64decode(encoded, validate=True)
        key = serialization.load_der_public_key(raw)
        if not isinstance(key, ec.EllipticCurvePublicKey) or not isinstance(key.curve, ec.SECP256R1):
            raise ValueError("Only ECDSA P-256 public SPKI is supported")
        if key.public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo) != raw:
            raise ValueError("Noncanonical public SPKI")
        import hashlib
        if hashlib.sha256(raw).hexdigest()[:16] != key_id:
            raise ValueError("Public-key ID does not match SPKI digest")
    return value


def main() -> int:
    """Validate a supplied public profile without echoing its contents or accepting credentials."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile", type=Path)
    args = parser.parse_args()
    from build_release import ensure_regular_tree
    ensure_regular_tree(args.profile)
    if args.profile.stat().st_size > 8192:
        raise ValueError("Public profile is too large")
    value = validate_public_profile(args.profile.read_bytes())
    print("PASS: production public trust, issuer=" + str(value["issuer"]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
