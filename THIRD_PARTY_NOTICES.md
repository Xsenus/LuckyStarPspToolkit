# Third-party and interoperability notices

## Scope of this notice

This repository is an engineering preview. A **rights review** is still required before public or commercial redistribution of all inherited components. The presence of a MIT LICENSE does not relicense third-party material or certify a clean-room development process.

## RyououGakuenToolkit and the VWF profile

Earlier research used TimepieceMaster/RyououGakuenToolkit, in particular the 133-word RGO VWF instruction table, format layouts, and patch locations at reference commit e734977fc55b39e30a5fbf2b3e52e12443b737b2. Relevant path: `apps/RGT_RGO_Patch_Builder/hdr/scripts/variable_width_font_patches.h`.

The C# RgoVwfPatchProfile and the JSON profile preserve replacement instruction sequences matching those researched materials. Rewriting a table in C# is not by itself proof of independent authorship or permission. The previously stated unconditional "not copied / independent / clean-room" assurances were not established by the available evidence and are withdrawn. No upstream LICENSE/permission record has been supplied with this project. Review provenance, obtain permission where required, or replace affected material before public redistribution. This is an explicit unresolved release-review item, not a technical build failure.

## PPSSPP and libkirk

PRX/KIRK behavior and known constants were cross-checked against PPSSPP/libkirk. Those projects retain their own notices and licenses. No claim is made that every historical derivation of this repository has been legally audited. See the reference sources in `docs/SOURCES.md`.

## Runtime dependencies

The CLI uses the .NET base class library and has no third-party runtime NuGet dependency. Self-contained packages include Microsoft's runtime components with their distribution notices. SDK Roslyn assemblies are used only by the development documentation auditor. Python libraries in validation/requirements.txt are separate development tools.

## Game/platform data and fonts

Lucky Star and PlayStation names identify interoperability targets. No original EBOOT, ISO, CPK, SFO, PMF, artwork, game translation, firmware, font file or proprietary SDK is distributed in this source snapshot. Synthetic fixtures are recreated locally from code; generated binary font/ISO fixtures are not committed. Users must provide their own authorized source material. Game assets and font licenses are separate from this toolkit's LICENSE.
