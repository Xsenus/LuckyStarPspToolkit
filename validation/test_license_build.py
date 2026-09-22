"""Production licensing packaging regressions. Uses only freshly generated temporary public test keys."""
import base64
import hashlib
import json
from pathlib import Path
import sys
import unittest
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from license_build_profile import validate_public_profile
from build_release import publish_command


class LicenseBuildTests(unittest.TestCase):
    """Prevent test-mode/profile substitution and private-key leakage during customer publication."""

    def profile(self):
        """Return a fresh public P-256 issuer profile without a fixed valid credential."""
        from cryptography.hazmat.primitives.asymmetric import ec
        from cryptography.hazmat.primitives import serialization
        key = ec.generate_private_key(ec.SECP256R1())
        spki = key.public_key().public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
        return {"schema": 1, "productId": "lucky-star-psp-toolkit", "issuer": str(uuid.uuid4()),
                "serverUrl": "https://licenses.example/", "publicKeys": {hashlib.sha256(spki).hexdigest()[:16]: base64.b64encode(spki).decode()},
                "developmentLoopback": False}

    def test_public_only(self):
        """A canonical public production profile is accepted."""
        value = self.profile()
        self.assertEqual(value, validate_public_profile(json.dumps(value).encode()))

    def test_reject_development(self):
        """No local test issuer may become an automatic production package."""
        value = self.profile(); value["developmentLoopback"] = True
        with self.assertRaises(ValueError): validate_public_profile(json.dumps(value).encode())

    def test_reject_private_and_extra_fields(self):
        """A private server/owner file must not be embedded even if some public fields match."""
        for field in ("privateKey", "pepper", "token", "adminTokenHash"):
            value = self.profile(); value[field] = "not-a-real-secret"
            with self.assertRaises(ValueError): validate_public_profile(json.dumps(value).encode())

    def test_reject_wrong_urls(self):
        """HTTP, credentials, paths, query strings and fragments are disallowed."""
        for url in ("http://127.0.0.1/", "https://user:pass@example.com/", "https://example.com/admin", "https://example.com/?x=1", "https://example.com/#x"):
            value = self.profile(); value["serverUrl"] = url
            with self.assertRaises(ValueError): validate_public_profile(json.dumps(value).encode())

    def test_reject_duplicate_escaped(self):
        """JSON escape aliases are not allowed to override public profile properties."""
        with self.assertRaises(ValueError): validate_public_profile(b'{"schema":1,"\\u0073chema":2}')

    def test_reject_wrong_curve_and_key_id(self):
        """Invalid key encodings, curve or an unrelated key identifier fail before publish."""
        value = self.profile(); value["publicKeys"] = {"0" * 16: "AAAA"}
        with self.assertRaises(ValueError): validate_public_profile(json.dumps(value).encode())
        from cryptography.hazmat.primitives.asymmetric import ec
        from cryptography.hazmat.primitives import serialization
        raw = ec.generate_private_key(ec.SECP384R1()).public_key().public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
        value["publicKeys"] = {hashlib.sha256(raw).hexdigest()[:16]: base64.b64encode(raw).decode()}
        with self.assertRaises(ValueError): validate_public_profile(json.dumps(value).encode())

    def test_no_default_runtime_bypass(self):
        """Publishing adds a trust resource only when explicitly supplied, without disabling the gate."""
        clean = publish_command("dotnet", "win-x64", Path("out"))
        bound = publish_command("dotnet", "win-x64", Path("out"), Path("public.json"))
        self.assertFalse(any("LicenseTrustFile=" in x for x in clean))
        self.assertIn("-p:LicenseTrustFile=public.json", bound)
        self.assertFalse(any("DisableLicense" in x for x in bound))
