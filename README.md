<h1 align="center">Jellyfin Interest Collections</h1>

<p align="center">
  Browse a big library by what things <em>are</em>, not just by genre.<br />
  <sub>Alpha · Jellyfin 10.11.x · MIT</sub>
</p>

---

Jellyfin gives you genres. Genres are blunt: *Breaking Bad* is Crime, Drama, Thriller — and
so are two hundred other things in your library. IMDb solves this with **Interests**, a much
finer taxonomy of 313 terms across 26 categories.

This plugin brings that layer to Jellyfin. It looks up each movie and series by its provider
id, fetches its interests, normalises and filters them, writes them as **Jellyfin tags**, and
optionally maintains **one collection per interest**.

*Breaking Bad* ends up in Dark Comedy, Drug Crime, Psychological Drama, Psychological
Thriller — all at once. One title, many collections, no duplicated files.

```
Psychological Thriller      Drug Crime          Dark Comedy
├── Breaking Bad            ├── Breaking Bad    ├── Breaking Bad
├── Mindhunter              ├── Ozark           ├── Fargo
├── Se7en                   └── Narcos          └── Barry
└── Shutter Island
```

## How it works

```
Jellyfin library
   └─ Media scanner ....... movies and series, one pass, no O(n²)
      └─ Provider id ...... IMDb / TMDb / TVDB, exact ids only, never fuzzy titles
         └─ Interest provider ..... IMDb GraphQL · TMDb keywords · offline rules
            └─ Cache ............... 30 days by default, keyed by provider id
               └─ Normalizer ....... 313-interest taxonomy, canonical names and ids
                  └─ Filter ........ categories, aliases, ignore list, patterns
                     └─ Tags ....... written to Jellyfin, your own tags untouched
                        └─ Collections ... created and maintained, ownership tracked
```

## Providers

| Provider | API key | Data quality | Notes |
|---|---|---|---|
| **IMDb Interests** *(default)* | none | The real thing — genuine IMDb interests with stable ids | Read the terms note below |
| **TMDb Keywords** | free key | Good; keywords are mapped onto the taxonomy and the noisy long tail is dropped | Official, documented API |
| **Local rules** | none | Modest — only what other metadata providers already wrote | Fully offline, no licensing question |

### A note on the IMDb provider

The plugin queries `caching.graphql.imdb.com`, the same structured endpoint the IMDb website
itself uses. No HTML is scraped. IMDb attaches a notice to every response stating that
public, commercial or otherwise non-private use of the data is not permitted, and that only
limited non-commercial use is allowed.

Tagging your own self-hosted library for your own household falls inside that limit.
Redistributing the results, or running this as a public service, does not. The plugin caches
answers for 30 days and throttles requests so a full library costs one request per title,
once. If you would rather not weigh this up at all, switch to **TMDb Keywords** or **Local
rules** — the whole point of the provider abstraction is that you are not locked in.

## Install

### From the plugin repository (recommended)

1. Dashboard → **Plugins** → **Repositories** → **+**
2. Name: `Interest Collections`, URL:
   `https://raw.githubusercontent.com/elisamuel40/jellyfin-plugin-interest-collections/main/manifest.json`
3. **Catalog** → **Metadata** → *Interest Collections* → **Install**
4. Restart Jellyfin.

### Manually

