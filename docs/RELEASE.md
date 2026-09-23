# Release procedure — 0.19.1

The authoritative customer release entry point is `scripts/build_release.py`;
`build.cmd`, `build.sh`, and `Build.proj` delegate to it. See
[the build guide](BUILD_AND_RELEASE_RU.md) and
[verified 0.19.1 results](VERIFICATION_0.19.1_RU.md).

A binary package is accepted after compilation, all three C# self-test projects,
the compiled Roslyn documentation audit, license-gate checks and the isolated
process licensing lifecycle with a licensed synthetic demonstration succeed.
Only the host's matching RID can be executed locally; Windows/Linux CI runs both
platforms natively. A source handoff alone does not establish binary or gameplay
acceptance.

Without `--license-trust` the package is a locked unconfigured preview. Customer
packages require the owner's validated public `client-trust.json`; never embed
private issuer material. After publication, verify the intended issuer/HTTPS URL,
activation, native self-test and revocation on the deployed authority. These steps
were executed for 0.19.1; repeat them for changed issuers or new releases.

For tagged CI, both native platform jobs and archive digests must pass before
publication of a prerelease. Review [third-party notices](../THIRD_PARTY_NOTICES.md) and
[the acceptance matrix](CUSTOMER_ACCEPTANCE_RU.md). A failed build must preserve
previous working packages. Use a new patch version for corrections instead of
silently replacing a published tag.

Source is public; working authority state, owner credentials, access keys,
recovery codes and customer game/font material remain private. Source archives
contain committed code, documentation and explicit redacted reports. Generated
synthetic binary fixtures are recreated by the reference script, not committed.
Audit every reachable Git tree, including deleted files, before importing new
history. The supplied bundle begins with the clean 0.10.0 snapshot; it is not
proof about private development history preceding that snapshot.

Owner server/admin/web packages are built separately by
`scripts/build_license_owner.py` under `artifacts/owner-releases/0.19.1/`.
Customer packages are under `artifacts/releases/0.19.1/`. Owner packaging must
not include production credentials, and customer packaging must not include
owner components. Actual game translation and PSP compatibility remain a separate
acceptance stage requiring authorized original resources.
