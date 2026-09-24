# Third-party and interoperability notices

## Scope of this notice

This repository is an engineering preview published with explicit attribution of inherited materials. The project's MIT LICENSE applies only to the contributors' own material to the extent they hold the relevant rights. It does not relicense third-party instruction tables, game/platform data or fonts, and does not certify a clean-room development process. The rights review of inherited material remains unresolved. Its provenance is identified below so recipients can assess their own intended use and distribution.

## RyououGakuenToolkit and the VWF profile

Earlier research used TimepieceMaster/RyououGakuenToolkit, in particular the 133-word RGO VWF instruction table, format layouts, and patch locations at reference commit e734977fc55b39e30a5fbf2b3e52e12443b737b2. Relevant path: `apps/RGT_RGO_Patch_Builder/hdr/scripts/variable_width_font_patches.h`.

The C# RgoVwfPatchProfile and the JSON profile preserve replacement instruction sequences matching those researched materials. Rewriting a table in C# is not by itself proof of independent authorship or permission. The previously stated unconditional "not copied / independent / clean-room" assurances were not established by the available evidence and are withdrawn. No upstream LICENSE/permission record has been supplied with this project. Review provenance, obtain permission where required, or replace affected material before public redistribution. This is an explicit unresolved release-review item, not a technical build failure.

The inherited replacement instruction table is **excluded from this repository's MIT grant**; no additional permission for that upstream material is asserted here. On 22 September 2026 a read-only check of the referenced Git tree found no LICENSE/COPYING/NOTICE file, and the repository API reported no license. This is a factual provenance record, not a legal determination that each instruction or table is protected or unprotected. See [publication audit](docs/PUBLICATION_AUDIT_RU.md).

## PPSSPP and libkirk

PRX/KIRK behavior and known constants were cross-checked against PPSSPP/libkirk. Those projects retain their own notices and licenses. No claim is made that every historical derivation of this repository has been legally audited. See the reference sources in `docs/SOURCES.md`.

The NIM Type-2 tag seed added in 0.20.0 was checked against PPSSPP's
`Core/ELF/PrxDecrypter.cpp`. It is used as interoperability data with the
existing local decryption implementation; no PPSSPP source routine was
transplanted in this change.

## Runtime dependencies

The CLI uses the .NET base class library and has no third-party runtime NuGet dependency. Self-contained packages include Microsoft's runtime components with their distribution notices. SDK Roslyn assemblies are used only by the development documentation auditor. Python libraries in validation/requirements.txt are separate development tools.

## Game/platform data and fonts

Lucky Star and PlayStation names identify interoperability targets. No original EBOOT, ISO, CPK, SFO, PMF, artwork, game translation, firmware, font file or proprietary SDK is distributed in this source snapshot. Synthetic fixtures are recreated locally from code; generated binary font/ISO fixtures are not committed. Users must provide their own authorized source material. Game assets and font licenses are separate from this toolkit's LICENSE.

## React browser production modules (0.16.0)

Four pinned modules (React, react-dom, react-dom/client, scheduler) from react/react official CI
oss-stable-semver artifact at commit 59aff3e18cb5b3a336c280bbfa57ec37999511b9 are included under MIT.
Exact artifact and file hashes are in web/license-admin/vendor-manifest.json; license text is retained
in vendor/react/LICENSE.txt and copied to production frontend THIRD_PARTY_LICENSES.txt.
This provenance does not assert byte identity to an npm release. No React Server Components are shipped.
