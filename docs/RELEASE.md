# Release procedure — 0.10.0

The authoritative release entry point is `scripts/build_release.py`; `build.cmd`, `build.sh`, and `Build.proj` delegate to it. See BUILD_AND_RELEASE_RU.md.

A source handoff is NOT a binary release. A binary package is accepted only after compilation, both C# self-test projects, compiled Roslyn documentation audit, native CLI self-test and the synthetic demonstration succeed. A real translated game is a further acceptance stage.

For tagged CI, both native platform jobs must pass. Only then does the workflow create a draft prerelease. Review THIRD_PARTY_NOTICES.md and the game-acceptance matrix before public publication. A rerun must not erase a previous working package on failure.

When packaging source, include committed source, documentation and explicit reports, but never private games, font files, generated binaries, tokens or compiler artifacts. Generated synthetic fixtures are recreated by the reference script rather than copied into the handoff. A clean snapshot Git bundle may start a publication history; do not label it the complete previous development history.
