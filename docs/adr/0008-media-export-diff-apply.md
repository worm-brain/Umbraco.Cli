# ADR 0008: Media export / diff / apply pipeline

- Status: Accepted
- Date: 2026-09-27
- Issue: #226 (Phase 6 of the #250 plan). Builds on ADR 0006 (content export/diff/apply).

## Context

Content references media by GUID (`featuredImage[].mediaKey`, media pickers, rich text). The
content pipeline (ADR 0006) keeps document GUIDs across instances, but there was no way to do the
same for media: the test round's promotion needed a hand-written script that read each item,
downloaded its file, staged it with `POST /temporary-file`, and created it with the same id
(#226). Phase 2 added `media upload --id`; this ADR covers the pipeline itself.

Media differs from content in the one way that matters: an item holds a **binary file**.

## Decision

Mirror the content pipeline - snapshot, exporter, pure diff engine, ordered applier, shared
prelude - as `media export`, `media diff`, `media apply`. The media-specific decisions:

### 1. The snapshot is a directory

```
<dir>/media.json            { mediaVersion: "1", root, items: [ { id, parent, body, file } ] }
<dir>/files/<id>/<name>     each item's file
```

`file` is `{ path, name, bytes, sha256 }`, or absent for an item with no file (a folder). The
files cannot ride in the JSON envelope, so `media export --out|-O <dir>` is required and the
snapshot argument of `diff`/`apply` is the directory (or its `media.json`); `-` (stdin) is an
`invalid_argument`. This is the one pipeline whose `--out` is a directory - recorded in
`docs/conventions.md`.

Export refuses a non-empty directory that is not an earlier media export, and replaces an earlier
export whole (its index and files are removed first, the new index is written last), so a failed
export never leaves an index describing files that are not there.

Alternative rejected: store only metadata and download each file from the source at apply time.
It needs the source instance reachable (and authenticated) from wherever apply runs, which is the
opposite of a portable snapshot.

### 2. GUID-only identity, as for content

Items match by GUID. That is the promise content relies on; a name or path fallback could pair a
picker's GUID with a different file.

### 3. What differs per instance is not a change

`umbracoFile`'s `src` carries a per-upload folder (`/media/<random>/name.ext`), and
`umbracoBytes`/`Width`/`Height`/`Extension` are computed by the server from the file. The body
comparison leaves them out (with `isTrashed`, `flags`, and the variant dates), and keeps the crops
and focal point. The **file** is compared on its own: by name and size, from the live body, so
`diff` downloads nothing; `--verify-files` also downloads each live file the snapshot has and
compares SHA-256.

### 4. Writes

- **Create**, in snapshot pre-order: stage the file, then `POST /media` with the snapshot id, the
  parent, and `umbracoFile = { temporaryFileId, crops, focalPoint }`. A folder is a plain POST.
- **Update**: `PUT /media/{id}`. A changed file is staged and set. Otherwise the live `src` and
  file-derived values are put back, because a PUT replaces every value and would drop the file.
- Apply never moves an item; a parent-only difference is `Drifted`, reported and not applied.
- Media types must already exist on the target by GUID (`schema apply` first).

### 5. Prune trashes

`--prune` moves items the snapshot omits to the **recycle bin**, deepest first, rather than
deleting them: content that uses an item can get it back with `media restore`. It is still
declared destructive (`--yes`): until restored, the pages using those items stop showing them.
A removed item with a kept item under it is left alone, since trashing it would take the kept
item along.

### 6. Downloads stay on the configured host

Media files are served by the site, not the Management API, so downloads resolve `src` against the
configured host (the HTTP client's base address). An absolute URL on another host (a CDN) is
refused rather than fetched, because the request carries the access token.

## Consequences

- A promotion is `schema apply` -> `media apply` -> `content apply`, each keeping GUIDs.
- The files are the bulk of a media snapshot; it belongs next to, not inside, a git repository of
  schema and content snapshots unless the library is small.
- Only the `umbracoFile` property is carried as a file. A custom media type with other upload
  fields keeps their values as JSON, which point at files that are not copied.
- An item whose GUID is in the target's recycle bin diffs as `Added`, and its create fails; restore
  or empty the recycle bin first.
- Files served from another host (a CDN, blob storage with its own domain) cannot be exported yet.
