## Orchestration preference

- Delivery priority: speed

## Language

- Everything in this repository is written in English: code, comments, UI text, docs, `release-notes.md`, `ERRORS.md`, commit messages, PR descriptions, and GitHub release titles and notes.
- This holds even when the conversation with the maintainer is in another language.

## Releases

- There is no CI release workflow. Releases are published manually: `release/x.y.z` branch → PR → squash merge into `main` → `installer\build-installer.ps1` → `gh release create vX.Y.Z` on `main` with `artifacts\installer\SmoothMice_Setup_X.Y.Z.exe` attached.
- Release notes are user-facing English, summarizing the version's section in `release-notes.md`.
- SwissTechKnife module: bumping `version` in `src/SmoothMice.Module/module.json` on `main` runs `.github/workflows/module-release.yml`, which publishes `module-vX.Y.Z` (not marked latest) with `smooth-mice-X.Y.Z.zip`; the host's catalog sync picks it up on its own. Add the version's English notes to `releaseNotes` in that `module.json` in the same change.
