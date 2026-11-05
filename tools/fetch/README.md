# Fetching upstream data

Upstream data files are fetched by exact path at a pinned tag or commit; repositories are never cloned and source archives never downloaded. The files go to a cache outside the repository: `$ONECONTACT_UPSTREAM/<source>` (default `.upstream/<source>`, which git ignores).

Each source has

- `upstream/<source>/PIN`: the pin (source name, licence, repository, tag or commit, URL prefix, and one `file <path> <sha256>` line per file),
- `upstream/<source>/LICENSE` and `NOTICE` files as the upstream publishes them,
- `tools/fetch/<source>.sh`: a two-line wrapper, `exec "$(dirname "$0")/fetch-pin.sh" "$(dirname "$0")/../../upstream/<source>/PIN" "$@"`,
- `tools/Ingest.<Source>/`: the converter (see [docs/architecture.md](../../docs/architecture.md)).

To create a pin, write the `file` lines with the hash `0000...` (64 zeros), run the fetch script, and copy the hashes it reports from the mismatch messages after you have read the licence and checked the files are the ones you meant. The ingest tool checks every hash again before it converts anything.
