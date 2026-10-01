## Orchestration preference

- Delivery priority: speed

## Language

- Everything in this repository is written in English: code, comments, UI text, docs, `release-notes.md`, `ERRORS.md`, commit messages, PR descriptions, and GitHub release titles and notes.
- This holds even when the conversation with the maintainer is in another language.

## Releases

- There is no CI release workflow. Releases are published manually: `release/x.y.z` branch → PR → squash merge into `main` → `installer\build-installer.ps1` → `gh release create vX.Y.Z` on `main` with `artifacts\installer\SmoothMice_Setup_X.Y.Z.exe` attached.
- Release notes are user-facing English, summarizing the version's section in `release-notes.md`.
