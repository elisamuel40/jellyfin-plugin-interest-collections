# Development

## Requirements

- .NET **9.0** SDK — Jellyfin 10.11 runs on .NET 9, and the plugin must match.
- Jellyfin **10.11.x** to test against.

```bash
# macOS
brew install dotnet@9
export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@9/libexec"

# Debian / Ubuntu
sudo apt install dotnet-sdk-9.0

# Windows
winget install Microsoft.DotNet.SDK.9
```

Verify: `dotnet --list-sdks` should show a `9.0.x` entry.

## Build and test

```bash
git clone https://github.com/elisamuel40/jellyfin-plugin-interest-collections.git
cd jellyfin-plugin-interest-collections

dotnet restore
dotnet build --configuration Release
dotnet test
```

The project builds with `TreatWarningsAsErrors` and the full Jellyfin analyzer set
(StyleCop, the .NET analyzers, the multithreading analyzer). A warning fails the build on
purpose — CI would reject it anyway.

## Package

```bash
./scripts/package.sh 0.1.0
```

Produces `dist/interest-collections-0.1.0.zip` and `dist/checksum.md5`. The zip contains only
`Jellyfin.Plugin.InterestCollections.dll`; the server supplies every other assembly, which is
why the project references `Jellyfin.Controller` with `ExcludeAssets=runtime`.

## Install a development build

Extract the DLL into a folder inside your server's plugin directory and restart Jellyfin:

```bash
# Linux
sudo mkdir -p "/var/lib/jellyfin/plugins/Interest Collections_0.1.0.0"
sudo unzip -o dist/interest-collections-0.1.0.zip \
  -d "/var/lib/jellyfin/plugins/Interest Collections_0.1.0.0"
sudo systemctl restart jellyfin

# Docker
docker cp dist/interest-collections-0.1.0.zip jellyfin:/tmp/
docker exec jellyfin sh -c 'mkdir -p "/config/plugins/Interest Collections_0.1.0.0" \
  && unzip -o /tmp/interest-collections-0.1.0.zip -d "/config/plugins/Interest Collections_0.1.0.0"'
docker restart jellyfin
```

```powershell
# Windows (service or tray install)
$dir = "$env:ProgramData\Jellyfin\Server\plugins\Interest Collections_0.1.0.0"
New-Item -ItemType Directory -Force -Path $dir
Expand-Archive -Force dist\interest-collections-0.1.0.zip -DestinationPath $dir
Restart-Service JellyfinServer
```

## Logs

| Platform | Location |
|---|---|
| Linux | `/var/log/jellyfin/` or `journalctl -u jellyfin -f` |
| Docker | `docker logs -f jellyfin`, or `/config/log/` |
| Windows | `%ProgramData%\Jellyfin\Server\log\` |
| macOS | `~/.local/share/jellyfin/log/` |

Set the log level to Debug in Dashboard → Logs for per-item detail. Every message from this
plugin is emitted under its own category, so `grep InterestCollections` narrows it quickly.

## Project layout

```
src/Jellyfin.Plugin.InterestCollections/
  Plugin.cs                     entry point; holds the ownership provider key
  PluginServiceRegistrator.cs   DI registration (IPluginServiceRegistrator)
  Api/                          endpoints backing both dashboard pages
  Configuration/                settings model and the two dashboard pages
  Data/imdb-interests.json      bundled taxonomy: 313 interests, 26 categories
  Events/                       IHostedService listening to library changes
  Models/                       plain data passed between layers
  Providers/                    IInterestProvider and the three implementations
  Services/                     scanner, normalizer, filter, tag and collection sync
  Storage/                      JSON-backed cache and state
  Tasks/                        the three scheduled tasks
tests/                          xUnit, no running server required
```

## Testing approach

Tests never touch the network or a Jellyfin server:

- `StubHttpMessageHandler` replays canned provider responses, including the real IMDb payload
  shape captured from a live call.
- `ILibraryManager` and `ICollectionManager` are interfaces and can be substituted.
- Storage tests run against a temporary directory.

When adding a provider, add a test that pins its behaviour when the service is **down** —
returning "no interests" instead of a failure is the single most damaging bug this plugin
could have, because the next run would strip tags the title legitimately has.

## Refreshing the bundled taxonomy

The taxonomy is a snapshot of IMDb's interest categories. To regenerate it:

```bash
curl -s -X POST https://caching.graphql.imdb.com/ \
  -H 'Content-Type: application/json' \
  -H 'x-imdb-client-name: imdb-web-next' \
  -H 'Origin: https://www.imdb.com' \
  -d '{"query":"query{interestCategories(first:60){edges{node{id text interests(first:200){edges{node{id primaryText{text}}}}}}}}"}'
```

Reshape into `src/Jellyfin.Plugin.InterestCollections/Data/imdb-interests.json`, keeping the
`genreLevel` flag (true when an interest's name equals its category's name). Update the
counts asserted in `InterestTaxonomyTests`.

## Releasing

1. Update `CHANGELOG.md`.
2. Tag: `git tag v0.2.0 && git push origin v0.2.0`.
3. The release workflow tests, packages, records the version and MD5 in `manifest.json`,
   commits that back to `main`, and publishes the GitHub release.

Versions starting with `0.` are published as pre-releases.
