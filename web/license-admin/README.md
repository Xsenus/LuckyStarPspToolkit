# Owner React console

Owner-only UI served by LicenseWebServer. Runtime: pinned React browser production modules,
local CSS and handwritten ES modules; no CDN/eval, native addons, remote imports or npm hooks.

Build (Node22+): `node web/license-admin/test.mjs` then
`node web/license-admin/build.mjs` from repository root. Output: web/license-admin/dist.
The owner publisher includes it as web/. No Node process is needed on the deployed VPS.

This small no-JSX build intentionally uses no registry packages; vendor-manifest.json is the
integrity/provenance lock. Do not treat official CI oss-stable-semver contents as independently
verified npm release bytes. See ../../docs/ADMIN_WEB_RU.md and ../../docs/ADMIN_ARCHITECTURE_RU.md.

Native browser integration: install requirements-test.txt and Playwright Chromium, build the
.NET9 solution, run `python scripts/web_reserve_integration.py --output artifacts/browser-check`.
Only use --browser-bridge for explicitly labelled constrained-environment diagnostics; never
claim that it tests browser TLS, cookie enforcement, CSP enforcement or native file downloads.

One account, password + TOTP or one-use recovery. No bearer owner secret in JavaScript; cookie
HttpOnly and CSRF separate. No localStorage/sessionStorage persistence of credentials.
All irreversible/sensitive actions confirm and can require fresh MFA. Offline revocation is
bounded by the old signed deadline, never guaranteed instantaneous.
