# AGENTS.md

## Build Output Rule
- Always build/publish artifacts to:
  - `C:\Users\Jason Cormier\AI Projects\Soundboard\Build`

## Release Packaging Rule
- When asked for a new release:
  - Package the contents of the Build output into a `.zip`.
  - Place the release zip in:
    - `C:\Users\Jason Cormier\AI Projects\Soundboard\releases`

## Changelog Rule
- Maintain a running changelog for all changes.
- Include the current running changelog with every release.
- After each release is created, reset the running changelog for the next release cycle.
- Include the changelog content in the GitHub Release notes when publishing a release.