Download the zip from [Releases](https://github.com/elisamuel40/jellyfin-plugin-interest-collections/releases)
and extract the `.dll` into a new folder inside the plugin directory:

| Platform | Plugin directory |
|---|---|
| Linux (package) | `/var/lib/jellyfin/plugins/` |
| Docker | `/config/plugins/` inside the container |
| Windows (service/tray) | `%ProgramData%\Jellyfin\Server\plugins\` |
| Windows (direct) | `%LocalAppData%\jellyfin\plugins\` |
| macOS | `~/.local/share/jellyfin/plugins/` |

```
plugins/
└── Interest Collections_0.1.0.0/
    └── Jellyfin.Plugin.InterestCollections.dll
```

Restart Jellyfin, then check Dashboard → Plugins shows it as **Active**.

## Configure

Dashboard → **Plugins** → **Interest Collections**.

Sensible defaults are already set; the settings worth knowing about:

**Provider** — which source to use, plus **Test connection**, which probes a known title and
tells you exactly what came back.

**Processing** — movies and series are on, **episodes are off**. Per-episode interests add a
lot of metadata noise for very little browsing value. New media is picked up automatically
after a 30-second quiet period, because Jellyfin announces items before their provider ids
have been filled in.

**Tagging** — interests are written as tags. *Lock the Tags field* stops a later metadata
refresh from discarding them, at the cost of blocking other sources from editing tags.

**Collections** — one collection per interest, with a **minimum of 3 titles**. Deleting
collections that drop below the minimum is **off** by default, because deleting is
destructive and thresholds get experimented with.

**Filters** — the important one. Filtering is by **taxonomy category**, so a single checkbox
switches off all 30 Language interests or all 70 Franchise ones. On top of that:

- *Exclude genre-level interests* (on) drops Drama, Crime, Thriller and the other 20 names
  that simply duplicate Jellyfin's genres.
- *Reject an interest named after the title itself* (on) — IMDb genuinely returns a "Breaking
  Bad" franchise interest for *Breaking Bad*.
- Ignore list, regex blocks, and aliases such as `Drug Trafficking = Drug Crime`.

**Dry run** — computes every tag and collection change and writes nothing. Run this first on
a large library.

## Tasks

Dashboard → **Scheduled Tasks** → *Interest Collections*:

| Task | What it does | Cost |
|---|---|---|
| **Scan new media for interests** | Classifies anything new or stale. Runs daily. | Cheap — cache hits |
| **Rebuild interest collections** | Re-evaluates every title against current filters. | Cheap — reuses the cache |
| **Refresh interest metadata** | Clears the cache and re-queries the provider. | One request per title |

Change a filter or an alias → *Rebuild*. Suspect the provider's data moved on → *Refresh*.

## Safety

The two things a plugin like this could plausibly ruin are your tags and your collections.

**Your tags are safe.** The plugin records exactly which tags it wrote, per item. It only
ever removes tags from that list. Tags you added by hand, or that another plugin wrote, are
copied through untouched. There is no ugly visible prefix.

**Your collections are safe.** Every collection the plugin creates is stamped with its own
provider id inside Jellyfin *and* recorded in the plugin's state file. Both signals must
agree before a collection is modified or deleted. A collection you made by hand has neither,
so it is untouchable — even if you happen to have named it exactly like an interest.

**A provider outage cannot damage anything.** A failed lookup is recorded as a failure and
retried later. It is never mistaken for "this title has no interests", which would otherwise
strip metadata the title legitimately has. The pipeline never throws into a Jellyfin library
scan.

## Caching and rate limits

Answers are cached for **30 days**, keyed by provider id — empty answers for only 3 days, so
a title that gains interests later is picked up reasonably soon. Requests are capped at **3
concurrent** with a **250 ms** minimum spacing, exponential backoff with jitter, and
`Retry-After` honoured on 429. All of it is configurable.

## Compatibility

| Jellyfin | Status |
|---|---|
| 10.11.x | Supported and tested against 10.11.11 |
| 10.10.x and older | Not supported — the plugin targets the 10.11 API and .NET 9 |
| 12.0 (RC) | Not yet — planned once it stabilises |

## Troubleshooting

**Nothing is tagged.** Check that the titles have IMDb ids (Edit metadata → Identify). Items
without a usable provider id are skipped deliberately rather than matched by title.

**"Access to the path … is denied" when creating collections.** Jellyfin needs write access
to its collections folder; this is a server permissions issue, not a plugin one. See
Jellyfin issue #16107.

**Tags disappear after a metadata refresh.** Enable *Lock the Tags field*, or just re-run the
scan task — it reapplies them.

**Collections were not created.** They need the minimum title count (3 by default). The dry
run report lists everything that fell below it.

**A few collections are missing after the first run.** Creating dozens of collections in one
burst occasionally leaves some uncommitted on the Jellyfin side. Run *Rebuild interest
collections* once; it detects the gap and recreates them.

Logs are tagged with the plugin name; set Jellyfin's log level to Debug for per-item detail.
API keys are never written to the log.

## Privacy

The IMDb and TMDb providers send **one provider id per title** to that service, and nothing
else. No filenames, no library layout, no user information. The local rules provider makes no
network requests at all. All plugin state stays in the plugin's own data folder in plain JSON.

## Development

See [DEVELOPMENT.md](DEVELOPMENT.md) for building, testing, packaging and installing from
source.

```bash
git clone https://github.com/elisamuel40/jellyfin-plugin-interest-collections.git
cd jellyfin-plugin-interest-collections
dotnet test
./scripts/package.sh 0.1.0
```

## Related plugins

[Smart Collections](https://github.com/johnpc/jellyfin-plugin-smart-collections) and
[Auto Collections](https://github.com/KeksBombe/jellyfin-plugin-auto-collections) build
collections *from tags*. If you prefer their collection behaviour, turn this plugin's
collection management off and let it act purely as the semantic tagging layer they consume.

## AI assistance

This plugin was built with substantial AI assistance — Anthropic's Claude, via
Claude Code — for design, implementation, tests and documentation. Many commits
carry a `Claude-Session` trailer linking to the session that produced them. All
changes are reviewed and tested against a real Jellyfin server by the author,
who takes responsibility for every release.

## License

MIT — see [LICENSE](LICENSE). Not affiliated with IMDb, TMDb or the Jellyfin project.
