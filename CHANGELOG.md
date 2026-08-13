# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.4] — 2026-08-13

### Fixed

- Interests that duplicate one of the item's own Jellyfin genres are dropped when "Exclude
  genre-level interests" is on. The exclusion only knew the taxonomy's 23 category heads, but
  Jellyfin's genre vocabulary is wider — a war movie still got "War" and "History" tags because
  IMDb files those under Action and Drama. Reported in issue #1.

## [0.1.3] — 2026-08-10

### Fixed

- Writing tags queued the plugin's own changes for a second pass. Jellyfin raises ItemUpdated
  while the write is still running, and the event handler's loop guard reads the per-item record
  that was only written afterwards — so a 227-title run queued 213 items for a pointless repeat.
  Ownership is now recorded before the write, and reverted if the write fails.

### Known issues

- Creating many collections at once can leave a few of them uncommitted: a run that created 39
  collections in about a second ended with 33 present. The next run detects the gap and recreates
  them, so running the scan or rebuild task twice resolves it.

## [0.1.2] — 2026-08-10

### Fixed

- Items that belong to no library folder are skipped. A real 10.11.11 server returned 400 Movie
  rows for a 216-movie library — the extras were leftover database rows for media that had moved
  or been removed. Processing them doubled the provider requests and would have filled every
  collection with titles nobody can see.

## [0.1.1] — 2026-08-10

### Fixed

- The library query returns a title once per library it is reachable through, so a 227-title
  library was processed as 417 items: every title was looked up twice and every reported count
  was inflated. Items are now deduplicated by id. Found by running a dry run against a real
  10.11.11 server.

## [0.1.0] — 2026-08-10

First alpha. The full pipeline works end to end; treat it as experimental until it has run
against more libraries than the author's.

### Added

- **Interest providers** behind a common interface: IMDb's public GraphQL endpoint (the only
  source of genuine IMDb Interests, no API key), TMDb keywords mapped onto the taxonomy, and
  an offline provider that derives interests from existing genres and tags.
- **Bundled IMDb taxonomy** of 313 interests across 26 categories, with genre-level entries
  flagged, so interests get canonical names and stable ids without a network call.
- **Normalization** that collapses spelling, casing, accents and punctuation, so providers
  can disagree freely about how to write "Psychological Thriller".
- **Filtering by taxonomy category**, plus aliases, an ignore list, regex blocks, per-interest
  disables, and rejection of an interest named after the title itself.
- **Tag synchronization** that only ever removes tags the plugin itself wrote.
- **Managed collections**, one per qualifying interest, with a minimum title count and
  ownership tracked both in Jellyfin and in the plugin's own state.
- **Three scheduled tasks**: daily scan, filter rebuild, and full provider refresh.
- **Automatic processing** of new and updated library items, debounced and loop-guarded.
- **Caching** with separate lifetimes for populated and empty answers, and **rate limiting**
  with concurrency caps, spacing, exponential backoff and `Retry-After` support.
- **Dry run** mode and a dry-run report on the configuration page.
- **Configuration page** and an **Interest Manager** page listing every applied interest with
  title counts and per-interest enable/disable.

[Unreleased]: https://github.com/elisamuel40/jellyfin-plugin-interest-collections/compare/v0.1.3...HEAD
[0.1.3]: https://github.com/elisamuel40/jellyfin-plugin-interest-collections/releases/tag/v0.1.3
[0.1.2]: https://github.com/elisamuel40/jellyfin-plugin-interest-collections/releases/tag/v0.1.2
[0.1.1]: https://github.com/elisamuel40/jellyfin-plugin-interest-collections/releases/tag/v0.1.1
[0.1.0]: https://github.com/elisamuel40/jellyfin-plugin-interest-collections/releases/tag/v0.1.0
