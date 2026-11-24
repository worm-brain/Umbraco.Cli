# Changelog

All notable user-visible changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). Every change lands under `## [Unreleased]`; the entries
move under a version heading in the commit that is tagged, and the release workflow uses that
section as the GitHub Release notes.

## [Unreleased]

### Changed

- `create` and `update --json-body` on `document-type`, `media-type`, `member-type`, `data-type`
  and `template` now fail with `invalid_argument` when the body has a key its `--schema` does not
  define, at any depth, listing each key's JSON path, and send nothing. Umbraco used to drop such
  keys and the command reported success. Edited `get` output is still accepted.
  `--allow-unknown-keys` sends the body unchecked, for an Umbraco newer than the CLI's schema.
- `content create` / `update`, `media update` and `document-blueprint create` / `update` now also
  fail with `invalid_argument` on a `--json-body` key their `--schema` does not define, at any
  depth, listing each key's JSON path, and send nothing. The CLI used to drop such keys while
  reporting success. Edited `get` output, a blueprint `scaffold` and `content create --example`
  output are still accepted. On these commands `--allow-unknown-keys` ignores the unknown keys
  instead of failing; it cannot send them. Their `--schema` now marks every object
  `additionalProperties: false`.
- The CLI runs on .NET 10: install the .NET 10 SDK or runtime before updating. .NET 9 support ends
  in November 2026, and Umbraco 17 sites already run on .NET 10.
- The tool's assembly is now `Umbraco.Community.Cli.dll`, matching the package name. The package id
  (`Umbraco.Community.Cli`) and the command (`umbraco`) are unchanged; only something that loads the
  assembly by file name needs the new name.
- Each release also publishes a symbols package (`.snupkg`) to nuget.org, and its GitHub Release
  notes come from this file.

### Fixed

- A wrong-typed field in `--json-body` on `content create` / `update`, `media update` and
  `document-blueprint create` / `update` is now reported by its path and the shape it takes
  (`contentType must be an object such as {"alias": "blogPost"} or {"id": "<guid>"}`), pointing
  at `--schema`, instead of a serializer message naming an internal CLI type. A missing required
  key (`documentType` on a blueprint) is reported the same way.

## Earlier releases

The releases up to and including `0.1.0-rc.5` predate this file. Their notes are on the
[GitHub Releases](https://github.com/worm-brain/Umbraco.Cli/releases) page, generated from the
merged pull requests.

Known gaps, kept as they are rather than back-filled:

- `v0.1.0-alpha.7` and `v0.1.0-rc.2` are tags with no GitHub Release and no package on nuget.org.
- `v0.1.0-rc.1` has a GitHub Release with the `.nupkg` attached, but was never published to
  nuget.org.
