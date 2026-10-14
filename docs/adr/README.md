# Architecture decision records

One file per decision, numbered in order, never renumbered. A superseded ADR stays in place and
says what replaced it in its status line.

Write a new ADR when a decision changes a contract, a dependency, the supported Umbraco range or
something an earlier ADR decided (see [AGENTS.md](../../AGENTS.md)).

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-contract-test-approach.md) | Contract-test the client against the committed OpenAPI document | Accepted |
| [0002](0002-integration-test-harness.md) | Live integration test harness | Accepted |
| [0003](0003-migrate-write-path-to-generated-client.md) | Migrate the write path to the generated Kiota client | Accepted; superseded in part by 0004 |
| [0004](0004-content-media-write-path-implementation.md) | Content and media write-path migration: implementation decisions | Accepted |
| [0005](0005-schema-export-diff-apply.md) | Schema export, diff and apply pipeline | Accepted |
| [0006](0006-content-export-diff-apply.md) | Content export, diff and apply pipeline | Accepted |
| [0007](0007-command-allow-list-safety-model.md) | Command allow-list safety model | Accepted |
| [0008](0008-media-export-diff-apply.md) | Media export, diff and apply pipeline | Accepted |
| [0009](0009-package-declared-cli-capabilities.md) | Packages declare CLI support in their Umbraco manifest | Accepted |
| [0010](0010-external-extension-commands.md) | External extension commands and the guarded API passthrough | Accepted |
