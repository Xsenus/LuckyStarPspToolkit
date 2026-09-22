# Licensing API v1

This is a fixed online authorization protocol, not an arbitrary JWT authentication service.
See C# XML documentation and `LuckyStarPspToolkit.Licensing/Contracts.cs` for exact models.
All request JSON is camel-case, case-sensitive, rejects duplicate/unknown fields and numeric strings.
Since 0.15, non-optional constructor fields must be present and non-nullable fields cannot be null.
Explicitly nullable fields and optional defaults keep their documented meaning. This is independent
of the private database schema migration.
Only the public protocol is exposed through HTTPS. No caller-supplied path, algorithm or private issuer key is accepted.

| Listener | Method/path | Authentication |
|---|---|---|
| Public 127.0.0.1:17840 | GET /health | Readiness only, no credentials |
| Public | POST /v1/challenge | Bounded issuance of one-use nonce |
| Public | POST /v1/authorize | Installation signature; access key on activate |
| Admin 127.0.0.1:17841 | GET /admin/licenses | Independent random Bearer token |
| Admin | GET /admin/audit | Same owner token |
| Admin | POST /admin/issue | Owner token plus idempotent issuance UUID |
| Admin | POST /admin/change | Owner token plus idempotent mutation UUID |

## Public exchange

Challenge request fields: `productId`, `action` (activate/check), `devicePublicKey` (canonical P-256 SPKI base64),
`hostBinding` (64 lower-case hexadecimal characters). Response: `challenge` (32 random bytes, unpadded base64url).

Authorization request fields: `productId`, `action`, `devicePublicKey`, `hostBinding`, `challenge`, `clientNonce`,
`licenseId`, `accessKey`, `proof`. Activation requires empty licenseId and a valid LSP- credential.
Check requires a license UUID and empty accessKey. Signature transcript is UTF-8 of newline-separated validated fields:
`LSP-POSSESSION-v1`, product, action, challenge, clientNonce, licenseId, SHA256(accessKey), public key, host digest.
None of these validated fields may inject a newline. Proof is 64-byte IEEE P1363 ECDSA-P256/SHA256, base64url.

Response: `{"token":"LSP1.key-id.payload.signature"}`. Signature covers ASCII `LSP1.key-id.payload`.
Key ID is the first 16 lower-case hex characters of SHA256(SPKI). Payload is UTF-8 JSON encoded base64url.
Claims include schema, issuer, productId, licenseId, deviceId, hostBinding, clientNonce, action, serverNow,
validUntil and nullable entitlementExpires. Claim equality and bounded exclusive expiry are checked only after signature validation.
Never accept `{"valid":true}` or an unsigned locally generated expiration date as permission.

## Owner issue

Fields: `id` (UUID for idempotency), `accessKey` (generated locally by LicenseAdmin), `label`, `unit`
(hours/days/years/permanent), `amount`, `starts` (issue/activation), `maxDevices`, nullable `activateBefore`.
The server stores the credential digest, never the plaintext. Exact UUID retry returns the existing entitlement;
changed semantics under the same UUID are rejected. Owner private saved request contains the original credential.

## Owner change

Fields: `requestId`, `licenseId`, `action`, `unit`, `amount`, `deviceId`.
Actions: suspend/resume/revoke/extend/permanent/reset-device. Only extend uses duration, only reset-device uses deviceId.
Irreversible revoke cannot be reset by another action. A retried extension request applies exactly once.

## Status/error policy

Errors have `code` and sanitized `message`. Input/path/method errors do not return stack traces.
Responses disable caching. Clients refuse success bodies with untrusted signatures or inconsistent claims.
HTTP 408/429/500/502/503/504 are transient even if a proxy returns no JSON or an HTML body.
They never grant access, extend a previous lease, or allow a fresh command offline. A malformed
HTTP 200 body remains a protocol failure. HTTP errors/network loss never extend a previous grant. Other explicit denials terminate the session.
Administrative tokens and access keys must not appear in URL query strings or request logs.
No CORS/browser integration or multi-tenant product administration is implemented.

## Additions in 0.16.0

POST /v1/reserve uses ReserveRefreshRequest {grantId, request}; request is a fresh proof for
action=check. Returns nonce-bound LSPR1 offline token, TTL<=604800 and <=parent expiry.
Separate loopback browser cookie API is documented in ADMIN_ARCHITECTURE_RU.md. It is not
a public exposure of /admin bearer routes.
