# Technical reference sources

Primary references checked while preparing the release process (21 September 2026):

- Microsoft, `dotnet publish`: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish
- Microsoft, single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- GitHub, licensing a repository: https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository

Historical interoperability references; inclusion here is attribution, not a permission grant:

- https://github.com/TimepieceMaster/RyououGakuenToolkit/tree/e734977fc55b39e30a5fbf2b3e52e12443b737b2
- RGO character-index table for ULJM05752: https://github.com/TimepieceMaster/RyououGakuenToolkit/blob/e734977fc55b39e30a5fbf2b3e52e12443b737b2/apps/RGOScriptExtractor/resources/rgo_font_strings.txt (3543 lines; original UTF-8/LF SHA-256 `d6bbd07170dce78f34ee363e7d910d7a912886e83ab4851cb78f54cf66513d89`; no upstream license found at this revision).
- https://github.com/hrydgard/ppsspp/tree/master/Core/ELF
- https://github.com/hrydgard/ppsspp/tree/master/ext/libkirk

For the 0.20.0 NIM PRX interoperability check, the Type-2 tag `0xD9160BF0`
and its 16-byte seed were cross-checked against PPSSPP's
[`PrxDecrypter.cpp`](https://github.com/hrydgard/ppsspp/blob/master/Core/ELF/PrxDecrypter.cpp).
The decryption implementation uses this repository's existing KIRK path; no
PPSSPP source routine was copied for this release.

Local evidence in `validation/customer-validation-0.8.0.json` is preserved from an earlier iteration. The original private archive was not available for a fresh byte-level run in 0.10.0.

## 0.11.0 verification references

- AES-CMAC specification and public known-answer examples: https://www.rfc-editor.org/rfc/rfc4493
- Incremental transform API: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.icryptotransform.transformblock?view=net-9.0
- Target SDK download: https://dotnet.microsoft.com/en-us/download/dotnet/9.0

The 81 additional boundary vectors were calculated with Python cryptography from
synthetic byte patterns, not copied from game data. Measurement values are local
observations, not statements from these external sources.
