# Test cases — Schema export / diff / apply (#68)

Living test-case list for the schema pipeline (ADR 0004). Tiered **E2E** (drive the built CLI
against a live instance), **Integration** (in-process, fake client), and **Manual** (needs human
judgement or a specially-shaped instance). Each case links to the automating test where one
exists.

## Acceptance criteria (from #68, first slice)

- AC1: Export document types, data types, and templates to a portable JSON snapshot.
- AC2: Diff a snapshot against a live instance (read-only).
- AC3: Apply a snapshot to a live instance (create + update; optional prune), safely.

## Cases

| ID | Tier | Step / Action | Expected result | Automated by |
|----|------|---------------|-----------------|--------------|
| TC-68-01 | E2E | `schema export --out f.json` against a live instance | Exit 0; file written; summary reports per-kind counts | `SchemaIntegrationTests.Export_WritesSnapshotFile` |
| TC-68-02 | E2E | Export, then `schema diff <that file>` against the same instance | Exit 0; empty diff (round-trip idempotency) | `SchemaIntegrationTests.ExportThenDiff_AgainstSameInstance_ShowsNoDrift` |
| TC-68-03 | E2E | Export, then `schema apply <that file> --dry-run` | Exit 0; empty plan; nothing written | `SchemaIntegrationTests.ExportThenApplyDryRun_AgainstSameInstance_PlansNothing` |
| TC-68-04 | Integration | Export against an instance with a folder-nested doc type | Snapshot includes the nested type; folder nodes are not fetched as entities | (client tree-walk; `SchemaExporterTests` + live TC-68-01) |
| TC-68-05 | Integration | Export where a per-entity read fails | Whole export fails (no partial snapshot) | `SchemaExporterTests.ExportAsync_FailsFast_WhenAPerEntityReadFails` |
| TC-68-06 | Integration | Raw get preserves lossy fields (properties, config `values`, Razor) | Verbatim body retained end to end | `SchemaClientRawTests.*`, `SchemaSnapshotTests.ToJson_FromJson_RoundTripsBodiesVerbatim` |
| TC-68-07 | Integration | Diff: entity only in snapshot / only live / changed / unchanged | Classified Added / Removed / Changed / Unchanged (count) | `SchemaDiffEngineTests.*` |
| TC-68-08 | Integration | Diff: same alias, different id | Matched by alias, `idMismatch` flagged, not double-counted as removed | `SchemaDiffEngineTests.Compare_DifferentIdsSameAlias_*` |
| TC-68-09 | Integration | Diff: two live entities share a data-type name | Ambiguous entity skipped with a note, not guessed | `SchemaDiffEngineTests.Compare_AmbiguousLiveName_IsSkipped_NotGuessed` |
| TC-68-10 | Integration | Diff: same content, different JSON property order | Reported Unchanged (order-insensitive objects) | `SchemaDiffEngineTests.Compare_SameContentDifferentPropertyOrder_IsUnchanged` |
| TC-68-11 | Integration | Apply `--dry-run` with pending changes | Plan reported; no writes sent | `SchemaApplierTests.ApplyAsync_DryRun_WritesNothing_ButPlansEverything`, `SchemaApplyCommandTests.Apply_DryRun_WritesNothing` |
| TC-68-12 | Integration | Apply orders data types → templates → doc types; compositions topologically | Writes recorded in dependency order | `SchemaApplierTests.ApplyAsync_CreatesInDependencyOrder_*`, `ApplyAsync_TopologicallyOrdersCompositions` |
| TC-68-13 | Integration | Apply without `--prune` where live has extra entities | Nothing deleted | `SchemaApplierTests.ApplyAsync_WithoutPrune_DoesNotDelete` |
| TC-68-14 | Integration | Apply `--prune --yes` with live-only entities | Extras deleted | `SchemaApplierTests.ApplyAsync_WithPrune_DeletesRemovedEntities`, `SchemaApplyCommandTests.Apply_Prune_WithYes_DeletesLiveOnlyEntity` |
| TC-68-15 | Integration | Apply `--prune` non-interactive without `--yes` | Refused (exit 2); nothing deleted | `SchemaApplyCommandTests.Apply_Prune_NonInteractiveWithoutYes_IsRefused` |
| TC-68-16 | Integration | Apply update where snapshot id ≠ live id (alias match) | Body id rewritten to live id; live entity updated | `SchemaApplierTests.ApplyAsync_UpdateRewritesBodyIdToLiveId` |
| TC-68-17 | Integration | Apply first write fails | Fail-fast: stops, reports; later writes not attempted | `SchemaApplierTests.ApplyAsync_FailFast_StopsAtFirstError` |
| TC-68-18 | Manual | Apply real create/update/delete against a live instance | Entities created/updated/deleted; re-export diffs clean | Validated manually during #68 (template create→update→delete round-trip on 17.3.5) |
| TC-68-19 | Manual | Apply under `--readonly` / `UMBRACO_READONLY=1` with pending writes | First write blocked (exit 2); nothing changed | Manual (interceptor-enforced; covered generally by `MutationInterceptorHandlerTests`) |
| TC-68-20 | Manual | Cross-environment apply where reference GUIDs differ | Referenced-GUID differences surface as changes (documented v1 limitation) | Manual |

## Notes

- TC-68-18/19/20 are Manual: they need a live instance and/or a second environment. TC-68-18's
  create/update/delete round-trip was exercised by hand against the local Umbraco 17.3.5 during
  development (template probe, self-cleaned). A future mutating live fixture could automate it.
- Cross-environment reference remapping (TC-68-20) is out of scope for the first slice; snapshots
  are most reliable when GUIDs are shared (see ADR 0004 §1 consequences).
