# Glossary

Project-specific terms. Add entries as concepts are introduced.

- **Contract test** — a fast unit test that asserts the set of Management API endpoints
  the CLI depends on exists (path + verb) in the committed OpenAPI document
  (`spec/management.json`). Guards against endpoint drift without a live instance. See
  [ADR 0001](adr/0001-contract-test-approach.md) and issue #52.
- **Endpoint drift** — when the real Umbraco Management API changes (endpoint moved,
  renamed or removed) such that a URL the CLI calls no longer exists. The original cause
  of the #39/#40/#44 bugs.
- **Hand-written path** — a Management API call in `UmbracoManagementClient` made directly
  through `HttpClient` with a URL string literal, as opposed to a Kiota-generated request
  builder. These are the drift-risk surface the contract test targets.
- **Generated client** — the Kiota-generated request builders and models under
  `src/Umbraco.Cli.Client/Generated`, produced from `spec/management.json` by
  `scripts/regen-client.ps1`.
