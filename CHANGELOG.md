# Changelog

All notable user-visible changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). Every change lands under `## [Unreleased]`; the entries
move under a version heading in the commit that is tagged, and the release workflow uses that
section as the GitHub Release notes.

## [Unreleased]

### Changed

- The CLI runs on .NET 10: install the .NET 10 SDK or runtime before updating. .NET 9 support ends
  in November 2026, and Umbraco 17 sites already run on .NET 10.
- The tool's assembly is now `Umbraco.Community.Cli.dll`, matching the package name. The package id
  (`Umbraco.Community.Cli`) and the command (`umbraco`) are unchanged; only something that loads the
  assembly by file name needs the new name.
- Each release also publishes a symbols package (`.snupkg`) to nuget.org, and its GitHub Release
  notes come from this file.

## Earlier releases

The releases up to and including `0.1.0-rc.5` predate this file. Their notes are on the
[GitHub Releases](https://github.com/worm-brain/Umbraco.Cli/releases) page, generated from the
merged pull requests.

Known gaps, kept as they are rather than back-filled:

- `v0.1.0-alpha.7` and `v0.1.0-rc.2` are tags with no GitHub Release and no package on nuget.org.
- `v0.1.0-rc.1` has a GitHub Release with the `.nupkg` attached, but was never published to
  nuget.org.
